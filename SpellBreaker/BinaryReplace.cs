using System.IO;

namespace SpellBreaker;

/// <summary>
/// Byte-level search &amp; replace, replacing the batch's binmay.exe usage.
/// </summary>
public static class BinaryReplace
{
    public static bool Contains(byte[] haystack, byte[] needle) => IndexOf(haystack, needle, 0) >= 0;

    public static int IndexOf(byte[] haystack, byte[] needle, int start = 0)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            var j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    /// <summary>Replace the first occurrence of needle. Returns null when not found.</summary>
    public static byte[]? ReplaceFirst(byte[] haystack, byte[] needle, byte[] replacement)
    {
        var i = IndexOf(haystack, needle);
        if (i < 0) return null;
        var outArr = new byte[haystack.Length - needle.Length + replacement.Length];
        Buffer.BlockCopy(haystack, 0, outArr, 0, i);
        Buffer.BlockCopy(replacement, 0, outArr, i, replacement.Length);
        Buffer.BlockCopy(haystack, i + needle.Length, outArr, i + replacement.Length,
            haystack.Length - i - needle.Length);
        return outArr;
    }

    /// <summary>Replace every occurrence of needle.</summary>
    public static byte[] ReplaceAll(byte[] haystack, byte[] needle, byte[] replacement)
    {
        var indices = new List<int>();
        var pos = 0;
        while (true)
        {
            var i = IndexOf(haystack, needle, pos);
            if (i < 0) break;
            indices.Add(i);
            pos = i + needle.Length;
        }
        if (indices.Count == 0) return haystack;
        using var ms = new MemoryStream();
        var cursor = 0;
        foreach (var i in indices)
        {
            ms.Write(haystack, cursor, i - cursor);
            ms.Write(replacement, 0, replacement.Length);
            cursor = i + needle.Length;
        }
        ms.Write(haystack, cursor, haystack.Length - cursor);
        return ms.ToArray();
    }
}
