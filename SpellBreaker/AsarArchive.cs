using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpellBreaker;

/// <summary>
/// Native reader/writer for Electron .asar archives (Chromium Pickle header + JSON + file data).
/// Header layout (verified against the applications asar file):
///   [u32 4][u32 payloadSize][u32 payloadSize-4][u32 jsonLen][JSON][pad to 4][file data]
/// File data starts at offset 8 + payloadSize.
/// </summary>
public class AsarArchive
{
    private const int IntegrityBlockSize = 4194304; // 4 MB

    private readonly string _path;
    private readonly long _dataStart;
    private readonly JsonObject _root;

    public class Entry
    {
        public required string Path { get; init; }
        public required JsonObject Node { get; init; }
        public long Size => Node["size"]?.GetValue<long>() ?? 0;
        public long Offset => long.TryParse(Node["offset"]?.GetValue<string>(), out var o) ? o : 0;
        public bool Unpacked => Node["unpacked"]?.GetValue<bool>() ?? false;
        public bool IsLink => Node.ContainsKey("link");
    }

    private AsarArchive(string path, JsonObject root, long dataStart)
    {
        _path = path;
        _root = root;
        _dataStart = dataStart;
    }

    public static AsarArchive Open(string path)
    {
        using var fs = File.OpenRead(path);
        var hdr = new byte[16];
        fs.ReadExactly(hdr, 0, 16);
        var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(4));
        var innerSize = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(8));
        if (payloadSize != innerSize + 4)
            throw new InvalidDataException("Not an asar archive (bad pickle header).");
        var jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(12));
        var json = new byte[jsonLen];
        fs.ReadExactly(json, 0, json.Length);
        var root = JsonNode.Parse(json)!.AsObject();
        return new AsarArchive(path, root, 8L + payloadSize);
    }

    /// <summary>Flat list of all real file entries (skips directories, links, unpacked).</summary>
    public List<Entry> Files()
    {
        var list = new List<Entry>();
        Collect(_root["files"]!.AsObject(), "", list);
        return list;
    }

    private static void Collect(JsonObject dir, string prefix, List<Entry> list)
    {
        foreach (var kv in dir)
        {
            if (kv.Value is not JsonObject node) continue;
            var path = string.IsNullOrEmpty(prefix) ? kv.Key : prefix + "/" + kv.Key;
            if (node.ContainsKey("files"))
                Collect(node["files"]!.AsObject(), path, list);
            else
                list.Add(new Entry { Path = path, Node = node });
        }
    }

    public byte[] Read(Entry e)
    {
        using var fs = File.OpenRead(_path);
        var buf = new byte[e.Size];
        fs.Seek(_dataStart + e.Offset, SeekOrigin.Begin);
        fs.ReadExactly(buf, 0, buf.Length);
        return buf;
    }

    /// <summary>
    /// Writes a new asar to <paramref name="destPath"/>: entries in <paramref name="overrides"/>
    /// get new bytes (size/offset/integrity recomputed); all other file data copied verbatim.
    /// </summary>
    public void WriteModified(string destPath, IReadOnlyDictionary<string, byte[]> overrides,
        Action<int>? progress = null)
    {
        var entries = Files();
        using var src = File.OpenRead(_path);
        using var dst = File.Create(destPath);

        // assign offsets + integrity (keep original offset/size for copying)
        long cursor = 0;
        var work = new List<(Entry e, long origOffset, long origSize, byte[]? newBytes)>();
        foreach (var e in entries)
        {
            overrides.TryGetValue(e.Path, out var nb);
            if (e.Unpacked || e.IsLink) { work.Add((e, 0, 0, nb)); continue; }
            var origOffset = e.Offset;
            var origSize = e.Size;
            var size = nb?.Length ?? (int)origSize;
            e.Node["offset"] = cursor.ToString();
            e.Node["size"] = (long)size;
            if (nb != null) e.Node["integrity"] = ComputeIntegrity(nb);
            cursor += size;
            work.Add((e, origOffset, origSize, nb));
        }

        var json = Encoding.UTF8.GetBytes(_root.ToJsonString());
        var pad = (4 - (json.Length % 4)) % 4;
        uint inner = (uint)(4 + json.Length + pad);
        uint payload = inner + 4;
        var head = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(0), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), payload);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), inner);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), (uint)json.Length);
        dst.Write(head);
        dst.Write(json);
        dst.Write(new byte[pad]);

        var copyBuf = new byte[1024 * 1024];
        var total = cursor;
        long written = 0;
        foreach (var (e, origOffset, origSize, nb) in work)
        {
            if (e.Unpacked || e.IsLink) continue;
            if (nb != null) { dst.Write(nb); }
            else
            {
                src.Seek(_dataStart + origOffset, SeekOrigin.Begin);
                var remaining = (int)origSize;
                while (remaining > 0)
                {
                    var n = src.Read(copyBuf, 0, Math.Min(copyBuf.Length, remaining));
                    if (n <= 0) throw new EndOfStreamException($"Unexpected EOF reading {e.Path}");
                    dst.Write(copyBuf, 0, n);
                    remaining -= n;
                    if (total > 0) progress?.Invoke(
                        (int)Math.Min(99, (written + origSize - remaining) * 100 / total));
                }
            }
            written += nb?.Length ?? origSize;
            if (total > 0) progress?.Invoke((int)Math.Min(99, written * 100 / total));
        }
        progress?.Invoke(100);
    }

    /// <summary>Buffered file copy with byte-progress reporting (0-100).</summary>
    public static void CopyFile(string src, string dst, Action<int>? progress = null)
    {
        using var input = File.OpenRead(src);
        using var output = File.Create(dst);
        var buf = new byte[1024 * 1024];
        var total = input.Length;
        long done = 0;
        int n;
        while ((n = input.Read(buf, 0, buf.Length)) > 0)
        {
            output.Write(buf, 0, n);
            done += n;
            if (total > 0) progress?.Invoke((int)Math.Min(99, done * 100 / total));
        }
        progress?.Invoke(100);
    }

    private static JsonObject ComputeIntegrity(byte[] data)
    {
        var blocks = new JsonArray();
        for (var i = 0; i < data.Length; i += IntegrityBlockSize)
        {
            var len = Math.Min(IntegrityBlockSize, data.Length - i);
            blocks.Add(Convert.ToHexString(SHA256.HashData(data.AsSpan(i, len))).ToLowerInvariant());
        }
        if (data.Length == 0)
            blocks.Add(Convert.ToHexString(SHA256.HashData(ReadOnlySpan<byte>.Empty)).ToLowerInvariant());
        return new JsonObject
        {
            ["algorithm"] = "SHA256",
            ["hash"] = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
            ["blockSize"] = (long)IntegrityBlockSize,
            ["blocks"] = blocks,
        };
    }
}
