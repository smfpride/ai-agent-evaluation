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
  secret: string
  endpoint?: string
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
