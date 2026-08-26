namespace ConfigDirector;

/// <summary>
/// Raised every time a config is evaluated, including evaluations that returned the caller's
/// default.
/// </summary>
public sealed class ConfigEvaluatedEventArgs : EventArgs
{
    internal ConfigEvaluatedEventArgs(ConfigEvaluation evaluation) => Evaluation = evaluation;

    /// <summary>What was asked for, and what came back.</summary>
    public ConfigEvaluation Evaluation { get; }
}
