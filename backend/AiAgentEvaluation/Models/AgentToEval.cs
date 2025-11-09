using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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
    [Required]
    public string DirectLineSpeechUrl { get; set; } = string.Empty;

    [JsonPropertyName("statecode")]
    public int StateCode { get; set; }

    [JsonPropertyName("statuscode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("createdon")]
    public DateTime CreatedOn { get; set; }

    [JsonPropertyName("modifiedon")]
    public DateTime ModifiedOn { get; set; }
}
