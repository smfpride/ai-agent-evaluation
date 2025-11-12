# Evaluation Prompt Templates

This directory contains the prompt templates used by the LLM Judge service to evaluate AI agent responses.

## Directory Structure

```
prompt-templates/
├── README.md
├── default/           # General-purpose evaluation prompts
│   ├── accuracy-evaluation-prompt.txt
│   ├── default-evaluation-prompt.txt
│   ├── relevancy-evaluation-prompt.txt
│   └── similarity-evaluation-prompt.txt
└── fashion/          # Fashion/clothing industry-specific prompts
    ├── README.md
    ├── color-accuracy-evaluation-prompt.txt
    ├── fashion-accuracy-evaluation-prompt.txt
    ├── fashion-relevancy-evaluation-prompt.txt
    ├── fashion-similarity-evaluation-prompt.txt
    ├── gender-accuracy-evaluation-prompt.txt
    ├── grounded-data-evaluation-prompt.txt
    ├── intent-support-evaluation-prompt.txt
    ├── product-id-evaluation-prompt.txt
    └── rewritten-query-evaluation-prompt.txt
```

## Default Evaluation Templates

The `default/` subdirectory contains general-purpose evaluation prompts suitable for most AI agent use cases:

### default/similarity-evaluation-prompt.txt
Evaluates semantic similarity between the expected response and the actual agent response. Focuses on:
- Semantic meaning alignment
- Content coverage
- Information accuracy
- Intent alignment

### default/accuracy-evaluation-prompt.txt
Assesses the factual accuracy of the agent's response. Evaluates:
- Factual correctness
- Information completeness
- Alignment with expected response
- Absence of misleading information
- Contextual accuracy

### default/relevancy-evaluation-prompt.txt
Measures how relevant the agent's response is to the user's input. Considers:
- Topic alignment
- User intent fulfillment
- Information usefulness
- Focus and directness
- Completeness

### default/default-evaluation-prompt.txt
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

All templates use a consistent 0-1 scoring scale:
- **0-0.2**: Very poor quality
- **0.3-0.4**: Poor quality with major issues
- **0.5-0.6**: Fair quality with notable room for improvement
- **0.7-0.8**: Good quality with minor issues
- **0.9-1.0**: Excellent quality, meets or exceeds expectations

## Output Format

All templates require the LLM to return a JSON response with:
```json
{
  "score": [0-1],
  "reasoning": "Detailed explanation of the score"
}
```

## Uploading Templates to Azure Blob Storage

To upload these templates to Azure Blob Storage for use in production:

1. Ensure the storage account and container are configured in your application settings
2. Use the `BlobStorageService.UploadPromptTemplateAsync()` method
3. The templates will be stored in the container specified by `Storage:PromptTemplateContainerName`

Example for default prompts:
```csharp
await blobStorageService.UploadPromptTemplateAsync(
    "default/similarity-evaluation-prompt.txt", 
    File.ReadAllText("prompt-templates/default/similarity-evaluation-prompt.txt"));
```

Example for fashion prompts:
```csharp
await blobStorageService.UploadPromptTemplateAsync(
    "fashion/gender-accuracy-evaluation-prompt.txt", 
    File.ReadAllText("prompt-templates/fashion/gender-accuracy-evaluation-prompt.txt"));
```

## Fashion Industry Specific Templates

The `fashion/` subdirectory contains specialized evaluation prompts for fashion and clothing e-commerce systems. See `fashion/README.md` for detailed documentation.

### General Fashion Evaluation Prompts

**fashion/fashion-accuracy-evaluation-prompt.txt**
- Evaluates factual accuracy specific to fashion products
- Verifies product attributes (color, gender, size, style)
- Checks product ID accuracy (msind_productid)
- Validates fashion domain knowledge and terminology
- Assesses catalog grounding and metadata accuracy

**fashion/fashion-relevancy-evaluation-prompt.txt**
- Measures relevance of fashion product recommendations
- Evaluates product type and attribute alignment
- Assesses user intent fulfillment for fashion queries
- Checks focus and precision in product recommendations

**fashion/fashion-similarity-evaluation-prompt.txt**
- Compares expected vs actual fashion product responses
- Evaluates product match similarity (msind_productid comparison)
- Assesses attribute consistency across responses
- Validates semantic similarity in fashion recommendations

### Specialized Fashion Evaluation Prompts

**fashion/gender-accuracy-evaluation-prompt.txt**
- Evaluates if products match requested gender category
- Validates gender-specific filtering (women's, men's, unisex, etc.)
- Assesses cross-gender product exclusion

**fashion/color-accuracy-evaluation-prompt.txt**
- Validates products match requested colors
- Evaluates color family accuracy
- Assesses color synonym recognition (e.g., navy vs dark blue)

**fashion/product-id-evaluation-prompt.txt**
- Compares msind_productid values between expected and actual responses
- Evaluates complete coverage of expected products
- Assesses precision and relevance of product IDs returned

**fashion/intent-support-evaluation-prompt.txt**
- Validates IsIntentSupported (from Flow) accuracy
- Evaluates Intent Type classification correctness
- Assesses consistency between intent support and type

**fashion/rewritten-query-evaluation-prompt.txt**
- Evaluates RewrittenUserQuery relevance to original input
- Assesses semantic preservation and clarity enhancement
- Validates information retention in query rewriting

**fashion/grounded-data-evaluation-prompt.txt**
- Verifies products come from actual catalog (not hallucinated)
- Evaluates presence of catalog identifiers (msind_productid)
- Assesses structured product data and consistency

## Creating Domain-Specific Prompt Sets

You can create additional domain-specific prompt sets similar to the `fashion/` directory:

1. Create a new subdirectory (e.g., `healthcare/`, `finance/`, `legal/`)
2. Copy and customize the default prompts for your domain
3. Add specialized evaluation prompts for domain-specific requirements
4. Create a README.md in the subdirectory documenting the prompts
5. Update this main README to reference the new prompt set

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
