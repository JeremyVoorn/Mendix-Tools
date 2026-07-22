namespace MendixTools.Core.Metadata;

/// <summary>
/// The canonical, ordered migration set for the local metadata store (MT-08 / N4).
///
/// SCHEMA (SQLite; timestamps stored as ISO-8601 UTC text, integers as INTEGER):
///
///   v1 tables
///   ─────────
///   restored_databases   — provenance of DBs this app restored (N4 / MT-17 / X1):
///       id, target_db_name, source_app, source_environment_id, snapshot_id,
///       snapshot_timestamp, mendix_version, size_bytes, restored_at, status
///   job_history          — durable after-the-fact job records (MT-09 writes these):
///       id, job_type, phases (JSON array), result, log_path, started_at, finished_at
///   cached_environment_state — stale/offline env payload cache (MT-20):
///       environment_id (PK), payload (JSON), fetched_at
///   cached_last_backup   — newest-snapshot-timestamp cache per env (MT-10/MT-20 lazy fill):
///       environment_id (PK), last_backup_at (nullable), fetched_at
///   snapshot_sizes       — locally-recorded archive sizes (the API has no size field):
///       snapshot_id (PK), size_bytes, recorded_at
///
/// Adding a table/column later = append a new <see cref="Migration"/> with the next version
/// (additive DDL only), so an older on-disk DB upgrades in place without data loss.
/// </summary>
public static class MetadataSchema
{
    /// <summary>The current highest schema version shipped by the app.</summary>
    public static int CurrentVersion => Migrations[^1].Version;

    /// <summary>All migrations, ascending. The store applies these on <c>InitializeAsync</c>.</summary>
    public static IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration
        {
            Version = 1,
            Description = "Initial schema: restored databases, job history, env cache, "
                          + "last-backup cache, snapshot sizes.",
            Statements =
            [
                """
                CREATE TABLE IF NOT EXISTS restored_databases (
                    id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                    target_db_name        TEXT    NOT NULL,
                    source_app            TEXT    NOT NULL,
                    source_environment_id TEXT    NOT NULL,
                    snapshot_id           TEXT    NULL,
                    snapshot_timestamp    TEXT    NULL,
                    mendix_version        TEXT    NULL,
                    size_bytes            INTEGER NULL,
                    restored_at           TEXT    NOT NULL,
                    status                INTEGER NOT NULL DEFAULT 0
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS job_history (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    job_type    TEXT    NOT NULL,
                    phases      TEXT    NOT NULL DEFAULT '[]',
                    result      INTEGER NOT NULL,
                    log_path    TEXT    NULL,
                    started_at  TEXT    NULL,
                    finished_at TEXT    NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS cached_environment_state (
                    environment_id TEXT PRIMARY KEY,
                    payload        TEXT NOT NULL,
                    fetched_at     TEXT NOT NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS cached_last_backup (
                    environment_id TEXT PRIMARY KEY,
                    last_backup_at TEXT NULL,
                    fetched_at     TEXT NOT NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS snapshot_sizes (
                    snapshot_id TEXT PRIMARY KEY,
                    size_bytes  INTEGER NOT NULL,
                    recorded_at TEXT    NOT NULL
                );
                """,
            ],
        },
    ];
}
