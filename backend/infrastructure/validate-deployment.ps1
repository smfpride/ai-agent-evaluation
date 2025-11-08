#Requires -Version 7.0
#Requires -Module Az

<#
.SYNOPSIS
    Validates the AI Agent Evaluation Function deployment and managed identity configuration.

.DESCRIPTION
    This script validates that all Azure resources are properly configured with managed identity,
    RBAC permissions are correctly assigned, and the Function App can authenticate to all services.

.PARAMETER SubscriptionId
    Azure subscription ID where resources are deployed.

.PARAMETER ResourceGroupName
    Name of the resource group containing the resources.

.PARAMETER FunctionAppName
    Name of the Function App to validate.

.EXAMPLE
    .\validate-deployment.ps1 -SubscriptionId "12345678-1234-1234-1234-123456789012" -ResourceGroupName "rg-ai-agent-eval" -FunctionAppName "ai-agent-eval-dev-func"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory = $true)]
    [string]$FunctionAppName
)

# Set error action preference
$ErrorActionPreference = "Stop"

Write-Host "🔍 Starting deployment validation..." -ForegroundColor Green

# Authenticate to Azure
Write-Host "🔐 Authenticating to Azure..." -ForegroundColor Yellow
try {
    $context = Get-AzContext
    if (-not $context -or $context.Subscription.Id -ne $SubscriptionId) {
        Connect-AzAccount -SubscriptionId $SubscriptionId
    }
    Set-AzContext -SubscriptionId $SubscriptionId
    Write-Host "✅ Successfully authenticated to subscription: $SubscriptionId" -ForegroundColor Green
}
catch {
    Write-Error "❌ Failed to authenticate to Azure: $($_.Exception.Message)"
    exit 1
}

# Get Function App
Write-Host "📱 Validating Function App..." -ForegroundColor Yellow
try {
    $functionApp = Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $FunctionAppName
    if (-not $functionApp) {
        Write-Error "❌ Function App '$FunctionAppName' not found in resource group '$ResourceGroupName'"
        exit 1
    }
    
    # Check if managed identity is enabled
    if ($functionApp.Identity.Type -eq "SystemAssigned") {
        $principalId = $functionApp.Identity.PrincipalId
        Write-Host "✅ System-assigned managed identity enabled. Principal ID: $principalId" -ForegroundColor Green
    }
    else {
        Write-Error "❌ System-assigned managed identity is not enabled on Function App"
        exit 1
    }
}
catch {
    Write-Error "❌ Failed to validate Function App: $($_.Exception.Message)"
    exit 1
}

# Validate RBAC assignments
Write-Host "🔐 Validating RBAC assignments..." -ForegroundColor Yellow

$validationResults = @()

# Key Vault Secrets User role
try {
    $keyVaults = Get-AzKeyVault -ResourceGroupName $ResourceGroupName
    foreach ($keyVault in $keyVaults) {
        $roleAssignments = Get-AzRoleAssignment -ObjectId $principalId -Scope $keyVault.ResourceId -RoleDefinitionName "Key Vault Secrets User" -ErrorAction SilentlyContinue
        if ($roleAssignments) {
            $validationResults += "✅ Key Vault Secrets User role assigned to $($keyVault.VaultName)"
            Write-Host "✅ Key Vault Secrets User role assigned to $($keyVault.VaultName)" -ForegroundColor Green
        }
        else {
            $validationResults += "❌ Key Vault Secrets User role NOT assigned to $($keyVault.VaultName)"
            Write-Host "❌ Key Vault Secrets User role NOT assigned to $($keyVault.VaultName)" -ForegroundColor Red
        }
    }
}
catch {
    Write-Warning "⚠️ Failed to validate Key Vault RBAC: $($_.Exception.Message)"
}

# Storage Blob Data Contributor role
try {
    $storageAccounts = Get-AzStorageAccount -ResourceGroupName $ResourceGroupName
    foreach ($storageAccount in $storageAccounts) {
        $roleAssignments = Get-AzRoleAssignment -ObjectId $principalId -Scope $storageAccount.Id -RoleDefinitionName "Storage Blob Data Contributor" -ErrorAction SilentlyContinue
        if ($roleAssignments) {
            $validationResults += "✅ Storage Blob Data Contributor role assigned to $($storageAccount.StorageAccountName)"
            Write-Host "✅ Storage Blob Data Contributor role assigned to $($storageAccount.StorageAccountName)" -ForegroundColor Green
        }
        else {
            $validationResults += "❌ Storage Blob Data Contributor role NOT assigned to $($storageAccount.StorageAccountName)"
            Write-Host "❌ Storage Blob Data Contributor role NOT assigned to $($storageAccount.StorageAccountName)" -ForegroundColor Red
        }
    }
}
catch {
    Write-Warning "⚠️ Failed to validate Storage RBAC: $($_.Exception.Message)"
}

