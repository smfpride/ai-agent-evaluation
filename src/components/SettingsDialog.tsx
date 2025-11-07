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
import { Alert, AlertDescription } from '@/components/ui/alert'
import { CheckCircle, XCircle, CircleNotch } from '@phosphor-icons/react'
import type { DirectLineConfig } from '@/types'
import { testConnection } from '@/lib/directline'

interface SettingsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  config: DirectLineConfig | null
  onSave: (config: DirectLineConfig) => void
}

export function SettingsDialog({
  open,
  onOpenChange,
  config,
  onSave,
}: SettingsDialogProps) {
  const [secret, setSecret] = useState(config?.secret || '')
  const [endpoint, setEndpoint] = useState(config?.endpoint || '')
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<'success' | 'error' | null>(null)

  useEffect(() => {
    if (config) {
      setSecret(config.secret)
      setEndpoint(config.endpoint || '')
    }
  }, [config])

  const handleTest = async () => {
    if (!secret.trim()) return

    setTesting(true)
    setTestResult(null)

    const result = await testConnection({
      secret: secret.trim(),
      endpoint: endpoint.trim() || undefined,
    })

    setTestResult(result ? 'success' : 'error')
    setTesting(false)
  }

  const handleSave = () => {
    if (!secret.trim()) return

    onSave({
      secret: secret.trim(),
      endpoint: endpoint.trim() || undefined,
    })
    onOpenChange(false)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Direct Line Configuration</DialogTitle>
          <DialogDescription>
            Configure your Direct Line API credentials to connect to your Copilot Studio agent.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-4 py-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="secret">Direct Line Secret *</Label>
            <Input
              id="secret"
              type="password"
              value={secret}
              onChange={e => setSecret(e.target.value)}
              placeholder="Enter your Direct Line secret..."
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="endpoint">Custom Endpoint (optional)</Label>
            <Input
              id="endpoint"
              value={endpoint}
              onChange={e => setEndpoint(e.target.value)}
              placeholder="https://directline.botframework.com/v3/directline"
            />
          </div>
          {testResult && (
            <Alert variant={testResult === 'success' ? 'default' : 'destructive'}>
              <div className="flex items-center gap-2">
                {testResult === 'success' ? (
                  <>
                    <CheckCircle className="h-4 w-4 text-success" />
                    <AlertDescription>
                      Connection successful! You can now run evaluations.
                    </AlertDescription>
                  </>
                ) : (
                  <>
                    <XCircle className="h-4 w-4" />
                    <AlertDescription>
                      Connection failed. Please check your credentials and try again.
                    </AlertDescription>
                  </>
                )}
              </div>
            </Alert>
          )}
        </div>
        <DialogFooter>
          <Button
            variant="outline"
            onClick={handleTest}
            disabled={!secret.trim() || testing}
          >
            {testing ? (
              <>
                <CircleNotch className="mr-2 h-4 w-4 animate-spin" />
                Testing...
              </>
            ) : (
              'Test Connection'
            )}
          </Button>
          <Button onClick={handleSave} disabled={!secret.trim()}>
            Save Configuration
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
