using MockDataStudio.Core.Common;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Common;

public sealed class ResultTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Ok_ShouldContainValue_AndSuccessTrue()
    {
        var result = Result<int>.Ok(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        Should.Throw<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Fail_ShouldContainError_AndSuccessFalse()
    {
        var error = new MdsError(ErrorCodes.PrvConnFailed, "Falha de conexão");
        var result = Result<int>.Fail(error);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(error);
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NonGenericResult_Ok_ShouldBeSuccess()
    {
        var result = Result.Ok();

        result.IsSuccess.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NonGenericResult_Fail_ShouldContainError()
    {
        var error = new MdsError(ErrorCodes.DigJson, "JSON inválido");
        var result = Result.Fail(error);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ImplicitConversions_ShouldWork()
    {
        Result<string> success = "dados gerados";
        success.IsSuccess.ShouldBeTrue();
        success.Value.ShouldBe("dados gerados");

        var error = new MdsError(ErrorCodes.OutDiskFull, "Disco cheio");
        Result<string> failure = error;
        failure.IsSuccess.ShouldBeFalse();
        failure.Error.Code.ShouldBe(ErrorCodes.OutDiskFull);
    }
}
