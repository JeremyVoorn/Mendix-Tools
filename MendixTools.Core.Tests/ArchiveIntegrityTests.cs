using System.IO.Compression;
using MendixTools.Core.Integrity;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-16 — tests for the LOCAL archive integrity check (the only correctness mechanism — the
/// Backups API exposes no checksum, D4). Uses in-memory / temp-file archives only; no network.
/// </summary>
public sealed class ArchiveIntegrityTests
{
    [Fact]
    public void ValidGzip_Passes_AndReportsGzipFormat()
    {
        using var stream = new MemoryStream(Gzip(RandomBytes(4096)));

        var result = ArchiveIntegrity.Verify(stream, stream.Length, expectedContentLength: stream.Length);

        Assert.True(result.IsValid);
        Assert.Equal(ArchiveFormat.Gzip, result.Format);
    }

    [Fact]
    public void ValidZip_Passes_AndReportsZipFormat()
    {
        using var stream = new MemoryStream(Zip("db.backup", RandomBytes(2048)));

        var result = ArchiveIntegrity.Verify(stream, stream.Length, expectedContentLength: null);

        Assert.True(result.IsValid);
        Assert.Equal(ArchiveFormat.Zip, result.Format);
    }

    [Fact]
    public void PgDumpCustomFormat_Passes_ByHeaderAndSize()
    {
        // "PGDMP" magic + arbitrary trailing bytes (pg_dump -Fc has no cheap trailer).
        var bytes = new byte[512];
        "PGDMP"u8.CopyTo(bytes);
        using var stream = new MemoryStream(bytes);

        var result = ArchiveIntegrity.Verify(stream, stream.Length, expectedContentLength: stream.Length);

        Assert.True(result.IsValid);
        Assert.Equal(ArchiveFormat.PgDumpCustom, result.Format);
    }

    [Fact]
    public void ContentLengthMismatch_Fails_EvenWhenBodyIsValidGzip()
    {
        var body = Gzip(RandomBytes(4096));
        using var stream = new MemoryStream(body);

        // Server advertised more bytes than actually landed → truncated download.
        var result = ArchiveIntegrity.Verify(stream, stream.Length, expectedContentLength: stream.Length + 100);

        Assert.False(result.IsValid);
        Assert.Contains("Size mismatch", result.Detail);
    }

    [Fact]
    public void TruncatedGzip_Fails_StructuralTrailerCheck()
    {
        // Compressible payload → real huffman-coded deflate blocks (random data would be STORED
        // uncompressed and survive truncation). Cutting the compressed body then breaks decoding.
        var compressible = System.Text.Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 5000)));
        var full = Gzip(compressible);
        var truncated = full[..(full.Length / 2)];
        using var stream = new MemoryStream(truncated);

        var result = ArchiveIntegrity.Verify(stream, stream.Length, expectedContentLength: null);

        Assert.False(result.IsValid);
        Assert.Equal(ArchiveFormat.Gzip, result.Format);
    }

    [Fact]
    public void UnknownMagic_Fails()
    {
        using var stream = new MemoryStream(RandomBytes(1024)); // random bytes, no known magic

        var result = ArchiveIntegrity.Verify(stream, stream.Length, expectedContentLength: null);

        Assert.False(result.IsValid);
        Assert.Equal(ArchiveFormat.Unknown, result.Format);
    }

    [Fact]
    public void TinyFile_Fails_TooSmall()
    {
        using var stream = new MemoryStream([0x1F, 0x8B]); // 2 bytes

        var result = ArchiveIntegrity.Verify(stream, stream.Length, expectedContentLength: null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void VerifyFile_MissingFile_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"mxt-missing-{Guid.NewGuid():N}.backup");

        var result = ArchiveIntegrity.VerifyFile(missing, expectedContentLength: null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void VerifyFile_ValidGzipOnDisk_Passes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mxt-arc-{Guid.NewGuid():N}.backup");
        File.WriteAllBytes(path, Gzip(RandomBytes(8192)));
        try
        {
            var result = ArchiveIntegrity.VerifyFile(path, expectedContentLength: new FileInfo(path).Length);
            Assert.True(result.IsValid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────
    private static byte[] RandomBytes(int count)
    {
        var b = new byte[count];
        new Random(count).NextBytes(b);
        return b;
    }

    private static byte[] Gzip(byte[] payload)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(payload);
        }

        return ms.ToArray();
    }

    private static byte[] Zip(string entryName, byte[] payload)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName);
            using var s = entry.Open();
            s.Write(payload);
        }

        return ms.ToArray();
    }
}
