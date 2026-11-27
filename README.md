# AI Agent Evaluation System

A cloud-native, serverless Azure Functions solution that automates the testing and evaluation of Copilot Studio agents using an LLM-as-a-Judge approach. The system executes test cases against Copilot Studio agents via the Direct Line API and uses Azure OpenAI (GPT-4o) to assess responses across three evaluation types: Similarity, Accuracy, and Relevancy.

## Overview

This system provides automated quality assurance for Copilot Studio agents through:

- **Automated Testing**: Execute test cases against Copilot Studio agents via Direct Line API
- **LLM-Based Evaluation**: Use Azure OpenAI (GPT-4o) to score responses with human-like judgment
- **Multi-Turn Conversations**: Support for complex conversation scenarios with context preservation
- **Scalable Processing**: Handle up to 50,000 test cases per evaluation run
- **Comprehensive Results**: Store detailed results, reasoning, and metrics in Dataverse
- **Flexible Evaluation Types**: Extensible evaluator types stored in Dataverse with custom prompts
- **Dual Execution Paths**: Service Bus workers for standard cases, Durable Functions for very long conversations

## Architecture

The system uses a serverless, queue-based architecture for massive scalability:

```
HTTP Trigger → Orchestrator Function → Service Bus Queues (2) → Worker Functions → 
Copilot Studio (Direct Line) → Azure OpenAI (Judge) → Dataverse (Results) → 
Monitor Function (Status Updates)
```

**Alternative Path for Long Conversations**:
```
Orchestrator Function → Durable Functions Orchestrator → Activities (Sequential Turns) →
Copilot Studio + Azure OpenAI → Dataverse (Results)
```

**📖 For detailed architecture information, see [Architecture Documentation](./docs/ARCHITECTURE.md)**

### Key Components

- **Orchestrator Function**: HTTP-triggered function that initiates evaluation runs and routes test cases
- **Worker Function**: Service Bus-triggered function that executes single-turn test cases
- **Durable Functions Orchestrator**: Long-running orchestration for all multi-turn conversations
- **Monitor Function**: Timer-triggered function that tracks progress and updates completion status
- **Azure Service Bus**: Message queue for scalable single-turn test case distribution
- **Azure OpenAI**: GPT-4o model providing LLM-as-a-Judge evaluation (Managed Identity)
- **Microsoft Dataverse**: Storage for test cases, prompts, results, and run metadata
- **Copilot Studio**: Target agents accessed via Direct Line API

## Features

### Core Capabilities

- **Extensible Evaluation Types**: 
  - **Built-in**: Similarity (semantic alignment), Accuracy (factual correctness), Relevancy (response appropriateness)
  - **Custom**: Domain-specific evaluators (fashion, healthcare, finance, etc.) via Dataverse
  - **Prompt Templates**: Stored in Dataverse EvaluatorType records for easy management
  
- **Multi-Turn Support**: 
  - Execute complex conversation flows with context preservation across turns
  - Durable Functions orchestration for unlimited execution time (no 5-minute limit)
  - Per-turn evaluation with aggregate results
  - Supports conversations of any length (1 turn to 100+ turns)

- **Massive Scale**: 
  - Process up to 50,000 test cases per run with automatic scaling
  - Hybrid execution: Service Bus for single-turn, Durable Functions for multi-turn
  - Up to 1000 concurrent single-turn workers plus unlimited multi-turn orchestrations

- **Managed Identity**: 
  - Secure authentication to all Azure services without connection strings
  - RBAC-based access control
  - OAuth for Dataverse (Managed Identity not supported)

- **Comprehensive Monitoring**: 
  - Application Insights integration with custom metrics and dashboards
  - Real-time progress tracking with percentages
  - Progress-based stall detection (configurable threshold, default 30 min)
  - Allows long evaluations to run as long as they make continuous progress
  - Detailed logging and distributed tracing

## Technology Stack

- **.NET 8**: Function runtime (isolated process model)
- **Azure Functions v4**: Serverless compute platform
- **Azure Durable Functions**: Long-running orchestrations for multi-turn conversations
- **Azure Service Bus**: Message queue for single-turn test case distribution
- **Azure OpenAI**: GPT-4o model for LLM judge (Managed Identity)
- **Microsoft Dataverse**: Data storage, prompt templates, and retrieval
- **Azure Key Vault**: External service secrets (Dataverse OAuth only)
- **Azure Storage**: Function state and Durable Functions orchestration state
- **Application Insights**: Monitoring and telemetry

**Note**: Infrastructure provisions a `multi-turn-test-case-processing` queue, but it is not actively used. Multi-turn execution uses Durable Functions orchestration.

## Getting Started

### Prerequisites

- Azure subscription
- .NET 8 SDK
- Azure Functions Core Tools v4
- Visual Studio 2022 or VS Code with Azure Functions extension
- Access to Microsoft Dataverse environment
- Azure OpenAI resource with GPT-4o deployment

### Configuration

The system uses managed identity for all Azure service authentication. Configuration is managed through application settings:

```json
{
  "DataverseConnectionString": "@Microsoft.KeyVault(VaultName=kv-name;SecretName=DataverseConnectionString)",
  "AzureOpenAIEndpoint": "https://your-openai.openai.azure.com/",
  "ServiceBus__Namespace": "yournamespace.servicebus.windows.net",
  "ServiceBus__QueueName": "test-case-processing",
  "ServiceBusNamespace__fullyQualifiedNamespace": "yournamespace.servicebus.windows.net",
  "KeyVaultUrl": "https://your-keyvault.vault.azure.net/",
  "CopilotStudio__TimeoutSeconds": "60",
  "AzureOpenAI__MaxRetryAttempts": "5"
}
```

