# Test Cases: API Integration

## Test Suite: External Service Integration
**Components:** All Azure service integrations  
**Focus:** API connectivity, authentication, error handling

### Dataverse Integration Test Cases

#### TC-INT-001: Dataverse Authentication
**Scenario:** System authenticates to Dataverse using Managed Identity  
**Given:** Function app configured with Managed Identity
**When:** Function attempts to connect to Dataverse
**Then:**
- Authentication succeeds without credential prompts
- Connection established with appropriate permissions
- Token refresh handled automatically
- Access logged for audit purposes

#### TC-INT-002: Evaluation Run CRUD Operations
**Scenario:** Read and update evaluation run data in Dataverse  
**Given:** Valid evaluation run exists in mcicaps_evaluationruns table
**When:** System performs read and update operations
**Then:**
- Read operations return complete evaluation run data
- Status updates applied correctly with proper option set values
- Concurrent access handled without conflicts
- Change tracking maintained for audit

**Table: mcicaps_evaluationruns**
- Read: mcicaps_testset, mcicaps_passingthreshold, statuscode
- Update: statuscode, mcicaps_overallscore, mcicaps_overallpass

#### TC-INT-003: Test Case Data Retrieval
**Scenario:** Retrieve test cases associated with evaluation run  
**Given:** Test set linked to evaluation run with various test types
**When:** System queries test case data
**Then:**
- All test cases retrieved with correct filtering
- Test types (778760000, 778760001, 778760002) handled properly
- Large result sets paged efficiently
- Related data loaded correctly

#### TC-INT-004: Evaluation Results Storage
**Scenario:** Store individual evaluation results in Dataverse  
**Given:** Completed test case evaluation with score and reasoning
**When:** Worker function saves evaluation result
**Then:**
- Result stored in mcicaps_evaluationresults table
- All required fields populated correctly
- Relationships to test case and evaluation run maintained
- Unicode and special characters preserved

#### TC-INT-005: Dataverse Rate Limiting
**Scenario:** Handle Dataverse API rate limits gracefully  
**Given:** High volume of concurrent operations
**When:** Rate limits are encountered
**Then:**
- Rate limit errors detected and handled
- Retry logic applied with exponential backoff
- Operations queued and retried appropriately
- System remains stable under throttling

### Service Bus Integration Test Cases

#### TC-INT-006: Service Bus Authentication
**Scenario:** Authenticate to Service Bus using Managed Identity  
**Given:** Function app configured for Service Bus access
**When:** Functions connect to Service Bus
**Then:**
- Authentication completes without manual credentials
- Send and receive permissions verified
- Connection pooling optimized for performance
- Security context maintained across operations

#### TC-INT-007: Message Queuing and Dequeuing
**Scenario:** Queue and process test case messages  
**Given:** Test case messages ready for processing
**When:** Messages sent to and received from queue
**Then:**
- Messages serialized and deserialized correctly
- Message properties preserved (correlation ID, timestamp)
- FIFO ordering maintained where required
- Message size limits respected

#### TC-INT-008: Dead Letter Queue Handling
**Scenario:** Failed messages routed to dead letter queue  
**Given:** Messages that repeatedly fail processing
**When:** Retry limit exceeded
**Then:**
- Messages moved to dead letter queue
- Original failure context preserved
- Manual intervention capability available
- Dead letter queue monitoring active

#### TC-INT-009: Auto-Scaling Based on Queue Depth
**Scenario:** Function instances scale based on queue load  
**Given:** Large number of messages queued
**When:** Queue depth increases significantly
**Then:**
- Additional function instances activated automatically
- Processing throughput increases proportionally  
- Scaling back occurs when queue depth decreases
- No message loss during scaling events

#### TC-INT-010: Service Bus Session Management
**Scenario:** Handle Service Bus sessions for ordered processing  
**Given:** Messages requiring ordered processing
**When:** Session-enabled queue used
**Then:**
- Messages within session processed in order
- Session state maintained properly
- Session locks managed correctly
- Session timeout handled gracefully

### Copilot Studio Direct Line API Test Cases

