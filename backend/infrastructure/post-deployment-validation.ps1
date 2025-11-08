# Post-Deployment Validation Script
# This script validates that all Azure resources are properly deployed and configured

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$false)]
    [string]$FunctionAppName,
    
    [Parameter(Mandatory=$false)]
    [switch]$Detailed
)

$ErrorActionPreference = "Stop"
$WarningPreference = "Continue"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Post-Deployment Validation Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$validationResults = @()
$overallSuccess = $true

function Add-ValidationResult {
    param(
        [string]$Component,
        [string]$Check,
        [bool]$Success,
        [string]$Message,
        [string]$Severity = "Error"
    )
    
    $validationResults += [PSCustomObject]@{
        Component = $Component
        Check = $Check
        Success = $Success
        Message = $Message
        Severity = $Severity
    }
    
    if (-not $Success -and $Severity -eq "Error") {
        $script:overallSuccess = $false
    }
}

# 1. Validate Resource Group exists
Write-Host "[1/15] Validating Resource Group..." -ForegroundColor Yellow
try {
    $rg = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction Stop
    Write-Host "✓ Resource Group exists: $($rg.ResourceGroupName)" -ForegroundColor Green
    Add-ValidationResult -Component "ResourceGroup" -Check "Exists" -Success $true -Message "Resource group found"
}
catch {
    Write-Host "✗ Resource Group not found: $ResourceGroupName" -ForegroundColor Red
    Add-ValidationResult -Component "ResourceGroup" -Check "Exists" -Success $false -Message $_.Exception.Message
    exit 1
}

# 2. Get all resources in the resource group
Write-Host "[2/15] Discovering resources..." -ForegroundColor Yellow
$resources = Get-AzResource -ResourceGroupName $ResourceGroupName
Write-Host "Found $($resources.Count) resources in resource group" -ForegroundColor Cyan

# 3. Validate Function App
Write-Host "[3/15] Validating Function App..." -ForegroundColor Yellow
$functionApps = $resources | Where-Object { $_.ResourceType -eq "Microsoft.Web/sites" -and $_.Kind -like "*functionapp*" }

if ($functionApps.Count -eq 0) {
    Write-Host "✗ No Function App found" -ForegroundColor Red
    Add-ValidationResult -Component "FunctionApp" -Check "Exists" -Success $false -Message "No function app found"
    exit 1
}

if (-not $FunctionAppName) {
    $FunctionAppName = $functionApps[0].Name
}

$functionApp = Get-AzFunctionApp -ResourceGroupName $ResourceGroupName -Name $FunctionAppName -ErrorAction SilentlyContinue

if ($functionApp) {
    Write-Host "✓ Function App found: $($functionApp.Name)" -ForegroundColor Green
    Write-Host "  State: $($functionApp.State)" -ForegroundColor Cyan
    Write-Host "  Runtime: $($functionApp.Runtime)" -ForegroundColor Cyan
    Write-Host "  SKU: $($functionApp.AppServicePlan)" -ForegroundColor Cyan
    Add-ValidationResult -Component "FunctionApp" -Check "Exists" -Success $true -Message "Function app is deployed"
    
    # Check if app is running
    if ($functionApp.State -eq "Running") {
        Write-Host "✓ Function App is running" -ForegroundColor Green
        Add-ValidationResult -Component "FunctionApp" -Check "State" -Success $true -Message "App is running"
    }
    else {
        Write-Host "✗ Function App is not running: $($functionApp.State)" -ForegroundColor Red
        Add-ValidationResult -Component "FunctionApp" -Check "State" -Success $false -Message "App state is $($functionApp.State)"
    }
}
else {
    Write-Host "✗ Function App not found: $FunctionAppName" -ForegroundColor Red
    Add-ValidationResult -Component "FunctionApp" -Check "Exists" -Success $false -Message "Function app not found"
}

