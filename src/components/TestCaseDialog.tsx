import { useState } from 'react'
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
import { Textarea } from '@/components/ui/textarea'
import type { TestCase } from '@/types'

interface TestCaseDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSave: (testCase: Omit<TestCase, 'id' | 'createdAt' | 'updatedAt'>) => void
  editingTestCase?: TestCase
}

export function TestCaseDialog({
  open,
  onOpenChange,
  onSave,
  editingTestCase,
}: TestCaseDialogProps) {
  const [prompt, setPrompt] = useState(editingTestCase?.prompt || '')
  const [expectedResponse, setExpectedResponse] = useState(
    editingTestCase?.expectedResponse || ''
  )
  const [tags, setTags] = useState(editingTestCase?.tags?.join(', ') || '')

  const handleSave = () => {
    if (!prompt.trim() || !expectedResponse.trim()) return

    onSave({
      prompt: prompt.trim(),
      expectedResponse: expectedResponse.trim(),
      tags: tags
        .split(',')
        .map(t => t.trim())
        .filter(Boolean),
    })

    setPrompt('')
    setExpectedResponse('')
    setTags('')
    onOpenChange(false)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>
            {editingTestCase ? 'Edit Test Case' : 'Add Test Case'}
          </DialogTitle>
          <DialogDescription>
            Create a test case with a prompt and expected response for evaluation.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-4 py-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor="prompt">Prompt</Label>
            <Textarea
              id="prompt"
              value={prompt}
              onChange={e => setPrompt(e.target.value)}
              placeholder="Enter the prompt to send to the agent..."
              className="min-h-24 font-mono text-sm"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="expected">Expected Response</Label>
            <Textarea
              id="expected"
              value={expectedResponse}
              onChange={e => setExpectedResponse(e.target.value)}
              placeholder="Enter the expected response..."
              className="min-h-32 font-mono text-sm"
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="tags">Tags (comma-separated)</Label>
            <Input
              id="tags"
              value={tags}
              onChange={e => setTags(e.target.value)}
              placeholder="e.g., greeting, product-info, support"
            />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            onClick={handleSave}
            disabled={!prompt.trim() || !expectedResponse.trim()}
          >
            {editingTestCase ? 'Save Changes' : 'Add Test Case'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
