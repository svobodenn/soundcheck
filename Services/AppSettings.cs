namespace SoundCheck.Services;

/// <summary>
/// User-facing preferences. Values are stored as strings in the existing
/// `settings` SQLite table (via Storage.GetSetting/SetSetting). Each property
/// reads on demand and writes through; `Changed` fires on any update so the
/// UI can react in real time.
///
/// Categories (key prefix):
///   play.*   — playback (seek/volume step, remember position)
///   ui.*     — interface (particles, floating bg, logo eq)
///   sys.*    — system (autostart, close-to-tray, minimize-to-tray)
///
/// Defaults mirror the player's behavior before settings existed, so existing
/// users see no surprise after first launch.
/// </summary>
public static class AppSettings
{
    private static Storage? _storage;

    /// <summary>Fires whenever any setting changes — UI subscribes to live-update.</summary>
    public static event Action? Changed;

    public static void Init(Storage storage) => _storage = storage;

    // ── PLAYBACK ────────────────────────────────────────────────────────────
    public static bool RememberPosition
    {
        get => GetBool("play.remember", true);
        set => SetBool("play.remember", value);
    }
    /// <summary>Crossfade duration between tracks, in seconds. 0 = off (hard cut).</summary>
    public static int CrossfadeSeconds
    {
        get => GetInt("play.crossfade", 0);
        set => SetInt("play.crossfade", value);
    }
    /// <summary>true → the 10-band equalizer is applied to playback.</summary>
    public static bool EqualizerEnabled
    {
        get => GetBool("play.eq_on", false);
        set => SetBool("play.eq_on", value);
    }
    /// <summary>Equalizer band gains in dB, stored as ';'-joined invariant floats.</summary>
    public static float[] EqualizerBands
    {
        get
        {
            var s = _storage?.GetSetting("play.eq_bands");
            var result = new float[10];
            if (string.IsNullOrEmpty(s)) return result;
            var parts = s.Split(';');
            for (int i = 0; i < result.Length && i < parts.Length; i++)
                float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out result[i]);
            return result;
        }
        set
        {
            var s = string.Join(";", value.Select(g =>
                g.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)));
            _storage?.SetSetting("play.eq_bands", s);
            Changed?.Invoke();
        }
    }

    /// <summary>User-saved custom EQ preset (10 dB gains). Empty array if none saved.</summary>
    public static float[] EqCustomPreset
    {
        get
        {
            var s = _storage?.GetSetting("play.eq_custom");
            var result = new float[10];
            if (string.IsNullOrEmpty(s)) return result;
            var parts = s.Split(';');
            for (int i = 0; i < result.Length && i < parts.Length; i++)
                float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out result[i]);
            return result;
        }
        set
        {
            _storage?.SetSetting("play.eq_custom", string.Join(";", value.Select(g =>
                g.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))));
            // no Changed — recalled explicitly via the preset button
        }
    }
    public static bool HasEqCustomPreset => !string.IsNullOrEmpty(_storage?.GetSetting("play.eq_custom"));

    /// <summary>Write EQ enable + gains WITHOUT firing <see cref="Changed"/>, so live
    /// slider drags don't trigger the heavy full ApplySettings each tick.</summary>
    public static void PersistEqualizer(bool enabled, float[] gains)
    {
        _storage?.SetSetting("play.eq_on", enabled ? "1" : "0");
        _storage?.SetSetting("play.eq_bands", string.Join(";", gains.Select(g =>
            g.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))));
    }

    // ── INTERFACE ───────────────────────────────────────────────────────────
    public static bool ParticlesEnabled
    {
        get => GetBool("ui.particles", true);
        set => SetBool("ui.particles", value);
    }
    public static bool FloatingBgEnabled
    {
        get => GetBool("ui.floating_bg", true);
        set => SetBool("ui.floating_bg", value);
    }
    public static bool LogoEqualizerEnabled
    {
        get => GetBool("ui.logo_eq", true);
        set => SetBool("ui.logo_eq", value);
    }
    /// <summary>true → the accent color follows the current track's cover art;
    /// false → a fixed accent (see <see cref="AccentColor"/>) is used.</summary>
    public static bool AccentFromCover
    {
        get => GetBool("ui.accent_from_cover", true);
        set => SetBool("ui.accent_from_cover", value);
    }
    /// <summary>Manual accent color as <c>#RRGGBB</c>. Empty → default gold.
    /// Only used when <see cref="AccentFromCover"/> is off.</summary>
    public static string AccentColor
    {
        get => _storage?.GetSetting("ui.accent_color") ?? "";
        set { _storage?.SetSetting("ui.accent_color", value ?? ""); Changed?.Invoke(); }
    }
    /// <summary>true → large blurred cover art is shown behind the UI.</summary>
    public static bool BlurBgEnabled
    {
        get => GetBool("ui.blur_bg", true);
        set => SetBool("ui.blur_bg", value);
    }
    /// <summary>true → disables all ambient/decorative animations at once
    /// (particles, floating bg, logo equalizer, ripples, pulses, accent fade).</summary>
    public static bool ReduceMotion
    {
        get => GetBool("ui.reduce_motion", false);
        set => SetBool("ui.reduce_motion", value);
    }

    // ── LANGUAGE ────────────────────────────────────────────────────────────
    /// <summary>Two-letter code: "ru" or "en". Default "en".</summary>
    public static string Language
    {
        get
        {
            var s = _storage?.GetSetting("ui.lang");
            return string.IsNullOrEmpty(s) ? "en" : s;
        }
        set
        {
            _storage?.SetSetting("ui.lang", value);
            Changed?.Invoke();
        }
    }

    // ── SYSTEM ──────────────────────────────────────────────────────────────
    public static bool AutoStart
    {
        get => GetBool("sys.autostart", false);
        set => SetBool("sys.autostart", value);
    }
    /// <summary>true → ✕ hides to tray; false → ✕ quits the app.</summary>
    public static bool CloseToTray
    {
        get => GetBool("sys.close_to_tray", true);
        set => SetBool("sys.close_to_tray", value);
    }
    /// <summary>true → ─ minimizes to system tray; false → to the taskbar.</summary>
    public static bool MinimizeToTray
    {
        get => GetBool("sys.min_to_tray", false);
        set => SetBool("sys.min_to_tray", value);
    }

    // ── REMEMBER-POSITION storage (last track path + position seconds) ─────
    public static string LastTrackPath
    {
        get => _storage?.GetSetting("play.last_track_path") ?? "";
        set { _storage?.SetSetting("play.last_track_path", value); Changed?.Invoke(); }
    }
    public static double LastTrackPosition
    {
        get => double.TryParse(_storage?.GetSetting("play.last_track_pos"), out var v) ? v : 0;
        set { _storage?.SetSetting("play.last_track_pos", value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)); }
    }

    // ── RESET ──────────────────────────────────────────────────────────────
    public static void ResetCategory(string prefix)
    {
        // Setting a key to "" lets the GetXxx default come back through.
        foreach (var key in KeysWithPrefix(prefix))
            _storage?.SetSetting(key, "");
        Changed?.Invoke();
    }
    public static void ResetAll()
    {
        foreach (var p in new[] { "play.", "ui.", "sys." })
            foreach (var key in KeysWithPrefix(p))
                _storage?.SetSetting(key, "");
        Changed?.Invoke();
    }

    // ── helpers ────────────────────────────────────────────────────────────
    private static readonly string[] KnownKeys =
    {
        "play.remember", "play.crossfade", "play.eq_on", "play.eq_bands", "play.eq_custom",
        "ui.particles", "ui.floating_bg", "ui.logo_eq", "ui.lang",
        "ui.accent_from_cover", "ui.accent_color", "ui.blur_bg", "ui.reduce_motion",
        "sys.autostart", "sys.close_to_tray", "sys.min_to_tray",
    };
    private static IEnumerable<string> KeysWithPrefix(string prefix) =>
        KnownKeys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal));

    private static bool GetBool(string key, bool def)
    {
        var s = _storage?.GetSetting(key);
        if (string.IsNullOrEmpty(s)) return def;
        return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
    private static void SetBool(string key, bool value)
    {
        _storage?.SetSetting(key, value ? "1" : "0");
        Changed?.Invoke();
    }
    private static int GetInt(string key, int def)
    {
        var s = _storage?.GetSetting(key);
        return int.TryParse(s, out var v) ? v : def;
    }
    private static void SetInt(string key, int value)
    {
        _storage?.SetSetting(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Changed?.Invoke();
    }
}
