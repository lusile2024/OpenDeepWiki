"use client";

import React, { useEffect, useMemo, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Loader2, Save, Eye, Sparkles } from "lucide-react";
import { toast } from "sonner";
import type {
  OverlayIndex,
  OverlaySuggestResponse,
  RepositoryOverlayConfig,
} from "@/types/overlay";
import {
  generateRepositoryOverlayWiki,
  getRepositoryOverlayConfig,
  previewRepositoryOverlay,
  saveRepositoryOverlayConfig,
  suggestRepositoryOverlayConfig,
} from "@/lib/admin-api";

export function RepositoryOverlayPanel({ repositoryId }: { repositoryId: string }) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [generating, setGenerating] = useState(false);
  const [suggesting, setSuggesting] = useState(false);

  const [config, setConfig] = useState<RepositoryOverlayConfig | null>(null);
  const [rawJson, setRawJson] = useState("");
  const [selectedProfileKey, setSelectedProfileKey] = useState<string>("");
  const [selectedCandidateKey, setSelectedCandidateKey] = useState<string>("");
  const [suggestionPrompt, setSuggestionPrompt] = useState(
    "请识别仓库里的基础版与项目版覆盖规则。项目版通常在特定目录段下，通过移除该目录段映射到基础版文件，只展示项目新增和覆盖文件。"
  );

  const [preview, setPreview] = useState<OverlayIndex | null>(null);
  const [suggestion, setSuggestion] = useState<OverlaySuggestResponse | null>(null);

  const profiles = useMemo(() => config?.profiles ?? [], [config]);
  const selectedProfile = useMemo(() => {
    if (!config) return null;
    const key = selectedProfileKey || config.activeProfileKey || profiles[0]?.key;
    return profiles.find((p) => p.key === key) ?? profiles[0] ?? null;
  }, [config, profiles, selectedProfileKey]);
  const variantCandidates = useMemo(() => suggestion?.analysis.variantCandidates ?? [], [suggestion]);
  const selectedCandidate = useMemo(() => {
    if (!variantCandidates.length) {
      return null;
    }

    return variantCandidates.find((candidate) => candidate.key === selectedCandidateKey) ?? variantCandidates[0];
  }, [selectedCandidateKey, variantCandidates]);

  useEffect(() => {
    if (!repositoryId) return;
    const run = async () => {
      setLoading(true);
      try {
        const cfg = await getRepositoryOverlayConfig(repositoryId);
        setConfig(cfg);
        setRawJson(JSON.stringify(cfg, null, 2));
        setSelectedProfileKey(cfg.activeProfileKey || cfg.profiles?.[0]?.key || "");
      } catch (e) {
        console.error(e);
        toast.error("加载 Overlay 配置失败");
      } finally {
        setLoading(false);
      }
    };
    run();
  }, [repositoryId]);

  useEffect(() => {
    if (!variantCandidates.length) {
      if (selectedCandidateKey) {
        setSelectedCandidateKey("");
      }
      return;
    }

    if (!variantCandidates.some((candidate) => candidate.key === selectedCandidateKey)) {
      setSelectedCandidateKey(variantCandidates[0].key);
    }
  }, [selectedCandidateKey, variantCandidates]);

  const parseJson = (): RepositoryOverlayConfig | null => {
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
      const saved = await saveRepositoryOverlayConfig(repositoryId, parsed);
      setConfig(saved);
      setRawJson(JSON.stringify(saved, null, 2));
      setSelectedProfileKey(saved.activeProfileKey || saved.profiles?.[0]?.key || "");
      toast.success("Overlay 配置已保存");
    } catch (e: unknown) {
      console.error(e);
      toast.error(getErrorMessage(e) || "保存失败");
    } finally {
      setSaving(false);
    }
  };

  const handleSuggest = async () => {
    if (!repositoryId) return;

    setSuggesting(true);
    try {
      const data = await suggestRepositoryOverlayConfig(repositoryId, {
        userIntent: suggestionPrompt,
        baseBranchName: selectedProfile?.baseBranchName,
        maxVariants: 3,
        maxSamplesPerVariant: 8,
      });
      setSuggestion(data);
      setConfig(data.suggestedConfig);
      setRawJson(JSON.stringify(data.suggestedConfig, null, 2));
      setSelectedProfileKey(data.suggestedConfig.activeProfileKey || data.suggestedConfig.profiles?.[0]?.key || "");
      setPreview(null);
      toast.success(data.usedAi ? "AI 已生成建议配置并填入编辑器" : "已生成结构分析建议并填入编辑器");
    } catch (e: unknown) {
      console.error(e);
      toast.error(getErrorMessage(e) || "AI 分析失败");
    } finally {
      setSuggesting(false);
    }
  };

  const handlePreview = async () => {
    if (!repositoryId) return;
    const profileKey = selectedProfile?.key;
    setPreviewing(true);
    try {
      const data = await previewRepositoryOverlay(repositoryId, profileKey);
      setPreview(data);
      toast.success(`预览完成：覆盖 ${data.summary.overrideCount}，新增 ${data.summary.addedCount}`);
    } catch (e: unknown) {
      console.error(e);
      toast.error(getErrorMessage(e) || "预览失败");
    } finally {
      setPreviewing(false);
    }
  };

  const handleGenerate = async () => {
    if (!repositoryId) return;
    const profileKey = selectedProfile?.key;
    setGenerating(true);
    try {
      const result = await generateRepositoryOverlayWiki(repositoryId, profileKey);
      toast.success(
        `生成完成：分支 ${result.overlayBranchName}（覆盖 ${result.summary.overrideCount}，新增 ${result.summary.addedCount}）`
      );
    } catch (e: unknown) {
      console.error(e);
      toast.error(getErrorMessage(e) || "生成失败");
    } finally {
      setGenerating(false);
    }
  };

  return (
    <Card className="space-y-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="font-semibold">Overlay Wiki</h3>
          <p className="text-xs text-muted-foreground">
            通过可配置规则识别“项目新增/覆盖”文件，并生成到 overlay/* 虚拟分支。现在支持先让 AI 基于路径结构生成建议配置。
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" onClick={handleSuggest} disabled={loading || suggesting}>
            {suggesting ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Sparkles className="mr-2 h-4 w-4" />}
            AI 分析生成配置
          </Button>
          <Button variant="outline" onClick={handlePreview} disabled={loading || previewing || generating}>
            {previewing ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Eye className="mr-2 h-4 w-4" />}
            预览差异
          </Button>
          <Button onClick={handleGenerate} disabled={loading || previewing || generating}>
            {generating ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Sparkles className="mr-2 h-4 w-4" />}
            生成 Overlay Wiki
          </Button>
          <Button variant="secondary" onClick={handleSave} disabled={loading || saving}>
            {saving ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}
            保存配置
          </Button>
        </div>
      </div>

      {loading ? (
        <div className="flex items-center justify-center py-10">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : (
        <>
          <div className="grid gap-3 md:grid-cols-2">
            <Card className="p-3">
              <div className="mb-2 flex items-center justify-between">
                <p className="text-xs text-muted-foreground">当前 Profile</p>
                {selectedProfile?.key && <Badge variant="outline">{selectedProfile.key}</Badge>}
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
                  {profiles.map((p) => (
                    <SelectItem key={p.key} value={p.key}>
                      {p.name} ({p.key})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {selectedProfile && (
                <div className="mt-3 space-y-1 text-xs text-muted-foreground">
                  <div>基础分支：{selectedProfile.baseBranchName}</div>
                  <div>虚拟分支模板：{selectedProfile.overlayBranchNameTemplate}</div>
                  <div>Roots：{selectedProfile.roots?.join(", ") || "(全仓库)"}</div>
                  <div>Variants：{selectedProfile.variants?.map((v) => v.key).join(", ") || "(无)"}</div>
                  <div>DiffMode：{selectedProfile.generation?.diffMode}</div>
                </div>
              )}
            </Card>

            <Card className="p-3">
              <p className="mb-2 text-xs text-muted-foreground">预览结果</p>
              {preview ? (
                <div className="space-y-2">
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="secondary">覆盖 {preview.summary.overrideCount}</Badge>
                    <Badge variant="secondary">新增 {preview.summary.addedCount}</Badge>
                    <Badge variant="outline">合计 {preview.summary.totalCount}</Badge>
                  </div>
                  {preview.isCapped ? (
                    <div className="text-xs text-muted-foreground">
                      当前预览已按配置的 maxFiles
                      {typeof preview.maxFilesApplied === "number" ? `=${preview.maxFilesApplied}` : ""}
                      进行了截断，用于控制 UI 速度和模型成本。
                      {preview.uncappedSummary
                        ? ` 截断前为：覆盖 ${preview.uncappedSummary.overrideCount}，新增 ${preview.uncappedSummary.addedCount}，合计 ${preview.uncappedSummary.totalCount}。`
                        : ""}
                    </div>
                  ) : null}
                </div>
              ) : (
                <div className="text-sm text-muted-foreground">尚未预览</div>
              )}
            </Card>
          </div>

          <Card className="p-3">
            <p className="mb-2 text-xs text-muted-foreground">让 AI 先分析仓库结构和命名约定</p>
            <textarea
              value={suggestionPrompt}
              onChange={(e) => setSuggestionPrompt(e.target.value)}
              className="h-28 w-full resize-none rounded-md border bg-background p-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
              placeholder="例如：src/Domain 和 src/Application 下，1397 目录表示项目版，移除该目录段映射基础版，只展示项目新增和覆盖文件。"
            />
            <p className="mt-2 text-xs text-muted-foreground">
              建议把你的命名约定、项目版目录规则、是否只展示新增/覆盖、Diff 偏好写在这里。点击“AI 分析生成配置”后，只会把建议写入下方 JSON 编辑器，不会自动保存。
            </p>
          </Card>

          {suggestion && (
            <Card className="space-y-3 p-3">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={suggestion.usedAi ? "default" : "secondary"}>
                  {suggestion.usedAi ? `AI 建议 / ${suggestion.model}` : "启发式建议"}
                </Badge>
                <Badge variant="outline">基础分支 {suggestion.baseBranchName}</Badge>
                {suggestion.detectedPrimaryLanguage && (
                  <Badge variant="outline">语言 {suggestion.detectedPrimaryLanguage}</Badge>
                )}
              </div>

              <div className="space-y-2">
                <div>
                  <div className="mb-1 text-xs text-muted-foreground">建议摘要</div>
                  <div className="text-sm">{suggestion.summary || "暂无摘要"}</div>
                </div>
                <div>
                  <div className="mb-1 text-xs text-muted-foreground">规则判断依据</div>
                  <div className="text-sm text-muted-foreground">
                    {suggestion.reasoningSummary || "暂无额外解释"}
                  </div>
                </div>
              </div>

              {suggestion.warnings.length > 0 && (
                <div className="space-y-1">
                  <div className="text-xs text-muted-foreground">注意事项</div>
                  {suggestion.warnings.map((warning) => (
                    <div key={warning} className="text-xs text-amber-700 dark:text-amber-400">
                      {warning}
                    </div>
                  ))}
                </div>
              )}

              <div className="space-y-3">
                <Card className="p-3">
                  <div className="mb-2 text-xs text-muted-foreground">建议 Roots / IncludeGlobs</div>
                  <div className="space-y-1 text-xs">
                    <div>Roots：{suggestion.analysis.suggestedRoots.join(", ") || "(未明确识别)"}</div>
                    <div>
                      IncludeGlobs：
                      {suggestion.analysis.suggestedIncludeGlobs.join(", ") || "(保持全部文件)"}
                    </div>
                  </div>
                </Card>

                <Card className="p-3">
                  <div className="mb-2 text-xs text-muted-foreground">候选项目版目录</div>
                  <div className="mb-3 rounded-md border bg-muted/30 p-2 text-xs text-muted-foreground">
                    先看表格做横向比较：每一行表示“如果把这个目录段当成项目版标识，预计会识别出多少覆盖和新增文件”。
                    点击某一行后，下方会展开这条候选的详细说明和映射样例。
                  </div>
                  {variantCandidates.length > 0 ? (
                    <div className="space-y-3">
                      <div className="overflow-x-auto rounded-md border">
                        <table className="min-w-full border-collapse text-xs">
                          <thead className="bg-muted/40 text-muted-foreground">
                            <tr>
                              <th className="px-3 py-2 text-left font-medium">推荐</th>
                              <th className="px-3 py-2 text-left font-medium">候选目录</th>
                              <th className="px-3 py-2 text-left font-medium">预计覆盖</th>
                              <th className="px-3 py-2 text-left font-medium">预计新增</th>
                              <th className="px-3 py-2 text-left font-medium">常见位置</th>
                              <th className="px-3 py-2 text-left font-medium">样例映射</th>
                            </tr>
                          </thead>
                          <tbody>
                            {variantCandidates.map((candidate, index) => {
                              const isSelected = selectedCandidate?.key === candidate.key;
                              const firstSample = candidate.overrideSamples[0];
                              return (
                                <tr
                                  key={candidate.key}
                                  className={`cursor-pointer border-t align-top transition-colors ${
                                    isSelected ? "bg-emerald-50/80 dark:bg-emerald-950/20" : "hover:bg-muted/30"
                                  }`}
                                  onClick={() => setSelectedCandidateKey(candidate.key)}
                                >
                                  <td className="px-3 py-2">
                                    <Badge variant={index === 0 ? "default" : "outline"}>
                                      {index === 0 ? "当前推荐" : `备选 ${index + 1}`}
                                    </Badge>
                                  </td>
                                  <td className="px-3 py-2">
                                    <div className="font-medium">{candidate.key}</div>
                                  </td>
                                  <td className="px-3 py-2">{candidate.overrideCount}</td>
                                  <td className="px-3 py-2">{candidate.addedCount}</td>
                                  <td className="px-3 py-2 text-muted-foreground">
                                    {formatList(candidate.roots, "(未识别)", 3)}
                                  </td>
                                  <td className="px-3 py-2 text-muted-foreground">
                                    {firstSample ? (
                                      <div className="max-w-[420px] space-y-1">
                                        <div className="break-all font-mono text-[11px]">{firstSample.projectPath}</div>
                                        <div className="text-[11px]">→</div>
                                        <div className="break-all font-mono text-[11px]">{firstSample.basePath}</div>
                                      </div>
                                    ) : (
                                      "暂无样例"
                                    )}
                                  </td>
                                </tr>
                              );
                            })}
                          </tbody>
                        </table>
                      </div>

                      {selectedCandidate && (
                        <div className="rounded-md border p-3">
                          <div className="mb-2 flex flex-wrap items-center gap-2">
                            <Badge variant="secondary">{selectedCandidate.key}</Badge>
                            {variantCandidates[0]?.key === selectedCandidate.key ? (
                              <Badge variant="default">当前推荐</Badge>
                            ) : (
                              <Badge variant="outline">备选</Badge>
                            )}
                            <span className="text-xs text-muted-foreground">
                              内部排序分 {selectedCandidate.score}
                            </span>
                          </div>
                          <div className="space-y-2 text-xs">
                            <div className="text-muted-foreground">
                              系统判断：把目录段 <span className="font-medium text-foreground">{selectedCandidate.key}</span> 当成项目版标识时，
                              常见位置包括 {formatList(selectedCandidate.roots, "(未识别)", 5)}。
                            </div>
                            <div className="flex flex-wrap gap-2">
                              <Badge variant="secondary">预计覆盖 {selectedCandidate.overrideCount}</Badge>
                              <Badge variant="secondary">预计新增 {selectedCandidate.addedCount}</Badge>
                            </div>
                            <div className="text-[11px] text-muted-foreground">
                              建议先根据下面的映射样例判断它是不是你真正想要的“项目版目录”。
                            </div>
                            <div className="space-y-2">
                              {selectedCandidate.overrideSamples.slice(0, 3).map((sample) => (
                                <div
                                  key={`${sample.projectPath}:${sample.basePath}`}
                                  className="rounded-md border bg-muted/20 p-2"
                                >
                                  <div className="text-[11px] text-muted-foreground">项目版文件</div>
                                  <div className="break-all font-mono text-[11px]">{sample.projectPath}</div>
                                  <div className="mt-2 text-[11px] text-muted-foreground">系统推断的基础版文件</div>
                                  <div className="break-all font-mono text-[11px]">{sample.basePath}</div>
                                </div>
                              ))}
                              {selectedCandidate.overrideSamples.length === 0 && (
                                <div className="text-[11px] text-muted-foreground">暂无可展示的映射样例</div>
                              )}
                            </div>
                            {variantCandidates[0]?.key === selectedCandidate.key ? (
                              <div className="text-[11px] text-emerald-700 dark:text-emerald-400">
                                这是当前推荐候选。可以直接点“预览差异”确认识别出来的覆盖/新增文件是否符合预期。
                              </div>
                            ) : (
                              <div className="text-[11px] text-muted-foreground">
                                这是备选候选。如果它的映射样例明显不符合你的项目目录规则，可以忽略。
                              </div>
                            )}
                          </div>
                        </div>
                      )}
                    </div>
                  ) : (
                    <div className="text-sm text-muted-foreground">暂无候选项目版目录</div>
                  )}
                </Card>
              </div>
            </Card>
          )}

          <Card className="p-3">
            <p className="mb-2 text-xs text-muted-foreground">Overlay 配置（JSON，可继续人工微调）</p>
            <textarea
              value={rawJson}
              onChange={(e) => setRawJson(e.target.value)}
              className="h-[380px] w-full resize-none rounded-md border bg-background p-3 font-mono text-xs leading-5 outline-none focus-visible:ring-2 focus-visible:ring-ring"
            />
            <p className="mt-2 text-xs text-muted-foreground">
              约束：overlayBranchNameTemplate 必须以 overlay/ 开头，否则会影响后台 Worker 的分支跳过逻辑。
            </p>
          </Card>

          {preview && (
            <div className="grid gap-3 md:grid-cols-2">
              <Card className="p-3">
                <p className="mb-2 text-xs text-muted-foreground">覆盖文件（Top 20）</p>
                <div className="max-h-[260px] space-y-1 overflow-auto pr-1">
                  {preview.overrides.slice(0, 20).map((item) => (
                    <div key={`${item.variantKey}:${item.projectPath}`} className="text-xs">
                      <Badge variant="secondary" className="mr-2">
                        {item.variantKey}
                      </Badge>
                      <span className="font-mono">{item.displayPath}</span>
                    </div>
                  ))}
                  {preview.overrides.length === 0 && <div className="text-sm text-muted-foreground">无覆盖文件</div>}
                </div>
              </Card>
              <Card className="p-3">
                <p className="mb-2 text-xs text-muted-foreground">新增文件（Top 20）</p>
                <div className="max-h-[260px] space-y-1 overflow-auto pr-1">
                  {preview.added.slice(0, 20).map((item) => (
                    <div key={`${item.variantKey}:${item.projectPath}`} className="text-xs">
                      <Badge variant="secondary" className="mr-2">
                        {item.variantKey}
                      </Badge>
                      <span className="font-mono">{item.displayPath}</span>
                    </div>
                  ))}
                  {preview.added.length === 0 && <div className="text-sm text-muted-foreground">无新增文件</div>}
                </div>
              </Card>
            </div>
          )}
        </>
      )}
    </Card>
  );
}

function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : undefined;
}

function formatList(values: string[], fallback: string, maxItems = 5) {
  if (!values.length) {
    return fallback;
  }

  if (values.length <= maxItems) {
    return values.join(", ");
  }

  return `${values.slice(0, maxItems).join(", ")} 等 ${values.length} 处`;
}
