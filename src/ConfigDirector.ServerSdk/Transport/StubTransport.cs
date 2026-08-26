using ConfigDirector.Evaluation;

namespace ConfigDirector.Transport;

// Stands in for the real transports until they are implemented: it hands the client one fixed
// bundle so the public API can be exercised, and a sample application can run, without a server.
internal sealed class StubTransport : ITransport
{
    private static readonly ConfigBundle Bundle = new()
    {
        Configs = new Dictionary<string, Config>(StringComparer.Ordinal)
        {
            ["new-checkout"] = new Config
            {
                Id = "11111111-1111-4111-8111-111111111111",
                Key = "new-checkout",
                Type = ConfigType.Boolean,
                Target = new TargetingRules
                {
                    DefaultValue = "false",
                    DefaultValueId = "new-checkout-off",
                    Rules =
                    [
                        new ConditionalRule
                        {
                            Id = "new-checkout-paid-plans",
                            Order = 1,
                            Value = true,
                            ValueId = "new-checkout-on",
                            Conditions =
                            [
                                new Condition
                                {
                                    Id = "new-checkout-plan",
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
            ["checkout-banner"] = new Config
            {
                Id = "22222222-2222-4222-8222-222222222222",
                Key = "checkout-banner",
                Type = ConfigType.String,
                Target = new TargetingRules
                {
                    DefaultValue = "Welcome back",
                    DefaultValueId = "checkout-banner-default",
                    Rules =
                    [
                        new ConditionalRule
                        {
                            Id = "checkout-banner-beta",
                            Order = 1,
                            Value = "Welcome to the beta",
                            ValueId = "checkout-banner-beta-copy",
                            Conditions =
                            [
                                new Condition
                                {
                                    Id = "checkout-banner-beta-tag",
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
                            Id = "checkout-banner-modern-apps",
                            Order = 2,
                            Value = "Welcome to the new checkout",
                            ValueId = "checkout-banner-modern-copy",
                            Conditions =
                            [
                                new Condition
                                {
                                    Id = "checkout-banner-app-version",
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
            ["max-cart-items"] = new Config
            {
                Id = "33333333-3333-4333-8333-333333333333",
                Key = "max-cart-items",
                Type = ConfigType.Integer,
                Target = new TargetingRules { DefaultValue = "25", DefaultValueId = "max-cart-items-default" },
            },
            ["discount-rate"] = new Config
            {
                Id = "44444444-4444-4444-8444-444444444444",
                Key = "discount-rate",
                Type = ConfigType.Float,
                Target = new TargetingRules { DefaultValue = "0.15", DefaultValueId = "discount-rate-default" },
            },
            ["checkout-settings"] = new Config
            {
                Id = "55555555-5555-4555-8555-555555555555",
                Key = "checkout-settings",
                Type = ConfigType.Json,
                Target = new TargetingRules
                {
                    DefaultValue = """{"retries":3,"timeoutMs":1500}""",
                    DefaultValueId = "checkout-settings-default",
                },
            },
            ["checkout-experiment"] = new Config
            {
                Id = "66666666-6666-4666-8666-666666666666",
                Key = "checkout-experiment",
                Type = ConfigType.String,
                Target = new TargetingRules
                {
                    DefaultValue = "control",
                    DefaultValueId = "checkout-experiment-default",
                    Rules =
                    [
                        new PercentageRule
                        {
                            Id = "checkout-experiment-split",
                            Order = 1,
                            Percentages =
                            [
                                new PercentageBucket
                                {
                                    Id = "checkout-experiment-control",
                                    Percentage = 50,
                                    Value = "control",
                                    ValueId = "checkout-experiment-control-value",
                                },
                                new PercentageBucket
                                {
                                    Id = "checkout-experiment-variant",
                                    Percentage = 50,
                                    Value = "variant",
                                    ValueId = "checkout-experiment-variant-value",
                                },
                            ],
                        },
                    ],
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
