import type { DataVerseConfig, TestCase, EvaluationResult } from '@/types'

interface AccessTokenResponse {
  access_token: string
  expires_in: number
  token_type: string
}

interface DataVerseEntity {
  [key: string]: unknown
}

interface ProxyRequest {
  method: string
  endpoint: string
  body?: unknown
  headers?: Record<string, string>
}

interface ProxyResponse {
  success: boolean
  data?: unknown
  error?: string
}

export class DataVerseService {
  private config: DataVerseConfig
  private accessToken: string | null = null
  private tokenExpiry: number = 0

  constructor(config: DataVerseConfig) {
    this.config = config
  }

  private async getAccessToken(): Promise<string> {
    if (this.config.useProxy) {
      return 'proxy-handled'
    }

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

    try {
      const response = await fetch(tokenUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded',
        },
        body: params.toString(),
      })

      if (!response.ok) {
        const errorText = await response.text()
        console.error('Token request failed:', {
          status: response.status,
          statusText: response.statusText,
          error: errorText,
          tokenUrl,
          tenantId: this.config.tenantId,
          clientId: this.config.clientId,
          scope: `${this.config.environmentUrl}/.default`,
        })
        throw new Error(`Failed to get access token: ${response.status} ${response.statusText} - ${errorText}`)
      }

      const data: AccessTokenResponse = await response.json()
      this.accessToken = data.access_token
      this.tokenExpiry = Date.now() + data.expires_in * 1000

