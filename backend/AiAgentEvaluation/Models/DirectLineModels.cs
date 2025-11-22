using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Direct Line API conversation response
/// </summary>
public class DirectLineConversationResponse
{
    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>
/// Direct Line API activity message
/// </summary>
public class DirectLineActivity
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("from")]
    public DirectLineFrom From { get; set; } = new();

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = "directline";

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    [JsonPropertyName("channelData")]
    public DirectLineChannelData? ChannelData { get; set; }
}

/// <summary>
/// Direct Line channel data from Copilot Studio
/// Contains various metadata fields that may or may not be present
/// </summary>
public class DirectLineChannelData
{
    [JsonPropertyName("isError")]
    public bool? IsError { get; set; }

    [JsonPropertyName("errorDetails")]
    public DirectLineErrorDetails? ErrorDetails { get; set; }
}

/// <summary>
/// Error details from Copilot Studio when isError is true
/// </summary>
public class DirectLineErrorDetails
{
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }
}

/// <summary>
/// Direct Line API from/to identity
/// </summary>
public class DirectLineFrom
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Direct Line API activities response
/// </summary>
public class DirectLineActivitiesResponse
{
    [JsonPropertyName("activities")]
    public DirectLineActivity[] Activities { get; set; } = Array.Empty<DirectLineActivity>();

    [JsonPropertyName("watermark")]
    public string? Watermark { get; set; }
}

/// <summary>
/// Token response from Copilot Studio token endpoint
/// </summary>
public class CopilotStudioTokenResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }
}