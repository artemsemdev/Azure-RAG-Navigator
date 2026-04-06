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

variable "search_sku" {
  type    = string
  default = "basic"
}

variable "index_name" {
  description = "Search index name (for reference; index is created by the application)"
  type        = string
}

variable "tags" {
  type    = map(string)
  default = {}
}
