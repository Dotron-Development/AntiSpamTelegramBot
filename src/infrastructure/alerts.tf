resource "azurerm_monitor_action_group" "function_failures" {
  name                = "ag-${local.appName}-failures-${var.environment_prefix}"
  resource_group_name = azurerm_resource_group.rg.name
  short_name          = "fn-failures"

  email_receiver {
    name                    = "function-failures-email"
    email_address           = var.function_alert_email
    use_common_alert_schema = true
  }

  tags = local.tags
}

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "function_failures" {
  name                = "alert-${local.appName}-failures-${var.environment_prefix}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = var.location
  description         = "Notify when Application Insights records a failed function execution."
  display_name        = "Function failures (${var.environment_prefix})"

  scopes                  = [azurerm_application_insights.appinsights.id]
  severity                = 2
  evaluation_frequency    = "PT1M"
  window_duration         = "PT5M"
  enabled                 = true
  auto_mitigation_enabled = true

  criteria {
    query                   = <<-QUERY
      requests
      | where success == false
    QUERY
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.function_failures.id]
  }

  tags = local.tags
}
