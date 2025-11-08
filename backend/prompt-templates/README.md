# Evaluation Prompt Templates

This directory contains the prompt templates used by the LLM Judge service to evaluate AI agent responses.

## Template Files

### similarity-evaluation-prompt.txt
Evaluates semantic similarity between the expected response and the actual agent response. Focuses on:
- Semantic meaning alignment
- Content coverage
- Information accuracy
- Intent alignment

### accuracy-evaluation-prompt.txt
Assesses the factual accuracy of the agent's response. Evaluates:
- Factual correctness
- Information completeness
- Alignment with expected response
- Absence of misleading information
- Contextual accuracy

### relevancy-evaluation-prompt.txt
Measures how relevant the agent's response is to the user's input. Considers:
- Topic alignment
- User intent fulfillment
- Information usefulness
- Focus and directness
- Completeness

### default-evaluation-prompt.txt
General-purpose evaluation template for overall response quality. Assesses:
- Appropriateness
- Quality
- Completeness
- Accuracy
- Alignment with expectations

## Template Placeholders

All templates support the following placeholders that will be replaced with actual values during evaluation:

- `{TestType}` - The type of test being performed (Similarity, Accuracy, or Relevancy)
- `{UserInput}` - The original user input/question
- `{ExpectedResponse}` - The expected response for this test case
- `{ActualResponse}` - The actual response from the AI agent
- `{ConversationId}` - The conversation ID (or "N/A" if not applicable)

## Scoring Scale

All templates use a consistent 0-10 scoring scale:
- **0-2**: Very poor quality
- **3-4**: Poor quality with major issues
- **5-6**: Fair quality with notable room for improvement
- **7-8**: Good quality with minor issues
- **9-10**: Excellent quality, meets or exceeds expectations

## Output Format

All templates require the LLM to return a JSON response with:
```json
{
  "score": [0-10],
  "reasoning": "Detailed explanation of the score"
}
```

## Uploading Templates to Azure Blob Storage

To upload these templates to Azure Blob Storage for use in production:

1. Ensure the storage account and container are configured in your application settings
2. Use the `BlobStorageService.UploadPromptTemplateAsync()` method
3. The templates will be stored in the container specified by `Storage:PromptTemplateContainerName`

Example:
```csharp
await blobStorageService.UploadPromptTemplateAsync(
    "similarity-evaluation-prompt.txt", 
    File.ReadAllText("prompt-templates/similarity-evaluation-prompt.txt"));
```

## Customization

You can customize these templates to match your specific evaluation needs:

1. Modify the evaluation criteria to focus on different aspects
2. Adjust the scoring guidelines to match your quality standards
3. Add domain-specific guidance for your particular use case
4. Enhance the output format to capture additional metadata

When customizing, maintain:
- Clear evaluation criteria
- Consistent scoring scales
- Structured output format
- Proper placeholder usage

## Testing

The templates are tested in the unit tests:
- `LLMJudgeServiceTests.cs` - Tests template loading and prompt building
- Templates are validated for correct placeholder replacement
- Fallback behavior is tested when templates are unavailable

## Default Templates

If a template is not found in Blob Storage, the service will fall back to a default template embedded in the code. This ensures the system continues to function even if storage is temporarily unavailable.
