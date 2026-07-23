using System.Buffers.Binary;
using System.IO.Compression;

namespace MendixTools.Core.Integrity;

/// <summary>
/// MT-16 — the LOCAL integrity check for a downloaded Backups-v2 archive. The Backups API exposes
/// NO checksum/hash of any kind (MT-01 §A2, D4), so this is the ONLY correctness mechanism, not a
/// fallback. It verifies two independent things:
///
///   1. <b>Size</b> — when the server advertised a <c>Content-Length</c>, the file on disk must be
///      exactly that many bytes (catches a truncated / interrupted download).
///   2. <b>Structure</b> — the file's magic bytes must identify a known archive format, and where
///      the format carries a verifiable trailer we validate it end-to-end:
///        • <b>gzip</b> (<c>1F 8B</c>) — a <c>.tar.gz</c> archive: decompressed in full so the
///          trailing CRC-32 + ISIZE are validated (catches silent corruption even with no
///          Content-Length).
///        • <b>zip</b> (<c>50 4B 03 04</c>) — opened via <see cref="ZipArchive"/>, which reads the
///          end-of-central-directory record (a trailer check).
///        • <b>PostgreSQL custom-format dump</b> (<c>PGDMP</c>) — a bare <c>pg_dump -Fc</c> file:
///          it has no cheap trailer, so we accept on magic + size match.
///
/// ASSUMPTION (documented, verify live — MT-01 items 5/6 are still open): a <c>database_only</c>
/// archive is one of the three formats above. Whatever the live layout turns out to be, the size
/// match always applies; the structural test degrades gracefully to "unrecognised format" rather
/// than a false pass. UI-agnostic (vision principle 7): no MAUI/Blazor types.
/// </summary>
public static class ArchiveIntegrity
{
    // Enough bytes to distinguish every format we recognise ("PGDMP" is the longest at 5).
    private const int MagicLength = 5;
    private const int MinPlausibleLength = 4;
    private const int ScratchSize = 81920;

    /// <summary>Verifies a file on disk. Reads its actual length from the filesystem.</summary>
    public static ArchiveIntegrityResult VerifyFile(string path, long? expectedContentLength, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return ArchiveIntegrityResult.Invalid(ArchiveFormat.Unknown, 0, "The downloaded file is missing.");
        }

