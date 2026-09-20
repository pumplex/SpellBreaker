using System.IO;
using Microsoft.Win32;

namespace SpellBreaker;

/// <summary>
/// Reads/writes Options.ini keeping the exact KEY='VALUE' line format used by the batch version.
/// </summary>
public class Options
{
    public const string FileName = "Options.ini";

    public string ForceLanguage { get; set; } = "";      // FORCE_LANGUAGE_FILE: '' = AUTO
    public string ForceThemeColor { get; set; } = "";    // FORCE_THEME_COLOR: '' = AUTO
    public bool Existed { get; private set; }

    // sounds (ENABLE_SOUNDS default on; SOUND_* = '1'|'2'|'3'|'USER' + saved user paths)
    public bool EnableSounds { get; set; } = true;
    public string SoundSuccess { get; set; } = "1";
    public string SoundFailure { get; set; } = "1";
    public string SoundSuccessUser { get; set; } = "";
    public string SoundFailureUser { get; set; } = "";

    // install folder saved after a successful folder-picker selection
    public string TargetAppFolder { get; set; } = "";

    // optional extras (persisted toggles, off by default)
    public bool ModPromos { get; set; }                  // delete promotion components
    public bool ModOverlay { get; set; }                 // also modify overlay-* bundles

    // last selector choices (persisted for convenience)
    public string LastMethod { get; set; } = AppInfo.SakMethod;
    public string LastSakVersion { get; set; } = "1.0.7";
    public bool LastUsernameOn { get; set; }
    public string LastUsername { get; set; } = "";
    public bool LastNoUpdate { get; set; }
    public bool LastDevtools { get; set; }

    public static string IniPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

    public static Options Load()
    {
        var o = new Options();
        if (!File.Exists(IniPath)) return o;
        o.Existed = true;
        foreach (var raw in File.ReadAllLines(IniPath))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim().Trim('\'');
            switch (key)
            {
                case "FORCE_LANGUAGE_FILE": o.ForceLanguage = val; break;
                case "FORCE_THEME_COLOR": o.ForceThemeColor = val.ToUpperInvariant(); break;
                case "ENABLE_SOUNDS": o.EnableSounds = IsYes(val); break;
                case "SOUND_SUCCESS": o.SoundSuccess = val; break;
                case "SOUND_FAILURE": o.SoundFailure = val; break;
                case "SOUND_SUCCESS_USER": o.SoundSuccessUser = val; break;
                case "SOUND_FAILURE_USER": o.SoundFailureUser = val; break;
                case "TARGET_FOLDER": o.TargetAppFolder = val; break;
                case "MOD_PROMOS": o.ModPromos = IsYes(val); break;
                case "MOD_OVERLAY": o.ModOverlay = IsYes(val); break;
                case "LAST_METHOD": o.LastMethod = val; break;
                case "LAST_SAKVER": o.LastSakVersion = val; break;
                case "LAST_USERNAME_ON": o.LastUsernameOn = IsYes(val); break;
                case "LAST_USERNAME": o.LastUsername = val; break;
                case "LAST_NOUPDATE": o.LastNoUpdate = IsYes(val); break;
                case "LAST_DEVTOOLS": o.LastDevtools = IsYes(val); break;
            }
        }
        return o;
    }

    private static bool IsYes(string v) => v.Equals("YES", StringComparison.OrdinalIgnoreCase);

    /// <summary>Effective theme after AUTO resolution (registry AppsUseLightTheme).</summary>
    public string EffectiveTheme()
    {
        if (ForceThemeColor is "LIGHT" or "DARK") return ForceThemeColor;
        try
        {
            var v = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return v is int i && i == 0 ? "DARK" : "LIGHT";
        }
        catch { return "LIGHT"; }
    }

    /// <summary>
    /// Keys not already present are appended (new keys added by upgrades aren't dropped).
    /// Creates a default file when missing.
    /// </summary>
    public void Save()
    {
        var nl = Environment.NewLine;
        var kv = new (string key, string val)[]
        {
            ("FORCE_LANGUAGE_FILE", ForceLanguage),
            ("FORCE_THEME_COLOR", ForceThemeColor),
            ("ENABLE_SOUNDS", EnableSounds ? "YES" : "NO"),
            ("SOUND_SUCCESS", SoundSuccess),
            ("SOUND_FAILURE", SoundFailure),
            ("SOUND_SUCCESS_USER", SoundSuccessUser),
            ("SOUND_FAILURE_USER", SoundFailureUser),
            ("TARGET_FOLDER", TargetAppFolder),
            ("MOD_PROMOS", ModPromos ? "YES" : "NO"),
            ("MOD_OVERLAY", ModOverlay ? "YES" : "NO"),
            ("LAST_METHOD", LastMethod),
            ("LAST_SAKVER", LastSakVersion),
            ("LAST_USERNAME_ON", LastUsernameOn ? "YES" : "NO"),
            ("LAST_USERNAME", LastUsername),
            ("LAST_NOUPDATE", LastNoUpdate ? "YES" : "NO"),
            ("LAST_DEVTOOLS", LastDevtools ? "YES" : "NO"),
        };

        string content;
        if (File.Exists(IniPath))
        {
            var lines = File.ReadAllLines(IniPath).ToList();
            foreach (var (key, val) in kv)
            {
                var idx = lines.FindIndex(l =>
                    l.TrimStart().StartsWith(key + "=", StringComparison.Ordinal));
                var newLine = $"{key}='{val}'";
                if (idx >= 0)
                {
                    var indent = lines[idx][..(lines[idx].Length - lines[idx].TrimStart().Length)];
                    lines[idx] = indent + newLine;
                }
                else lines.Add(newLine);
            }
            content = string.Join(nl, lines);
        }
        else
        {
            content =
                "# SpellBreaker Options:" + nl +
                "[LANGUAGE]" + nl +
                $"FORCE_LANGUAGE_FILE='{ForceLanguage}'" + nl +
                "[THEME]" + nl +
                $"FORCE_THEME_COLOR='{ForceThemeColor}'" + nl +
                "[SOUNDS]" + nl +
                $"ENABLE_SOUNDS='{(EnableSounds ? "YES" : "NO")}'" + nl +
                $"SOUND_SUCCESS='{SoundSuccess}'" + nl +
                $"SOUND_FAILURE='{SoundFailure}'" + nl +
                $"SOUND_SUCCESS_USER='{SoundSuccessUser}'" + nl +
                $"SOUND_FAILURE_USER='{SoundFailureUser}'" + nl +
                "[TARGET]" + nl +
                $"TARGET_FOLDER='{TargetAppFolder}'" + nl +
                "[MODS]" + nl +
                $"MOD_PROMOS='{(ModPromos ? "YES" : "NO")}'" + nl +
                $"MOD_OVERLAY='{(ModOverlay ? "YES" : "NO")}'" + nl +
                "[LAST SELECTIONS]" + nl +
                $"LAST_METHOD='{LastMethod}'" + nl +
                $"LAST_SAKVER='{LastSakVersion}'" + nl +
                $"LAST_USERNAME_ON='{(LastUsernameOn ? "YES" : "NO")}'" + nl +
                $"LAST_USERNAME='{LastUsername}'" + nl +
                $"LAST_NOUPDATE='{(LastNoUpdate ? "YES" : "NO")}'" + nl +
                $"LAST_DEVTOOLS='{(LastDevtools ? "YES" : "NO")}'" + nl;
        }
        File.WriteAllText(IniPath, content);
    }
}
