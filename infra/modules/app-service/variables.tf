variable "resource_group_name" {
  type = string
}

variable "location" {
  type = string
}

variable "project" {
  type = string
}

variable "environment" {
  type = string
}

variable "sku_name" {
  type    = string
  default = "B1"
}

variable "dotnet_version" {
  type    = string
  default = "9.0"
}

variable "app_settings" {
  description = "Application settings (environment variables) for the web app"
  type        = map(string)
  default     = {}
}

variable "tags" {
  type    = map(string)
  default = {}
}
