using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CadSpace.Model;

namespace CadSpace.Dxf;

public sealed record DxfBytesResult(byte[] Bytes, ImmutableArray<string> Warnings);

/// <summary>ASCII/code-page and binary DXF transport, including pre-R13 byte-sized group codes.</summary>
public static class DxfBinary
{
    private static readonly byte[] Sentinel = Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\x1a\0");
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static DxfBinary() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static bool IsBinary(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Sentinel);

    public static DxfReadResult Read(byte[] bytes, string name = "Drawing.dxf")
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > DxfCodec.MaximumCharacters) throw new FormatException("DXF exceeds the 64 MiB input limit.");
        var encoding = DetectEncoding(bytes);
        var text = IsBinary(bytes) ? Decode(bytes, encoding) : encoding.GetString(bytes);
        var result = DxfCodec.Read(text.TrimStart('\uFEFF'), name);
        return result with { Source = result.Source with { OriginalBytes = bytes.ToArray() } };
    }

    public static DxfBytesResult Write(Drawing drawing, DxfSource? source = null, bool binary = false)
    {
        if (source?.OriginalBytes is { } original && drawing == source.Original && IsBinary(original) == binary)
            return new(original.ToArray(), []);
        var result = DxfCodec.Write(drawing, source);
        var warnings = result.Warnings;
        if (!binary) return new(new UTF8Encoding(false, true).GetBytes(result.Text), warnings);
        var pairs = DxfCodec.ParsePairs(result.Text);
        if (pairs.Any(p => p.Code == 999)) warnings = warnings.Add("Binary DXF excludes comment groups (999).");
        return new(Encode(pairs, new UTF8Encoding(false, true)), warnings);
    }

    public static ImmutableArray<DxfPair> ReadPairs(byte[] bytes)
    {
        if (!IsBinary(bytes)) throw new FormatException("The binary DXF sentinel is missing.");
        return DxfCodec.ParsePairs(Decode(bytes, DetectEncoding(bytes)));
    }

    public static byte[] Encode(IEnumerable<DxfPair> pairs, Encoding? encoding = null, bool legacyCodes = false)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, encoding ?? Encoding.UTF8, true);
        writer.Write(Sentinel);
        foreach (var pair in pairs)
        {
            if (pair.Code == 999) continue;
            var kind = ValueKind(pair.Code);
            if (legacyCodes) { if (pair.Code > 254) { writer.Write((byte)255); writer.Write((ushort)pair.Code); } else writer.Write((byte)pair.Code); }
            else writer.Write((ushort)pair.Code);
            switch (kind)
            {
                case 's':
                    if (pair.Value.Contains('\0')) throw new FormatException("DXF strings cannot contain NUL.");
                    writer.Write((encoding ?? Encoding.UTF8).GetBytes(pair.Value)); writer.Write((byte)0); break;
                case 'd':
                    if (!Geometry.GeometryMath.Number(pair.Value, out var number)) throw new FormatException("DXF contains a nonfinite or invalid floating-point value.");
                    writer.Write(number); break;
                case 'h': writer.Write(short.Parse(pair.Value, Invariant)); break;
                case 'i': writer.Write(int.Parse(pair.Value, Invariant)); break;
                case 'l': writer.Write(long.Parse(pair.Value, Invariant)); break;
                case 'b':
                    var boolean = byte.Parse(pair.Value, Invariant); if (boolean > 1) throw new FormatException("DXF booleans must be 0 or 1."); writer.Write(boolean); break;
                case 'x':
                    var chunk = Convert.FromHexString(pair.Value.Trim()); if (chunk.Length > 255) throw new FormatException("A binary DXF chunk cannot exceed 255 bytes.");
                    writer.Write((byte)chunk.Length); writer.Write(chunk); break;
            }
            if (stream.Length > DxfCodec.MaximumCharacters) throw new FormatException("DXF exceeds the 64 MiB output budget.");
        }
        writer.Flush(); return stream.ToArray();
    }

    private static string Decode(byte[] bytes, Encoding encoding)
    {
        // A file starts with group 0. Its high byte is zero in R13+ but 'S' (SECTION) in R12.
        if (bytes.Length < Sentinel.Length + 2) throw new FormatException("Truncated binary DXF.");
        var legacy = bytes[Sentinel.Length + 1] != 0;
        using var stream = new MemoryStream(bytes, false); stream.Position = Sentinel.Length;
        using var reader = new BinaryReader(stream, encoding, true);
        var text = new StringBuilder(); var eof = false;
        try
        {
            while (stream.Position < stream.Length)
            {
                var offset = stream.Position;
                int code;
                if (legacy) { code = reader.ReadByte(); if (code == 255) code = reader.ReadUInt16(); }
                else code = reader.ReadUInt16();
                string value;
                switch (ValueKind(code))
                {
                    case 's':
                        var start = (int)stream.Position; var end = Array.IndexOf(bytes, (byte)0, start);
                        if (end < 0) throw new EndOfStreamException();
                        value = encoding.GetString(bytes, start, end - start); stream.Position = end + 1; break;
                    case 'd': var d = reader.ReadDouble(); if (!double.IsFinite(d)) throw new FormatException($"Nonfinite DXF value at byte {offset}."); value = d.ToString("R", Invariant); break;
                    case 'h': value = reader.ReadInt16().ToString(Invariant); break;
                    case 'i': value = reader.ReadInt32().ToString(Invariant); break;
                    case 'l': value = reader.ReadInt64().ToString(Invariant); break;
                    case 'b': var b = reader.ReadByte(); if (b > 1) throw new FormatException($"Invalid boolean at byte {offset}."); value = b.ToString(Invariant); break;
                    case 'x': var size = reader.ReadByte(); var data = reader.ReadBytes(size); if (data.Length != size) throw new EndOfStreamException(); value = Convert.ToHexString(data); break;
                    default: throw new FormatException($"Invalid group code {code} at byte {offset}.");
                }
                text.Append(code).Append('\n').Append(value).Append('\n');
                if (text.Length > DxfCodec.MaximumCharacters) throw new FormatException("Decoded DXF exceeds the text budget.");
                if (code == 0 && value == "EOF") { eof = true; break; }
            }
        }
        catch (EndOfStreamException error) { throw new FormatException($"Truncated binary DXF at byte {stream.Position}.", error); }
        if (!eof) throw new FormatException("The binary DXF EOF record is missing.");
        if (stream.Position != stream.Length) throw new FormatException("Unexpected trailing bytes after binary DXF EOF.");
        return text.ToString();
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return new UTF8Encoding(false, true);
        var prefix = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 65536));
        var version = Regex.Match(prefix, @"AC10[0-9]{2}").Value;
        if (string.CompareOrdinal(version, "AC1021") >= 0) return new UTF8Encoding(false, true);
        var codePage = Regex.Match(prefix, @"(?:ANSI|DOS)_([0-9]{3,5})");
        if (codePage.Success && int.TryParse(codePage.Groups[1].Value, out var page))
        {
            try { return Encoding.GetEncoding(page, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
            catch (ArgumentException error) { throw new FormatException($"Unsupported DXF code page {page}.", error); }
        }
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static char ValueKind(int code) => code switch
    {
        >= 0 and <= 9 or 100 or 102 or 105 or >= 300 and <= 309 or >= 320 and <= 369 or >= 390 and <= 399 or >= 410 and <= 419 or >= 430 and <= 439 or >= 470 and <= 481 or 999 or >= 1000 and <= 1003 or >= 1005 and <= 1009 => 's',
        >= 10 and <= 59 or >= 110 and <= 149 or >= 210 and <= 239 or >= 460 and <= 469 or >= 1010 and <= 1059 => 'd',
        >= 60 and <= 79 or >= 170 and <= 179 or >= 270 and <= 289 or >= 370 and <= 389 or >= 400 and <= 409 or >= 1060 and <= 1070 => 'h',
        >= 90 and <= 99 or >= 420 and <= 429 or >= 440 and <= 459 or 1071 => 'i',
        >= 160 and <= 169 => 'l',
        >= 290 and <= 299 => 'b',
        >= 310 and <= 319 or 1004 => 'x',
        _ => throw new FormatException($"Unsupported binary group-code type {code}.")
    };
}
