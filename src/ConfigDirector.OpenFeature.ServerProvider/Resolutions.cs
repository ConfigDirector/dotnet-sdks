using OpenFeature.Constant;
using OpenFeature.Model;

namespace ConfigDirector.OpenFeature;

internal static class Resolutions
{
    internal static ResolutionDetails<T> Of<T>(string flagKey, T value, ConfigEvaluation? evaluation)
    {
        if (evaluation is null)
        {
            return new ResolutionDetails<T>(flagKey, value, ErrorType.None, null, null, null, null);
        }

        switch (evaluation.Reason)
        {
            case EvaluationReason.FoundMatch:
                return new ResolutionDetails<T>(flagKey, value, ErrorType.None, Reason.TargetingMatch, evaluation.ValueId, null, null);

            case EvaluationReason.ValueMissing:
                return new ResolutionDetails<T>(flagKey, value, ErrorType.None, Reason.Default, null, null, null);

            case EvaluationReason.ConfigStateMissing:
                return Error(flagKey, value, ErrorType.FlagNotFound, $"No config with the key '{evaluation.Key}' was found.");

            case EvaluationReason.ClientNotReady:
                return Error(flagKey, value, ErrorType.ProviderNotReady, "The ConfigDirector client has not received any configs yet.");

            default:
                return evaluation.IsDefault
                    ? Error(
                        flagKey,
                        value,
                        ErrorType.TypeMismatch,
                        $"The value of '{evaluation.Key}' does not match the requested type ({EvaluationReasons.WireName(evaluation.Reason)}).")
                    : new ResolutionDetails<T>(flagKey, value, ErrorType.None, null, null, null, null);
        }
    }

    internal static ResolutionDetails<T> Closed<T>(string flagKey, T value) =>
        Error(flagKey, value, ErrorType.ProviderFatal, "The ConfigDirector client has been closed.");

    private static ResolutionDetails<T> Error<T>(string flagKey, T value, ErrorType errorType, string message) =>
        new(flagKey, value, errorType, Reason.Error, null, message, null);
}
