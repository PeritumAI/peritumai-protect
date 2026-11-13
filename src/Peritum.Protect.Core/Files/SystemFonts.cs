using System.Text;

namespace Peritum.Protect.Core.Files;

/// <summary>
/// Reads Windows system fonts. Nothing is copied into the repository.
/// </summary>
public static class SystemFonts
{
    public sealed class Face
    {
        public required string Path { get; init; }
        public required byte[] Bytes { get; init; }
        public required HashSet<int> Glyphs { get; init; }
    }

    public static IReadOnlyList<Face> Load()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string[] preferred =
        [
            "arial.ttf",
            "segoeui.ttf",
            "malgun.ttf",
            "YuGothR.ttf",
            "YuGothM.ttf",
            "msyh.ttc",
            "msyh.ttf",
            "simsun.ttc",
            "Nirmala.ttf",
            "segoeuihistoric.ttf"
        ];
        var faces = new List<Face>();
        foreach (var name in preferred)
        {
            var path = System.IO.Path.Combine(dir, name);
            if (!File.Exists(path))
                continue;
            try
            {
                var raw = File.ReadAllBytes(path);
                var bytes = ExtractFirst(raw);
                var glyphs = ReadCmap(bytes);
                if (glyphs.Count == 0)
                    continue;
                faces.Add(new Face { Path = path, Bytes = bytes, Glyphs = glyphs });
            }
            catch
            {
                // A font we cannot parse is skipped. Another face may cover the character.
            }
        }
        return faces;
    }

    public static Face? FaceFor(IReadOnlyList<Face> faces, char c)
    {
        var code = (int)c;
        foreach (var face in faces)
        {
            if (face.Glyphs.Contains(code))
                return face;
        }
        return null;
    }

    public static byte[] ExtractFirst(byte[] file)
    {
        if (file.Length < 12)
            return file;
        var tag = Encoding.ASCII.GetString(file, 0, 4);
        if (tag != "ttcf")
            return file;
        var count = ReadU32(file, 8);
        if (count < 1)
            return file;
        var fontOffset = ReadU32(file, 12);
        var numTables = ReadU16(file, fontOffset + 4);
        var tables = new List<(byte[] Tag, byte[] Data)>();
        for (var i = 0; i < numTables; i++)
        {
            var rec = fontOffset + 12 + (i * 16);
            var tableTag = file.AsSpan(rec, 4).ToArray();
            var offset = ReadU32(file, rec + 8);
            var length = ReadU32(file, rec + 12);
            var data = new byte[(length + 3) & ~3];
            Buffer.BlockCopy(file, offset, data, 0, length);
            tables.Add((tableTag, data));
        }

        var header = 12 + (numTables * 16);
        var output = new byte[header + tables.Sum(table => table.Data.Length)];
        output[0] = 0;
        output[1] = 1;
        output[2] = 0;
        output[3] = 0;
        WriteU16(output, 4, numTables);
        var search = HighestPowerOfTwo(numTables) * 16;
        WriteU16(output, 6, search);
        WriteU16(output, 8, (int)Math.Log2(Math.Max(1, search / 16)));
        WriteU16(output, 10, (numTables * 16) - search);
        var cursor = header;
        for (var i = 0; i < tables.Count; i++)
        {
            var rec = 12 + (i * 16);
            Buffer.BlockCopy(tables[i].Tag, 0, output, rec, 4);
            WriteU32(output, rec + 4, Checksum(tables[i].Data));
            WriteU32(output, rec + 8, cursor);
            var rawLength = tables[i].Data.Length;
            while (rawLength > 0 && tables[i].Data[rawLength - 1] == 0 && (rawLength % 4) != 0)
                rawLength--;
            WriteU32(output, rec + 12, tables[i].Data.Length == 0 ? 0 : LengthOf(file, fontOffset, i, numTables));
            Buffer.BlockCopy(tables[i].Data, 0, output, cursor, tables[i].Data.Length);
            cursor += tables[i].Data.Length;
        }
        FixHeadChecksum(output);
        return output;
    }

    private static int LengthOf(byte[] file, int fontOffset, int index, int numTables)
    {
        var rec = fontOffset + 12 + (index * 16);
        return ReadU32(file, rec + 12);
    }

    public static HashSet<int> ReadCmap(byte[] font)
    {
        var glyphs = new HashSet<int>();
        var tables = TableOffsets(font);
        if (!tables.TryGetValue("cmap", out var cmap))
            return glyphs;
        var count = ReadU16(font, cmap.Offset + 2);
        int? format12 = null;
        int? format4 = null;
        for (var i = 0; i < count; i++)
        {
            var rec = cmap.Offset + 4 + (i * 8);
            var formatOffset = cmap.Offset + ReadU32(font, rec + 4);
            var format = ReadU16(font, formatOffset);
            if (format == 12)
                format12 = formatOffset;
            else if (format == 4)
                format4 = formatOffset;
        }
        if (format12 is int f12)
            ReadFormat12(font, f12, glyphs);
        if (format4 is int f4)
            ReadFormat4(font, f4, glyphs);
        return glyphs;
    }

    private static void ReadFormat4(byte[] font, int offset, HashSet<int> glyphs)
    {
        var segCount = ReadU16(font, offset + 6) / 2;
        var end = offset + 14;
        var start = end + (2 * segCount) + 2;
        var delta = start + (2 * segCount);
        var range = delta + (2 * segCount);
        for (var i = 0; i < segCount; i++)
        {
            var endCode = ReadU16(font, end + (2 * i));
            var startCode = ReadU16(font, start + (2 * i));
            var idDelta = (short)ReadU16(font, delta + (2 * i));
            var idRange = ReadU16(font, range + (2 * i));
            if (endCode == 0xFFFF && startCode == 0xFFFF)
                break;
            for (var code = startCode; code <= endCode && code < 0xFFFF; code++)
            {
                int glyph;
                if (idRange == 0)
                    glyph = (code + idDelta) & 0xFFFF;
                else
                {
                    var glyphOffset = range + (2 * i) + idRange + (2 * (code - startCode));
                    if (glyphOffset + 1 >= font.Length)
                        continue;
                    glyph = ReadU16(font, glyphOffset);
                    if (glyph != 0)
                        glyph = (glyph + idDelta) & 0xFFFF;
                }
                if (glyph != 0)
                    glyphs.Add(code);
            }
        }
    }

    private static void ReadFormat12(byte[] font, int offset, HashSet<int> glyphs)
    {
        var groups = ReadU32(font, offset + 12);
        var cursor = offset + 16;
        for (var i = 0; i < groups; i++)
        {
            if (cursor + 12 > font.Length)
                break;
            var start = ReadU32(font, cursor);
            var end = ReadU32(font, cursor + 4);
            var glyph = ReadU32(font, cursor + 8);
            cursor += 12;
            if (glyph == 0 && start == end)
                continue;
            var limit = Math.Min(end, start + 200_000);
            for (var code = start; code <= limit; code++)
                glyphs.Add(code);
        }
    }

    private static Dictionary<string, (int Offset, int Length)> TableOffsets(byte[] font)
    {
        var map = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        if (font.Length < 12)
            return map;
        var numTables = ReadU16(font, 4);
        for (var i = 0; i < numTables; i++)
        {
            var rec = 12 + (i * 16);
            if (rec + 16 > font.Length)
                break;
            var tag = Encoding.ASCII.GetString(font, rec, 4);
            map[tag] = (ReadU32(font, rec + 8), ReadU32(font, rec + 12));
        }
        return map;
    }

    private static void FixHeadChecksum(byte[] font)
    {
        var tables = TableOffsets(font);
        if (!tables.TryGetValue("head", out var head) || head.Offset + 12 > font.Length)
            return;
        WriteU32(font, head.Offset + 8, 0);
        uint sum = 0;
        var length = font.Length & ~3;
        for (var i = 0; i < length; i += 4)
            sum += (uint)ReadU32(font, i);
        var adjustment = 0xB1B0AFBA - sum;
        WriteU32(font, head.Offset + 8, (int)adjustment);
    }

    private static int Checksum(byte[] data)
    {
        uint sum = 0;
        var length = data.Length & ~3;
        for (var i = 0; i < length; i += 4)
            sum += (uint)((data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]);
        return (int)sum;
    }

    private static int HighestPowerOfTwo(int value)
    {
        var power = 1;
        while (power * 2 <= value)
            power *= 2;
        return power;
    }

    private static int ReadU16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

    private static int ReadU32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static void WriteU16(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    private static void WriteU32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
}
