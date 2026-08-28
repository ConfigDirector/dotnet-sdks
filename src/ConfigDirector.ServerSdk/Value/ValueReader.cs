namespace ConfigDirector.Value;

// How an evaluated config state becomes the type the caller asked for. The evaluation path takes
// this rather than a flag, so the binder that needs reflection is only reachable from the callers
// that pass it, and the typed getters stay analyzable for trimming.
internal delegate ParseResult<T> ValueReader<T>(ConfigState state, T defaultValue);
