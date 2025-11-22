using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Basic health check function that doesn't depend on any services
/// </summary>
public class BasicHealthFunction
{
    private readonly ILogger<BasicHealthFunction> _logger;
    
    // Build timestamp to track deployments - updated with each build
    private static readonly string BuildTimestamp = "2025-11-22T02:00:00Z";
    private static readonly string DeploymentVersion = "1.0.57"; // Dataverse IsMultiTurn field diagnostic logging

    public BasicHealthFunction(ILogger<BasicHealthFunction> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Basic health check endpoint - returns 200 if function app is running
    /// This endpoint doesn't depend on any external services
    /// </summary>
    [Function("BasicHealth")]
    public async Task<HttpResponseData> BasicHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/basic")] HttpRequestData req)
    {
        _logger.LogInformation("Basic health check endpoint called");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow,
            version = DeploymentVersion,
            buildTimestamp = BuildTimestamp,
            message = "Function app is running"
        });

        return response;
    }
}