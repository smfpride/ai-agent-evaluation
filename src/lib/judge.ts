interface JudgeResult {
  score: number
  reasoning: string
  passed: boolean
}

export async function evaluateResponse(
  prompt: string,
  expectedResponse: string,
  actualResponse: string,
  passThreshold: number = 70
): Promise<JudgeResult> {
  const evaluationPrompt = spark.llmPrompt`You are an expert evaluator judging the semantic similarity between an expected response and an actual response from an AI agent.

Your task is to evaluate whether the actual response conveys the same meaning and information as the expected response, even if the wording is different.

PROMPT THAT WAS SENT:
${prompt}

EXPECTED RESPONSE:
${expectedResponse}

ACTUAL RESPONSE:
${actualResponse}

Evaluate the actual response based on these criteria:
1. Does it contain the same core information as the expected response?
2. Is the meaning semantically equivalent, even if worded differently?
3. Does it address the same aspects of the prompt?
4. Are any critical facts or details missing or incorrect?

Provide your evaluation as a JSON object with:
- score: A number from 0-100 indicating semantic similarity (100 = perfect match, 0 = completely different)
- reasoning: A brief explanation of your scoring decision
- passed: Boolean indicating if the response meets quality standards

Return ONLY valid JSON with this structure:
{
  "score": <number>,
  "reasoning": "<string>",
  "passed": <boolean>
}`

  try {
    const resultStr = await spark.llm(evaluationPrompt, 'gpt-4o', true)
    const result = JSON.parse(resultStr)
    
    return {
      score: result.score,
      reasoning: result.reasoning,
      passed: result.score >= passThreshold,
    }
  } catch (error) {
    console.error('LLM evaluation failed:', error)
    return {
      score: 0,
      reasoning: `Evaluation failed: ${error instanceof Error ? error.message : 'Unknown error'}`,
      passed: false,
    }
  }
}
