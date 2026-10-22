using Models.Exceptions;

using SealCode.Hub;

namespace SealCode.Tests;

public sealed class HubErrorsTests
{
    [Fact]
    public void TranslatesKnownFailuresWithoutLeakingNullParameterDetails()
    {
        Assert.Equal("Room not found", HubErrors.Translate(new RoomNotFoundException())!.Message);
        Assert.Equal("Join the room before changing it", HubErrors.Translate(new UnauthorizedAccessException())!.Message);
        Assert.Equal("LimitExceeded: Update size", HubErrors.Translate(new RoomLimitException("Update size"))!.Message);
        Assert.Equal("InvalidInput: Invalid room input", HubErrors.Translate(new ArgumentNullException("secret"))!.Message);
        Assert.Equal("Invalid language", HubErrors.Translate(new ArgumentException("Invalid language"))!.Message);
        Assert.Equal("InvalidInput: Invalid room input", HubErrors.Translate(WithParameter("param"))!.Message);
        Assert.Null(HubErrors.Translate(new IOException()));
        var fallback = new RoomLimitException();
        Assert.Equal($"LimitExceeded: {fallback.Resource}", fallback.Message);
    }
    private static ArgumentException WithParameter(string param) => new($"x: {param}", nameof(param));
}
