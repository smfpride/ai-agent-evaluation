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

.PARAMETER AppName
    Application name prefix for resources. Default is 'ai-agent-eval'.

.PARAMETER DataverseBaseUrl
    Dataverse base URL (e.g., https://orgname.crm.dynamics.com/)

.PARAMETER CopilotStudioDirectLineSecret
    Copilot Studio Direct Line API secret

.PARAMETER SkipSecretUpdate
    Skip updating Key Vault secrets (useful for testing infrastructure only)

.EXAMPLE
    .\deploy.ps1 -SubscriptionId "12345678-1234-1234-1234-123456789012" -ResourceGroupName "rg-ai-agent-eval" -DataverseBaseUrl "https://orgname.crm.dynamics.com/" -CopilotStudioDirectLineSecret "your-secret-here"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory = $false)]
    [string]$Location = "eastus",
    
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentName = "dev",
    
    [Parameter(Mandatory = $false)]
    [string]$AppName = "ai-agent-eval",
    
    [Parameter(Mandatory = $false)]
    [string]$DataverseBaseUrl,
    
    [Parameter(Mandatory = $false)]
    [string]$CopilotStudioDirectLineSecret,
    
    [Parameter(Mandatory = $false)]
    [switch]$SkipSecretUpdate
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Import required modules
Import-Module Az.Accounts -Force
Import-Module Az.Resources -Force
Import-Module Az.KeyVault -Force
Import-Module Az.Storage -Force

Write-Host "🚀 Starting AI Agent Evaluation Function deployment..." -ForegroundColor Green

# Authenticate to Azure
Write-Host "� Authenticating to Azure..." -ForegroundColor Yellow
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

# Create resource group if it doesn't exist
Write-Host "� Checking resource group..." -ForegroundColor Yellow
try {
    $resourceGroup = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
    if (-not $resourceGroup) {
        Write-Host "Creating resource group: $ResourceGroupName" -ForegroundColor Yellow
        New-AzResourceGroup -Name $ResourceGroupName -Location $Location
        Write-Host "✅ Resource group created successfully" -ForegroundColor Green
    }
    else {
        Write-Host "✅ Resource group already exists" -ForegroundColor Green
    }
}
catch {
    Write-Error "❌ Failed to create/verify resource group: $($_.Exception.Message)"
    exit 1
}

