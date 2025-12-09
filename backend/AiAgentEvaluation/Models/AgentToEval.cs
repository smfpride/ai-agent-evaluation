using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiAgentEvaluation.Services.Adapters;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Represents an agent to evaluate entity from Dataverse (mcicaps_agenttoeval)
/// Contains the Direct Line Speech URL for the agent
/// </summary>
public class AgentToEval
{
    [JsonPropertyName("mcicaps_agenttoevalid")]
    public Guid Id { get; set; }

    [JsonPropertyName("mcicaps_name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mcicaps_directlinespeechurl")]
    public string DirectLineSpeechUrl { get; set; } = string.Empty;

    [JsonPropertyName("mcicaps_agenttype")]
    public int? AgentType { get; set; }

    [JsonPropertyName("mcicaps_agentconfiguration")]
    public string? AgentConfiguration { get; set; }

    [JsonPropertyName("statecode")]
    public int StateCode { get; set; }

    [JsonPropertyName("statuscode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("createdon")]
    public DateTime CreatedOn { get; set; }

    [JsonPropertyName("modifiedon")]
    public DateTime ModifiedOn { get; set; }

    /// <summary>
    /// Parse configuration JSON into strongly-typed object
    /// </summary>
    public AgentConfiguration GetConfiguration()
    {
        var config = new Dictionary<string, object>();
        
        if (!string.IsNullOrEmpty(AgentConfiguration))
        {
            try
            {
                config = JsonSerializer.Deserialize<Dictionary<string, object>>(AgentConfiguration) 
                    ?? new Dictionary<string, object>();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse agent configuration JSON for agent {Name}", ex);
            }
        }
        
        // Backward compatibility: If no AgentType set, default to DirectLine
        var agentType = AgentType ?? 778760000;
        
        // Backward compatibility: If DirectLineSpeechUrl set but no config, migrate
        if (config.Count == 0 && !string.IsNullOrEmpty(DirectLineSpeechUrl))
        {
            config["directLineUrl"] = DirectLineSpeechUrl;
        }
        
        return new AgentConfiguration
        {
            AgentType = agentType,
            Config = config
        };
    }
}
