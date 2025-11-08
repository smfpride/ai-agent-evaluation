# Test Cases: End-to-End Scenarios

## Test Suite: End-to-End Integration Testing
**Focus:** Complete workflow validation from start to finish  
**Scope:** Full system integration across all components

### Complete Evaluation Lifecycle Test Cases

#### TC-E2E-001: Standard Evaluation Run Lifecycle
**Scenario:** Complete evaluation run from initiation to completion  
**Given:** 
- Valid evaluation run with 1,000 test cases (mixed types)
- All system components healthy and available
- Copilot Studio agent ready for testing
**When:** Full evaluation lifecycle executed
**Then:**
- Orchestrator accepts run and queues all test cases (< 30 seconds)
- All test cases processed by workers successfully
- LLM Judge evaluations completed with valid scores
- Results stored correctly in Dataverse
- Monitor function detects completion and updates status
- Final metrics calculated accurately

**End-to-End Flow:**
1. HTTP POST → StartEvaluationRun function
2. Status updated to Running (778760002)
3. Test cases queued to Service Bus
4. Workers process test cases via ProcessTestCase
5. Copilot Studio interactions via Direct Line API
6. LLM Judge evaluations via Azure OpenAI
7. Results stored in mcicaps_evaluationresults
8. Monitor function calculates final metrics
9. Status updated to Completed (778760004)

#### TC-E2E-002: Multi-Test Type Evaluation
**Scenario:** Evaluation run with all three test types  
**Given:** 
- Test set with 300 Similarity, 300 Accuracy, 300 Relevancy tests
- Different judge prompts for each test type
**When:** Evaluation run processed
**Then:**
- Correct judge prompt loaded for each test type
- Type-specific evaluation criteria applied
- Scores reflect appropriate evaluation focus
- Results categorized correctly by test type
- Final metrics include breakdown by test type

#### TC-E2E-003: Large Scale End-to-End (10,000+ Test Cases)
**Scenario:** Large evaluation run processed completely  
**Given:** Evaluation run with 10,000 test cases
**When:** Complete processing performed
**Then:**
- Orchestrator handles large volume without timeout
- Auto-scaling activates to handle load
- All test cases processed within 2 hours
- Success rate > 99.5%
- System remains stable throughout processing
- Final results accurate and complete

### Error Recovery and Resilience Test Cases

#### TC-E2E-004: Service Failure Recovery
**Scenario:** System recovers from temporary service failures  
**Given:** Evaluation run in progress with partial completion
**When:** 
- Copilot Studio API becomes temporarily unavailable
- Service recovers after 10 minutes
**Then:**
- Failed test cases retried automatically
- Successful test cases not reprocessed
- Evaluation run completes successfully
- Final results include all test cases
- Failure details logged for analysis

#### TC-E2E-005: Database Connectivity Issues
**Scenario:** Evaluation continues despite database interruptions  
**Given:** Evaluation run with test cases being processed
**When:** Dataverse connection interrupted temporarily
**Then:**
- Test case processing continues in memory
- Results queued for storage when connection restores
- No data loss occurs
- Progress tracking resumes accurately
- Evaluation run completes successfully

#### TC-E2E-006: Partial Failure Scenarios
**Scenario:** Evaluation completes with some failed test cases  
**Given:** Evaluation run where 5% of test cases fail
**When:** Evaluation run processed to completion
**Then:**
- Successful test cases processed normally
- Failed test cases handled gracefully
- Overall metrics calculated from successful cases
- Failure rate and reasons documented
- Status reflects partial success appropriately

### Business Logic Integration Test Cases

#### TC-E2E-007: Threshold-Based Evaluation Pass/Fail
**Scenario:** Pass/fail determination based on configured thresholds  
**Given:** 
- Evaluation run with passing threshold of 0.75
- Test results with average score of 0.78
**When:** Monitor function calculates final metrics
**Then:**
- Overall pass status set to true
- Individual test case pass/fail based on threshold
- Pass rate calculated correctly
- Threshold comparison logic accurate
- Results reflect business requirements

#### TC-E2E-008: Test Type Weighting
**Scenario:** Weighted scoring across different test types  
**Given:** 
- Evaluation configuration with test type weights
- Results from all three test types
**When:** Final scoring calculated
**Then:**
- Weights applied correctly to each test type
- Overall score reflects weighted average
- Individual test type scores preserved
- Weighting logic documented and auditable
- Business requirements satisfied

#### TC-E2E-009: Custom Evaluation Criteria
**Scenario:** Evaluation using customized judge prompts  
**Given:** 
- Updated judge prompts in Blob Storage
- Test cases requiring custom evaluation criteria
**When:** Evaluation run processed
**Then:**
- Custom prompts loaded and applied
- Evaluation results reflect custom criteria
- Consistency maintained across similar test cases
- Custom reasoning captured appropriately
- Results align with custom requirements