#### TC-INT-011: Direct Line API Authentication
**Scenario:** Authenticate with Copilot Studio using Direct Line secret  
**Given:** Valid Direct Line secret for Copilot Studio agent
**When:** Worker establishes connection
**Then:**
- Authentication succeeds with valid secret
- Conversation token obtained successfully
- Token refresh handled automatically
- API key rotation supported

#### TC-INT-012: Conversation Lifecycle Management
**Scenario:** Manage complete conversation lifecycle  
**Given:** Test case requiring agent interaction
**When:** Worker initiates conversation flow
**Then:**
- Conversation started successfully
- User message sent and received by agent
- Agent response captured completely
- Conversation ended cleanly
- Resources released properly

#### TC-INT-013: Agent Response Handling
**Scenario:** Process various agent response formats  
**Given:** Different types of agent responses (text, cards, attachments)
**When:** Worker processes agent responses
**Then:**
- Text responses extracted correctly
- Rich content handled appropriately
- Large responses processed without truncation
- Response timing captured for analysis

#### TC-INT-014: Direct Line API Error Handling
**Scenario:** Handle various API errors gracefully  
**Given:** Different API error scenarios (401, 403, 429, 500)
**When:** Worker encounters API errors
**Then:**
- Error types identified and handled appropriately
- Retry logic applied for transient errors
- Permanent errors reported clearly
- Conversation state cleaned up on errors

#### TC-INT-015: Conversation Timeout Management
**Scenario:** Handle conversation timeouts appropriately  
**Given:** Long-running conversation exceeding timeout
**When:** Timeout limit reached
**Then:**
- Conversation terminated gracefully
- Partial response captured if available
- Timeout recorded for analysis
- Resources released properly

### Azure OpenAI Integration Test Cases

#### TC-INT-016: Azure OpenAI Authentication
**Scenario:** Authenticate to Azure OpenAI using Managed Identity  
**Given:** Function app with Azure OpenAI access permissions
**When:** Worker connects to Azure OpenAI
**Then:**
- Managed Identity authentication succeeds
- API access permissions verified
- Token management handled automatically
- Security context maintained

#### TC-INT-017: LLM Judge Prompt Processing
**Scenario:** Send evaluation prompts to Azure OpenAI  
**Given:** Test case data and evaluation prompt template
**When:** Worker sends evaluation request
**Then:**
- Prompt formatted correctly with test case context
- Request parameters applied appropriately
- Response received within timeout limits
- Token usage tracked for billing

#### TC-INT-018: Structured Response Parsing
**Scenario:** Parse LLM Judge JSON responses  
**Given:** Azure OpenAI response with evaluation data
**When:** Worker parses response
**Then:**
- JSON structure validated correctly
- Required fields (score, passed, reasoning) extracted
- Invalid responses handled gracefully
- Fallback logic applied for malformed responses

#### TC-INT-019: Azure OpenAI Rate Limiting
**Scenario:** Handle OpenAI rate limits and quotas  
**Given:** High volume of evaluation requests
**When:** Rate limits or quotas exceeded
**Then:**
- Rate limit errors detected and handled
- Requests retried with appropriate backoff
- Circuit breaker activated for persistent issues
- Alternative processing paths activated if available

#### TC-INT-020: Model Response Quality Validation
**Scenario:** Validate LLM Judge response quality  
**Given:** Evaluation responses from Azure OpenAI
**When:** Worker validates response content
**Then:**
- Score values within valid range (0-1)
- Reasoning provides meaningful explanation
- Evaluation assessment clear and actionable
- Response consistency verified across similar inputs

### Azure Blob Storage Integration Test Cases

#### TC-INT-021: Blob Storage Authentication
**Scenario:** Access Blob Storage using Managed Identity  
**Given:** Function app with Blob Storage access permissions
**When:** Worker accesses prompt templates
**Then:**
- Authentication succeeds without credentials
- Container access permissions verified
- Blob operations complete successfully
- Access patterns optimized for performance

#### TC-INT-022: Prompt Template Retrieval
**Scenario:** Load evaluation prompt templates from Blob Storage  
**Given:** Prompt templates stored in evaluation-prompts container
**When:** Worker loads templates for different test types
**Then:**
- Correct template loaded for each test type
- Template content retrieved completely
- File encoding handled correctly (UTF-8)
- Missing templates handled with fallback

