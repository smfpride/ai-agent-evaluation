# Test Cases: Monitor Function (MonitorEvaluationRuns)

## Test Suite: Timer Monitor Function
**Component:** MonitorEvaluationRuns  
**Function Type:** Timer Trigger (Every 1 minute)  
**Timeout:** 2 minutes

### Positive Test Cases

#### TC-MON-001: Successful Evaluation Completion Detection
**Scenario:** Monitor detects completed evaluation run and updates status  
**Given:**
- Evaluation run with status 778760002 (Running)
- All test cases completed (completed count = total count)
- All results stored in Dataverse
**When:** Monitor function executes
**Then:**
- Evaluation run status updated to 778760004 (Completed)
- Overall score calculated and stored in mcicaps_overallscore
- Overall pass/fail determined based on threshold
- Completion timestamp recorded
- Success metrics calculated by test type

**Test Scenario Data:**
- Total test cases: 1000
- Completed results: 1000
- Average score: 0.78
- Passing threshold: 0.70
- Expected: Status = Completed, Overall Pass = true

#### TC-MON-002: Multi-Test Type Metrics Calculation
**Scenario:** Monitor calculates metrics across all test types  
**Given:** Evaluation run with mixed test types completed
**When:** Monitor calculates final metrics
**Then:**
- Overall average score calculated correctly
- Test type breakdown computed (Similarity, Accuracy, Relevancy)
- Pass rates calculated per test type
- Weighted scoring applied if configured

**Test Data:**
- Similarity tests (778760000): 400 cases, avg score 0.85
- Accuracy tests (778760001): 300 cases, avg score 0.75  
- Relevancy tests (778760002): 300 cases, avg score 0.80
- Expected overall average: 0.80

#### TC-MON-003: Multiple Evaluation Runs Monitoring
**Scenario:** Monitor handles multiple running evaluations simultaneously  
**Given:** 5 evaluation runs in Running status with different completion levels
**When:** Monitor function executes
**Then:**
- Each evaluation run checked independently
- Completed runs updated to Completed status
- Running runs remain in Running status
- Progress calculated accurately for each
- No cross-evaluation interference

#### TC-MON-004: Evaluation Run Error Detection
**Scenario:** Monitor detects evaluation with high error rate  
**Given:**
- Evaluation run with 20% test case failures
- Error threshold set to 15%
**When:** Monitor evaluates run status
**Then:**
- Evaluation run status updated to 778760003 (Errored)
- Error details captured in status reason
- Overall metrics calculated excluding failed cases
- Alert triggered for high error rate

### Error Handling Test Cases

#### TC-MON-005: Stalled Evaluation Detection (Progress-Based)
**Scenario:** Monitor detects evaluation with no progress  
**Given:**
- Evaluation run with no progress in last 35 minutes (threshold: 30 minutes)
- LastProgressUpdate timestamp is 35 minutes old
- Evaluation may have been running for any duration (no total runtime limit)
**When:** Monitor checks evaluation status
**Then:**
- Stalled evaluation detected based on lack of progress
- Status updated to 778760003 (Errored) 
- Log message: "Evaluation run {Id} has made no progress for {Minutes} minutes (threshold: {MaxMinutes}), marking as errored"
- Alert sent via logging (structured logs in Application Insights)
- Remaining test cases not explicitly marked (evaluation terminated)

**Note:** As of Nov 2025, there is no total runtime timeout. Evaluations can run for hours or days as long as they continue making progress (LastProgressUpdate is updated regularly).

#### TC-MON-006: Dataverse Connection Failure During Monitoring
**Scenario:** Monitor cannot connect to Dataverse  
**Given:** Dataverse becomes unavailable during monitoring cycle
**When:** Monitor attempts to query evaluation runs
**Then:**
- Connection error caught and handled
- Retry logic activated with exponential backoff
- If retries fail, monitoring skipped for this cycle
- Error logged with correlation details
- Next monitoring cycle continues normally

#### TC-MON-005b: Long-Running Evaluation with Progress (Does Not Timeout)
**Scenario:** Monitor allows long-running evaluation that continues making progress  
**Given:**
- Evaluation run started 3 hours ago (StartTime: 3 hours ago)
- Last progress update was 5 minutes ago (LastProgressUpdate: 5 minutes ago)
- Evaluation still has 2000 test cases remaining out of 10000 total
**When:** Monitor checks evaluation status
**Then:**
- Evaluation NOT marked as errored (progress within threshold)
- Status remains 778760002 (Running)
- Log message: "Processing evaluation run {Id}: {Completed}/{Total} completed"
- Evaluation continues processing normally
- No timeout warnings generated

**Note:** This validates that removing total runtime timeout allows large evaluations to complete successfully.

#### TC-MON-007: Partial Results Scenario
**Scenario:** Some test cases completed but others still processing  
**Given:**
- Evaluation run with 1000 total test cases
- 750 completed, 250 still in queue/processing
- No errors or stalls detected
**When:** Monitor checks completion status
**Then:**
- Status remains 778760002 (Running)
- Progress percentage updated to 75%
- Estimated completion time recalculated
- Interim metrics calculated for completed cases

