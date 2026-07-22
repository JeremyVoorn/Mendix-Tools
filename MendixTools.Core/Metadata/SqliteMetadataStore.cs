using System.Globalization;
using System.Text.Json;
using MendixTools.Core.Models;
using Microsoft.Data.Sqlite;

namespace MendixTools.Core.Metadata;

/// <summary>
/// <see cref="IMetadataStore"/> backed by a single local SQLite file (Microsoft.Data.Sqlite).
/// A connection is opened per operation (ADO.NET pooling keeps this cheap) so the store is
/// safe to share as a singleton across the app's async callers. Timestamps are persisted as
/// ISO-8601 UTC text; enums as their integer value. The DB path is supplied by the caller —
/// the MAUI app passes <c>FileSystem.AppDataDirectory</c>, keeping this library UI-agnostic.
/// </summary>
public sealed class SqliteMetadataStore : IMetadataStore
{
    private readonly string _connectionString;
    private readonly IReadOnlyList<Migration> _migrations;

    /// <summary>Creates a store over the SQLite file at <paramref name="databasePath"/>.</summary>
    /// <param name="databasePath">Absolute path to the .db file (created on first use).</param>
    /// <param name="migrations">Migration set to apply; defaults to <see cref="MetadataSchema.Migrations"/>.
    /// Overridable so tests can simulate an older DB.</param>
    public SqliteMetadataStore(string databasePath, IReadOnlyList<Migration>? migrations = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        _migrations = migrations ?? MetadataSchema.Migrations;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        SqliteMigrator.Apply(connection, _migrations);
    }

    // ── Restored databases ────────────────────────────────────────────────────────

    public async Task<long> AddRestoredDatabaseAsync(RestoredDatabase record, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO restored_databases
                (target_db_name, source_app, source_environment_id, snapshot_id,
                 snapshot_timestamp, mendix_version, size_bytes, restored_at, status)
            VALUES
                ($target, $app, $env, $snapshot, $snapTs, $version, $size, $restoredAt, $status);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$target", record.TargetDatabaseName);
        cmd.Parameters.AddWithValue("$app", record.SourceApp);
        cmd.Parameters.AddWithValue("$env", record.SourceEnvironmentId);
        cmd.Parameters.AddWithValue("$snapshot", (object?)record.SnapshotId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$snapTs", ToText(record.SnapshotTimestamp));
        cmd.Parameters.AddWithValue("$version", (object?)record.MendixVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", (object?)record.SizeBytes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$restoredAt", ToText(record.RestoredAt)!);
        cmd.Parameters.AddWithValue("$status", (int)record.Status);

        var id = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        record.Id = id;
        return id;
    }

    public async Task<RestoredDatabase?> GetRestoredDatabaseAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = RestoredSelect + " WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRestored(reader) : null;
    }

    public async Task<IReadOnlyList<RestoredDatabase>> GetRestoredDatabasesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = RestoredSelect + " ORDER BY restored_at DESC, id DESC;";

