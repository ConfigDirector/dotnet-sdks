namespace ConfigDirector;

/// <summary>
/// The evaluation context for the request being handled, built by the delegate given to
/// <c>WithContext</c>.
/// </summary>
/// <remarks>
/// Injecting this is how an action evaluates configs for the caller without rebuilding the context
/// itself. It is registered only when <c>WithContext</c> has been called.
/// </remarks>
public interface IConfigDirectorContextAccessor
{
    /// <summary>
    /// The context for this request, or null when no request is in flight. Built once per request
    /// and reused, so several evaluations in one action cost one call to the delegate.
    /// </summary>
    Context? Context { get; }
}
