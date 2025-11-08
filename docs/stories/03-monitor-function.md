# User Stories: Monitor Function (MonitorEvaluationRuns)

## Epic: Evaluation Run Monitoring and Completion
As a system, I want to monitor running evaluation runs and calculate final results so that users receive complete evaluation outcomes.

### Story 1: Monitor Running Evaluations
**As a** monitoring system  
**I want to** periodically check evaluation runs in progress  
**So that** I can determine when they are complete

**Acceptance Criteria:**
- [ ] Timer trigger executes every 1 minute
- [ ] Query evaluation runs with status 778760002 (Running)
- [ ] Compare completed test cases count vs total test cases
- [ ] Handle multiple concurrent evaluation runs
- [ ] Complete execution within 2 minutes timeout

### Story 2: Calculate Overall Metrics
**As a** monitoring system  
**I want to** calculate aggregate metrics for completed evaluation runs  
**So that** users can see overall performance results

**Acceptance Criteria:**
- [ ] Calculate average score across all test cases
- [ ] Determine overall pass/fail based on passing threshold
- [ ] Compute success rates by test type (Similarity, Accuracy, Relevancy)
- [ ] Handle edge cases (no test cases, all failures)
- [ ] Provide breakdown by test type for detailed analysis

**Metrics to Calculate:**
- Overall average score (0-1 scale)
- Overall pass/fail status (based on threshold)
- Test type breakdown (total, completed, passed, failed)
- Success rate percentage
- Error count and rate

### Story 3: Update Completion Status
**As a** monitoring system  
**I want to** update evaluation run status when complete  
**So that** users know the evaluation has finished

**Acceptance Criteria:**
- [ ] Update status to 778760004 (Completed) when all test cases processed successfully
- [ ] Update status to 778760003 (Errored) if significant failures occurred
- [ ] Update `mcicaps_overallscore` and `mcicaps_overallpass` fields
- [ ] Set completion timestamp
- [ ] Handle race conditions with worker functions

### Story 4: Handle Stalled Evaluations
**As a** monitoring system  
**I want to** detect and handle evaluations that have stalled  
**So that** the system doesn't have permanently running evaluations

**Acceptance Criteria:**
- [ ] Detect evaluations running longer than expected duration
- [ ] Check for evaluations with no progress in extended period
- [ ] Mark stalled evaluations as errored with appropriate reason
- [ ] Send alerts for stalled evaluation detection
- [ ] Provide manual intervention capabilities

### Story 5: Provide Status Updates
**As a** monitoring system  
**I want to** maintain accurate status information  
**So that** client applications can track evaluation progress

**Acceptance Criteria:**
- [ ] Update progress percentages accurately
- [ ] Maintain test type breakdown statistics
- [ ] Calculate and update estimated completion times
- [ ] Provide error details and failure reasons
- [ ] Support status API queries for real-time updates

### Story 6: Handle Monitoring Errors
**As a** monitoring system  
**I want to** handle errors during monitoring activities  
**So that** monitoring failures don't affect evaluation processing

**Acceptance Criteria:**
- [ ] Log monitoring errors without affecting evaluation runs
- [ ] Continue monitoring other evaluation runs on individual failures
- [ ] Implement retry logic for transient Dataverse connection issues
- [ ] Alert on repeated monitoring failures
- [ ] Maintain monitoring function health status