namespace ConfigDirector.Tests.Integration;

// The config state the stubbed server serves, in the wire format the server really sends. The keys
// are the ones every ConfigDirector sample application uses.
internal static class SampleConfigs
{
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

    // A delta carrying one config, which is what the server sends when a single config changes.
    internal static string DayOfTheWeek(string value, string timestamp = "2026-08-01T12:00:01.000Z") =>
        $$"""
        {
          "kind": "delta",
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
