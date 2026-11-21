namespace Budgetly.Api.Application;

public enum HandlerErrorKind { Validation, NotFound, Conflict, Unauthorized }

public sealed record HandlerError(
    HandlerErrorKind Kind,
    string? Message = null,
    Dictionary<string, string[]>? ValidationErrors = null,
    Dictionary<string, object?>? Details = null)
{
    public static HandlerError Invalid(string field, string message) =>
        Invalid(new Dictionary<string, string[]> { [field] = [message] });

    public static HandlerError Invalid(Dictionary<string, string[]> errors) =>
        new(HandlerErrorKind.Validation, ValidationErrors: errors);

    public static HandlerError NotFound() => new(HandlerErrorKind.NotFound);
    public static HandlerError Unauthorized() => new(HandlerErrorKind.Unauthorized);
    public static HandlerError Conflict(string message, Dictionary<string, object?>? details = null) =>
        new(HandlerErrorKind.Conflict, message, Details: details);
}

public sealed record HandlerResult<T>(T? Value, HandlerError? Error = null)
{
    public static HandlerResult<T> Success(T value) => new(value);
    public static implicit operator HandlerResult<T>(HandlerError error) => new(default, error);
}
