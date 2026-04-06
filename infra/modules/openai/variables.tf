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

variable "chat_model_name" {
  type = string
}

variable "chat_model_version" {
  type = string
}

variable "embedding_model_name" {
  type = string
}

variable "embedding_model_version" {
  type = string
}

variable "tags" {
  type    = map(string)
  default = {}
}
