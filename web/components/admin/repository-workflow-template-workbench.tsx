"use client";

import React, { useEffect, useMemo, useState } from "react";
import { Accordion, AccordionContent, AccordionItem, AccordionTrigger } from "@/components/ui/accordion";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";
import {
  adoptRepositoryWorkflowTemplateVersion,
  augmentRepositoryWorkflowTemplateDraft,
  createRepositoryWorkflowAnalysisSession,
  createRepositoryWorkflowTemplateSession,
  getRepositoryWorkflowAnalysisSession,
  getRepositoryWorkflowAnalysisSessionLogs,
  getRepositoryWorkflowAnalysisSessions,
  getRepositoryWorkflowTemplateSession,
  getRepositoryWorkflowTemplateSessions,
  rollbackRepositoryWorkflowTemplateVersion,
  sendRepositoryWorkflowTemplateMessage,
} from "@/lib/admin-api";
import type {
  RepositoryWorkflowConfig,
  WorkflowAnalysisArtifact,
  WorkflowAnalysisLog,
  WorkflowAnalysisSessionDetail,
  WorkflowAnalysisSessionSummary,
  WorkflowLspAugmentResult,
  WorkflowTemplateDiscoveryCandidate,
  WorkflowTemplateDraftVersion,
  WorkflowTemplateSessionDetail,
  WorkflowTemplateSessionSummary,
} from "@/types/workflow-config";
import { WorkflowAnalysisPanel } from "@/components/admin/workflow-analysis-panel";
import {
  Bot,
  CheckCheck,
  History,
  Loader2,
  MessageSquarePlus,
  RefreshCw,
  Route,
  RotateCcw,
  Save,
  SendHorizontal,
  Sparkles,
  User,
  WandSparkles,
} from "lucide-react";
import { toast } from "sonner";

interface RepositoryWorkflowTemplateWorkbenchProps {
  repositoryId: string;
  selectedBranchId: string;
  selectedLanguage: string;
  isFormalConfigDirty: boolean;
  formalConfigLoading: boolean;
  formalConfigSaving: boolean;
  workflowRegenerating: boolean;
  canRegenerateCurrentWorkflow: boolean;
  formalSelectedProfileKey?: string | null;
  formalSelectedProfileName?: string | null;
  onApplyWmsTemplate: () => void;
  onSaveFormalConfig: () => Promise<void>;
  onRegenerateCurrentWorkflow: () => Promise<boolean>;
  onRegenerateSpecificWorkflow: (profileKey: string) => Promise<boolean>;
  onRegenerateAllWorkflows: () => Promise<boolean>;
  onConfigAdopted: (config: RepositoryWorkflowConfig) => void;
}

