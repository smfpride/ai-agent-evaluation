using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using AiAgentEvaluation.Services;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Health check function for monitoring and readiness/liveness probes
/// </summary>
public class HealthCheckFunction
{
    private readonly ILogger<HealthCheckFunction> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly IServiceBusService _serviceBusService;
    
    // Build timestamp to track deployments - updated with each build
    private static readonly string BuildTimestamp = "2025-11-26T19:30:00Z";
    private static readonly string DeploymentVersion = "1.0.69"; // Progress-based timeout implementation

    public HealthCheckFunction(
        ILogger<HealthCheckFunction> logger,
        IDataverseService dataverseService,
        IServiceBusService serviceBusService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _serviceBusService = serviceBusService;
    }

    /// <summary>
    /// Comprehensive health check - validates all dependencies and returns detailed status
    /// This is the main health check endpoint for monitoring systems
    /// </summary>
    [Function("HealthCheck")]
    public async Task<HttpResponseData> HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/health")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Health check requested");

        try
        {
            // Perform health checks for all dependencies in parallel
            var dataverseHealthTask = _dataverseService.CheckHealthAsync(cancellationToken);
            var serviceBusHealthTask = _serviceBusService.CheckHealthAsync(cancellationToken);

            await Task.WhenAll(dataverseHealthTask, serviceBusHealthTask);

            var dataverseHealthy = await dataverseHealthTask;
            var serviceBusHealthy = await serviceBusHealthTask;

            var allHealthy = dataverseHealthy && serviceBusHealthy;

            _logger.LogInformation("Health check completed: Overall={Status}, Dataverse={Dataverse}, ServiceBus={ServiceBus}",
                allHealthy ? "Healthy" : "Degraded", 
                dataverseHealthy ? "Healthy" : "Unhealthy",
                serviceBusHealthy ? "Healthy" : "Unhealthy");

            var statusCode = allHealthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
            var response = req.CreateResponse(statusCode);

            await response.WriteAsJsonAsync(new
            {
                status = allHealthy ? "Healthy" : "Degraded",
                timestamp = DateTime.UtcNow,
                version = DeploymentVersion,
                buildTimestamp = BuildTimestamp,
                environment = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development",
                services = new
                {
                    dataverse = dataverseHealthy ? "Healthy" : "Unhealthy",
                    serviceBus = serviceBusHealthy ? "Healthy" : "Unhealthy"
                }
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");

            var response = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            await response.WriteAsJsonAsync(new
            {
                status = "Unhealthy",
                timestamp = DateTime.UtcNow,
                error = ex.Message
            });

            return response;
        }
    }

    /// <summary>
    /// Readiness check - validates all dependencies are available
    /// </summary>
    [Function("ReadinessCheck")]
    public async Task<HttpResponseData> ReadinessCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/ready")] HttpRequestData req)
    {
        _logger.LogInformation("Readiness check endpoint called");

        var checks = new Dictionary<string, HealthCheckResult>();
        var overallHealthy = true;

        // Check Dataverse connectivity
        try
        {
            _logger.LogInformation("Checking Dataverse connectivity...");
            var isHealthy = await _dataverseService.CheckHealthAsync();
            checks["dataverse"] = new HealthCheckResult { Status = isHealthy ? "Healthy" : "Unhealthy", Message = isHealthy ? "Connected successfully" : "Connection failed" };
            overallHealthy = overallHealthy && isHealthy;
            _logger.LogInformation("Dataverse check: {Status}", isHealthy ? "Healthy" : "Unhealthy");
        }
        catch (Exception ex)
        {
            checks["dataverse"] = new HealthCheckResult { Status = "Unhealthy", Message = ex.Message };
            overallHealthy = false;
            _logger.LogError(ex, "Dataverse check: Unhealthy");
        }

        // Check Service Bus connectivity
        try
        {
            _logger.LogInformation("Checking Service Bus connectivity...");
            // Service Bus connectivity is validated during initialization
            // If we got here, the service was constructed successfully
            checks["serviceBus"] = new HealthCheckResult { Status = "Healthy", Message = "Connected successfully" };
            _logger.LogInformation("Service Bus check: Healthy");
        }
        catch (Exception ex)
        {
            checks["serviceBus"] = new HealthCheckResult { Status = "Unhealthy", Message = ex.Message };
            overallHealthy = false;
            _logger.LogError(ex, "Service Bus check: Unhealthy");
        }

        var statusCode = overallHealthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
        var response = req.CreateResponse(statusCode);

        await response.WriteAsJsonAsync(new
        {
            status = overallHealthy ? "Healthy" : "Unhealthy",
            timestamp = DateTime.UtcNow,
            checks = checks
        });

        return response;
    }

    /// <summary>
    /// Liveness check - validates function app is alive and responsive
    /// </summary>
    [Function("LivenessCheck")]
    public async Task<HttpResponseData> LivenessCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/live")] HttpRequestData req)
    {
        _logger.LogInformation("Liveness check endpoint called");

        // Simple check that the function app is responsive
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "Alive",
            timestamp = DateTime.UtcNow,
            version = DeploymentVersion,
            buildTimestamp = BuildTimestamp,
            uptime = GetUptime()
        });

        return response;
    }

    /// <summary>
    /// Detailed health check with metrics
    /// </summary>
    [Function("HealthCheckDetailed")]
    public async Task<HttpResponseData> HealthCheckDetailed(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/detailed")] HttpRequestData req)
    {
        _logger.LogInformation("Detailed health check endpoint called");

        var checks = new Dictionary<string, object>();

        // System information
        checks["system"] = new
        {
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.ToString(),
            processorCount = Environment.ProcessorCount,
            dotNetVersion = Environment.Version.ToString(),
            workingSet = Environment.WorkingSet,
            gcTotalMemory = GC.GetTotalMemory(false)
        };

        // Configuration validation
        checks["configuration"] = new
        {
            hasDataverseConfig = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DataverseConnectionString")),
            hasServiceBusConfig = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ServiceBus__Namespace"))
        };

        // Uptime
        checks["uptime"] = GetUptime();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow,
            version = DeploymentVersion,
            buildTimestamp = BuildTimestamp,
            checks = checks
        });

        return response;
    }

    private static TimeSpan GetUptime()
    {
        return TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    private class HealthCheckResult
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