      return this.accessToken
    } catch (error) {
      console.error('Access token error:', error)
      throw error
    }
  }

  private async makeProxyRequest(method: string, endpoint: string, body?: unknown): Promise<unknown> {
    if (!this.config.proxyUrl) {
      throw new Error('Proxy URL not configured')
    }

    const proxyRequest: ProxyRequest = {
      method,
      endpoint,
      body,
      headers: {
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        Accept: 'application/json',
        'Content-Type': 'application/json',
      }
    }

    const response = await fetch(this.config.proxyUrl, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-DataVerse-Config': JSON.stringify({
          environmentUrl: this.config.environmentUrl,
          clientId: this.config.clientId,
          clientSecret: this.config.clientSecret,
          tenantId: this.config.tenantId,
        }),
      },
      body: JSON.stringify(proxyRequest),
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(`Proxy request failed: ${response.status} ${response.statusText} - ${errorText}`)
    }

    const proxyResponse: ProxyResponse = await response.json()

    if (!proxyResponse.success) {
      throw new Error(proxyResponse.error || 'Proxy request failed')
    }

    return proxyResponse.data
  }

  private async makeDirectRequest(url: string, options: RequestInit): Promise<Response> {
    const token = await this.getAccessToken()
    
    return fetch(url, {
      ...options,
      headers: {
        ...options.headers,
        Authorization: `Bearer ${token}`,
      },
    })
  }

  async fetchTestCases(
    top: number = 100,
    skip: number = 0,
    filter?: string
  ): Promise<{ testCases: TestCase[]; totalCount: number }> {
    const baseUrl = `${this.config.environmentUrl}/api/data/v9.2/${this.config.testCaseTable}`

    let endpoint = `${baseUrl}?$count=true&$top=${top}&$skip=${skip}&$orderby=createdon desc`
    if (filter) {
      endpoint += `&$filter=${encodeURIComponent(filter)}`
    }

    try {
      let data: any

      if (this.config.useProxy) {
        data = await this.makeProxyRequest('GET', endpoint)
      } else {
        const response = await this.makeDirectRequest(endpoint, {
          headers: {
            'OData-MaxVersion': '4.0',
            'OData-Version': '4.0',
            Accept: 'application/json',
            'Content-Type': 'application/json',
          },
        })

        if (!response.ok) {
          const errorText = await response.text()
          console.error('Fetch test cases failed:', {
            status: response.status,
            statusText: response.statusText,
            error: errorText,
            url: endpoint,
            table: this.config.testCaseTable,
          })
          throw new Error(`Failed to fetch test cases: ${response.status} ${response.statusText} - ${errorText}`)
        }

        data = await response.json()
      }

      const testCases: TestCase[] = data.value.map((entity: DataVerseEntity) => ({
        id: entity.mcicaps_testcasesid as string,
        prompt: entity.mcicaps_prompt as string,
        expectedResponse: entity.mcicaps_expectedresponse as string,
        tags: entity.mcicaps_tags ? JSON.parse(entity.mcicaps_tags as string) : undefined,
        createdAt: entity.createdon ? new Date(entity.createdon as string).getTime() : Date.now(),
        updatedAt: entity.modifiedon ? new Date(entity.modifiedon as string).getTime() : Date.now(),
      }))

      return {
        testCases,
        totalCount: data['@odata.count'] || 0,
      }
    } catch (error) {
      console.error('Fetch test cases error:', error)
      throw error
    }
  }

  async createTestCase(testCase: Omit<TestCase, 'id' | 'createdAt' | 'updatedAt'>): Promise<TestCase> {
    const url = `${this.config.environmentUrl}/api/data/v9.2/${this.config.testCaseTable}`

    const entity: DataVerseEntity = {
      mcicaps_prompt: testCase.prompt,
      mcicaps_expectedresponse: testCase.expectedResponse,
      mcicaps_testcasename: testCase.prompt.substring(0, 100),
    }

    if (testCase.tags) {
      entity.mcicaps_tags = JSON.stringify(testCase.tags)
    }

    let createdEntity: any

    if (this.config.useProxy) {
      createdEntity = await this.makeProxyRequest('POST', url, entity)
    } else {
      const response = await this.makeDirectRequest(url, {
        method: 'POST',
        headers: {
          'OData-MaxVersion': '4.0',
          'OData-Version': '4.0',
          Accept: 'application/json',
          'Content-Type': 'application/json',
          Prefer: 'return=representation',
        },
        body: JSON.stringify(entity),
      })

      if (!response.ok) {
        const errorText = await response.text()
        throw new Error(`Failed to create test case: ${response.statusText} - ${errorText}`)
      }

      createdEntity = await response.json()
    }

    return {
      id: createdEntity.mcicaps_testcasesid,
      prompt: testCase.prompt,
      expectedResponse: testCase.expectedResponse,
      tags: testCase.tags,
      createdAt: createdEntity.createdon ? new Date(createdEntity.createdon).getTime() : Date.now(),
      updatedAt: createdEntity.modifiedon ? new Date(createdEntity.modifiedon).getTime() : Date.now(),
    }
  }

  async updateTestCase(id: string, updates: Partial<Omit<TestCase, 'id' | 'createdAt' | 'updatedAt'>>): Promise<void> {
    const url = `${this.config.environmentUrl}/api/data/v9.2/${this.config.testCaseTable}(${id})`

    const entity: DataVerseEntity = {}
    if (updates.prompt !== undefined) {
      entity.mcicaps_prompt = updates.prompt
      entity.mcicaps_testcasename = updates.prompt.substring(0, 100)
    }
    if (updates.expectedResponse !== undefined) {
      entity.mcicaps_expectedresponse = updates.expectedResponse
    }
    if (updates.tags !== undefined) {
      entity.mcicaps_tags = JSON.stringify(updates.tags)
    }

    if (this.config.useProxy) {
      await this.makeProxyRequest('PATCH', url, entity)
    } else {
      const response = await this.makeDirectRequest(url, {
        method: 'PATCH',
        headers: {
          'OData-MaxVersion': '4.0',
          'OData-Version': '4.0',
          Accept: 'application/json',
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(entity),
      })

      if (!response.ok) {
        const errorText = await response.text()
        throw new Error(`Failed to update test case: ${response.statusText} - ${errorText}`)
      }
    }
  }

  async deleteTestCase(id: string): Promise<void> {
    const url = `${this.config.environmentUrl}/api/data/v9.2/${this.config.testCaseTable}(${id})`

    if (this.config.useProxy) {
      await this.makeProxyRequest('DELETE', url)
    } else {
      const response = await this.makeDirectRequest(url, {
        method: 'DELETE',
        headers: {
          'OData-MaxVersion': '4.0',
          'OData-Version': '4.0',
        },
      })

      if (!response.ok) {
        throw new Error(`Failed to delete test case: ${response.statusText}`)
      }
    }
  }

  async createEvaluationResult(result: EvaluationResult): Promise<void> {
    const url = `${this.config.environmentUrl}/api/data/v9.2/${this.config.evaluationResultTable}`

    const entity: DataVerseEntity = {
      mcicaps_evaluationresultname: `Result ${new Date().toISOString()}`,
      mcicaps_actualresponse: result.actualResponse,
      mcicaps_score: result.score,
      mcicaps_reasoning: result.reasoning,
      mcicaps_passed: result.passed,
      'mcicaps_testcase@odata.bind': `/${this.config.testCaseTable}(${result.testCaseId})`,
    }

    if (this.config.useProxy) {
      await this.makeProxyRequest('POST', url, entity)
    } else {
      const response = await this.makeDirectRequest(url, {
        method: 'POST',
        headers: {
          'OData-MaxVersion': '4.0',
          'OData-Version': '4.0',
          Accept: 'application/json',
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(entity),
      })

      if (!response.ok) {
        const errorText = await response.text()
        throw new Error(`Failed to create evaluation result: ${response.statusText} - ${errorText}`)
      }
    }
  }

  async fetchEvaluationResults(
    top: number = 100,
    skip: number = 0,
    filter?: string
  ): Promise<{ results: EvaluationResult[]; totalCount: number }> {
    const baseUrl = `${this.config.environmentUrl}/api/data/v9.2/${this.config.evaluationResultTable}`

    let endpoint = `${baseUrl}?$count=true&$top=${top}&$skip=${skip}&$orderby=createdon desc&$expand=mcicaps_testcase($select=mcicaps_testcasesid,mcicaps_prompt,mcicaps_expectedresponse)`
    if (filter) {
      endpoint += `&$filter=${encodeURIComponent(filter)}`
    }

    let data: any

    if (this.config.useProxy) {
      data = await this.makeProxyRequest('GET', endpoint)
    } else {
      const response = await this.makeDirectRequest(endpoint, {
        headers: {
          'OData-MaxVersion': '4.0',
          'OData-Version': '4.0',
          Accept: 'application/json',
          'Content-Type': 'application/json',
        },
      })

      if (!response.ok) {
        const errorText = await response.text()
        throw new Error(`Failed to fetch evaluation results: ${response.statusText} - ${errorText}`)
      }

      data = await response.json()
    }

    const results: EvaluationResult[] = data.value.map((entity: DataVerseEntity) => {
      const testCase = entity.mcicaps_testcase as DataVerseEntity | undefined
      return {
        id: entity.mcicaps_evaluationresultsid as string,
        testCaseId: testCase?.mcicaps_testcasesid as string || '',
        prompt: testCase?.mcicaps_prompt as string || '',
        expectedResponse: testCase?.mcicaps_expectedresponse as string || '',
        actualResponse: entity.mcicaps_actualresponse as string,
        score: entity.mcicaps_score as number,
        reasoning: entity.mcicaps_reasoning as string,
        passed: entity.mcicaps_passed as boolean,
        timestamp: entity.createdon ? new Date(entity.createdon as string).getTime() : Date.now(),
        executionTime: 0,
      }
    })

    return {
      results,
      totalCount: data['@odata.count'] || 0,
    }
  }
}

export async function testDataVerseConnection(config: DataVerseConfig): Promise<boolean> {
  try {
    console.log('Testing DataVerse connection with config:', {
      environmentUrl: config.environmentUrl,
      tenantId: config.tenantId,
      clientId: config.clientId,
      testCaseTable: config.testCaseTable,
      evaluationResultTable: config.evaluationResultTable,
      useProxy: config.useProxy,
      proxyUrl: config.proxyUrl,
    })
    
    const service = new DataVerseService(config)
    const result = await service.fetchTestCases(1, 0)
    
    console.log('DataVerse connection test successful. Found test cases:', result.totalCount)
    return true
  } catch (error) {
    console.error('DataVerse connection test failed:', error)
    if (error instanceof Error) {
      console.error('Error message:', error.message)
      console.error('Error stack:', error.stack)
    }
    return false
  }
}
