using Abstractions;

using Models;

namespace SealCode.Tests;

internal sealed class NoOpObserverUpdates : IObserverUpdates
{
    public void RecordUpdate(RoomId roomId, string update, long revision) { }
    public (int Count, long Revision) Count(RoomId roomId) => (0, 0);
}
