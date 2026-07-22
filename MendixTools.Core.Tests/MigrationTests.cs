using MendixTools.Core.Metadata;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-08 AC: "Given a schema change later, when the app starts on an older DB, then
/// migrations upgrade it without data loss." Simulated with a temp DB migrated to v1, then
/// re-opened with a v1+v2 set — the v1 row must survive and the v2 schema must exist.
/// </summary>
public sealed class MigrationTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"mxt-mig-{Guid.NewGuid():N}.db");

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _dbPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
    }.ToString();

    private static readonly Migration V1 = new()
    {
        Version = 1,
        Description = "create widgets",
        Statements = ["CREATE TABLE widgets (id INTEGER PRIMARY KEY, name TEXT NOT NULL);"],
    };

    private static readonly Migration V2 = new()
    {
        Version = 2,
        Description = "add widgets.color + a new table",
        Statements =
        [
            "ALTER TABLE widgets ADD COLUMN color TEXT NULL;",
            "CREATE TABLE gadgets (id INTEGER PRIMARY KEY, note TEXT NULL);",
        ],
    };

    [Fact]
    public void OlderDatabase_UpgradesToNewVersion_WithoutDataLoss()
    {
        // --- First run: only v1 is known. Seed a row. ---
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            var applied = SqliteMigrator.Apply(conn, [V1]);
            Assert.Equal(1, applied);
            Assert.Equal(1, SqliteMigrator.GetSchemaVersion(conn));

            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO widgets (name) VALUES ('acme');";
            insert.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        // --- Later run: the app now ships v1 + v2. Re-open the SAME file. ---
        using (var conn = new SqliteConnection(ConnectionString))
        {
            conn.Open();
            var applied = SqliteMigrator.Apply(conn, [V1, V2]);
            Assert.Equal(2, applied);
            Assert.Equal(2, SqliteMigrator.GetSchemaVersion(conn));

            // Pre-existing data survived the upgrade.
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT name FROM widgets WHERE id = 1;";
                Assert.Equal("acme", count.ExecuteScalar());
            }

            // The v2 column exists and is null for the pre-existing row.
            using (var color = conn.CreateCommand())
            {
                color.CommandText = "SELECT color FROM widgets WHERE id = 1;";
                Assert.True(color.ExecuteScalar() is null or DBNull);
            }

            // The v2 table exists.
            using (var gadgets = conn.CreateCommand())
            {
                gadgets.CommandText = "SELECT COUNT(*) FROM gadgets;";
                Assert.Equal(0L, gadgets.ExecuteScalar());
            }
        }
    }

    [Fact]
    public void ReapplyingSameMigrations_IsNoOp()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        Assert.Equal(2, SqliteMigrator.Apply(conn, [V1, V2]));
        // Second apply advances nothing and does not throw (idempotent by version).
        Assert.Equal(2, SqliteMigrator.Apply(conn, [V1, V2]));
    }

    [Fact]
    public void ShippedSchema_AppliesToCurrentVersion()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        var applied = SqliteMigrator.Apply(conn, MetadataSchema.Migrations);
        Assert.Equal(MetadataSchema.CurrentVersion, applied);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch (IOException) { /* temp file; ignore */ }
        }
    }
}
