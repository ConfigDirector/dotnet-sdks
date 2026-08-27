using ConfigDirector.Telemetry;

namespace ConfigDirector.Tests.Telemetry;

public class ContextRegistryTests
{
    [Fact]
    public void CollectsTheContextsItIsGiven()
    {
        var registry = new ContextRegistry(10);
        registry.Add("user-a", new Context { Id = "user-a" });
        registry.Add("user-b", new Context { Id = "user-b" });

        var (contexts, dropped) = registry.TakeSnapshot();

        contexts.Select(context => context.Id).ShouldBe(["user-a", "user-b"]);
        dropped.ShouldBe(0);
    }

    [Fact]
    public void KeepsOnlyTheMostRecentContextForAnId()
    {
        var registry = new ContextRegistry(10);
        registry.Add("user-a", new Context { Id = "user-a" });
        registry.Add("user-a", new Context { Id = "user-a", Name = "Admin" });

        var (contexts, _) = registry.TakeSnapshot();

        contexts.ShouldHaveSingleItem().Name.ShouldBe("Admin");
    }

    [Fact]
    public void ASnapshotStartsTheNextBatchOver()
    {
        var registry = new ContextRegistry(10);
        registry.Add("user-a", new Context { Id = "user-a" });
        registry.TakeSnapshot();

        var (contexts, dropped) = registry.TakeSnapshot();

        contexts.ShouldBeEmpty();
        dropped.ShouldBe(0);
    }

    [Fact]
    public void EvictsTheOldestContextOnceFull()
    {
        var registry = new ContextRegistry(2);
        foreach (var identifier in new[] { "user-a", "user-b", "user-c", "user-d" })
        {
            registry.Add(identifier, new Context { Id = identifier });
        }

        var (contexts, dropped) = registry.TakeSnapshot();

        contexts.Select(context => context.Id).ShouldBe(["user-c", "user-d"]);
        dropped.ShouldBe(2);
    }

    // Re-assigning a key leaves it where it was, which is how the other SDKs behave too.
    [Fact]
    public void SeeingAContextAgainDoesNotSaveItFromEviction()
    {
        var registry = new ContextRegistry(2);
        registry.Add("user-a", new Context { Id = "user-a" });
        registry.Add("user-b", new Context { Id = "user-b" });
        registry.Add("user-a", new Context { Id = "user-a", Name = "Admin" });
        registry.Add("user-c", new Context { Id = "user-c" });

        var (contexts, _) = registry.TakeSnapshot();

        contexts.Select(context => context.Id).ShouldBe(["user-b", "user-c"]);
    }

    [Fact]
    public void ClearingDiscardsTheContextsAndTheDroppedCount()
    {
        var registry = new ContextRegistry(1);
        registry.Add("user-a", new Context { Id = "user-a" });
        registry.Add("user-b", new Context { Id = "user-b" });

        registry.Clear();

        var (contexts, dropped) = registry.TakeSnapshot();
        contexts.ShouldBeEmpty();
        dropped.ShouldBe(0);
    }

    [Fact]
    public async Task ConcurrentAddsAllLand()
    {
        var registry = new ContextRegistry(1_000);
        using var start = new Barrier(4);

        var adders = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            start.SignalAndWait();
            for (var index = 0; index < 50; index++)
            {
                var identifier = $"user-{worker}-{index}";
                registry.Add(identifier, new Context { Id = identifier });
            }
        })).ToArray();

        await Task.WhenAll(adders);

        var (contexts, dropped) = registry.TakeSnapshot();
        contexts.Count.ShouldBe(200);
        dropped.ShouldBe(0);
    }
}
