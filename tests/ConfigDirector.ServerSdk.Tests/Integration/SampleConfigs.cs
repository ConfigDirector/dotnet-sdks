namespace ConfigDirector.Tests.Integration;

// The config state the stubbed server serves, in the wire format the server really sends. The keys
// are the ones every ConfigDirector sample application uses.
internal static class SampleConfigs
{
    internal const string Variations = """
        {"kind": "full", "timestamp": "2026-08-01T12:00:00.000Z", "configs": {
          "checkout-experiment": {"id": "11111111-1111-4111-8111-111111111111", "key": "checkout-experiment", "type": "string",
            "variations": [
              {"id": "variation-control", "value": "control", "name": "Control"},
              {"id": "variation-treatment", "value": "treatment", "name": "Treatment"}],
            "target": {"defaultValue": "control", "defaultValueId": "value-control", "defaultVariationId": "variation-control", "rules": [
              {"id": "rule-rollout", "type": "percentage", "order": 1, "percentages": [
                {"id": "share-control", "percentage": 50, "value": "control", "valueId": "value-control", "variationId": "variation-control"},
                {"id": "share-treatment", "percentage": 50, "value": "treatment", "valueId": "value-treatment", "variationId": "variation-treatment"}]}]}},
          "greeting-by-plan": {"id": "greeting-by-plan-id", "key": "greeting-by-plan", "type": "string",
            "variations": [
              {"id": "variation-hello", "value": "hello", "name": "Hello"},
              {"id": "variation-welcome", "value": "welcome", "name": "Welcome"}],
            "target": {"defaultValue": "hello", "defaultValueId": "value-hello", "defaultVariationId": "variation-hello", "rules": [
              {"id": "rule-pro", "type": "conditional", "order": 1, "value": "welcome", "valueId": "value-welcome", "variationId": "variation-welcome",
               "conditions": [{"id": "condition-pro", "attribute": "traits", "trait": "/plan", "operator": "=", "targetType": "text", "targetValues": ["pro"]}]}]}},
          "unnamed-experiment": {"id": "unnamed-experiment-id", "key": "unnamed-experiment", "type": "string",
            "variations": [{"id": "variation-unnamed", "value": "only", "name": null}],
            "target": {"defaultValue": "only", "defaultValueId": "value-only", "defaultVariationId": "variation-unnamed", "rules": []}},
          "rolled-out-flag": {"id": "rolled-out-flag-id", "key": "rolled-out-flag", "type": "boolean",
            "target": {"defaultValue": false, "defaultValueId": "value-off", "rules": [
              {"id": "rule-flag-rollout", "type": "percentage", "order": 1, "percentages": [
                {"id": "share-on", "percentage": 100, "value": true, "valueId": "value-on"}]}]}},
          "count-experiment": {"id": "count-experiment-id", "key": "count-experiment", "type": "integer",
            "variations": [{"id": "variation-count", "value": 3, "name": "Three"}],
            "target": {"defaultValue": 1, "defaultValueId": "value-1", "rules": [
              {"id": "rule-count-rollout", "type": "percentage", "order": 1, "percentages": [
                {"id": "share-count", "percentage": 100, "value": 3, "valueId": "value-3", "variationId": "variation-count"}]}]}},
          "whole-number": {"id": "whole-number-id", "key": "whole-number", "type": "integer",
            "target": {"defaultValue": 26, "defaultValueId": "value-26", "rules": []}},
          "ratio": {"id": "ratio-id", "key": "ratio", "type": "float",
            "target": {"defaultValue": 2.5, "defaultValueId": "value-2-5", "rules": []}},
          "settings": {"id": "settings-id", "key": "settings", "type": "json",
            "variations": [{"id": "variation-settings", "value": {"name": "checkout"}, "name": "Settings"}],
            "target": {"defaultValue": {"name": "checkout"}, "defaultValueId": "value-settings", "defaultVariationId": "variation-settings", "rules": []}}}}
        """;

    /// <summary>The checkout experiment with a rule after its rollout that serves pro plans "new copy".</summary>
    internal const string CheckoutExperimentWithARuleAfterItsRollout = """
        {"kind": "full", "timestamp": "2026-08-01T12:00:00.000Z", "configs": {
          "checkout-experiment": {"id": "11111111-1111-4111-8111-111111111111", "key": "checkout-experiment", "type": "string",
            "variations": [
              {"id": "variation-control", "value": "control", "name": "Control"},
              {"id": "variation-treatment", "value": "treatment", "name": "Treatment"},
              {"id": "variation-new-copy", "value": "new copy", "name": "New copy"}],
            "target": {"defaultValue": "control", "defaultValueId": "value-control", "defaultVariationId": "variation-control", "rules": [
              {"id": "rule-rollout", "type": "percentage", "order": 1, "percentages": [
                {"id": "share-treatment", "percentage": 100, "value": "treatment", "valueId": "value-treatment", "variationId": "variation-treatment"}]},
              {"id": "rule-pro", "type": "conditional", "order": 2, "value": "new copy", "valueId": "value-new-copy", "variationId": "variation-new-copy",
               "conditions": [{"id": "condition-pro", "attribute": "traits", "trait": "/plan", "operator": "=", "targetType": "text", "targetValues": ["pro"]}]}]}}}}
        """;

