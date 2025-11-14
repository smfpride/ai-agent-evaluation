# Integration Documentation

This folder contains integration guides for external services and components used by the AI Agent Evaluation System.

## Files

### Service Integrations
- **[copilot-studio-token-endpoint-integration.md](copilot-studio-token-endpoint-integration.md)** - Copilot Studio Direct Line API integration with token endpoint authentication
- **[SERVICE_BUS_SETUP.md](SERVICE_BUS_SETUP.md)** - Azure Service Bus queue configuration and setup

### Implementation Summaries
- **[MONITOR_FUNCTION_IMPLEMENTATION.md](MONITOR_FUNCTION_IMPLEMENTATION.md)** - MonitorFunction implementation for tracking evaluation run completion
- **[HANDOFF_SUMMARY.md](HANDOFF_SUMMARY.md)** - Project handoff summary including Dataverse OAuth integration

## Integration Overview

The system integrates with:
- **Copilot Studio**: Direct Line API for agent conversations
- **Dataverse**: Test case storage, results persistence (OAuth)
- **Azure Service Bus**: Single-turn test case message queuing
- **Azure OpenAI**: LLM-as-a-Judge evaluation
- **Azure Storage**: Durable Functions state persistence

## Using Integration Documentation

### Setting Up New Integrations
1. Review relevant integration guide (e.g., copilot-studio-token-endpoint-integration.md)
2. Configure authentication per [Security Documentation](../security/)
3. Test integration following guide procedures

### Troubleshooting Integrations
1. Check [HANDOFF_SUMMARY.md](HANDOFF_SUMMARY.md) for known issues
2. Review [Production Runbook](../operations/production-runbook.md) for troubleshooting steps
3. Verify authentication configuration

### Understanding Integration Architecture
1. Read integration-specific documentation
2. Review [Architecture Overview](../ARCHITECTURE.md) for data flows
3. Check [PRD](../prd/PRD-Agent-Evaluation-Function.md) for API contracts

## Related Documentation

- [**Durable Functions Guide**](../durable-functions-guide.md) - Multi-turn orchestration patterns (ROOT LEVEL)
- [Architecture](../ARCHITECTURE.md) - System integration architecture
- [Security](../security/ManagedIdentityImplementation.md) - Authentication for integrations
- [Operations Runbook](../operations/production-runbook.md) - Troubleshooting integration issues

---

**Last Updated**: November 14, 2025
