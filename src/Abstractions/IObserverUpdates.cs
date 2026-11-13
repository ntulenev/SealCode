using Models;

namespace Abstractions;

/// <summary>Publishes committed edits and reads the independent guest count.</summary>
public interface IObserverUpdates
{
    /// <summary>Records a committed document update for guest delivery.</summary>
    /// <param name="roomId">The edited room.</param>
    /// <param name="update">The encoded document update.</param>
    /// <param name="revision">The committed document revision.</param>
    void RecordUpdate(RoomId roomId, string update, long revision);

    /// <summary>Reads the independent guest count and revision.</summary>
    /// <param name="roomId">The room to inspect.</param>
    /// <returns>The guest count and its revision.</returns>
    (int Count, long Revision) Count(RoomId roomId);
}
