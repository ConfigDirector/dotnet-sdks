#if !NET8_0_OR_GREATER

// The trimming attributes ship with .NET 5 onwards. Declaring them here lets the rest of the SDK
// annotate reflective APIs once, rather than wrapping every use site in a #if for netstandard2.0,
// where nothing reads them anyway. The namespace is the framework's, not this folder's, which is
// the whole point of a polyfill.
#pragma warning disable IDE0130
namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
internal sealed class RequiresUnreferencedCodeAttribute(string message) : Attribute
{
    public string Message { get; } = message;

    public string? Url { get; set; }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
internal sealed class RequiresDynamicCodeAttribute(string message) : Attribute
{
    public string Message { get; } = message;

    public string? Url { get; set; }
}

[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
internal sealed class UnconditionalSuppressMessageAttribute(string category, string checkId) : Attribute
{
    public string Category { get; } = category;

    public string CheckId { get; } = checkId;

    public string? Justification { get; set; }
}

#endif
