import { Card } from '@/components/ui/card'
import { Info } from '@phosphor-icons/react'

export function ProxyArchitectureDiagram() {
  return (
    <Card className="p-4 bg-muted/30 border-accent/20">
      <div className="flex items-start gap-3 mb-3">
        <Info className="h-5 w-5 text-accent flex-shrink-0 mt-0.5" weight="fill" />
        <div>
          <h4 className="font-medium text-sm mb-1">How the Backend Proxy Works</h4>
          <p className="text-sm text-muted-foreground">
            The proxy solves browser CORS restrictions by handling authentication server-side
          </p>
        </div>
      </div>
      
      <div className="space-y-2 text-sm font-mono">
        <div className="flex items-center gap-2">
          <div className="w-32 truncate text-foreground">Browser App</div>
          <div className="text-accent">→</div>
          <div className="flex-1 truncate text-foreground">Your Proxy</div>
          <div className="text-accent">→</div>
          <div className="w-32 truncate text-muted-foreground">Entra ID</div>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-32 truncate text-foreground">Browser App</div>
          <div className="text-accent">←</div>
          <div className="flex-1 truncate text-foreground">Your Proxy</div>
          <div className="text-accent">→</div>
          <div className="w-32 truncate text-muted-foreground">DataVerse</div>
        </div>
      </div>
      
      <div className="mt-3 pt-3 border-t border-border">
        <p className="text-xs text-muted-foreground">
          See <code className="px-1 py-0.5 bg-muted rounded">PROXY_IMPLEMENTATION.md</code> for complete deployment guides
        </p>
      </div>
    </Card>
  )
}
