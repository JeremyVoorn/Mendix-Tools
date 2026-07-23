using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-REL — auto-update client (Windows only).
///
/// Lives under Platforms/Windows so it compiles ONLY for the Windows TFM (where the
/// Velopack package reference exists); the mobile heads never see it. Wired from
/// <c>MauiProgram.CreateMauiApp</c> behind <c>#if WINDOWS</c>.
///
/// Flow: on startup we check the GitHub Releases feed for a newer version, download it
/// in the background, and tell the user via the existing toast seam. We do NOT restart
/// mid-session — <c>VelopackApp.Build().Run()</c> at the next launch auto-applies the
/// staged update, so the update is invisible until the user chooses to restart.
///
/// The <see cref="UpdateManager.IsInstalled"/> guard is the clean way to short-circuit
/// dev runs: running from <c>bin/</c> is not a real Velopack install, and any check call
/// would otherwise throw <c>NotInstalledException</c>.
/// </summary>
public sealed class WindowsUpdateService
{
    // The update feed is this repo's GitHub Releases (public → no token needed).
    private const string RepoUrl = "https://github.com/JeremyVoorn/Mendix-Tools";

    private readonly IUserNotifier _notifier;
    private readonly ILogger<WindowsUpdateService> _logger;

    public WindowsUpdateService(IUserNotifier notifier, ILogger<WindowsUpdateService> logger)
    {
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// Fire-and-forget from startup. Never throws — an update check must never be able to
    /// take the app down, so every failure is logged and swallowed.
    /// </summary>
    public async Task CheckInBackgroundAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));

            if (!mgr.IsInstalled)
            {
                _logger.LogInformation("Not a Velopack install (dev/bin run) — skipping update check.");
                return;
            }

            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion is null)
            {
                _logger.LogInformation("Mendix Tools is up to date.");
                return;
            }

            var version = newVersion.TargetFullRelease.Version;
            _logger.LogInformation("Update available: v{Version}. Downloading…", version);

            await mgr.DownloadUpdatesAsync(newVersion);

            // Staged only — applied automatically on the next launch by VelopackApp.Run().
            _notifier.Success(
                $"Update ready — v{version}",
                "Restart Mendix Tools to finish updating.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed; continuing on the current version.");
        }
    }
}