# Service Bus Data Owner role
try {
    $serviceBusNamespaces = Get-AzServiceBusNamespace -ResourceGroupName $ResourceGroupName
    foreach ($namespace in $serviceBusNamespaces) {
        $roleAssignments = Get-AzRoleAssignment -ObjectId $principalId -Scope $namespace.Id -RoleDefinitionName "Azure Service Bus Data Owner" -ErrorAction SilentlyContinue
        if ($roleAssignments) {
            $validationResults += "✅ Azure Service Bus Data Owner role assigned to $($namespace.Name)"
            Write-Host "✅ Azure Service Bus Data Owner role assigned to $($namespace.Name)" -ForegroundColor Green
        }
        else {
            $validationResults += "❌ Azure Service Bus Data Owner role NOT assigned to $($namespace.Name)"
            Write-Host "❌ Azure Service Bus Data Owner role NOT assigned to $($namespace.Name)" -ForegroundColor Red
        }
    }
}
catch {
    Write-Warning "⚠️ Failed to validate Service Bus RBAC: $($_.Exception.Message)"
}

# Cognitive Services OpenAI User role
try {
    $openAIAccounts = Get-AzCognitiveServicesAccount -ResourceGroupName $ResourceGroupName | Where-Object { $_.Kind -eq "OpenAI" }
    foreach ($account in $openAIAccounts) {
        $roleAssignments = Get-AzRoleAssignment -ObjectId $principalId -Scope $account.Id -RoleDefinitionName "Cognitive Services OpenAI User" -ErrorAction SilentlyContinue
        if ($roleAssignments) {
            $validationResults += "✅ Cognitive Services OpenAI User role assigned to $($account.Name)"
            Write-Host "✅ Cognitive Services OpenAI User role assigned to $($account.Name)" -ForegroundColor Green
        }
        else {
            $validationResults += "❌ Cognitive Services OpenAI User role NOT assigned to $($account.Name)"
            Write-Host "❌ Cognitive Services OpenAI User role NOT assigned to $($account.Name)" -ForegroundColor Red
        }
    }
}
catch {
    Write-Warning "⚠️ Failed to validate Azure OpenAI RBAC: $($_.Exception.Message)"
}

# Validate Function App configuration
Write-Host "⚙️ Validating Function App configuration..." -ForegroundColor Yellow
try {
    $appSettings = Get-AzWebAppSetting -ResourceGroupName $ResourceGroupName -Name $FunctionAppName
    
    $requiredSettings = @(
        "KeyVaultUrl",
        "StorageAccountName", 
        "ServiceBusNamespace",
        "AzureOpenAIEndpoint",
        "DataverseBaseUrl"
    )
    
    foreach ($setting in $requiredSettings) {
        if ($appSettings.Properties.ContainsKey($setting) -and -not [string]::IsNullOrEmpty($appSettings.Properties[$setting])) {
            Write-Host "✅ App setting '$setting' is configured" -ForegroundColor Green
        }
        else {
            Write-Host "❌ App setting '$setting' is missing or empty" -ForegroundColor Red
        }
    }
}
catch {
    Write-Warning "⚠️ Failed to validate Function App settings: $($_.Exception.Message)"
}

# Test Function App endpoints
Write-Host "🌐 Testing Function App endpoints..." -ForegroundColor Yellow
try {
    $functionAppUrl = "https://$($functionApp.DefaultHostName)"
    Write-Host "Function App URL: $functionAppUrl" -ForegroundColor Cyan
    
    # Test if the function app is responding (this will likely return 401/403 without proper authentication, which is expected)
    try {
        $response = Invoke-WebRequest -Uri $functionAppUrl -Method GET -ErrorAction SilentlyContinue
        Write-Host "✅ Function App is responding (Status: $($response.StatusCode))" -ForegroundColor Green
    }
    catch {
        if ($_.Exception.Response.StatusCode -eq 401 -or $_.Exception.Response.StatusCode -eq 403) {
            Write-Host "✅ Function App is responding (authentication required, which is expected)" -ForegroundColor Green
        }
        else {
            Write-Host "⚠️ Function App response: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}
catch {
    Write-Warning "⚠️ Failed to test Function App endpoints: $($_.Exception.Message)"
}

# Summary
Write-Host "`n📋 Validation Summary:" -ForegroundColor Cyan
foreach ($result in $validationResults) {
    Write-Host $result
}

$successCount = ($validationResults | Where-Object { $_ -match "✅" }).Count
$failureCount = ($validationResults | Where-Object { $_ -match "❌" }).Count

Write-Host "`n📊 Results: $successCount successful, $failureCount failed" -ForegroundColor Cyan

if ($failureCount -gt 0) {
    Write-Host "`n⚠️ Some validations failed. Please review the RBAC assignments and configuration." -ForegroundColor Yellow
    Write-Host "You may need to wait a few minutes for RBAC assignments to propagate." -ForegroundColor Yellow
    exit 1
}
else {
    Write-Host "`n🎉 All validations passed! Your deployment is properly configured." -ForegroundColor Green
    exit 0
}