# AI Evaluation Dashboard - Product Requirements Document

AI Evaluation Dashboard enables quality assurance teams to systematically test and evaluate Copilot Studio agents by running test cases against the Direct Line API and using LLM-as-judge to assess semantic similarity between expected and actual responses.

**Experience Qualities**:
1. **Systematic** - Provides structured, repeatable evaluation workflows that build confidence in AI agent quality
2. **Transparent** - Clearly displays evaluation criteria, scoring rationale, and detailed results for informed decision-making
3. **Efficient** - Streamlines batch testing with progress tracking and organized result management

**Complexity Level**: Complex Application (advanced functionality with state management)
  - Requires API integration, batch processing, persistent storage, LLM evaluation logic, and comprehensive result visualization

## Essential Features

### Test Case Management
- **Functionality**: Create, edit, delete, and organize test cases with prompts and expected responses
- **Purpose**: Maintain a reusable library of test scenarios for consistent evaluation
- **Trigger**: User clicks "Add Test Case" button or imports test cases
- **Progression**: Click add → Enter prompt text → Enter expected response → Optionally add tags/metadata → Save → Test case appears in library
- **Success criteria**: Test cases persist between sessions, can be edited/deleted, and display clearly in organized list

### Direct Line API Configuration
- **Functionality**: Store and manage Direct Line API credentials (endpoint URL, secret key)
- **Purpose**: Enable connection to Copilot Studio agents for testing
- **Trigger**: User navigates to settings or first-time setup prompt
- **Progression**: Open settings → Enter Direct Line secret → Optionally enter custom endpoint → Save → Connection validated
- **Success criteria**: Credentials stored securely, connection status indicator shows success/failure

### Evaluation Execution
- **Functionality**: Run test cases individually or in batch against the configured agent
- **Purpose**: Execute systematic testing and collect actual responses
- **Trigger**: User selects test cases and clicks "Run Evaluation"
- **Progression**: Select test cases → Click run → Progress indicator shows → Each prompt sent to Direct Line → Agent response captured → LLM judge evaluates semantic match → Results stored and displayed
- **Success criteria**: All selected tests execute successfully, progress accurately tracked, errors handled gracefully

### LLM-as-Judge Evaluation
- **Functionality**: Use Spark's LLM API to evaluate semantic similarity between expected and actual responses
- **Purpose**: Provide automated, intelligent assessment beyond exact string matching
- **Trigger**: Automatically invoked after receiving agent response
- **Progression**: Receive agent response → Construct evaluation prompt with expected vs actual → LLM analyzes semantic similarity → Receive score (0-100) and reasoning → Store results
- **Success criteria**: LLM provides consistent, explainable scores with clear reasoning for pass/fail determination

### Results Dashboard
- **Functionality**: Display evaluation results with scores, pass/fail status, and detailed comparison
- **Purpose**: Enable quick assessment of agent quality and identification of problematic areas
- **Trigger**: After evaluation completes or when viewing historical results
- **Progression**: View results list → See overall pass rate → Click individual result → View detailed comparison with expected vs actual response → Read LLM judge reasoning → Optionally re-run test
- **Success criteria**: Results clearly organized, filterable by status, searchable, with visual indicators for quick scanning

### Historical Tracking
- **Functionality**: Store and retrieve past evaluation runs with timestamps
- **Purpose**: Track agent performance over time and identify regressions
- **Trigger**: Automatic after each evaluation run
- **Progression**: Run evaluation → Results automatically saved with timestamp → View history list → Filter by date range or test case → Compare results across runs
- **Success criteria**: All evaluation results persist, can be filtered and sorted, and historical trends visible

## Edge Case Handling

- **Network Failures**: Retry logic with exponential backoff for Direct Line API calls, clear error messages when max retries exceeded
- **API Rate Limits**: Queue management with appropriate delays between requests, visual feedback on throttling
- **Invalid Credentials**: Immediate validation feedback with actionable error messages and setup guidance
- **Malformed Responses**: Graceful handling of unexpected response formats with error logging for debugging
- **Empty Test Cases**: Validation prevents running evaluations without test cases, helpful empty state guides user
- **Concurrent Evaluations**: Prevent multiple simultaneous runs, show active evaluation status clearly
- **Large Batch Processing**: Chunked processing with progress updates, ability to cancel mid-execution
- **LLM Judge Failures**: Fallback to partial results, clear indication when judge evaluation unavailable

## Design Direction

The design should evoke professional confidence and analytical precision - this is a quality assurance tool that teams rely on for critical testing workflows. The interface should feel systematic and organized like a professional testing suite, with clear visual hierarchy that guides users from test creation through execution to results analysis. A clean, data-focused design with generous whitespace helps reduce cognitive load during complex evaluation workflows.

## Color Selection

Triadic color scheme (blue, green, amber) to represent different evaluation states and provide clear visual categorization while maintaining professional appearance.

- **Primary Color**: Deep Blue (oklch(0.45 0.15 250)) - Communicates trust, professionalism, and analytical precision appropriate for enterprise testing tools
- **Secondary Colors**: 
  - Slate Gray (oklch(0.50 0.02 250)) for supporting UI elements and subtle backgrounds
  - Cool White (oklch(0.98 0.01 250)) for cards and elevated surfaces
