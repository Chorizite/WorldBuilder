namespace WorldBuilder.Shared.Lib;

/// <summary>
/// Represents the result of an operation, which can be either a success with a value or a failure with an error.
/// </summary>
/// <typeparam name="T">The type of the value in case of success.</typeparam>
/// <typeparam name="TError">The type of the error in case of failure.</typeparam>
public readonly record struct Result<T, TError> {
    /// <summary>Gets the value if the operation was successful.</summary>
    public T Value { get; }
    /// <summary>Gets the error if the operation failed.</summary>
    public TError Error { get; }
    /// <summary>Gets a value indicating whether the operation was successful.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    private Result(T value, TError error, bool isSuccess) {
        Value = value!;
        Error = error!;
        IsSuccess = isSuccess;
    }

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T, TError> Success(T value) => new(value, default!, true);
    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The error.</param>
    /// <returns>A failed result.</returns>
    public static Result<T, TError> Failure(TError error) => new(default!, error, false);

    /// <summary>Matches the result and returns a value.</summary>
    /// <param name="onSuccess">Function to call on success.</param>
    /// <param name="onFailure">Function to call on failure.</param>
    /// <returns>The result of the match function.</returns>
    public T Match(Func<T, T> onSuccess, Func<TError, T> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    /// <summary>Matches the result and returns a result of a different type.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="onSuccess">Function to call on success.</param>
    /// <param name="onFailure">Function to call on failure.</param>
    /// <returns>The result of the match function.</returns>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<TError, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);
}

/// <summary>
/// Represents the result of an operation, which can be either a success with a value or a failure with an <see cref="Error"/>.
/// </summary>
/// <typeparam name="T">The type of the value in case of success.</typeparam>
public readonly record struct Result<T> {
    /// <summary>Gets the value if the operation was successful.</summary>
    public T Value { get; }
    /// <summary>Gets the error if the operation failed.</summary>
    public Error Error { get; }
    /// <summary>Gets a value indicating whether the operation was successful.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    private Result(T value, Error error, bool isSuccess) {
        Value = value!;
        Error = error;
        IsSuccess = isSuccess;
    }

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Success(T value) => new(value, Error.None, true);
    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The error.</param>
    /// <returns>A failed result.</returns>
    public static Result<T> Failure(Error error) => new(default!, error, false);
    /// <summary>Creates a failed result with a message and optional code.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="code">The optional failure code.</param>
    /// <returns>A failed result.</returns>
    public static Result<T> Failure(string message, string? code = null) => new(default!, Error.Failure(message, code), false);

    /// <summary>Matches the result and returns a value.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="onSuccess">Function to call on success.</param>
    /// <param name="onFailure">Function to call on failure.</param>
    /// <returns>The result of the match function.</returns>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    /// <summary>Matches the result and executes an action.</summary>
    /// <param name="onSuccess">Action to call on success.</param>
    /// <param name="onFailure">Action to call on failure.</param>
    public void Match(Action<T> onSuccess, Action<Error> onFailure) {
        if (IsSuccess) onSuccess(Value);
        else onFailure(Error);
    }
}

/// <summary>
/// Base class for all errors in the system.
/// </summary>
/// <param name="Message">The error message.</param>
/// <param name="Code">The error code.</param>
public abstract record Error(string Message, string Code) {
    /// <summary>Represents no error.</summary>
    public static Error None => new NoneError();
    /// <summary>Creates a generic failure error.</summary>
    public static Error Failure(string message, string? code = null) => new FailureError(message, code ?? "GENERIC_ERROR");
    /// <summary>Creates a validation error.</summary>
    public static Error Validation(string message, string? code = null) => new ValidationError(message, code ?? "VALIDATION_ERROR");
    /// <summary>Creates a not found error.</summary>
    public static Error NotFound(string message, string? code = null) => new NotFoundError(message, code ?? "NOT_FOUND_ERROR");

    private record NoneError(string Message = "", string Code = "") : Error(Message, Code);
    private record FailureError(string Message, string Code) : Error(Message, Code);
    private record ValidationError(string Message, string Code) : Error(Message, Code);
    private record NotFoundError(string Message, string Code) : Error(Message, Code);
}

/// <summary>
/// Represents a unit type with a single value.
/// </summary>
public readonly struct Unit {
    /// <summary>The single value of the unit type.</summary>
    public static readonly Unit Value = new();
}