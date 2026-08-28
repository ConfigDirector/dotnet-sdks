# Slow tests

Behaviors whose only symptom is elapsed time. Each one guards a constant that the fast suite
cannot observe, because a test that could would have to outlive the timeout it is checking.

They are kept out of `ConfigDirector.slnx` so that `dotnet test` at the repository root stays
quick. CI builds this project on every push and runs it on a schedule.

```bash
dotnet test tests/ConfigDirector.ServerSdk.SlowTests/ConfigDirector.ServerSdk.SlowTests.csproj
```

Expect a couple of minutes.
