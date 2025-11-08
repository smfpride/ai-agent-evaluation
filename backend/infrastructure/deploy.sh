#!/bin/bash

# Azure Infrastructure Deployment Script for AI Agent Evaluation Function
# Usage: ./deploy.sh [environment] [resource-group] [subscription-id]

set -e

# Default values
ENVIRONMENT=${1:-dev}
RESOURCE_GROUP=${2:-ai-agent-eval-$ENVIRONMENT-rg}
SUBSCRIPTION_ID=${3}
LOCATION="East US"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}🚀 AI Agent Evaluation Function - Infrastructure Deployment${NC}"
echo -e "${BLUE}=================================================${NC}"
echo ""
echo -e "Environment: ${GREEN}$ENVIRONMENT${NC}"
echo -e "Resource Group: ${GREEN}$RESOURCE_GROUP${NC}"
echo -e "Location: ${GREEN}$LOCATION${NC}"
echo ""

# Set subscription if provided
if [ ! -z "$SUBSCRIPTION_ID" ]; then
    echo -e "${YELLOW}📋 Setting Azure subscription...${NC}"
    az account set --subscription "$SUBSCRIPTION_ID"
    echo -e "${GREEN}✅ Subscription set to: $SUBSCRIPTION_ID${NC}"
    echo ""
fi

# Show current context
echo -e "${YELLOW}📋 Current Azure context:${NC}"
az account show --query "{subscriptionId: id, subscriptionName: name, tenantId: tenantId}" -o table
echo ""

# Check if resource group exists
echo -e "${YELLOW}🔍 Checking if resource group exists...${NC}"
if az group show --name "$RESOURCE_GROUP" &> /dev/null; then
    echo -e "${GREEN}✅ Resource group '$RESOURCE_GROUP' already exists${NC}"
else
    echo -e "${YELLOW}📦 Creating resource group '$RESOURCE_GROUP'...${NC}"
    az group create --name "$RESOURCE_GROUP" --location "$LOCATION"
    echo -e "${GREEN}✅ Resource group created successfully${NC}"
fi
echo ""

# Validate Bicep template
echo -e "${YELLOW}🔍 Validating Bicep template...${NC}"
if az deployment group validate \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "main.bicep" \
    --parameters "@main.parameters.$ENVIRONMENT.json" \
    --query "error" \
    --output tsv | grep -q "null"; then
    echo -e "${GREEN}✅ Bicep template validation passed${NC}"
else
    echo -e "${RED}❌ Bicep template validation failed${NC}"
    az deployment group validate \
        --resource-group "$RESOURCE_GROUP" \
        --template-file "main.bicep" \
        --parameters "@main.parameters.$ENVIRONMENT.json"
    exit 1
fi
echo ""

# Deploy infrastructure
echo -e "${YELLOW}🚀 Deploying infrastructure...${NC}"
echo -e "${YELLOW}This may take 10-15 minutes...${NC}"
echo ""

DEPLOYMENT_NAME="ai-agent-eval-$ENVIRONMENT-$(date +%Y%m%d-%H%M%S)"

az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "main.bicep" \
    --parameters "@main.parameters.$ENVIRONMENT.json" \
    --name "$DEPLOYMENT_NAME" \
    --verbose

if [ $? -eq 0 ]; then
    echo ""
    echo -e "${GREEN}✅ Infrastructure deployment completed successfully!${NC}"
    echo ""
    
    # Get deployment outputs
    echo -e "${YELLOW}📋 Deployment outputs:${NC}"
    az deployment group show \
        --resource-group "$RESOURCE_GROUP" \
        --name "$DEPLOYMENT_NAME" \
        --query "properties.outputs" \
        --output table
    
    echo ""
    echo -e "${YELLOW}🔐 Next steps:${NC}"
    echo -e "${BLUE}1.${NC} Update Key Vault secrets with actual values:"
    echo -e "   - DataverseConnectionString"
    echo -e "   - CopilotStudioDirectLineSecret"
    echo ""
    echo -e "${BLUE}2.${NC} Deploy the Function App code using:"
    echo -e "   ${GREEN}func azure functionapp publish [function-app-name]${NC}"
    echo ""
    echo -e "${BLUE}3.${NC} Test the deployment using the health check endpoint:"
    echo -e "   ${GREEN}curl https://[function-app-name].azurewebsites.net/api/health${NC}"
    echo ""
    
else
    echo ""
    echo -e "${RED}❌ Infrastructure deployment failed!${NC}"
    echo -e "${YELLOW}Check the deployment logs above for details.${NC}"
    exit 1
fi