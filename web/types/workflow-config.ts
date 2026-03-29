export type RepositoryWorkflowProfileMode = "WcsRequestExecutor";
export type WorkflowProfileAnalysisMode = "Manual" | "Roslyn" | "Hybrid";
export type WorkflowChapterAnalysisMode = "Standard" | "Deep";

export interface RepositoryWorkflowProfileSource {
  type: string;
  sessionId?: string | null;
  versionNumber?: number | null;
  updatedByUserId?: string | null;
  updatedByUserName?: string | null;
  updatedAt?: string | null;
}

export interface WorkflowDocumentPreferences {
  writingHint?: string | null;
  preferredTerms: string[];
  requiredSections: string[];
  avoidPrimaryTriggerNames: string[];
}

export interface WorkflowProfileAnalysisOptions {
  mode: WorkflowProfileAnalysisMode;
  entryDirectories: string[];
  rootSymbolNames: string[];
  mustExplainSymbols: string[];
  allowedNamespaces: string[];
  stopNamespacePrefixes: string[];
  stopNamePatterns: string[];
  depthBudget: number;
  maxNodes: number;
  enableCoverageValidation: boolean;
}

export interface WorkflowChapterProfile {
  key: string;
  title: string;
  description?: string | null;
  analysisMode: WorkflowChapterAnalysisMode;
  rootSymbolNames: string[];
  mustExplainSymbols: string[];
  requiredSections: string[];
  outputArtifacts: string[];
  depthBudget: number;
  maxNodes: number;
  includeFlowchart: boolean;
  includeMindmap: boolean;
}

export interface WorkflowCallHierarchyEdge {
  fromSymbol: string;
  toSymbol: string;
  kind: string;
  reason?: string | null;
}

export interface WorkflowLspAssistOptions {
  enabled: boolean;
  preferredServer?: string | null;
  includeCallHierarchy: boolean;
  requestTimeoutMs: number;
  enableDefinitionLookup: boolean;
  enableReferenceLookup: boolean;
  enablePrepareCallHierarchy: boolean;
  additionalEntrySymbolHints: string[];
  suggestedEntryDirectories: string[];
  suggestedRootSymbolNames: string[];
  suggestedMustExplainSymbols: string[];
  callHierarchyEdges: WorkflowCallHierarchyEdge[];
  lastAugmentedAt?: string | null;
}

export interface WorkflowAcpOptions {
  enabled: boolean;
  objective: string;
  maxBranchTasks: number;
  maxParallelTasks: number;
  splitStrategy: string;
  generateMindMapSeed: boolean;
  generateFlowchartSeed: boolean;
}

export interface RepositoryWorkflowProfile {
  key: string;
  name: string;
  description?: string | null;
  enabled: boolean;
  mode: RepositoryWorkflowProfileMode;
  entryRoots: string[];
  entryKinds: string[];
  anchorDirectories: string[];
  anchorNames: string[];
  primaryTriggerDirectories: string[];
  compensationTriggerDirectories: string[];
  schedulerDirectories: string[];
  serviceDirectories: string[];
  repositoryDirectories: string[];
  primaryTriggerNames: string[];
  compensationTriggerNames: string[];
  schedulerNames: string[];
  requestEntityNames: string[];
  requestServiceNames: string[];
  requestRepositoryNames: string[];
  source: RepositoryWorkflowProfileSource;
  documentPreferences: WorkflowDocumentPreferences;
  analysis: WorkflowProfileAnalysisOptions;
  chapterProfiles: WorkflowChapterProfile[];
  lspAssist: WorkflowLspAssistOptions;
  acp: WorkflowAcpOptions;
}

export interface RepositoryWorkflowConfig {
  version: number;
  activeProfileKey?: string | null;
  profiles: RepositoryWorkflowProfile[];
}

export interface CreateWorkflowTemplateSessionRequest {
  branchId?: string;
  languageCode?: string;
  title?: string;
}

export interface WorkflowTemplateMessageRequest {
  content: string;
}

export interface WorkflowTemplateAugmentRequest {
  applyToDraftVersion?: boolean;
}

export interface CreateWorkflowAnalysisSessionRequest {
  chapterKey?: string;
  objective?: string;
}

export interface WorkflowTemplateSessionSummary {
  sessionId: string;
  repositoryId: string;
  status: string;
  title?: string | null;
  branchId?: string | null;
  branchName?: string | null;
  languageCode?: string | null;
  currentDraftKey?: string | null;
  currentDraftName?: string | null;
  currentVersionNumber: number;
  adoptedVersionNumber?: number | null;
  messageCount: number;
  lastActivityAt: string;
  createdAt: string;
}

