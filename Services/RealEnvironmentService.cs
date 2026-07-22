using Mendix_Tools.Models;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-20 — the real <see cref="IEnvironmentService"/> that drops into the MT-10 seam behind
/// the SAME interface, so <c>Components/Pages/Environments.razor</c> needs no structural
/// change. Backed by the shared <see cref="IMendixApiClient"/> (Deploy v1 + Backups v2).
///
/// Graceful, never-crash contract (the seam has no error channel, and the app does no
/// credential pre-validation — MT-13 accepted deviation):
///   • no credential / 401 / 403 / offline / malformed → an EMPTY app list (the dashboard
///     renders its calm empty state), NOT an exception. Genuine caller cancellation still
///     propagates so the page's refresh-cancel path works.
///   • sandbox environments keep null <c>MendixVersion</c>/<c>ModelVersion</c>/<c>RuntimeLayer</c>
///     (the card shows "—"); their backup lookup returns null (no backups).
///
/// MT-08 cache / stale-offline rendering and MT-12 auto-refresh pacing layer ON TOP of this
/// service (out of MT-20's client-and-mapping slice); they consume the same seam unchanged.
/// </summary>
public sealed class RealEnvironmentService : IEnvironmentService
{
    private readonly IMendixApiClient _api;

    public RealEnvironmentService(IMendixApiClient api) => _api = api;

    public async Task<IReadOnlyList<MendixApp>> GetAppsAsync(CancellationToken ct = default)
    {
        var appsResult = await _api.GetAppsAsync(ct).ConfigureAwait(false);
        if (!appsResult.IsSuccess || appsResult.Value is null)
        {
            // No creds / rejected / offline / unreadable — surface an empty dashboard, never throw.
            return [];
        }

        var apps = new List<MendixApp>(appsResult.Value.Count);
        foreach (var rawApp in appsResult.Value)
        {
            if (string.IsNullOrWhiteSpace(rawApp.AppId))
            {
                continue;
            }

            // One environments call per app (N+1, sequential = polite pacing / rate-limit friendly).
            var envResult = await _api.GetEnvironmentsAsync(rawApp.AppId, ct).ConfigureAwait(false);
            var environments = envResult.IsSuccess && envResult.Value is not null
                ? envResult.Value.Select(MapEnvironment).ToList()
                : [];

            apps.Add(new MendixApp
            {
                AppId = rawApp.AppId,
                Name = string.IsNullOrWhiteSpace(rawApp.Name) ? rawApp.AppId : rawApp.Name,
                ProjectId = rawApp.ProjectId ?? string.Empty,
                Url = rawApp.Url ?? string.Empty,
                Environments = environments,
            });
        }

        return apps;
    }

    public async Task<DateTimeOffset?> GetNewestBackupAsync(string projectId, string environmentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(environmentId))
        {
            return null; // sandboxes carry no ProjectId-backed backups; nothing to look up.
        }

        // Snapshots come newest-first; ask for one. Any failure (incl. sandbox NOT_SUPPORTED,
        // 429, offline) → null so the card shows "—" without blocking or erroring.
        var result = await _api.GetSnapshotsAsync(projectId, environmentId, limit: 1, ct).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value?.Snapshots is not { Count: > 0 } snapshots)
        {
            return null;
        }

        DateTimeOffset? newest = null;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.CreatedAt is { } created && (newest is null || created > newest))
            {
                newest = created;
            }
        }

        return newest;
    }

    private static MendixEnvironment MapEnvironment(MendixEnvironmentRaw raw) => new()
    {
        EnvironmentId = raw.EnvironmentId ?? string.Empty,
        Url = raw.Url ?? string.Empty,
        Mode = raw.Mode ?? string.Empty,
        Status = MapStatus(raw.Status),
        Production = raw.Production,
        // Sandbox payloads omit these — normalise empty/whitespace to null so the card shows "—".
        MendixVersion = NullIfBlank(raw.MendixVersion),
        ModelVersion = NullIfBlank(raw.ModelVersion),
        RuntimeLayer = NullIfBlank(raw.RuntimeLayer),
        Instances = raw.Instances,
        MemoryPerInstance = raw.MemoryPerInstance,
        TotalMemory = raw.TotalMemory,
    };

    /// <summary>
    /// Maps the Deploy v1 <c>Status</c> string to the trimmed enum. Only Running/Stopped/Empty
    /// exist in the API (D1). Anything unexpected falls back to <see cref="EnvironmentStatus.Stopped"/>
    /// — a conservative default that never fabricates a healthy "Running" and never reintroduces
    /// the trimmed "Degraded"/"Deploying".
    /// </summary>
    private static EnvironmentStatus MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "running" => EnvironmentStatus.Running,
        "stopped" => EnvironmentStatus.Stopped,
        "empty" => EnvironmentStatus.Empty,
        _ => EnvironmentStatus.Stopped,
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
