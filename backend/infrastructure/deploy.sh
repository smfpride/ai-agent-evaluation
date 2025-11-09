#!/bin/bash

# Azure Infrastructure Deployment Script
# Usage: ./deploy.sh [subscription-id] [resource-group] [location] [environment]

set -e

SUBSCRIPTION_ID=${1}
RESOURCE_GROUP=${2:-rg-agent-eval-staging}
LOCATION=${3:-eastus}
ENVIRONMENT=${4:-staging}

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${GREEN}🚀 Starting deployment...${NC}"

# Set subscription
if [ ! -z "$SUBSCRIPTION_ID" ]; then
    echo -e "${YELLOW}Authenticating...${NC}"
    az account set --subscription "$SUBSCRIPTION_ID"
    echo -e "${GREEN}✅ Authenticated${NC}"
fi

# Create resource group
echo -e "${YELLOW}� Resource group...${NC}"
if az group show --name "$RESOURCE_GROUP" &> /dev/null; then
    echo -e "${GREEN}✅ Exists${NC}"
else
    az group create --name "$RESOURCE_GROUP" --location "$LOCATION" > /dev/null
    echo -e "${GREEN}✅ Created${NC}"
fi

# Deploy infrastructure
echo -e "${YELLOW}🏗️ Deploying...${NC}"
DEPLOYMENT_NAME="deploy-$(date +%Y%m%d%H%M%S)"

OUTPUTS=$(az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "main.bicep" \
    --parameters location="$LOCATION" environmentName="$ENVIRONMENT" \
    --name "$DEPLOYMENT_NAME" \
    --query "properties.outputs" \
    --output json)

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✅ Deployed${NC}"
    
    FUNCTION_APP=$(echo $OUTPUTS | jq -r '.functionAppName.value')
    KEY_VAULT=$(echo $OUTPUTS | jq -r '.keyVaultName.value')
    STORAGE=$(echo $OUTPUTS | jq -r '.storageAccountName.value')
    SERVICE_BUS=$(echo $OUTPUTS | jq -r '.serviceBusNamespaceName.value')
    OPENAI=$(echo $OUTPUTS | jq -r '.azureOpenAIAccountName.value')
    
    echo ""
    echo -e "${YELLOW}� Resources:${NC}"
    echo "  Function:  $FUNCTION_APP"
    echo "  Key Vault: $KEY_VAULT"
    echo "  Storage:   $STORAGE"
    echo "  Bus:       $SERVICE_BUS"
    echo "  OpenAI:    $OPENAI"
    
    echo ""
    echo -e "${GREEN}✅ Deployment completed successfully!${NC}"
    echo ""
    echo "Deployed resources:"
    echo "  Function App: $FUNCTION_APP"
    echo "  Key Vault: $KEY_VAULT"
    echo "  Storage Account: $STORAGE"
    echo "  Service Bus: $SERVICE_BUS"
    echo "  Azure OpenAI: $OPENAI"
    echo ""
    echo "Next steps:"
    echo "  1. Deploy function code: func azure functionapp publish $FUNCTION_APP"
    echo "  2. Update DataverseConnectionString in Key Vault if needed"
else
    echo -e "${RED}❌ Deployment failed${NC}"
    exit 1
fi