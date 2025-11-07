export interface TestCase {
  id: string
  prompt: string
  expectedResponse: string
  tags?: string[]
  createdAt: number
  updatedAt: number
}

export interface EvaluationResult {
  id: string
  testCaseId: string
  prompt: string
  expectedResponse: string
  actualResponse: string
  score: number
  reasoning: string
  passed: boolean
  timestamp: number
  executionTime: number
}

export interface DirectLineConfig {
  endpoint: string
  secret?: string
}

export interface AzureOpenAIConfig {
  endpoint: string
  apiKey: string
  deploymentName: string
  apiVersion?: string
}

export interface DataVerseConfig {
  environmentUrl: string
  clientId: string
  clientSecret: string
  tenantId: string
  testCaseTable: string
  evaluationResultTable: string
  useProxy: boolean
  proxyUrl?: string
}

export interface AppConfig {
  directLine: DirectLineConfig | null
  azureOpenAI: AzureOpenAIConfig | null
  dataVerse: DataVerseConfig | null
}

export interface EvaluationRun {
  id: string
  testCaseIds: string[]
  results: EvaluationResult[]
  timestamp: number
  overallPassRate: number
  status: 'running' | 'completed' | 'failed' | 'cancelled'
}

export type EvaluationStatus = 'idle' | 'running' | 'completed' | 'error'