# Deploy infrastructure
Write-Host "🏗️ Deploying infrastructure..." -ForegroundColor Yellow
try {
    $deploymentName = "ai-agent-eval-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    
    $deploymentParams = @{
        location = $Location
        environmentName = $EnvironmentName
        appName = $AppName
    }
    
    $deployment = New-AzResourceGroupDeployment `
        -ResourceGroupName $ResourceGroupName `
        -Name $deploymentName `
        -TemplateFile ".\main.bicep" `
        -TemplateParameterObject $deploymentParams `
        -Verbose
    
    if ($deployment.ProvisioningState -eq "Succeeded") {
        Write-Host "✅ Infrastructure deployed successfully" -ForegroundColor Green
        
        # Extract outputs
        $functionAppName = $deployment.Outputs.functionAppName.Value
        $keyVaultName = $deployment.Outputs.keyVaultName.Value
        $storageAccountName = $deployment.Outputs.storageAccountName.Value
        $serviceBusNamespaceName = $deployment.Outputs.serviceBusNamespaceName.Value
        $azureOpenAIAccountName = $deployment.Outputs.azureOpenAIAccountName.Value
        
        Write-Host "📋 Deployment Outputs:" -ForegroundColor Cyan
        Write-Host "  Function App: $functionAppName" -ForegroundColor White
        Write-Host "  Key Vault: $keyVaultName" -ForegroundColor White
        Write-Host "  Storage Account: $storageAccountName" -ForegroundColor White
        Write-Host "  Service Bus: $serviceBusNamespaceName" -ForegroundColor White
        Write-Host "  Azure OpenAI: $azureOpenAIAccountName" -ForegroundColor White
    }
    else {
        Write-Error "❌ Infrastructure deployment failed with state: $($deployment.ProvisioningState)"
        exit 1
    }
}
catch {
    Write-Error "❌ Failed to deploy infrastructure: $($_.Exception.Message)"
    exit 1
}

# Update Key Vault secrets if provided
if (-not $SkipSecretUpdate) {
    Write-Host "� Updating Key Vault secrets..." -ForegroundColor Yellow
    
    try {
        # Wait a moment for RBAC to propagate
        Start-Sleep -Seconds 30
        
        if ($DataverseBaseUrl) {
            Write-Host "Updating Dataverse Base URL..." -ForegroundColor Yellow
            Set-AzKeyVaultSecret -VaultName $keyVaultName -Name "DataverseBaseUrl" -SecretValue (ConvertTo-SecureString $DataverseBaseUrl -AsPlainText -Force)
            Write-Host "✅ Dataverse Base URL updated" -ForegroundColor Green
        }
        else {
            Write-Warning "⚠️ DataverseBaseUrl not provided - please update manually in Key Vault"
        }
        
        if ($CopilotStudioDirectLineSecret) {
            Write-Host "Updating Copilot Studio Direct Line Secret..." -ForegroundColor Yellow
            Set-AzKeyVaultSecret -VaultName $keyVaultName -Name "CopilotStudioDirectLineSecret" -SecretValue (ConvertTo-SecureString $CopilotStudioDirectLineSecret -AsPlainText -Force)
            Write-Host "✅ Copilot Studio Direct Line Secret updated" -ForegroundColor Green
        }
        else {
            Write-Warning "⚠️ CopilotStudioDirectLineSecret not provided - please update manually in Key Vault"
        }
    }
    catch {
        Write-Warning "⚠️ Failed to update some Key Vault secrets: $($_.Exception.Message)"
        Write-Warning "Please update the secrets manually in the Azure portal"
    }
}

# Upload evaluation prompt templates
Write-Host "� Uploading evaluation prompt templates..." -ForegroundColor Yellow
try {
    # Create prompt templates locally if they don't exist
    $promptsDir = ".\prompts"
    if (-not (Test-Path $promptsDir)) {
        New-Item -ItemType Directory -Path $promptsDir -Force
    }
    
    # Create sample prompt templates
    $similarityPrompt = @"
You are an AI evaluation judge. Compare the expected response with the actual response from an AI agent and rate their similarity.

Expected Response: {expectedResponse}
Actual Response: {actualResponse}

Rate the similarity on a scale of 0-10 where:
- 0: Completely different responses
- 5: Partially similar responses
- 10: Essentially identical responses

Consider:
- Semantic meaning and intent
- Key information coverage
- Overall message alignment

Provide only a numeric score (0-10) as your response.
"@

    $accuracyPrompt = @"
You are an AI evaluation judge. Assess how accurately the AI agent's response answers the given question.

Question: {question}
AI Agent Response: {actualResponse}
Reference Answer (if available): {expectedResponse}

Rate the accuracy on a scale of 0-10 where:
- 0: Completely inaccurate or wrong information
- 5: Partially accurate with some errors
- 10: Completely accurate and correct

Consider:
- Factual correctness
- Completeness of the answer
- Relevance to the question

Provide only a numeric score (0-10) as your response.
"@

    $relevancyPrompt = @"
You are an AI evaluation judge. Evaluate how relevant the AI agent's response is to the given question or context.

Question/Context: {question}
AI Agent Response: {actualResponse}

Rate the relevancy on a scale of 0-10 where:
- 0: Completely irrelevant response
- 5: Somewhat relevant but missing key points
- 10: Highly relevant and directly addresses the question

Consider:
- Direct relevance to the question
- Appropriateness of the response
- Context awareness

Provide only a numeric score (0-10) as your response.
"@

    # Write prompt files
    Set-Content -Path "$promptsDir\similarity.txt" -Value $similarityPrompt -Encoding UTF8
    Set-Content -Path "$promptsDir\accuracy.txt" -Value $accuracyPrompt -Encoding UTF8
    Set-Content -Path "$promptsDir\relevancy.txt" -Value $relevancyPrompt -Encoding UTF8
    
    # Upload to blob storage
    $storageAccount = Get-AzStorageAccount -ResourceGroupName $ResourceGroupName -Name $storageAccountName
    $storageContext = $storageAccount.Context
    
    Set-AzStorageBlobContent -File "$promptsDir\similarity.txt" -Container "evaluation-prompts" -Blob "similarity.txt" -Context $storageContext -Force
    Set-AzStorageBlobContent -File "$promptsDir\accuracy.txt" -Container "evaluation-prompts" -Blob "accuracy.txt" -Context $storageContext -Force
    Set-AzStorageBlobContent -File "$promptsDir\relevancy.txt" -Container "evaluation-prompts" -Blob "relevancy.txt" -Context $storageContext -Force
    
    Write-Host "✅ Evaluation prompt templates uploaded successfully" -ForegroundColor Green
}
catch {
    Write-Warning "⚠️ Failed to upload prompt templates: $($_.Exception.Message)"
    Write-Warning "You can upload them manually later"
}

# Wait for RBAC propagation before validation
Write-Host "⏳ Waiting for RBAC propagation (60 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 60

# Run post-deployment validation
Write-Host "`n🔍 Running post-deployment validation..." -ForegroundColor Yellow
try {
    $validationScriptPath = ".\validate-deployment.ps1"
    if (Test-Path $validationScriptPath) {
        Write-Host "Executing validation script..." -ForegroundColor Yellow
        & $validationScriptPath -SubscriptionId $SubscriptionId -ResourceGroupName $ResourceGroupName -FunctionAppName $functionAppName
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Post-deployment validation passed!" -ForegroundColor Green
        }
        else {
            Write-Warning "⚠️ Post-deployment validation found issues. Please review the output above."
        }
    }
    else {
        Write-Warning "⚠️ Validation script not found. Skipping post-deployment validation."
    }
}
catch {
    Write-Warning "⚠️ Failed to run post-deployment validation: $($_.Exception.Message)"
}

