namespace MendixTools.Core.Models;

/// <summary>
/// Cached Deploy-v1 environment payload for the stale/offline path (MT-08 / N4; consumed
/// by MT-20). The env DTO is stored verbatim as JSON so the cache is decoupled from the
/// DTO shape; <see cref="FetchedAt"/> drives the visible "stale since …" indicator
/// (vision principle 6: offline is a first-class state).
/// </summary>
public sealed class CachedEnvironmentState
{
    /// <summary>Deploy-v1 <c>EnvironmentId</c> — the cache key.</summary>
    public required string EnvironmentId { get; set; }

    /// <summary>The environment payload, serialized as JSON by the caller.</summary>
    public required string Payload { get; set; }

    /// <summary>When this payload was fetched from the API (for staleness display).</summary>
    public DateTimeOffset FetchedAt { get; set; }
}
