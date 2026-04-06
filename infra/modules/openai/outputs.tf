output "endpoint" {
  value = azurerm_cognitive_account.openai.endpoint
}

output "account_id" {
  value = azurerm_cognitive_account.openai.id
}

output "chat_deployment_name" {
  value = azurerm_cognitive_deployment.chat.name
}

output "embedding_deployment_name" {
  value = azurerm_cognitive_deployment.embedding.name
}
