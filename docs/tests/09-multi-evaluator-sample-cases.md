# Multi-Evaluator Sample Test Cases

**Version**: 1.0  
**Date**: 2025-11-12  
**Purpose**: Provide concrete sample data illustrating single-turn and multi-turn evaluation units across multiple evaluator types, including Not Applicable (NA) scenarios.

## 1. Evaluator Types Used
| Name | GUID (placeholder) | requiresExpectedResponse | Notes |
|------|--------------------|--------------------------|-------|
| Similarity | 11111111-1111-1111-1111-111111111111 | false | Semantic overlap focus |
| Accuracy   | 22222222-2222-2222-2222-222222222222 | true  | Requires expected response for factual comparison |
| Relevancy  | 33333333-3333-3333-3333-333333333333 | false | Prompt alignment |

## 2. Single-Turn Cases
| TestCaseId | Prompt | Expected Response | NA Evaluators | Total Units | Notes |
|------------|--------|------------------|--------------|-------------|-------|
| ST-01 | "List three US states." | "California, Texas, Florida." | (none) | 3 | All evaluators execute |
| ST-02 | "Summarize the product benefits." | "" (empty) | Accuracy | 3 (1 NA + 2 active) | Accuracy short-circuits NA |
| ST-03 | "What is 2+2?" | "4" | (none) | 3 | Baseline numeric accuracy |

### ST-02 Breakdown
Evaluation units:
- Similarity: Prompt + empty expected → still evaluates (template tolerates blank)  
- Accuracy: requiresExpectedResponse=true & missing → NA (statuscode 778760004, score 0.0)  
- Relevancy: evaluates normally

## 3. Multi-Turn Cases
| TestCaseId | Turns | Turn Prompts | Turn Expected Responses | NA Turns (Accuracy) | Evaluators | Total Units Calculation |
|------------|-------|--------------|-------------------------|---------------------|------------|-------------------------|
| MT-01 | 3 | T1:"Start order" T2:"Add item" T3:"Checkout" | All filled | None | 3 | (3 turns ×3 evals)+3 aggregates = 12 |
| MT-02 | 2 | T1:"Get weather" T2:"Advise clothing" | T1 empty, T2 filled | T1 only | 3 | (2×3)+3=9 (Accuracy turn1 NA) |
| MT-03 | 2 | T1:"Compute invoice" T2:"Apply discount" | Both empty | Both | 3 | (2×3)+3=9 (All Accuracy turns NA; aggregate Accuracy NA) |

### MT-02 Detailed Units
| Unit | statuscode | Notes |
|------|------------|-------|
| T1 Similarity | 778760002 | Expected blank tolerated |
| T1 Accuracy | 778760004 | NA (missing expected) |
| T1 Relevancy | 778760002 | Evaluated |
| T2 Similarity | 778760002 | Evaluated |
| T2 Accuracy | 778760002 | Evaluated (expected present) |
| T2 Relevancy | 778760002 | Evaluated |
| Aggregate Similarity | 778760002 | Average of turn scores |
| Aggregate Accuracy | 778760002 | Average of T2 only (T1 excluded) |
| Aggregate Relevancy | 778760002 | Average of both turns |

### MT-03 Accuracy Aggregate Logic
All Accuracy turn results NA → Aggregate result:
- statuscode 778760004
- score 0.0
- reasoning: "All turns Not Applicable (missing expected responses)."

## 4. Expected Response Examples
| TestCaseId | Turn | Expected Response Example |
|------------|------|---------------------------|
| MT-01 | 1 | "Hello! How can I help with your order today?" |
| MT-01 | 2 | "Item added to cart." |
| MT-01 | 3 | "Order complete. Confirmation #12345" |
| MT-02 | 2 | "You should wear a light jacket; temp around 60F." |
| MT-03 | 1 | (blank) |
| MT-03 | 2 | (blank) |

## 5. Aggregation Examples (Pseudo JSON)
Successful aggregate (Similarity MT-01):
```json
{
  "testCaseId": "MT-01",
  "evaluatorTypeId": "11111111-1111-1111-1111-111111111111",
  "isAggregate": true,
  "statuscode": 778760002,
  "score": 0.87,
  "reasoning": "Turn scores: [0.85, 0.90, 0.86] average excludes none"
}
```
All-NA aggregate (Accuracy MT-03):
```json
{
  "testCaseId": "MT-03",
  "evaluatorTypeId": "22222222-2222-2222-2222-222222222222",
  "isAggregate": true,
  "statuscode": 778760004,
  "score": 0.0,
  "reasoning": "All turns Not Applicable (missing expected responses)."
}
```

## 6. Acceptance Criteria Mapping
| Criterion | Sample Case Covering |
|-----------|----------------------|
| NA short-circuit single-turn | ST-02 Accuracy |
| Partial NA multi-turn | MT-02 Accuracy turn 1 |
| All NA multi-turn aggregate | MT-03 Accuracy |
| Aggregation excludes NA turns | MT-02 Accuracy aggregate |
| Progress counting includes NA units | ST-02 & MT-03 |

## 7. Test Data Preparation Guidance
1. Create evaluator types with GUID placeholders replaced by real IDs.
2. For Accuracy set `mcicaps_requiresexpectedresponse = true`.
3. Generate test cases; leave expected response blank intentionally for NA scenarios.
4. Create test case turns for multi-turn cases ensuring sequence order.
5. Associate evaluator types to evaluation run via join table.

## 8. Query Snippets (Dataverse FetchXML Pseudo)
Fetch NA results for a run:
```xml
<fetch>
  <entity name="mcicaps_evaluationresults">
    <attribute name="mcicaps_evaluationresultsid" />
    <filter>
      <condition attribute="mcicaps_evaluationrun" operator="eq" value="{runId}" />
      <condition attribute="statuscode" operator="eq" value="778760004" />
    </filter>
  </entity>
</fetch>
```

Fetch aggregates excluding NA:
```xml
<fetch>
  <entity name="mcicaps_evaluationresults">
    <attribute name="mcicaps_evaluationresultsid" />
    <attribute name="mcicaps_score" />
    <filter>
      <condition attribute="mcicaps_evaluationrun" operator="eq" value="{runId}" />
      <condition attribute="mcicaps_isaggregate" operator="eq" value="1" />
      <condition attribute="statuscode" operator="ne" value="778760004" />
    </filter>
  </entity>
</fetch>
```

## 9. Future Expansion Notes
- Add evaluator weighting column if ordering or weighting becomes necessary.
- Consider tagging test cases with domain for domain-specific evaluator sets.

**End of Document**