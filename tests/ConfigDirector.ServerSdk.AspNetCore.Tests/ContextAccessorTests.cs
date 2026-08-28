using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ConfigDirector.AspNetCore.Tests;

public class ContextAccessorTests
{
    [Fact]
    public void BuildsTheContextFromTheRequest()
    {
        using var host = BuildHost(http => new Context { Id = http.Request.Query["id"] });
        using var scope = host.Services.CreateScope();
        Request(scope, "?id=user-1");

        Accessor(scope).Context!.Id.ShouldBe("user-1");
    }

    [Fact]
    public void BuildsTheContextOnceForTheWholeRequest()
    {
        var built = 0;
        using var host = BuildHost(_ =>
        {
            built++;

            return new Context { Id = "user-1" };
        });

        using var scope = host.Services.CreateScope();
        Request(scope, "?id=user-1");

        var accessor = Accessor(scope);
        _ = accessor.Context;
        _ = accessor.Context;
        _ = accessor.Context;

        built.ShouldBe(1);
    }

    [Fact]
    public void BuildsTheContextAgainForTheNextRequest()
    {
        var built = 0;
        using var host = BuildHost(_ =>
        {
            built++;

            return new Context { Id = "user-1" };
        });

        using (var first = host.Services.CreateScope())
        {
            Request(first, "?id=user-1");
            _ = Accessor(first).Context;
        }

        using (var second = host.Services.CreateScope())
        {
            Request(second, "?id=user-2");
            _ = Accessor(second).Context;
        }

        built.ShouldBe(2);
    }

    [Fact]
    public void HasNoContextOutsideARequest()
    {
        using var host = BuildHost(_ => new Context { Id = "user-1" });
        using var scope = host.Services.CreateScope();

        Accessor(scope).Context.ShouldBeNull();
    }

    [Fact]
    public void EvaluatesWithoutAContextWhenTheDelegateDeclines()
    {
        using var host = BuildHost(_ => null);
        using var scope = host.Services.CreateScope();
        Request(scope, "?id=user-1");

        Accessor(scope).Context.ShouldBeNull();
    }

    // Deliberately not registered by default: a null context silently disables targeting, so a
    // missing WithContext should fail loudly rather than evaluate everyone the same way.
    [Fact]
    public void IsNotRegisteredUntilWithContextIsCalled()
    {
        using var host = BuildHost(build: null);

        Should.Throw<InvalidOperationException>(
            () => host.Services.GetRequiredService<IConfigDirectorContextAccessor>());
    }

    [Fact]
    public void RejectsAMissingDelegate() =>
        Should.Throw<ArgumentNullException>(
            () => new ServiceCollection()
                .AddConfigDirector(options => options.ServerSdkKey = "a-key")
                .WithContext(null!));

    private static IConfigDirectorContextAccessor Accessor(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IConfigDirectorContextAccessor>();

    private static void Request(IServiceScope scope, string query) =>
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { Request = { QueryString = new QueryString(query) } };

    private static IHost BuildHost(Func<HttpContext, Context?>? build)
    {
        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { ApplicationName = "checkout" });

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConfigDirector:ServerSdkKey"] = "a-key" });

        var configDirector = builder.Services.AddConfigDirector();
        if (build is not null)
        {
            configDirector.WithContext(build);
        }

        return builder.Build();
    }
}
