using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpellBreaker;

/// <summary>
/// Restores a target version to unmodified state. Prefers the exact .bak files when present;
/// otherwise reverses the known modification shapes in place (fuse flag, injected payloads,
/// username/promo/CSS insertions, updater/devtools) and repacks the asar.
/// </summary>
public partial class TargetAppRestorer
{
    private readonly Localization _lang;
    private readonly TargetAppContext _ctx;

    public Action<string>? StatusText;
    public Action<string, LogKind>? LogLine;
    public Action<int>? ProgressChanged;

    /// <summary>True once any LogKind.Fail line was emitted (drives the outcome sound).</summary>
    public bool Failed { get; private set; }

    private int _progress;

    public TargetAppRestorer(Localization lang, TargetAppContext ctx) { _lang = lang; _ctx = ctx; }

    private void Status(string key) => StatusText?.Invoke(_lang[key]);
    private void Log(string text, LogKind kind = LogKind.Info)
    {
        if (kind == LogKind.Fail) Failed = true;
        LogLine?.Invoke(text, kind);
    }
    private void SetProgress(int v)
    {
        if (v == _progress) return;
        _progress = v;
        ProgressChanged?.Invoke(v);
    }
    private void AddProgress(int d) => SetProgress(_progress + d);

    private static readonly byte[] FuseSentinel =
        Encoding.ASCII.GetBytes("dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX").Append((byte)1).Append((byte)9).ToArray();

    // byte-level markers — every injected shape carries one; clean files skip decode+regex entirely
    private static readonly byte[] MarkSubscription = Encoding.ASCII.GetBytes(".subscription");
    private static readonly byte[] MarkBrand = Encoding.ASCII.GetBytes("SpellBreaker");
    private static readonly byte[] MarkComponents = Encoding.ASCII.GetBytes(".components");
    private static readonly byte[] MarkCssHide = Encoding.ASCII.GetBytes("display:none");
    private static readonly byte[] MarkUpdater = Encoding.ASCII.GetBytes("isUpdaterAvailable(){return false}");
    private static readonly byte[] MarkDevtools = Encoding.ASCII.GetBytes("if(true){openDevTools()");

    public void Restore()
    {
        SetProgress(1);
        var asarPath = Path.Combine(_ctx.ResourcesPath, "app.asar");
        var asarBak = asarPath + ".bak";
        var exeBak = _ctx.TargetAppExePath + ".bak";

        try
        {
            Status("LANGBPT22");
            Log($" {_lang["LANG78"]}", LogKind.Title);
            var hasBaks = File.Exists(exeBak) || File.Exists(asarBak);
            if (!hasBaks) Log($" {_lang["LANG79"]}", LogKind.Title);

            // ---- target exe ---- (segment 1-21)
            if (File.Exists(exeBak))
            {
                if (File.Exists(_ctx.TargetAppExePath)) File.Delete(_ctx.TargetAppExePath);
                File.Move(exeBak, _ctx.TargetAppExePath);
                Log($" {AppInfo.Target}.exe: {_lang["LANG75"]}", LogKind.Ok);
            }
            else if (File.Exists(_ctx.TargetAppExePath) &&
                     RestoreExeFuse(_ctx.TargetAppExePath, p => SetProgress(1 + p * 20 / 100)))
                Log($" {AppInfo.Target}.exe: {_lang["LANG75"]}", LogKind.Ok);
            else
                Log($" {AppInfo.Target}.exe: {_lang["LANG76"]}", LogKind.Info);
            SetProgress(21);

            // ---- app.asar ---- (segment 21-90)
            if (File.Exists(asarBak))
            {
                if (File.Exists(asarPath)) File.Delete(asarPath);
                File.Move(asarBak, asarPath);
                Log($" app.asar: {_lang["LANG75"]}", LogKind.Ok);
            }
            else if (File.Exists(asarPath))
            {
                var changed = SyntheticRevert(asarPath, p => SetProgress(21 + p * 69 / 100));
                Log(changed ? $" app.asar: {_lang["LANG75"]}" : $" app.asar: {_lang["LANG76"]}",
                    changed ? LogKind.Ok : LogKind.Info);
            }
            else
            {
                Log($" app.asar: {_lang["LANG04"]}", LogKind.Fail);
            }
            SetProgress(90);

            // metadata file is stale after a restore
            var meta = Path.Combine(_ctx.ResourcesPath, ModInspector.MetaFileName);
            if (File.Exists(meta)) { try { File.Delete(meta); } catch { } }

            Log($" {_lang[hasBaks ? "LANG57" : "LANG80"]}", LogKind.Title);
        }
        catch (Exception ex)
        {
            Log($" {ex.Message}", LogKind.Fail);
        }

        Status("LANGBPT23");
        SetProgress(100);
    }

