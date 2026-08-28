namespace ConfigDirector;

// Shared by the interface and the implementation, which have to carry identical annotations on the
// members that bind a config to a caller's type.
internal static class Reflective
{
    internal const string BindingNeedsReflection =
        "Binding a config to a type of the caller's own reads that type's members reflectively. "
        + "Read the config as JsonElement instead, or keep the type rooted when trimming.";
}
