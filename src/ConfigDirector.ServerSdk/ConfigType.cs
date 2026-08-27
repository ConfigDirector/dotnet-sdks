using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ConfigDirector;

/// <summary>The type a config was declared with in the ConfigDirector dashboard.</summary>
[JsonConverter(typeof(ConfigTypeJsonConverter))]
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The members are the type names ConfigDirector declares configs with.")]
public enum ConfigType
{
    /// <summary>A value the dashboard applies no type constraint to.</summary>
    Custom = 0,

    /// <summary><see langword="true"/> or <see langword="false"/>.</summary>
    Boolean,

    /// <summary>Free text.</summary>
    String,

    /// <summary>A whole number.</summary>
    Integer,

    /// <summary>A number that may have a fractional part.</summary>
    Float,

    /// <summary>Text restricted to a set of allowed values.</summary>
    Enum,

    /// <summary>Text constrained to a URL.</summary>
    Url,

    /// <summary>A JSON document.</summary>
    Json,
}