# 4. Validate Managed Identity
Write-Host "[4/15] Validating Managed Identity..." -ForegroundColor Yellow
try {
    $identity = Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $FunctionAppName | Select-Object -ExpandProperty Identity
    
    if ($identity.Type -eq "SystemAssigned") {
        Write-Host "✓ System-Assigned Managed Identity is enabled" -ForegroundColor Green
        Write-Host "  Principal ID: $($identity.PrincipalId)" -ForegroundColor Cyan
        Add-ValidationResult -Component "ManagedIdentity" -Check "Enabled" -Success $true -Message "System-assigned identity configured"
        
        # Store principal ID for RBAC checks
        $principalId = $identity.PrincipalId
    }
    else {
        Write-Host "✗ Managed Identity is not configured" -ForegroundColor Red
        Add-ValidationResult -Component "ManagedIdentity" -Check "Enabled" -Success $false -Message "Managed identity not enabled"
    }
}
catch {
    Write-Host "✗ Failed to check Managed Identity: $($_.Exception.Message)" -ForegroundColor Red
    Add-ValidationResult -Component "ManagedIdentity" -Check "Enabled" -Success $false -Message $_.Exception.Message
}

# 5. Validate Service Bus
Write-Host "[5/15] Validating Service Bus..." -ForegroundColor Yellow
$serviceBusNamespaces = $resources | Where-Object { $_.ResourceType -eq "Microsoft.ServiceBus/namespaces" }

if ($serviceBusNamespaces.Count -gt 0) {
    $sbNamespace = Get-AzServiceBusNamespace -ResourceGroupName $ResourceGroupName -Name $serviceBusNamespaces[0].Name
    Write-Host "✓ Service Bus Namespace found: $($sbNamespace.Name)" -ForegroundColor Green
    Write-Host "  SKU: $($sbNamespace.Sku.Name)" -ForegroundColor Cyan
    Add-ValidationResult -Component "ServiceBus" -Check "Exists" -Success $true -Message "Service Bus namespace deployed"
    
    # Check for evaluation queue
    $queues = Get-AzServiceBusQueue -ResourceGroupName $ResourceGroupName -Namespace $sbNamespace.Name
    $evalQueue = $queues | Where-Object { $_.Name -like "*evaluation*" }
    
    if ($evalQueue) {
        Write-Host "✓ Evaluation queue found: $($evalQueue.Name)" -ForegroundColor Green
        Add-ValidationResult -Component "ServiceBus" -Check "Queue" -Success $true -Message "Evaluation queue exists"
    }
    else {
        Write-Host "⚠ Evaluation queue not found" -ForegroundColor Yellow
        Add-ValidationResult -Component "ServiceBus" -Check "Queue" -Success $false -Message "Queue not found" -Severity "Warning"
    }
}
else {
    Write-Host "✗ Service Bus Namespace not found" -ForegroundColor Red
    Add-ValidationResult -Component "ServiceBus" -Check "Exists" -Success $false -Message "Service Bus not deployed"
}

# 6. Validate Storage Account
Write-Host "[6/15] Validating Storage Account..." -ForegroundColor Yellow
$storageAccounts = $resources | Where-Object { $_.ResourceType -eq "Microsoft.Storage/storageAccounts" }

if ($storageAccounts.Count -gt 0) {
    $storageAccount = Get-AzStorageAccount -ResourceGroupName $ResourceGroupName -Name $storageAccounts[0].Name
    Write-Host "✓ Storage Account found: $($storageAccount.StorageAccountName)" -ForegroundColor Green
    Write-Host "  SKU: $($storageAccount.Sku.Name)" -ForegroundColor Cyan
    Add-ValidationResult -Component "Storage" -Check "Exists" -Success $true -Message "Storage account deployed"
    
    # Check for prompt templates container
    $ctx = $storageAccount.Context
    $containers = Get-AzStorageContainer -Context $ctx
    $promptContainer = $containers | Where-Object { $_.Name -eq "prompt-templates" }
    
    if ($promptContainer) {
        Write-Host "✓ Prompt templates container found" -ForegroundColor Green
        Add-ValidationResult -Component "Storage" -Check "Container" -Success $true -Message "Prompt templates container exists"
    }
    else {
        Write-Host "⚠ Prompt templates container not found (will be created on first use)" -ForegroundColor Yellow
        Add-ValidationResult -Component "Storage" -Check "Container" -Success $true -Message "Container will be created automatically" -Severity "Warning"
    }
}
else {
    Write-Host "✗ Storage Account not found" -ForegroundColor Red
    Add-ValidationResult -Component "Storage" -Check "Exists" -Success $false -Message "Storage account not deployed"
}

