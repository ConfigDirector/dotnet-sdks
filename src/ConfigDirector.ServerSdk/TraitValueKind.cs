using System.Diagnostics.CodeAnalysis;

namespace ConfigDirector;

/// <summary>The JSON shape a <see cref="TraitValue"/> carries.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The members name JSON types, as System.Text.Json's own JsonValueKind does.")]
public enum TraitValueKind
{
    /// <summary>No value. Targeting rules treat it the same as a trait the context never carried.</summary>
    Null = 0,

    /// <summary>Text.</summary>
    String,

    /// <summary>A number, whole or fractional.</summary>
    Number,

    /// <summary><see langword="true"/> or <see langword="false"/>.</summary>
    Boolean,

    /// <summary>An ordered list of values.</summary>
    Array,

    /// <summary>A set of named values.</summary>
    Object,
}
