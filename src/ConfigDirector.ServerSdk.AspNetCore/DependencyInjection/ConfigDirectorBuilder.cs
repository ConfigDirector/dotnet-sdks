namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Configures the ConfigDirector services added by <c>AddConfigDirector</c>.
/// </summary>
public sealed class ConfigDirectorBuilder
{
    internal ConfigDirectorBuilder(IServiceCollection services) => Services = services;

    /// <summary>The collection the ConfigDirector services were added to.</summary>
    public IServiceCollection Services { get; }
}