# 7. Validate Key Vault
Write-Host "[7/15] Validating Key Vault..." -ForegroundColor Yellow
$keyVaults = $resources | Where-Object { $_.ResourceType -eq "Microsoft.KeyVault/vaults" }

if ($keyVaults.Count -gt 0) {
    $keyVault = Get-AzKeyVault -ResourceGroupName $ResourceGroupName -VaultName $keyVaults[0].Name
    Write-Host "✓ Key Vault found: $($keyVault.VaultName)" -ForegroundColor Green
    Write-Host "  URI: $($keyVault.VaultUri)" -ForegroundColor Cyan
    Add-ValidationResult -Component "KeyVault" -Check "Exists" -Success $true -Message "Key Vault deployed"
}
else {
    Write-Host "⚠ Key Vault not found (optional for managed identity-only deployments)" -ForegroundColor Yellow
    Add-ValidationResult -Component "KeyVault" -Check "Exists" -Success $true -Message "Key Vault not required" -Severity "Warning"
}

# 8. Validate Application Insights
Write-Host "[8/15] Validating Application Insights..." -ForegroundColor Yellow
$appInsights = $resources | Where-Object { $_.ResourceType -eq "microsoft.insights/components" }

if ($appInsights.Count -gt 0) {
    Write-Host "✓ Application Insights found: $($appInsights[0].Name)" -ForegroundColor Green
    
    $appInsightsResource = Get-AzApplicationInsights -ResourceGroupName $ResourceGroupName -Name $appInsights[0].Name
    Write-Host "  Instrumentation Key: $($appInsightsResource.InstrumentationKey.Substring(0,8))..." -ForegroundColor Cyan
    Add-ValidationResult -Component "AppInsights" -Check "Exists" -Success $true -Message "Application Insights deployed"
}
else {
    Write-Host "✗ Application Insights not found" -ForegroundColor Red
    Add-ValidationResult -Component "AppInsights" -Check "Exists" -Success $false -Message "Application Insights not deployed"
}

# 9. Validate RBAC Role Assignments (if managed identity exists)
Write-Host "[9/15] Validating RBAC Role Assignments..." -ForegroundColor Yellow
if ($principalId) {
    $roleAssignments = Get-AzRoleAssignment -ObjectId $principalId
    
    Write-Host "Found $($roleAssignments.Count) role assignments" -ForegroundColor Cyan
    
    $requiredRoles = @(
        "Storage Blob Data Contributor",
        "Service Bus Data Owner",
        "Cognitive Services OpenAI User"
    )
    
    foreach ($role in $requiredRoles) {
        $hasRole = $roleAssignments | Where-Object { $_.RoleDefinitionName -eq $role }
        
        if ($hasRole) {
            Write-Host "✓ Has role: $role" -ForegroundColor Green
            Add-ValidationResult -Component "RBAC" -Check $role -Success $true -Message "Role assigned"
        }
        else {
            Write-Host "⚠ Missing role: $role" -ForegroundColor Yellow
            Add-ValidationResult -Component "RBAC" -Check $role -Success $false -Message "Role not assigned" -Severity "Warning"
        }
    }
}
else {
    Write-Host "⚠ Skipping RBAC validation (no managed identity)" -ForegroundColor Yellow
}

