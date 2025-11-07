import { useState, useEffect } from 'react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { CheckCircle, XCircle, CircleNotch, Warning } from '@phosphor-icons/react'
import type { AppConfig } from '@/types'
import { testConnection } from '@/lib/directline'
import { testAzureOpenAIConnection } from '@/lib/judge'
import { testDataVerseConnection } from '@/lib/dataverse'

interface SettingsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  config: AppConfig
  onSave: (config: AppConfig) => void
}

export function SettingsDialog({
  open,
  onOpenChange,
  config,
  onSave,
}: SettingsDialogProps) {
  const [directLineEndpoint, setDirectLineEndpoint] = useState(config.directLine?.endpoint || '')
  const [directLineSecret, setDirectLineSecret] = useState(config.directLine?.secret || '')
  
  const [azureEndpoint, setAzureEndpoint] = useState(config.azureOpenAI?.endpoint || '')
  const [azureApiKey, setAzureApiKey] = useState(config.azureOpenAI?.apiKey || '')
  const [azureDeployment, setAzureDeployment] = useState(config.azureOpenAI?.deploymentName || '')
  const [azureApiVersion, setAzureApiVersion] = useState(config.azureOpenAI?.apiVersion || '2024-02-15-preview')
  
  const [dvEnvironmentUrl, setDvEnvironmentUrl] = useState(config.dataVerse?.environmentUrl || '')
  const [dvClientId, setDvClientId] = useState(config.dataVerse?.clientId || '')
  const [dvClientSecret, setDvClientSecret] = useState(config.dataVerse?.clientSecret || '')
  const [dvTenantId, setDvTenantId] = useState(config.dataVerse?.tenantId || '')
  const [dvTestCaseTable, setDvTestCaseTable] = useState(config.dataVerse?.testCaseTable || '')
  const [dvResultTable, setDvResultTable] = useState(config.dataVerse?.evaluationResultTable || '')

  const [testing, setTesting] = useState<string | null>(null)
  const [testResults, setTestResults] = useState<Record<string, 'success' | 'error'>>({})

  useEffect(() => {
    if (config.directLine) {
      setDirectLineEndpoint(config.directLine.endpoint)
      setDirectLineSecret(config.directLine.secret || '')
    }
    if (config.azureOpenAI) {
      setAzureEndpoint(config.azureOpenAI.endpoint)
      setAzureApiKey(config.azureOpenAI.apiKey)
      setAzureDeployment(config.azureOpenAI.deploymentName)
      setAzureApiVersion(config.azureOpenAI.apiVersion || '2024-02-15-preview')
    }
    if (config.dataVerse) {
      setDvEnvironmentUrl(config.dataVerse.environmentUrl)
      setDvClientId(config.dataVerse.clientId)
      setDvClientSecret(config.dataVerse.clientSecret)
      setDvTenantId(config.dataVerse.tenantId)
      setDvTestCaseTable(config.dataVerse.testCaseTable)
      setDvResultTable(config.dataVerse.evaluationResultTable)
    }
  }, [config])

  const handleTestDirectLine = async () => {
    if (!directLineEndpoint.trim()) return

    setTesting('directline')
    setTestResults(prev => ({ ...prev, directline: undefined as any }))

    const result = await testConnection({
      endpoint: directLineEndpoint.trim(),
      secret: directLineSecret.trim() || undefined,
    })

    setTestResults(prev => ({ ...prev, directline: result ? 'success' : 'error' }))
    setTesting(null)
  }

  const handleTestAzureOpenAI = async () => {
    if (!azureEndpoint.trim() || !azureApiKey.trim() || !azureDeployment.trim()) return

    setTesting('azure')
    setTestResults(prev => ({ ...prev, azure: undefined as any }))

    const result = await testAzureOpenAIConnection({
      endpoint: azureEndpoint.trim(),
      apiKey: azureApiKey.trim(),
      deploymentName: azureDeployment.trim(),
      apiVersion: azureApiVersion.trim(),
    })

    setTestResults(prev => ({ ...prev, azure: result ? 'success' : 'error' }))
    setTesting(null)
  }

  const handleTestDataVerse = async () => {
    if (!dvEnvironmentUrl.trim() || !dvClientId.trim() || !dvClientSecret.trim() || 
        !dvTenantId.trim() || !dvTestCaseTable.trim() || !dvResultTable.trim()) return

    setTesting('dataverse')
    setTestResults(prev => ({ ...prev, dataverse: undefined as any }))

    const result = await testDataVerseConnection({
      environmentUrl: dvEnvironmentUrl.trim(),
      clientId: dvClientId.trim(),
      clientSecret: dvClientSecret.trim(),
      tenantId: dvTenantId.trim(),
      testCaseTable: dvTestCaseTable.trim(),
      evaluationResultTable: dvResultTable.trim(),
    })

    setTestResults(prev => ({ ...prev, dataverse: result ? 'success' : 'error' }))
    setTesting(null)
  }

  const handleSave = () => {
    const newConfig: AppConfig = {
      directLine: directLineEndpoint.trim()
        ? {
            endpoint: directLineEndpoint.trim(),
            secret: directLineSecret.trim() || undefined,
          }
        : null,
      azureOpenAI: azureEndpoint.trim() && azureApiKey.trim() && azureDeployment.trim()
        ? {
            endpoint: azureEndpoint.trim(),
            apiKey: azureApiKey.trim(),
            deploymentName: azureDeployment.trim(),
            apiVersion: azureApiVersion.trim(),
          }
        : null,
      dataVerse: dvEnvironmentUrl.trim() && dvClientId.trim() && dvClientSecret.trim() && 
                 dvTenantId.trim() && dvTestCaseTable.trim() && dvResultTable.trim()
        ? {
            environmentUrl: dvEnvironmentUrl.trim(),
            clientId: dvClientId.trim(),
            clientSecret: dvClientSecret.trim(),
            tenantId: dvTenantId.trim(),
            testCaseTable: dvTestCaseTable.trim(),
            evaluationResultTable: dvResultTable.trim(),
          }
        : null,
    }

    onSave(newConfig)
    onOpenChange(false)
  }

  const isConfigValid = 
    directLineEndpoint.trim() && 
    azureEndpoint.trim() && azureApiKey.trim() && azureDeployment.trim() &&
    dvEnvironmentUrl.trim() && dvClientId.trim() && dvClientSecret.trim() && 
    dvTenantId.trim() && dvTestCaseTable.trim() && dvResultTable.trim()

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Configuration</DialogTitle>
          <DialogDescription>
            Configure your Direct Line, Azure OpenAI, and DataVerse credentials.
          </DialogDescription>
        </DialogHeader>
        
        <Tabs defaultValue="directline" className="w-full">
          <TabsList className="grid w-full grid-cols-3">
            <TabsTrigger value="directline">Direct Line</TabsTrigger>
            <TabsTrigger value="azure">Azure OpenAI</TabsTrigger>
            <TabsTrigger value="dataverse">DataVerse</TabsTrigger>
          </TabsList>

          <TabsContent value="directline" className="space-y-4 mt-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="directline-endpoint">Direct Line Endpoint *</Label>
              <Input
                id="directline-endpoint"
                value={directLineEndpoint}
                onChange={e => setDirectLineEndpoint(e.target.value)}
                placeholder="https://your-endpoint.azurewebsites.net/api/messages"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="directline-secret">Secret (optional)</Label>
              <Input
                id="directline-secret"
                type="password"
                value={directLineSecret}
                onChange={e => setDirectLineSecret(e.target.value)}
                placeholder="Enter secret if required..."
              />
            </div>
            {testResults.directline && (
              <Alert variant={testResults.directline === 'success' ? 'default' : 'destructive'}>
                <div className="flex items-center gap-2">
                  {testResults.directline === 'success' ? (
                    <>
                      <CheckCircle className="h-4 w-4 text-success" />
                      <AlertDescription>Connection successful!</AlertDescription>
                    </>
                  ) : (
                    <>
                      <XCircle className="h-4 w-4" />
                      <AlertDescription>Connection failed. Check your endpoint.</AlertDescription>
                    </>
                  )}
                </div>
              </Alert>
            )}
            <Button
              variant="outline"
              onClick={handleTestDirectLine}
              disabled={!directLineEndpoint.trim() || testing === 'directline'}
              className="w-full"
            >
              {testing === 'directline' ? (
                <>
                  <CircleNotch className="mr-2 h-4 w-4 animate-spin" />
                  Testing...
                </>
              ) : (
                'Test Connection'
              )}
            </Button>
          </TabsContent>

          <TabsContent value="azure" className="space-y-4 mt-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="azure-endpoint">Azure OpenAI Endpoint *</Label>
              <Input
                id="azure-endpoint"
                value={azureEndpoint}
                onChange={e => setAzureEndpoint(e.target.value)}
                placeholder="https://your-resource.openai.azure.com"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="azure-key">API Key *</Label>
              <Input
                id="azure-key"
                type="password"
                value={azureApiKey}
                onChange={e => setAzureApiKey(e.target.value)}
                placeholder="Enter your Azure OpenAI API key..."
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="azure-deployment">Deployment Name *</Label>
              <Input
                id="azure-deployment"
                value={azureDeployment}
                onChange={e => setAzureDeployment(e.target.value)}
                placeholder="gpt-4"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="azure-version">API Version</Label>
              <Input
                id="azure-version"
                value={azureApiVersion}
                onChange={e => setAzureApiVersion(e.target.value)}
                placeholder="2024-02-15-preview"
              />
            </div>
            {testResults.azure && (
              <Alert variant={testResults.azure === 'success' ? 'default' : 'destructive'}>
                <div className="flex items-center gap-2">
                  {testResults.azure === 'success' ? (
                    <>
                      <CheckCircle className="h-4 w-4 text-success" />
                      <AlertDescription>Connection successful!</AlertDescription>
                    </>
                  ) : (
                    <>
                      <XCircle className="h-4 w-4" />
                      <AlertDescription>Connection failed. Check your credentials.</AlertDescription>
                    </>
                  )}
                </div>
              </Alert>
            )}
            <Button
              variant="outline"
              onClick={handleTestAzureOpenAI}
              disabled={!azureEndpoint.trim() || !azureApiKey.trim() || !azureDeployment.trim() || testing === 'azure'}
              className="w-full"
            >
              {testing === 'azure' ? (
                <>
                  <CircleNotch className="mr-2 h-4 w-4 animate-spin" />
                  Testing...
                </>
              ) : (
                'Test Connection'
              )}
            </Button>
          </TabsContent>

          <TabsContent value="dataverse" className="space-y-4 mt-4">
            <Alert className="mb-4">
              <Warning className="h-4 w-4" />
              <AlertDescription>
                <strong>Browser CORS Limitation:</strong> Microsoft Entra ID and DataVerse APIs do not allow direct browser access due to CORS policies. 
                To use DataVerse with this browser-based app, you'll need to set up a backend proxy (e.g., Azure Function, Express server) 
                that handles authentication and forwards requests. See <code>README.md</code> and <code>DATAVERSE_CORS.md</code> for detailed solutions and example code.
              </AlertDescription>
            </Alert>
            <div className="flex flex-col gap-2">
              <Label htmlFor="dv-url">Environment URL *</Label>
              <Input
                id="dv-url"
                value={dvEnvironmentUrl}
                onChange={e => setDvEnvironmentUrl(e.target.value)}
                placeholder="https://your-org.crm.dynamics.com"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="dv-client-id">Client ID *</Label>
              <Input
                id="dv-client-id"
                value={dvClientId}
                onChange={e => setDvClientId(e.target.value)}
                placeholder="Enter your application client ID..."
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="dv-client-secret">Client Secret *</Label>
              <Input
                id="dv-client-secret"
                type="password"
                value={dvClientSecret}
                onChange={e => setDvClientSecret(e.target.value)}
                placeholder="Enter your application client secret..."
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="dv-tenant-id">Tenant ID *</Label>
              <Input
                id="dv-tenant-id"
                value={dvTenantId}
                onChange={e => setDvTenantId(e.target.value)}
                placeholder="Enter your Azure AD tenant ID..."
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="dv-testcase-table">Test Case Table Name *</Label>
              <Input
                id="dv-testcase-table"
                value={dvTestCaseTable}
                onChange={e => setDvTestCaseTable(e.target.value)}
                placeholder="mcicaps_testcases"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="dv-result-table">Evaluation Result Table Name *</Label>
              <Input
                id="dv-result-table"
                value={dvResultTable}
                onChange={e => setDvResultTable(e.target.value)}
                placeholder="mcicaps_evaluationresults"
              />
            </div>
            {testResults.dataverse && (
              <Alert variant={testResults.dataverse === 'success' ? 'default' : 'destructive'}>
                <div className="flex items-center gap-2">
                  {testResults.dataverse === 'success' ? (
                    <>
                      <CheckCircle className="h-4 w-4 text-success" />
                      <AlertDescription>Connection successful!</AlertDescription>
                    </>
                  ) : (
                    <>
                      <XCircle className="h-4 w-4" />
                      <AlertDescription>
                        Connection failed - this is expected due to browser CORS restrictions. 
                        See README.md for proxy setup instructions, or check browser console if using a proxy.
                      </AlertDescription>
                    </>
                  )}
                </div>
              </Alert>
            )}
            <Button
              variant="outline"
              onClick={handleTestDataVerse}
              disabled={
                !dvEnvironmentUrl.trim() || !dvClientId.trim() || !dvClientSecret.trim() || 
                !dvTenantId.trim() || !dvTestCaseTable.trim() || !dvResultTable.trim() || 
                testing === 'dataverse'
              }
              className="w-full"
            >
              {testing === 'dataverse' ? (
                <>
                  <CircleNotch className="mr-2 h-4 w-4 animate-spin" />
                  Testing...
                </>
              ) : (
                'Test Connection'
              )}
            </Button>
          </TabsContent>
        </Tabs>

        <DialogFooter>
          <Button onClick={handleSave} disabled={!isConfigValid}>
            Save Configuration
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
