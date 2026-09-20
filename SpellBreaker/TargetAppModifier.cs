using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SpellBreaker;

public enum LogKind { Info, Ok, Fail, Title }

public class SelectorResult
{
    public bool EnablePro { get; set; } = true;
    public string ProMethod { get; set; } = AppInfo.SakMethod;      // Sak | Adaptive
    public string SakVersion { get; set; } = "1.0.7";        // 1.0.4 | 1.0.7
    public bool ChangeUsername { get; set; }
    public string CustomUsername { get; set; } = "";
    public bool DisableAutoUpdates { get; set; }
    public bool EnableDevtools { get; set; }

    // optional extras (persisted in Options.ini)
    public bool ModPromos { get; set; }                  // delete promo components
    public bool ModOverlay { get; set; }                 // also modify overlay-* bundles
}

/// <summary>Locations for the target application install being modified.</summary>
public class TargetAppContext
{
    public required string ResourcesPath { get; init; }
    public required string TargetAppExePath { get; init; }
    public required string TargetAppDataPath { get; init; }
    public string LastVersion { get; init; } = "";
    public string AppAsarVersion { get; set; } = "";

    // resolved live so a context reused after a restore never points at a consumed .bak
    public string AppAsarPath => Path.Combine(ResourcesPath, "app.asar");
    public string AppAsarBak => AppAsarPath + ".bak";
    public string AppAsarSource => File.Exists(AppAsarBak) ? AppAsarBak : AppAsarPath;
    public bool ModifiedBefore => File.Exists(AppAsarBak);
}

public partial class TargetAppModifier
{
    private readonly Localization _lang;
    private readonly TargetAppContext _ctx;
    private readonly SelectorResult _sel;

    public Action<string>? StatusText;                    // LANGBPT* progress texts
    public Action<string, LogKind>? LogLine;
    public Action<int>? ProgressChanged;                  // absolute percent 0-100

    private int _progress;
    private string _proMethodUsed = "";
    private readonly List<string> _proFiles = new();
    private string? _metaUpdater;        // original isUpdaterAvailable body (for sb_meta.json)
    private string? _metaDevtools;       // original devMode anchor (for sb_meta.json)
    private bool _cssModified;

    public TargetAppModifier(Localization lang, TargetAppContext ctx, SelectorResult sel)
    {
        _lang = lang; _ctx = ctx; _sel = sel;
    }

    private void Status(string key) => StatusText?.Invoke(_lang[key]);
    private void Log(string text, LogKind kind = LogKind.Info)
    {
        if (kind == LogKind.Fail) Failed = true;
        LogLine?.Invoke(text, kind);
    }
    private void AddProgress(int delta) => SetProgress(_progress + delta);
    private void SetProgress(int v)
    {
        v = Math.Min(v, 100);
        if (v == _progress) return;
        _progress = v;
        ProgressChanged?.Invoke(v);
    }

    /// <summary>Result of a modify run (restore is handled separately by TargetAppRestorer).</summary>
    public enum Outcome { Done, NothingSelected }

    /// <summary>True once any LogKind.Fail line was emitted (drives the outcome sound).</summary>
    public bool Failed { get; private set; }

