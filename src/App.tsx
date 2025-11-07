import { useState } from 'react'
import { useKV } from '@github/spark/hooks'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Progress } from '@/components/ui/progress'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  Plus,
  Play,
  Gear,
  Flask,
  CheckCircle,
  XCircle,
  Warning,
  CircleNotch,
} from '@phosphor-icons/react'
import { TestCaseDialog } from '@/components/TestCaseDialog'
import { TestCaseCard } from '@/components/TestCaseCard'
import { SettingsDialog } from '@/components/SettingsDialog'
import { ResultCard } from '@/components/ResultCard'
import { ResultDetailDialog } from '@/components/ResultDetailDialog'
import type { TestCase, EvaluationResult, DirectLineConfig } from '@/types'
import { DirectLineService } from '@/lib/directline'
import { evaluateResponse } from '@/lib/judge'
import { toast } from 'sonner'

function App() {
  const [testCases, setTestCases] = useKV<TestCase[]>('test-cases', [])
  const [results, setResults] = useKV<EvaluationResult[]>('evaluation-results', [])
  const [config, setConfig] = useKV<DirectLineConfig | null>('directline-config', null)

  const [selectedTestCases, setSelectedTestCases] = useState<Set<string>>(new Set())
  const [testDialogOpen, setTestDialogOpen] = useState(false)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [editingTestCase, setEditingTestCase] = useState<TestCase | undefined>()
  const [running, setRunning] = useState(false)
  const [progress, setProgress] = useState({ current: 0, total: 0 })
  const [selectedResult, setSelectedResult] = useState<EvaluationResult | null>(null)
  const [resultDetailOpen, setResultDetailOpen] = useState(false)

  const safeTestCases = testCases || []
  const safeResults = results || []
  const safeConfig = config || null

  const handleSaveTestCase = (testCaseData: Omit<TestCase, 'id' | 'createdAt' | 'updatedAt'>) => {
    const now = Date.now()
    
    if (editingTestCase) {
      setTestCases(current =>
        (current || []).map(tc =>
          tc.id === editingTestCase.id
            ? { ...tc, ...testCaseData, updatedAt: now }
            : tc
        )
      )
      toast.success('Test case updated')
    } else {
      const newTestCase: TestCase = {
        ...testCaseData,
        id: `tc-${now}`,
        createdAt: now,
        updatedAt: now,
      }
      setTestCases(current => [...(current || []), newTestCase])
      toast.success('Test case added')
    }
    
    setEditingTestCase(undefined)
  }

  const handleDeleteTestCase = (id: string) => {
    setTestCases(current => (current || []).filter(tc => tc.id !== id))
    setSelectedTestCases(prev => {
      const next = new Set(prev)
      next.delete(id)
      return next
    })
    toast.success('Test case deleted')
  }

  const handleEditTestCase = (testCase: TestCase) => {
    setEditingTestCase(testCase)
    setTestDialogOpen(true)
  }

  const toggleTestCase = (id: string) => {
    setSelectedTestCases(prev => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  const selectAll = () => {
    setSelectedTestCases(new Set(safeTestCases.map(tc => tc.id)))
  }

  const deselectAll = () => {
    setSelectedTestCases(new Set())
  }

  const runEvaluation = async () => {
    if (!safeConfig) {
      toast.error('Please configure Direct Line credentials first')
      setSettingsOpen(true)
      return
    }

    if (selectedTestCases.size === 0) {
      toast.error('Please select at least one test case')
      return
    }

    const selectedTests = safeTestCases.filter(tc => selectedTestCases.has(tc.id))
    
    setRunning(true)
    setProgress({ current: 0, total: selectedTests.length })

    const newResults: EvaluationResult[] = []
    const directLine = new DirectLineService(safeConfig)

    try {
      for (let i = 0; i < selectedTests.length; i++) {
        const testCase = selectedTests[i]
        setProgress({ current: i + 1, total: selectedTests.length })

        try {
          const startTime = Date.now()
          const actualResponse = await directLine.sendMessage(testCase.prompt)
          
          const judgeResult = await evaluateResponse(
            testCase.prompt,
            testCase.expectedResponse,
            actualResponse
          )

          const result: EvaluationResult = {
            id: `result-${Date.now()}-${i}`,
            testCaseId: testCase.id,
            prompt: testCase.prompt,
            expectedResponse: testCase.expectedResponse,
            actualResponse,
            score: judgeResult.score,
            reasoning: judgeResult.reasoning,
            passed: judgeResult.passed,
            timestamp: Date.now(),
            executionTime: Date.now() - startTime,
          }

          newResults.push(result)
          
          directLine.resetConversation()
          
          if (i < selectedTests.length - 1) {
            await new Promise(resolve => setTimeout(resolve, 500))
          }
        } catch (error) {
          toast.error(`Failed to evaluate: ${testCase.prompt.substring(0, 50)}...`)
          console.error('Evaluation error:', error)
        }
      }

      setResults(current => [...newResults, ...(current || [])])
      
      const passCount = newResults.filter(r => r.passed).length
      const totalCount = newResults.length
      
      toast.success(`Evaluation complete: ${passCount}/${totalCount} passed`)
    } finally {
      setRunning(false)
      setProgress({ current: 0, total: 0 })
    }
  }

  const handleResultClick = (result: EvaluationResult) => {
    setSelectedResult(result)
    setResultDetailOpen(true)
  }

  const passedResults = safeResults.filter(r => r.passed).length
  const failedResults = safeResults.filter(r => !r.passed).length
  const passRate = safeResults.length > 0 ? Math.round((passedResults / safeResults.length) * 100) : 0

  return (
    <div className="min-h-screen bg-background">
      <header className="border-b border-border bg-card">
        <div className="container mx-auto px-6 py-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <Flask className="h-8 w-8 text-accent" weight="fill" />
              <div>
                <h1 className="text-2xl font-semibold tracking-tight">
                  AI Evaluation Dashboard
                </h1>
                <p className="text-sm text-muted-foreground">
                  Test and evaluate Copilot Studio agents
                </p>
              </div>
            </div>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setSettingsOpen(true)}
            >
              <Gear className="mr-2 h-4 w-4" />
              Settings
            </Button>
          </div>
        </div>
      </header>

      <main className="container mx-auto px-6 py-6">
        {!safeConfig && (
          <Alert className="mb-6">
            <Warning className="h-4 w-4" />
            <AlertDescription>
              Please configure your Direct Line credentials in Settings to start running evaluations.
            </AlertDescription>
          </Alert>
        )}

        {running && (
          <Card className="p-6 mb-6">
            <div className="flex items-center gap-4 mb-3">
              <CircleNotch className="h-5 w-5 animate-spin text-accent" />
              <span className="font-medium">
                Running evaluations... ({progress.current}/{progress.total})
              </span>
            </div>
            <Progress 
              value={(progress.current / progress.total) * 100} 
              className="h-2"
            />
          </Card>
        )}

        <Tabs defaultValue="tests" className="space-y-6">
          <TabsList>
            <TabsTrigger value="tests">Test Cases ({safeTestCases.length})</TabsTrigger>
            <TabsTrigger value="results">Results ({safeResults.length})</TabsTrigger>
          </TabsList>

          <TabsContent value="tests" className="space-y-4">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Button onClick={() => setTestDialogOpen(true)} size="sm">
                  <Plus className="mr-2 h-4 w-4" />
                  Add Test Case
                </Button>
                {safeTestCases.length > 0 && (
                  <>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={selectAll}
                      disabled={selectedTestCases.size === safeTestCases.length}
                    >
                      Select All
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={deselectAll}
                      disabled={selectedTestCases.size === 0}
                    >
                      Deselect All
                    </Button>
                  </>
                )}
              </div>
              {selectedTestCases.size > 0 && (
                <Button
                  onClick={runEvaluation}
                  disabled={running || !safeConfig}
                  className="bg-accent text-accent-foreground hover:bg-accent/90"
                >
                  <Play className="mr-2 h-4 w-4" weight="fill" />
                  Run Evaluation ({selectedTestCases.size})
                </Button>
              )}
            </div>

            {safeTestCases.length === 0 ? (
              <Card className="p-12 text-center">
                <Flask className="h-12 w-12 mx-auto mb-4 text-muted-foreground" />
                <h3 className="text-lg font-medium mb-2">No test cases yet</h3>
                <p className="text-sm text-muted-foreground mb-4">
                  Create your first test case to start evaluating your agent.
                </p>
                <Button onClick={() => setTestDialogOpen(true)}>
                  <Plus className="mr-2 h-4 w-4" />
                  Add Test Case
                </Button>
              </Card>
            ) : (
              <div className="grid gap-3">
                {safeTestCases.map(testCase => (
                  <TestCaseCard
                    key={testCase.id}
                    testCase={testCase}
                    selected={selectedTestCases.has(testCase.id)}
                    onSelect={checked => checked && toggleTestCase(testCase.id)}
                    onEdit={() => handleEditTestCase(testCase)}
                    onDelete={() => handleDeleteTestCase(testCase.id)}
                  />
                ))}
              </div>
            )}
          </TabsContent>

          <TabsContent value="results" className="space-y-4">
            {safeResults.length > 0 && (
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
                <Card className="p-4">
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-sm text-muted-foreground">Pass Rate</span>
                    <Badge className="bg-accent text-accent-foreground">
                      {passRate}%
                    </Badge>
                  </div>
                  <Progress value={passRate} className="h-2" />
                </Card>
                <Card className="p-4">
                  <div className="flex items-center gap-2 mb-2">
                    <CheckCircle className="h-4 w-4 text-success" weight="fill" />
                    <span className="text-sm text-muted-foreground">Passed</span>
                  </div>
                  <p className="text-2xl font-semibold">{passedResults}</p>
                </Card>
                <Card className="p-4">
                  <div className="flex items-center gap-2 mb-2">
                    <XCircle className="h-4 w-4 text-destructive" weight="fill" />
                    <span className="text-sm text-muted-foreground">Failed</span>
                  </div>
                  <p className="text-2xl font-semibold">{failedResults}</p>
                </Card>
              </div>
            )}

            {safeResults.length === 0 ? (
              <Card className="p-12 text-center">
                <CheckCircle className="h-12 w-12 mx-auto mb-4 text-muted-foreground" />
                <h3 className="text-lg font-medium mb-2">No results yet</h3>
                <p className="text-sm text-muted-foreground mb-4">
                  Run your first evaluation to see results here.
                </p>
              </Card>
            ) : (
              <div className="grid gap-3">
                {safeResults.map(result => (
                  <ResultCard
                    key={result.id}
                    result={result}
                    onClick={() => handleResultClick(result)}
                  />
                ))}
              </div>
            )}
          </TabsContent>
        </Tabs>
      </main>

      <TestCaseDialog
        open={testDialogOpen}
        onOpenChange={open => {
          setTestDialogOpen(open)
          if (!open) setEditingTestCase(undefined)
        }}
        onSave={handleSaveTestCase}
        editingTestCase={editingTestCase}
      />

      <SettingsDialog
        open={settingsOpen}
        onOpenChange={setSettingsOpen}
        config={safeConfig}
        onSave={cfg => {
          setConfig(cfg)
          toast.success('Configuration saved')
        }}
      />

      <ResultDetailDialog
        open={resultDetailOpen}
        onOpenChange={setResultDetailOpen}
        result={selectedResult}
      />
    </div>
  )
}

export default App