# 10. Validate Function App Configuration
Write-Host "[10/15] Validating Function App Configuration..." -ForegroundColor Yellow
try {
    $appSettings = Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $FunctionAppName | Select-Object -ExpandProperty SiteConfig | Select-Object -ExpandProperty AppSettings
    
    $requiredSettings = @(
        "AzureWebJobsStorage",
        "FUNCTIONS_WORKER_RUNTIME",
        "StorageAccountName",
        "ServiceBusNamespace",
        "AzureOpenAIEndpoint"
    )
    
    foreach ($setting in $requiredSettings) {
        $hasSetting = $appSettings | Where-Object { $_.Name -eq $setting }
        
        if ($hasSetting) {
            Write-Host "✓ Configuration found: $setting" -ForegroundColor Green
            Add-ValidationResult -Component "Configuration" -Check $setting -Success $true -Message "Setting configured"
        }
        else {
            Write-Host "⚠ Configuration missing: $setting" -ForegroundColor Yellow
            Add-ValidationResult -Component "Configuration" -Check $setting -Success $false -Message "Setting not configured" -Severity "Warning"
        }
    }
}
catch {
    Write-Host "✗ Failed to validate configuration: $($_.Exception.Message)" -ForegroundColor Red
    Add-ValidationResult -Component "Configuration" -Check "AppSettings" -Success $false -Message $_.Exception.Message
}

# 11. Test Health Check Endpoint
Write-Host "[11/15] Testing Health Check Endpoint..." -ForegroundColor Yellow
try {
    $healthUrl = "https://$FunctionAppName.azurewebsites.net/api/health"
    Write-Host "Testing: $healthUrl" -ForegroundColor Cyan
    
    $response = Invoke-WebRequest -Uri $healthUrl -Method Get -TimeoutSec 30 -ErrorAction Stop
    
    if ($response.StatusCode -eq 200) {
        Write-Host "✓ Health endpoint returned 200 OK" -ForegroundColor Green
        Add-ValidationResult -Component "HealthCheck" -Check "BasicHealth" -Success $true -Message "Health endpoint responsive"
    }
    else {
        Write-Host "⚠ Health endpoint returned: $($response.StatusCode)" -ForegroundColor Yellow
        Add-ValidationResult -Component "HealthCheck" -Check "BasicHealth" -Success $false -Message "Unexpected status code" -Severity "Warning"
    }
}
catch {
    Write-Host "⚠ Health endpoint not accessible: $($_.Exception.Message)" -ForegroundColor Yellow
    Add-ValidationResult -Component "HealthCheck" -Check "BasicHealth" -Success $false -Message "Endpoint not accessible" -Severity "Warning"
}

# 12. Test Readiness Check Endpoint
Write-Host "[12/15] Testing Readiness Check Endpoint..." -ForegroundColor Yellow
try {
    $readinessUrl = "https://$FunctionAppName.azurewebsites.net/api/health/ready"
    Write-Host "Testing: $readinessUrl" -ForegroundColor Cyan
    
    $response = Invoke-WebRequest -Uri $readinessUrl -Method Get -TimeoutSec 30 -ErrorAction Stop
    
    if ($response.StatusCode -eq 200) {
        Write-Host "✓ Readiness endpoint returned 200 OK" -ForegroundColor Green
        $content = $response.Content | ConvertFrom-Json
        Write-Host "  Status: $($content.status)" -ForegroundColor Cyan
        Add-ValidationResult -Component "HealthCheck" -Check "Readiness" -Success $true -Message "All dependencies healthy"
        
        if ($Detailed -and $content.checks) {
            Write-Host "  Dependency Health:" -ForegroundColor Cyan
            $content.checks.PSObject.Properties | ForEach-Object {
                $checkName = $_.Name
                $checkValue = $_.Value
                Write-Host "    - $checkName : $($checkValue.Status) - $($checkValue.Message)" -ForegroundColor Cyan
            }
        }
    }
    else {
        Write-Host "⚠ Readiness endpoint returned: $($response.StatusCode)" -ForegroundColor Yellow
        Add-ValidationResult -Component "HealthCheck" -Check "Readiness" -Success $false -Message "Dependencies not ready" -Severity "Warning"
    }
}
catch {
    Write-Host "⚠ Readiness endpoint not accessible: $($_.Exception.Message)" -ForegroundColor Yellow
    Add-ValidationResult -Component "HealthCheck" -Check "Readiness" -Success $false -Message "Endpoint not accessible" -Severity "Warning"
}