export function RepositoryWorkflowTemplateWorkbench({
  repositoryId,
  selectedBranchId,
  selectedLanguage,
  isFormalConfigDirty,
  formalConfigLoading,
  formalConfigSaving,
  workflowRegenerating,
  canRegenerateCurrentWorkflow,
  formalSelectedProfileKey,
  formalSelectedProfileName,
  onApplyWmsTemplate,
  onSaveFormalConfig,
  onRegenerateCurrentWorkflow,
  onRegenerateSpecificWorkflow,
  onRegenerateAllWorkflows,
  onConfigAdopted,
}: RepositoryWorkflowTemplateWorkbenchProps) {
  const [sessions, setSessions] = useState<WorkflowTemplateSessionSummary[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState("");
  const [sessionDetail, setSessionDetail] = useState<WorkflowTemplateSessionDetail | null>(null);
  const [loadingSessions, setLoadingSessions] = useState(true);
  const [loadingSession, setLoadingSession] = useState(false);
  const [creatingSession, setCreatingSession] = useState(false);
  const [refreshingSessions, setRefreshingSessions] = useState(false);
  const [sendingMessage, setSendingMessage] = useState(false);
  const [adoptingVersionNumber, setAdoptingVersionNumber] = useState<number | null>(null);
  const [rollingBackVersionNumber, setRollingBackVersionNumber] = useState<number | null>(null);
  const [startingCandidateKey, setStartingCandidateKey] = useState<string | null>(null);
  const [adoptingAndRegenerating, setAdoptingAndRegenerating] = useState(false);
  const [adoptingAndRegeneratingAll, setAdoptingAndRegeneratingAll] = useState(false);
  const [augmentingDraft, setAugmentingDraft] = useState(false);
  const [creatingAnalysisSession, setCreatingAnalysisSession] = useState(false);
  const [loadingAnalysisSession, setLoadingAnalysisSession] = useState(false);
  const [analysisSessions, setAnalysisSessions] = useState<WorkflowAnalysisSessionSummary[]>([]);
  const [selectedAnalysisSessionId, setSelectedAnalysisSessionId] = useState("");
  const [analysisScope, setAnalysisScope] = useState<"workflow" | "chapter">("workflow");
  const [analysisSessionDetail, setAnalysisSessionDetail] = useState<WorkflowAnalysisSessionDetail | null>(null);
  const [selectedAnalysisChapterKey, setSelectedAnalysisChapterKey] = useState<string>("");
  const [lastAugmentResult, setLastAugmentResult] = useState<WorkflowLspAugmentResult | null>(null);
  const [isAugmentDialogOpen, setIsAugmentDialogOpen] = useState(false);
  const [isAnalysisDialogOpen, setIsAnalysisDialogOpen] = useState(false);
  const [isSessionWorkspaceDialogOpen, setIsSessionWorkspaceDialogOpen] = useState(false);
  const [draftMessage, setDraftMessage] = useState("");

  const currentDraft = sessionDetail?.currentDraft ?? null;
  const currentVersionNumber = sessionDetail?.currentVersionNumber ?? null;
  const currentDraftJson = useMemo(
    () => (currentDraft ? JSON.stringify(currentDraft, null, 2) : ""),
    [currentDraft]
  );
  const selectedAnalysisSummary = useMemo(
    () =>
      analysisSessions.find((item) => item.analysisSessionId === selectedAnalysisSessionId) ?? null,
    [analysisSessions, selectedAnalysisSessionId]
  );
  const activeAnalysisStatus = analysisSessionDetail?.status ?? selectedAnalysisSummary?.status ?? null;
  const analysisTaskTitleById = useMemo(
    () => new Map((analysisSessionDetail?.tasks ?? []).map((task) => [task.id, task.title])),
    [analysisSessionDetail?.tasks]
  );
  const currentAnalysisTaskTitle = analysisSessionDetail?.currentTaskId
    ? analysisTaskTitleById.get(analysisSessionDetail.currentTaskId)
    : null;
  const recentAnalysisLogs = useMemo(
    () => [...(analysisSessionDetail?.recentLogs ?? [])].slice(-5).reverse(),
    [analysisSessionDetail?.recentLogs]
  );
  const recentAnalysisArtifactDiffs = useMemo(
    () => buildRecentAnalysisArtifactDiffs(analysisSessionDetail?.artifacts ?? [], analysisTaskTitleById),
    [analysisSessionDetail?.artifacts, analysisTaskTitleById]
  );
  const workbenchBusy =
    creatingSession ||
    sendingMessage ||
    adoptingAndRegenerating ||
    adoptingAndRegeneratingAll ||
    augmentingDraft ||
    creatingAnalysisSession ||
    startingCandidateKey !== null ||
    loadingSession ||
    loadingAnalysisSession;

  useEffect(() => {
    if (!repositoryId) {
      setSessions([]);
      setSelectedSessionId("");
      setSessionDetail(null);
      setLoadingSessions(false);
      return;
    }

    let cancelled = false;

    const loadSessions = async () => {
      setLoadingSessions(true);
      try {
        const items = await getRepositoryWorkflowTemplateSessions(repositoryId);
        if (cancelled) return;

        setSessions(items);
        setSelectedSessionId((current) => {
          if (current && items.some((item) => item.sessionId === current)) {
            return current;
          }

          return items[0]?.sessionId ?? "";
        });
        if (items.length === 0) {
          setSessionDetail(null);
        }
      } catch (error) {
        if (cancelled) return;
        console.error(error);
        toast.error(getErrorMessage(error) || "加载工作台会话失败");
        setSessions([]);
        setSelectedSessionId("");
        setSessionDetail(null);
      } finally {
        if (!cancelled) {
          setLoadingSessions(false);
        }
      }
    };

    void loadSessions();

    return () => {
      cancelled = true;
    };
  }, [repositoryId]);

  useEffect(() => {
    if (!repositoryId || !selectedSessionId) {
      setSessionDetail(null);
      setLoadingSession(false);
      return;
    }

    let cancelled = false;

    const loadSession = async () => {
      setLoadingSession(true);
      try {
        const detail = await getRepositoryWorkflowTemplateSession(repositoryId, selectedSessionId);
        if (cancelled) return;

        setSessionDetail(detail);
        setSessions((current) => mergeSessionSummary(current, detail));
      } catch (error) {
        if (cancelled) return;
        console.error(error);
        toast.error(getErrorMessage(error) || "加载会话详情失败");
      } finally {
        if (!cancelled) {
          setLoadingSession(false);
        }
      }
    };

    void loadSession();

    return () => {
      cancelled = true;
    };
  }, [repositoryId, selectedSessionId]);

  useEffect(() => {
    if (!repositoryId || !selectedSessionId) {
      setAnalysisSessions([]);
      setSelectedAnalysisSessionId("");
      setAnalysisSessionDetail(null);
      setLastAugmentResult(null);
      return;
    }

    let cancelled = false;

    const loadAnalysisSessions = async () => {
      try {
        const items = await getRepositoryWorkflowAnalysisSessions(repositoryId, selectedSessionId);
        if (cancelled) return;

        setAnalysisSessions(items);
        setSelectedAnalysisSessionId((current) => {
          if (current && items.some((item) => item.analysisSessionId === current)) {
            return current;
          }

          return items[0]?.analysisSessionId ?? "";
        });

        if (items.length === 0) {
          setAnalysisSessionDetail(null);
        }
      } catch (error) {
        if (cancelled) return;
        console.error(error);
        toast.error(getErrorMessage(error) || "加载深挖会话失败");
        setAnalysisSessions([]);
        setSelectedAnalysisSessionId("");
        setAnalysisSessionDetail(null);
      }
    };

    void loadAnalysisSessions();

    return () => {
      cancelled = true;
    };
  }, [repositoryId, selectedSessionId]);

  useEffect(() => {
    setLastAugmentResult(null);
    setIsAugmentDialogOpen(false);
    setIsAnalysisDialogOpen(false);
  }, [selectedSessionId]);

  useEffect(() => {
    const chapterProfiles = currentDraft?.chapterProfiles ?? [];
    if (chapterProfiles.length === 0) {
      setSelectedAnalysisChapterKey("");
      return;
    }

    setSelectedAnalysisChapterKey((current) => {
      if (current && chapterProfiles.some((chapter) => chapter.key === current)) {
        return current;
      }

      return chapterProfiles[0]?.key ?? "";
    });
  }, [currentDraft]);

  useEffect(() => {
    if (!repositoryId || !selectedSessionId || !selectedAnalysisSessionId) {
      setAnalysisSessionDetail(null);
      setLoadingAnalysisSession(false);
      return;
    }

    let cancelled = false;

    const loadAnalysisSession = async () => {
      setLoadingAnalysisSession(true);
      try {
        const detail = await getRepositoryWorkflowAnalysisSession(
          repositoryId,
          selectedSessionId,
          selectedAnalysisSessionId
        );
        if (cancelled) return;
        setAnalysisSessionDetail(detail);
      } catch (error) {
        if (cancelled) return;
        console.error(error);
        toast.error(getErrorMessage(error) || "加载深挖详情失败");
        setAnalysisSessionDetail(null);
      } finally {
        if (!cancelled) {
          setLoadingAnalysisSession(false);
        }
      }
    };

    void loadAnalysisSession();

    return () => {
      cancelled = true;
    };
  }, [repositoryId, selectedAnalysisSessionId, selectedSessionId]);

  useEffect(() => {
    if (
      !repositoryId ||
      !selectedSessionId ||
      !selectedAnalysisSessionId ||
      !shouldPollAnalysisSession(activeAnalysisStatus)
    ) {
      return;
    }

    let cancelled = false;
    let timerId: number | null = null;

    const poll = async () => {
      try {
        const [detail, logs, items] = await Promise.all([
          getRepositoryWorkflowAnalysisSession(repositoryId, selectedSessionId, selectedAnalysisSessionId),
          getRepositoryWorkflowAnalysisSessionLogs(repositoryId, selectedSessionId, selectedAnalysisSessionId, {
            limit: 100,
          }),
          getRepositoryWorkflowAnalysisSessions(repositoryId, selectedSessionId),
        ]);

        if (cancelled) return;

        setAnalysisSessionDetail({
          ...detail,
          recentLogs: logs.length > 0 ? logs : detail.recentLogs,
        });
        setAnalysisSessions(items);
      } catch (error) {
        if (cancelled) return;
        console.error(error);
      }
    };

    timerId = window.setInterval(() => {
      void poll();
    }, 3000);
    void poll();

    return () => {
      cancelled = true;
      if (timerId !== null) {
        window.clearInterval(timerId);
      }
    };
  }, [activeAnalysisStatus, repositoryId, selectedAnalysisSessionId, selectedSessionId]);

  const handleRefreshSessions = async () => {
    if (!repositoryId) return;

    setRefreshingSessions(true);
    try {
      const items = await getRepositoryWorkflowTemplateSessions(repositoryId);
      setSessions(items);

      const nextSessionId =
        (selectedSessionId && items.some((item) => item.sessionId === selectedSessionId)
          ? selectedSessionId
          : items[0]?.sessionId) ?? "";

      setSelectedSessionId(nextSessionId);

      if (nextSessionId) {
        const detail = await getRepositoryWorkflowTemplateSession(repositoryId, nextSessionId);
        setSessionDetail(detail);
        setSessions((current) => mergeSessionSummary(current, detail));
      } else {
        setSessionDetail(null);
      }
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "刷新会话失败");
    } finally {
      setRefreshingSessions(false);
    }
  };

  const handleCreateSession = async () => {
    if (!repositoryId) return;
    if (!selectedBranchId || !selectedLanguage) {
      toast.error("请先在上方选择分支和语言，再创建工作台会话");
      return;
    }

    setCreatingSession(true);
    try {
      const created = await createRepositoryWorkflowTemplateSession(repositoryId, {
        branchId: selectedBranchId,
        languageCode: selectedLanguage,
      });
      setSessionDetail(created);
      setSelectedSessionId(created.sessionId);
      setSessions((current) => mergeSessionSummary(current, created));
      setDraftMessage("");
      toast.success("已创建新的业务流模板会话");
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "创建会话失败");
    } finally {
      setCreatingSession(false);
    }
  };

  const handleSendMessage = async () => {
    if (!repositoryId || !sessionDetail) {
      toast.error("请先创建或选择一个工作台会话");
      return;
    }
    if (!draftMessage.trim()) {
      toast.error("请输入本轮调整要求");
      return;
    }

    setSendingMessage(true);
    try {
      const updated = await sendRepositoryWorkflowTemplateMessage(repositoryId, sessionDetail.sessionId, {
        content: draftMessage.trim(),
      });
      setSessionDetail(updated);
      setSessions((current) => mergeSessionSummary(current, updated));
      setDraftMessage("");
      toast.success(`已生成 v${updated.currentVersionNumber} 草稿`);
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "发送消息失败");
    } finally {
      setSendingMessage(false);
    }
  };

  const handleAugmentDraft = async () => {
    if (!repositoryId || !sessionDetail) {
      toast.error("请先创建或选择一个工作台会话");
      return;
    }

    setAugmentingDraft(true);
    try {
      const result = await augmentRepositoryWorkflowTemplateDraft(repositoryId, sessionDetail.sessionId, {
        applyToDraftVersion: true,
      });
      setLastAugmentResult(result.augment);
      setIsAugmentDialogOpen(true);
      setSessionDetail(result.session);
      setSessions((current) => mergeSessionSummary(current, result.session));
      toast.success(
        result.createdVersionNumber
          ? `已生成增强草稿 v${result.createdVersionNumber}`
          : "已完成当前草稿增强"
      );
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "增强当前草稿失败");
    } finally {
      setAugmentingDraft(false);
    }
  };

  const handleCreateAnalysisSession = async () => {
    if (!repositoryId || !sessionDetail) {
      toast.error("请先创建或选择一个工作台会话");
      return;
    }

    if (analysisScope === "chapter" && !selectedAnalysisChapterKey) {
      toast.error("请先选择要聚焦深挖的章节");
      return;
    }

    setCreatingAnalysisSession(true);
    try {
      const detail = await createRepositoryWorkflowAnalysisSession(repositoryId, sessionDetail.sessionId, {
        chapterKey: analysisScope === "chapter" ? selectedAnalysisChapterKey : undefined,
        objective: currentDraft?.acp?.objective,
      });
      setAnalysisSessionDetail(detail);
      setSelectedAnalysisSessionId(detail.analysisSessionId);
      setAnalysisSessions((current) => mergeAnalysisSessionSummary(current, detail));
      setIsAnalysisDialogOpen(true);
      toast.success(
        analysisScope === "chapter"
          ? "已创建章节 ACP 深挖会话"
          : "已创建整条业务流 ACP 深挖会话"
      );
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "创建深挖会话失败");
    } finally {
      setCreatingAnalysisSession(false);
    }
  };

  const handleRollbackVersion = async (versionNumber: number) => {
    if (!repositoryId || !sessionDetail) return;

    setRollingBackVersionNumber(versionNumber);
    try {
      const updated = await rollbackRepositoryWorkflowTemplateVersion(
        repositoryId,
        sessionDetail.sessionId,
        versionNumber
      );
      setSessionDetail(updated);
      setSessions((current) => mergeSessionSummary(current, updated));
      toast.success(`已基于 v${versionNumber} 生成新的回滚草稿`);
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "回滚版本失败");
    } finally {
      setRollingBackVersionNumber(null);
    }
  };

  const adoptVersion = async (
    versionNumber: number,
    options?: { silentSuccess?: boolean }
  ) => {
    if (!repositoryId || !sessionDetail) return;
    if (isFormalConfigDirty) {
      toast.error("正式 Workflow JSON 还有未保存修改，请先保存或放弃修改后再采用草稿");
      return;
    }

    setAdoptingVersionNumber(versionNumber);
    try {
      const result = await adoptRepositoryWorkflowTemplateVersion(
        repositoryId,
        sessionDetail.sessionId,
        versionNumber
      );
      setSessionDetail(result.session);
      setSessions((current) => mergeSessionSummary(current, result.session));
      onConfigAdopted(result.savedConfig);
      if (!options?.silentSuccess) {
        toast.success(`已将 v${versionNumber} 采用到正式 Workflow 配置`);
      }
      return result;
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "采用版本失败");
      return null;
    } finally {
      setAdoptingVersionNumber(null);
    }
  };

  const handleAdoptVersion = async (versionNumber: number) => {
    await adoptVersion(versionNumber);
  };

  const handleAdoptCurrentVersion = async () => {
    if (!currentVersionNumber) {
      toast.error("当前还没有可采用的草稿版本");
      return;
    }

    await adoptVersion(currentVersionNumber);
  };

  const handleAdoptAndRegenerateCurrentVersion = async () => {
    if (!repositoryId || !sessionDetail || !currentVersionNumber) {
      toast.error("当前还没有可采用的草稿版本");
      return;
    }
    if (!currentDraftKey) {
      toast.error("当前草稿还没有可用于重建的 profileKey");
      return;
    }
    if (!selectedBranchId || !selectedLanguage) {
      toast.error("请先在上方选择分支和语言");
      return;
    }
    if (isFormalConfigDirty) {
      toast.error("正式 Workflow JSON 还有未保存修改，请先保存或放弃修改后再采用草稿");
      return;
    }

    setAdoptingAndRegenerating(true);
    try {
      const adopted = await adoptVersion(currentVersionNumber, { silentSuccess: true });
      if (!adopted) {
        return;
      }

      const success = await onRegenerateSpecificWorkflow(currentDraftKey);
      if (success) {
        toast.success(`已采用 v${currentVersionNumber} 并完成当前草稿重建`);
      }
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "采用并重建当前草稿失败");
    } finally {
      setAdoptingAndRegenerating(false);
    }
  };

  const handleAdoptAndRegenerateAllWorkflows = async () => {
    if (!repositoryId || !sessionDetail || !currentVersionNumber) {
      toast.error("当前还没有可采用的草稿版本");
      return;
    }
    if (!selectedBranchId || !selectedLanguage) {
      toast.error("请先在上方选择分支和语言");
      return;
    }
    if (isFormalConfigDirty) {
      toast.error("正式 Workflow JSON 还有未保存修改，请先保存或放弃修改后再采用草稿");
      return;
    }

    setAdoptingAndRegeneratingAll(true);
    try {
      const adopted = await adoptVersion(currentVersionNumber, { silentSuccess: true });
      if (!adopted) {
        return;
      }

      const success = await onRegenerateAllWorkflows();
      if (success) {
        toast.success(`已采用 v${currentVersionNumber} 并完成全部业务流重建`);
      }
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "采用并重建全部业务流失败");
    } finally {
      setAdoptingAndRegeneratingAll(false);
    }
  };

  const handleRegenerateCurrentWorkflow = async () => {
    if (!selectedBranchId || !selectedLanguage) {
      toast.error("请先在上方选择分支和语言");
      return;
    }
    if (isFormalConfigDirty) {
      toast.error("正式 Workflow JSON 还有未保存修改，请先保存或放弃修改后再重建");
      return;
    }

    try {
      await onRegenerateCurrentWorkflow();
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "已选正式业务流重建失败");
    }
  };

  const handleRegenerateAllWorkflows = async () => {
    if (!selectedBranchId || !selectedLanguage) {
      toast.error("请先在上方选择分支和语言");
      return;
    }
    if (isFormalConfigDirty) {
      toast.error("正式 Workflow JSON 还有未保存修改，请先保存或放弃修改后再重建");
      return;
    }

    try {
      await onRegenerateAllWorkflows();
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "全部业务流重建失败");
    }
  };

  const handleStartDiscussionFromCandidate = async (
    candidate: WorkflowTemplateDiscoveryCandidate
  ) => {
    if (!repositoryId) return;
    if (!selectedBranchId || !selectedLanguage) {
      toast.error("请先在上方选择分支和语言，再基于候选流程开始讨论");
      return;
    }

    const seedPrompt = buildCandidateSeedPrompt(candidate);
    setStartingCandidateKey(candidate.key);
    try {
      const created = await createRepositoryWorkflowTemplateSession(repositoryId, {
        branchId: selectedBranchId,
        languageCode: selectedLanguage,
        title: candidate.name,
      });

      setSessionDetail(created);
      setSelectedSessionId(created.sessionId);
      setSessions((current) => mergeSessionSummary(current, created));

      setSendingMessage(true);
      try {
        const updated = await sendRepositoryWorkflowTemplateMessage(repositoryId, created.sessionId, {
          content: seedPrompt,
        });
        setSessionDetail(updated);
        setSessions((current) => mergeSessionSummary(current, updated));
        setDraftMessage("");
        toast.success(`已基于候选“${candidate.name}”创建会话并生成首稿`);
      } catch (error) {
        console.error(error);
        setDraftMessage(seedPrompt);
        toast.error(
          getErrorMessage(error) || "候选流程会话已创建，但自动生成首稿失败，可直接调整输入框后继续发送"
        );
      } finally {
        setSendingMessage(false);
      }
    } catch (error) {
      console.error(error);
      toast.error(getErrorMessage(error) || "基于候选流程创建会话失败");
    } finally {
      setStartingCandidateKey(null);
    }
  };

  const currentDraftKey = currentDraft?.key || sessionDetail?.currentDraftKey || null;
  const hasExecutionContext = Boolean(selectedBranchId && selectedLanguage);
  const configReadyForRegeneration = hasExecutionContext && !isFormalConfigDirty;
  const formalProfileLabel = formalSelectedProfileKey
    ? `${formalSelectedProfileName || formalSelectedProfileKey} (${formalSelectedProfileKey})`
    : "(未选择)";
  const currentDraftProfileLabel = currentDraftKey
    ? `${currentDraft?.name || sessionDetail?.currentDraftName || currentDraftKey} (${currentDraftKey})`
    : "(尚无 Key)";
  const formalWorkflowActionReady = canRegenerateCurrentWorkflow && configReadyForRegeneration;
  const draftWorkflowActionReady = Boolean(currentVersionNumber && currentDraftKey) && configReadyForRegeneration;
  const allWorkflowActionReady = configReadyForRegeneration;
  const hasAugmentResult = Boolean(lastAugmentResult);
  const hasAnalysisSession = analysisSessions.length > 0 || Boolean(analysisSessionDetail);

  return (
    <div className="min-w-0 space-y-4 overflow-x-hidden">
      <Card className="min-w-0 space-y-3 overflow-hidden p-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h4 className="font-semibold">工作台闭环操作</h4>
            <p className="text-xs text-muted-foreground">
              在 AI 页签内直接完成“采用到正式配置”与“已选正式/当前草稿/全部业务流重建”，不需要再切回 JSON 页签。
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              onClick={onApplyWmsTemplate}
              disabled={formalConfigLoading || formalConfigSaving || workbenchBusy}
            >
              <WandSparkles className="mr-2 h-4 w-4" />
              填入 WMS/WCS 模板
            </Button>
            <Button
              variant="outline"
              onClick={handleRegenerateCurrentWorkflow}
              disabled={
                !canRegenerateCurrentWorkflow ||
                formalConfigLoading ||
                formalConfigSaving ||
                workflowRegenerating ||
                workbenchBusy ||
                !selectedBranchId ||
                !selectedLanguage ||
                isFormalConfigDirty
              }
            >
              {workflowRegenerating ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Route className="mr-2 h-4 w-4" />
              )}
              重建已选正式业务流
            </Button>
            <Button
              variant="outline"
              onClick={handleRegenerateAllWorkflows}
              disabled={
                formalConfigLoading ||
                formalConfigSaving ||
                workflowRegenerating ||
                workbenchBusy ||
                !selectedBranchId ||
                !selectedLanguage ||
                isFormalConfigDirty
              }
            >
              {workflowRegenerating ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Route className="mr-2 h-4 w-4" />
              )}
              重建全部业务流
            </Button>
            <Button
              variant="outline"
              onClick={onSaveFormalConfig}
              disabled={formalConfigLoading || formalConfigSaving || workbenchBusy}
            >
              {formalConfigSaving ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Save className="mr-2 h-4 w-4" />
              )}
              保存配置
            </Button>
            <Button
              variant="outline"
              onClick={handleAugmentDraft}
              disabled={!sessionDetail || !currentDraft || workbenchBusy}
            >
              {augmentingDraft ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Sparkles className="mr-2 h-4 w-4" />
              )}
              增强当前草稿
            </Button>
            <Button
              variant="outline"
              onClick={handleCreateAnalysisSession}
              disabled={!sessionDetail || !currentDraft || workbenchBusy}
            >
              {creatingAnalysisSession ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Route className="mr-2 h-4 w-4" />
              )}
              {analysisScope === "chapter" ? "发起章节 ACP 深挖" : "发起整条业务流 ACP 深挖"}
            </Button>
            <Button
              variant="outline"
              onClick={handleAdoptCurrentVersion}
              disabled={
                !currentVersionNumber ||
                workbenchBusy ||
                formalConfigLoading ||
                formalConfigSaving ||
                isFormalConfigDirty
              }
            >
              {adoptingVersionNumber === currentVersionNumber ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <CheckCheck className="mr-2 h-4 w-4" />
              )}
              仅采用当前草稿
            </Button>
            <Button
              onClick={handleAdoptAndRegenerateCurrentVersion}
              disabled={
                !currentVersionNumber ||
                workbenchBusy ||
                formalConfigLoading ||
                formalConfigSaving ||
                workflowRegenerating ||
                !selectedBranchId ||
                !selectedLanguage ||
                isFormalConfigDirty
              }
            >
              {adoptingAndRegenerating ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Route className="mr-2 h-4 w-4" />
              )}
              采用并重建当前草稿
            </Button>
            <Button
              onClick={handleAdoptAndRegenerateAllWorkflows}
              disabled={
                !currentVersionNumber ||
                workbenchBusy ||
                formalConfigLoading ||
                formalConfigSaving ||
                workflowRegenerating ||
                !selectedBranchId ||
                !selectedLanguage ||
                isFormalConfigDirty
              }
            >
              {adoptingAndRegeneratingAll ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Route className="mr-2 h-4 w-4" />
              )}
              采用并重建全部业务流
            </Button>
          </div>
        </div>

        <div className="space-y-3 rounded-md border bg-muted/15 px-3 py-3">
          <div className="flex flex-wrap items-center gap-3">
            <div className="text-xs font-medium text-muted-foreground">ACP 深挖范围</div>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="button"
                size="sm"
                variant={analysisScope === "workflow" ? "default" : "outline"}
                onClick={() => setAnalysisScope("workflow")}
                disabled={!currentDraft}
              >
                整条业务流
              </Button>
              <Button
                type="button"
                size="sm"
                variant={analysisScope === "chapter" ? "default" : "outline"}
                onClick={() => setAnalysisScope("chapter")}
                disabled={!currentDraft?.chapterProfiles?.length}
              >
                仅某章节
              </Button>
            </div>
            <div className="text-xs text-muted-foreground">
              默认会分析当前业务流的主线、章节和分支任务；只有要单独重跑某一章时才需要切到“仅某章节”。
            </div>
          </div>

          <div className="grid gap-3 2xl:grid-cols-[1.15fr_0.85fr]">
            <div className="min-w-0 rounded-xl border bg-background/90 p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="text-sm font-medium text-foreground">AI 工作台视图摘要</div>
                <div className="flex flex-wrap items-center gap-2">
                  {activeAnalysisStatus ? <Badge>{activeAnalysisStatus}</Badge> : null}
                  {selectedAnalysisSummary?.chapterKey ? (
                    <Badge variant="outline">{selectedAnalysisSummary.chapterKey}</Badge>
                  ) : (
                    <Badge variant="outline">workflow</Badge>
                  )}
                </div>
              </div>
              <div className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2 2xl:grid-cols-4">
                <div className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 break-all">
                  最新 ACP 会话：{selectedAnalysisSummary?.analysisSessionId || "(暂无)"}
                </div>
                <div className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 break-all">
                  当前任务：{currentAnalysisTaskTitle || analysisSessionDetail?.currentTaskId || "(等待)"}
                </div>
                <div className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 break-words">
                  进度：{analysisSessionDetail?.completedTasks ?? 0}/{analysisSessionDetail?.totalTasks ?? 0}
                </div>
                <div className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 break-words">
                  并行：{analysisSessionDetail?.runningTaskCount ?? 0} / 失败：{analysisSessionDetail?.failedTasks ?? 0}
                </div>
              </div>
              <div className="mt-3 text-xs leading-5 text-muted-foreground">
                主界面现在只保留状态摘要，实时日志、任务拆分和产物差异统一收进弹窗，避免和草稿正文、版本历史互相抢空间。
              </div>
            </div>

            <div className="min-w-0 rounded-xl border border-dashed bg-background/90 p-3">
              <div className="text-sm font-medium text-foreground">浮层入口</div>
              <div className="mt-2 space-y-2">
                <Button
                  className="w-full justify-start"
                  variant={sessionDetail ? "default" : "outline"}
                  disabled={!sessionDetail && sessions.length === 0}
                  onClick={() => setIsSessionWorkspaceDialogOpen(true)}
                >
                  <Bot className="mr-2 h-4 w-4" />
                  打开会话工作区
                </Button>
                <Button
                  className="w-full justify-start"
                  variant={hasAugmentResult ? "default" : "outline"}
                  disabled={!hasAugmentResult}
                  onClick={() => setIsAugmentDialogOpen(true)}
                >
                  <Sparkles className="mr-2 h-4 w-4" />
                  查看增强结果
                </Button>
                <Button
                  className="w-full justify-start"
                  variant={hasAnalysisSession ? "default" : "outline"}
                  disabled={!hasAnalysisSession}
                  onClick={() => setIsAnalysisDialogOpen(true)}
                >
                  <Route className="mr-2 h-4 w-4" />
                  查看 ACP 实时视图
                </Button>
              </div>
              <div className="mt-3 text-xs leading-5 text-muted-foreground">
                关闭弹窗只会隐藏，不会清空增强结果或 ACP 会话；需要时可以随时从这里重新打开。
              </div>
            </div>
          </div>

          {analysisScope === "chapter" ? (
            currentDraft?.chapterProfiles?.length ? (
              <div className="flex flex-wrap items-center gap-3">
                <div className="text-xs font-medium text-muted-foreground">聚焦章节</div>
                <Select value={selectedAnalysisChapterKey} onValueChange={setSelectedAnalysisChapterKey}>
                  <SelectTrigger className="w-full max-w-[360px]">
                    <SelectValue placeholder="选择要深挖的章节" />
                  </SelectTrigger>
                  <SelectContent>
                    {currentDraft.chapterProfiles.map((chapter) => (
                      <SelectItem key={chapter.key} value={chapter.key}>
                        {chapter.title}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <div className="text-xs text-muted-foreground">
                  章节聚焦模式只重跑这里选中的章节，用于补分析或单章重试。
                </div>
              </div>
            ) : (
              <div className="text-xs text-muted-foreground">
                当前草稿还没有章节切片，先执行一次“增强当前草稿”再做章节聚焦。
              </div>
            )
          ) : null}
        </div>

        <div className="grid gap-2 text-xs text-muted-foreground md:grid-cols-2 xl:grid-cols-5">
          <div className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 break-words">
            当前会话：{sessionDetail?.title || sessionDetail?.currentDraftName || "(未选择)"}
          </div>
          <div className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 break-words">
            当前草稿版本：{currentVersionNumber ? `v${currentVersionNumber}` : "(尚无版本)"}
          </div>
          <div className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 break-all">
            已选正式 Profile：
            {formalProfileLabel}
          </div>
          <div className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 break-all">
            当前草稿 Profile：
            {currentDraftProfileLabel}
          </div>
          <div className="min-w-0 rounded-md border bg-muted/20 px-3 py-2 break-words">
            下一步：重建已选正式业务流=当前下拉选中项；采用并重建当前草稿=当前会话草稿
          </div>
        </div>

        <div className="grid gap-2 md:grid-cols-2 2xl:grid-cols-3">
          <div className="min-w-0 rounded-md border bg-background px-3 py-3">
            <div className="flex items-center justify-between gap-2">
              <div className="text-sm font-medium text-foreground">已选正式业务流</div>
              <Badge variant={formalWorkflowActionReady ? "default" : "secondary"}>
                {formalWorkflowActionReady ? "可执行" : "待补齐"}
              </Badge>
            </div>
            <div className="mt-2 text-xs text-muted-foreground break-all">作用对象：{formalProfileLabel}</div>
            <div className="text-xs text-muted-foreground">按钮：重建已选正式业务流</div>
            <div className="text-xs text-muted-foreground">
              前提：已选正式 profile，分支/语言已选择，正式配置已保存
            </div>
          </div>

          <div className="min-w-0 rounded-md border bg-background px-3 py-3">
            <div className="flex items-center justify-between gap-2">
              <div className="text-sm font-medium text-foreground">当前草稿</div>
              <Badge variant={draftWorkflowActionReady ? "default" : "secondary"}>
                {draftWorkflowActionReady ? "可执行" : "待补齐"}
              </Badge>
            </div>
            <div className="mt-2 text-xs text-muted-foreground break-all">作用对象：{currentDraftProfileLabel}</div>
            <div className="text-xs text-muted-foreground">按钮：仅采用当前草稿 / 采用并重建当前草稿</div>
            <div className="text-xs text-muted-foreground">
              前提：当前会话已有草稿版本与草稿 key，分支/语言已选择，正式配置已保存
            </div>
          </div>

          <div className="min-w-0 rounded-md border bg-background px-3 py-3">
            <div className="flex items-center justify-between gap-2">
              <div className="text-sm font-medium text-foreground">全部业务流</div>
              <Badge variant={allWorkflowActionReady ? "default" : "secondary"}>
                {allWorkflowActionReady ? "可执行" : "待补齐"}
              </Badge>
            </div>
            <div className="mt-2 text-xs text-muted-foreground">作用对象：当前仓库全部业务流</div>
            <div className="text-xs text-muted-foreground">按钮：重建全部业务流 / 采用并重建全部业务流</div>
            <div className="text-xs text-muted-foreground">
              前提：分支/语言已选择，正式配置已保存；若要先采用草稿，还需要当前会话已有草稿版本
            </div>
          </div>
        </div>
      </Card>

      <div className="min-w-0 space-y-4">
      <Card className="min-w-0 overflow-hidden p-4">
        <div className="mb-3 flex items-start justify-between gap-3">
          <div>
            <h4 className="font-semibold">当前草稿</h4>
            <p className="text-xs text-muted-foreground">中间区域只看当前版本，避免把历史版本和正式配置混在一起。</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              onClick={() => setIsSessionWorkspaceDialogOpen(true)}
            >
              <Bot className="mr-2 h-4 w-4" />
              打开会话工作区
            </Button>
            {currentDraft?.key ? <Badge variant="outline">{currentDraft.key}</Badge> : null}
          </div>
        </div>

        {!sessionDetail ? (
          <div className="rounded-md border border-dashed px-3 py-10 text-sm text-muted-foreground">
            暂无当前草稿。先在左侧创建会话并发送需求。
          </div>
        ) : !currentDraft ? (
          <div className="rounded-md border border-dashed px-3 py-10 text-sm text-muted-foreground">
            当前会话还没有生成任何草稿版本。
          </div>
        ) : (
          <ScrollArea className="h-[1100px] pr-3">
            <div className="space-y-4">
              <VersionHistoryAccordion
                sessionDetail={sessionDetail}
                isFormalConfigDirty={isFormalConfigDirty}
                adoptingVersionNumber={adoptingVersionNumber}
                rollingBackVersionNumber={rollingBackVersionNumber}
                onAdoptVersion={handleAdoptVersion}
                onRollbackVersion={handleRollbackVersion}
              />

              <div className="rounded-xl border bg-muted/15 p-4">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge>{currentDraft.name}</Badge>
                  <Badge variant="outline">{currentDraft.mode}</Badge>
                  <Badge variant={currentDraft.enabled ? "default" : "secondary"}>
                    {currentDraft.enabled ? "enabled=true" : "enabled=false"}
                  </Badge>
                  {sessionDetail.currentVersionNumber ? (
                    <Badge variant="outline">当前版本 v{sessionDetail.currentVersionNumber}</Badge>
                  ) : null}
                </div>
                <div className="mt-3 min-w-0 space-y-2 break-all text-sm leading-6">
                  <div>
                    <span className="font-medium">业务标题：</span>
                    {currentDraft.name || "(未命名)"}
                  </div>
                  <div>
                    <span className="font-medium">业务说明：</span>
                    {currentDraft.description || "(未提供)"}
                  </div>
                  <div>
                    <span className="font-medium">锚点名称：</span>
                    {joinValues(currentDraft.anchorNames)}
                  </div>
                  <div>
                    <span className="font-medium">锚点目录：</span>
                    {joinValues(currentDraft.anchorDirectories)}
                  </div>
                  <div>
                    <span className="font-medium">主入口名称：</span>
                    {joinValues(currentDraft.primaryTriggerNames)}
                  </div>
                  <div>
                    <span className="font-medium">补偿入口名称：</span>
                    {joinValues(currentDraft.compensationTriggerNames)}
                  </div>
                  <div>
                    <span className="font-medium">调度器名称：</span>
                    {joinValues(currentDraft.schedulerNames)}
                  </div>
                  <div>
                    <span className="font-medium">请求实体：</span>
                    {joinValues(currentDraft.requestEntityNames)}
                  </div>
                  <div>
                    <span className="font-medium">请求服务：</span>
                    {joinValues(currentDraft.requestServiceNames)}
                  </div>
                  <div>
                    <span className="font-medium">请求仓储：</span>
                    {joinValues(currentDraft.requestRepositoryNames)}
                  </div>
                </div>
              </div>

              <div className="rounded-xl border p-4">
                <div className="mb-2 flex items-center gap-2 text-sm font-medium">
                  <Sparkles className="h-4 w-4" />
                  文档偏好
                </div>
                <div className="min-w-0 space-y-2 break-all text-sm leading-6">
                  <div>
                    <span className="font-medium">写作提示：</span>
                    {currentDraft.documentPreferences.writingHint || "(未提供)"}
                  </div>
                  <div>
                    <span className="font-medium">优先术语：</span>
                    {joinValues(currentDraft.documentPreferences.preferredTerms)}
                  </div>
                  <div>
                    <span className="font-medium">必备章节：</span>
                    {joinValues(currentDraft.documentPreferences.requiredSections)}
                  </div>
                  <div>
                    <span className="font-medium">禁止作为主入口：</span>
                    {joinValues(currentDraft.documentPreferences.avoidPrimaryTriggerNames)}
                  </div>
                </div>
              </div>

              <div className="rounded-xl border p-4">
                <div className="mb-2 flex items-center gap-2 text-sm font-medium">
                  <History className="h-4 w-4" />
                  草稿来源
                </div>
                <div className="min-w-0 space-y-2 break-all text-sm leading-6">
                  <div>
                    <span className="font-medium">来源类型：</span>
                    {currentDraft.source.type}
                  </div>
                  <div>
                    <span className="font-medium">关联会话：</span>
                    {currentDraft.source.sessionId || "(无)"}
                  </div>
                  <div>
                    <span className="font-medium">来源版本：</span>
                    {currentDraft.source.versionNumber ? `v${currentDraft.source.versionNumber}` : "(无)"}
                  </div>
                  <div>
                    <span className="font-medium">更新人：</span>
                    {currentDraft.source.updatedByUserName || currentDraft.source.updatedByUserId || "(未知)"}
                  </div>
                  <div>
                    <span className="font-medium">更新时间：</span>
                    {formatDateTime(currentDraft.source.updatedAt)}
                  </div>
                </div>
              </div>

              <div className="rounded-xl border p-4">
                <div className="mb-2 text-sm font-medium">草稿 JSON 预览</div>
                <Textarea
                  readOnly
                  value={currentDraftJson}
                  className="min-h-[520px] min-w-0 resize-none [field-sizing:fixed] font-mono text-xs leading-5"
                />
              </div>
            </div>
          </ScrollArea>
        )}
      </Card>
      </div>

      <Dialog open={isSessionWorkspaceDialogOpen} onOpenChange={setIsSessionWorkspaceDialogOpen}>
        <DialogContent className="max-h-[92vh] max-w-[calc(100vw-2rem)] gap-0 overflow-hidden p-0 xl:max-w-6xl">
          <div className="grid max-h-[92vh] grid-rows-[auto_1fr] overflow-hidden">
            <DialogHeader className="border-b px-6 py-5">
              <DialogTitle>会话工作区</DialogTitle>
              <DialogDescription>
                会话列表、多轮对话、上下文和候选流程统一放进弹窗，需要时再打开，不占主草稿视图空间。
              </DialogDescription>
            </DialogHeader>
            <ScrollArea className="h-full">
              <div className="p-6">
                <SessionWorkspacePanel
                  sessionDetail={sessionDetail}
                  selectedBranchId={selectedBranchId}
                  selectedLanguage={selectedLanguage}
                  sessions={sessions}
                  selectedSessionId={selectedSessionId}
                  loadingSessions={loadingSessions}
                  refreshingSessions={refreshingSessions}
                  creatingSession={creatingSession}
                  loadingSession={loadingSession}
                  sendingMessage={sendingMessage}
                  startingCandidateKey={startingCandidateKey}
                  draftMessage={draftMessage}
                  onDraftMessageChange={setDraftMessage}
                  onRefreshSessions={handleRefreshSessions}
                  onCreateSession={handleCreateSession}
                  onSelectSession={setSelectedSessionId}
                  onStartDiscussionFromCandidate={handleStartDiscussionFromCandidate}
                  onSendMessage={handleSendMessage}
                />
              </div>
            </ScrollArea>
          </div>
        </DialogContent>
      </Dialog>

      <Dialog open={isAugmentDialogOpen} onOpenChange={setIsAugmentDialogOpen}>
        <DialogContent className="max-h-[90vh] max-w-[calc(100vw-2rem)] gap-0 overflow-hidden p-0 lg:max-w-6xl">
          <div className="grid max-h-[90vh] grid-rows-[auto_1fr] overflow-hidden">
            <DialogHeader className="border-b px-6 py-5">
              <DialogTitle>增强当前草稿结果</DialogTitle>
              <DialogDescription>
                增强日志与建议结构改为浮层查看。关闭只隐藏视图，不会丢失当前增强结果。
              </DialogDescription>
            </DialogHeader>
            <ScrollArea className="h-full">
              <div className="p-6">
                <AugmentResultPanel
                  result={lastAugmentResult}
                  currentDraftKey={currentDraftKey}
                  currentVersionNumber={currentVersionNumber}
                />
              </div>
            </ScrollArea>
          </div>
        </DialogContent>
      </Dialog>

      <Dialog open={isAnalysisDialogOpen} onOpenChange={setIsAnalysisDialogOpen}>
        <DialogContent className="max-h-[92vh] max-w-[calc(100vw-2rem)] gap-0 overflow-hidden p-0 xl:max-w-7xl">
          <div className="grid max-h-[92vh] grid-rows-[auto_1fr] overflow-hidden">
            <DialogHeader className="border-b px-6 py-5">
              <DialogTitle>ACP 实时执行视图</DialogTitle>
              <DialogDescription>
                主界面只保留摘要，这里集中查看实时日志、任务拆分、产物差异和完整会话细节。
              </DialogDescription>
            </DialogHeader>
            <ScrollArea className="h-full">
              <div className="space-y-4 p-6">
                <AnalysisLivePreview
                  detail={analysisSessionDetail}
                  status={activeAnalysisStatus}
                  currentTaskTitle={currentAnalysisTaskTitle}
                  logs={recentAnalysisLogs}
                  diffs={recentAnalysisArtifactDiffs}
                />
                <WorkflowAnalysisPanel
                  sessions={analysisSessions}
                  selectedSessionId={selectedAnalysisSessionId}
                  detail={analysisSessionDetail}
                  chapterProfiles={currentDraft?.chapterProfiles ?? []}
                  onSelectSession={setSelectedAnalysisSessionId}
                />
              </div>
            </ScrollArea>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

interface SessionWorkspacePanelProps {
  sessionDetail: WorkflowTemplateSessionDetail | null;
  selectedBranchId: string;
  selectedLanguage: string;
  sessions: WorkflowTemplateSessionSummary[];
  selectedSessionId: string;
  loadingSessions: boolean;
  refreshingSessions: boolean;
  creatingSession: boolean;
  loadingSession: boolean;
  sendingMessage: boolean;
  startingCandidateKey: string | null;
  draftMessage: string;
  onDraftMessageChange: (value: string) => void;
  onRefreshSessions: () => Promise<void>;
  onCreateSession: () => Promise<void>;
  onSelectSession: (sessionId: string) => void;
  onStartDiscussionFromCandidate: (candidate: WorkflowTemplateDiscoveryCandidate) => Promise<void>;
  onSendMessage: () => Promise<void>;
}

function SessionWorkspacePanel({
  sessionDetail,
  selectedBranchId,
  selectedLanguage,
  sessions,
  selectedSessionId,
  loadingSessions,
  refreshingSessions,
  creatingSession,
  loadingSession,
  sendingMessage,
  startingCandidateKey,
  draftMessage,
  onDraftMessageChange,
  onRefreshSessions,
  onCreateSession,
  onSelectSession,
  onStartDiscussionFromCandidate,
  onSendMessage,
}: SessionWorkspacePanelProps) {
  return (
    <div className="grid gap-4 xl:grid-cols-[0.92fr_1.08fr]">
      <Card className="space-y-3 p-4">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h4 className="font-semibold">会话与上下文</h4>
            <p className="text-xs text-muted-foreground">
              每个会话只收敛一个业务流，支持多轮追问、版本回滚和正式采用。
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => void onRefreshSessions()}
              disabled={loadingSessions || refreshingSessions}
            >
              {refreshingSessions ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <RefreshCw className="mr-2 h-4 w-4" />
              )}
              刷新
            </Button>
            <Button size="sm" onClick={() => void onCreateSession()} disabled={creatingSession || loadingSessions}>
              {creatingSession ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <MessageSquarePlus className="mr-2 h-4 w-4" />
              )}
              新建会话
            </Button>
          </div>
        </div>

        <div className="grid gap-2 text-xs text-muted-foreground sm:grid-cols-2">
          <div className="rounded-md border bg-muted/20 px-3 py-2">
            当前分支：{sessionDetail?.branchName || selectedBranchId || "(未选择)"}
          </div>
          <div className="rounded-md border bg-muted/20 px-3 py-2">
            当前语言：{sessionDetail?.languageCode || selectedLanguage || "(未选择)"}
          </div>
        </div>

        <ScrollArea className="h-[240px] rounded-md border">
          <div className="space-y-2 p-2">
            {loadingSessions ? (
              <div className="flex items-center justify-center py-10 text-sm text-muted-foreground">
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                正在加载会话
              </div>
            ) : sessions.length === 0 ? (
              <div className="rounded-md border border-dashed px-3 py-6 text-sm text-muted-foreground">
                还没有工作台会话。先用当前分支和语言创建一个单业务流会话，再开始多轮调整。
              </div>
            ) : (
              sessions.map((session) => {
                const isSelected = session.sessionId === selectedSessionId;
                return (
                  <button
                    key={session.sessionId}
                    type="button"
                    onClick={() => onSelectSession(session.sessionId)}
                    className={cn(
                      "w-full rounded-lg border px-3 py-3 text-left transition-colors",
                      isSelected
                        ? "border-primary bg-primary/5 shadow-sm"
                        : "border-border bg-background hover:bg-muted/40"
                    )}
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <div className="truncate text-sm font-medium">
                          {session.title || session.currentDraftName || "未命名会话"}
                        </div>
                        <div className="mt-1 truncate text-xs text-muted-foreground">
                          {session.currentDraftKey || "尚无草稿 key"}
                        </div>
                      </div>
                      <Badge variant={isSelected ? "default" : "outline"}>v{session.currentVersionNumber}</Badge>
                    </div>
                    <div className="mt-2 flex flex-wrap gap-2 text-[11px] text-muted-foreground">
                      <span>{session.branchName || "未绑定分支"}</span>
                      <span>{session.languageCode || "未绑定语言"}</span>
                      <span>{session.messageCount} 条消息</span>
                    </div>
                    <div className="mt-2 text-[11px] text-muted-foreground">
                      最后活动：{formatDateTime(session.lastActivityAt)}
                    </div>
                  </button>
                );
              })
            )}
          </div>
        </ScrollArea>

        {sessionDetail?.context ? (
          <div className="space-y-3 rounded-md border bg-muted/20 p-3">
            <div className="flex items-center justify-between gap-2">
              <div className="text-sm font-medium">会话上下文</div>
              {sessionDetail.context.primaryLanguage ? (
                <Badge variant="outline">{sessionDetail.context.primaryLanguage}</Badge>
              ) : null}
            </div>
            <div className="space-y-1 text-xs text-muted-foreground">
              <div>仓库：{sessionDetail.context.repositoryName}</div>
              <div>源码位置：{sessionDetail.context.sourceLocation || "(未提供)"}</div>
            </div>
            <Accordion type="single" collapsible className="rounded-xl border bg-background/80 px-3">
              <AccordionItem value="directory-preview" className="border-none">
                <AccordionTrigger className="rounded-lg bg-slate-900 px-3 py-3 text-xs font-semibold text-slate-50 hover:bg-slate-800 hover:no-underline dark:bg-slate-100 dark:text-slate-900 dark:hover:bg-slate-200">
                  <span className="flex flex-wrap items-center gap-2">
                    <span>展开目录预览</span>
                    <Badge variant="secondary" className="bg-white/15 text-slate-50 dark:bg-slate-900 dark:text-slate-100">
                      默认隐藏
                    </Badge>
                  </span>
                </AccordionTrigger>
                <AccordionContent className="pt-3">
                  <pre className="rounded-md bg-background p-3 font-mono text-[11px] leading-5 whitespace-pre-wrap">
                    {sessionDetail.context.directoryPreview || "(无目录预览)"}
                  </pre>
                </AccordionContent>
              </AccordionItem>
            </Accordion>
            <div>
              <div className="mb-2 text-xs font-medium text-foreground">现有发现候选</div>
              <div className="space-y-2">
                {sessionDetail.context.discoveryCandidates.length === 0 ? (
                  <div className="text-xs text-muted-foreground">当前没有发现候选，可直接通过对话新建业务流模板。</div>
                ) : (
                  sessionDetail.context.discoveryCandidates.map((candidate) => (
                    <div key={candidate.key} className="rounded-md border bg-background px-3 py-2">
                      <div className="flex items-start justify-between gap-2">
                        <div className="text-sm font-medium">{candidate.name}</div>
                        <Badge variant="outline">{candidate.key}</Badge>
                      </div>
                      <div className="mt-1 text-xs text-muted-foreground">{candidate.summary}</div>
                      <div className="mt-2 text-[11px] text-muted-foreground">
                        主入口：{joinValues(candidate.triggerPoints)}
                      </div>
                      <div className="text-[11px] text-muted-foreground">
                        执行器：{joinValues(candidate.executorFiles)}
                      </div>
                      <div className="mt-2 text-[11px] text-muted-foreground">
                        证据：{joinValues(candidate.evidenceFiles)}
                      </div>
                      <div className="mt-3 flex justify-end">
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={startingCandidateKey !== null || sendingMessage || creatingSession}
                          onClick={() => void onStartDiscussionFromCandidate(candidate)}
                        >
                          {startingCandidateKey === candidate.key ? (
                            <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                          ) : (
                            <MessageSquarePlus className="mr-2 h-4 w-4" />
                          )}
                          基于该候选开始讨论
                        </Button>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>
          </div>
        ) : null}
      </Card>

      <Card className="space-y-3 p-4">
        <div className="flex items-center justify-between gap-2">
          <div>
            <h4 className="font-semibold">多轮对话</h4>
            <p className="text-xs text-muted-foreground">
              每轮会让 AI 返回新的结构化草稿，并自动沉淀成版本。
            </p>
          </div>
          {loadingSession ? <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" /> : null}
        </div>

        <ScrollArea className="h-[420px] rounded-md border">
          <div className="space-y-3 p-3">
            {!sessionDetail ? (
              <div className="rounded-md border border-dashed px-3 py-8 text-sm text-muted-foreground">
                请选择左侧会话，或者新建会话后开始描述目标业务流。
              </div>
            ) : sessionDetail.messages.length === 0 ? (
              <div className="rounded-md border border-dashed px-3 py-8 text-sm text-muted-foreground">
                这个会话还没有对话。可以直接输入“把流程限定成容器托盘入库，主入口是 xxx executor”之类的指令。
              </div>
            ) : (
              sessionDetail.messages.map((message) => {
                const isUser = message.role.toLowerCase() === "user";
                const isAssistant = message.role.toLowerCase() === "assistant";
                return (
                  <div
                    key={message.id}
                    className={cn(
                      "rounded-lg border px-3 py-3",
                      isUser && "border-primary/30 bg-primary/5",
                      isAssistant && "border-emerald-500/30 bg-emerald-500/5"
                    )}
                  >
                    <div className="mb-2 flex items-center justify-between gap-2 text-xs">
                      <div className="flex items-center gap-2">
                        {isUser ? <User className="h-3.5 w-3.5" /> : <Bot className="h-3.5 w-3.5" />}
                        <span className="font-medium">
                          {isUser ? "你" : isAssistant ? "AI 草稿助手" : "系统"}
                        </span>
                        {message.versionNumber ? (
                          <Badge variant="outline">v{message.versionNumber}</Badge>
                        ) : null}
                      </div>
                      <span className="text-muted-foreground">{formatDateTime(message.messageTimestamp)}</span>
                    </div>
                    {message.changeSummary ? (
                      <div className="mb-2 text-xs text-muted-foreground">变更摘要：{message.changeSummary}</div>
                    ) : null}
                    <div className="text-sm leading-6 whitespace-pre-wrap">{message.content}</div>
                  </div>
                );
              })
            )}
          </div>
        </ScrollArea>

        <div className="space-y-2">
          <Textarea
            value={draftMessage}
            onChange={(event) => onDraftMessageChange(event.target.value)}
            placeholder="例如：把这个流程限定成容器托盘入库；主入口是 WCS 请求落库，不要把日志重试控制器写成主入口；标题和菜单请改成中文业务名称。"
            className="min-h-[120px] min-w-0 [field-sizing:fixed] text-sm leading-6"
          />
          <div className="flex items-center justify-between gap-3">
            <p className="text-xs text-muted-foreground">
              建议一轮只提出一类修正，例如“纠正主入口”“补充状态流转”“把菜单名改成中文”。
            </p>
            <Button onClick={() => void onSendMessage()} disabled={!sessionDetail || sendingMessage}>
              {sendingMessage ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <SendHorizontal className="mr-2 h-4 w-4" />
              )}
              发送并生成新草稿
            </Button>
          </div>
        </div>
      </Card>
    </div>
  );
}

interface VersionHistoryAccordionProps {
  sessionDetail: WorkflowTemplateSessionDetail;
  isFormalConfigDirty: boolean;
  adoptingVersionNumber: number | null;
  rollingBackVersionNumber: number | null;
  onAdoptVersion: (versionNumber: number) => Promise<void>;
  onRollbackVersion: (versionNumber: number) => Promise<void>;
}

function VersionHistoryAccordion({
  sessionDetail,
  isFormalConfigDirty,
  adoptingVersionNumber,
  rollingBackVersionNumber,
  onAdoptVersion,
  onRollbackVersion,
}: VersionHistoryAccordionProps) {
  return (
    <Accordion type="single" collapsible className="rounded-xl border bg-background/80 px-4">
      <AccordionItem value="version-history" className="border-none">
        <AccordionTrigger className="py-4 hover:no-underline">
          <span className="flex flex-wrap items-center gap-2 text-left">
            <span className="text-sm font-semibold text-foreground">版本历史</span>
            <Badge variant="outline">{sessionDetail.versions.length} 个版本</Badge>
            {sessionDetail.currentVersionNumber ? (
              <Badge variant="secondary">当前 v{sessionDetail.currentVersionNumber}</Badge>
            ) : null}
            {isFormalConfigDirty ? <Badge variant="destructive">正式 JSON 未保存</Badge> : null}
          </span>
        </AccordionTrigger>
        <AccordionContent className="pt-0 pb-4">
          {sessionDetail.versions.length === 0 ? (
            <div className="rounded-md border border-dashed px-3 py-10 text-sm text-muted-foreground">
              当前会话还没有版本。
            </div>
          ) : (
            <ScrollArea className="h-[420px] pr-3">
              <div className="space-y-3">
                {sessionDetail.versions.map((version) => (
                  <VersionCard
                    key={version.id}
                    version={version}
                    isCurrent={version.versionNumber === sessionDetail.currentVersionNumber}
                    isAdopted={version.versionNumber === sessionDetail.adoptedVersionNumber}
                    formalConfigDirty={isFormalConfigDirty}
                    adopting={adoptingVersionNumber === version.versionNumber}
                    rollingBack={rollingBackVersionNumber === version.versionNumber}
                    onAdopt={(versionNumber) => void onAdoptVersion(versionNumber)}
                    onRollback={(versionNumber) => void onRollbackVersion(versionNumber)}
                  />
                ))}
              </div>
            </ScrollArea>
          )}
        </AccordionContent>
      </AccordionItem>
    </Accordion>
  );
}

interface VersionCardProps {
  version: WorkflowTemplateDraftVersion;
  isCurrent: boolean;
  isAdopted: boolean;
  formalConfigDirty: boolean;
  adopting: boolean;
  rollingBack: boolean;
  onAdopt: (versionNumber: number) => void;
  onRollback: (versionNumber: number) => void;
}

function VersionCard({
  version,
  isCurrent,
  isAdopted,
  formalConfigDirty,
  adopting,
  rollingBack,
  onAdopt,
  onRollback,
}: VersionCardProps) {
  const rollbackDisabled = rollingBack || isCurrent;
  const adoptDisabled = adopting || formalConfigDirty;

  return (
    <div
      className={cn(
        "rounded-xl border p-4",
        isCurrent && "border-primary bg-primary/5",
        isAdopted && "border-emerald-500/40 bg-emerald-500/5"
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <div className="text-sm font-semibold">v{version.versionNumber}</div>
            <Badge variant="outline">{version.sourceType}</Badge>
            {isCurrent ? <Badge>当前</Badge> : null}
            {isAdopted ? <Badge variant="secondary">已采用</Badge> : null}
          </div>
          <div className="mt-1 text-xs text-muted-foreground">{formatDateTime(version.createdAt)}</div>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            disabled={rollbackDisabled}
            onClick={() => onRollback(version.versionNumber)}
          >
            {rollingBack ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <RotateCcw className="mr-2 h-4 w-4" />
            )}
            回滚
          </Button>
          <Button size="sm" disabled={adoptDisabled} onClick={() => onAdopt(version.versionNumber)}>
            {adopting ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <CheckCheck className="mr-2 h-4 w-4" />
            )}
            采用
          </Button>
        </div>
      </div>

      <div className="mt-3 space-y-2 text-sm leading-6">
        <div>
          <span className="font-medium">草稿名称：</span>
          {version.draft.name || "(未命名)"}
        </div>
        <div>
          <span className="font-medium">草稿 Key：</span>
          {version.draft.key || "(未提供)"}
        </div>
        <div>
          <span className="font-medium">变更摘要：</span>
          {version.changeSummary || "(未提供)"}
        </div>
        <div>
          <span className="font-medium">回滚来源：</span>
          {version.basedOnVersionNumber ? `v${version.basedOnVersionNumber}` : "(无)"}
        </div>
      </div>

      <div className="mt-3 space-y-3">
        <VersionListSection title="校验结果" emptyText="无校验问题">
          {version.validationIssues.map((item) => (
            <li key={item} className="text-amber-700 dark:text-amber-300">
              {item}
            </li>
          ))}
        </VersionListSection>

        <VersionListSection title="风险提示" emptyText="无风险提示">
          {version.riskNotes.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </VersionListSection>

        <VersionListSection title="证据文件" emptyText="无证据文件">
          {version.evidenceFiles.map((item) => (
            <li key={item} className="font-mono text-[11px] break-all">
              {item}
            </li>
          ))}
        </VersionListSection>
      </div>
    </div>
  );
}

interface VersionListSectionProps {
  title: string;
  emptyText: string;
  children: React.ReactNode;
}

function VersionListSection({ title, emptyText, children }: VersionListSectionProps) {
  const items = React.Children.toArray(children);
  return (
    <div className="rounded-md border bg-background/80 p-3">
      <div className="mb-2 text-xs font-medium text-foreground">{title}</div>
      {items.length === 0 ? (
        <div className="text-xs text-muted-foreground">{emptyText}</div>
      ) : (
        <ul className="space-y-1 text-xs text-muted-foreground">{items}</ul>
      )}
    </div>
  );
}

interface AugmentResultPanelProps {
  result: WorkflowLspAugmentResult | null;
  currentDraftKey?: string | null;
  currentVersionNumber?: number | null;
}

function AugmentResultPanel({
  result,
  currentDraftKey,
  currentVersionNumber,
}: AugmentResultPanelProps) {
  if (!result) {
    return (
      <div className="rounded-xl border border-dashed px-4 py-10 text-sm text-muted-foreground">
        暂无增强结果。点击“增强当前草稿”后，这里会展示摘要、建议符号、章节切片、调用边和诊断信息。
      </div>
    );
  }

  return (
    <Tabs defaultValue="summary" className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge>{result.profileKey}</Badge>
          {currentVersionNumber ? <Badge variant="outline">当前版本 v{currentVersionNumber}</Badge> : null}
          {currentDraftKey && currentDraftKey !== result.profileKey ? (
            <Badge variant="secondary">当前草稿 Key: {currentDraftKey}</Badge>
          ) : null}
          {result.lspServerName ? <Badge variant="outline">{result.lspServerName}</Badge> : null}
        </div>
        <TabsList className="h-auto w-full flex-wrap justify-start sm:w-auto">
          <TabsTrigger value="summary">摘要</TabsTrigger>
          <TabsTrigger value="symbols">符号建议</TabsTrigger>
          <TabsTrigger value="chapters">章节切片</TabsTrigger>
          <TabsTrigger value="evidence">证据与诊断</TabsTrigger>
        </TabsList>
      </div>

      <TabsContent value="summary" className="space-y-4">
        <div className="rounded-xl border bg-muted/15 p-4 text-sm leading-6">{result.summary}</div>
        <div className="grid gap-3 lg:grid-cols-4">
          <InfoMetricCard label="增强策略" value={result.strategy || "(未提供)"} />
          <InfoMetricCard label="建议入口目录" value={String(result.suggestedEntryDirectories.length)} />
          <InfoMetricCard label="建议核心符号" value={String(result.suggestedRootSymbolNames.length)} />
          <InfoMetricCard label="章节建议数" value={String(result.suggestedChapterProfiles.length)} />
        </div>
        {result.fallbackReason ? (
          <div className="rounded-xl border border-amber-500/30 bg-amber-500/5 p-4 text-sm leading-6 text-amber-800 dark:text-amber-200">
            回退原因：{result.fallbackReason}
          </div>
        ) : null}
      </TabsContent>

      <TabsContent value="symbols" className="space-y-4">
        <div className="grid gap-4 xl:grid-cols-3">
          <StringListCard
            title="建议入口目录"
            emptyText="暂无建议入口目录"
            values={result.suggestedEntryDirectories}
            monospace
          />
          <StringListCard
            title="建议 Root Symbols"
            emptyText="暂无建议 Root Symbols"
            values={result.suggestedRootSymbolNames}
            monospace
          />
          <StringListCard
            title="建议 Must Explain"
            emptyText="暂无建议 Must Explain"
            values={result.suggestedMustExplainSymbols}
            monospace
          />
        </div>
      </TabsContent>

      <TabsContent value="chapters" className="space-y-4">
        {result.suggestedChapterProfiles.length === 0 ? (
          <div className="rounded-xl border border-dashed px-4 py-10 text-sm text-muted-foreground">
            当前增强结果没有给出章节切片建议。
          </div>
        ) : (
          <div className="space-y-3">
            {result.suggestedChapterProfiles.map((chapter) => (
              <div key={chapter.key} className="rounded-xl border p-4">
                <div className="flex flex-wrap items-center gap-2">
                  <div className="text-sm font-medium text-foreground">{chapter.title}</div>
                  <Badge variant="outline">{chapter.key}</Badge>
                  <Badge variant="secondary">{chapter.analysisMode}</Badge>
                </div>
                {chapter.description ? (
                  <div className="mt-2 text-sm leading-6 text-muted-foreground">{chapter.description}</div>
                ) : null}
                <div className="mt-3 grid gap-3 lg:grid-cols-2">
                  <StringListCard
                    title="Root Symbols"
                    emptyText="暂无 Root Symbols"
                    values={chapter.rootSymbolNames}
                    monospace
                  />
                  <StringListCard
                    title="Must Explain"
                    emptyText="暂无 Must Explain"
                    values={chapter.mustExplainSymbols}
                    monospace
                  />
                  <StringListCard
                    title="Required Sections"
                    emptyText="暂无 Required Sections"
                    values={chapter.requiredSections}
                  />
                  <StringListCard
                    title="Artifacts"
                    emptyText="暂无 Artifacts"
                    values={chapter.outputArtifacts}
                  />
                </div>
              </div>
            ))}
          </div>
        )}
      </TabsContent>

      <TabsContent value="evidence" className="space-y-4">
        <div className="grid gap-4 xl:grid-cols-2">
          <StringListCard
            title="证据文件"
            emptyText="暂无证据文件"
            values={result.evidenceFiles}
            monospace
          />
          <ResolvedLocationCard title="定义定位" emptyText="暂无定义定位" locations={result.resolvedDefinitions} />
          <ResolvedLocationCard title="引用定位" emptyText="暂无引用定位" locations={result.resolvedReferences} />
          <DiagnosticCard diagnostics={result.diagnostics} />
        </div>

        <div className="rounded-xl border p-4">
          <div className="mb-3 text-sm font-medium text-foreground">调用边</div>
          {result.callHierarchyEdges.length === 0 ? (
            <div className="text-sm text-muted-foreground">暂无调用边。</div>
          ) : (
            <div className="space-y-2">
              {result.callHierarchyEdges.map((edge, index) => (
                <div key={`${edge.fromSymbol}-${edge.toSymbol}-${index}`} className="rounded-md border bg-muted/10 p-3">
                  <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                    <Badge variant="outline">{edge.kind}</Badge>
                    <span className="font-mono">{edge.fromSymbol}</span>
                    <span>→</span>
                    <span className="font-mono">{edge.toSymbol}</span>
                  </div>
                  {edge.reason ? <div className="mt-1 text-xs leading-5 text-muted-foreground">{edge.reason}</div> : null}
                </div>
              ))}
            </div>
          )}
        </div>
      </TabsContent>
    </Tabs>
  );
}

function InfoMetricCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border bg-background px-4 py-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 text-sm font-medium text-foreground">{value}</div>
    </div>
  );
}

function StringListCard({
  title,
  values,
  emptyText,
  monospace = false,
}: {
  title: string;
  values: string[];
  emptyText: string;
  monospace?: boolean;
}) {
  return (
    <div className="rounded-xl border p-4">
      <div className="mb-3 text-sm font-medium text-foreground">{title}</div>
      {values.length === 0 ? (
        <div className="text-sm text-muted-foreground">{emptyText}</div>
      ) : (
        <div className="space-y-2">
          {values.map((value) => (
            <div
              key={`${title}-${value}`}
              className={cn(
                "rounded-md border bg-muted/10 px-3 py-2 text-sm text-muted-foreground",
                monospace && "font-mono text-xs break-all"
              )}
            >
              {value}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function ResolvedLocationCard({
  title,
  locations,
  emptyText,
}: {
  title: string;
  locations: WorkflowLspAugmentResult["resolvedDefinitions"];
  emptyText: string;
}) {
  return (
    <div className="rounded-xl border p-4">
      <div className="mb-3 text-sm font-medium text-foreground">{title}</div>
      {locations.length === 0 ? (
        <div className="text-sm text-muted-foreground">{emptyText}</div>
      ) : (
        <div className="space-y-2">
          {locations.map((location, index) => (
            <div key={`${location.filePath}-${location.lineNumber ?? 0}-${index}`} className="rounded-md border bg-muted/10 p-3">
              <div className="text-xs font-medium text-foreground">
                {location.symbolName || location.source || "未命名符号"}
              </div>
              <div className="mt-1 font-mono text-xs leading-5 text-muted-foreground break-all">
                {location.filePath}
                {location.lineNumber ? `:${location.lineNumber}` : ""}
                {location.columnNumber ? `:${location.columnNumber}` : ""}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function DiagnosticCard({ diagnostics }: { diagnostics: WorkflowLspAugmentResult["diagnostics"] }) {
  return (
    <div className="rounded-xl border p-4">
      <div className="mb-3 text-sm font-medium text-foreground">诊断信息</div>
      {diagnostics.length === 0 ? (
        <div className="text-sm text-muted-foreground">暂无诊断信息。</div>
      ) : (
        <div className="space-y-2">
          {diagnostics.map((diagnostic, index) => (
            <div key={`${diagnostic.level}-${diagnostic.message}-${index}`} className="rounded-md border bg-muted/10 p-3">
              <div className="flex items-center gap-2 text-xs text-muted-foreground">
                <Badge variant={diagnostic.level === "error" ? "destructive" : "outline"}>{diagnostic.level}</Badge>
              </div>
              <div className="mt-1 text-sm leading-6 text-foreground">{diagnostic.message}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

interface AnalysisLivePreviewProps {
  detail: WorkflowAnalysisSessionDetail | null;
  status?: string | null;
  currentTaskTitle?: string | null;
  logs: WorkflowAnalysisLog[];
  diffs: AnalysisArtifactDiffPreview[];
}

interface AnalysisArtifactDiffPreview {
  id: string;
  title: string;
  diffKind: string;
  generatedBy: string;
  chapterKey?: string;
  branchRoot?: string;
  taskTitle?: string;
  baselinePreview?: string;
  currentPreview: string;
}

function AnalysisLivePreview({
  detail,
  status,
  currentTaskTitle,
  logs,
  diffs,
}: AnalysisLivePreviewProps) {
  if (!detail) {
    return (
      <div className="rounded-md border border-primary/20 bg-primary/5 px-3 py-3 text-xs leading-5 text-muted-foreground">
        还没有 ACP 深挖会话。发起后，这里会直接显示 `task_runner` 当前任务、实时 AI 日志和最近产物差异，不再只是静态提示。
      </div>
    );
  }

  const scopeLabel = detail.chapterKey ? "章节聚焦" : "整条业务流";

  return (
    <div className="space-y-3 rounded-md border border-primary/20 bg-primary/5 px-3 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="text-xs font-medium text-foreground">ACP 实时执行视图</div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge>{status || detail.status}</Badge>
          <Badge variant="outline">{scopeLabel}</Badge>
          {shouldPollAnalysisSession(status || detail.status) ? (
            <Badge variant="secondary">每 3 秒刷新</Badge>
          ) : null}
        </div>
      </div>

      <div className="grid gap-2 text-xs text-muted-foreground md:grid-cols-4">
        <div className="rounded-md border bg-background/80 px-3 py-2">
          当前任务：{currentTaskTitle || detail.currentTaskId || "(等待调度)"}
        </div>
        <div className="rounded-md border bg-background/80 px-3 py-2">
          运行中：{detail.runningTaskCount} / 失败：{detail.failedTasks}
        </div>
        <div className="rounded-md border bg-background/80 px-3 py-2">
          已完成：{detail.completedTasks} / {detail.totalTasks}
        </div>
        <div className="rounded-md border bg-background/80 px-3 py-2">
          进度：{detail.progressMessage || "(暂无)"}
        </div>
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        <div className="rounded-md border bg-background/80 p-3">
          <div className="mb-2 flex items-center gap-2 text-xs font-medium text-foreground">
            <Bot className="h-3.5 w-3.5" />
            最近日志
          </div>
          {logs.length === 0 ? (
            <div className="text-xs leading-5 text-muted-foreground">
              当前还没有实时日志，一旦 `task_runner` 开始规划、调用 AI 或回填产物，这里会立即刷新。
            </div>
          ) : (
            <div className="space-y-2">
              {logs.map((log) => (
                <div key={log.id} className="rounded-md border bg-muted/10 px-3 py-2">
                  <div className="flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
                    <Badge variant={log.level === "ai" ? "default" : "outline"}>{log.level}</Badge>
                    <span>{formatDateTime(log.createdAt)}</span>
                  </div>
                  <div className="mt-1 text-xs leading-5 text-foreground">{log.message}</div>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="rounded-md border bg-background/80 p-3">
          <div className="mb-2 flex items-center gap-2 text-xs font-medium text-foreground">
            <Sparkles className="h-3.5 w-3.5" />
            最近 AI 产物差异
          </div>
          {diffs.length === 0 ? (
            <div className="text-xs leading-5 text-muted-foreground">
              暂无 AI 产物差异。章节或分支摘要一旦被 AI 新建或覆盖，这里会先显示摘要差异，下方会话面板再给完整内容。
            </div>
          ) : (
            <div className="space-y-2">
              {diffs.map((diff) => (
                <div key={diff.id} className="rounded-md border bg-muted/10 px-3 py-2">
                  <div className="flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
                    <Badge>{diff.generatedBy}</Badge>
                    <Badge variant="outline">{diff.diffKind}</Badge>
                    {diff.taskTitle ? <span>任务：{diff.taskTitle}</span> : null}
                    {diff.chapterKey ? <span>章节：{diff.chapterKey}</span> : null}
                    {diff.branchRoot ? <span>分支：{diff.branchRoot}</span> : null}
                  </div>
                  <div className="mt-1 text-xs font-medium text-foreground">{diff.title}</div>
                  {diff.baselinePreview ? (
                    <div className="mt-1 text-xs leading-5 text-muted-foreground">
                      基线：{diff.baselinePreview}
                    </div>
                  ) : null}
                  <div className="mt-1 text-xs leading-5 text-foreground">
                    当前：{diff.currentPreview}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function buildRecentAnalysisArtifactDiffs(
  artifacts: WorkflowAnalysisArtifact[],
  taskTitleById: Map<string, string>
) {
  return [...artifacts]
    .filter((artifact) => shouldShowAnalysisArtifactDiff(artifact))
    .sort((left, right) => new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime())
    .slice(0, 3)
    .map((artifact) => {
      const metadata = artifact.metadata ?? {};
      return {
        id: artifact.id,
        title: artifact.title,
        diffKind: metadata.diffKind || "updated",
        generatedBy: metadata.generatedBy || "task-runner",
        chapterKey: metadata.chapterKey,
        branchRoot: metadata.branchRoot,
        taskTitle: artifact.taskId ? taskTitleById.get(artifact.taskId) : undefined,
        baselinePreview: buildArtifactPreview(metadata.baselineContent),
        currentPreview: buildArtifactPreview(artifact.content),
      };
    });
}

function shouldShowAnalysisArtifactDiff(artifact: WorkflowAnalysisArtifact) {
  const metadata = artifact.metadata ?? {};
  if (metadata.generatedBy === "task-runner-ai") {
    return true;
  }

  return metadata.diffKind !== undefined && metadata.diffKind !== "unchanged";
}

function buildArtifactPreview(content?: string | null, maxLength: number = 120) {
  if (!content) {
    return undefined;
  }

  const normalized = content.replace(/\s+/g, " ").trim();
  if (!normalized) {
    return undefined;
  }

  return normalized.length <= maxLength ? normalized : `${normalized.slice(0, maxLength)}...`;
}

function mergeAnalysisSessionSummary(
  current: WorkflowAnalysisSessionSummary[],
  detail: WorkflowAnalysisSessionDetail
): WorkflowAnalysisSessionSummary[] {
  const nextSummary: WorkflowAnalysisSessionSummary = {
    analysisSessionId: detail.analysisSessionId,
    repositoryId: detail.repositoryId,
    workflowTemplateSessionId: detail.workflowTemplateSessionId,
    profileKey: detail.profileKey,
    draftVersionNumber: detail.draftVersionNumber,
    chapterKey: detail.chapterKey,
    status: detail.status,
    objective: detail.objective,
    summary: detail.summary,
    totalTasks: detail.totalTasks,
    completedTasks: detail.completedTasks,
    failedTasks: detail.failedTasks,
    pendingTaskCount: detail.pendingTaskCount,
    runningTaskCount: detail.runningTaskCount,
    currentTaskId: detail.currentTaskId,
    progressMessage: detail.progressMessage,
    createdAt: detail.createdAt,
    startedAt: detail.startedAt,
    completedAt: detail.completedAt,
    lastActivityAt: detail.lastActivityAt,
  };

  const withoutCurrent = current.filter((item) => item.analysisSessionId !== detail.analysisSessionId);
  return [nextSummary, ...withoutCurrent].sort(
    (left, right) => new Date(right.lastActivityAt).getTime() - new Date(left.lastActivityAt).getTime()
  );
}

function shouldPollAnalysisSession(status?: string | null) {
  if (!status) {
    return false;
  }

  return status === "Queued" || status === "Running" || status === "Composing";
}

function mergeSessionSummary(
  current: WorkflowTemplateSessionSummary[],
  detail: WorkflowTemplateSessionDetail
): WorkflowTemplateSessionSummary[] {
  const nextSummary: WorkflowTemplateSessionSummary = {
    sessionId: detail.sessionId,
    repositoryId: detail.repositoryId,
    status: detail.status,
    title: detail.title,
    branchId: detail.branchId,
    branchName: detail.branchName,
    languageCode: detail.languageCode,
    currentDraftKey: detail.currentDraftKey,
    currentDraftName: detail.currentDraftName,
    currentVersionNumber: detail.currentVersionNumber,
    adoptedVersionNumber: detail.adoptedVersionNumber,
    messageCount: detail.messageCount,
    lastActivityAt: detail.lastActivityAt,
    createdAt: detail.createdAt,
  };

  const withoutCurrent = current.filter((item) => item.sessionId !== detail.sessionId);
  return [nextSummary, ...withoutCurrent].sort(
    (left, right) => new Date(right.lastActivityAt).getTime() - new Date(left.lastActivityAt).getTime()
  );
}

function joinValues(values: string[] | undefined | null, emptyText: string = "(未提供)") {
  if (!values || values.length === 0) {
    return emptyText;
  }

  return values.join("，");
}

function formatDateTime(value?: string | null) {
  if (!value) {
    return "(未知)";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : undefined;
}

function buildCandidateSeedPrompt(
  candidate: WorkflowTemplateDiscoveryCandidate
) {
  const lines = [
    `请基于当前仓库上下文，围绕候选业务流“${candidate.name}”(${candidate.key}) 创建一份新的单业务流 Workflow 草稿。`,
    "要求：",
    "1. 只聚焦这一条业务流，不要混入其他流程、工具类或日志重试功能。",
    "2. 标题、菜单名称、description 都使用清晰中文业务名称。",
    `3. 候选摘要：${candidate.summary || "(无)"}`,
    `4. 候选主入口：${joinValues(candidate.triggerPoints)}`,
    `5. 候选补偿入口：${joinValues(candidate.compensationTriggerPoints)}`,
    `6. 候选请求实体：${joinValues(candidate.requestEntities)}`,
    `7. 候选调度文件：${joinValues(candidate.schedulerFiles)}`,
    `8. 候选执行器文件：${joinValues(candidate.executorFiles)}`,
    `9. 优先参考这些证据文件：${joinValues(candidate.evidenceFiles)}`,
    "10. 如果候选证据不足，请在 riskNotes 里明确写出不确定点，但仍先给出一版可落地的草稿。",
  ];

  return lines.join("\n");
}