        var list = new List<RestoredDatabase>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadRestored(reader));
        }

        return list;
    }

    public async Task UpdateRestoredDatabaseAsync(RestoredDatabase record, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            UPDATE restored_databases SET
                target_db_name        = $target,
                source_app            = $app,
                source_environment_id = $env,
                snapshot_id           = $snapshot,
                snapshot_timestamp    = $snapTs,
                mendix_version        = $version,
                size_bytes            = $size,
                restored_at           = $restoredAt,
                status                = $status
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$target", record.TargetDatabaseName);
        cmd.Parameters.AddWithValue("$app", record.SourceApp);
        cmd.Parameters.AddWithValue("$env", record.SourceEnvironmentId);
        cmd.Parameters.AddWithValue("$snapshot", (object?)record.SnapshotId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$snapTs", ToText(record.SnapshotTimestamp));
        cmd.Parameters.AddWithValue("$version", (object?)record.MendixVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", (object?)record.SizeBytes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$restoredAt", ToText(record.RestoredAt)!);
        cmd.Parameters.AddWithValue("$status", (int)record.Status);
        cmd.Parameters.AddWithValue("$id", record.Id);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Job history ────────────────────────────────────────────────────────────────

    public async Task<long> AddJobAsync(JobHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO job_history (job_type, phases, result, log_path, started_at, finished_at)
            VALUES ($type, $phases, $result, $log, $started, $finished);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$type", entry.JobType);
        cmd.Parameters.AddWithValue("$phases", JsonSerializer.Serialize(entry.Phases));
        cmd.Parameters.AddWithValue("$result", (int)entry.Result);
        cmd.Parameters.AddWithValue("$log", (object?)entry.LogPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$started", ToText(entry.StartedAt));
        cmd.Parameters.AddWithValue("$finished", ToText(entry.FinishedAt));

        var id = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        entry.Id = id;
        return id;
    }

    public async Task<IReadOnlyList<JobHistoryEntry>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, job_type, phases, result, log_path, started_at, finished_at "
            + "FROM job_history ORDER BY id DESC;";

        var list = new List<JobHistoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new JobHistoryEntry
            {
                Id = reader.GetInt64(0),
                JobType = reader.GetString(1),
                Phases = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [],
                Result = (JobResult)reader.GetInt32(3),
                LogPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                StartedAt = ReadTimestamp(reader, 5),
                FinishedAt = ReadTimestamp(reader, 6),
            });
        }

        return list;
    }

    // ── Cached environment state ────────────────────────────────────────────────────

    public async Task CacheEnvironmentStateAsync(CachedEnvironmentState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO cached_environment_state (environment_id, payload, fetched_at)
            VALUES ($env, $payload, $fetched)
            ON CONFLICT(environment_id) DO UPDATE SET
                payload = excluded.payload,
                fetched_at = excluded.fetched_at;
            """;
        cmd.Parameters.AddWithValue("$env", state.EnvironmentId);
        cmd.Parameters.AddWithValue("$payload", state.Payload);
        cmd.Parameters.AddWithValue("$fetched", ToText(state.FetchedAt)!);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CachedEnvironmentState?> GetEnvironmentStateAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT environment_id, payload, fetched_at FROM cached_environment_state WHERE environment_id = $env;";
        cmd.Parameters.AddWithValue("$env", environmentId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CachedEnvironmentState
        {
            EnvironmentId = reader.GetString(0),
            Payload = reader.GetString(1),
            FetchedAt = ReadTimestamp(reader, 2)!.Value,
        };
    }

    // ── Cached last-backup timestamp ────────────────────────────────────────────────

    public async Task CacheLastBackupAsync(string environmentId, DateTimeOffset? lastBackupAt, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO cached_last_backup (environment_id, last_backup_at, fetched_at)
            VALUES ($env, $last, $fetched)
            ON CONFLICT(environment_id) DO UPDATE SET
                last_backup_at = excluded.last_backup_at,
                fetched_at = excluded.fetched_at;
            """;
        cmd.Parameters.AddWithValue("$env", environmentId);
        cmd.Parameters.AddWithValue("$last", ToText(lastBackupAt));
        cmd.Parameters.AddWithValue("$fetched", ToText(fetchedAt)!);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CachedLastBackup?> GetLastBackupAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT environment_id, last_backup_at, fetched_at FROM cached_last_backup WHERE environment_id = $env;";
        cmd.Parameters.AddWithValue("$env", environmentId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CachedLastBackup
        {
            EnvironmentId = reader.GetString(0),
            LastBackupAt = ReadTimestamp(reader, 1),
            FetchedAt = ReadTimestamp(reader, 2)!.Value,
        };
    }

    // ── Snapshot sizes ──────────────────────────────────────────────────────────────

    public async Task RecordSnapshotSizeAsync(string snapshotId, long sizeBytes, DateTimeOffset recordedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO snapshot_sizes (snapshot_id, size_bytes, recorded_at)
            VALUES ($id, $size, $recorded)
            ON CONFLICT(snapshot_id) DO UPDATE SET
                size_bytes = excluded.size_bytes,
                recorded_at = excluded.recorded_at;
            """;
        cmd.Parameters.AddWithValue("$id", snapshotId);
        cmd.Parameters.AddWithValue("$size", sizeBytes);
        cmd.Parameters.AddWithValue("$recorded", ToText(recordedAt)!);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long?> GetSnapshotSizeAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT size_bytes FROM snapshot_sizes WHERE snapshot_id = $id;";
        cmd.Parameters.AddWithValue("$id", snapshotId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private const string RestoredSelect =
        "SELECT id, target_db_name, source_app, source_environment_id, snapshot_id, "
        + "snapshot_timestamp, mendix_version, size_bytes, restored_at, status "
        + "FROM restored_databases";

    private static RestoredDatabase ReadRestored(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        TargetDatabaseName = reader.GetString(1),
        SourceApp = reader.GetString(2),
        SourceEnvironmentId = reader.GetString(3),
        SnapshotId = reader.IsDBNull(4) ? null : reader.GetString(4),
        SnapshotTimestamp = ReadTimestamp(reader, 5),
        MendixVersion = reader.IsDBNull(6) ? null : reader.GetString(6),
        SizeBytes = reader.IsDBNull(7) ? null : reader.GetInt64(7),
        RestoredAt = ReadTimestamp(reader, 8)!.Value,
        Status = (RestoreStatus)reader.GetInt32(9),
    };

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static object ToText(DateTimeOffset? value) =>
        value is null ? DBNull.Value : value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ReadTimestamp(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var text = reader.GetString(ordinal);
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