export interface WorkflowTemplateDiscoveryCandidate {
  key: string;
  name: string;
  summary: string;
  triggerPoints: string[];
  compensationTriggerPoints: string[];
  requestEntities: string[];
  schedulerFiles: string[];
  executorFiles: string[];
  evidenceFiles: string[];
}

export interface WorkflowTemplateSessionContext {
  repositoryName: string;
  branchName?: string | null;
  languageCode?: string | null;
  primaryLanguage?: string | null;
  sourceLocation?: string | null;
  directoryPreview: string;
  discoveryCandidates: WorkflowTemplateDiscoveryCandidate[];
}

export interface WorkflowTemplateMessage {
  id: string;
  sequenceNumber: number;
  role: string;
  content: string;
  versionNumber?: number | null;
  changeSummary?: string | null;
  messageTimestamp: string;
}

export interface WorkflowTemplateDraftVersion {
  id: string;
  versionNumber: number;
  basedOnVersionNumber?: number | null;
  sourceType: string;
  changeSummary?: string | null;
  riskNotes: string[];
  evidenceFiles: string[];
  validationIssues: string[];
  draft: RepositoryWorkflowProfile;
  createdAt: string;
}

export interface WorkflowTemplateSessionDetail extends WorkflowTemplateSessionSummary {
  context?: WorkflowTemplateSessionContext | null;
  currentDraft?: RepositoryWorkflowProfile | null;
  messages: WorkflowTemplateMessage[];
  versions: WorkflowTemplateDraftVersion[];
}

export interface WorkflowTemplateAdoptResult {
  session: WorkflowTemplateSessionDetail;
  savedConfig: RepositoryWorkflowConfig;
}

export interface WorkflowLspAugmentResult {
  profileKey: string;
  summary: string;
  strategy: string;
  fallbackReason?: string | null;
  lspServerName?: string | null;
  suggestedEntryDirectories: string[];
  suggestedRootSymbolNames: string[];
  suggestedMustExplainSymbols: string[];
  suggestedChapterProfiles: WorkflowChapterProfile[];
  callHierarchyEdges: WorkflowCallHierarchyEdge[];
  evidenceFiles: string[];
  diagnostics: WorkflowLspDiagnostic[];
  resolvedDefinitions: WorkflowLspResolvedLocation[];
  resolvedReferences: WorkflowLspResolvedLocation[];
}

export interface WorkflowLspDiagnostic {
  level: string;
  message: string;
}

export interface WorkflowLspResolvedLocation {
  symbolName?: string | null;
  filePath: string;
  lineNumber?: number | null;
  columnNumber?: number | null;
  source?: string | null;
}

export interface WorkflowTemplateAugmentResultPayload {
  augment: WorkflowLspAugmentResult;
  session: WorkflowTemplateSessionDetail;
  createdVersionNumber?: number | null;
}

export interface WorkflowAnalysisSessionSummary {
  analysisSessionId: string;
  repositoryId: string;
  workflowTemplateSessionId: string;
  profileKey?: string | null;
  draftVersionNumber?: number | null;
  chapterKey?: string | null;
  status: string;
  objective?: string | null;
  summary?: string | null;
  totalTasks: number;
  completedTasks: number;
  failedTasks: number;
  pendingTaskCount: number;
  runningTaskCount: number;
  currentTaskId?: string | null;
  progressMessage?: string | null;
  createdAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
  lastActivityAt: string;
}

export interface WorkflowAnalysisTask {
  id: string;
  parentTaskId?: string | null;
  sequenceNumber: number;
  depth: number;
  taskType: string;
  title: string;
  status: string;
  summary?: string | null;
  focusSymbols: string[];
  focusFiles: string[];
  startedAt?: string | null;
  completedAt?: string | null;
  errorMessage?: string | null;
  metadata: Record<string, string>;
}

export interface WorkflowAnalysisArtifact {
  id: string;
  taskId?: string | null;
  artifactType: string;
  title: string;
  contentFormat: string;
  content: string;
  createdAt: string;
  metadata: Record<string, string>;
}

export interface WorkflowAnalysisSessionDetail extends WorkflowAnalysisSessionSummary {
  tasks: WorkflowAnalysisTask[];
  artifacts: WorkflowAnalysisArtifact[];
  recentLogs: WorkflowAnalysisLog[];
}

export interface WorkflowAnalysisLog {
  id: string;
  taskId?: string | null;
  level: string;
  message: string;
  createdAt: string;
}