#### TC-MON-008: Calculation Error Handling
**Scenario:** Error occurs during metric calculation  
**Given:** Evaluation results with corrupted or invalid score data
**When:** Monitor calculates overall metrics
**Then:**
- Invalid scores identified and excluded
- Calculation continues with valid data
- Warning logged about data quality issues
- Metrics marked with data quality flags
- Evaluation completed despite calculation issues

### Performance Test Cases

#### TC-MON-009: Large Scale Monitoring
**Scenario:** Monitor handles many concurrent evaluations efficiently  
**Given:** 50 evaluation runs in various states (Running, Completed, Errored)
**When:** Monitor function executes
**Then:**
- All evaluations processed within 2-minute timeout
- Memory usage remains within limits
- Database queries optimized and efficient
- No performance degradation observed

#### TC-MON-010: High Volume Results Processing
**Scenario:** Monitor processes evaluation with 50,000 test case results  
**Given:** Large evaluation run with all test cases completed
**When:** Monitor calculates final metrics
**Then:**
- Metrics calculation completes within timeout
- Accurate aggregation across all results
- Memory usage optimized for large datasets
- Database operations batched efficiently

### Business Logic Test Cases

#### TC-MON-011: Threshold-Based Pass/Fail Logic
**Scenario:** Pass/fail determination based on configured thresholds  
**Given:** Various evaluation runs with different average scores
**When:** Monitor applies pass/fail logic
**Then:**
- Scores above threshold marked as passed
- Scores below threshold marked as failed  
- Edge cases (exactly at threshold) handled correctly
- Different thresholds per evaluation run supported

**Test Cases:**
- Score 0.75, Threshold 0.70: Expected Pass = true
- Score 0.65, Threshold 0.70: Expected Pass = false  
- Score 0.70, Threshold 0.70: Expected Pass = true

#### TC-MON-012: Test Type Weighting
**Scenario:** Different test types weighted differently in overall score  
**Given:** Evaluation with weighted test type configuration
**When:** Monitor calculates weighted average
**Then:**
- Similarity tests weighted 40%
- Accuracy tests weighted 35%
- Relevancy tests weighted 25%
- Overall score reflects proper weighting
- Individual test type scores preserved

#### TC-MON-013: Zero Results Edge Case
**Scenario:** Evaluation run completes with no successful results  
**Given:** All test cases failed due to system errors
**When:** Monitor processes completed evaluation
**Then:**
- Status updated to 778760003 (Errored)
- Overall score set to 0.0
- Overall pass set to false
- Error reason explains failure cause
- Detailed error summary provided

### Integration Test Cases

#### TC-MON-014: End-to-End Evaluation Lifecycle
**Scenario:** Complete evaluation from start to finish monitoring  
**Given:** Evaluation run initiated by orchestrator
**When:** Full lifecycle completes including monitoring
**Then:**
- Status progression: Created → Running → Completed
- All test cases processed by workers
- Monitor detects completion accurately
- Final metrics calculated correctly
- Status updates visible to clients

#### TC-MON-015: Race Condition Prevention
**Scenario:** Monitor and workers don't conflict on status updates  
**Given:** Workers completing final test cases while monitor runs
**When:** Both attempt status updates simultaneously
**Then:**
- No race conditions or data conflicts
- Final status accurately reflects completion
- All results counted properly
- Concurrency handled gracefully

#### TC-MON-016: Alert Integration
**Scenario:** Monitor triggers appropriate alerts for various conditions  
**Given:** Evaluation runs in different error states
**When:** Monitor detects alert conditions
**Then:**
- Stalled evaluation alerts triggered
- High error rate alerts sent
- Completion notifications delivered
- Alert details include evaluation context
- Alert frequency managed appropriately

### Timer Execution Test Cases

#### TC-MON-017: Timer Trigger Reliability
**Scenario:** Monitor executes consistently every minute  
**Given:** System under normal operation
**When:** Timer triggers monitoring function
**Then:**
- Function executes within 5 seconds of scheduled time
- No missed executions under normal load
- Execution duration tracked and logged
- Timer reliability maintains 99%+ uptime

#### TC-MON-018: Overlapping Execution Prevention
**Scenario:** Previous monitor execution still running when timer triggers  
**Given:** Previous monitor cycle taking longer than 1 minute
**When:** Next timer trigger fires
**Then:**
- New execution skipped or queued appropriately
- No overlapping monitor processes
- Warning logged about extended execution time
- System performance impact minimized

#### TC-MON-019: Monitor Function Recovery
**Scenario:** Monitor function recovers from failures  
**Given:** Monitor function crashes or fails
**When:** Next timer cycle executes
**Then:**
- Function restarts cleanly
- Previous failures don't affect new execution
- State recovered from persistent storage
- Error context preserved for debugging