**Template Files:**
- `778760000-judge-prompt.txt` (Similarity)
- `778760001-judge-prompt.txt` (Accuracy)  
- `778760002-judge-prompt.txt` (Relevancy)

#### TC-INT-023: Prompt Template Caching
**Scenario:** Cache prompt templates for performance  
**Given:** Multiple evaluation requests using same test types
**When:** Worker accesses prompt templates
**Then:**
- Templates cached in memory after first load
- Cache invalidation handled appropriately
- Memory usage optimized for caching
- Cache hit rates monitored and optimized

#### TC-INT-024: Template Versioning Support
**Scenario:** Handle prompt template updates and versioning  
**Given:** Updated prompt templates uploaded to Blob Storage
**When:** Worker detects template changes
**Then:**
- New template versions detected automatically
- Cache updated with latest templates
- Version history maintained for rollback
- Gradual rollout supported for template changes

#### TC-INT-025: Blob Storage Error Handling
**Scenario:** Handle Blob Storage access errors  
**Given:** Various Blob Storage error conditions
**When:** Worker attempts to access templates
**Then:**
- Network errors handled with retry logic
- Missing files handled with fallback templates
- Access permission errors logged and reported
- Service degradation handled gracefully

### Key Vault Integration Test Cases

#### TC-INT-026: Key Vault Authentication
**Scenario:** Access Key Vault using Managed Identity  
**Given:** Function app with Key Vault access permissions
**When:** System retrieves secrets
**Then:**
- Authentication succeeds with Managed Identity
- Secret access permissions verified
- Audit trail maintained for secret access
- Token refresh handled automatically

#### TC-INT-027: Secret Retrieval and Caching
**Scenario:** Retrieve and cache secrets efficiently  
**Given:** Configuration requiring various secrets
**When:** Functions load configuration
**Then:**
- Secrets retrieved successfully from Key Vault
- Secrets cached appropriately in memory
- Cache invalidation handled for secret rotation
- Secret values never logged or exposed

#### TC-INT-028: Connection String Management
**Scenario:** Manage connection strings for external services  
**Given:** Multiple services requiring connection strings
**When:** Functions establish service connections
**Then:**
- Connection strings retrieved from Key Vault
- Connection pooling optimized using retrieved strings
- String rotation handled without downtime
- Connection health monitored continuously

#### TC-INT-029: Secret Rotation Handling
**Scenario:** Handle automatic secret rotation  
**Given:** Secrets rotated in Key Vault
**When:** Functions detect secret changes
**Then:**
- New secret values retrieved automatically
- Cached secrets updated appropriately
- Connections re-established with new secrets
- No service disruption during rotation

### Application Insights Integration Test Cases

#### TC-INT-030: Telemetry Collection
**Scenario:** Collect comprehensive telemetry data  
**Given:** Function execution with various operations
**When:** Functions perform processing tasks
**Then:**
- Custom metrics captured for evaluation operations
- Performance counters tracked accurately
- Exception details logged with full context
- Correlation IDs maintained across services

#### TC-INT-031: Custom Metrics Tracking
**Scenario:** Track evaluation-specific metrics  
**Given:** Evaluation runs with various outcomes
**When:** System processes evaluations
**Then:**
- Processing rates tracked per function
- Success/failure rates monitored
- Average scores tracked by test type
- Queue depth and throughput monitored

#### TC-INT-032: Alert Configuration
**Scenario:** Configure alerts for system health  
**Given:** Alert thresholds defined for key metrics
**When:** Metrics exceed defined thresholds
**Then:**
- Alerts triggered appropriately
- Alert notifications delivered to administrators
- Alert context includes relevant details
- Alert frequency managed to prevent spam

#### TC-INT-033: Dashboard Integration
**Scenario:** Provide dashboard views for system monitoring  
**Given:** Telemetry data collected from all components
**When:** Administrators access monitoring dashboards
**Then:**
- Real-time system health visible
- Historical trends displayed clearly
- Drill-down capability for investigation
- Performance baselines established and monitored