    /// <summary>
    /// Flip the Electron fuse flag back in place (sentinel-anchored). Streams the exe in
    /// 8 MB chunks — tail carryover covers a sentinel+flags pair straddling a boundary —
    /// and reports read progress instead of buffering ~150 MB at once.
    /// </summary>
    private static bool RestoreExeFuse(string exePath, Action<int>? progress)
    {
        using var fs = new FileStream(exePath, FileMode.Open, FileAccess.ReadWrite);
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
                    if (hay[flagOff + 4] != (byte)'0') return false; // not modified
                    fs.Seek(read - tail.Length + flagOff + 4, SeekOrigin.Begin);
                    fs.WriteByte((byte)'1');
                    progress?.Invoke(100);
                    return true;
                }
            }
            read += n;
            progress?.Invoke((int)Math.Min(99, read * 100 / total));
            var keep = FuseSentinel.Length + 8; // sentinel + flag bytes can straddle chunks
            tail = hay.Length > keep ? hay[^keep..] : hay;
        }
        return false;
    }

    /// <summary>
    /// Reverse all known modification shapes inside the asar; repacks when anything changed.
    /// progress: scan/reverse 0-50, repack 50-95, final copy 95-100.
    /// </summary>
    private bool SyntheticRevert(string asarPath, Action<int>? progress = null)
    {
        var meta = ReadMeta();
        var asar = AsarArchive.Open(asarPath);
        var work = new Dictionary<string, byte[]>();

        var files = asar.Files();
        for (var i = 0; i < files.Count; i++)
        {
            var e = files[i];
            var name = Path.GetFileName(e.Path);
            var isIndex = name.Equals("index.js", StringComparison.OrdinalIgnoreCase);
            var isJs = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
            if (isIndex || isJs)
            {
                var raw = asar.Read(e);
                var needIndex = isIndex && (BinaryReplace.Contains(raw, MarkUpdater) ||
                                            BinaryReplace.Contains(raw, MarkDevtools));
                var needPro = isJs && (BinaryReplace.Contains(raw, MarkSubscription) ||
                                       BinaryReplace.Contains(raw, MarkBrand));
                var needPromos = isJs && BinaryReplace.Contains(raw, MarkComponents);
                var needCss = isJs && BinaryReplace.Contains(raw, MarkCssHide);
                if (needIndex || needPro || needPromos || needCss)
                {
                    var text = Encoding.UTF8.GetString(raw);
                    var orig = text;
                    if (needPro) { text = RestoreProPayload(text); text = RestoreUsername(text); }
                    if (needPromos) text = RestorePromos(text);
                    if (needCss) text = RestoreCss(text);
                    if (needIndex) text = RestoreIndex(text, meta);
                    if (text != orig) work[e.Path] = Encoding.UTF8.GetBytes(text);
                }
            }
            progress?.Invoke(i * 50 / files.Count);
        }

        if (work.Count == 0) return false;

        var tmp = Path.Combine(Path.GetTempPath(), $"spellbreaker_{Guid.NewGuid():N}.asar");
        try
        {
            asar.WriteModified(tmp, work, p => progress?.Invoke(50 + p * 45 / 100));
            AsarArchive.CopyFile(tmp, asarPath, p => progress?.Invoke(95 + p * 5 / 100));
            return true;
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    /// <summary>
    /// Excise an injected response wrapper including its outer braces:
    /// `{if(cond){…}return await v.text()}` → `{return cond?await v.json():await v.text()}`.
    /// Payload markers (.subscription / SpellBreaker) locate candidate sites via IndexOf,
    /// then the wrapper regex runs on a ±16 KB window only — a full-text lazy match stalls
    /// for seconds on multi-MB bundles. `cond` is reused verbatim, so the reconstruction is
    /// byte-identical to the original for any variable name, quoting style or comparison order.
    /// </summary>
    private static string RestoreProPayload(string text)
    {
        if (!text.Contains(".subscription") && !text.Contains("SpellBreaker")) return text;
        var search = 0;
        while (search < text.Length)
        {
            var a = text.IndexOf(".subscription", search, StringComparison.Ordinal);
            var b = text.IndexOf("SpellBreaker", search, StringComparison.Ordinal);
            var h = a < 0 ? b : b < 0 ? a : Math.Min(a, b);
            if (h < 0) break;
            var wStart = Math.Max(0, h - 16384);
            var m = ProWrapperRe().Match(text, wStart, Math.Min(text.Length, h + 16384) - wStart);
            if (m.Success && IsInjectedWrapper(m))
            {
                var v = m.Groups["v"].Value;
                text = text[..m.Index]
                    + $"{{return{m.Groups["cond"].Value}?await {v}.json():await {v}.text()}}"
                    + text[(m.Index + m.Length)..];
                search = m.Index + 1;
            }
            else search = h + 1;
        }
        return text;
    }

    /// <summary>Only counts when `cond` is a Content-Type/JSON check and the body writes
    /// `.subscription` (every injected payload does) — natural code is never touched.</summary>
    private static bool IsInjectedWrapper(Match m)
    {
        var cond = m.Groups["cond"].Value;
        var body = m.Groups["body"].Value;
        return cond.Contains("application/json") && cond.Contains(".headers.get(")
            && cond.Contains("ontent-Type")
            && (body.Contains(".subscription") || body.Contains("SpellBreaker"));
    }

    /// <summary>
    /// Remove `;v.username='…'` — only when glued to a `.subscription=` write, so a
    /// legitimate username assignment in original code is never stripped.
    /// </summary>
    private static string RestoreUsername(string text) =>
        Regex.Replace(text,
            @"(\.subscription=[A-Za-z_$][\w$]*);[A-Za-z_$][\w$]*\.username='[^']*'", "$1;");

    /// <summary>
    /// Remove `if(v.components){delete v.components.X;…}` — only when every deleted
    /// slot is one of the 11 promotion slots the modifier inserts.
    /// </summary>
    private static string RestorePromos(string text) =>
        Regex.Replace(text,
            @"if\([A-Za-z_$][\w$]*\.components\)\{(?:delete [A-Za-z_$][\w$]*\.components\.(?:" +
            "appBanner|dialog|notification|sidebarCard|sidebarIframeDialogCta|featureAnnouncement|" +
            "proShowcaseFeature|toast|featureEducationCarousel|productMarketingCarousel|" +
            @"highlightsEducationCarousel);)+\}",
            "");

    /// <summary>Undo the remote-button/objectives CSS modifications.</summary>
    private static string RestoreCss(string text) =>
        text.Replace("remote-button{position:relative;display:none}", "remote-button{position:relative}")
            .Replace(".sections section.objectives{display:none}", ".sections section.objectives{max-height:300px}")
            .Replace(".sections section.announcements{margin-top:0px;", ".sections section.announcements{");

    /// <summary>
    /// Restore index.js updater + devtools. The true original can't be derived from the
    /// modified text, so resolve it in order: sb_meta.json → a sibling app-* install's
    /// index.js → a shaped fallback that still matches the modifier's own anchors
    /// (keeps re-modifying working even when the exact original is unknown).
    /// </summary>
    private string RestoreIndex(string text, JsonElement meta)
    {
        if (text.Contains("function isUpdaterAvailable(){return false}"))
        {
            var orig = MetaString(meta, "updater") ?? BorrowIndexOriginal("updater")
                ?? "function isUpdaterAvailable(){try{return!0}catch{return!0}}";
            text = text.Replace("function isUpdaterAvailable(){return false}", orig);
        }
        if (text.Contains("if(true){openDevTools()"))
        {
            var orig = MetaString(meta, "devtools") ?? BorrowIndexOriginal("devtools")
                ?? "if(e.devMode){openDevTools()";
            text = text.Replace("if(true){openDevTools()", orig);
        }
        return text;
    }

    /// <summary>
    /// Search sibling app-* installs for an unmodified index.js and capture the original
    /// updater/devtools anchor from it (index.js is usually identical across versions).
    /// </summary>
    private string? BorrowIndexOriginal(string key)
    {
        try
        {
            var root = Directory.GetParent(_ctx.ResourcesPath)?.Parent?.FullName;
            if (root == null) return null;
            var re = key == "updater" ? UpdaterOrigRe() : DevtoolsOrigRe();
            foreach (var dir in Directory.GetDirectories(root, "app-*"))
            {
                var res = Path.Combine(dir, "resources");
                if (res.Equals(_ctx.ResourcesPath, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var asarFile in new[]
                {
                    Path.Combine(res, "app.asar.bak"), // pristine copy first
                    Path.Combine(res, "app.asar"),
                })
                {
                    var indexJs = TryReadAsarFile(asarFile, "index.js");
                    if (indexJs == null) continue;
                    var m = re.Match(indexJs);
                    if (m.Success) return m.Value;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Read one named file out of an asar archive; null on any failure.</summary>
    private static string? TryReadAsarFile(string asarPath, string fileName)
    {
        try
        {
            if (!File.Exists(asarPath)) return null;
            var asar = AsarArchive.Open(asarPath);
            foreach (var e in asar.Files())
                if (Path.GetFileName(e.Path).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    return Encoding.UTF8.GetString(asar.Read(e));
        }
        catch { }
        return null;
    }

    [GeneratedRegex(@"\{\s*if\((?<cond>[\s\S]{15,200}?)\)\{(?<body>[\s\S]*?)\}\s*return await (?<v>[A-Za-z_$][\w$]*)\.text\(\)\s*;?\s*\}")]
    private static partial Regex ProWrapperRe();

    [GeneratedRegex(@"function isUpdaterAvailable\(\)\{.*catch\{return ?(?:false|!0)\}\}")]
    private static partial Regex UpdaterOrigRe();
    [GeneratedRegex(@"if\(.{1,3}?\.devMode\)\{openDevTools\(\)")]
    private static partial Regex DevtoolsOrigRe();

    private JsonElement ReadMeta()
    {
        try
        {
            var path = ModInspector.MetaPath(_ctx); 
            if (path != null)
                return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        }
        catch { }
        return default;
    }

    private static string? MetaString(JsonElement meta, string prop) =>
        meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(prop, out var v) &&
        v.ValueKind == JsonValueKind.String ? v.GetString() : null;

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
