export type OverlayMappingRuleType = "RemoveVariantSegment" | "RegexReplace";

export interface OverlayMappingRule {
  type: OverlayMappingRuleType;
  segment?: string | null;
  pattern?: string | null;
  replacement?: string | null;
}

export interface OverlayVariant {
  key: string;
  name?: string | null;
  detectionMode: "PathSegmentEquals";
}

export interface OverlayGenerationOptions {
  onlyShowProjectChanges: boolean;
  diffMode: "A" | "B" | string;
  maxFiles: number;
  maxFileBytes: number;
}

export interface OverlayProfile {
  key: string;
  name: string;
  baseBranchName: string;
  overlayBranchNameTemplate: string;
  roots: string[];
  variants: OverlayVariant[];
  mappingRules: OverlayMappingRule[];
  includeGlobs: string[];
  excludeGlobs: string[];
  generation: OverlayGenerationOptions;
}

export interface RepositoryOverlayConfig {
  version: number;
  activeProfileKey?: string | null;
  profiles: OverlayProfile[];
}

export interface OverlayOverrideItem {
  variantKey: string;
  variantName: string;
  projectPath: string;
  basePath: string;
  displayPath: string;
}

export interface OverlayAddedItem {
  variantKey: string;
  variantName: string;
  projectPath: string;
  displayPath: string;
}

export interface OverlayIndexSummary {
  overrideCount: number;
  addedCount: number;
  totalCount: number;
}

export interface OverlayIndex {
  profileKey: string;
  profileName: string;
  baseBranchName: string;
  isCapped?: boolean;
  maxFilesApplied?: number | null;
  uncappedSummary?: OverlayIndexSummary | null;
  overrides: OverlayOverrideItem[];
  added: OverlayAddedItem[];
  summary: OverlayIndexSummary;
}

export interface OverlayGenerationResult {
  overlayBranchName: string;
  languageCode: string;
  summary: OverlayIndexSummary;
}

export interface OverlaySuggestRequest {
  userIntent?: string | null;
  baseBranchName?: string | null;
  maxVariants?: number;
  maxSamplesPerVariant?: number;
}

export interface OverlayOverrideSample {
  root: string;
  projectPath: string;
  basePath: string;
}

export interface OverlayAddedSample {
  root: string;
  projectPath: string;
  displayPath: string;
}

export interface OverlayVariantCandidate {
  key: string;
  suggestedName: string;
  score: number;
  overrideCount: number;
  addedCount: number;
  roots: string[];
  overrideSamples: OverlayOverrideSample[];
  addedSamples: OverlayAddedSample[];
}

export interface OverlayRepositoryStructureAnalysis {
  suggestedRoots: string[];
  suggestedIncludeGlobs: string[];
  variantCandidates: OverlayVariantCandidate[];
}

export interface OverlaySuggestResponse {
  repositoryId: string;
  repositoryName: string;
  baseBranchName: string;
  detectedPrimaryLanguage: string;
  usedAi: boolean;
  model: string;
  summary: string;
  reasoningSummary: string;
  warnings: string[];
  analysis: OverlayRepositoryStructureAnalysis;
  suggestedConfig: RepositoryOverlayConfig;
}
