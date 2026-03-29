"use client";

import React from "react";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { ScrollArea } from "@/components/ui/scroll-area";
import type {
  WorkflowAnalysisArtifact,
  WorkflowAnalysisSessionDetail,
  WorkflowAnalysisSessionSummary,
  WorkflowAnalysisTask,
  WorkflowChapterProfile,
} from "@/types/workflow-config";
import { Bot, GitBranchPlus, ListTree, Logs, Network, Route, Sparkles } from "lucide-react";

interface WorkflowAnalysisPanelProps {
  sessions: WorkflowAnalysisSessionSummary[];
  selectedSessionId?: string | null;
  detail?: WorkflowAnalysisSessionDetail | null;
  chapterProfiles: WorkflowChapterProfile[];
  onSelectSession: (analysisSessionId: string) => void;
}

interface ArtifactDiffEntry {
  artifact: WorkflowAnalysisArtifact;
  diffKind: string;
  generatedBy: string;
  baselineContent?: string;
  baselineTitle?: string;
  baselineContentFormat?: string;
  chapterKey?: string;
  branchRoot?: string;
  taskTitle?: string;
}

export function WorkflowAnalysisPanel({
  sessions,
  selectedSessionId,
  detail,
  chapterProfiles,
  onSelectSession,
}: WorkflowAnalysisPanelProps) {
  const resolveChapterLabel = (chapterKey?: string | null) => {
    if (!chapterKey) {
      return "整条业务流";
    }

    return chapterProfiles.find((chapter) => chapter.key === chapterKey)?.title || chapterKey;
  };

  const taskTitleById = React.useMemo(() => {
    return new Map(detail?.tasks.map((task) => [task.id, task.title]) ?? []);
  }, [detail?.tasks]);

  const selectedChapter =
    (detail?.chapterKey && chapterProfiles.find((chapter) => chapter.key === detail.chapterKey)) || null;
  const selectedScopeLabel = detail?.chapterKey
    ? selectedChapter?.title || detail.chapterKey
    : "整条业务流";
  const currentTaskTitle = detail?.currentTaskId ? taskTitleById.get(detail.currentTaskId) : null;

  const overviewArtifact = detail?.artifacts.find((artifact) => artifact.artifactType === "analysis-overview");
  const flowcharts = detail?.artifacts.filter((artifact) => artifact.artifactType === "flowchart") ?? [];
  const mindmaps = detail?.artifacts.filter((artifact) => artifact.artifactType === "mindmap") ?? [];
  const additionalArtifacts = React.useMemo(
    () =>
      detail?.artifacts.filter(
        (artifact) =>
          artifact.artifactType !== "analysis-overview" &&
          artifact.artifactType !== "flowchart" &&
          artifact.artifactType !== "mindmap"
      ) ?? [],
    [detail?.artifacts]
  );
  const artifactDiffs = React.useMemo(
    () =>
      additionalArtifacts
        .map((artifact) => toArtifactDiffEntry(artifact, taskTitleById))
        .filter((item) => shouldShowArtifactDiff(item)),
    [additionalArtifacts, taskTitleById]
  );

  return (
    <div className="space-y-4">
      <Card className="space-y-3 p-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h4 className="font-semibold">ACP 深挖会话</h4>
            <p className="text-xs text-muted-foreground">
              会话持久化保存，可持续查看整条业务流或单章聚焦分析的任务拆分、实时日志、AI 覆盖结果与图种子。
            </p>
          </div>
          <Badge variant="outline">{sessions.length} 条</Badge>
        </div>

        {sessions.length === 0 ? (
          <div className="rounded-md border border-dashed px-3 py-8 text-sm text-muted-foreground">
            还没有深挖会话。先对当前草稿做增强，再发起一次整条业务流 ACP 深挖。
          </div>
        ) : (
          <div className="grid gap-2">
            {sessions.map((session) => {
              const isSelected = session.analysisSessionId === selectedSessionId;
              return (
                <button
                  key={session.analysisSessionId}
                  type="button"
                  onClick={() => onSelectSession(session.analysisSessionId)}
                  className={`rounded-lg border px-3 py-3 text-left transition-colors ${
                    isSelected
                      ? "border-primary bg-primary/5 shadow-sm"
                      : "border-border bg-background hover:bg-muted/40"
                  }`}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="truncate text-sm font-medium">
                        {resolveChapterLabel(session.chapterKey)} / {session.profileKey || "draft"}
                      </div>
                      <div className="mt-1 truncate text-xs text-muted-foreground">
                        {session.objective || "未填写目标"}
                      </div>
                    </div>
                    <Badge variant={isSelected ? "default" : "outline"}>{session.status}</Badge>
                  </div>
                  <div className="mt-2 text-[11px] text-muted-foreground">
                    任务 {session.completedTasks}/{session.totalTasks}，排队 {session.pendingTaskCount}，运行{" "}
                    {session.runningTaskCount}
                  </div>
                  {session.progressMessage ? (
                    <div className="mt-1 truncate text-[11px] text-muted-foreground">
                      进度：{session.progressMessage}
                    </div>
                  ) : null}
                  <div className="mt-1 text-[11px] text-muted-foreground">
                    创建于 {new Date(session.createdAt).toLocaleString()}
                  </div>
                </button>
              );
            })}
          </div>
        )}
      </Card>

      {detail ? (
        <Card className="space-y-4 p-4">
          <div className="flex flex-wrap items-center gap-2">
            <Badge>{detail.status}</Badge>
            {detail.profileKey ? <Badge variant="outline">{detail.profileKey}</Badge> : null}
            <Badge variant="outline">{resolveChapterLabel(detail.chapterKey)}</Badge>
            <Badge variant="secondary">{selectedScopeLabel}</Badge>
          </div>

          <div className="grid gap-2 text-xs text-muted-foreground md:grid-cols-3">
            <div className="rounded-md border bg-muted/20 px-3 py-2">任务：{detail.completedTasks}/{detail.totalTasks}</div>
            <div className="rounded-md border bg-muted/20 px-3 py-2">
              开始：{formatTime(detail.startedAt || detail.createdAt)}
            </div>
            <div className="rounded-md border bg-muted/20 px-3 py-2">
              结束：{formatTime(detail.completedAt || detail.lastActivityAt)}
            </div>
          </div>

          <div className="grid gap-2 text-xs text-muted-foreground md:grid-cols-4">
            <div className="rounded-md border bg-muted/20 px-3 py-2">待处理：{detail.pendingTaskCount}</div>
            <div className="rounded-md border bg-muted/20 px-3 py-2">运行中：{detail.runningTaskCount}</div>
            <div className="rounded-md border bg-muted/20 px-3 py-2">
              当前任务：{currentTaskTitle || detail.currentTaskId || "(无)"}
            </div>
            <div className="rounded-md border bg-muted/20 px-3 py-2">进度：{detail.progressMessage || "(无)"}</div>
          </div>

          {detail.runningTaskCount > 1 ? (
            <div className="rounded-md border border-primary/20 bg-primary/5 px-3 py-2 text-xs leading-5 text-muted-foreground">
              当前有 {detail.runningTaskCount} 个任务正在并行执行，`task_runner` 的 AI 日志会随着轮询即时刷新，章节/分支产物差异会在下方“AI 产物差异”区滚动出现。
            </div>
          ) : null}

          {detail.summary ? (
            <div className="rounded-md border bg-muted/15 px-3 py-3 text-sm leading-6">{detail.summary}</div>
          ) : null}

          {overviewArtifact ? (
            <ArtifactCard
              title={overviewArtifact.title}
              icon={<Route className="h-4 w-4" />}
              artifact={overviewArtifact}
              taskTitleById={taskTitleById}
            />
          ) : null}

          <div className="rounded-xl border p-4">
            <div className="mb-3 flex items-center gap-2 text-sm font-medium">
              <GitBranchPlus className="h-4 w-4" />
              任务拆分
            </div>
            <ScrollArea className="h-[260px] pr-3">
              <div className="space-y-2">
                {detail.tasks.map((task) => (
                  <div key={task.id} className="rounded-lg border bg-background px-3 py-3">
                    <div className="flex items-center justify-between gap-2">
                      <div className="text-sm font-medium">
                        {task.sequenceNumber}. {task.title}
                      </div>
                      <Badge variant="outline">{task.taskType}</Badge>
                    </div>
                    <div className="mt-1 text-xs text-muted-foreground">
                      深度 {task.depth} / {task.status}
                    </div>
                    <TaskMetadataHint task={task} />
                    {task.summary ? (
                      <div className="mt-2 text-sm leading-6 text-muted-foreground">{task.summary}</div>
                    ) : null}
                    {task.focusSymbols.length > 0 ? (
                      <div className="mt-2 text-[11px] text-muted-foreground">
                        聚焦符号：{task.focusSymbols.join(" / ")}
                      </div>
                    ) : null}
                  </div>
                ))}
              </div>
            </ScrollArea>
          </div>

          {artifactDiffs.length > 0 ? (
            <Card className="space-y-3 p-4">
              <div className="flex items-center gap-2 text-sm font-medium">
                <Sparkles className="h-4 w-4" />
                AI 产物差异
              </div>
              <div className="space-y-3">
                {artifactDiffs.map((item) => (
                  <ArtifactDiffCard key={item.artifact.id} entry={item} />
                ))}
              </div>
            </Card>
          ) : null}

          {flowcharts.length > 0 ? (
            <ArtifactList
              title="流程图种子"
              icon={<Network className="h-4 w-4" />}
              artifacts={flowcharts}
              taskTitleById={taskTitleById}
            />
          ) : null}

          {mindmaps.length > 0 ? (
            <ArtifactList
              title="脑图种子"
              icon={<ListTree className="h-4 w-4" />}
              artifacts={mindmaps}
              taskTitleById={taskTitleById}
            />
          ) : null}

          {additionalArtifacts.length > 0 ? (
            <ArtifactList
              title="章节与分支产物"
              icon={<GitBranchPlus className="h-4 w-4" />}
              artifacts={additionalArtifacts}
              taskTitleById={taskTitleById}
            />
          ) : null}

          <div className="rounded-xl border p-4">
            <div className="mb-3 flex items-center gap-2 text-sm font-medium">
              <Logs className="h-4 w-4" />
              task_runner 实时日志
            </div>
            {detail.recentLogs.length === 0 ? (
              <div className="text-sm text-muted-foreground">暂无运行日志。</div>
            ) : (
              <ScrollArea className="h-[260px] pr-3">
                <div className="space-y-2">
                  {detail.recentLogs.map((log) => {
                    const taskTitle = log.taskId ? taskTitleById.get(log.taskId) : null;
                    return (
                      <div key={log.id} className="rounded-lg border bg-background px-3 py-3">
                        <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                          <Badge variant={log.level === "ai" ? "default" : "outline"}>{log.level}</Badge>
                          {taskTitle ? <span>{taskTitle}</span> : log.taskId ? <span>任务 {log.taskId}</span> : null}
                          <span>{formatTime(log.createdAt)}</span>
                        </div>
                        <div className="mt-2 text-sm leading-6">
                          {log.level === "ai" ? <Bot className="mr-2 inline h-4 w-4 align-text-bottom" /> : null}
                          {log.message}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </ScrollArea>
            )}
          </div>
        </Card>
      ) : null}
    </div>
  );
}

function ArtifactList({
  title,
  icon,
  artifacts,
  taskTitleById,
}: {
  title: string;
  icon: React.ReactNode;
  artifacts: WorkflowAnalysisArtifact[];
  taskTitleById: Map<string, string>;
}) {
  return (
    <Card className="space-y-3 p-4">
      <div className="flex items-center gap-2 text-sm font-medium">
        {icon}
        {title}
      </div>
      <div className="space-y-3">
        {artifacts.map((artifact) => (
          <ArtifactCard
            key={artifact.id}
            title={artifact.title}
            icon={null}
            artifact={artifact}
            taskTitleById={taskTitleById}
          />
        ))}
      </div>
    </Card>
  );
}

function ArtifactCard({
  title,
  icon,
  artifact,
  taskTitleById,
}: {
  title: string;
  icon: React.ReactNode;
  artifact: WorkflowAnalysisArtifact;
  taskTitleById: Map<string, string>;
}) {
  const metadata = artifact.metadata ?? {};
  const taskTitle = artifact.taskId ? taskTitleById.get(artifact.taskId) : null;
  const generatedBy = metadata.generatedBy;
  const diffKind = metadata.diffKind;
  const chapterKey = metadata.chapterKey;
  const branchRoot = metadata.branchRoot;

  return (
    <div className="rounded-xl border p-4">
      <div className="mb-2 flex flex-wrap items-center gap-2 text-sm font-medium">
        {icon}
        {title}
        <Badge variant="outline">{artifact.contentFormat}</Badge>
        {generatedBy ? <Badge variant="secondary">{generatedBy}</Badge> : null}
        {diffKind ? <Badge variant="outline">{diffKind}</Badge> : null}
      </div>
      {(taskTitle || chapterKey || branchRoot) ? (
        <div className="mb-2 flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
          {taskTitle ? <span>任务：{taskTitle}</span> : null}
          {chapterKey ? <span>章节：{chapterKey}</span> : null}
          {branchRoot ? <span>分支：{branchRoot}</span> : null}
        </div>
      ) : null}
      <pre className="overflow-x-auto rounded-md bg-muted/20 p-3 text-xs leading-5 whitespace-pre-wrap">
        {artifact.content}
      </pre>
    </div>
  );
}

function ArtifactDiffCard({ entry }: { entry: ArtifactDiffEntry }) {
  const { artifact, generatedBy, diffKind, baselineContent, baselineContentFormat, baselineTitle, taskTitle } = entry;

  return (
    <div className="rounded-xl border border-primary/20 bg-primary/5 p-4">
      <div className="mb-2 flex flex-wrap items-center gap-2 text-sm font-medium">
        <Sparkles className="h-4 w-4" />
        {artifact.title}
        <Badge>{generatedBy}</Badge>
        <Badge variant="outline">{diffKind}</Badge>
      </div>
      <div className="mb-3 flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
        {taskTitle ? <span>任务：{taskTitle}</span> : null}
        {entry.chapterKey ? <span>章节：{entry.chapterKey}</span> : null}
        {entry.branchRoot ? <span>分支：{entry.branchRoot}</span> : null}
      </div>
      {baselineContent ? (
        <div className="grid gap-3 md:grid-cols-2">
          <div className="space-y-2">
            <div className="text-xs font-medium text-muted-foreground">
              基线产物 {baselineTitle ? `· ${baselineTitle}` : ""} {baselineContentFormat ? `(${baselineContentFormat})` : ""}
            </div>
            <pre className="overflow-x-auto rounded-md bg-background p-3 text-xs leading-5 whitespace-pre-wrap">
              {baselineContent}
            </pre>
          </div>
          <div className="space-y-2">
            <div className="text-xs font-medium text-muted-foreground">当前产物 ({artifact.contentFormat})</div>
            <pre className="overflow-x-auto rounded-md bg-background p-3 text-xs leading-5 whitespace-pre-wrap">
              {artifact.content}
            </pre>
          </div>
        </div>
      ) : (
        <pre className="overflow-x-auto rounded-md bg-background p-3 text-xs leading-5 whitespace-pre-wrap">
          {artifact.content}
        </pre>
      )}
    </div>
  );
}

function TaskMetadataHint({ task }: { task: WorkflowAnalysisTask }) {
  const metadata = task.metadata ?? {};
  const plannerSource = metadata.plannerSource;
  const chapterKey = metadata.chapterKey;
  const branchRoot = metadata.branchRoot;
  const branchReason = metadata.branchReason;
  const hasBadgeMetadata = Boolean(plannerSource || chapterKey || branchRoot);
  const hasReason = Boolean(branchReason);

  if (!hasBadgeMetadata && !hasReason) {
    return null;
  }

  return (
    <div className="mt-2 space-y-2">
      {hasBadgeMetadata ? (
        <div className="flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
          {plannerSource ? <Badge variant="secondary">planner: {plannerSource}</Badge> : null}
          {chapterKey ? <Badge variant="outline">chapter: {chapterKey}</Badge> : null}
          {branchRoot ? <Badge variant="outline">branch: {branchRoot}</Badge> : null}
        </div>
      ) : null}
      {hasReason ? (
        <div className="text-[11px] leading-5 text-muted-foreground">分支原因：{branchReason}</div>
      ) : null}
    </div>
  );
}

function toArtifactDiffEntry(artifact: WorkflowAnalysisArtifact, taskTitleById: Map<string, string>): ArtifactDiffEntry {
  const metadata = artifact.metadata ?? {};
  return {
    artifact,
    generatedBy: metadata.generatedBy || "unknown",
    diffKind: metadata.diffKind || "unknown",
    baselineContent: metadata.baselineContent,
    baselineTitle: metadata.baselineTitle,
    baselineContentFormat: metadata.baselineContentFormat,
    chapterKey: metadata.chapterKey,
    branchRoot: metadata.branchRoot,
    taskTitle: artifact.taskId ? taskTitleById.get(artifact.taskId) : undefined,
  };
}

function shouldShowArtifactDiff(entry: ArtifactDiffEntry) {
  if (entry.generatedBy === "task-runner-ai") {
    return true;
  }

  return entry.diffKind !== "unchanged" && entry.diffKind !== "unknown";
}

function formatTime(value?: string | null) {
  if (!value) return "(无)";
  return new Date(value).toLocaleString();
}
