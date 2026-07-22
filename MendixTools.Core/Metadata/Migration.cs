using Microsoft.Data.Sqlite;

namespace MendixTools.Core.Metadata;

/// <summary>
/// One forward schema step, identified by a monotonically increasing <see cref="Version"/>.
/// Migrations are applied in order and each is idempotent-by-version: the store records the
/// highest applied version in SQLite's <c>PRAGMA user_version</c>, so re-running is a no-op
/// and an older on-disk DB is upgraded without touching already-migrated data (MT-08 AC:
/// "migrations upgrade an older DB without data loss").
/// </summary>
public sealed class Migration
{
    /// <summary>1-based version number. Must be unique and increasing across the set.</summary>
    public required int Version { get; init; }

    /// <summary>Human-readable note for diagnostics/log.</summary>
    public required string Description { get; init; }

    /// <summary>SQL statements executed, in order, inside one transaction for this version.
    /// Use additive DDL (CREATE TABLE / ADD COLUMN) so existing rows survive.</summary>
    public required IReadOnlyList<string> Statements { get; init; }
}

/// <summary>
/// Applies a set of <see cref="Migration"/>s to a SQLite connection using
/// <c>PRAGMA user_version</c> as the applied-version marker. UI-agnostic and reusable, so a
/// test can construct a partial migration set to simulate an older DB (MT-08 migration test).
/// </summary>
public static class SqliteMigrator
{
    /// <summary>Reads the DB's current schema version (0 on a brand-new database).</summary>
    public static int GetSchemaVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = cmd.ExecuteScalar();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Applies every migration whose <see cref="Migration.Version"/> exceeds the DB's current
    /// version, in ascending order, each in its own transaction, advancing
    /// <c>user_version</c> after each. Returns the resulting schema version.
    /// </summary>
    public static int Apply(SqliteConnection connection, IReadOnlyList<Migration> migrations)
    {
        var ordered = migrations.OrderBy(m => m.Version).ToList();
        var current = GetSchemaVersion(connection);

        foreach (var migration in ordered)
        {
            if (migration.Version <= current)
            {
                continue;
            }

            using var tx = connection.BeginTransaction();
            foreach (var statement in migration.Statements)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = statement;
                cmd.ExecuteNonQuery();
            }

            // user_version cannot be parameterised; Version is our own trusted int.
            using (var versionCmd = connection.CreateCommand())
            {
                versionCmd.Transaction = tx;
                versionCmd.CommandText = $"PRAGMA user_version = {migration.Version};";
                versionCmd.ExecuteNonQuery();
            }

            tx.Commit();
            current = migration.Version;
        }

        return current;
    }
}
