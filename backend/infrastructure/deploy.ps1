#Requires -Version 7.0
#Requires -Module Az

<#
.SYNOPSIS
    Deploys the AI Agent Evaluation Function infrastructure to Azure using managed identity.

.DESCRIPTION
    This script deploys all required Azure resources for the AI Agent Evaluation Function,
    configures managed identity authentication, and sets up proper RBAC permissions.

.PARAMETER SubscriptionId
    Azure subscription ID where resources will be deployed.

.PARAMETER ResourceGroupName
    Name of the resource group. If it doesn't exist, it will be created.

.PARAMETER Location
    Azure region for resource deployment. Default is 'eastus'.

.PARAMETER EnvironmentName
    Environment name (dev, staging, prod). Default is 'dev'.

.PARAMETER DataverseConnectionString
    Optional: Dataverse connection string to store in Key Vault.
    Format: AuthenticationType=ClientSecret;Url=https://...;ClientId=...;ClientSecret=...

.EXAMPLE
    .\deploy.ps1 -SubscriptionId "xxx" -ResourceGroupName "rg-agent-eval-staging"
    
.EXAMPLE
    .\deploy.ps1 -SubscriptionId "xxx" -ResourceGroupName "rg-agent-eval-staging" -DataverseConnectionString "AuthenticationType=ClientSecret;..."
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory = $false)]
    [string]$Location = "eastus",
    
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentName = "staging",
    
    [Parameter(Mandatory = $false)]
    [string]$DataverseConnectionString
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 Starting deployment..." -ForegroundColor Green

# Authenticate to Azure
Write-Host "🔐 Authenticating..." -ForegroundColor Yellow
try {
    $context = Get-AzContext
    if (-not $context -or $context.Subscription.Id -ne $SubscriptionId) {
        Connect-AzAccount -SubscriptionId $SubscriptionId
    }
    Set-AzContext -SubscriptionId $SubscriptionId
    Write-Host "✅ Authenticated" -ForegroundColor Green
}
catch {
    Write-Error "❌ Authentication failed: $($_.Exception.Message)"
    exit 1
}

# Create resource group
Write-Host "📦 Resource group..." -ForegroundColor Yellow
try {
    $rg = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
    if (-not $rg) {
        New-AzResourceGroup -Name $ResourceGroupName -Location $Location | Out-Null
        Write-Host "✅ Created" -ForegroundColor Green
    }
    else {
        Write-Host "✅ Exists" -ForegroundColor Green
    }
}
catch {
    Write-Error "❌ Failed: $($_.Exception.Message)"
    exit 1
}

# Deploy infrastructure
Write-Host "🏗️ Deploying..." -ForegroundColor Yellow
try {
    $deploymentName = "deploy-$(Get-Date -Format 'yyyyMMddHHmmss')"
    
    $deployment = New-AzResourceGroupDeployment `
        -ResourceGroupName $ResourceGroupName `
        -Name $deploymentName `
        -TemplateFile ".\main.bicep" `
        -location $Location `
        -environmentName $EnvironmentName `
        -Verbose
    
    if ($deployment.ProvisioningState -eq "Succeeded") {
        Write-Host "✅ Deployed" -ForegroundColor Green
        
        $functionAppName = $deployment.Outputs.functionAppName.Value
        $keyVaultName = $deployment.Outputs.keyVaultName.Value
        $storageAccountName = $deployment.Outputs.storageAccountName.Value
        $serviceBusName = $deployment.Outputs.serviceBusNamespaceName.Value
        $openAIName = $deployment.Outputs.azureOpenAIAccountName.Value
        
        Write-Host "`n📋 Resources:" -ForegroundColor Cyan
        Write-Host "  Function:  $functionAppName" -ForegroundColor White
        Write-Host "  Key Vault: $keyVaultName" -ForegroundColor White
        Write-Host "  Storage:   $storageAccountName" -ForegroundColor White
        Write-Host "  Bus:       $serviceBusName" -ForegroundColor White
        Write-Host "  OpenAI:    $openAIName" -ForegroundColor White
    }
    else {
        Write-Error "❌ Failed: $($deployment.ProvisioningState)"
        exit 1
    }
}
catch {
    Write-Error "❌ Deployment failed: $($_.Exception.Message)"
    exit 1
}

# Update Key Vault secret if provided
if ($DataverseConnectionString) {
    Write-Host "Updating Key Vault secret..." -ForegroundColor Yellow
    
    try {
        Start-Sleep -Seconds 30  # Wait for RBAC propagation
        Set-AzKeyVaultSecret -VaultName $keyVaultName -Name "DataverseConnectionString" -SecretValue (ConvertTo-SecureString $DataverseConnectionString -AsPlainText -Force)
        Write-Host "✅ DataverseConnectionString updated" -ForegroundColor Green
    }
    catch {
        Write-Warning "Failed to update Key Vault secret: $($_.Exception.Message)"
        Write-Warning "Update manually in Azure portal if needed"
    }
}

Write-Host "`n✅ Deployment completed successfully!" -ForegroundColor Green
Write-Host "`nDeployed resources:" -ForegroundColor Cyan
Write-Host "  Function App: $functionAppName" -ForegroundColor White
Write-Host "  Key Vault: $keyVaultName" -ForegroundColor White
Write-Host "  Storage Account: $storageAccountName" -ForegroundColor White
Write-Host "  Service Bus: $serviceBusName" -ForegroundColor White
Write-Host "  Azure OpenAI: $openAIName" -ForegroundColor White

Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "  1. Deploy function code: func azure functionapp publish $functionAppName" -ForegroundColor Yellow
Write-Host "  2. Update DataverseConnectionString in Key Vault if not provided" -ForegroundColor Yellow