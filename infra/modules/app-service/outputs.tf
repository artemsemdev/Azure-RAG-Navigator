output "default_hostname" {
  value = "https://${azurerm_linux_web_app.main.default_hostname}"
}

output "app_id" {
  value = azurerm_linux_web_app.main.id
}

output "principal_id" {
  description = "System-assigned managed identity principal ID (use for RBAC assignments)"
  value       = azurerm_linux_web_app.main.identity[0].principal_id
}