- **Accent Color**: Vibrant Cyan (oklch(0.65 0.18 220)) - Highlights active states, primary actions, and draws attention to key metrics
- **Foreground/Background Pairings**:
  - Background (Cool White oklch(0.98 0.01 250)): Dark Text (oklch(0.20 0.02 250)) - Ratio 12.8:1 ✓
  - Card (White oklch(1 0 0)): Dark Text (oklch(0.20 0.02 250)) - Ratio 14.2:1 ✓
  - Primary (Deep Blue oklch(0.45 0.15 250)): White (oklch(1 0 0)) - Ratio 7.8:1 ✓
  - Success (Green oklch(0.55 0.15 150)): White (oklch(1 0 0)) - Ratio 5.2:1 ✓
  - Warning (Amber oklch(0.70 0.15 85)): Dark (oklch(0.20 0.02 250)) - Ratio 8.5:1 ✓
  - Destructive (Red oklch(0.55 0.22 25)): White (oklch(1 0 0)) - Ratio 5.8:1 ✓
  - Accent (Vibrant Cyan oklch(0.65 0.18 220)): Dark (oklch(0.20 0.02 250)) - Ratio 6.2:1 ✓

## Font Selection

Typography should convey technical precision and clarity with excellent readability for dense information displays and numerical data.

- **Primary Font**: Inter - Modern, highly legible sans-serif optimized for UI and data-heavy interfaces
- **Monospace Font**: JetBrains Mono - For displaying API responses, prompts, and technical content

- **Typographic Hierarchy**:
  - H1 (Page Title): Inter SemiBold/32px/tight (-0.02em) - Main dashboard heading
  - H2 (Section Title): Inter SemiBold/24px/tight (-0.01em) - Test cases, Results sections
  - H3 (Card Title): Inter Medium/18px/normal - Individual test case names
  - H4 (Label): Inter Medium/14px/normal - Form labels, metrics labels
  - Body (Content): Inter Regular/15px/relaxed (1.6) - Descriptions, explanatory text
  - Body Small (Metadata): Inter Regular/13px/normal - Timestamps, tags, secondary info
  - Code (Technical): JetBrains Mono Regular/14px/normal (1.5) - API responses, prompts

## Animations

Animations should feel purposeful and professional, reinforcing the systematic nature of the evaluation process with smooth transitions that communicate progress and state changes without distracting from the analytical workflow.

- **Purposeful Meaning**: Progress animations during evaluation runs communicate active processing, success animations confirm completion with subtle celebration, and transition animations maintain spatial context when navigating between test cases and results
- **Hierarchy of Movement**: 
  - Critical: Evaluation progress indicators (animated with smooth interpolation)
  - Important: State transitions (pass/fail reveals with gentle scale/fade)
  - Subtle: Card hovers, button interactions (minimal lift/shadow changes)
  - Background: List reordering, filtering (gentle fade/slide)

## Component Selection

- **Components**: 
  - **Card** (Test cases, results, metrics) - With subtle shadow and border for professional elevation
  - **Button** (Primary actions, secondary actions) - Primary for "Run Evaluation", secondary for "Add Test"
  - **Input/Textarea** (Test prompts, expected responses) - With clear focus states and validation
  - **Badge** (Pass/fail status, tags) - Color-coded for quick visual scanning
  - **Table** (Results listing) - Sortable columns for organized data display
  - **Dialog** (Settings, test case editor) - Modal overlays for focused workflows
  - **Progress** (Evaluation execution) - Clear percentage and step indicators
  - **Tabs** (Test Cases / Results / History) - Main navigation between dashboard sections
  - **Alert** (Error messages, warnings) - Contextual notifications for issues
  - **Separator** (Section dividers) - Subtle visual organization
  - **ScrollArea** (Long lists, response text) - Smooth scrolling for content overflow
  - **Tooltip** (Help text, additional info) - Contextual guidance on hover

- **Customizations**: 
  - Custom score visualization component (0-100 scale with color gradient)
  - Diff viewer component for expected vs actual comparison
  - Custom progress tracker showing evaluation pipeline stages
  - Metric summary cards with large numbers and trend indicators

- **States**: 
  - Buttons: Distinct hover (slight lift), active (pressed), disabled (muted), loading (spinner)
  - Inputs: Default, focused (accent border + ring), error (destructive border), success (green border)
  - Cards: Default (subtle shadow), hover (increased shadow), selected (accent border)
  - Test cases: Draft, ready, running, complete, error (each with appropriate visual indicator)

- **Icon Selection**:
  - Play (Run evaluation), Plus (Add test case), Gear (Settings), CheckCircle (Pass), XCircle (Fail)
  - Flask (Evaluation/testing), ArrowsClockwise (Re-run), MagnifyingGlass (Search), Funnel (Filter)
  - Calendar (History), ChartBar (Metrics), Warning (Errors), Info (Help/tooltips)

- **Spacing**: 
  - Container padding: p-6 (24px) for main sections
  - Card padding: p-4 (16px) for content cards
  - Section gaps: gap-6 (24px) between major sections
  - Element gaps: gap-3 (12px) within forms and lists
  - Tight spacing: gap-2 (8px) for related inline elements

- **Mobile**: 
  - Stack navigation tabs vertically on mobile
  - Full-width cards and buttons for easier touch targets
  - Collapsible test case details with expand/collapse interaction
  - Simplified table view with swipe-to-reveal actions
  - Fixed bottom action bar for primary CTA on mobile
  - Responsive typography scales down by 1-2px on mobile
  - Hide secondary columns in result tables, show on expand
