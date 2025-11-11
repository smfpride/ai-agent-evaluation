# AI Agent Evaluation System

A cloud-native, serverless Azure Functions solution that automates the testing and evaluation of Copilot Studio agents using an LLM-as-a-Judge approach. The system executes test cases against Copilot Studio agents via the Direct Line API and uses Azure OpenAI (GPT-4o) to assess responses across three evaluation types: Similarity, Accuracy, and Relevancy.

## Overview

This system provides automated quality assurance for Copilot Studio agents through:

- **Automated Testing**: Execute test cases against Copilot Studio agents via Direct Line API
- **LLM-Based Evaluation**: Use Azure OpenAI (GPT-4o) to score responses with human-like judgment
- **Multi-Turn Conversations**: Support for complex conversation scenarios with context preservation
- **Scalable Processing**: Handle up to 50,000 test cases per evaluation run
- **Comprehensive Results**: Store detailed results, reasoning, and metrics in Dataverse

## Architecture

The system uses a serverless, queue-based architecture for massive scalability:

```
HTTP Trigger → Orchestrator Function → Service Bus Queues → Worker Functions → 
Copilot Studio (Direct Line) → Azure OpenAI (Judge) → Dataverse (Results) → 
Monitor Function (Status Updates)
```

**📖 For detailed architecture information, see [Architecture Documentation](./docs/ARCHITECTURE.md)**

### Key Components

- **Orchestrator Function**: HTTP-triggered function that initiates evaluation runs and queues test cases
- **Worker Functions**: Service Bus-triggered functions that execute test cases (single-turn and multi-turn)
- **Monitor Function**: Timer-triggered function that tracks progress and updates completion status
- **Azure Service Bus**: Message queues for scalable test case distribution
- **Azure OpenAI**: GPT-4o model providing LLM-as-a-Judge evaluation
- **Microsoft Dataverse**: Storage for test cases, results, and run metadata
- **Copilot Studio**: Target agents accessed via Direct Line API

## Features

### Core Capabilities

- **Three Evaluation Types**: 
  - **Similarity** (778760000): Semantic meaning alignment
  - **Accuracy** (778760001): Factual correctness
  - **Relevancy** (778760002): Response appropriateness
  
- **Multi-Turn Support**: Execute complex conversation flows with context preservation across turns

- **Massive Scale**: Process up to 50,000 test cases per run with automatic scaling

- **Managed Identity**: Secure authentication to all Azure services without connection strings

- **Comprehensive Monitoring**: Application Insights integration with custom metrics and dashboards

## Technology Stack

- **.NET 8**: Function runtime (isolated process model)
- **Azure Functions v4**: Serverless compute platform
- **Azure Service Bus**: Message queue (2 queues: single-turn and multi-turn)
- **Azure OpenAI**: GPT-4o model for LLM judge
- **Microsoft Dataverse**: Data storage and retrieval
- **Azure Key Vault**: External service secrets management
- **Application Insights**: Monitoring and telemetry

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
  "DataverseEnvironmentUrl": "https://yourorg.crm.dynamics.com/",
  "AzureOpenAIEndpoint": "https://your-openai.openai.azure.com/",
  "ServiceBusNamespace": "yournamespace.servicebus.windows.net",
  "KeyVaultUrl": "https://your-keyvault.vault.azure.net/"
}
```

See [Architecture Documentation](./docs/ARCHITECTURE.md#configuration-reference) for complete configuration details.

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
│   │   ├── Services/               # Business logic services
│   │   ├── Models/                 # Data models
│   │   └── Extensions/             # Utility extensions
│   ├── infrastructure/             # Bicep IaC templates
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
- Azure SDK for .NET
- xUnit for testing
- Moq for mocking

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
