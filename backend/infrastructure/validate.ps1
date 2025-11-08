# Bicep Template Validation Script
# Usage: .\validate.ps1 [environment]

param(
    [string]$Environment = "dev"
)

function Write-ColorOutput($Message, $Color = "White") {
    Write-Host $Message -ForegroundColor $Color
}

Write-ColorOutput "🔍 Validating Bicep Template for AI Agent Evaluation Function" "Blue"
Write-ColorOutput "=====================================================" "Blue"
Write-Host ""

# Check if Bicep is installed
Write-ColorOutput "Checking Bicep installation..." "Yellow"
try {
    $bicepVersion = az bicep version --output tsv 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-ColorOutput "✅ Bicep is installed: $bicepVersion" "Green"
    } else {
        Write-ColorOutput "Installing Bicep..." "Yellow"
        az bicep install
        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "✅ Bicep installed successfully" "Green"
        } else {
            Write-ColorOutput "❌ Failed to install Bicep" "Red"
            exit 1
        }
    }
} catch {
    Write-ColorOutput "❌ Azure CLI not found. Please install Azure CLI first." "Red"
    exit 1
}

Write-Host ""

# Check if parameter file exists
$parameterFile = "main.parameters.$Environment.json"
if (-not (Test-Path $parameterFile)) {
    Write-ColorOutput "❌ Parameter file '$parameterFile' not found" "Red"
    exit 1
}
Write-ColorOutput "✅ Parameter file '$parameterFile' found" "Green"

# Validate Bicep syntax
Write-ColorOutput "🔍 Validating Bicep syntax..." "Yellow"
az bicep build --file "main.bicep" --stdout > $null 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-ColorOutput "✅ Bicep syntax validation passed" "Green"
} else {
    Write-ColorOutput "❌ Bicep syntax validation failed" "Red"
    az bicep build --file "main.bicep"
    exit 1
}

# Create a temporary resource group for validation
$tempRgName = "bicep-validation-temp-rg"
$location = "East US"

Write-ColorOutput "🔍 Creating temporary resource group for validation..." "Yellow"
az group create --name $tempRgName --location $location > $null 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-ColorOutput "✅ Temporary resource group created" "Green"
} else {
    Write-ColorOutput "❌ Failed to create temporary resource group" "Red"
    exit 1
}

try {
    # Validate deployment
    Write-ColorOutput "🔍 Validating deployment template..." "Yellow"
    $validationResult = az deployment group validate `
        --resource-group $tempRgName `
        --template-file "main.bicep" `
        --parameters "@$parameterFile" `
        --query "error" `
        --output tsv

    if ($validationResult -eq "null" -or $LASTEXITCODE -eq 0) {
        Write-ColorOutput "✅ Deployment validation passed" "Green"
        
        # Show what-if results
        Write-ColorOutput "🔍 Generating deployment preview..." "Yellow"
        az deployment group what-if `
            --resource-group $tempRgName `
            --template-file "main.bicep" `
            --parameters "@$parameterFile"
            
        Write-Host ""
        Write-ColorOutput "✅ All validations passed! Template is ready for deployment." "Green"
    } else {
        Write-ColorOutput "❌ Deployment validation failed" "Red"
        az deployment group validate `
            --resource-group $tempRgName `
            --template-file "main.bicep" `
            --parameters "@$parameterFile"
        exit 1
    }
} finally {
    # Clean up temporary resource group
    Write-ColorOutput "🧹 Cleaning up temporary resource group..." "Yellow"
    az group delete --name $tempRgName --yes --no-wait > $null 2>&1
    Write-ColorOutput "✅ Cleanup initiated" "Green"
}

Write-Host ""
Write-ColorOutput "🚀 Ready to deploy! Use .\deploy.ps1 to proceed with deployment." "Blue"