namespace WorldBuilder.Shared.Lib;

public readonly record struct Result<T, TError> {
    public T Value { get; }
    public TError Error { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private Result(T value, TError error, bool isSuccess) {
        Value = value!;
        Error = error!;
        IsSuccess = isSuccess;
    }

    public static Result<T, TError> Success(T value) => new(value, default!, true);
    public static Result<T, TError> Failure(TError error) => new(default!, error, false);

    public T Match(Func<T, T> onSuccess, Func<TError, T> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<TError, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);
}

public readonly record struct Result<T> {
    public T Value { get; }
    public Error Error { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private Result(T value, Error error, bool isSuccess) {
        Value = value!;
        Error = error;
        IsSuccess = isSuccess;
    }

    public static Result<T> Success(T value) => new(value, Error.None, true);
    public static Result<T> Failure(Error error) => new(default!, error, false);
    public static Result<T> Failure(string message, string? code = null) => new(default!, Error.Failure(message, code), false);

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    public void Match(Action<T> onSuccess, Action<Error> onFailure) {
        if (IsSuccess) onSuccess(Value);
        else onFailure(Error);
    }
}

public abstract record Error(string Message, string Code) {
    public static Error None => new NoneError();
    public static Error Failure(string message, string? code = null) => new FailureError(message, code ?? "GENERIC_ERROR");
    public static Error Validation(string message, string? code = null) => new ValidationError(message, code ?? "VALIDATION_ERROR");
    public static Error NotFound(string message, string? code = null) => new NotFoundError(message, code ?? "NOT_FOUND_ERROR");

    private record NoneError(string Message = "", string Code = "") : Error(Message, Code);
    private record FailureError(string Message, string Code) : Error(Message, Code);
    private record ValidationError(string Message, string Code) : Error(Message, Code);
    private record NotFoundError(string Message, string Code) : Error(Message, Code);
}

public readonly struct Unit {
    public static readonly Unit Value = new();
}