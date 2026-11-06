namespace ConfigDirector;

/// <summary>
/// What a tracked event carries besides its name: a number it measures and details about it.
/// </summary>
/// <remarks>
/// <code>
/// client.Track("checkout-completed", context, new TrackOptions
/// {
///     Value = order.Total,
///     Details = { ["plan"] = "pro" },
/// });
/// </code>
/// </remarks>
public sealed record TrackOptions
{
    private readonly TrackedEventDetails _details = [];
    private readonly bool _detailsAssigned;

    /// <summary>
    /// A number the event measures, such as an amount of money, a count or a duration. A value that
    /// is not a finite number is left out and the event is still reported.
    /// </summary>
    public TrackedEventValue? Value { get; init; }

    /// <summary>
    /// Details of the event. Never null; details that are never set or written to are not sent.
    /// Details with more than 20 top-level keys, or larger than 1 KB as JSON, are left out and the
    /// event is still reported. Assigning details copies them.
    /// </summary>
    public TrackedEventDetails Details
    {
        get => _details;
        init
        {
            _details = value is null ? [] : new TrackedEventDetails(value);
            _detailsAssigned = value is not null;
        }
    }

    internal bool HasDetails => _detailsAssigned || _details.Count > 0;
}
