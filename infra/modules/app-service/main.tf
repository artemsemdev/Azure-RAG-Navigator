resource "azurerm_service_plan" "main" {
  name                = "plan-${var.project}-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  os_type             = "Linux"
  sku_name            = var.sku_name

  tags = var.tags
}

resource "azurerm_linux_web_app" "main" {
  name                = "app-${var.project}-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  service_plan_id     = azurerm_service_plan.main.id

  identity {
    type = "SystemAssigned"
  }

  site_config {
    application_stack {
      dotnet_version = var.dotnet_version
    }

    always_on = var.sku_name != "F1"
  }

  app_settings = var.app_settings

  tags = var.tags
}
