using System.IO.Compression;

namespace Peritum.Protect.Core.Files;

public sealed class PackageLimitException : Exception
{
    public PackageLimitException(string message) : base(message)
    {
    }
}

/// <summary>
/// One set of limits for every Office package and nested zip.
/// Entry.Length is checked before a read, and the bytes actually read are counted because the header can lie.
/// </summary>
public static class PackageLimits
{
    public const long MaxTotalUncompressed = 256L * 1024 * 1024;
    public const long MaxEntryUncompressed = 64L * 1024 * 1024;
    public const int MaxEntries = 10_000;
    public const int MaxCompressionRatio = 100;
    public const int MaxNestingDepth = 3;
    public const int MaxPdfPages = 2_000;
    public static readonly TimeSpan PdfTimeBudget = TimeSpan.FromSeconds(15);

    public const string ExpandsTooFar = "This file expands to more than 256 MB. It was not fully checked.";
    public const string EntryTooBig = "This file has a part larger than 64 MB. It was not fully checked.";
    public const string TooManyEntries = "This file has more than 10,000 parts. It was not fully checked.";
    public const string RatioTooHigh = "This file is compressed more than 100 to 1. It was not fully checked.";
    public const string NestedTooDeep = "This file nests packages more than 3 deep. It was not fully checked.";
    public const string PdfTooManyPages = "This PDF has more than 2,000 pages. It was not fully checked.";
    public const string PdfTooSlow = "This PDF took too long to check. It was not fully checked.";

    public static DocumentRead? Blocked(string path) => Blocked(path, new Budget(), 1);

    public static DocumentRead? Blocked(string path, Budget budget, int depth)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Inspect(stream, budget, depth);
            return null;
        }
        catch (PackageLimitException ex)
        {
            return new DocumentRead { Incomplete = true, Note = ex.Message };
        }
    }

    public static void Charge(Budget budget, long bytes)
    {
        budget.Entries++;
        if (budget.Entries > MaxEntries)
            throw new PackageLimitException(TooManyEntries);
        if (bytes < 0)
            bytes = 0;
        if (bytes > MaxEntryUncompressed)
            throw new PackageLimitException(EntryTooBig);
        if (bytes > MaxTotalUncompressed - budget.Total)
            throw new PackageLimitException(ExpandsTooFar);
        budget.Total += bytes;
    }

    public static void Inspect(Stream stream)
    {
        Inspect(stream, new Budget(), 1);
    }

    public static byte[] ReadEntry(ZipArchiveEntry entry, Budget budget)
    {
        budget.Entries++;
        if (budget.Entries > MaxEntries)
            throw new PackageLimitException(TooManyEntries);
        if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            return [];

        var declared = entry.Length;
        var compressed = entry.CompressedLength < 0 ? 0 : entry.CompressedLength;
        if (declared > MaxEntryUncompressed)
            throw new PackageLimitException(EntryTooBig);
        if (declared > MaxTotalUncompressed - budget.Total)
            throw new PackageLimitException(ExpandsTooFar);
        if (compressed > 0 && declared > compressed * MaxCompressionRatio)
            throw new PackageLimitException(RatioTooHigh);

        using var input = entry.Open();
        using var copy = new MemoryStream(declared > 0 && declared <= MaxEntryUncompressed ? (int)Math.Min(declared, 65_536) : 0);
        var buffer = new byte[8192];
        long read = 0;
        try
        {
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                read += n;
                if (read > MaxEntryUncompressed)
                    throw new PackageLimitException(EntryTooBig);
                if (budget.Total + read > MaxTotalUncompressed)
                    throw new PackageLimitException(ExpandsTooFar);
                if (compressed > 0 && read > compressed * MaxCompressionRatio)
                    throw new PackageLimitException(RatioTooHigh);
                copy.Write(buffer, 0, n);
            }
        }
        catch (PackageLimitException)
        {
            throw;
        }
        catch (InvalidDataException) when (compressed > 0 && read > compressed * MaxCompressionRatio)
        {
            throw new PackageLimitException(RatioTooHigh);
        }
        catch (InvalidDataException) when (read > MaxEntryUncompressed || budget.Total + read > MaxTotalUncompressed)
        {
            throw new PackageLimitException(read > MaxEntryUncompressed ? EntryTooBig : ExpandsTooFar);
        }

        budget.Total += read;
        return copy.ToArray();
    }

    private static void Inspect(Stream stream, Budget budget, int depth)
    {
        if (depth > MaxNestingDepth)
            throw new PackageLimitException(NestedTooDeep);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (budget.Entries + zip.Entries.Count > MaxEntries)
            throw new PackageLimitException(TooManyEntries);
        foreach (var entry in zip.Entries)
        {
            var data = ReadEntry(entry, budget);
            if (!IsZip(data))
                continue;
            using var nested = new MemoryStream(data, writable: false);
            Inspect(nested, budget, depth + 1);
        }
    }

    public static bool IsZip(byte[] data) =>
        data.Length > 3 && data[0] == (byte)'P' && data[1] == (byte)'K';

    public sealed class Budget
    {
        public int Entries { get; set; }
        public long Total { get; set; }
    }
}
