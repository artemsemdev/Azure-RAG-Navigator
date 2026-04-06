output "resource_group_name" {
  value = azurerm_resource_group.main.name
}

output "openai_endpoint" {
  value = module.openai.endpoint
}

output "search_endpoint" {
  value = module.search.endpoint
}

output "app_service_url" {
  value = module.app_service.default_hostname
}
