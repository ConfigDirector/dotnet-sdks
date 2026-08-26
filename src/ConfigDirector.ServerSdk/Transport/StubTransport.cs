using ConfigDirector.Evaluation;

namespace ConfigDirector.Transport;

// Stands in for the real transports until they are implemented: it hands the client one fixed
// bundle so the public API can be exercised, and a sample application can run, without a server.
// The keys are the ones every ConfigDirector sample application uses.
internal sealed class StubTransport : ITransport
{
    private static readonly ConfigBundle Bundle = new()
    {
        Configs = new Dictionary<string, Config>(StringComparer.Ordinal)
        {
            ["temporary-feature-flag"] = new Config
            {
                Id = "11111111-1111-4111-8111-111111111111",
                Key = "temporary-feature-flag",
                Type = ConfigType.Boolean,
                Target = new TargetingRules
                {
                    DefaultValue = "false",
                    DefaultValueId = "temporary-feature-flag-off",
                    Rules =
                    [
                        new ConditionalRule
                        {
                            Id = "temporary-feature-flag-paid-plans",
                            Order = 1,
                            Value = true,
                            ValueId = "temporary-feature-flag-on",
                            Conditions =
                            [
                                new Condition
                                {
                                    Id = "temporary-feature-flag-plan",
                                    Attribute = "traits",
                                    Trait = "/plan",
                                    Operator = "is one of",
                                    TargetType = "text",
                                    TargetValues = ["pro", "enterprise"],
                                },
                            ],
                        },
                    ],
                },
            },

            // Rolled out by percentage, so which half a context lands in depends on its id alone.
            ["permanent-kill-switch"] = new Config
            {
                Id = "22222222-2222-4222-8222-222222222222",
                Key = "permanent-kill-switch",
                Type = ConfigType.Boolean,
                Target = new TargetingRules
                {
                    DefaultValue = "false",
                    DefaultValueId = "permanent-kill-switch-default",
                    Rules =
                    [
                        new PercentageRule
                        {
                            Id = "permanent-kill-switch-rollout",
                            Order = 1,
                            Percentages =
                            [
                                new PercentageBucket
                                {
                                    Id = "permanent-kill-switch-held",
                                    Percentage = 50,
                                    Value = false,
                                    ValueId = "permanent-kill-switch-off",
                                },
                                new PercentageBucket
                                {
                                    Id = "permanent-kill-switch-engaged",
                                    Percentage = 50,
                                    Value = true,
                                    ValueId = "permanent-kill-switch-on",
                                },
                            ],
                        },
                    ],
                },
            },

            ["integer-config"] = new Config
            {
                Id = "33333333-3333-4333-8333-333333333333",
                Key = "integer-config",
                Type = ConfigType.Integer,
                Target = new TargetingRules { DefaultValue = "25", DefaultValueId = "integer-config-default" },
            },

            ["day-of-the-week-config"] = new Config
            {
                Id = "44444444-4444-4444-8444-444444444444",
                Key = "day-of-the-week-config",
                Type = ConfigType.String,
                Target = new TargetingRules
                {
                    DefaultValue = "Monday",
                    DefaultValueId = "day-of-the-week-config-default",
                    Rules =
                    [
                        new ConditionalRule
                        {
                            Id = "day-of-the-week-config-beta",
                            Order = 1,
                            Value = "Caturday",
                            ValueId = "day-of-the-week-config-beta-day",
                            Conditions =
                            [
                                new Condition
                                {
                                    Id = "day-of-the-week-config-beta-tag",
                                    Attribute = "traits",
                                    Trait = "/tags",
                                    Operator = "contains any of",
                                    TargetType = "array",
                                    TargetValues = ["beta"],
                                },
                            ],
                        },
                        new ConditionalRule
                        {
                            Id = "day-of-the-week-config-modern-apps",
                            Order = 2,
                            Value = "Sunday",
                            ValueId = "day-of-the-week-config-modern-day",
                            Conditions =
                            [
                                new Condition
                                {
                                    Id = "day-of-the-week-config-app-version",
                                    Attribute = "appVersion",
                                    Operator = ">=",
                                    TargetType = "semver",
                                    TargetValues = ["2.0.0"],
                                },
                            ],
                        },
                    ],
                },
            },

            ["json-value-config"] = new Config
            {
                Id = "55555555-5555-4555-8555-555555555555",
                Key = "json-value-config",
                Type = ConfigType.Json,
                Target = new TargetingRules
                {
                    DefaultValue = """{"retries":3,"timeoutMs":1500}""",
                    DefaultValueId = "json-value-config-default",
                },
            },
        },
    };

    private readonly Action<ConfigBundle> _onBundle;

    internal StubTransport(Action<ConfigBundle> onBundle) => _onBundle = onBundle;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _onBundle(Bundle);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => default;
}
