namespace ConfigDirector;

/// <summary>Why an evaluation produced the value that it did.</summary>
public enum EvaluationReason
{
    /// <summary>A value from ConfigDirector was found and returned.</summary>
    FoundMatch = 0,

    /// <summary>The SDK holds no config by that key, so the in-code default value was returned.</summary>
    ConfigStateMissing,

    /// <summary>No config state has arrived yet, so the in-code default value was returned.</summary>
    ClientNotReady,

    /// <summary>The config matched but carries no value, so the in-code default value was returned.</summary>
    ValueMissing,

    /// <summary>The value would not parse as the number the in-code default value asked for.</summary>
    InvalidNumber,

    /// <summary>The value would not parse as the JSON shape the in-code default value asked for.</summary>
    InvalidJson,

    /// <summary>The value is neither <c>true</c> nor <c>false</c>.</summary>
    InvalidBoolean,

    /// <summary>
    /// The config holds a type the in-code default value did not ask for, so that value was returned.
    /// </summary>
    TypeMismatch,

    /// <summary>
    /// A percentage rollout was skipped because the context has no <see cref="Context.Id"/> to place it
    /// in a share, so the default targeting rule value was returned.
    /// <see cref="ConfigEvaluation.IsDefault"/> is false, since that value came from ConfigDirector.
    /// </summary>
    RolloutSkippedNoIdentifier,
}
