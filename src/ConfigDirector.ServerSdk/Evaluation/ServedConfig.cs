namespace ConfigDirector.Evaluation;

internal readonly record struct ServedConfig(ConfigState State, EvaluationReason Reason);