### Data Flow Integration Test Cases

#### TC-E2E-010: Data Consistency Across Services
**Scenario:** Data remains consistent across all system components  
**Given:** Evaluation run processed through all components
**When:** Data accessed from different services
**Then:**
- Test case data consistent between Dataverse and Service Bus
- Evaluation results match original test case context
- Status updates reflected consistently across services
- No data corruption or inconsistencies detected
- Audit trails complete and accurate

#### TC-E2E-011: Large Data Volume Handling
**Scenario:** System handles large evaluation responses and data  
**Given:** Test cases generating large agent responses (1MB+)
**When:** Complete evaluation processing performed
**Then:**
- Large responses processed without truncation
- Storage systems handle large data efficiently
- Processing time scales appropriately
- Memory usage optimized for large payloads
- No data loss or corruption

#### TC-E2E-012: Unicode and Internationalization
**Scenario:** System handles international content correctly  
**Given:** Test cases with content in multiple languages and Unicode
**When:** Complete evaluation processing performed
**Then:**
- Unicode content preserved through all processing steps
- LLM Judge handles international content appropriately
- Database storage maintains character encoding
- Search and retrieval work with international content
- Display and reporting handle Unicode correctly

### Real-World Usage Scenarios

#### TC-E2E-013: Production Workload Simulation
**Scenario:** Simulate realistic production usage patterns  
**Given:** 
- Multiple evaluation runs of varying sizes
- Realistic timing patterns and load distribution
- Mixed test types and content
**When:** Simulated production load applied
**Then:**
- System handles realistic workload patterns
- Performance meets production requirements
- Resource utilization optimized
- User experience remains positive
- System stability maintained under realistic load

#### TC-E2E-014: User Workflow Integration
**Scenario:** Complete user workflow from setup to results  
**Given:** User configuring and running evaluation
**When:** Complete user workflow executed
**Then:**
- Evaluation run configuration captured correctly
- User can track progress through status API
- Results accessible via configured interfaces
- Error messages clear and actionable
- User experience smooth and intuitive

#### TC-E2E-015: Batch Processing Scenario
**Scenario:** Multiple evaluation runs processed in sequence  
**Given:** Queue of 20 evaluation runs of various sizes
**When:** Batch processing executed
**Then:**
- Runs processed in appropriate order
- Resource allocation optimized across runs
- No interference between different runs
- Progress tracking accurate for all runs
- Batch completion within expected timeframes

### Monitoring and Observability Integration

#### TC-E2E-016: End-to-End Monitoring
**Scenario:** Complete monitoring coverage for evaluation lifecycle  
**Given:** Evaluation run with comprehensive monitoring enabled
**When:** Complete evaluation lifecycle executed
**Then:**
- All processing stages monitored and logged
- Performance metrics captured accurately
- Correlation IDs maintained across all components
- Dashboards provide complete visibility
- Alerts triggered appropriately for issues

#### TC-E2E-017: Troubleshooting and Debugging
**Scenario:** Support troubleshooting of evaluation issues  
**Given:** Evaluation run with various types of issues
**When:** Troubleshooting and debugging performed
**Then:**
- Complete audit trail available for investigation
- Error context provides sufficient detail
- Correlation across services enables root cause analysis
- Diagnostic information easily accessible
- Resolution guidance available for common issues

### Business Continuity Integration

#### TC-E2E-018: Multi-Region Failover
**Scenario:** Evaluation continues during regional failover  
**Given:** Evaluation run in progress during regional outage
**When:** Failover to secondary region executed
**Then:**
- Evaluation state preserved during failover
- Processing resumes in secondary region
- No data loss during transition
- Results remain consistent and complete
- Recovery time within acceptable limits

#### TC-E2E-019: Disaster Recovery Testing
**Scenario:** Complete system recovery from disaster scenario  
**Given:** Complete system failure and recovery procedures
**When:** Disaster recovery executed
**Then:**
- System functionality restored completely
- Data integrity maintained through recovery
- Evaluation capability fully operational
- Historical data and results preserved
- Recovery procedures validated and effective

#### TC-E2E-020: Long-Running Evaluation Resilience
**Scenario:** Very long evaluation run maintains stability  
**Given:** Evaluation run designed to take 8+ hours
**When:** Extended evaluation processing performed
**Then:**
- System maintains stability over extended period
- No performance degradation over time
- Resource management effective for long runs
- Progress tracking accurate throughout
- Successful completion despite extended duration