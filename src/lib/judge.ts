import type { AzureOpenAIConfig } from '@/types'

interface JudgeResult {
  score: number
  reasoning: string
  passed: boolean
}

interface AzureOpenAIMessage {
  role: 'system' | 'user' | 'assistant'
  content: string
}

interface AzureOpenAIRequest {
  messages: AzureOpenAIMessage[]
  temperature?: number
  max_tokens?: number
  response_format?: { type: 'json_object' }
}

interface AzureOpenAIResponse {
  choices: Array<{
    message: {
      content: string
    }
  }>
}

export class AzureOpenAIJudge {
  private config: AzureOpenAIConfig

  constructor(config: AzureOpenAIConfig) {
    this.config = config
  }

  async evaluate(
    prompt: string,
    expectedResponse: string,
    actualResponse: string,
    passThreshold: number = 70
  ): Promise<JudgeResult> {
    const systemPrompt = `You are an expert evaluator judging the semantic similarity between an expected response and an actual response from an AI agent.

Your task is to evaluate whether the actual response conveys the same meaning and information as the expected response, even if the wording is different.

Evaluate based on these criteria:
1. Does it contain the same core information as the expected response?
2. Is the meaning semantically equivalent, even if worded differently?
3. Does it address the same aspects of the prompt?
4. Are any critical facts or details missing or incorrect?

Provide your evaluation as a JSON object with:
- score: A number from 0-100 indicating semantic similarity (100 = perfect match, 0 = completely different)
- reasoning: A brief explanation of your scoring decision
- passed: Boolean indicating if the response meets quality standards`

    const userPrompt = `PROMPT THAT WAS SENT:
${prompt}

EXPECTED RESPONSE:
${expectedResponse}

ACTUAL RESPONSE:
${actualResponse}

Return ONLY valid JSON with this structure:
{
  "score": <number>,
  "reasoning": "<string>",
  "passed": <boolean>
}`

    const apiVersion = this.config.apiVersion || '2024-02-15-preview'
    const url = `${this.config.endpoint}/openai/deployments/${this.config.deploymentName}/chat/completions?api-version=${apiVersion}`

    const requestBody: AzureOpenAIRequest = {
      messages: [
        { role: 'system', content: systemPrompt },
        { role: 'user', content: userPrompt },
      ],
      temperature: 0.3,
      max_tokens: 500,
      response_format: { type: 'json_object' },
    }

    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'api-key': this.config.apiKey,
        },
        body: JSON.stringify(requestBody),
      })

      if (!response.ok) {
        const errorText = await response.text()
        throw new Error(`Azure OpenAI API error: ${response.statusText} - ${errorText}`)
      }

      const data: AzureOpenAIResponse = await response.json()
      const content = data.choices[0]?.message?.content

      if (!content) {
        throw new Error('No content in Azure OpenAI response')
      }

      const result = JSON.parse(content)

      return {
        score: result.score,
        reasoning: result.reasoning,
        passed: result.score >= passThreshold,
      }
    } catch (error) {
      console.error('Azure OpenAI evaluation failed:', error)
      return {
        score: 0,
        reasoning: `Evaluation failed: ${error instanceof Error ? error.message : 'Unknown error'}`,
        passed: false,
      }
    }
  }
}

export async function testAzureOpenAIConnection(config: AzureOpenAIConfig): Promise<boolean> {
  try {
    const judge = new AzureOpenAIJudge(config)
    await judge.evaluate('Test', 'Test response', 'Test response', 70)
    return true
  } catch (error) {
    console.error('Azure OpenAI connection test failed:', error)
    return false
  }
}
