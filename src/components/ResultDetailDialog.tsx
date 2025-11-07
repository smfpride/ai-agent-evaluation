import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Separator } from '@/components/ui/separator'
import { Progress } from '@/components/ui/progress'
import { CheckCircle, XCircle } from '@phosphor-icons/react'
import type { EvaluationResult } from '@/types'
import { cn } from '@/lib/utils'

interface ResultDetailDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  result: EvaluationResult | null
}

export function ResultDetailDialog({
  open,
  onOpenChange,
  result,
}: ResultDetailDialogProps) {
  if (!result) return null

  const scoreColor =
    result.score >= 80
      ? 'text-success'
      : result.score >= 60
      ? 'text-warning'
      : 'text-destructive'

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl max-h-[90vh]">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-3">
            {result.passed ? (
              <CheckCircle className="h-6 w-6 text-success" weight="fill" />
            ) : (
              <XCircle className="h-6 w-6 text-destructive" weight="fill" />
            )}
            Evaluation Result
          </DialogTitle>
        </DialogHeader>
        <ScrollArea className="max-h-[calc(90vh-8rem)]">
          <div className="flex flex-col gap-6 pr-4">
            <div className="flex items-center gap-4">
              <div className="flex-1">
                <Progress value={result.score} className="h-3" />
              </div>
              <span className={cn('text-2xl font-semibold', scoreColor)}>
                {result.score}%
              </span>
              <Badge
                variant={result.passed ? 'default' : 'destructive'}
                className={cn(
                  result.passed && 'bg-success hover:bg-success/90'
                )}
              >
                {result.passed ? 'Pass' : 'Fail'}
              </Badge>
            </div>

            <Separator />

            <div>
              <h4 className="text-sm font-semibold mb-2">Prompt</h4>
              <Card className="p-4 bg-muted">
                <p className="font-mono text-sm whitespace-pre-wrap">
                  {result.prompt}
                </p>
              </Card>
            </div>

            <div>
              <h4 className="text-sm font-semibold mb-2">Expected Response</h4>
              <Card className="p-4 bg-muted">
                <p className="font-mono text-sm whitespace-pre-wrap">
                  {result.expectedResponse}
                </p>
              </Card>
            </div>

            <div>
              <h4 className="text-sm font-semibold mb-2">Actual Response</h4>
              <Card className="p-4 bg-muted">
                <p className="font-mono text-sm whitespace-pre-wrap">
                  {result.actualResponse}
                </p>
              </Card>
            </div>

            <Separator />

            <div>
              <h4 className="text-sm font-semibold mb-2">Judge Reasoning</h4>
              <Card className="p-4 border-accent/50">
                <p className="text-sm leading-relaxed">{result.reasoning}</p>
              </Card>
            </div>

            <div className="flex flex-wrap gap-4 text-xs text-muted-foreground">
              <div>
                <span className="font-medium">Execution Time:</span>{' '}
                {(result.executionTime / 1000).toFixed(2)}s
              </div>
              <div>
                <span className="font-medium">Timestamp:</span>{' '}
                {new Date(result.timestamp).toLocaleString()}
              </div>
            </div>
          </div>
        </ScrollArea>
      </DialogContent>
    </Dialog>
  )
}
