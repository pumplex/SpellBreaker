using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SpellBreaker;

/// <summary>
/// Loads embedded lang_*.ini translation files (KEY='value' format):
/// FORCE_LANGUAGE_FILE -> lang_&lt;locale&gt;.ini -> lang_&lt;lang&gt;.ini -> embedded English.
/// </summary>
public class Localization
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public string Language { get; private set; } = "English";
    public string BatchLang { get; private set; } = "en";

    public string this[string key] => _map.TryGetValue(key, out var v) ? v : key;

    public static Localization Load(string forceLanguage)
    {
        var loc = new Localization();
        var culture = CultureInfo.CurrentCulture; // e.g. en-US
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(forceLanguage))
            candidates.Add(forceLanguage);
        else
        {
            candidates.Add(culture.Name);                          // en-US
            var two = culture.Name.Split('-')[0];
            if (two != culture.Name) candidates.Add(two);          // en
        }

        foreach (var code in candidates)
        {
            if (loc.LoadEmbedded(code))
            {
                loc.BatchLang = code;
                return loc;
            }
        }
        loc.LoadEmbedded("en");
        loc.BatchLang = "en";
        return loc;
    }

    private bool LoadEmbedded(string code)
    {
        var name = $"SpellBreaker.lang.lang_{code}.ini";
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream(name);
        if (s == null) return false;
        using var r = new StreamReader(s, Encoding.UTF8);
        Parse(r.ReadToEnd());
        return true;
    }

    private void Parse(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var val = AppInfo.DecodeTokens(line[(eq + 1)..].Trim().Trim('\''));
            if (key == "language") Language = val;
            else if (key.StartsWith("LANG", StringComparison.Ordinal)) _map[key] = val;
        }
    }

    /// <summary>Language codes of the embedded lang_*.ini resources (e.g. "en", "zh-CN").</summary>
    public static List<string> AvailableLanguages()
    {
        var list = new List<string>();
        foreach (var res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            const string pre = "SpellBreaker.lang.lang_", suf = ".ini";
            if (res.StartsWith(pre, StringComparison.Ordinal) && res.EndsWith(suf, StringComparison.Ordinal))
                list.Add(res[pre.Length..^suf.Length]);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }
}
