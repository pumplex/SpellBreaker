using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;

namespace SpellBreaker;

public static class AppInfo
{
    public const string Title = "Spell Breaker";
    /// <summary>OS window caption only — emoji can't render inside WPF surfaces, so Title stays plain.</summary>
    public const string WindowTitle = "🪄Spell Breaker";
    public const string Version = "1.0.0";

    // Target strings are stored "b64:"-prefixed
    // To override, just write plaintext — e.g. TargetApp = "Notepad"
    // works unchanged, since Decode() only touches b64:-prefixed values.
    public const string TargetApp = "b64:V2FuZA==";      // target application name
    public const string SakMethodId = "b64:U2FrMzIwMDk="; // modification method identifier
    public const string ProLabelId = "b64:UFJP";          // status tag label

    /// <summary>Decoded <see cref="TargetApp"/> — use this in place of the literal name.</summary>
    public static readonly string Target = Decode(TargetApp);
    /// <summary>Decoded <see cref="SakMethodId"/>.</summary>
    public static readonly string SakMethod = Decode(SakMethodId);
    /// <summary>Decoded <see cref="ProLabelId"/>.</summary>
    public static readonly string ProLabel = Decode(ProLabelId);

    /// <summary>"b64:..." → base64-decoded text; anything else → returned unchanged.</summary>
    public static string Decode(string s)
    {
        if (!s.StartsWith("b64:", StringComparison.Ordinal)) return s;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(s[4..])); }
        catch { return s; }
    }

    /// <summary>Replaces inline ~b64:...~ tokens (used inside lang_*.ini values).</summary>
    public static string DecodeTokens(string s)
    {
        while (true)
        {
            var i = s.IndexOf("~b64:", StringComparison.Ordinal);
            if (i < 0) return s;
            var j = s.IndexOf('~', i + 5);
            if (j < 0) return s;
            s = s[..i] + Decode(s[(i + 1)..j]) + s[(j + 1)..];
        }
    }
}

public enum SoundKind { Success, Failure }

/// <summary>
/// Plays the modify/restore outcome sounds selected in Options (SOUNDS section).
/// Embedded mp3s are extracted to a temp cache on first use; USER sounds play from their saved path.
/// </summary>
public static class SoundPlayer
{
    private static readonly string CacheDir =
        Path.Combine(Path.GetTempPath(), "SpellBreaker", "sounds");

    // keep players referenced until playback ends or they get GC'd mid-sound
    private static readonly List<MediaPlayer> Active = new();

    /// <summary>Fire-and-forget playback; silent no-op when disabled or the file can't be resolved.</summary>
    public static void Play(SoundKind kind)
    {
        var opt = App.Opts;
        if (!opt.EnableSounds) return;
        var sel = kind == SoundKind.Success ? opt.SoundSuccess : opt.SoundFailure;
        PlayFile(ResolvePath(kind, sel,
            kind == SoundKind.Success ? opt.SoundSuccessUser : opt.SoundFailureUser));
    }

    /// <summary>Preview an arbitrary selection (Options speaker button); ignores EnableSounds.</summary>
    public static void Preview(SoundKind kind, string sel, string userPath) =>
        PlayFile(ResolvePath(kind, sel, userPath));

    private static void PlayFile(string? path)
    {
        if (path == null) return;
        try
        {
            var player = new MediaPlayer();
            lock (Active) Active.Add(player);
            player.MediaEnded += (_, _) => { lock (Active) Active.Remove(player); };
            player.MediaFailed += (_, _) => { lock (Active) Active.Remove(player); };
            player.Open(new Uri(path, UriKind.Absolute));
            player.Play();
        }
        catch { }
    }

    private static string? ResolvePath(SoundKind kind, string sel, string userPath)
    {
        if (sel.Equals("USER", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(userPath) && File.Exists(userPath) ? userPath : null;
        if (sel is not ("1" or "2" or "3")) return null;
        var name = $"{(kind == SoundKind.Success ? "modified" : "failed")}{sel}.mp3";
        var dest = Path.Combine(CacheDir, name);
        if (File.Exists(dest)) return dest;
        try
        {
            Directory.CreateDirectory(CacheDir);
            var res = $"SpellBreaker.Resources.{name}";
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res);
            if (s == null) return null;
            using var fs = File.Create(dest);
            s.CopyTo(fs);
            return dest;
        }
        catch { return null; }
    }
}
