variable "project" {
  description = "Project name used in resource naming"
  type        = string
  default     = "ragnavigator"
}

variable "environment" {
  description = "Deployment environment (dev, staging, prod)"
  type        = string
  default     = "dev"

  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Environment must be dev, staging, or prod."
  }
}

variable "location" {
  description = "Azure region for all resources"
  type        = string
  default     = "swedencentral"
}

# --- OpenAI ---

variable "chat_model_name" {
  description = "Azure OpenAI chat model deployment name"
  type        = string
  default     = "gpt-4o"
}

variable "chat_model_version" {
  description = "Azure OpenAI chat model version"
  type        = string
  default     = "2024-11-20"
}

variable "embedding_model_name" {
  description = "Azure OpenAI embedding model deployment name"
  type        = string
  default     = "text-embedding-3-small"
}

variable "embedding_model_version" {
  description = "Azure OpenAI embedding model version"
  type        = string
  default     = "1"
}

# --- AI Search ---

variable "search_sku" {
  description = "SKU for Azure AI Search (free, basic, standard, etc.)"
  type        = string
  default     = "basic"
}

variable "search_index_name" {
  description = "Name of the search index"
  type        = string
  default     = "rag-navigator-index"
}

# --- App Service ---

variable "app_service_sku" {
  description = "App Service plan SKU (B1, S1, P1v3, etc.)"
  type        = string
  default     = "B1"
}
