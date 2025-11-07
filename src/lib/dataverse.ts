import type { DataVerseConfig, TestCase, EvaluationResult } from '@/types'

interface AccessTokenResponse {
  access_token: string
  expires_in: number
  token_type: string
}

interface DataVerseEntity {
  [key: string]: unknown
}

export class DataVerseService {
  private config: DataVerseConfig
  private accessToken: string | null = null
  private tokenExpiry: number = 0

  constructor(config: DataVerseConfig) {
    this.config = config
  }

  private async getAccessToken(): Promise<string> {
    if (this.accessToken && Date.now() < this.tokenExpiry - 60000) {
      return this.accessToken
    }

    const tokenUrl = `https://login.microsoftonline.com/${this.config.tenantId}/oauth2/v2.0/token`
    const params = new URLSearchParams({
      client_id: this.config.clientId,
      client_secret: this.config.clientSecret,
      scope: `${this.config.environmentUrl}/.default`,
      grant_type: 'client_credentials',
    })

    const response = await fetch(tokenUrl, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
      },
      body: params.toString(),
    })

    if (!response.ok) {
      throw new Error(`Failed to get access token: ${response.statusText}`)
    }

    const data: AccessTokenResponse = await response.json()
    this.accessToken = data.access_token
    this.tokenExpiry = Date.now() + data.expires_in * 1000

    return this.accessToken
  }

  async fetchTestCases(
    top: number = 100,
    skip: number = 0,
    filter?: string
  ): Promise<{ testCases: TestCase[]; totalCount: number }> {
    const token = await this.getAccessToken()
    const baseUrl = `${this.config.environmentUrl}/api/data/v9.2/${this.config.testCaseTable}`

    let url = `${baseUrl}?$count=true&$top=${top}&$skip=${skip}`
    if (filter) {
      url += `&$filter=${encodeURIComponent(filter)}`
    }

    const response = await fetch(url, {
      headers: {
        Authorization: `Bearer ${token}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
    })

    if (!response.ok) {
      throw new Error(`Failed to fetch test cases: ${response.statusText}`)
    }

    const data = await response.json()

    const testCases: TestCase[] = data.value.map((entity: DataVerseEntity) => ({
      id: entity.id as string,
      prompt: entity.prompt as string,
      expectedResponse: entity.expectedresponse as string,
      tags: entity.tags ? JSON.parse(entity.tags as string) : undefined,
      createdAt: entity.createdon ? new Date(entity.createdon as string).getTime() : Date.now(),
      updatedAt: entity.modifiedon ? new Date(entity.modifiedon as string).getTime() : Date.now(),
    }))

    return {
      testCases,
      totalCount: data['@odata.count'] || 0,
    }
  }

  async createTestCase(testCase: Omit<TestCase, 'id' | 'createdAt' | 'updatedAt'>): Promise<TestCase> {
    const token = await this.getAccessToken()
    const url = `${this.config.environmentUrl}/api/data/v9.2/${this.config.testCaseTable}`

    const entity: DataVerseEntity = {
      prompt: testCase.prompt,
      expectedresponse: testCase.expectedResponse,
      tags: testCase.tags ? JSON.stringify(testCase.tags) : undefined,
    }

    const response = await fetch(url, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(entity),
    })

    if (!response.ok) {
      throw new Error(`Failed to create test case: ${response.statusText}`)
    }

    const entityId = response.headers.get('OData-EntityId')?.split('(')[1].split(')')[0]
    const createdEntity = await response.json()

    return {
      id: entityId || createdEntity.id,
      prompt: testCase.prompt,
      expectedResponse: testCase.expectedResponse,
      tags: testCase.tags,
      createdAt: Date.now(),
      updatedAt: Date.now(),
    }
  }

  async updateTestCase(id: string, updates: Partial<Omit<TestCase, 'id' | 'createdAt' | 'updatedAt'>>): Promise<void> {
    const token = await this.getAccessToken()
    const url = `${this.config.environmentUrl}/api/data/v9.2/${this.config.testCaseTable}(${id})`

    const entity: DataVerseEntity = {}
    if (updates.prompt !== undefined) entity.prompt = updates.prompt
    if (updates.expectedResponse !== undefined) entity.expectedresponse = updates.expectedResponse
    if (updates.tags !== undefined) entity.tags = JSON.stringify(updates.tags)

    const response = await fetch(url, {
      method: 'PATCH',
      headers: {
        Authorization: `Bearer ${token}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(entity),
    })

    if (!response.ok) {
      throw new Error(`Failed to update test case: ${response.statusText}`)
    }
  }

  async deleteTestCase(id: string): Promise<void> {
    const token = await this.getAccessToken()
    const url = `${this.config.environmentUrl}/api/data/v9.2/${this.config.testCaseTable}(${id})`

    const response = await fetch(url, {
      method: 'DELETE',
      headers: {
        Authorization: `Bearer ${token}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
      },
    })

    if (!response.ok) {
      throw new Error(`Failed to delete test case: ${response.statusText}`)
    }
  }

  async createEvaluationResult(result: EvaluationResult): Promise<void> {
    const token = await this.getAccessToken()
    const url = `${this.config.environmentUrl}/api/data/v9.2/${this.config.evaluationResultTable}`

    const entity: DataVerseEntity = {
      id: result.id,
      testcaseid: result.testCaseId,
      prompt: result.prompt,
      expectedresponse: result.expectedResponse,
      actualresponse: result.actualResponse,
      score: result.score,
      reasoning: result.reasoning,
      passed: result.passed,
      timestamp: result.timestamp,
      executiontime: result.executionTime,
    }

    const response = await fetch(url, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(entity),
    })

    if (!response.ok) {
      throw new Error(`Failed to create evaluation result: ${response.statusText}`)
    }
  }

  async fetchEvaluationResults(
    top: number = 100,
    skip: number = 0,
    filter?: string
  ): Promise<{ results: EvaluationResult[]; totalCount: number }> {
    const token = await this.getAccessToken()
    const baseUrl = `${this.config.environmentUrl}/api/data/v9.2/${this.config.evaluationResultTable}`

    let url = `${baseUrl}?$count=true&$top=${top}&$skip=${skip}&$orderby=timestamp desc`
    if (filter) {
      url += `&$filter=${encodeURIComponent(filter)}`
    }

    const response = await fetch(url, {
      headers: {
        Authorization: `Bearer ${token}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
    })

    if (!response.ok) {
      throw new Error(`Failed to fetch evaluation results: ${response.statusText}`)
    }

    const data = await response.json()

    const results: EvaluationResult[] = data.value.map((entity: DataVerseEntity) => ({
      id: entity.id as string,
      testCaseId: entity.testcaseid as string,
      prompt: entity.prompt as string,
      expectedResponse: entity.expectedresponse as string,
      actualResponse: entity.actualresponse as string,
      score: entity.score as number,
      reasoning: entity.reasoning as string,
      passed: entity.passed as boolean,
      timestamp: entity.timestamp as number,
      executionTime: entity.executiontime as number,
    }))

    return {
      results,
      totalCount: data['@odata.count'] || 0,
    }
  }
}

export async function testDataVerseConnection(config: DataVerseConfig): Promise<boolean> {
  try {
    const service = new DataVerseService(config)
    await service.fetchTestCases(1, 0)
    return true
  } catch (error) {
    console.error('DataVerse connection test failed:', error)
    return false
  }
}