# 13. Validate Network Configuration
Write-Host "[13/15] Validating Network Configuration..." -ForegroundColor Yellow
try {
    $webapp = Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $FunctionAppName
    
    if ($webapp.HttpsOnly) {
        Write-Host "✓ HTTPS only is enabled" -ForegroundColor Green
        Add-ValidationResult -Component "Network" -Check "HTTPSOnly" -Success $true -Message "HTTPS enforcement enabled"
    }
    else {
        Write-Host "⚠ HTTPS only is NOT enabled" -ForegroundColor Yellow
        Add-ValidationResult -Component "Network" -Check "HTTPSOnly" -Success $false -Message "HTTP is allowed" -Severity "Warning"
    }
    
    if ($webapp.ClientCertEnabled) {
        Write-Host "✓ Client certificates are enabled" -ForegroundColor Green
    }
    
    Write-Host "  Min TLS Version: $($webapp.SiteConfig.MinTlsVersion)" -ForegroundColor Cyan
}
catch {
    Write-Host "⚠ Could not validate network configuration: $($_.Exception.Message)" -ForegroundColor Yellow
}

# 14. Validate Monitoring Configuration
Write-Host "[14/15] Validating Monitoring Configuration..." -ForegroundColor Yellow
try {
    $diagnosticSettings = Get-AzDiagnosticSetting -ResourceId $functionApp.Id -ErrorAction SilentlyContinue
    
    if ($diagnosticSettings) {
        Write-Host "✓ Diagnostic settings are configured" -ForegroundColor Green
        Add-ValidationResult -Component "Monitoring" -Check "Diagnostics" -Success $true -Message "Diagnostic settings enabled"
    }
    else {
        Write-Host "⚠ No diagnostic settings found" -ForegroundColor Yellow
        Add-ValidationResult -Component "Monitoring" -Check "Diagnostics" -Success $false -Message "Diagnostics not configured" -Severity "Warning"
    }
}
catch {
    Write-Host "⚠ Could not validate monitoring: $($_.Exception.Message)" -ForegroundColor Yellow
}

# 15. Validate Auto-scale Configuration
Write-Host "[15/15] Validating Auto-scale Configuration..." -ForegroundColor Yellow
try {
    $appServicePlan = Get-AzAppServicePlan -ResourceGroupName $ResourceGroupName -Name $functionApp.AppServicePlan
    
    if ($appServicePlan.Sku.Tier -like "*Elastic*" -or $appServicePlan.Sku.Tier -eq "Premium") {
        Write-Host "✓ Auto-scale capable SKU: $($appServicePlan.Sku.Name)" -ForegroundColor Green
        Write-Host "  Max Workers: $($appServicePlan.MaximumElasticWorkerCount)" -ForegroundColor Cyan
        Add-ValidationResult -Component "AutoScale" -Check "SKU" -Success $true -Message "Auto-scale enabled"
    }
    else {
        Write-Host "⚠ SKU does not support auto-scale: $($appServicePlan.Sku.Tier)" -ForegroundColor Yellow
        Add-ValidationResult -Component "AutoScale" -Check "SKU" -Success $false -Message "Auto-scale not available" -Severity "Warning"
    }
}
catch {
    Write-Host "⚠ Could not validate auto-scale: $($_.Exception.Message)" -ForegroundColor Yellow
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Validation Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$successCount = ($validationResults | Where-Object { $_.Success }).Count
$errorCount = ($validationResults | Where-Object { -not $_.Success -and $_.Severity -eq "Error" }).Count
$warningCount = ($validationResults | Where-Object { -not $_.Success -and $_.Severity -eq "Warning" }).Count

Write-Host "Total Checks: $($validationResults.Count)" -ForegroundColor Cyan
Write-Host "Passed: $successCount" -ForegroundColor Green
Write-Host "Errors: $errorCount" -ForegroundColor Red
Write-Host "Warnings: $warningCount" -ForegroundColor Yellow

if ($Detailed) {
    Write-Host ""
    Write-Host "Detailed Results:" -ForegroundColor Cyan
    $validationResults | Format-Table -AutoSize
}

Write-Host ""
if ($overallSuccess) {
    Write-Host "✓ Deployment validation PASSED" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "✗ Deployment validation FAILED" -ForegroundColor Red
    Write-Host "Review errors above and remediate before proceeding to production" -ForegroundColor Yellow
    exit 1
}
