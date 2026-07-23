namespace Mendix_Tools.Components.UI;

/// <summary>
/// MT-18 — the configured restore request <see cref="RestoreDialog"/> emits when the user clicks
/// "Start restore". Clean-restore only in v1 ("Merge into existing" is CUT — D5), so no strategy is
/// carried: the target database name and the keep-file choice are all the flow needs. The Backups
/// page takes this, presents the MT-19 typed-identifier guard (TokenValue = <see cref="TargetDatabaseName"/>),
/// and only on the guard's confirm downloads the archive + calls
/// <c>RestoreJobs.StartRestore(..., confirmed: true, keepFile: KeepFile, ...)</c>.
/// </summary>
/// <param name="TargetDatabaseName">Local Postgres database to (drop and) recreate and import into;
/// also the exact identifier the guard requires the user to retype.</param>
/// <param name="KeepFile">Keep the downloaded .backup after a successful restore (MT-12 preference
/// default; maps to the <c>keepFile</c> argument of <c>StartRestore</c>).</param>
public sealed record RestoreDialogResult(string TargetDatabaseName, bool KeepFile);