        var actualLength = new FileInfo(path).Length;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Verify(stream, actualLength, expectedContentLength, ct);
    }

    /// <summary>
    /// Verifies an open, seekable stream. <paramref name="actualLength"/> is the real byte length;
    /// <paramref name="expectedContentLength"/> is the advertised Content-Length (null when absent).
    /// </summary>
    public static ArchiveIntegrityResult Verify(Stream stream, long actualLength, long? expectedContentLength, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (actualLength < MinPlausibleLength)
        {
            return ArchiveIntegrityResult.Invalid(ArchiveFormat.Unknown, actualLength,
                "The downloaded archive is too small to be a valid backup.");
        }

        // 1. Size match (when advertised). A short read is the most common interrupted-download failure.
        if (expectedContentLength is { } expected && expected != actualLength)
        {
            return ArchiveIntegrityResult.Invalid(ArchiveFormat.Unknown, actualLength,
                $"Size mismatch: expected {expected} bytes but the file is {actualLength} bytes.");
        }

        // 2. Structural check.
        if (!stream.CanSeek)
        {
            // Defensive: our callers pass FileStream/MemoryStream; without seek we can only size-check.
            return ArchiveIntegrityResult.Invalid(ArchiveFormat.Unknown, actualLength,
                "The archive stream is not seekable and could not be verified structurally.");
        }

        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> head = stackalloc byte[MagicLength];
        var read = ReadFull(stream, head);
        var format = DetectFormat(head[..read]);

        stream.Seek(0, SeekOrigin.Begin);
        return format switch
        {
            ArchiveFormat.Gzip => VerifyGzip(stream, actualLength, ct),
            ArchiveFormat.Zip => VerifyZip(stream, actualLength),
            // pg_dump custom format has no cheap trailer — magic + size is the best local check.
            ArchiveFormat.PgDumpCustom => ArchiveIntegrityResult.Valid(ArchiveFormat.PgDumpCustom, actualLength,
                "Recognised a PostgreSQL custom-format dump (verified by header + size)."),
            _ => ArchiveIntegrityResult.Invalid(ArchiveFormat.Unknown, actualLength,
                "Unrecognised archive format — the download does not look like a gzip, zip, or PostgreSQL dump."),
        };
    }

    private static ArchiveFormat DetectFormat(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 2 && head[0] == 0x1F && head[1] == 0x8B)
        {
            return ArchiveFormat.Gzip;
        }

        if (head.Length >= 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04)
        {
            return ArchiveFormat.Zip;
        }

        // "PGDMP" — the magic of a pg_dump custom/directory archive.
        if (head.Length >= 5 && head[0] == 0x50 && head[1] == 0x47 && head[2] == 0x44 && head[3] == 0x4D && head[4] == 0x50)
        {
            return ArchiveFormat.PgDumpCustom;
        }

        return ArchiveFormat.Unknown;
    }

    private static ArchiveIntegrityResult VerifyGzip(Stream stream, long actualLength, CancellationToken ct)
    {
        // The gzip trailer is CRC-32 (4 bytes LE) + ISIZE (4 bytes LE = uncompressed length mod 2^32).
        // .NET's inflater does NOT throw on a truncated deflate body, so we validate the ISIZE
        // trailer ourselves: decompress fully counting bytes, then require the produced length to
        // match the declared ISIZE. A truncated download's "last 4 bytes" are mid-stream deflate
        // data, so they will not match the real decompressed length → the truncation is caught even
        // when no Content-Length was advertised.
        uint declaredIsize = 0;
        var haveIsize = false;
        if (actualLength >= 8)
        {
            stream.Seek(-4, SeekOrigin.End);
            Span<byte> tail = stackalloc byte[4];
            if (ReadFull(stream, tail) == 4)
            {
                declaredIsize = BinaryPrimitives.ReadUInt32LittleEndian(tail);
                haveIsize = true;
            }

            stream.Seek(0, SeekOrigin.Begin);
        }

        try
        {
            using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
            var scratch = new byte[ScratchSize];
            long produced = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var n = gzip.Read(scratch, 0, scratch.Length);
                if (n == 0)
                {
                    break;
                }

                produced += n;
            }

            if (haveIsize && (uint)(produced & 0xFFFFFFFF) != declaredIsize)
            {
                return ArchiveIntegrityResult.Invalid(ArchiveFormat.Gzip, actualLength,
                    "The gzip archive is corrupt or truncated (decompressed length does not match its trailer).");
            }

            return ArchiveIntegrityResult.Valid(ArchiveFormat.Gzip, actualLength,
                "gzip archive decompressed and its length trailer validated.");
        }
        catch (InvalidDataException)
        {
            return ArchiveIntegrityResult.Invalid(ArchiveFormat.Gzip, actualLength,
                "The gzip archive is corrupt or truncated (failed to decompress).");
        }
    }

    private static ArchiveIntegrityResult VerifyZip(Stream stream, long actualLength)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            // Touching the entries forces the central directory (trailer) to be read/validated.
            _ = zip.Entries.Count;
            return ArchiveIntegrityResult.Valid(ArchiveFormat.Zip, actualLength,
                "zip archive opened and its central directory validated.");
        }
        catch (InvalidDataException)
        {
            return ArchiveIntegrityResult.Invalid(ArchiveFormat.Zip, actualLength,
                "The zip archive is corrupt or truncated (central directory unreadable).");
        }
    }

    private static int ReadFull(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = stream.Read(buffer[total..]);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }
}

/// <summary>Archive format identified from a file's magic bytes (MT-16 integrity check).</summary>
public enum ArchiveFormat
{
    Unknown = 0,
    Gzip = 1,
    Zip = 2,
    PgDumpCustom = 3,
}

/// <summary>Outcome of a local archive integrity check.</summary>
/// <param name="IsValid">True when the archive passed every applicable check.</param>
/// <param name="Format">The detected format (Unknown on a magic-byte failure).</param>
/// <param name="ActualLength">The file's real byte length.</param>
/// <param name="Detail">A human-readable, secret-free explanation of the result.</param>
public sealed record ArchiveIntegrityResult(bool IsValid, ArchiveFormat Format, long ActualLength, string Detail)
{
    internal static ArchiveIntegrityResult Valid(ArchiveFormat format, long length, string detail) =>
        new(true, format, length, detail);

    internal static ArchiveIntegrityResult Invalid(ArchiveFormat format, long length, string detail) =>
        new(false, format, length, detail);
}
