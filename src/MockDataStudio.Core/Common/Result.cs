namespace MockDataStudio.Core.Common;

/// <summary>
/// Estrutura de retorno para operações funcionais com sucesso ou erro de domínio.
/// </summary>
/// <typeparam name="T">Tipo do valor em caso de sucesso.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Exigência normativa do contrato C01 para fábricas funcionais Result<T>.Ok e Result<T>.Fail (TASK-00.04)")]
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly MdsError? _error;

    public bool IsSuccess { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value of failed result. Error: {_error?.Code} - {_error?.Message}");

    public MdsError Error => !IsSuccess
        ? _error!
        : throw new InvalidOperationException("Cannot access Error of successful result.");

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
        _error = null;
    }

    private Result(MdsError error)
    {
        IsSuccess = false;
        _value = default;
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public static Result<T> Ok(T value) => new(value);

    public static Result<T> Fail(MdsError error) => new(error);

    public static Result<T> Fail(string code, string message, IReadOnlyDictionary<string, string>? details = null)
        => new(new MdsError(code, message, details));

    public static implicit operator Result<T>(T value) => Ok(value);

    public static implicit operator Result<T>(MdsError error) => Fail(error);
}

/// <summary>
/// Estrutura de retorno sem valor para operações funcionais que podem falhar com MdsError.
/// </summary>
public readonly struct Result
{
    private readonly MdsError? _error;

    public bool IsSuccess { get; }

    public MdsError Error => !IsSuccess
        ? _error!
        : throw new InvalidOperationException("Cannot access Error of successful result.");

    private Result(bool isSuccess, MdsError? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    public static Result Ok() => new(true, null);

    public static Result Fail(MdsError error) => new(false, error ?? throw new ArgumentNullException(nameof(error)));

    public static Result Fail(string code, string message, IReadOnlyDictionary<string, string>? details = null)
        => new(false, new MdsError(code, message, details));

    public static implicit operator Result(MdsError error) => Fail(error);
}
