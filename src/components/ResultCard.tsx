import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Progress } from '@/components/ui/progress'
import { CheckCircle, XCircle } from '@phosphor-icons/react'
import type { EvaluationResult } from '@/types'
import { cn } from '@/lib/utils'

interface ResultCardProps {
  result: EvaluationResult
  onClick: () => void
}

export function ResultCard({ result, onClick }: ResultCardProps) {
  const scoreColor =
    result.score >= 80
      ? 'text-success'
      : result.score >= 60
      ? 'text-warning'
      : 'text-destructive'

  return (
    <Card
      className="p-4 cursor-pointer hover:shadow-md transition-shadow"
      onClick={onClick}
    >
      <div className="flex items-start gap-3">
        <div className="pt-0.5">
          {result.passed ? (
            <CheckCircle className="h-5 w-5 text-success" weight="fill" />
          ) : (
            <XCircle className="h-5 w-5 text-destructive" weight="fill" />
          )}
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-start justify-between gap-2 mb-2">
            <p className="font-mono text-sm font-medium text-foreground line-clamp-2">
              {result.prompt}
            </p>
            <Badge
              variant={result.passed ? 'default' : 'destructive'}
              className={cn(
                'shrink-0',
                result.passed && 'bg-success hover:bg-success/90'
              )}
            >
              {result.passed ? 'Pass' : 'Fail'}
            </Badge>
          </div>
          <div className="flex items-center gap-3 mb-2">
            <div className="flex-1">
              <Progress value={result.score} className="h-2" />
            </div>
            <span className={cn('text-sm font-semibold', scoreColor)}>
              {result.score}%
            </span>
          </div>
          <p className="text-xs text-muted-foreground line-clamp-2">
            {result.reasoning}
          </p>
        </div>
      </div>
    </Card>
  )
}
