using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace MotorMod.Soundtrack
{
    /// <summary>
    /// The sea soundtrack. Lives on the plugin object for the whole game. Every player reports once a second whether they
    /// ride a bike on water, with the songs they have. The host (the dedicated server or the player hosting) groups the
    /// riders, runs one shared playlist (SoundtrackLogic.Director) over the songs most of them have, and sends everyone the
    /// state: song, start time on the game clock, paused, listeners. Each player plays that song at the right position,
    /// fades in when listed and on water, and out slowly (FadeOutSeconds) once the rider leaves the water. Songs are whatever audio files each player keeps in
    /// the music folder (MusicLibrary); the dedicated server needs none.
    /// </summary>
    public class SoundtrackManager : MonoBehaviour
    {
        public static SoundtrackManager Instance;
        private const string RpcReport = "MM_Soundtrack_Report", RpcState = "MM_Soundtrack_State", RpcSkip = "MM_Soundtrack_Skip";
        private const float FadeInSeconds = 2f, QuickFadeSeconds = 1.5f, ReportInterval = 1f, HostInterval = 0.5f, BroadcastInterval = 2f;
        private const double StaleAfter = 4.0, ForgetAfter = 15.0, DriftLimit = 0.3;

        internal static ConfigEntry<bool> Enabled, QuietGameMusic;
        internal static ConfigEntry<string> MusicFolder;
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<float> Volume, FadeOutSeconds, RadiusMeters, GraceSeconds, GapSeconds;

        public static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind("Soundtrack", "Enabled", true, "Hear the sea soundtrack (the ToggleKey flips this in game).");
            ToggleKey = config.Bind("Soundtrack", "ToggleKey", KeyCode.F7, "Key that switches the soundtrack off and on for you.");
            Volume = config.Bind("Soundtrack", "Volume", 1f, "Soundtrack loudness on top of the game's music slider (1 = balanced against the engines, up to 2 for louder).");
            MusicFolder = config.Bind("Soundtrack", "MusicFolder", "", "Folder with your songs (MP3, OGG, WAV). Empty = BepInEx/config/MotorMod/music. Friends who have the same files hear the same song.");
            QuietGameMusic = config.Bind("Soundtrack", "QuietGameMusic", true, "Hold the game's own music silent while the soundtrack is playing.");
            FadeOutSeconds = config.Bind("Soundtrack", "FadeOutSeconds", 30f, "Seconds the music takes to fade out after you leave the water (back on it sooner and it fades back in).");
            RadiusMeters = config.Bind("Soundtrack", "RadiusMeters", 500f, "Host: riders on water within this distance of each other share the music.");
            GraceSeconds = config.Bind("Soundtrack", "GraceSeconds", 30f, "Host: seconds off the water before the playlist pauses (back sooner and the same song carries on in time; keep it at least as long as FadeOutSeconds).");
            GapSeconds = config.Bind("Soundtrack", "GapSeconds", 3f, "Host: seconds of silence between songs.");
        }

        // ---------- this player's songs ----------
        private MusicLibrary m_library;
        private bool m_measuring;
        public string MusicFolderPath => m_library?.Folder;

        // ---------- session ----------
        private ZRoutedRpc m_rpc;            // the routed-RPC instance we registered with; every world session makes a new one

        // ---------- host ----------
        private Director m_director;
        private readonly Dictionary<long, RiderReport> m_reports = new Dictionary<long, RiderReport>();
        private HashSet<long> m_qualifying = new HashSet<long>();
        private HashSet<long> m_sentListeners = new HashSet<long>();
        private int m_sentVersion = -1;
        private float m_nextHostTick, m_nextBroadcast;

        // ---------- player: last state from the host ----------
        private string m_song;
        private double m_startTime, m_pausedOffset, m_songSeconds;
        private bool m_paused, m_haveState;
        private readonly HashSet<long> m_listeners = new HashSet<long>();
        private float m_nextReport, m_nextCatalog;
        private bool m_lastOnWater;

        // ---------- player: playback ----------
        private AudioSource m_source;
        private AudioClip m_clip;
        private string m_clipSong, m_loadingSong;
        private float m_gain = 1f, m_level, m_nextSeek;
        private bool m_pausedByGame;
        private int m_seeks;
        private static AccessTools.FieldRef<MusicMan, AudioSource> s_gameMusic;

        private void Awake()
        {
            Instance = this;
            string folder = MusicFolder != null && !string.IsNullOrWhiteSpace(MusicFolder.Value) ? MusicFolder.Value.Trim() : Path.Combine(BepInEx.Paths.ConfigPath, "MotorMod", "music");
            m_library = new MusicLibrary(folder);
            try { s_gameMusic = AccessTools.FieldRefAccess<MusicMan, AudioSource>("m_musicSource"); } catch { s_gameMusic = null; }
            if (!Application.isBatchMode) Rescan();                      // a dedicated server plays nothing and needs no songs
        }

        // ==================== this player's songs ====================
        /// <summary>Looks at the music folder again; new or changed songs are measured in the background.</summary>
        public void Rescan()
        {
            m_library.Scan();
            m_nextCatalog = 0f;                                          // tell the host about the songs soon
            if (m_library.Busy && !m_measuring) StartCoroutine(MeasureNewSongs());
        }

        private IEnumerator MeasureNewSongs()
        {
            m_measuring = true;
            yield return m_library.MeasureQueued();
            m_measuring = false; m_nextCatalog = 0f;
        }

        private string LibraryNote() => $"{m_library.Songs.Count} songs{(m_library.Pending > 0 ? $" ({m_library.Pending} still being measured)" : "")}{(m_library.Unreadable > 0 ? $", {m_library.Unreadable} unreadable" : "")}";

        // ==================== session and frame loop ====================
        private void Update()
        {
            var znet = ZNet.instance;
            if (znet == null || ZRoutedRpc.instance == null) { if (m_rpc != null) EndSession(); return; }
            if (ZRoutedRpc.instance != m_rpc) StartSession();
            if (znet.IsServer()) HostUpdate(znet);
            if (!znet.IsDedicated()) PlayerUpdate(znet);
        }

        private void StartSession()
        {
            EndSession();
            m_rpc = ZRoutedRpc.instance;
            m_rpc.Register<ZPackage>(RpcReport, RPC_Report);
            m_rpc.Register<ZPackage>(RpcState, RPC_State);
            m_rpc.Register<int>(RpcSkip, RPC_Skip);
            m_director = new Director(Environment.TickCount);
            m_reports.Clear(); m_qualifying.Clear(); m_sentListeners.Clear(); m_sentVersion = -1;
            m_haveState = false; m_song = null; m_listeners.Clear(); m_nextCatalog = 0f;
            if (!Application.isBatchMode && !m_measuring) Rescan();       // songs dropped in since the last world
        }

        private void EndSession()
        {
            m_rpc = null;
            StopPlayback();
        }

        private void LateUpdate()
        {
            // the game's own music stays silent while ours is audible (MusicMan sets its volume in Update; we follow it)
            if (QuietGameMusic == null || !QuietGameMusic.Value || s_gameMusic == null || MusicMan.instance == null) return;
            if (m_source != null && m_source.isPlaying && m_source.volume > 0.001f)
            {
                var src = s_gameMusic(MusicMan.instance);
                if (src != null) src.volume = 0f;
            }
        }

        // ==================== host ====================
        private void RPC_Report(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                pkg.ReadInt();                                   // format version
                if (!m_reports.TryGetValue(sender, out var r)) m_reports[sender] = r = new RiderReport { Peer = sender };
                r.OnWater = pkg.ReadBool(); r.X = pkg.ReadSingle(); r.Z = pkg.ReadSingle();
                if (pkg.ReadBool())
                {
                    int n = pkg.ReadInt(); var songs = new Dictionary<string, double>();
                    for (int i = 0; i < n; i++) { string name = pkg.ReadString(); double sec = pkg.ReadDouble(); songs[name] = sec; }
                    r.Songs = songs;
                }
                r.ReportedAt = ZNet.instance.GetTimeSeconds();
            }
            catch (Exception e) { MotorModPlugin.Log.LogWarning($"Soundtrack: bad report from {sender}: {e.Message}"); }
        }

        private void RPC_Skip(long sender, int unused)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || m_director == null) return;
            double now = ZNet.instance.GetTimeSeconds();
            if (m_director.Skip(now, m_qualifying, Catalogs())) MotorModPlugin.Log.LogInfo($"Soundtrack: skipped to '{m_director.Song}'");
            Broadcast();
        }

        private Dictionary<long, Dictionary<string, double>> Catalogs() => m_reports.ToDictionary(kv => kv.Key, kv => kv.Value.Songs);

        private void HostUpdate(ZNet znet)
        {
            if (Time.unscaledTime < m_nextHostTick || m_director == null) return;
            m_nextHostTick = Time.unscaledTime + HostInterval;
            double now = znet.GetTimeSeconds();
            foreach (var gone in m_reports.Where(kv => now - kv.Value.ReportedAt > ForgetAfter).Select(kv => kv.Key).ToList()) m_reports.Remove(gone);
            m_qualifying = Listeners.Qualifying(m_reports.Values, znet.GetNrOfPlayers(), RadiusMeters.Value, now, StaleAfter);
            m_director.GraceSeconds = GraceSeconds.Value; m_director.GapSeconds = GapSeconds.Value;
            string before = m_director.Song;
            if (m_director.Tick(now, m_qualifying, Catalogs()) && MotorModPlugin.Verbose)
                MotorModPlugin.Log.LogInfo($"Soundtrack host: {(m_director.Song != before ? $"now '{m_director.Song}'" : m_director.Paused ? $"paused at {m_director.PausedOffset:0.0} s" : $"resumed at {m_director.Position(now):0.0} s")}, listeners {m_qualifying.Count}");
            if (m_director.Version != m_sentVersion || !m_qualifying.SetEquals(m_sentListeners) || Time.unscaledTime >= m_nextBroadcast) Broadcast();
        }

        private void Broadcast()
        {
            if (m_rpc == null || m_director == null) return;
            var pkg = new ZPackage();
            pkg.Write(1);
            pkg.Write(m_director.Version);
            pkg.Write(m_director.Song ?? "");
            pkg.Write(m_director.StartTime);
            pkg.Write(m_director.Paused);
            pkg.Write(m_director.PausedOffset);
            pkg.Write(m_director.SongSeconds);
            pkg.Write(m_qualifying.Count);
            foreach (var peer in m_qualifying) pkg.Write(peer);
            m_rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcState, pkg);
            m_sentVersion = m_director.Version; m_sentListeners = new HashSet<long>(m_qualifying);
            m_nextBroadcast = Time.unscaledTime + BroadcastInterval;
        }

        // ==================== player ====================
        private void RPC_State(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || ZNet.instance.IsDedicated()) return;   // the dedicated server only runs the playlist
            try
            {
                pkg.ReadInt(); pkg.ReadInt();
                string song = pkg.ReadString();
                m_song = song.Length == 0 ? null : song;
                m_startTime = pkg.ReadDouble(); m_paused = pkg.ReadBool(); m_pausedOffset = pkg.ReadDouble(); m_songSeconds = pkg.ReadDouble();
                int n = pkg.ReadInt(); m_listeners.Clear();
                for (int i = 0; i < n; i++) m_listeners.Add(pkg.ReadLong());
                m_haveState = true;
                if (m_song != null && m_song != m_clipSong && m_song != m_loadingSong) StartCoroutine(Load(m_song));
            }
            catch (Exception e) { MotorModPlugin.Log.LogWarning("Soundtrack: bad state message: " + e.Message); }
        }

        /// <summary>True while the local player rides one of our bikes and that bike is on water.</summary>
        public static bool LocalRidingOnWater()
        {
            var p = Player.m_localPlayer;
            if (p == null) return false;
            var bike = p.GetDoodadController() as MotorcycleController;
            return bike != null && bike.IsOnWater;
        }

        private void PlayerUpdate(ZNet znet)
        {
            var player = Player.m_localPlayer;
            bool onWater = LocalRidingOnWater();
            if (player != null && (Time.unscaledTime >= m_nextReport || onWater != m_lastOnWater)) SendReport(player, onWater);
            m_lastOnWater = onWater;
            if (player != null && ZInput.GetKeyDown(ToggleKey.Value, false) && !Typing()) Toggle();
            UpdatePlayback(znet, onWater);
        }

        private void SendReport(Player player, bool onWater)
        {
            if (m_rpc == null) return;
            m_nextReport = Time.unscaledTime + ReportInterval;
            bool withCatalog = Time.unscaledTime >= m_nextCatalog;
            var pkg = new ZPackage();
            pkg.Write(1);
            pkg.Write(onWater);
            var pos = player.transform.position;
            pkg.Write(pos.x); pkg.Write(pos.z);
            pkg.Write(withCatalog);
            if (withCatalog)
            {
                var songs = m_library.Songs.Values.ToList();
                pkg.Write(songs.Count);
                foreach (var s in songs) { pkg.Write(s.Name); pkg.Write(s.Seconds); }
                m_nextCatalog = Time.unscaledTime + 10f;
            }
            m_rpc.InvokeRoutedRPC(RpcReport, pkg);          // to the host (handled right here when we are the host)
        }

        private static bool Typing() =>
            Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()) || TextInput.IsVisible() || Menu.IsVisible();

        public void Toggle()
        {
            Enabled.Value = !Enabled.Value;
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, Enabled.Value ? "Soundtrack on" : "Soundtrack off");
        }

        public void RequestSkip() { m_rpc?.InvokeRoutedRPC(RpcSkip, 0); }

        private void EnsureSource()
        {
            if (m_source != null) return;
            var go = new GameObject("MotorcycleSoundtrack");
            go.transform.SetParent(transform, false);
            m_source = go.AddComponent<AudioSource>();
            m_source.playOnAwake = false; m_source.loop = false; m_source.spatialBlend = 0f; m_source.priority = 0;
            m_source.bypassReverbZones = true; m_source.volume = 0f;
            if (MusicMan.instance != null && MusicMan.instance.m_musicMixer != null) m_source.outputAudioMixerGroup = MusicMan.instance.m_musicMixer;
        }

        private void UpdatePlayback(ZNet znet, bool onWater)
        {
            EnsureSource();
            if (m_source.outputAudioMixerGroup == null && MusicMan.instance != null) m_source.outputAudioMixerGroup = MusicMan.instance.m_musicMixer;
            // single player pause menu: the game clock stops, so the song stops too
            if (Game.IsPaused())
            {
                if (m_source.isPlaying) { m_source.Pause(); m_pausedByGame = true; }
                return;
            }
            if (m_pausedByGame) { m_pausedByGame = false; m_source.UnPause(); }

            bool ready = m_haveState && m_song != null && m_clip != null && m_clipSong == m_song;
            double pos = m_paused ? m_pausedOffset : znet.GetTimeSeconds() - m_startTime;
            bool inSong = ready && !m_paused && pos >= 0 && pos < m_clip.length - 0.05;
            bool listed = m_listeners.Contains(ZNet.GetUID());
            bool wanted = Enabled.Value && onWater && listed && inSong;

            float target = wanted ? 1f : 0f;
            bool leftWater = Enabled.Value && inSong && !onWater;      // the song runs on while the rider is off the water: a long fade
            float rate = target > m_level ? 1f / FadeInSeconds : 1f / (leftWater ? Mathf.Max(0.1f, FadeOutSeconds.Value) : QuickFadeSeconds);
            m_level = Mathf.MoveTowards(m_level, target, Time.unscaledDeltaTime * rate);

            if (inSong && (wanted || m_level > 0f))
            {
                if (m_source.clip != m_clip) { m_source.Stop(); m_source.clip = m_clip; }
                if (!m_source.isPlaying) { m_source.time = (float)pos; m_source.Play(); }
                else if (Math.Abs(m_source.time - pos) > DriftLimit && Time.unscaledTime >= m_nextSeek)
                {
                    m_source.time = (float)pos; m_nextSeek = Time.unscaledTime + 1f; m_seeks++;
                }
            }
            else if (m_level <= 0f && m_source.isPlaying) m_source.Stop();
            // the game turns music down linearly (master x music) but effects on a power curve (10*log10): follow the sliders on the
            // effects' curve so the music/engine balance does not move when the master slider does
            m_source.volume = Mathf.Clamp01(m_level * m_gain * Mathf.Clamp(Volume.Value, 0f, 2f) * Mathf.Sqrt(Mathf.Max(0f, MusicMan.m_masterMusicVolume)));
        }

        private IEnumerator Load(string name)
        {
            m_loadingSong = name;
            if (!m_library.Songs.TryGetValue(name, out var song))
            {
                MotorModPlugin.Log.LogInfo($"Soundtrack: the host plays '{name}', which this player doesn't have (silence until the next song)");
                m_loadingSong = null; yield break;
            }
            float t0 = Time.realtimeSinceStartup;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(song.File).AbsoluteUri, MusicLibrary.TypeOf(song.File)))
            {
                ((DownloadHandlerAudioClip)req.downloadHandler).compressed = true;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    MotorModPlugin.Log.LogWarning($"Soundtrack: cannot load {Path.GetFileName(song.File)}: {req.error}");
                    if (m_loadingSong == name) m_loadingSong = null;
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (m_loadingSong != name) { if (clip) Destroy(clip); yield break; }        // the host moved on meanwhile
                if (clip == null || clip.length <= 0f) { MotorModPlugin.Log.LogWarning($"Soundtrack: {Path.GetFileName(song.File)} decoded to nothing"); m_loadingSong = null; yield break; }
                if (m_source != null) { m_source.Stop(); m_source.clip = null; }
                if (m_clip != null) Destroy(m_clip);
                clip.name = name;
                m_clip = clip; m_clipSong = name; m_loadingSong = null;
                m_gain = Mathf.Pow(10f, song.GainDb / 20f);
                MotorModPlugin.Log.LogInfo($"Soundtrack: loaded '{name}' ({clip.length:0.0} s, gain {song.GainDb:+0.0;-0.0} dB) in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
            }
        }

        /// <summary>Harness check: every song in the music folder is measured, and decodes for playback to about its measured length.</summary>
        public IEnumerator CheckAllSongs(List<string> log, List<string> errors)
        {
            Rescan();
            float wait = Time.realtimeSinceStartup;
            while (m_library.Busy && Time.realtimeSinceStartup - wait < 180f) yield return null;
            log.Add($"songs: {LibraryNote()} in {m_library.Folder}");
            foreach (var song in m_library.Songs.Values.OrderBy(s => s.Name).ToList())
            {
                float t0 = Time.realtimeSinceStartup;
                using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(song.File).AbsoluteUri, MusicLibrary.TypeOf(song.File)))
                {
                    ((DownloadHandlerAudioClip)req.downloadHandler).compressed = true;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { errors.Add($"songs: '{song.Name}' does not load: {req.error}"); continue; }
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    float len = clip != null ? clip.length : 0f;
                    if (clip != null) Destroy(clip);
                    log.Add($"songs: '{song.Name}' plays {len:0.0} s (measured {song.Seconds:0.0} s, {song.Lufs:0.0} LUFS, gain {song.GainDb:+0.0;-0.0} dB) in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
                    // the game's streaming MP3 decoder estimates some files 1-3 s long; the 3 s gap between songs covers that
                    if (len <= 0f || Mathf.Abs(len - (float)song.Seconds) > 3f) errors.Add($"songs: '{song.Name}' plays {len:0.0} s but measured {song.Seconds:0.0} s");
                }
            }
        }

        private void StopPlayback()
        {
            if (m_source != null) { m_source.Stop(); m_source.clip = null; m_source.volume = 0f; }
            if (m_clip != null) { Destroy(m_clip); m_clip = null; }
            m_clipSong = null; m_loadingSong = null; m_level = 0f; m_pausedByGame = false;
        }

        // ==================== status (console, harness) ====================
        public bool Audible => m_source != null && m_source.isPlaying && m_level > 0.01f;   // playing and faded in (the player's own music slider may still be 0)
        public float SourceTime => m_source != null && m_source.isPlaying ? m_source.time : -1f;
        public string CurrentSong => m_song;
        public string ClipSong => m_clipSong;
        public bool HostPaused => m_paused;
        public float HostPausedOffset => (float)m_pausedOffset;
        public float Level => m_level;
        public float SourceVolume => m_source != null ? m_source.volume : 0f;

        public string Describe()
        {
            var znet = ZNet.instance;
            double now = znet != null ? znet.GetTimeSeconds() : 0;
            string host = znet != null && znet.IsServer() && m_director != null
                ? $"host: {(m_director.Song == null ? "nothing chosen" : $"'{m_director.Song}' {(m_director.Paused ? "paused" : "at")} {m_director.Position(now):0.0}/{m_director.SongSeconds:0} s")}, riders reporting {m_reports.Count}, listeners {m_qualifying.Count}. "
                : "";
            string me = $"me: music slider x master {MusicMan.m_masterMusicVolume:0.00}; songs {LibraryNote()}; state {(m_haveState ? $"'{m_song ?? "-"}' {(m_paused ? "paused" : "playing")}" : "none yet")}; " +
                        $"listed {m_listeners.Contains(ZNet.GetUID())}, riding on water {LocalRidingOnWater()}, enabled {Enabled.Value}; " +
                        $"clip {(m_clip != null ? $"'{m_clipSong}' {m_clip.length:0.0} s" : m_loadingSong != null ? "loading" : "none")}; " +
                        $"source {(m_source != null && m_source.isPlaying ? $"at {m_source.time:0.0} s" : "stopped")}, level {m_level:0.00}, volume {SourceVolume:0.000}, seeks {m_seeks}";
            return host + me;
        }
    }
}
