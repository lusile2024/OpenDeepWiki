export type RepositoryWorkflowProfileMode = "WcsRequestExecutor";

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

export interface RepositoryWorkflowProfile {
  key: string;
  name: string;
  description?: string | null;
  enabled: boolean;
  mode: RepositoryWorkflowProfileMode;
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
