resource "azurerm_search_service" "main" {
  name                = "srch-${var.project}-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = var.search_sku

  tags = var.tags
}
