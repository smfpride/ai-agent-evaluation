# Fashion Industry Evaluation Prompts

This directory contains specialized evaluation prompts designed for fashion and clothing e-commerce AI agents. These prompts help evaluate the accuracy, relevancy, and quality of product recommendations and search results in the fashion domain.

## Overview

Fashion e-commerce requires specialized evaluation criteria that go beyond general text evaluation. These prompts assess:

- Product attribute accuracy (color, gender, size, style, material)
- Product catalog grounding and retrieval
- Fashion domain terminology and knowledge
- Shopping intent understanding
- Query interpretation and rewriting for fashion searches

## Prompt Categories

### General Fashion Evaluation

These prompts provide comprehensive evaluation tailored to fashion e-commerce:

1. **fashion-accuracy-evaluation-prompt.txt** - Overall accuracy assessment for fashion product responses
2. **fashion-relevancy-evaluation-prompt.txt** - Relevance of product recommendations to user queries
3. **fashion-similarity-evaluation-prompt.txt** - Semantic similarity between expected and actual fashion responses

### Attribute-Specific Evaluation

These prompts focus on specific product attributes:

4. **gender-accuracy-evaluation-prompt.txt** - Validates correct gender categorization (women's, men's, unisex)
5. **color-accuracy-evaluation-prompt.txt** - Verifies color accuracy and color family matching

### Technical Evaluation

These prompts assess technical aspects of the fashion agent response:

6. **product-id-evaluation-prompt.txt** - Compares product IDs (msind_productid) for exact product matching
7. **intent-support-evaluation-prompt.txt** - Validates intent classification and support flags
8. **rewritten-query-evaluation-prompt.txt** - Evaluates query rewriting quality and relevance
9. **grounded-data-evaluation-prompt.txt** - Verifies catalog grounding vs hallucination

## Response Structure Expected

These prompts are designed to evaluate responses with the following structure:

```
[Response Text]

**IsIntentSupported (from Flow):** [true/false]
**Intent Type:** [intent classification]
**RewrittenUserQuery:** [rewritten query text]

**Products:**
[JSON array of products with msind_productid and attributes]
```

## Usage Guidelines

### Choosing the Right Prompt

- **For overall quality assessment**: Use fashion-accuracy, fashion-relevancy, or fashion-similarity prompts
- **For specific attribute validation**: Use gender-accuracy or color-accuracy prompts
- **For product matching verification**: Use product-id-evaluation-prompt
- **For system behavior validation**: Use intent-support, rewritten-query, or grounded-data prompts

### Best Practices

1. **Combine Multiple Evaluations**: Use multiple prompts together for comprehensive assessment
   - Example: fashion-accuracy + product-id + gender-accuracy for women's clothing search

2. **Focus on Critical Attributes**: Prioritize the attributes most important for your use case
   - For apparel: gender and color are often critical
   - For accessories: style and material may be more important

3. **Validate Grounding**: Always include grounded-data evaluation to catch hallucinations

4. **Check Intent Handling**: Use intent-support prompt to ensure queries are understood correctly

### Common Evaluation Scenarios

**Scenario 1: Basic Product Search Validation**
- fashion-accuracy-evaluation-prompt.txt
- product-id-evaluation-prompt.txt
- grounded-data-evaluation-prompt.txt

**Scenario 2: Gendered Product Search**
- gender-accuracy-evaluation-prompt.txt
- fashion-relevancy-evaluation-prompt.txt
- product-id-evaluation-prompt.txt

**Scenario 3: Color-Specific Search**
- color-accuracy-evaluation-prompt.txt
- product-id-evaluation-prompt.txt
- fashion-accuracy-evaluation-prompt.txt

**Scenario 4: Complex Multi-Attribute Search**
- fashion-accuracy-evaluation-prompt.txt
- gender-accuracy-evaluation-prompt.txt
- color-accuracy-evaluation-prompt.txt
- product-id-evaluation-prompt.txt
- rewritten-query-evaluation-prompt.txt

## Scoring Interpretation

All prompts use a 0-1 scoring scale:

- **0.9-1.0**: Excellent - Production-ready quality
- **0.7-0.8**: Good - Minor improvements needed
- **0.5-0.6**: Fair - Notable issues to address
- **0.3-0.4**: Poor - Significant problems
- **0-0.2**: Unacceptable - Major failures

### Setting Quality Thresholds

Recommended minimum scores for production:

- **Product ID matching**: 0.9+ (must return correct products)
- **Gender accuracy**: 0.9+ (critical for gendered products)
- **Color accuracy**: 0.8+ (important but some color variation acceptable)
- **Intent support**: 0.9+ (must understand user intent)
- **Grounded data**: 0.9+ (must not hallucinate products)
- **Overall accuracy**: 0.8+ (comprehensive quality)
- **Relevancy**: 0.8+ (must meet user needs)

## Integration with Test Framework

These prompts are designed to work with the AI Agent Evaluation Function application. They:

- Support standard placeholder replacement: `{UserInput}`, `{ExpectedResponse}`, `{ActualResponse}`, `{TestType}`
- Return structured JSON output: `{"score": [0-1], "reasoning": "..."}`
- Can be loaded from Azure Blob Storage or used as embedded resources
- Work with the LLMJudgeService for automated evaluation

## Customization

To customize these prompts for your specific fashion catalog or requirements:

1. **Adjust evaluation criteria**: Modify the aspects being evaluated
2. **Add domain-specific attributes**: Include brand, price range, occasion, season, etc.
3. **Modify scoring guidelines**: Adjust thresholds based on your quality standards
4. **Enhance reasoning requirements**: Request specific details in the evaluation reasoning
5. **Add business rules**: Include specific catalog requirements or constraints

## Example Test Cases

### Example 1: Women's Navy Blouse Search

**User Input**: "Show me women's navy blouses"

**Expected Products**: Should contain msind_productid values for women's navy blouses

**Evaluation Prompts**:
- gender-accuracy-evaluation-prompt.txt (expect 1.0 - all products should be women's)
- color-accuracy-evaluation-prompt.txt (expect 0.9+ - all should be navy/dark blue)
- product-id-evaluation-prompt.txt (expect 0.9+ - should match expected IDs)

### Example 2: Men's Casual Pants

**User Input**: "I need men's casual pants for work"

**Expected Products**: Should contain msind_productid values for men's business casual pants

**Evaluation Prompts**:
- fashion-relevancy-evaluation-prompt.txt (are products appropriate for work?)
- gender-accuracy-evaluation-prompt.txt (all products should be men's)
- grounded-data-evaluation-prompt.txt (products from real catalog?)

## Support and Maintenance

When updating these prompts:

1. **Test thoroughly**: Validate changes with representative test cases
2. **Document changes**: Update this README with any modifications
3. **Version control**: Track prompt versions for reproducibility
4. **Validate JSON output**: Ensure prompts still return valid JSON
5. **Re-upload to storage**: Update prompts in Azure Blob Storage after changes

## Related Documentation

- Parent README: `../README.md` - General evaluation prompt documentation
- Architecture: `../../docs/ARCHITECTURE.md` - System architecture overview
- PRD: `../../docs/prd/PRD-Agent-Evaluation-Function.md` - Product requirements
