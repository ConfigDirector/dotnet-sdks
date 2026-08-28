using ConfigDirector;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Configures what <c>AddConfigDirector</c> registered.
/// </summary>
public static class ConfigDirectorBuilderExtensions
{
    /// <summary>
    /// Declares how a request becomes an evaluation context, once, rather than in every action.
    /// </summary>
    /// <remarks>
    /// Actions then take an <see cref="IConfigDirectorContextAccessor"/> and read
    /// <see cref="IConfigDirectorContextAccessor.Context"/>. The delegate runs at most once per
    /// request, the first time the context is asked for, and may return null to evaluate without
    /// one.
    /// <code>
    /// builder.Services.AddConfigDirector()
    ///     .WithContext(http => new Context { Id = http.User.FindFirst("sub")?.Value });
    /// </code>
    /// </remarks>
    /// <param name="builder">The builder returned by <c>AddConfigDirector</c>.</param>
    /// <param name="build">Builds the context for a request.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static ConfigDirectorBuilder WithContext(
        this ConfigDirectorBuilder builder, Func<HttpContext, Context?> build)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(build);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IConfigDirectorContextAccessor>(
            services => new HttpContextConfigDirectorContextAccessor(
                services.GetRequiredService<IHttpContextAccessor>(), build));

        return builder;
    }
}