# Verify Function App runtime status
Write-Host "`n🏃‍♂️ Checking Function App runtime status..." -ForegroundColor Yellow
try {
    $functionApp = Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $functionAppName
    if ($functionApp.State -eq "Running") {
        Write-Host "✅ Function App is running" -ForegroundColor Green
    }
    else {
        Write-Warning "⚠️ Function App state: $($functionApp.State)"
    }
    
    # Check if managed identity is properly configured
    if ($functionApp.Identity.Type -eq "SystemAssigned") {
        Write-Host "✅ System-assigned managed identity is configured" -ForegroundColor Green
        Write-Host "   Principal ID: $($functionApp.Identity.PrincipalId)" -ForegroundColor Cyan
    }
    else {
        Write-Warning "⚠️ System-assigned managed identity is not configured"
    }
}
catch {
    Write-Warning "⚠️ Failed to check Function App status: $($_.Exception.Message)"
}

# Test Key Vault connectivity
Write-Host "`n🔑 Testing Key Vault connectivity..." -ForegroundColor Yellow
try {
    $keyVault = Get-AzKeyVault -ResourceGroupName $ResourceGroupName -VaultName $keyVaultName
    if ($keyVault) {
        Write-Host "✅ Key Vault is accessible" -ForegroundColor Green
        
        # List secrets (just to verify permissions)
        $secrets = Get-AzKeyVaultSecret -VaultName $keyVaultName -ErrorAction SilentlyContinue
        if ($secrets) {
            Write-Host "✅ Key Vault secrets are accessible ($($secrets.Count) secrets found)" -ForegroundColor Green
        }
        else {
            Write-Host "📝 No secrets found in Key Vault (this is normal for new deployments)" -ForegroundColor Cyan
        }
    }
}
catch {
    Write-Warning "⚠️ Failed to test Key Vault connectivity: $($_.Exception.Message)"
}

# Display connection information
Write-Host "`n🎉 Deployment completed successfully!" -ForegroundColor Green
Write-Host "`n📋 Connection Information:" -ForegroundColor Cyan
Write-Host "Resource Group: $ResourceGroupName" -ForegroundColor White
Write-Host "Function App: $functionAppName" -ForegroundColor White
Write-Host "Function App URL: https://$functionAppName.azurewebsites.net" -ForegroundColor White
Write-Host "Key Vault: $keyVaultName" -ForegroundColor White
Write-Host "Storage Account: $storageAccountName" -ForegroundColor White
Write-Host "Service Bus: $serviceBusNamespaceName" -ForegroundColor White
Write-Host "Azure OpenAI: $azureOpenAIAccountName" -ForegroundColor White

Write-Host "`n📝 Next Steps:" -ForegroundColor Cyan
Write-Host "1. Update the following Key Vault secrets if not provided:" -ForegroundColor Yellow
if (-not $DataverseBaseUrl) {
    Write-Host "   - DataverseBaseUrl: Your Dataverse base URL" -ForegroundColor Yellow
}
if (-not $CopilotStudioDirectLineSecret) {
    Write-Host "   - CopilotStudioDirectLineSecret: Your Copilot Studio Direct Line secret" -ForegroundColor Yellow
}
Write-Host "2. Deploy your Function App code using 'func azure functionapp publish $functionAppName'" -ForegroundColor Yellow
Write-Host "3. Run integration tests to verify managed identity authentication" -ForegroundColor Yellow
Write-Host "4. Configure your evaluation test cases and trigger workflows" -ForegroundColor Yellow

Write-Host "`n🔗 Useful Links:" -ForegroundColor Cyan
Write-Host "Function App Portal: https://portal.azure.com/#@/resource/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$functionAppName" -ForegroundColor Blue
Write-Host "Key Vault Portal: https://portal.azure.com/#@/resource/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.KeyVault/vaults/$keyVaultName" -ForegroundColor Blue
Write-Host "Application Insights: https://portal.azure.com/#@/resource/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.Insights/components/$functionAppName-ai" -ForegroundColor Blue

Write-Host "`n🚀 Deployment Enhancement Tips:" -ForegroundColor Magenta
Write-Host "- Run '.\validate-deployment.ps1' anytime to verify your configuration" -ForegroundColor White
Write-Host "- Monitor Application Insights for runtime issues and performance" -ForegroundColor White
Write-Host "- Use Azure Monitor alerts for proactive monitoring" -ForegroundColor White
Write-Host "- Review managed identity assignments if authentication fails" -ForegroundColor White

Write-Host "`n✨ Deployment script completed!" -ForegroundColor Green