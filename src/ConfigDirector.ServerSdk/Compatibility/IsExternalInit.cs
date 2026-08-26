#if !NET5_0_OR_GREATER

#pragma warning disable IDE0130 // the runtime fixes this namespace, the folder cannot match it

namespace System.Runtime.CompilerServices;

// init-only setters and records need this type to exist. .NET ships it; netstandard2.0 does not,
// so the compiler picks up this one when targeting it.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class IsExternalInit
{
}

#endif
