import { useState, useEffect } from 'react'
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
  ArrowsClockwise,
} from '@phosphor-icons/react'
import { TestCaseDialog } from '@/components/TestCaseDialog'
import { TestCaseCard } from '@/components/TestCaseCard'
import { SettingsDialog } from '@/components/SettingsDialog'
import { ResultCard } from '@/components/ResultCard'
import { ResultDetailDialog } from '@/components/ResultDetailDialog'
import type { TestCase, EvaluationResult, AppConfig } from '@/types'
import { DirectLineService } from '@/lib/directline'
import { AzureOpenAIJudge } from '@/lib/judge'
import { DataVerseService } from '@/lib/dataverse'
import { toast } from 'sonner'

function App() {
  const [config, setConfig] = useKV<AppConfig>('app-config', {
    directLine: null,
    azureOpenAI: null,
    dataVerse: null,
  })

  const [testCases, setTestCases] = useState<TestCase[]>([])
  const [results, setResults] = useState<EvaluationResult[]>([])
  const [selectedTestCases, setSelectedTestCases] = useState<Set<string>>(new Set())
  const [testDialogOpen, setTestDialogOpen] = useState(false)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [editingTestCase, setEditingTestCase] = useState<TestCase | undefined>()
  const [running, setRunning] = useState(false)
  const [progress, setProgress] = useState({ current: 0, total: 0 })
  const [selectedResult, setSelectedResult] = useState<EvaluationResult | null>(null)
  const [resultDetailOpen, setResultDetailOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [currentPage, setCurrentPage] = useState(0)
  const [totalTestCases, setTotalTestCases] = useState(0)
  const [totalResults, setTotalResults] = useState(0)
  const PAGE_SIZE = 100

  const safeConfig = config || { directLine: null, azureOpenAI: null, dataVerse: null }

  useEffect(() => {
    if (safeConfig.dataVerse) {
      loadTestCases()
      loadResults()
    }
  }, [safeConfig.dataVerse, currentPage])

  const loadTestCases = async () => {
    if (!safeConfig.dataVerse) return

    setLoading(true)
    try {
      const service = new DataVerseService(safeConfig.dataVerse)
      const { testCases: fetchedCases, totalCount } = await service.fetchTestCases(
        PAGE_SIZE,
        currentPage * PAGE_SIZE
      )
      setTestCases(fetchedCases)
      setTotalTestCases(totalCount)
    } catch (error) {
      toast.error('Failed to load test cases from DataVerse')
      console.error('Load test cases error:', error)
    } finally {
      setLoading(false)
    }
  }

  const loadResults = async () => {
    if (!safeConfig.dataVerse) return

    try {
      const service = new DataVerseService(safeConfig.dataVerse)
      const { results: fetchedResults, totalCount } = await service.fetchEvaluationResults(
        PAGE_SIZE,
        0
      )
      setResults(fetchedResults)
      setTotalResults(totalCount)
    } catch (error) {
      toast.error('Failed to load results from DataVerse')
      console.error('Load results error:', error)
    }
  }

  const handleSaveTestCase = async (testCaseData: Omit<TestCase, 'id' | 'createdAt' | 'updatedAt'>) => {
    if (!safeConfig.dataVerse) {
      toast.error('DataVerse not configured')
      return
    }

    const service = new DataVerseService(safeConfig.dataVerse)

    try {
      if (editingTestCase) {
        await service.updateTestCase(editingTestCase.id, testCaseData)
        toast.success('Test case updated')
        await loadTestCases()
      } else {
        await service.createTestCase(testCaseData)
        toast.success('Test case added')
        await loadTestCases()
      }
      setEditingTestCase(undefined)
    } catch (error) {
      toast.error('Failed to save test case')
      console.error('Save test case error:', error)
    }
  }

  const handleDeleteTestCase = async (id: string) => {
    if (!safeConfig.dataVerse) return

    const service = new DataVerseService(safeConfig.dataVerse)

    try {
      await service.deleteTestCase(id)
      setSelectedTestCases(prev => {
        const next = new Set(prev)
        next.delete(id)
        return next
      })
      toast.success('Test case deleted')
      await loadTestCases()
    } catch (error) {
      toast.error('Failed to delete test case')
      console.error('Delete test case error:', error)
    }
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
    setSelectedTestCases(new Set(testCases.map(tc => tc.id)))
  }

  const deselectAll = () => {
    setSelectedTestCases(new Set())
  }

  const runEvaluation = async () => {
    if (!safeConfig.directLine) {
      toast.error('Please configure Direct Line credentials first')
      setSettingsOpen(true)
      return
    }

    if (!safeConfig.azureOpenAI) {
      toast.error('Please configure Azure OpenAI credentials first')
      setSettingsOpen(true)
      return
    }

    if (!safeConfig.dataVerse) {
      toast.error('Please configure DataVerse credentials first')
      setSettingsOpen(true)
      return
    }

    if (selectedTestCases.size === 0) {
      toast.error('Please select at least one test case')
      return
    }

    const selectedTests = testCases.filter(tc => selectedTestCases.has(tc.id))
    
    setRunning(true)
    setProgress({ current: 0, total: selectedTests.length })

    const newResults: EvaluationResult[] = []
    const directLine = new DirectLineService(safeConfig.directLine)
    const judge = new AzureOpenAIJudge(safeConfig.azureOpenAI)
    const dataVerse = new DataVerseService(safeConfig.dataVerse)

    try {
      for (let i = 0; i < selectedTests.length; i++) {
        const testCase = selectedTests[i]
        setProgress({ current: i + 1, total: selectedTests.length })

        try {
          const startTime = Date.now()
          const actualResponse = await directLine.sendMessage(testCase.prompt)
          
          const judgeResult = await judge.evaluate(
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
          await dataVerse.createEvaluationResult(result)
          
          directLine.resetConversation()
          
          if (i < selectedTests.length - 1) {
            await new Promise(resolve => setTimeout(resolve, 500))
          }
        } catch (error) {
          toast.error(`Failed to evaluate: ${testCase.prompt.substring(0, 50)}...`)
          console.error('Evaluation error:', error)
        }
      }

      await loadResults()
      
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

  const handleRefresh = async () => {
    await loadTestCases()
    await loadResults()
    toast.success('Data refreshed')
  }

  const passedResults = results.filter(r => r.passed).length
  const failedResults = results.filter(r => !r.passed).length
  const passRate = results.length > 0 ? Math.round((passedResults / results.length) * 100) : 0

  const isConfigured = safeConfig.directLine && safeConfig.azureOpenAI && safeConfig.dataVerse

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
                  Test and evaluate Copilot Studio agents with DataVerse
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              {isConfigured && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleRefresh}
                  disabled={loading}
                >
                  <ArrowsClockwise className="mr-2 h-4 w-4" />
                  Refresh
                </Button>
              )}
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
        </div>
      </header>

      <main className="container mx-auto px-6 py-6">
        {!isConfigured && (
          <Alert className="mb-6">
            <Warning className="h-4 w-4" />
            <AlertDescription>
              Please configure your Direct Line, Azure OpenAI, and DataVerse credentials in Settings to start.
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
            <TabsTrigger value="tests">
              Test Cases ({totalTestCases.toLocaleString()})
            </TabsTrigger>
            <TabsTrigger value="results">
              Results ({totalResults.toLocaleString()})
            </TabsTrigger>
          </TabsList>

          <TabsContent value="tests" className="space-y-4">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Button 
                  onClick={() => setTestDialogOpen(true)} 
                  size="sm"
                  disabled={!isConfigured}
                >
                  <Plus className="mr-2 h-4 w-4" />
                  Add Test Case
                </Button>
                {testCases.length > 0 && (
                  <>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={selectAll}
                      disabled={selectedTestCases.size === testCases.length}
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
                  disabled={running || !isConfigured}
                  className="bg-accent text-accent-foreground hover:bg-accent/90"
                >
                  <Play className="mr-2 h-4 w-4" weight="fill" />
                  Run Evaluation ({selectedTestCases.size})
                </Button>
              )}
            </div>

            {loading ? (
              <Card className="p-12 text-center">
                <CircleNotch className="h-12 w-12 mx-auto mb-4 text-muted-foreground animate-spin" />
                <h3 className="text-lg font-medium mb-2">Loading test cases...</h3>
              </Card>
            ) : testCases.length === 0 ? (
              <Card className="p-12 text-center">
                <Flask className="h-12 w-12 mx-auto mb-4 text-muted-foreground" />
                <h3 className="text-lg font-medium mb-2">No test cases yet</h3>
                <p className="text-sm text-muted-foreground mb-4">
                  {isConfigured 
                    ? 'Create your first test case to start evaluating your agent.'
                    : 'Configure your credentials in Settings first.'}
                </p>
                {isConfigured && (
                  <Button onClick={() => setTestDialogOpen(true)}>
                    <Plus className="mr-2 h-4 w-4" />
                    Add Test Case
                  </Button>
                )}
              </Card>
            ) : (
              <>
                <div className="grid gap-3">
                  {testCases.map(testCase => (
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
                {totalTestCases > PAGE_SIZE && (
                  <div className="flex items-center justify-center gap-2 mt-4">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setCurrentPage(p => Math.max(0, p - 1))}
                      disabled={currentPage === 0 || loading}
                    >
                      Previous
                    </Button>
                    <span className="text-sm text-muted-foreground">
                      Page {currentPage + 1} of {Math.ceil(totalTestCases / PAGE_SIZE)}
                    </span>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setCurrentPage(p => p + 1)}
                      disabled={currentPage >= Math.ceil(totalTestCases / PAGE_SIZE) - 1 || loading}
                    >
                      Next
                    </Button>
                  </div>
                )}
              </>
            )}
          </TabsContent>

          <TabsContent value="results" className="space-y-4">
            {results.length > 0 && (
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

            {results.length === 0 ? (
              <Card className="p-12 text-center">
                <CheckCircle className="h-12 w-12 mx-auto mb-4 text-muted-foreground" />
                <h3 className="text-lg font-medium mb-2">No results yet</h3>
                <p className="text-sm text-muted-foreground mb-4">
                  Run your first evaluation to see results here.
                </p>
              </Card>
            ) : (
              <div className="grid gap-3">
                {results.map(result => (
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
