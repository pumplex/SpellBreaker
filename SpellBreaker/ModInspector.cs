using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpellBreaker;

/// <summary>Detected modification state of one installed target application version.</summary>
public class VersionStatus
{
    public required string Version { get; init; }
    public bool ExeModified { get; set; }
    public bool Pro { get; set; }
    public string ProMethod { get; set; } = "";
    public bool Promos { get; set; }
    public bool Overlay { get; set; }
    public bool Username { get; set; }
    public bool NoUpdate { get; set; }
    public bool Devtools { get; set; }
    public bool CssHide { get; set; }
    public bool HasBak { get; set; }
    public bool HasMeta { get; set; }
    public bool ModifiedAny =>
        ExeModified || Pro || Promos || Overlay || Username || NoUpdate || Devtools || CssHide;

    /// <summary>Tag labels in display order (empty when unmodified).</summary>
    public List<string> Tags()
    {
        var t = new List<string>();
        if (Pro) t.Add(string.IsNullOrEmpty(ProMethod) ? AppInfo.ProLabel : $"{AppInfo.ProLabel}:{ProMethod}");
        if (Promos) t.Add("Promos");
        if (Overlay) t.Add("Overlay");
        if (Username) t.Add("Username");
        if (NoUpdate) t.Add("NoUpdate");
        if (Devtools) t.Add("Devtools");
        if (CssHide) t.Add("UI");
        return t;
    }
}

/// <summary>
/// Scans the target application version dir for applied modifications: exe fuse flag
/// (sentinel-anchored), asar bundle/index.js markers, and the sb_meta.json metadata
/// written by newer runs (legacy sb_patch.json is also read).
/// </summary>
public static class ModInspector
{
    // Electron fuse: sentinel + \x01\t + 8 flag chars; '1' at idx4 = asar integrity ON
    private static readonly byte[] FuseSentinel =
        Encoding.ASCII.GetBytes("dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX").Append((byte)1).Append((byte)9).ToArray();

    public const string MetaFileName = "sb_meta.json";

    /// <summary>Scan one version; <paramref name="progress"/> (0-100) is optional.</summary>
    public static VersionStatus Inspect(TargetAppContext ctx, Action<int>? progress = null)
    {
        var st = new VersionStatus
        {
            Version = string.IsNullOrEmpty(ctx.LastVersion) ? "?" : ctx.LastVersion,
        };
        var asar = Path.Combine(ctx.ResourcesPath, "app.asar");
        var bak = asar + ".bak";
        st.HasBak = File.Exists(bak) || File.Exists(ctx.TargetAppExePath + ".bak");
        st.HasMeta = MetaPath(ctx) != null;

        progress?.Invoke(2);
        st.ExeModified = ExeIsModified(ctx.TargetAppExePath, p => progress?.Invoke(2 + p * 55 / 100));
        if (File.Exists(asar)) ScanAsar(asar, st, p => progress?.Invoke(57 + p * 38 / 100));
        MergeMeta(ctx, st);
        if (string.IsNullOrEmpty(ctx.AppAsarVersion) && File.Exists(ctx.AppAsarSource))
            ctx.AppAsarVersion = TargetAppLocator.ReadAsarVersion(ctx.AppAsarSource);
        progress?.Invoke(100);
        return st;
    }

    /// <summary>Path of the metadata file; null if absent.</summary>
    public static string? MetaPath(TargetAppContext ctx)
    {
        var cur = Path.Combine(ctx.ResourcesPath, MetaFileName);
        return File.Exists(cur) ? cur : null;
    }

    /// <summary>exe fuse: sentinel present + flags don't start '0'? -> flag[4]=='0' means integrity off = modified.</summary>
    public static bool ExeIsModified(string exePath) => ExeIsModified(exePath, null);

