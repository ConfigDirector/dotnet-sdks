namespace ConfigDirector;

/// <summary>Why an evaluation produced the value that it did.</summary>
public enum EvaluationReason
{
    /// <summary>A value was found and returned.</summary>
    FoundMatch = 0,

    /// <summary>The SDK holds no config by that key, so the default was returned.</summary>
    ConfigStateMissing,

    /// <summary>No config state has arrived yet, so the default was returned.</summary>
    ClientNotReady,

    /// <summary>The config matched but carries no value, so the default was returned.</summary>
    ValueMissing,

    /// <summary>The value would not parse as the number the default asked for.</summary>
    InvalidNumber,

    /// <summary>The value would not parse as the JSON shape the default asked for.</summary>
    InvalidJson,

    /// <summary>The value is neither <c>true</c> nor <c>false</c>.</summary>
    InvalidBoolean,
}
