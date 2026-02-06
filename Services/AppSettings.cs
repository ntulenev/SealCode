namespace SealCode.Services;

public sealed class AppSettings
{
    public string AdminPassword { get; set; } = "change-me";
    public int MaxUsersPerRoom { get; set; } = 5;
}
