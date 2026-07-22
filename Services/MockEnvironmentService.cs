using Mendix_Tools.Models;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-10 mock <see cref="IEnvironmentService"/>. Hardcoded data mirroring
/// <c>docs/design-system/ui_kits/app/EnvironmentsScreen.jsx</c>'s ENVS — 3 licensed
/// customer apps (6 environments, Mendix versions 9.24–10.12) PLUS one personal sandbox
/// app — as amended by D1 (vision N6):
///   • statuses are Running/Stopped only (the mock's "Degraded"/"Deploying" are gone —
///     they are not API statuses and no logic may be built on them);
///   • no region and no live DB size anywhere (trimmed — the DTO has no field for them);
///   • sandbox environments omit MendixVersion/ModelVersion/RuntimeLayer (nullable).
///
/// <see cref="GetNewestBackupAsync"/> simulates the per-env latency of the real Backups-v2
/// call with a small per-environment delay, so the dashboard's lazy fill (vision N6) is
/// visibly proven. Sandboxes return <c>null</c> (no backups → card shows "—").
///
/// This is throwaway data for the mock-first slice: it is NOT wired to any real API and
/// carries no secrets. MT-20 replaces this class with the real Deploy-v1/Backups-v2 client.
/// </summary>
public sealed class MockEnvironmentService : IEnvironmentService
{
    // Relative to "now" so the rendered "2h ago"/"1d ago" strings stay stable across runs
    // and match the JSX per-environment values.
    private static readonly IReadOnlyList<MendixApp> Apps = BuildApps();

    // Per-environment newest-backup instants, keyed by EnvironmentId. Absent key => no
    // backups (sandboxes). Values chosen to reproduce the JSX lastBackup labels.
    private static readonly IReadOnlyDictionary<string, TimeSpan> BackupAges = new Dictionary<string, TimeSpan>
    {
        ["acme-prod"] = TimeSpan.FromHours(2),   // "2h ago"
        ["acme-accp"] = TimeSpan.FromDays(1),    // "1d ago"
        ["acme-test"] = TimeSpan.FromDays(6),    // "6d ago"
        ["belfort-prod"] = TimeSpan.FromHours(4),// "4h ago"
        ["kwik-accp"] = TimeSpan.FromDays(3),    // "3d ago"
        ["kwik-prod"] = TimeSpan.FromSeconds(20),// "just now"
    };

    public Task<IReadOnlyList<MendixApp>> GetAppsAsync(CancellationToken ct = default)
        => Task.FromResult(Apps);

    public async Task<DateTimeOffset?> GetNewestBackupAsync(
        string projectId, string environmentId, CancellationToken ct = default)
    {
        // Simulate the real per-env Backups-v2 round-trip so the lazy fill is observable.
        // Varying the delay per env makes the independent, non-blocking fill visible.
        var delayMs = 350 + Math.Abs(environmentId.GetHashCode() % 900);
        await Task.Delay(delayMs, ct).ConfigureAwait(false);

        return BackupAges.TryGetValue(environmentId, out var age)
            ? DateTimeOffset.Now - age
            : null; // sandboxes (and any env without backups) → "—"
    }