    /// <summary>Chunked sentinel search so callers get read progress across the ~150 MB exe.</summary>
    public static bool ExeIsModified(string exePath, Action<int>? progress)
    {
        try
        {
            using var fs = File.OpenRead(exePath);
            var total = fs.Length;
            var tail = Array.Empty<byte>();
            var chunk = new byte[8 * 1024 * 1024];
            long read = 0;
            int n;
            while ((n = fs.Read(chunk, 0, chunk.Length)) > 0)
            {
                var hay = new byte[tail.Length + n];
                Buffer.BlockCopy(tail, 0, hay, 0, tail.Length);
                Buffer.BlockCopy(chunk, 0, hay, tail.Length, n);
                var i = IndexOf(hay, FuseSentinel);
                if (i >= 0)
                {
                    var flagOff = i + FuseSentinel.Length;
                    if (flagOff + 8 <= hay.Length)
                    {
                        var flags = Encoding.ASCII.GetString(hay, flagOff, 8);
                        // untouched = "00001101" (integrity bit idx4='1'); modified flips it to '0'
                        return flags[4] == '0';
                    }
                }
                read += n;
                progress?.Invoke((int)Math.Min(99, read * 100 / total));
                var keep = FuseSentinel.Length - 1;
                if (n > keep)
                {
                    tail = new byte[keep];
                    Buffer.BlockCopy(chunk, n - keep, tail, 0, keep);
                }
                else tail = hay;
            }
            return false;
        }
        catch { return false; }
    }

    private static void ScanAsar(string asarPath, VersionStatus st, Action<int>? progress = null)
    {
        try
        {
            var asar = AsarArchive.Open(asarPath);
            var files = asar.Files();
            var scanned = 0;
            foreach (var e in files)
            {
                scanned++;
                progress?.Invoke(scanned * 100 / files.Count);
                var name = Path.GetFileName(e.Path);
                var isOverlay = name.StartsWith("overlay-", StringComparison.OrdinalIgnoreCase);
                var isApp = Regex.IsMatch(name, @"^app.*bundle\.js$", RegexOptions.IgnoreCase);
                var isIndex = name.Equals("index.js", StringComparison.OrdinalIgnoreCase);
                if (!isOverlay && !isApp && !isIndex) continue;
                var text = Encoding.UTF8.GetString(asar.Read(e));
                if (isIndex)
                {
                    if (text.Contains("isUpdaterAvailable(){return false}")) st.NoUpdate = true;
                    if (text.Contains("if(true){openDevTools()")) st.Devtools = true;
                    continue;
                }
                var pro = text.Contains("SpellBreaker") || text.Contains(AppInfo.Target + "Mod:") ||
                          text.Contains("$sbD") || text.Contains("t.subscription=o") ||
                          text.Contains("data.subscription=subscription") ||
                          text.Contains("response.subscription");
                if (pro)
                {
                    if (isOverlay) st.Overlay = true; else st.Pro = true;
                    if (string.IsNullOrEmpty(st.ProMethod))
                        st.ProMethod = text.Contains("$sbD") ? "Adaptive"
                            : text.Contains("data.subscription=subscription") ? $"{AppInfo.SakMethod}_v1.0.4"
                            : $"{AppInfo.SakMethod}_v1.0.7";
                }
                if (Regex.IsMatch(text, @"delete\s+\w+\.components\.appBanner|components\.appBanner\s*=\s*null"))
                    st.Promos = true;
                if (Regex.IsMatch(text, @"\w+\.username='[^']*'")) st.Username = true;
                if (text.Contains("remote-button{position:relative;display:none}") ||
                    text.Contains(".sections section.objectives{display:none}")) st.CssHide = true;
            }
        }
        catch { }
    }

    /// <summary>sb_meta.json (written by current SpellBreaker) provides exact applied-modification data.</summary>
    private static void MergeMeta(TargetAppContext ctx, VersionStatus st)
    {
        try
        {
            var path = MetaPath(ctx);
            if (path == null) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            if (r.TryGetProperty("proMethod", out var pm) && pm.ValueKind == JsonValueKind.String)
            {
                st.Pro = true;
                st.ProMethod = pm.GetString() ?? st.ProMethod;
            }
            if (r.TryGetProperty("overlay", out var ov) && ov.GetBoolean()) st.Overlay = true;
            if (r.TryGetProperty("promos", out var pr) && pr.GetBoolean()) st.Promos = true;
            if (r.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(un.GetString())) st.Username = true;
            if (r.TryGetProperty("updater", out var up) && up.ValueKind == JsonValueKind.String) st.NoUpdate = true;
            if (r.TryGetProperty("devtools", out var dv) && dv.ValueKind == JsonValueKind.String) st.Devtools = true;
            if (r.TryGetProperty("css", out var css) && css.GetBoolean()) st.CssHide = true;
            st.HasMeta = true;
        }
        catch { }
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (var i = 0; i <= hay.Length - needle.Length; i++)
        {
            var j = 0;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