    internal const string Bundle = """
        {
          "kind": "full",
          "environmentId": "environment-1",
          "projectId": "project-1",
          "timestamp": "2026-08-01T12:00:00.000Z",
          "configs": {
            "temporary-feature-flag": {
              "id": "11111111-1111-4111-8111-111111111111",
              "key": "temporary-feature-flag",
              "type": "boolean",
              "target": {
                "defaultValue": false,
                "defaultValueId": "temporary-feature-flag-off",
                "rules": [
                  {
                    "id": "temporary-feature-flag-paid-plans",
                    "type": "conditional",
                    "order": 1,
                    "value": true,
                    "valueId": "temporary-feature-flag-on",
                    "conditions": [
                      {
                        "id": "temporary-feature-flag-plan",
                        "attribute": "traits",
                        "trait": "/plan",
                        "operator": "is one of",
                        "targetType": "text",
                        "targetValues": ["pro", "enterprise"]
                      }
                    ]
                  }
                ]
              }
            },
            "permanent-kill-switch": {
              "id": "22222222-2222-4222-8222-222222222222",
              "key": "permanent-kill-switch",
              "type": "boolean",
              "target": {
                "defaultValue": false,
                "defaultValueId": "permanent-kill-switch-default",
                "rules": [
                  {
                    "id": "permanent-kill-switch-rollout",
                    "type": "percentage",
                    "order": 1,
                    "percentages": [
                      {
                        "id": "permanent-kill-switch-held",
                        "percentage": 50,
                        "value": false,
                        "valueId": "permanent-kill-switch-off"
                      },
                      {
                        "id": "permanent-kill-switch-engaged",
                        "percentage": 50,
                        "value": true,
                        "valueId": "permanent-kill-switch-on"
                      }
                    ]
                  }
                ]
              }
            },
            "integer-config": {
              "id": "33333333-3333-4333-8333-333333333333",
              "key": "integer-config",
              "type": "integer",
              "target": {
                "defaultValue": 25,
                "defaultValueId": "integer-config-default"
              }
            },
            "day-of-the-week-config": {
              "id": "44444444-4444-4444-8444-444444444444",
              "key": "day-of-the-week-config",
              "type": "string",
              "target": {
                "defaultValue": "Monday",
                "defaultValueId": "day-of-the-week-config-default",
                "rules": [
                  {
                    "id": "day-of-the-week-config-beta",
                    "type": "conditional",
                    "order": 1,
                    "value": "Caturday",
                    "valueId": "day-of-the-week-config-beta-day",
                    "conditions": [
                      {
                        "id": "day-of-the-week-config-beta-tag",
                        "attribute": "traits",
                        "trait": "/tags",
                        "operator": "contains any of",
                        "targetType": "array",
                        "targetValues": ["beta"]
                      }
                    ]
                  },
                  {
                    "id": "day-of-the-week-config-modern-apps",
                    "type": "conditional",
                    "order": 2,
                    "value": "Sunday",
                    "valueId": "day-of-the-week-config-modern-day",
                    "conditions": [
                      {
                        "id": "day-of-the-week-config-app-version",
                        "attribute": "appVersion",
                        "operator": ">=",
                        "targetType": "semver",
                        "targetValues": ["2.0.0"]
                      }
                    ]
                  }
                ]
              }
            },
            "json-value-config": {
              "id": "55555555-5555-4555-8555-555555555555",
              "key": "json-value-config",
              "type": "json",
              "target": {
                "defaultValue": { "retries": 3, "timeoutMs": 1500 },
                "defaultValueId": "json-value-config-default"
              }
            }
          }
        }
        """;

    // One config on its own: a delta is what the server sends when only that config changed, a
    // full bundle is what it sends when that is all the config state there is.
    internal static string DayOfTheWeek(
        string value,
        string kind = "delta",
        string timestamp = "2026-08-01T12:00:01.000Z") =>
        $$"""
        {
          "kind": "{{kind}}",
          "timestamp": "{{timestamp}}",
          "configs": {
            "day-of-the-week-config": {
              "id": "44444444-4444-4444-8444-444444444444",
              "key": "day-of-the-week-config",
              "type": "string",
              "target": {
                "defaultValue": "{{value}}",
                "defaultValueId": "day-of-the-week-config-default"
              }
            }
          }
        }
        """;
}
