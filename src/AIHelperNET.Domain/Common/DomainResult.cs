namespace AIHelperNET.Domain.Common;

/// <summary>Represents the outcome of a domain operation with no return value.</summary>
public readonly struct DomainResult
{
    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailed => !IsSuccess;

    /// <summary>Gets the error message, or an empty string when the result is successful.</summary>
    public string Error { get; }

    private DomainResult(bool success, string error = "")
    {
        IsSuccess = success;
        Error = error;
    }

    /// <summary>Creates a successful result.</summary>
    public static DomainResult Ok() => new(true);

    /// <summary>Creates a failed result with the given error message.</summary>
    /// <param name="error">The error description.</param>
    public static DomainResult Fail(string error) => new(false, error);

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to carry.</param>
    public static DomainResult<T> Ok<T>(T value) => DomainResult<T>.Ok(value);

    /// <summary>Creates a failed result for a value-carrying operation.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="error">The error description.</param>
    public static DomainResult<T> Fail<T>(string error) => DomainResult<T>.Fail(error);
}

/// <summary>Represents the outcome of a domain operation that returns a value of type <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The type of the carried value.</typeparam>
public readonly struct DomainResult<T>
{
    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailed => !IsSuccess;

    /// <summary>Gets the error message, or an empty string when the result is successful.</summary>
    public string Error { get; }

    private readonly T? _value;

    /// <summary>
    /// Gets the carried value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is failed.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value of a failed result: {Error}");

    private DomainResult(bool success, T? value, string error)
    {
        IsSuccess = success;
        _value = value;
        Error = error;
    }

#pragma warning disable CA1000 // Static factory on generic type — called only via non-generic DomainResult
    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <param name="value">The value to carry.</param>
    public static DomainResult<T> Ok(T value) => new(true, value, "");

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The error description.</param>
    public static DomainResult<T> Fail(string error) => new(false, default, error);
#pragma warning restore CA1000
}
