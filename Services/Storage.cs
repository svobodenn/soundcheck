using System.IO;
using Microsoft.Data.Sqlite;
using SoundCheck.Models;

namespace SoundCheck.Services;

public class Storage : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly string _dbPath;

    /// <summary>Absolute path to the SQLite file currently in use.</summary>
    public string DbPath => _dbPath;

    public Storage()
    {
        var dir = GetLibraryDir();
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "library.db");
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        _conn.Open();
        Init();
    }

    /// <summary>Default location: %AppData%\SoundCheck</summary>
    public static string DefaultLibraryDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundCheck");

    /// <summary>Bootstrap file lives at the default location and contains the
    /// override path (if user moved it). Read once at startup — must work
    /// before SQLite is even opened, so we can't store it in the DB.</summary>
    private static string BootstrapFile =>
        Path.Combine(DefaultLibraryDir, "library_path.txt");

    /// <summary>The directory the SQLite file actually lives in. Falls back
    /// to default if no override is set or the override path is unusable.</summary>
    public static string GetLibraryDir()
    {
        try
        {
            if (File.Exists(BootstrapFile))
            {
                var custom = File.ReadAllText(BootstrapFile).Trim();
                if (!string.IsNullOrEmpty(custom) && Directory.Exists(custom))
                    return custom;
            }
        }
        catch { /* fall through to default */ }
        return DefaultLibraryDir;
    }

    /// <summary>Persist a new library directory for next launch. Empty/null
    /// resets to default. The actual SQLite file is not moved here — caller
    /// is responsible for copying library.db into the new directory.</summary>
    public static void SetLibraryDirOverride(string? dir)
    {
        try
        {
            Directory.CreateDirectory(DefaultLibraryDir);
            if (string.IsNullOrWhiteSpace(dir) || dir == DefaultLibraryDir)
            {
                if (File.Exists(BootstrapFile)) File.Delete(BootstrapFile);
            }
            else
            {
                File.WriteAllText(BootstrapFile, dir);
            }
        }
        catch { /* user can re-pick from settings later */ }
    }

    private void Init()
    {
        Exec(@"
            CREATE TABLE IF NOT EXISTS tracks (
                path TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                artist TEXT NOT NULL,
                album TEXT,
                duration_secs INTEGER NOT NULL,
                added_at INTEGER NOT NULL,
                cover_blob BLOB
            );
            CREATE TABLE IF NOT EXISTS history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                track_path TEXT NOT NULL,
                title TEXT NOT NULL,
                artist TEXT NOT NULL,
                played_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_hist_at ON history(played_at DESC);
            CREATE TABLE IF NOT EXISTS stats (
                track_path TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                artist TEXT NOT NULL,
                plays INTEGER DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS playlists (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS playlist_tracks (
                playlist_id INTEGER NOT NULL,
                path TEXT NOT NULL,
                position INTEGER NOT NULL,
                PRIMARY KEY (playlist_id, path)
            );
            CREATE INDEX IF NOT EXISTS idx_pltracks ON playlist_tracks(playlist_id, position);");

        // Migrations for older schemas (silent if column already exists)
        TryExec("ALTER TABLE tracks ADD COLUMN album TEXT");
        TryExec("ALTER TABLE tracks ADD COLUMN cover_blob BLOB");
        TryExec("ALTER TABLE playlists ADD COLUMN cover_blob BLOB");
    }

    private void TryExec(string sql)
    {
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        catch { /* column already exists or other harmless error */ }
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long TsNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ── Tracks ──
    public List<StoredTrack> LoadTracks()
    {
        var out_ = new List<StoredTrack>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT path, title, artist, album, duration_secs, cover_blob FROM tracks ORDER BY added_at";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var path = r.GetString(0);
            // Keep moved/deleted files in the library (flagged as missing in the UI)
            // instead of silently dropping them — the user can see and remove them.
            byte[]? cover = r.IsDBNull(5) ? null : (byte[])r["cover_blob"];
            out_.Add(new StoredTrack
            {
                Path = path,
                Title = r.GetString(1),
                Artist = r.GetString(2),
                Album = r.IsDBNull(3) ? "" : r.GetString(3),
                DurationSecs = r.GetInt64(4),
                CoverBlob = cover,
            });
        }
        return out_;
    }

    public void SaveTrack(Track t)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT OR IGNORE INTO tracks
            (path, title, artist, album, duration_secs, added_at, cover_blob)
            VALUES ($p, $ti, $ar, $al, $d, $ts, $c)";
        cmd.Parameters.AddWithValue("$p", t.Path);
        cmd.Parameters.AddWithValue("$ti", t.Title);
        cmd.Parameters.AddWithValue("$ar", t.Artist);
        cmd.Parameters.AddWithValue("$al", (object?)t.Album ?? "");
        cmd.Parameters.AddWithValue("$d", (long)t.Duration.TotalSeconds);
        cmd.Parameters.AddWithValue("$ts", TsNow());
        cmd.Parameters.AddWithValue("$c", (object?)t.CoverBytes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteTrack(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tracks WHERE path = $p; DELETE FROM playlist_tracks WHERE path = $p;";
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Update a track's file path after the file was renamed/moved on disk.</summary>
    public void UpdateTrackPath(string oldPath, string newPath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE OR IGNORE tracks SET path = $new WHERE path = $old; " +
                          "UPDATE OR IGNORE playlist_tracks SET path = $new WHERE path = $old;";
        cmd.Parameters.AddWithValue("$new", newPath);
        cmd.Parameters.AddWithValue("$old", oldPath);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Rename a track's display title (library entry only — file tags untouched).</summary>
    public void UpdateTrackTitle(string path, string title)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE tracks SET title = $t WHERE path = $p";
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Update a track's title/artist/album in the library after editing its file tags.</summary>
    public void UpdateTrackMeta(string path, string title, string artist, string album)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE tracks SET title = $t, artist = $ar, album = $al WHERE path = $p";
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$ar", artist);
        cmd.Parameters.AddWithValue("$al", album);
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Update a track's cached cover art blob (null clears it).</summary>
    public void UpdateTrackCover(string path, byte[]? cover)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE tracks SET cover_blob = $c WHERE path = $p";
        cmd.Parameters.AddWithValue("$c", (object?)cover ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    public void ClearTracks() => Exec("DELETE FROM tracks; DELETE FROM playlist_tracks;");

    // ── Playlists ──
    public record PlaylistInfo(long Id, string Name, int Count, byte[]? Cover);

    public List<PlaylistInfo> LoadPlaylists()
    {
        var list = new List<PlaylistInfo>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT p.id, p.name, COUNT(pt.path), p.cover_blob
                            FROM playlists p
                            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
                            GROUP BY p.id, p.name, p.cover_blob
                            ORDER BY p.created_at";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            byte[]? cover = r.IsDBNull(3) ? null : (byte[])r["cover_blob"];
            list.Add(new PlaylistInfo(r.GetInt64(0), r.GetString(1), r.GetInt32(2), cover));
        }
        return list;
    }

    public void UpdatePlaylistCover(long id, byte[]? cover)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET cover_blob = $c WHERE id = $id";
        cmd.Parameters.AddWithValue("$c", (object?)cover ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public long CreatePlaylist(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO playlists (name, created_at) VALUES ($n, $ts); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$ts", TsNow());
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void RenamePlaylist(long id, string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET name = $n WHERE id = $id";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeletePlaylist(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = $id; DELETE FROM playlists WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Append a track to a playlist. Returns false if it was already present.</summary>
    public bool AddToPlaylist(long id, string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT OR IGNORE INTO playlist_tracks (playlist_id, path, position)
                            VALUES ($id, $p, COALESCE((SELECT MAX(position)+1 FROM playlist_tracks WHERE playlist_id = $id), 0))";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$p", path);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void RemoveFromPlaylist(long id, string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = $id AND path = $p";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Persist a new track order for a playlist (positions 0..n in the given order).</summary>
    public void ReorderPlaylist(long id, List<string> orderedPaths)
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE playlist_tracks SET position = $pos WHERE playlist_id = $id AND path = $p";
        var pPos = cmd.CreateParameter(); pPos.ParameterName = "$pos"; cmd.Parameters.Add(pPos);
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = id; cmd.Parameters.Add(pId);
        var pPath = cmd.CreateParameter(); pPath.ParameterName = "$p"; cmd.Parameters.Add(pPath);
        for (int i = 0; i < orderedPaths.Count; i++)
        {
            pPos.Value = i;
            pPath.Value = orderedPaths[i];
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public List<string> LoadPlaylistPaths(long id)
    {
        var list = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT path FROM playlist_tracks WHERE playlist_id = $id ORDER BY position";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Close the connection (clearing the pool so the file unlocks) and
    /// delete library.db plus any WAL/journal side files. The instance is unusable
    /// afterwards — the caller must restart the app to get a fresh database.</summary>
    public void CloseAndDelete()
    {
        CloseConnection();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            try { var f = _dbPath + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    /// <summary>Close the connection and release the OS file handle (pool) without deleting.
    /// The instance is unusable afterwards — caller restarts the app.</summary>
    public void CloseConnection()
    {
        try { _conn.Close(); } catch { }
        try { _conn.Dispose(); } catch { }
        // Microsoft.Data.Sqlite pools connections; clearing pools releases the OS file handle.
        try { SqliteConnection.ClearAllPools(); } catch { }
    }

    // ── History + Stats ──
    public void PushHistory(Track t)
    {
        // HTML pushHist: dedupe (drop prior entries for same track), then push to top
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM history WHERE track_path = $p";
            cmd.Parameters.AddWithValue("$p", t.Path);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"INSERT INTO history (track_path, title, artist, played_at)
                VALUES ($p, $ti, $ar, $ts)";
            cmd.Parameters.AddWithValue("$p", t.Path);
            cmd.Parameters.AddWithValue("$ti", t.Title);
            cmd.Parameters.AddWithValue("$ar", t.Artist);
            cmd.Parameters.AddWithValue("$ts", TsNow());
            cmd.ExecuteNonQuery();
        }
        // Trim to last 40
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"DELETE FROM history WHERE id NOT IN
                (SELECT id FROM history ORDER BY played_at DESC LIMIT 40)";
            cmd.ExecuteNonQuery();
        }
        // Increment stats
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"INSERT INTO stats (track_path, title, artist, plays)
                VALUES ($p, $ti, $ar, 1)
                ON CONFLICT(track_path) DO UPDATE SET
                    plays = plays + 1,
                    title = excluded.title,
                    artist = excluded.artist";
            cmd.Parameters.AddWithValue("$p", t.Path);
            cmd.Parameters.AddWithValue("$ti", t.Title);
            cmd.Parameters.AddWithValue("$ar", t.Artist);
            cmd.ExecuteNonQuery();
        }
    }

    public List<HistoryEntry> LoadHistory(int limit = 10)
    {
        var out_ = new List<HistoryEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT title, artist, played_at FROM history ORDER BY played_at DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            out_.Add(new HistoryEntry
            {
                Title = r.GetString(0),
                Artist = r.GetString(1),
                PlayedAt = r.GetInt64(2),
            });
        }
        return out_;
    }

    public List<TopTrack> LoadTopTracks(int limit = 5)
    {
        var out_ = new List<TopTrack>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT title, artist, plays FROM stats
            WHERE plays > 0 ORDER BY plays DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            out_.Add(new TopTrack
            {
                Title = r.GetString(0),
                Artist = r.GetString(1),
                Plays = r.GetInt64(2),
            });
        }
        return out_;
    }

    public long TotalPlays()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(plays), 0) FROM stats";
        var r = cmd.ExecuteScalar();
        return r is long l ? l : 0;
    }

    // HTML: const arts = Object.entries(ST.artistPlays).sort(...); arts[0]?.[0]
    public string TopArtist()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT artist, SUM(plays) AS total FROM stats
            WHERE artist <> '' AND artist <> 'Unknown Artist'
            GROUP BY artist ORDER BY total DESC LIMIT 1";
        using var r = cmd.ExecuteReader();
        return r.Read() ? r.GetString(0) : "—";
    }

    public void ResetStats()
    {
        Exec("DELETE FROM stats");
        Exec("DELETE FROM history");
    }

    // ── Aggregates for Profile charts ─────────────────────────────────────

    /// <summary>Top N artists by total plays (excluding "Unknown Artist").</summary>
    public List<(string Artist, long Plays)> LoadTopArtists(int limit = 5)
    {
        var list = new List<(string, long)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT artist, SUM(plays) AS total FROM stats
            WHERE artist <> '' AND artist <> 'Unknown Artist'
            GROUP BY artist ORDER BY total DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetInt64(1)));
        return list;
    }

    /// <summary>Returns plays-per-day for the last N days. Index 0 = today, length = days.</summary>
    public int[] LoadPlaysPerDay(int days = 30)
    {
        var result = new int[days];
        long now = TsNow();
        // Bucket history by floor(daysAgo)
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT played_at FROM history WHERE played_at >= $since";
        long since = now - (days * 86400L);
        cmd.Parameters.AddWithValue("$since", since);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long ts = r.GetInt64(0);
            int daysAgo = (int)((now - ts) / 86400L);
            if (daysAgo >= 0 && daysAgo < days) result[daysAgo]++;
        }
        return result;
    }

    /// <summary>Returns plays-per-hour-of-day aggregated over ALL history (length=24).</summary>
    public int[] LoadPlaysPerHour()
    {
        var result = new int[24];
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT played_at FROM history";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long ts = r.GetInt64(0);
            var dt = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
            int h = dt.Hour;
            if (h >= 0 && h < 24) result[h]++;
        }
        return result;
    }

    // ── Settings ──
    public string? GetSetting(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var r = cmd.ExecuteScalar();
        return r as string;
    }

    public void SetSetting(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO settings (key, value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public long TotalListenedSecs()
        => long.TryParse(GetSetting("total_secs") ?? "0", out var v) ? v : 0;

    public void SetTotalListened(long secs) => SetSetting("total_secs", secs.ToString());

    public void Dispose() => _conn.Dispose();

    public static string FmtAgo(long unixSecs)
    {
        var diff = TsNow() - unixSecs;
        bool en = Localization.Current == Localization.En;
        if (en)
        {
            if (diff < 30) return "just now";
            if (diff < 60) return $"{diff}s ago";
            if (diff < 3600) return $"{diff / 60}m ago";
            if (diff < 86400) return $"{diff / 3600}h ago";
            return $"{diff / 86400}d ago";
        }
        if (diff < 30) return "только что";
        if (diff < 60) return $"{diff}с назад";
        if (diff < 3600) return $"{diff / 60}м назад";
        if (diff < 86400) return $"{diff / 3600}ч назад";
        return $"{diff / 86400}д назад";
    }
}

public class StoredTrack
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public long DurationSecs { get; set; }
    public byte[]? CoverBlob { get; set; }
}

public class HistoryEntry
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public long PlayedAt { get; set; }
}

public class TopTrack
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public long Plays { get; set; }
}
