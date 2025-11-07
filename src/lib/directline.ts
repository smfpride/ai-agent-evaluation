import type { DirectLineConfig } from '@/types'

interface DirectLineConversation {
  conversationId: string
  token: string
  expiresIn: number
}

interface DirectLineActivity {
  type: string
  from: { id: string; name?: string }
  text?: string
  timestamp: string
  id: string
}

export class DirectLineService {
  private config: DirectLineConfig
  private conversationId: string | null = null
  private watermark: string | null = null

  constructor(config: DirectLineConfig) {
    this.config = config
  }

  private getHeaders(): HeadersInit {
    const headers: HeadersInit = {
      'Content-Type': 'application/json',
    }
    
    if (this.config.secret) {
      headers['Authorization'] = `Bearer ${this.config.secret}`
    }
    
    return headers
  }

  async startConversation(): Promise<void> {
    const response = await fetch(`${this.config.endpoint}/conversations`, {
      method: 'POST',
      headers: this.getHeaders(),
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(`Failed to start conversation: ${response.statusText} - ${errorText}`)
    }

    const data: DirectLineConversation = await response.json()
    this.conversationId = data.conversationId
    this.watermark = null
  }

  async sendMessage(text: string): Promise<string> {
    if (!this.conversationId) {
      await this.startConversation()
    }

    const response = await fetch(
      `${this.config.endpoint}/conversations/${this.conversationId}/activities`,
      {
        method: 'POST',
        headers: this.getHeaders(),
        body: JSON.stringify({
          type: 'message',
          from: { id: 'user' },
          text,
        }),
      }
    )

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(`Failed to send message: ${response.statusText} - ${errorText}`)
    }

    return this.waitForResponse()
  }

  private async waitForResponse(maxAttempts = 30): Promise<string> {
    for (let attempt = 0; attempt < maxAttempts; attempt++) {
      await new Promise(resolve => setTimeout(resolve, 1000))

      const url = this.watermark
        ? `${this.config.endpoint}/conversations/${this.conversationId}/activities?watermark=${this.watermark}`
        : `${this.config.endpoint}/conversations/${this.conversationId}/activities`

      const headers: HeadersInit = {}
      if (this.config.secret) {
        headers['Authorization'] = `Bearer ${this.config.secret}`
      }

      const response = await fetch(url, { headers })

      if (!response.ok) {
        const errorText = await response.text()
        throw new Error(`Failed to get activities: ${response.statusText} - ${errorText}`)
      }

      const data: { activities: DirectLineActivity[]; watermark: string } = await response.json()
      this.watermark = data.watermark

      const botMessages = data.activities.filter(
        activity => activity.type === 'message' && activity.from.id !== 'user'
      )

      if (botMessages.length > 0) {
        const lastMessage = botMessages[botMessages.length - 1]
        return lastMessage.text || 'No text in response'
      }
    }

    throw new Error('Timeout waiting for bot response')
  }

  resetConversation(): void {
    this.conversationId = null
    this.watermark = null
  }
}

export async function testConnection(config: DirectLineConfig): Promise<boolean> {
  try {
    const service = new DirectLineService(config)
    await service.startConversation()
    return true
  } catch (error) {
    console.error('Connection test failed:', error)
    return false
  }
}
