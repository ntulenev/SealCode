using Microsoft.AspNetCore.SignalR;

namespace SealCode.Observers;

/// <summary>Records stable observer refusals.</summary>
internal static class ObserverSessionErrors
{
    /// <summary>Creates a stable protocol refusal and increments its metric.</summary>
    /// <param name="code">The stable protocol refusal code.</param>
    /// <returns>The corresponding transport exception.</returns>
    public static HubException Error(string code)
    {
        ObserverMetrics.Rejected.Add(1, new KeyValuePair<string, object?>("reason",
            code));
        return new(code);
    }
}
