using System.Reflection;
using ConfigDirector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers ConfigDirector with an <see cref="IServiceCollection"/>.
/// </summary>
public static class ConfigDirectorServiceCollectionExtensions
{
    /// <summary>
    /// Registers a single <see cref="IConfigDirectorClient"/> for the whole application, bound from
    /// the <c>ConfigDirector</c> configuration section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client is a singleton, which is what the SDK expects: it holds the connection, and a
    /// fresh one serves defaults until its first config state arrives. The container disposes it on
    /// shutdown.
    /// </para>
    /// <para>
    /// It is registered with <c>TryAdd</c>, so an <see cref="IConfigDirectorClient"/> already in the
    /// collection is left alone — which is how an integration test substitutes a fake.
    /// </para>
    /// </remarks>
    /// <param name="services">The collection to add to.</param>
    /// <returns>A builder for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static ConfigDirectorBuilder AddConfigDirector(this IServiceCollection services)
        => services.AddConfigDirector(_ => { });

    /// <summary>
    /// Registers ConfigDirector, bound from the <c>ConfigDirector</c> configuration section and
    /// then adjusted by <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="configure"/> runs after binding, so it wins over configuration. It is where
    /// a key held in a secret store belongs.
    /// </remarks>
    /// <param name="services">The collection to add to.</param>
    /// <param name="configure">Applied to the bound settings.</param>
    /// <inheritdoc cref="AddConfigDirector(IServiceCollection)"/>
    public static ConfigDirectorBuilder AddConfigDirector(
        this IServiceCollection services, Action<ConfigDirectorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return Add(
            services,
            ConfigDirectorOptions.SectionName,
            options => options.BindConfiguration(ConfigDirectorOptions.SectionName).Configure(configure));
    }

    /// <summary>
    /// Registers ConfigDirector, bound from <paramref name="section"/>.
    /// </summary>
    /// <param name="services">The collection to add to.</param>
    /// <param name="section">The configuration to bind, usually a named section.</param>
    /// <inheritdoc cref="AddConfigDirector(IServiceCollection)"/>
    public static ConfigDirectorBuilder AddConfigDirector(
        this IServiceCollection services, IConfiguration section)
        => services.AddConfigDirector(section, _ => { });

    /// <summary>
    /// Registers ConfigDirector, bound from <paramref name="section"/> and then adjusted by
    /// <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The collection to add to.</param>
    /// <param name="section">The configuration to bind, usually a named section.</param>
    /// <param name="configure">Applied to the bound settings, so it wins over configuration.</param>
    /// <inheritdoc cref="AddConfigDirector(IServiceCollection)"/>
    public static ConfigDirectorBuilder AddConfigDirector(
        this IServiceCollection services, IConfiguration section, Action<ConfigDirectorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(configure);

        var path = (section as IConfigurationSection)?.Path ?? ConfigDirectorOptions.SectionName;

        return Add(services, path, options => options.Bind(section).Configure(configure));
    }

    private static ConfigDirectorBuilder Add(
        IServiceCollection services,
        string sectionPath,
        Action<OptionsBuilder<ConfigDirectorOptions>> bind)
    {
        services.AddOptions();
        services.AddLogging();

        var options = services.AddOptions<ConfigDirectorOptions>();
        bind(options);

        options
            .PostConfigure<IHostEnvironment>(DescribeTheApplication)
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.ServerSdkKey),
                $"A ConfigDirector server SDK key is required. Set '{sectionPath}:{nameof(ConfigDirectorOptions.ServerSdkKey)}', "
                    + "or supply it in code with AddConfigDirector(options => options.ServerSdkKey = ...).")
            .ValidateOnStart();

        services.TryAddSingleton<IConfigDirectorClient>(BuildClient);

        return new ConfigDirectorBuilder(services);
    }

    private static IConfigDirectorClient BuildClient(IServiceProvider services)
    {
        var settings = services.GetRequiredService<IOptions<ConfigDirectorOptions>>().Value;

        return new ConfigDirectorClient(settings.ServerSdkKey!, new ConfigDirectorClientOptions
        {
            Metadata = MetadataFrom(settings),
            LoggerFactory = services.GetRequiredService<ILoggerFactory>(),
            Connection = settings.Connection,
            Telemetry = settings.Telemetry,
        });
    }

    private static Metadata? MetadataFrom(ConfigDirectorOptions settings)
        => settings.AppName is null && settings.AppVersion is null
            ? null
            : new Metadata { AppName = settings.AppName, AppVersion = settings.AppVersion };

    private static void DescribeTheApplication(ConfigDirectorOptions settings, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(settings.AppName))
        {
            settings.AppName = string.IsNullOrWhiteSpace(environment.ApplicationName)
                ? null
                : environment.ApplicationName;
        }

        if (string.IsNullOrWhiteSpace(settings.AppVersion))
        {
            settings.AppVersion = EntryAssemblyVersion();
        }
    }

    private static string? EntryAssemblyVersion()
    {
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        // Build metadata is normal in an informational version and matches no semver targeting
        // rule, so "1.2.3+9f4c1a" is reported as "1.2.3".
        var metadata = version.IndexOf('+');

        return metadata < 0 ? version : version[..metadata];
    }
}