    private static IReadOnlyList<MendixApp> BuildApps() =>
    [
        new MendixApp
        {
            AppId = "acme-insurance",
            Name = "Acme Insurance",
            ProjectId = "b1f7c0e2-3a44-4d21-9f10-0a1b2c3d4e5f",
            Url = "https://sprintr.home.mendix.com/link/project/acme-insurance",
            Environments =
            [
                new MendixEnvironment
                {
                    EnvironmentId = "acme-prod",
                    Url = "acme-prod.mendixcloud.com",
                    Mode = "Production",
                    Status = EnvironmentStatus.Running,
                    Production = true,
                    MendixVersion = "10.12.4",
                    ModelVersion = "1.12.4.5521",
                    RuntimeLayer = "mxruntime",
                    Instances = 2,
                    MemoryPerInstance = 2048,
                    TotalMemory = 4096,
                },
                new MendixEnvironment
                {
                    EnvironmentId = "acme-accp",
                    Url = "acme-accp.mendixcloud.com",
                    Mode = "Acceptance",
                    Status = EnvironmentStatus.Running,
                    Production = false,
                    MendixVersion = "10.12.4",
                    ModelVersion = "1.12.4.5521",
                    RuntimeLayer = "mxruntime",
                    Instances = 1,
                    MemoryPerInstance = 1024,
                    TotalMemory = 1024,
                },
                new MendixEnvironment
                {
                    EnvironmentId = "acme-test",
                    Url = "acme-test.mendixcloud.com",
                    Mode = "Test",
                    Status = EnvironmentStatus.Stopped,
                    Production = false,
                    MendixVersion = "10.11.0",
                    ModelVersion = "1.11.0.5410",
                    RuntimeLayer = "mxruntime",
                    Instances = 1,
                    MemoryPerInstance = 1024,
                    TotalMemory = 1024,
                },
            ],
        },
        new MendixApp
        {
            AppId = "belfort-logistics",
            Name = "Belfort Logistics",
            ProjectId = "c2a8d1f3-4b55-4e32-8a21-1b2c3d4e5f60",
            Url = "https://sprintr.home.mendix.com/link/project/belfort-logistics",
            Environments =
            [
                // Was "Degraded" in the JSX mock — trimmed per D1; a real env is Running here.
                new MendixEnvironment
                {
                    EnvironmentId = "belfort-prod",
                    Url = "belfort.mendixcloud.com",
                    Mode = "Production",
                    Status = EnvironmentStatus.Running,
                    Production = true,
                    MendixVersion = "9.24.2",
                    ModelVersion = "3.4.1.9902",
                    RuntimeLayer = "mxruntime",
                    Instances = 3,
                    MemoryPerInstance = 4096,
                    TotalMemory = 12288,
                },
            ],
        },
        new MendixApp
        {
            AppId = "kwikpark",
            Name = "KwikPark",
            ProjectId = "d3b9e2a4-5c66-4f43-9b32-2c3d4e5f6071",
            Url = "https://sprintr.home.mendix.com/link/project/kwikpark",
            Environments =
            [
                new MendixEnvironment
                {
                    EnvironmentId = "kwik-accp",
                    Url = "kwikpark-accp.mendixcloud.com",
                    Mode = "Acceptance",
                    Status = EnvironmentStatus.Running,
                    Production = false,
                    MendixVersion = "10.6.1",
                    ModelVersion = "0.9.3.1204",
                    RuntimeLayer = "mxruntime",
                    Instances = 1,
                    MemoryPerInstance = 1024,
                    TotalMemory = 1024,
                },
                // Was "Deploying" in the JSX mock — trimmed per D1; a real env is Running here.
                new MendixEnvironment
                {
                    EnvironmentId = "kwik-prod",
                    Url = "kwikpark.mendixcloud.com",
                    Mode = "Production",
                    Status = EnvironmentStatus.Running,
                    Production = true,
                    MendixVersion = "10.6.1",
                    ModelVersion = "0.9.3.1204",
                    RuntimeLayer = "mxruntime",
                    Instances = 2,
                    MemoryPerInstance = 2048,
                    TotalMemory = 4096,
                },
            ],
        },
        // Personal sandbox app — GET /api/1/apps mixes these in with licensed apps
        // (MT-01 live run). Leaner payload: Mode=Sandbox, no version/runtime fields, no
        // backups. Grouped separately by the dashboard so it can't drown customer apps.
        new MendixApp
        {
            AppId = "app1099",
            Name = "Weekend Prototype",
            ProjectId = "e4c0f3b5-6d77-4054-8c43-3d4e5f607182",
            Url = "https://sprintr.home.mendix.com/link/project/app1099",
            Environments =
            [
                new MendixEnvironment
                {
                    EnvironmentId = "app1099-sandbox",
                    Url = "app1099.mxapps.io",
                    Mode = "Sandbox",
                    Status = EnvironmentStatus.Running,
                    Production = false,
                    // Sandbox payload omits these three (nullable) — card shows "—".
                    MendixVersion = null,
                    ModelVersion = null,
                    RuntimeLayer = null,
                    Instances = 1,
                    MemoryPerInstance = 512,
                    TotalMemory = 512,
                },
            ],
        },
    ];
}
