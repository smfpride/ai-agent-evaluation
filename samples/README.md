# Sample Data

Importable sample data for testing a deployed AI Agent Evaluation environment end to end.

| File | Contents |
|------|----------|
| `fashion/fashion-sample-test-set.json` | Fashion e-commerce test set: 11 single-turn and 3 multi-turn test cases, plus evaluator presets for runs |
| `Import-SampleData.ps1` | Idempotent importer for evaluator types, the test set, test cases and turns, and optionally an evaluation run |

## Prerequisites

- The `EvalsTool` solution is imported into the Dataverse environment.
- `az login` with an account that can write to that environment.
- A **Judge Agent** record (Azure OpenAI type) exists. For example, to create one:

  | Field | Value |
  |-------|-------|
  | Name | Azure OpenAI GPT-4o |
  | Judge Type | Azure OpenAI |
  | AOAI Endpoint | `https://<openai-account>.openai.azure.com/` |
  | AOAI Deployment Name | `gpt-4o` |

- To create a run: an **Agent to Eval** record with the Copilot Studio Direct Line token endpoint in *Direct Line Speech URL*.

## Usage

Import evaluator types (from `backend/prompt-templates`) and the fashion test set:

```powershell
.\samples\Import-SampleData.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -ImportEvaluators
```

Create an evaluation run against your agent and start it:

```powershell
.\samples\Import-SampleData.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -AgentName "<Agent to Eval name>" -RunPreset "Fashion Core" -StartRun
```

Without `-StartRun`, the run is created in the *Created* state. Set its status to **Execute/Run** in the model-driven app to start it.

Re-running the importer updates records in place:

| Record | Matched by |
|--------|------------|
| Evaluator types | Name |
| Test set | Name |
| Test cases | `mcicaps_importid` |
| Turns | Test case + sequence |

Each invocation with `-AgentName` creates a new evaluation run.

## Run presets

| Preset | Evaluators | When to use |
|--------|-----------|-------------|
| Fashion Core | Fashion - Accuracy, Fashion - Relevancy, Fashion - Gender Accuracy, Fashion - Color Accuracy | First run against any fashion shopping agent |
| Fashion Full | All 9 fashion evaluators | Agents that emit the structured response format (see below) |
| General | Accuracy, Relevancy, Similarity | Domain-neutral baseline |

## About the expected responses

Expected responses describe the criteria a correct answer must meet, for example "all items are women's and navy". They don't contain exact product text, so the LLM judge can score any fashion agent without a matching catalog.

The following evaluators assume the agent returns the structured format described in [`backend/prompt-templates/fashion/README.md`](../backend/prompt-templates/fashion/README.md):

- **Fashion - Product Id** expects `msind_productid` values in a `**Products:**` JSON block.
- **Fashion - Intent Support** and **Fashion - Rewritten Query** expect the `IsIntentSupported`, `Intent Type` and `RewrittenUserQuery` fields.

For agents that don't emit this format, use the *Fashion Core* preset. To use *Product Id*, replace the expected responses with your catalog's product IDs.

## Test cases

| Import ID | Name | Focus |
|-----------|------|-------|
| fashion-st-01 | Women's navy blouses | gender, color |
| fashion-st-02 | Men's casual work pants | gender, occasion relevancy |
| fashion-st-03 | Red dress for a wedding guest | color, occasion |
| fashion-st-04 | Women's white sneakers | gender, color, footwear |
| fashion-st-05 | Men's black leather belt | accessories, "none available" handling |
| fashion-st-06 | Unisex grey hoodie | unisex gender handling |
| fashion-st-07 | Women's plus-size black jeans | size constraint |
| fashion-st-08 | Kids' yellow rain jacket | kids category |
| fashion-st-09 | Something green for summer | ambiguous request / clarification |
| fashion-st-10 | Weather in Seattle | out-of-scope intent |
| fashion-st-11 | Men's summer dresses | gender-mismatch edge case |
| fashion-mt-01 | Blue shirts shopping (2 turns) | context carry-over: color + size |
| fashion-mt-02 | Summer dress refinement (3 turns) | progressive filtering |
| fashion-mt-03 | Outfit building (3 turns) | cross-sell, context retention |