    public Outcome Run()
    {
        SetProgress(1);
        var allNo = !_sel.EnablePro && !_sel.DisableAutoUpdates && !_sel.EnableDevtools;
        if (allNo) { AddProgress(80); Status("LANGBPT23"); return Outcome.NothingSelected; }

        try
        {
            ApplyExeIntegrity();
            if (!File.Exists(_ctx.AppAsarSource))
            {
                Log($" app.asar: {_lang["LANG04"]}", LogKind.Fail);
                Status("LANGBPT21");
                SetProgress(100);
                Status("LANGBPT23");
                return Outcome.Done;
            }
            var (bundles, indexJs, originals) = ExtractTargets();

            int proErr = 0, updErr = 0, devErr = 0;
            var indexModified = false;

            // PRO
            if (_sel.EnablePro)
            {
                AddProgress(10);
                proErr = RunProMod(bundles);
                if (proErr != 0)
                {
                    // batch aborts everything when all methods fail
                    Log($" {_lang["LANG37"]}", LogKind.Fail);
                    Status("LANGBPT21");
                    SetProgress(100);
                    Status("LANGBPT23");
                    return Outcome.Done;
                }
            }
            else AddProgress(30);

            // custom username 
            if (_sel.ChangeUsername && _proFiles.Count > 0)
                ApplyCustomUsername(bundles);

            // remote-button + objectives (best effort, all bundles)
            Status("LANGBPT14");
            ApplyCssMods(bundles);

            // auto-updates
            if (_sel.DisableAutoUpdates)
            {
                AddProgress(20);
                updErr = ModifyUpdater(ref indexJs);
                indexModified = updErr == 0;
            }
            else AddProgress(30);

            // devtools
            if (_sel.EnableDevtools)
            {
                AddProgress(20);
                devErr = ModifyDevtools(ref indexJs);
                indexModified = indexModified || devErr == 0;
            }
            else AddProgress(30);

            var err = proErr + updErr + devErr;
            if (err == 0)
            {
                Log($" {_lang["LANG42"]}", LogKind.Ok);
                var work = new Dictionary<string, byte[]>();
                if (indexModified) work["index.js"] = indexJs;
                foreach (var kv in bundles)
                    if (!ReferenceEquals(kv.Value, originals[kv.Key]))
                        work[kv.Key] = kv.Value;
                Pack(work);
                Log($" {_lang["LANG49"]}", LogKind.Title);
            }
            else
            {
                var key = err switch
                {
                    1 => "LANG43", 2 => "LANG44", 3 => "LANG45",
                    5 => "LANG46", 6 => "LANG47", 7 => "LANG48",
                    _ => "LANG37",
                };
                Log($" {_lang[key]}", LogKind.Fail);
            }
        }
        catch (Exception ex)
        {
            Log($" {ex.Message}", LogKind.Fail);
        }

        Status("LANGBPT21");
        SetProgress(100);
        Status("LANGBPT23");
        return Outcome.Done;
    }

