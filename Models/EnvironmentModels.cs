namespace Mendix_Tools.Models;

// MT-10 — Environments dashboard DTOs.
//
// Shaped to the LIVE-VERIFIED Deploy API v1 payloads (MT-01 spike, 2026-07-22) so the
// wired MT-20 implementation reuses this exact shape without touching the page:
//
//   GET /api/1/apps                      -> AppId / Name / ProjectId / Url  (mixes
//                                           personal sandboxes with licensed apps).
//   GET /api/1/apps/{AppId}/environments -> Url / Mode / Status / ModelVersion /
//                                           MendixVersion / Production / Instances /
//                                           MemoryPerInstance / TotalMemory /
//                                           EnvironmentId / RuntimeLayer.
//
// TRIMMED per D1 (vision N6) — never modelled, never faked: "Degraded"/"Deploying"
// statuses, region, live DB size. Do not add them back and do not build logic on them.
//
// Sandbox environments return a LEANER payload: ModelVersion / MendixVersion /
// RuntimeLayer are ABSENT — hence nullable here; cards render "—" for them.

/// <summary>
/// Deploy API v1 environment status. Live run observed only <c>Running</c> and
/// <c>Stopped</c>; <c>Empty</c> is documented (never-deployed) and kept. "Degraded"
/// and "Deploying" are NOT statuses the API returns (D1 trim) and are absent by design.
/// </summary>
public enum EnvironmentStatus
{
    Running,
    Stopped,
    Empty,
}

/// <summary>
/// One deployment environment of an app (Deploy API v1 environment object). Version
/// fields are nullable because sandbox payloads omit them.
/// </summary>
public sealed record MendixEnvironment
{
    /// <summary>Deploy v1 <c>EnvironmentId</c>. Carried on the DTO; NOT shown on the card in v1.</summary>
    public required string EnvironmentId { get; init; }

    /// <summary>Deploy v1 <c>Url</c> — the environment host, shown mono on the card.</summary>
    public required string Url { get; init; }

    /// <summary>Deploy v1 <c>Mode</c> — Test / Acceptance / Production, or <c>Sandbox</c> for personal apps.</summary>
    public required string Mode { get; init; }

    /// <summary>Deploy v1 <c>Status</c> (Running/Stopped/Empty only — D1 trim).</summary>
    public required EnvironmentStatus Status { get; init; }

    /// <summary>Deploy v1 <c>Production</c> flag — drives the production marker.</summary>
    public bool Production { get; init; }

    /// <summary>Deploy v1 <c>MendixVersion</c> (e.g. <c>10.24.16.96987</c>). Null on sandboxes → card shows "—".</summary>
    public string? MendixVersion { get; init; }

    /// <summary>Deploy v1 <c>ModelVersion</c> (e.g. <c>1.8.82.e3c1a393</c>). Null on sandboxes.</summary>
    public string? ModelVersion { get; init; }

    /// <summary>Deploy v1 <c>RuntimeLayer</c>. Null on sandboxes. Carried; NOT shown in v1.</summary>
    public string? RuntimeLayer { get; init; }

    /// <summary>Deploy v1 <c>Instances</c>. Carried; NOT shown in v1.</summary>
    public int Instances { get; init; }

    /// <summary>Deploy v1 <c>MemoryPerInstance</c> (MB). Carried; NOT shown in v1.</summary>
    public int MemoryPerInstance { get; init; }

    /// <summary>Deploy v1 <c>TotalMemory</c> (MB). Carried; NOT shown in v1.</summary>
    public int TotalMemory { get; init; }

    /// <summary>True when this is a personal sandbox environment (leaner payload, no backups).</summary>
    public bool IsSandbox => string.Equals(Mode, "Sandbox", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// MT-20 — the outcome of an <see cref="Services.IEnvironmentService.GetAppsAsync"/> call.
/// The MT-10 seam originally returned a bare list, which collapsed "no credentials",
/// "credential rejected", and "offline" all to an empty grid. This distinguishes them so the
/// dashboard can render an actionable state (connect / fix credentials / check connection)
/// instead of a silent blank. Mirrors the typed <see cref="Services.MendixApiOutcome"/> the
/// underlying client already produces, trimmed to what the dashboard needs to say.
/// </summary>
public enum EnvironmentsOutcome
{
    /// <summary>The call succeeded (the list may still be empty if the account has no apps).</summary>
    Ok,

    /// <summary>No credential is stored — prompt the user to connect in Settings › Credentials.</summary>
    NoCredentials,

    /// <summary>HTTP 401/403 — the stored credential was rejected or lacks API Rights.</summary>
    CredentialsRejected,

    /// <summary>Transport failure / timeout — the Mendix API could not be reached.</summary>
    Offline,

    /// <summary>Rate-limited or an unreadable/unexpected response — a generic, retryable failure.</summary>
    Error,
}

/// <summary>
/// Result of listing apps + environments (MT-20). <see cref="Apps"/> is non-null and empty
/// for any non-<see cref="EnvironmentsOutcome.Ok"/> outcome, so the page can iterate it
/// unconditionally and branch on <see cref="Outcome"/> for the state it renders.
/// </summary>
public sealed record EnvironmentsResult(EnvironmentsOutcome Outcome, IReadOnlyList<MendixApp> Apps)
{
    /// <summary>A successful result carrying the fetched apps.</summary>
    public static EnvironmentsResult Ok(IReadOnlyList<MendixApp> apps) => new(EnvironmentsOutcome.Ok, apps);

    /// <summary>A failed result: no apps, just the outcome the dashboard renders a state for.</summary>
    public static EnvironmentsResult Failure(EnvironmentsOutcome outcome) => new(outcome, []);

    /// <summary>True when the list rendered normally (stat row + grid path).</summary>
    public bool IsSuccess => Outcome == EnvironmentsOutcome.Ok;
}

/// <summary>
/// An app the credential can see (Deploy API v1 apps object). Carries its environments so
/// the dashboard can group per app. <see cref="ProjectId"/> is the GUID the Backups API v2
/// uses (not <see cref="AppId"/>), so the seam's backup lookup takes ProjectId + EnvironmentId.
/// </summary>
public sealed record MendixApp
{
    /// <summary>Deploy v1 <c>AppId</c> — the subdomain string, used for Deploy v1 env/deploy calls.</summary>
    public required string AppId { get; init; }

    /// <summary>Deploy v1 <c>Name</c> — the customer-facing app name.</summary>
    public required string Name { get; init; }

    /// <summary>Deploy v1 <c>ProjectId</c> — the GUID used by the Backups API v2.</summary>
    public required string ProjectId { get; init; }

    /// <summary>Deploy v1 <c>Url</c> — the app's portal URL.</summary>
    public required string Url { get; init; }

    /// <summary>This app's environments.</summary>
    public required IReadOnlyList<MendixEnvironment> Environments { get; init; }

    /// <summary>True when this is a personal sandbox app — grouped separately so it can't drown licensed apps.</summary>
    public bool IsSandbox => Environments.Any(e => e.IsSandbox);
}
