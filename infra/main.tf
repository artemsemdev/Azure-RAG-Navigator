terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  backend "azurerm" {
    # Configure via backend config file or CLI args:
    #   terraform init -backend-config=backend.tfvars
    #
    # resource_group_name  = "tfstate-rg"
    # storage_account_name = "tfstateragnavigator"
    # container_name       = "tfstate"
    # key                  = "ragnavigator.tfstate"
  }
}

provider "azurerm" {
  features {}
}

# ---------- Resource Group ----------

resource "azurerm_resource_group" "main" {
  name     = "rg-${var.project}-${var.environment}"
  location = var.location
  tags     = local.tags
}

# ---------- Modules ----------

module "openai" {
  source = "./modules/openai"

  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project             = var.project
  environment         = var.environment
  chat_model_name     = var.chat_model_name
  chat_model_version  = var.chat_model_version
  embedding_model_name    = var.embedding_model_name
  embedding_model_version = var.embedding_model_version
  tags                = local.tags
}

module "search" {
  source = "./modules/search"

  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project             = var.project
  environment         = var.environment
  search_sku          = var.search_sku
  index_name          = var.search_index_name
  tags                = local.tags
}

module "app_service" {
  source = "./modules/app-service"

  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  project             = var.project
  environment         = var.environment
  sku_name            = var.app_service_sku
  dotnet_version      = "9.0"
  tags                = local.tags

  app_settings = {
    AZURE_OPENAI_ENDPOINT              = module.openai.endpoint
    AZURE_OPENAI_CHAT_DEPLOYMENT       = module.openai.chat_deployment_name
    AZURE_OPENAI_EMBEDDING_DEPLOYMENT  = module.openai.embedding_deployment_name
    AZURE_SEARCH_ENDPOINT              = module.search.endpoint
    AZURE_SEARCH_INDEX_NAME            = var.search_index_name
  }
}

# ---------- Locals ----------

locals {
  tags = {
    project     = var.project
    environment = var.environment
    managed_by  = "terraform"
  }
}