    // ---------------------------------------------------------------- ASAR_INTEGRITY
    private void ApplyExeIntegrity()
    {
        Status("LANGBPT24");
        if (!File.Exists(_ctx.TargetAppExePath)) return;
        var exe = File.ReadAllBytes(_ctx.TargetAppExePath);
        var sentinel = Encoding.ASCII.GetBytes("dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX")
            .Append((byte)1).Append((byte)9).ToArray();
        var i = IndexOf(exe, sentinel);
        if (i < 0) return;
        var flagOff = i + sentinel.Length;
        if (flagOff + 8 > exe.Length || exe[flagOff + 4] != '1') return; // integrity bit already off
        Status("LANGBPT25");
        var bak = _ctx.TargetAppExePath + ".bak";
        if (!File.Exists(bak)) File.Move(_ctx.TargetAppExePath, bak);
        else File.Copy(_ctx.TargetAppExePath, bak, true);
        exe[flagOff + 4] = (byte)'0';
        File.WriteAllBytes(_ctx.TargetAppExePath, exe);
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

    // ---------------------------------------------------------------- EXTRACT
    private (Dictionary<string, byte[]> bundles, byte[] indexJs, Dictionary<string, byte[]> originals) ExtractTargets()
    {
        Status("LANGBPT10");
        var asar = AsarArchive.Open(_ctx.AppAsarSource);
        var bundles = new Dictionary<string, byte[]>();
        var originals = new Dictionary<string, byte[]>();
        byte[] indexJs = Array.Empty<byte>();
        foreach (var e in asar.Files())
        {
            var name = e.Path[(e.Path.LastIndexOf('/') + 1)..];
            var isApp = AppBundleRe().IsMatch(name);
            var isOverlay = _sel.ModOverlay && OverlayBundleRe().IsMatch(name);
            if (isApp || isOverlay)
            {
                var b = asar.Read(e);
                bundles[e.Path] = b;
                originals[e.Path] = b;
            }
            else if (name.Equals("index.js", StringComparison.OrdinalIgnoreCase)) indexJs = asar.Read(e);
        }
        return (bundles, indexJs, originals);
    }

    private static bool IsOverlayPath(string path) =>
        Path.GetFileName(path).StartsWith("overlay-", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- PRØ: method chain
    private int RunProMod(Dictionary<string, byte[]> bundles)
    {
        // ordered attempts: selection first, then every other method/version as fallback
        var tries = new List<(string method, string ver)>();
        if (_sel.ProMethod == "Adaptive")
        {
            tries.Add(("Adaptive", ""));
            tries.Add((AppInfo.SakMethod, "1.0.7"));
            tries.Add((AppInfo.SakMethod, "1.0.4"));
        }
        else
        {
            tries.Add((AppInfo.SakMethod, _sel.SakVersion));
            tries.Add((AppInfo.SakMethod, _sel.SakVersion == "1.0.7" ? "1.0.4" : "1.0.7"));
            tries.Add(("Adaptive", ""));
        }

        int err = 4;
        for (var i = 0; i < tries.Count; i++)
        {
            var (method, ver) = tries[i];
            if (i > 0)
                Log($" @{AppInfo.Title}: {_lang[method == "Adaptive" ? "LANG35" : "LANG36"]} {(method == AppInfo.SakMethod ? $"v{ver}" : "")}", LogKind.Info);
            err = method == "Adaptive" ? ApplyAdaptive(bundles) : ApplySak(bundles, ver);
            if (err == 0) break;
        }
        return err;
    }

    // ---------------------------------------------------------------- PRØ: Sak method
    private int ApplySak(Dictionary<string, byte[]> bundles, string ver)
    {
        Status("LANGBPT11");
        Log($" {_lang["LANG31"]} ({_lang["LANG34"]} v{ver}):", LogKind.Info);
        var anchor = "{return\"application/json\"===e.headers.get(\"Content-Type\")?await e.json():await e.text()}";
        var anchorBytes = Encoding.ASCII.GetBytes(anchor);
        var payload = Encoding.ASCII.GetBytes(LoadSakPayload(ver));
        var appModified = false;

        foreach (var (name, bytes) in bundles)
        {
            var isOverlay = IsOverlayPath(name);
            if (isOverlay && (!_sel.ModOverlay || _proFiles.Any(IsOverlayPath))) continue;
            if (!isOverlay && _proFiles.Any(p => !IsOverlayPath(p))) continue;
            if (!BinaryReplace.Contains(bytes, anchorBytes)) continue;

            var modified = BinaryReplace.ReplaceFirst(bytes, anchorBytes, payload);
            if (modified == null) continue;
            var ok = BinaryReplace.Contains(modified, Encoding.ASCII.GetBytes("SpellBreaker"));
            if (!ok) continue;

            bundles[name] = modified;
            _proFiles.Add(name);
            _proMethodUsed = $"{AppInfo.SakMethod}_v{ver}";
            if (!isOverlay) appModified = true;
        }

        if (!appModified) { Log($" {_lang["LANG22"]}", LogKind.Fail); AddProgress(1); return 4; }
        Log($" {_lang["LANG21"]}", LogKind.Ok);
        Status("LANGBPT12");
        Log($" {_lang["LANG32"]} ({_lang["LANG34"]} v{ver}):", LogKind.Info);
        Log($" {_lang["LANG21"]}", LogKind.Ok);
        AddProgress(3);
        return 0;
    }

    private string LoadSakPayload(string ver)
    {
        var res = $"SpellBreaker.Resources.payload_{ver.Replace(".", "")}.js";
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream(res)
            ?? throw new InvalidDataException($"Missing embedded resource {res}");
        using var r = new StreamReader(s, Encoding.UTF8);
        var text = r.ReadToEnd().Replace("\t", "").Replace("Gift_Sender", "SpellBreaker");
        return ApplySakExtras(text, ver);
    }

    /// <summary>Fold optional extras into a Sak payload at deterministic anchors (skipped if missing).</summary>
    private string ApplySakExtras(string payload, string ver)
    {
        string dataVar = ver == "1.0.4" ? "data" : "t";
        string retAnchor = ver == "1.0.4" ? "return data" : "return t";

        // insert before the LAST return — "return t" is also a substring of
        // "return this." inside the payload's date-class methods
        var i = payload.LastIndexOf(retAnchor, StringComparison.Ordinal);
        if (_sel.ModPromos && i >= 0)
            payload = payload[..i] + PromosDelete(dataVar) + payload[i..];
        return payload;
    }

    /// <summary>
    /// Promo-hiding snippet: deletes every known promotion slot from the response's
    /// `components` object, so it arrives as if the server sent none.
    /// </summary>
    private static string PromosDelete(string d)
    {
        var slots = new[]
        {
            "appBanner", "dialog", "notification", "sidebarCard", "sidebarIframeDialogCta",
            "featureAnnouncement", "proShowcaseFeature", "toast", "featureEducationCarousel",
            "productMarketingCarousel", "highlightsEducationCarousel",
        };
        var sb = new StringBuilder($"if({d}.components){{");
        foreach (var s in slots) sb.Append($"delete {d}.components.{s};");
        return sb.Append('}').ToString();
    }

    // ---------------------------------------------------------------- PRØ: Adaptive
    /// <summary>
    /// Regex-driven seek/detect/apply fallback. Locates the JSON response-decode
    /// statement in any app/overlay bundle tolerantly (variable name, quoting,
    /// comparison order, spacing) and injects a generated subscription modifier.
    /// </summary>
    private int ApplyAdaptive(Dictionary<string, byte[]> bundles)
    {
        Status("LANGBPT11");
        Log($" {_lang["LANG31"]} ({_lang["LANG33"]}):", LogKind.Info);
        var appModified = false;

        foreach (var (name, bytes) in bundles)
        {
            var isOverlay = IsOverlayPath(name);
            if (isOverlay && _proFiles.Any(IsOverlayPath)) continue;
            if (!isOverlay && _proFiles.Any(p => !IsOverlayPath(p))) continue;

            var text = Encoding.UTF8.GetString(bytes);
            string? modified = null;
            foreach (var re in AdaptivePatterns())
            {
                var m = re.Match(text);
                if (!m.Success) continue;
                var v = m.Groups["v"].Value;
                modified = text[..m.Index] + BuildAdaptivePayload(v) + text[(m.Index + m.Length)..];
                break;
            }
            if (modified == null) continue;
            if (!modified.Contains("$sbD.subscription=$sbS")) continue; // verification

            bundles[name] = Encoding.UTF8.GetBytes(modified);
            _proFiles.Add(name);
            _proMethodUsed = "Adaptive";
            if (!isOverlay) appModified = true;
        }

        if (!appModified) { Log($" {_lang["LANG22"]}", LogKind.Fail); AddProgress(1); return 4; }
        Log($" {_lang["LANG21"]}", LogKind.Ok);
        Status("LANGBPT12");
        Log($" {_lang["LANG32"]} ({_lang["LANG33"]}):", LogKind.Info);
        Log($" {_lang["LANG21"]}", LogKind.Ok);
        AddProgress(3);
        return 0;
    }

    /// <summary>Generated payload using collision-proof identifiers.</summary>
    private string BuildAdaptivePayload(string v)
    {
        var sb = new StringBuilder();
        sb.Append($"if(\"application/json\"==={v}.headers.get(\"Content-Type\")){{");
        sb.Append("var $sbD=await ").Append(v).Append(".json();");
        sb.Append("if(void 0!==$sbD.subscription){");
        sb.Append("class $sbC{constructor(){this.d=new Date((new Date).getFullYear())}" +
                  "plus(e,t){var i=Number(e);return\"year\"===t?this.d.setFullYear(this.d.getFullYear()+i)" +
                  ":\"day\"===t&&this.d.setDate(this.d.getDate()+i),this}" +
                  "minus(e,t){return this.plus(-1*e,t)}toISOString(){return this.d.toISOString()}}");
        sb.Append("var $sbI=(new $sbC).minus(1,\"day\"),$sbN=$sbI.toISOString(),$sbE=$sbI.plus(1,\"year\").toISOString(),");
        sb.Append("$sbS={startedAt:$sbN,endsAt:$sbE,period:\"yearly\",state:\"active\"," +
                  "nextInvoice:{date:$sbE,amount:0,currency:\"EUR\"},gift:{senderName:\"SpellBreaker\"}};");
        sb.Append("$sbD.subscription=$sbS;");
        sb.Append('}');
        if (_sel.ModPromos)
            sb.Append(PromosDelete("$sbD"));
        sb.Append("return $sbD}");
        sb.Append($"return await {v}.text()");
        return sb.ToString();
    }

    private static IEnumerable<Regex> AdaptivePatterns()
    {
        // decode statement: return "application/json"===v.headers.get("Content-Type")?await v.json():await v.text()
        const string ternary = @"\?\s*await\s+\k<v>\.json\(\)\s*:\s*await\s+\k<v>\.text\(\)";
        const string ct = @"[""'][Cc]ontent-[Tt]ype[""']";
        const string json = @"[""']application/json[""']";
        const string var = @"(?<v>[A-Za-z_$][\w$]*)";
        yield return new Regex($"return\\s*{json}\\s*={{2,3}}\\s*{var}\\.headers\\.get\\({ct}\\)\\s*{ternary}");
        yield return new Regex($"return\\s*{var}\\.headers\\.get\\({ct}\\)\\s*={{2,3}}\\s*{json}\\s*{ternary}");
    }

    // ---------------------------------------------------------------- CUSTOM USERNAME
    private void ApplyCustomUsername(Dictionary<string, byte[]> bundles)
    {
        Status("LANGBPT13");
        var user = _sel.CustomUsername.Replace("'", "");
        var (find, replace) = _proMethodUsed switch
        {
            "Adaptive" => ("$sbD.subscription=$sbS;",
                $"$sbD.subscription=$sbS;$sbD.username='{user}';"),
            var m when m.EndsWith("1.0.4") => ("data.subscription=subscription;",
                $"data.subscription=subscription;data.username='{user}';"),
            _ => ("t.subscription=o",
                $"t.subscription=o;t.username='{user}';"),
        };
        foreach (var file in _proFiles)
        {
            var text = Encoding.UTF8.GetString(bundles[file]);
            var idx = text.IndexOf(find, StringComparison.Ordinal);
            if (idx < 0) continue;
            text = text[..idx] + replace + text[(idx + find.Length)..];
            bundles[file] = Encoding.UTF8.GetBytes(text);
        }
    }

    // ---------------------------------------------------------------- CSS MODS
    private void ApplyCssMods(Dictionary<string, byte[]> bundles)
    {
        Status("LANGBPT15");
        foreach (var name in bundles.Keys.ToList())
        {
            var text = Encoding.UTF8.GetString(bundles[name]);
            var orig = text;
            text = text.Replace("remote-button{position:relative}",
                                "remote-button{position:relative;display:none}");
            if (text.Contains(".sections section.objectives{max-height:300px}"))
            {
                text = text.Replace(".sections section.objectives{max-height:300px}",
                                    ".sections section.objectives{display:none}")
                           .Replace(".sections section.announcements{",
                                    ".sections section.announcements{margin-top:0px;");
            }
            if (text != orig) { _cssModified = true; bundles[name] = Encoding.UTF8.GetBytes(text); }
        }
    }

    // ---------------------------------------------------------------- AUTO-UPDATES
    private int ModifyUpdater(ref byte[] indexJs)
    {
        Status("LANGBPT16");
        Log($" {_lang["LANG38"]}", LogKind.Info);
        var text = Encoding.UTF8.GetString(indexJs);
        var m = UpdaterRe().Match(text);
        if (!m.Success)
        {
            Log($" {_lang["LANG22"]}", LogKind.Fail);
            AddProgress(5);
            return 2;
        }
        Log($" {_lang["LANG21"]}", LogKind.Ok);
        _metaUpdater = m.Value; // original body recorded for exact restore
        text = ReplaceAt(text, m, "function isUpdaterAvailable(){return false}");
        indexJs = Encoding.UTF8.GetBytes(text);
        Status("LANGBPT17");
        Log($" {_lang["LANG39"]}", LogKind.Info);
        var ok = text.Contains("function isUpdaterAvailable(){return false}");
        Log(ok ? $" {_lang["LANG21"]}" : $" {_lang["LANG22"]}", ok ? LogKind.Ok : LogKind.Fail);
        AddProgress(5);
        return ok ? 0 : 2;
    }

    // ---------------------------------------------------------------- DEVTOOLS
    private int ModifyDevtools(ref byte[] indexJs)
    {
        Status("LANGBPT18");
        Log($" {_lang["LANG40"]}", LogKind.Info);
        var text = Encoding.UTF8.GetString(indexJs);
        var m = DevtoolsRe().Match(text);
        if (!m.Success)
        {
            Log($" {_lang["LANG22"]}", LogKind.Fail);
            AddProgress(5);
            return 1;
        }
        Log($" {_lang["LANG21"]}", LogKind.Ok);
        _metaDevtools = m.Value; // original anchor recorded for exact restore
        AddProgress(5);
        text = DevtoolsRe().Replace(text, "if(true){openDevTools()");
        indexJs = Encoding.UTF8.GetBytes(text);
        Status("LANGBPT19");
        Log($" {_lang["LANG41"]}", LogKind.Info);
        var ok = text.Contains("if(true){openDevTools()");
        Log(ok ? $" {_lang["LANG21"]}" : $" {_lang["LANG22"]}", ok ? LogKind.Ok : LogKind.Fail);
        AddProgress(5);
        return ok ? 0 : 1;
    }

    // ---------------------------------------------------------------- PACK
    private void Pack(Dictionary<string, byte[]> work)
    {
        Status("LANGBPT20");
        var original = File.ReadAllBytes(_ctx.AppAsarSource);
        var asar = AsarArchive.Open(_ctx.AppAsarSource);
        var tmp = Path.Combine(Path.GetTempPath(), $"spellbreaker_{Guid.NewGuid():N}.asar");
        try
        {
            var baseP = Math.Max(_progress, 80);
            asar.WriteModified(tmp, work,
                p => SetProgress(Math.Min(98, baseP + p * (98 - baseP) / 100)));
            var bak = Path.Combine(_ctx.ResourcesPath, "app.asar.bak");
            File.WriteAllBytes(bak, original);
            AsarArchive.CopyFile(tmp, Path.Combine(_ctx.ResourcesPath, "app.asar"),
                p => SetProgress(Math.Min(100, 98 + p * 2 / 100)));
            WriteModMeta();
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    /// <summary>Record applied modifications + captured originals so restores don't need .bak.</summary>
    private void WriteModMeta()
    {
        try
        {
            var meta = new Dictionary<string, object?>
            {
                ["proMethod"] = _proMethodUsed,
                ["promos"] = _sel.ModPromos,
                ["overlay"] = _sel.ModOverlay && _proFiles.Any(IsOverlayPath),
                ["username"] = _sel.ChangeUsername ? _sel.CustomUsername : null,
                ["updater"] = _metaUpdater,
                ["devtools"] = _metaDevtools,
                ["css"] = _cssModified,
                ["exe"] = true,
            };
            var json = System.Text.Json.JsonSerializer.Serialize(meta);
            File.WriteAllText(Path.Combine(_ctx.ResourcesPath, ModInspector.MetaFileName), json);
        }
        catch { }
    }

    private static string ReplaceAt(string text, Capture c, string replacement) =>
        text[..c.Index] + replacement + text[(c.Index + c.Length)..];

    // ---------------------------------------------------------------- regexes
    [GeneratedRegex(@"^app.*bundle\.js$", RegexOptions.IgnoreCase)]
    private static partial Regex AppBundleRe();
    [GeneratedRegex(@"^overlay-.*bundle\.js$", RegexOptions.IgnoreCase)]
    private static partial Regex OverlayBundleRe();

    [GeneratedRegex(@"function isUpdaterAvailable\(\)\{.*catch\{return ?(?:false|!0)\}\}")]
    private static partial Regex UpdaterRe();
    [GeneratedRegex(@"if\(.{1,3}?\.devMode\)\{openDevTools\(\)")]
    private static partial Regex DevtoolsRe();
}