**Note**: Only Dataverse requires OAuth connection string (stored in Key Vault) as it does not support Managed Identity.

### Local Development

1. Clone the repository
2. Copy `local.settings.example.json` to `local.settings.json`
3. Configure connection settings
4. Run `func start` in the `backend/AiAgentEvaluation` directory

### Deployment

The project includes Bicep infrastructure-as-code templates:

```bash
cd backend/infrastructure
az deployment group create \
  --resource-group rg-ai-eval-prod \
  --template-file main.bicep \
  --parameters main.parameters.prod.json
```

**📖 See [Deployment Checklist](./docs/deployment/DEPLOYMENT_CHECKLIST.md) for step-by-step deployment instructions**

After deployment, load evaluator types and a fashion sample test set with [samples/Import-SampleData.ps1](./samples/README.md).

## Documentation

Comprehensive documentation is available in the [`docs/`](./docs/) folder:

### Core Documentation
- **[Architecture](./docs/ARCHITECTURE.md)**: System architecture, components, and data flow
- **[Product Requirements](./docs/prd/PRD-Agent-Evaluation-Function.md)**: Detailed functional and technical requirements
- **[Implementation Summary](./docs/implementation/IMPLEMENTATION_SUMMARY.md)**: Development progress and decisions

### Deployment & Operations
- **[Deployment Checklist](./docs/deployment/DEPLOYMENT_CHECKLIST.md)**: Step-by-step deployment guide
- **[Production Runbook](./docs/operations/production-runbook.md)**: Operational procedures
- **[Monitoring Configuration](./docs/operations/monitoring-configuration.md)**: Observability setup

### Security
- **[Managed Identity Implementation](./docs/security/ManagedIdentityImplementation.md)**: Secure authentication setup
- **[Security Hardening Checklist](./docs/security/security-hardening-checklist.md)**: Security best practices

### User Stories & Tests
- **[Stories](./docs/stories/)**: Detailed implementation stories for each component
- **[Tests](./docs/tests/)**: Test plans and results for unit, integration, and performance testing

## Project Structure

```
ai-agent-evaluation/
├── backend/
│   ├── AiAgentEvaluation/          # Azure Functions project (.NET 8)
│   │   ├── Functions/              # Function implementations
│   │   │   ├── OrchestratorFunction.cs
│   │   │   ├── WorkerFunction.cs (single-turn)
│   │   │   ├── MultiTurnWorkerFunction.cs (unused legacy code)
│   │   │   ├── MultiTurnDurableOrchestrator.cs (primary multi-turn execution)
│   │   │   ├── MultiTurnDurableActivities.cs
│   │   │   ├── MonitorFunction.cs
│   │   │   └── HealthCheckFunction.cs
│   │   ├── Services/               # Business logic services
│   │   ├── Models/                 # Data models
│   │   └── Extensions/             # Utility extensions
│   ├── infrastructure/             # Bicep IaC templates
│   ├── prompt-templates/           # Sample prompt templates
│   │   ├── default/               # General-purpose prompts
│   │   └── fashion/               # Fashion industry prompts
│   └── tests/                      # Unit and integration tests
├── docs/                           # Documentation
│   ├── ARCHITECTURE.md            # System architecture
│   ├── deployment/                # Deployment guides
│   ├── operations/                # Operations guides
│   ├── security/                  # Security documentation
│   ├── stories/                   # Implementation stories
│   └── tests/                     # Test documentation
└── README.md                      # This file
```
## API Endpoints

### Orchestrator Endpoint
```http
POST /api/orchestrator
Content-Type: application/json

{
  "evaluationRunId": "guid"
}
```

### Health Check Endpoint
```http
GET /api/health

Response: 200 OK
{
  "status": "healthy",
  "services": {
    "dataverse": "connected",
    "serviceBus": "connected",
    "azureOpenAI": "connected",
    "keyVault": "connected"
  },
  "timestamp": "2025-11-12T10:00:00Z"
}
```

**📖 See [Architecture Documentation](./docs/ARCHITECTURE.md#api-endpoints) for complete API reference**

## Performance

The system is designed for high-scale evaluation runs:

- **Orchestrator Response**: < 30 seconds (for 50,000 test cases)
- **Individual Test Case**: < 5 minutes per evaluation
- **Overall Throughput**: 1,000+ test cases per minute
- **Concurrent Workers**: Up to 1,000 function instances

## Development

This is a .NET 8 Azure Functions application built with:
- .NET 8 (isolated process model)
- Azure Functions v4
- Azure Durable Functions (for multi-turn orchestrations)
- Azure SDK for .NET
- xUnit for testing
- Moq for mocking

### Key Design Patterns
- **Queue-Based Architecture**: Decouple orchestration from execution
- **Managed Identity**: Secure Azure service authentication
- **Resilient Service Pattern**: Retry logic with exponential backoff
- **Atomic Operations**: Progress counter updates with single Dataverse calls
- **Durable Functions**: Long-running orchestrations for complex scenarios

## Contributing

1. Review the [Architecture Documentation](./docs/ARCHITECTURE.md)
2. Follow the development setup in [Getting Started](#getting-started)
3. Review the [Implementation Stories](./docs/stories/) for context
4. Run tests before submitting changes

## Support

For issues, questions, or contributions, please refer to:
- [Architecture Documentation](./docs/ARCHITECTURE.md)
- [Operations Runbook](./docs/operations/production-runbook.md)
- [Test Documentation](./docs/tests/)

## License

The Spark Template files and resources from GitHub are licensed under the terms of the MIT license, Copyright GitHub, Inc.
