import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Checkbox } from '@/components/ui/checkbox'
import { Pencil, Trash } from '@phosphor-icons/react'
import type { TestCase } from '@/types'

interface TestCaseCardProps {
  testCase: TestCase
  selected: boolean
  onSelect: (selected: boolean) => void
  onEdit: () => void
  onDelete: () => void
}

export function TestCaseCard({
  testCase,
  selected,
  onSelect,
  onEdit,
  onDelete,
}: TestCaseCardProps) {
  return (
    <Card className="p-4 hover:shadow-md transition-shadow">
      <div className="flex items-start gap-3">
        <div className="pt-0.5">
          <Checkbox checked={selected} onCheckedChange={onSelect} />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-start justify-between gap-2 mb-2">
            <div className="flex-1">
              <p className="font-mono text-sm font-medium text-foreground line-clamp-2 mb-1">
                {testCase.prompt}
              </p>
              <p className="font-mono text-xs text-muted-foreground line-clamp-2">
                Expected: {testCase.expectedResponse}
              </p>
            </div>
            <div className="flex gap-1 shrink-0">
              <Button
                variant="ghost"
                size="sm"
                onClick={onEdit}
                className="h-8 w-8 p-0"
              >
                <Pencil className="h-4 w-4" />
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={onDelete}
                className="h-8 w-8 p-0 text-destructive hover:text-destructive"
              >
                <Trash className="h-4 w-4" />
              </Button>
            </div>
          </div>
          {testCase.tags && testCase.tags.length > 0 && (
            <div className="flex flex-wrap gap-1 mt-2">
              {testCase.tags.map(tag => (
                <Badge key={tag} variant="secondary" className="text-xs">
                  {tag}
                </Badge>
              ))}
            </div>
          )}
        </div>
      </div>
    </Card>
  )
}
