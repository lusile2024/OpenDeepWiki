"use client";

import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { AlertTriangle, CheckCircle2, Clock3, Loader2, RefreshCw, Route, Save, WandSparkles } from "lucide-react";
import { toast } from "sonner";
import type { RepositoryWorkflowConfig } from "@/types/workflow-config";
import {
  getRepositoryWorkflowConfig,
  regenerateRepositoryWorkflows,
  saveRepositoryWorkflowConfig,
  type AdminRepositoryBranch,
} from "@/lib/admin-api";
import { RepositoryWorkflowTemplateWorkbench } from "@/components/admin/repository-workflow-template-workbench";
import { fetchProcessingLogs } from "@/lib/repository-api";
import type { ProcessingLogItem, ProcessingLogResponse } from "@/types/repository";

const WORKFLOW_REGENERATION_STORAGE_PREFIX = "odw:workflow-regeneration:";
const WORKFLOW_PROGRESS_POLL_INTERVAL_MS = 2500;
const WORKFLOW_PROGRESS_LOG_LIMIT = 200;
const WORKFLOW_PROGRESS_RECENT_LOG_LIMIT = 6;

type WorkflowRegenerationTarget = "all" | "current";
type WorkflowRunStatus = "running" | "completed" | "failed";

interface WorkflowRegenerationSession {
  repositoryId: string;
  branchId: string;
  languageCode: string;
  target: WorkflowRegenerationTarget;
  profileKey?: string;
  startedAt: string;
  status?: WorkflowRunStatus;
  completedAt?: string;
  lastMessage?: string;
}

interface WorkflowProgressRefreshOptions {
  run?: WorkflowRegenerationSession;
  autoFinalize?: boolean;
}

function getWorkflowRunStorageKey(repositoryId: string) {
  return `${WORKFLOW_REGENERATION_STORAGE_PREFIX}${repositoryId}`;
}

function getWorkflowRunKey(run: WorkflowRegenerationSession) {
  return `${run.repositoryId}:${run.branchId}:${run.languageCode}:${run.target}:${run.profileKey ?? "all"}:${run.startedAt}`;
}

function isAbortError(error: unknown) {
  return error instanceof DOMException
    ? error.name === "AbortError"
    : error instanceof Error && error.name === "AbortError";
}

function getWorkflowCompletionState(logs: ProcessingLogItem[]) {
  const failureLog = [...logs].reverse().find((log) => log.message.includes("业务流程重建失败"));
  if (failureLog) {
    return {
      status: "failed" as const,
      message: failureLog.message,
    };
  }

  const completionLog = [...logs].reverse().find((log) => {
    if (log.stepName !== "Complete") return false;
    return (
      log.message.includes("业务流程重建完成") ||
      log.message.includes("全部业务流程重建完成") ||
      log.message.includes("当前业务流程重建完成")
    );
  });

  if (completionLog) {
    return {
      status: "completed" as const,
      message: completionLog.message,
    };
  }

  return null;
}

interface RepositoryWorkflowPanelProps {
  repositoryId: string;
  repositoryOwner?: string | null;
  repositoryName?: string | null;
  branches: AdminRepositoryBranch[];
  selectedBranchId: string;
  selectedLanguage: string;
  onBranchChange: (branchId: string) => void;
  onLanguageChange: (languageCode: string) => void;
}

