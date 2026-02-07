namespace Transport;

public sealed record JoinRoomResult(string Name, string Language, string Text, int Version, string[] Users, string CreatedBy);
