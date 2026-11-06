namespace ConfigDirector.Testing;

/// <summary>
/// An event the code under test tracked, as a test client recorded it.
/// </summary>
/// <param name="Name">The name the event was tracked with.</param>
/// <param name="Context">
/// The context the event was tracked for, or null when there was none or its kind was invalid.
/// </param>
/// <param name="Value">
/// The value the event was tracked with, or null when there was none or it was not a finite number.
/// </param>
/// <param name="Details">
/// The details the event was tracked with, or null when there were none or they were outside the limits.
/// </param>
public sealed record TrackedEvent(
    string Name,
    Context? Context = null,
    TrackedEventValue? Value = null,
    TrackedEventDetails? Details = null);