export function RepositoryWorkflowPanel({
  repositoryId,
  repositoryOwner,
  repositoryName,
  branches,
  selectedBranchId,
  selectedLanguage,
  onBranchChange,
  onLanguageChange,
}: RepositoryWorkflowPanelProps) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [config, setConfig] = useState<RepositoryWorkflowConfig | null>(null);
  const [rawJson, setRawJson] = useState("");
  const [selectedProfileKey, setSelectedProfileKey] = useState("");
  const [workflowRun, setWorkflowRun] = useState<WorkflowRegenerationSession | null>(null);
  const [workflowProgress, setWorkflowProgress] = useState<ProcessingLogResponse | null>(null);
  const [workflowLogs, setWorkflowLogs] = useState<ProcessingLogItem[]>([]);
  const [workflowLastUpdated, setWorkflowLastUpdated] = useState<string | null>(null);
  const workflowRunRef = useRef<WorkflowRegenerationSession | null>(null);
  const finalizedWorkflowRunKeysRef = useRef<Set<string>>(new Set());

  const profiles = useMemo(() => config?.profiles ?? [], [config]);
  const selectedProfile = useMemo(() => {
    if (!config) return null;
    const key = selectedProfileKey || config.activeProfileKey || profiles[0]?.key;
    return profiles.find((profile) => profile.key === key) ?? profiles[0] ?? null;
  }, [config, profiles, selectedProfileKey]);
  const selectedBranch = useMemo(
    () => branches.find((branch) => branch.id === selectedBranchId) ?? null,
    [branches, selectedBranchId]
  );
  const selectedLanguageInfo = useMemo(
    () => selectedBranch?.languages.find((item) => item.languageCode === selectedLanguage) ?? null,
    [selectedBranch, selectedLanguage]
  );
  const workflowRunBranch = useMemo(
    () => (workflowRun ? branches.find((branch) => branch.id === workflowRun.branchId) ?? null : null),
    [branches, workflowRun]
  );
  const workflowRunLanguage = workflowRun?.languageCode || selectedLanguageInfo?.languageCode || null;
  const savedJson = useMemo(() => (config ? JSON.stringify(config, null, 2) : ""), [config]);
  const isDirty = useMemo(() => rawJson.trim() !== savedJson.trim(), [rawJson, savedJson]);
  const workflowRunStatus = workflowRun?.status ?? "running";
  const regeneratingTarget = workflowRunStatus === "running" ? workflowRun?.target ?? null : null;
  const workflowStorageKey = useMemo(
    () => (repositoryId ? getWorkflowRunStorageKey(repositoryId) : null),
    [repositoryId]
  );
  const workflowProgressRatio = useMemo(() => {
    const total = workflowProgress?.totalDocuments ?? 0;
    const completed = workflowProgress?.completedDocuments ?? 0;
    if (total <= 0) {
      return 0;
    }
    return Math.min(100, Math.round((completed / total) * 100));
  }, [workflowProgress]);
  const workflowRecentLogs = useMemo(
    () => workflowLogs.slice(-WORKFLOW_PROGRESS_RECENT_LOG_LIMIT).reverse(),
    [workflowLogs]
  );
  const workflowStatusBadge = useMemo(() => {
    if (!workflowRun) return null;
    return workflowRun.target === "current"
      ? `当前业务流${workflowRun.profileKey ? ` · ${workflowRun.profileKey}` : ""}`
      : "全部业务流";
  }, [workflowRun]);
  const workflowStatusText = useMemo(() => {
    switch (workflowRunStatus) {
      case "completed":
        return "已完成";
      case "failed":
        return "失败";
      default:
        return "执行中";
    }
  }, [workflowRunStatus]);
  const applyConfigState = (nextConfig: RepositoryWorkflowConfig) => {
    setConfig(nextConfig);
    setRawJson(JSON.stringify(nextConfig, null, 2));
    setSelectedProfileKey(nextConfig.activeProfileKey || nextConfig.profiles?.[0]?.key || "");
  };

  useEffect(() => {
    workflowRunRef.current = workflowRun;
  }, [workflowRun]);

  const persistWorkflowRun = useCallback(
    (nextRun: WorkflowRegenerationSession | null) => {
      if (typeof window === "undefined" || !workflowStorageKey) return;
      if (nextRun) {
        window.sessionStorage.setItem(workflowStorageKey, JSON.stringify(nextRun));
      } else {
        window.sessionStorage.removeItem(workflowStorageKey);
      }
    },
    [workflowStorageKey]
  );

  const clearWorkflowRun = useCallback(() => {
    setWorkflowRun(null);
    setWorkflowProgress(null);
    setWorkflowLogs([]);
    setWorkflowLastUpdated(null);
    persistWorkflowRun(null);
  }, [persistWorkflowRun]);

  const updateWorkflowRun = useCallback(
    (updater: (current: WorkflowRegenerationSession) => WorkflowRegenerationSession) => {
      setWorkflowRun((current) => {
        if (!current) return current;
        const next = updater(current);
        persistWorkflowRun(next);
        return next;
      });
    },
    [persistWorkflowRun]
  );

  const finalizeWorkflowRun = useCallback(
    (
      run: WorkflowRegenerationSession,
      status: "completed" | "failed",
      message: string,
      options?: { showToast?: boolean }
    ) => {
      const runKey = getWorkflowRunKey(run);
      if (finalizedWorkflowRunKeysRef.current.has(runKey)) {
        return;
      }

      finalizedWorkflowRunKeysRef.current.add(runKey);
      updateWorkflowRun((current) =>
        getWorkflowRunKey(current) === runKey
          ? {
              ...current,
              status,
              completedAt: new Date().toISOString(),
              lastMessage: message,
            }
          : current
      );

      if (options?.showToast === false) {
        return;
      }

      if (status === "completed") {
        toast.success(message);
      } else {
        toast.error(message);
      }
    },
    [updateWorkflowRun]
  );

  const startWorkflowRun = useCallback(
    (nextRun: WorkflowRegenerationSession) => {
      setWorkflowProgress(null);
      setWorkflowLogs([]);
      setWorkflowLastUpdated(null);
      const normalizedRun = { ...nextRun, status: "running" as const, completedAt: undefined, lastMessage: undefined };
      setWorkflowRun(normalizedRun);
      persistWorkflowRun(normalizedRun);
    },
    [persistWorkflowRun]
  );

  const refreshWorkflowProgress = useCallback(async (options?: WorkflowProgressRefreshOptions) => {
    const currentRun = options?.run ?? workflowRunRef.current;
    if (!currentRun || !repositoryOwner || !repositoryName) {
      return;
    }

    const runKey = getWorkflowRunKey(currentRun);
    const response = await fetchProcessingLogs(
      repositoryOwner,
      repositoryName,
      new Date(currentRun.startedAt),
      WORKFLOW_PROGRESS_LOG_LIMIT
    );

    if (!workflowRunRef.current || getWorkflowRunKey(workflowRunRef.current) !== runKey) {
      return;
    }

    setWorkflowProgress(response);
    setWorkflowLogs(response.logs);
    setWorkflowLastUpdated(new Date().toISOString());

    const completion = getWorkflowCompletionState(response.logs);
    if (completion && options?.autoFinalize !== false) {
      finalizeWorkflowRun(currentRun, completion.status, completion.message);
    }
  }, [finalizeWorkflowRun, repositoryName, repositoryOwner]);

  useEffect(() => {
    if (!repositoryId) return;

    const load = async () => {
      setLoading(true);
      try {
        const cfg = await getRepositoryWorkflowConfig(repositoryId);
        setConfig(cfg);
        setRawJson(JSON.stringify(cfg, null, 2));
        setSelectedProfileKey(cfg.activeProfileKey || cfg.profiles?.[0]?.key || "");
      } catch (error) {
        console.error(error);
        toast.error("加载 Workflow 配置失败");
      } finally {
        setLoading(false);
      }
    };

    load();
  }, [repositoryId]);

  useEffect(() => {
    if (typeof window === "undefined" || !workflowStorageKey) return;

    const raw = window.sessionStorage.getItem(workflowStorageKey);
    if (!raw) {
      setWorkflowRun(null);
      return;
    }

    try {
      const parsed = JSON.parse(raw) as WorkflowRegenerationSession;
      if (!parsed?.repositoryId || parsed.repositoryId !== repositoryId) {
        window.sessionStorage.removeItem(workflowStorageKey);
        setWorkflowRun(null);
        return;
      }
      setWorkflowRun({
        ...parsed,
        status: parsed.status ?? "running",
      });
    } catch {
      window.sessionStorage.removeItem(workflowStorageKey);
      setWorkflowRun(null);
    }
  }, [repositoryId, workflowStorageKey]);

  useEffect(() => {
    if (!workflowRun || workflowRunStatus !== "running" || !repositoryOwner || !repositoryName) return;

    let disposed = false;

    const loadProgress = async () => {
      try {
        await refreshWorkflowProgress();
      } catch (error) {
        if (!disposed) {
          console.error(error);
        }
      }
    };

    void loadProgress();
    const timer = window.setInterval(() => {
      void loadProgress();
    }, WORKFLOW_PROGRESS_POLL_INTERVAL_MS);

    return () => {
      disposed = true;
      window.clearInterval(timer);
    };
  }, [refreshWorkflowProgress, repositoryName, repositoryOwner, workflowRun, workflowRunStatus]);

  const parseJson = (): RepositoryWorkflowConfig | null => {
    try {
      return JSON.parse(rawJson);
    } catch {
      toast.error("JSON 解析失败，请检查格式");
      return null;
    }
  };

  const handleSave = async () => {
    if (!repositoryId) return;
    const parsed = parseJson();
    if (!parsed) return;

    setSaving(true);
    try {
      const saved = await saveRepositoryWorkflowConfig(repositoryId, parsed);
      applyConfigState(saved);
      toast.success("Workflow 配置已保存");
    } catch (error: unknown) {
      console.error(error);
      toast.error(getErrorMessage(error) || "保存失败");
    } finally {
      setSaving(false);
    }
  };

  const handleRegenerateWorkflows = async (profileKey?: string) => {
    const normalizedProfileKey = profileKey?.trim() || undefined;

    if (!repositoryId) return false;
    if (!repositoryOwner || !repositoryName) {
      toast.error("当前仓库标识不完整，无法查询重建进度");
      return false;
    }
    if (!selectedBranchId || !selectedLanguage) {
      toast.error("请先选择分支和语言");
      return false;
    }
    if (isDirty) {
      toast.error("当前 Workflow 配置尚未保存，请先保存再执行已选正式业务流重建");
      return false;
    }
    if (profileKey && !normalizedProfileKey) {
      toast.error("当前没有可用于重建的业务流 profile");
      return false;
    }

    const run: WorkflowRegenerationSession = {
      repositoryId,
      branchId: selectedBranchId,
      languageCode: selectedLanguage,
      target: normalizedProfileKey ? "current" : "all",
      profileKey: normalizedProfileKey,
      startedAt: new Date().toISOString(),
    };

    startWorkflowRun(run);

    try {
      const result = await regenerateRepositoryWorkflows(repositoryId, {
        branchId: selectedBranchId,
        languageCode: selectedLanguage,
        profileKey: normalizedProfileKey,
      });
      await refreshWorkflowProgress({ run, autoFinalize: false });

      if (result.success) {
        finalizeWorkflowRun(
          run,
          "completed",
          result.message ||
            (normalizedProfileKey ? "已选正式业务流程重建已完成" : "全部业务流程重建已完成")
        );
        return true;
      }
      finalizeWorkflowRun(
        run,
        "failed",
        result.message ||
          (normalizedProfileKey ? "已选正式业务流程重建失败" : "全部业务流程重建失败")
      );
      return false;
    } catch (error: unknown) {
      if (isAbortError(error)) {
        return false;
      }

      console.error(error);
      await refreshWorkflowProgress({ run, autoFinalize: false }).catch((refreshError) => {
        console.error(refreshError);
      });
      finalizeWorkflowRun(
        run,
        "failed",
        getErrorMessage(error) ||
          (normalizedProfileKey ? "已选正式业务流程重建失败" : "全部业务流程重建失败")
      );
      return false;
    }
  };

  const handleApplyWmsTemplate = () => {
    const template = createWmsWorkflowTemplateConfig();
    applyConfigState(template);
    toast.success("已填入 WMS/WCS 模板，可继续按仓库结构微调");
  };

  return (
    <Card className="min-w-0 space-y-4 overflow-hidden p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="font-semibold">Workflow 规则</h3>
          <p className="text-xs text-muted-foreground">
            用仓库级 profile 约束 workflow 发现边界。适合 WMS/WCS 这类流程跨模块、但入口目录相对固定的项目。
          </p>
        </div>
      </div>

      {workflowRun ? (
        <Card className="border-dashed p-3">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="space-y-2">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant="secondary">{workflowStatusBadge}</Badge>
                <Badge variant="outline">
                  {workflowRunBranch?.name || workflowRun.branchId} · {workflowRunLanguage || workflowRun.languageCode}
                </Badge>
                <Badge variant="outline">
                  <Clock3 className="mr-1 h-3.5 w-3.5" />
                  {workflowRunStatus === "running" ? workflowProgress?.currentStepName || "Workspace" : workflowStatusText}
                </Badge>
              </div>
              <div className="text-sm text-muted-foreground">
                {workflowProgress?.totalDocuments ? (
                  <span>
                    业务流进度：{workflowProgress.completedDocuments} / {workflowProgress.totalDocuments}
                  </span>
                ) : workflowRun.lastMessage ? (
                  <span>{workflowRun.lastMessage}</span>
                ) : (
                  <span>正在准备或收集业务流日志…</span>
                )}
              </div>
              <div className="h-2 w-full rounded-full bg-muted">
                <div
                  className="h-full rounded-full bg-primary transition-all duration-300"
                  style={{ width: `${workflowProgressRatio}%` }}
                />
              </div>
              <div className="grid gap-1 text-xs text-muted-foreground">
                <div>开始时间：{new Date(workflowRun.startedAt).toLocaleString()}</div>
                <div>最近刷新：{workflowLastUpdated ? new Date(workflowLastUpdated).toLocaleString() : "等待首次轮询"}</div>
                {workflowRun.completedAt ? <div>结束时间：{new Date(workflowRun.completedAt).toLocaleString()}</div> : null}
              </div>
            </div>
            <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => void refreshWorkflowProgress()}
              disabled={!repositoryOwner || !repositoryName}
            >
              <RefreshCw className="mr-2 h-4 w-4" />
              刷新进度
            </Button>
              <Button variant="ghost" size="sm" onClick={clearWorkflowRun}>
                关闭
              </Button>
            </div>
          </div>

          <div className="mt-3 rounded-md border bg-muted/30 p-3">
            <div className="mb-2 flex items-center gap-2 text-xs font-medium text-muted-foreground">
              {workflowRunStatus === "running" ? (
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
              ) : workflowRunStatus === "completed" ? (
                <CheckCircle2 className="h-3.5 w-3.5 text-emerald-600" />
              ) : (
                <AlertTriangle className="h-3.5 w-3.5 text-destructive" />
              )}
              最近日志
            </div>
            {workflowRecentLogs.length > 0 ? (
              <div className="space-y-2">
                {workflowRecentLogs.map((log) => {
                  const isFailure = log.message.includes("失败");
                  const isSuccess = log.stepName === "Complete";
                  return (
                    <div key={log.id} className="flex items-start gap-2 text-xs">
                      {isFailure ? (
                        <AlertTriangle className="mt-0.5 h-3.5 w-3.5 text-destructive" />
                      ) : isSuccess ? (
                        <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 text-emerald-600" />
                      ) : (
                        <Loader2 className="mt-0.5 h-3.5 w-3.5 animate-spin text-muted-foreground" />
                      )}
                      <div className="min-w-0 flex-1">
                        <div>{log.message}</div>
                        <div className="text-[11px] text-muted-foreground">
                          {new Date(log.createdAt).toLocaleString()}
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : (
              <div className="text-xs text-muted-foreground">当前 run 还没有新日志，通常是工作区仍在准备中。</div>
            )}
          </div>
        </Card>
      ) : null}

      {loading ? (
        <div className="flex items-center justify-center py-10">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : (
        <Tabs defaultValue="config" className="min-w-0 w-full space-y-4">
          <TabsList className="grid w-full grid-cols-2 sm:w-auto">
            <TabsTrigger value="config">配置编辑</TabsTrigger>
            <TabsTrigger value="workbench">AI 多轮工作台</TabsTrigger>
          </TabsList>

          <TabsContent value="config" className="min-w-0 space-y-4 overflow-hidden">
            <div className="flex flex-wrap items-center justify-end gap-2">
              <Button variant="outline" onClick={handleApplyWmsTemplate} disabled={loading || saving}>
                <WandSparkles className="mr-2 h-4 w-4" />
                填入 WMS/WCS 模板
              </Button>
              <Button
                variant="outline"
                onClick={() => handleRegenerateWorkflows(selectedProfile?.key)}
                disabled={
                  loading ||
                  saving ||
                  regeneratingTarget !== null ||
                  !selectedBranchId ||
                  !selectedLanguage ||
                  !selectedProfile?.key
                }
              >
                {regeneratingTarget === "current" ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Route className="mr-2 h-4 w-4" />}
                重建已选正式业务流
              </Button>
              <Button
                variant="outline"
                onClick={() => handleRegenerateWorkflows()}
                disabled={loading || saving || regeneratingTarget !== null || !selectedBranchId || !selectedLanguage}
              >
                {regeneratingTarget === "all" ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Route className="mr-2 h-4 w-4" />}
                重建全部业务流
              </Button>
              <Button onClick={handleSave} disabled={loading || saving}>
                {saving ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}
                保存配置
              </Button>
            </div>

            <div className="grid gap-3 md:grid-cols-2">
              <Card className="p-3">
                <div className="mb-2 flex items-center justify-between">
                  <p className="text-xs text-muted-foreground">已选正式 Profile</p>
                  {selectedProfile?.key ? <Badge variant="outline">{selectedProfile.key}</Badge> : null}
                </div>
                <Select
                  value={selectedProfileKey || selectedProfile?.key || ""}
                  onValueChange={(value) => setSelectedProfileKey(value)}
                  disabled={profiles.length <= 1}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="选择 Profile" />
                  </SelectTrigger>
                  <SelectContent>
                    {profiles.map((profile) => (
                      <SelectItem key={profile.key} value={profile.key}>
                        {profile.name} ({profile.key})
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {selectedProfile ? (
                  <div className="mt-3 space-y-1 text-xs text-muted-foreground">
                    <div>模式：{selectedProfile.mode}</div>
                    <div>业务说明：{selectedProfile.description || "(未配置)"}</div>
                    <div>锚点名称：{selectedProfile.anchorNames.join(", ") || "(未配置)"}</div>
                    <div>锚点目录：{selectedProfile.anchorDirectories.join(", ") || "(未配置)"}</div>
                    <div>主入口名称：{selectedProfile.primaryTriggerNames.join(", ") || "(未配置)"}</div>
                    <div>主入口目录：{selectedProfile.primaryTriggerDirectories.join(", ") || "(未配置)"}</div>
                    <div>补偿入口名称：{selectedProfile.compensationTriggerNames.join(", ") || "(未配置)"}</div>
                    <div>补偿入口目录：{selectedProfile.compensationTriggerDirectories.join(", ") || "(未配置)"}</div>
                    <div>调度名称：{selectedProfile.schedulerNames.join(", ") || "(未配置)"}</div>
                    <div>调度目录：{selectedProfile.schedulerDirectories.join(", ") || "(未配置)"}</div>
                  </div>
                ) : (
                  <div className="mt-3 text-sm text-muted-foreground">当前尚未配置 workflow profile。</div>
                )}
              </Card>

              <Card className="p-3">
                <p className="mb-2 text-xs text-muted-foreground">执行范围</p>
                <div className="grid gap-2 md:grid-cols-2">
                  <div>
                    <p className="mb-1 text-xs text-muted-foreground">分支</p>
                    <Select
                      value={selectedBranchId || ""}
                      onValueChange={onBranchChange}
                      disabled={regeneratingTarget !== null}
                    >
                      <SelectTrigger>
                        <SelectValue placeholder="选择分支" />
                      </SelectTrigger>
                      <SelectContent>
                        {branches.map((branch) => (
                          <SelectItem key={branch.id} value={branch.id}>
                            {branch.name}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  <div>
                    <p className="mb-1 text-xs text-muted-foreground">语言</p>
                    <Select
                      value={selectedLanguage || ""}
                      onValueChange={onLanguageChange}
                      disabled={regeneratingTarget !== null || !selectedBranch || selectedBranch.languages.length === 0}
                    >
                      <SelectTrigger>
                        <SelectValue placeholder="选择语言" />
                      </SelectTrigger>
                      <SelectContent>
                        {(selectedBranch?.languages ?? []).map((language) => (
                          <SelectItem key={language.id} value={language.languageCode}>
                            {language.languageCode}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                </div>
                <div className="mt-3 space-y-1 text-xs text-muted-foreground">
                  <div>当前目标分支：{selectedBranch?.name || "(未选择)"}</div>
                  <div>当前目标语言：{selectedLanguageInfo?.languageCode || "(未选择)"}</div>
                </div>
              </Card>
            </div>

            <Card className="p-3">
              <p className="mb-2 text-xs text-muted-foreground">建议用法</p>
              <div className="space-y-2 text-xs text-muted-foreground">
                <div>1. 规则已比较稳定时，用本页 JSON 直接改正式 profile。</div>
                <div>2. 业务流边界不清、名称不准、入口混杂时，切到“AI 多轮工作台”。</div>
                <div>3. 在工作台里多轮收敛后，采用某个版本到正式配置。</div>
                <div>4. 若只想刷新当前选中的正式 profile，请点击“重建已选正式业务流”。</div>
                <div>5. 若要整体刷新该仓库所有业务流程，请点击“重建全部业务流”。</div>
              </div>
            </Card>

            <Card className="p-3">
              <p className="mb-2 text-xs text-muted-foreground">Workflow 配置（JSON）</p>
              <textarea
                value={rawJson}
                onChange={(event) => setRawJson(event.target.value)}
                className="h-[420px] w-full resize-none rounded-md border bg-background p-3 font-mono text-xs leading-5 outline-none focus-visible:ring-2 focus-visible:ring-ring"
              />
              <p className="mt-2 text-xs text-muted-foreground">
                当前支持 `description`、`anchorNames`、`source`、`documentPreferences` 等增强字段。
                `WcsRequestExecutor` 模式下可用目录和名称双重约束，精确锁定单业务流。
              </p>
              {isDirty ? (
                <p className="mt-2 text-xs text-amber-600">
                  当前 JSON 尚未保存。已选正式业务流重建和工作台采用都以数据库里的已保存配置为准。
                </p>
              ) : null}
            </Card>
          </TabsContent>

          <TabsContent value="workbench" className="min-w-0 space-y-4 overflow-hidden">
            <RepositoryWorkflowTemplateWorkbench
              repositoryId={repositoryId}
              selectedBranchId={selectedBranchId}
              selectedLanguage={selectedLanguage}
              isFormalConfigDirty={isDirty}
              formalConfigLoading={loading}
              formalConfigSaving={saving}
              workflowRegenerating={regeneratingTarget !== null}
              canRegenerateCurrentWorkflow={Boolean(selectedProfile?.key)}
              formalSelectedProfileKey={selectedProfile?.key ?? null}
              formalSelectedProfileName={selectedProfile?.name ?? null}
              onApplyWmsTemplate={handleApplyWmsTemplate}
              onSaveFormalConfig={handleSave}
              onRegenerateCurrentWorkflow={() => handleRegenerateWorkflows(selectedProfile?.key)}
              onRegenerateSpecificWorkflow={(profileKey) => handleRegenerateWorkflows(profileKey)}
              onRegenerateAllWorkflows={() => handleRegenerateWorkflows()}
              onConfigAdopted={applyConfigState}
            />
          </TabsContent>
        </Tabs>
      )}
    </Card>
  );
}

function createWmsWorkflowTemplateConfig(): RepositoryWorkflowConfig {
  return {
    version: 1,
    activeProfileKey: "wms-wcs-request-flow",
    profiles: [
      {
        key: "wms-wcs-request-flow",
        name: "WMS/WCS 请求执行流程",
        description: "WCS 请求先落请求表，再由定时任务扫描并分发到对应 executor 执行。",
        enabled: true,
        mode: "WcsRequestExecutor",
        anchorDirectories: ["src/Cimc.Tianda.Wms.Application/Wcs/WcsRequestExecutors"],
        anchorNames: [],
        primaryTriggerDirectories: ["src/Cimc.Tianda.Wms.WebApi/Controllers/Wcs"],
        compensationTriggerDirectories: ["src/Cimc.Tianda.Wms.WebApi/Controllers/SystemLog"],
        schedulerDirectories: ["src/Cimc.Tianda.Wms.Jobs/Wcs"],
        serviceDirectories: ["src/Cimc.Tianda.Wms.Application/Wcs"],
        repositoryDirectories: ["src/Cimc.Tianda.Wms.Domain/Repositories/Wcs"],
        primaryTriggerNames: ["WmsJobInterfaceController"],
        compensationTriggerNames: ["LogExternalInterfaceController"],
        schedulerNames: ["WcsRequestWmsExecutorJob"],
        requestEntityNames: ["WcsRequest"],
        requestServiceNames: ["IWcsRequestService", "WcsRequestService"],
        requestRepositoryNames: ["IWcsRequestRepository", "WcsRequestRepository"],
        source: {
          type: "manual-template",
        },
        documentPreferences: {
          writingHint: "重点写清楚主入口、请求落库、定时调度、executor 执行和补偿入口分离。",
          preferredTerms: ["主入口", "补偿入口", "请求落库", "定时调度"],
          requiredSections: ["端到端时序", "状态流转"],
          avoidPrimaryTriggerNames: ["LogExternalInterfaceController"],
        },
      },
    ],
  };
}

function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : undefined;
}
