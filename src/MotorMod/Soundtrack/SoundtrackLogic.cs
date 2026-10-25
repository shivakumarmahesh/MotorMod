using System;
using System.Collections.Generic;
using System.Linq;

// Plain playlist logic for the sea soundtrack, with no game or Unity types, so tests/SoundtrackTests can check it
// outside the game. The host owns one Director; the mod feeds it what the players report.
namespace MotorMod.Soundtrack
{
    /// <summary>What one player last told the host.</summary>
    public sealed class RiderReport
    {
        public long Peer;
        public bool OnWater;                 // riding one of our bikes, and that bike is on water
        public float X, Z;                   // position in metres (only meaningful while OnWater)
        public double ReportedAt;            // host clock, seconds
        public Dictionary<string, double> Songs = new Dictionary<string, double>();   // song name -> length in seconds
    }

    public static class Listeners
    {
        /// <summary>
        /// Who hears the soundtrack right now. Riders on water join up when they are within <paramref name="radius"/> of
        /// each other, chained (A near B near C is one group); every group of two or more listens. A rider alone on water
        /// listens only when they are the only player on the server. Reports older than <paramref name="staleAfter"/> are ignored.
        /// </summary>
        public static HashSet<long> Qualifying(IEnumerable<RiderReport> reports, int playersOnServer, float radius, double now, double staleAfter)
        {
            var riders = reports.Where(r => r != null && r.OnWater && now - r.ReportedAt <= staleAfter).ToList();
            var result = new HashSet<long>();
            if (riders.Count == 0) return result;
            if (playersOnServer <= 1)
            {
                foreach (var r in riders) result.Add(r.Peer);
                return result;
            }
            int n = riders.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            float r2 = radius * radius;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float dx = riders[i].X - riders[j].X, dz = riders[i].Z - riders[j].Z;
                    if (dx * dx + dz * dz <= r2) parent[Find(i)] = Find(j);
                }
            var size = new Dictionary<int, int>();
            for (int i = 0; i < n; i++) { int root = Find(i); size[root] = size.TryGetValue(root, out var s) ? s + 1 : 1; }
            for (int i = 0; i < n; i++) if (size[Find(i)] >= 2) result.Add(riders[i].Peer);
            return result;
        }
    }

    /// <summary>
    /// The server's one shared playlist. Plays while anyone qualifies; keeps going silently for the grace period after
    /// the last listener leaves the water, then pauses; resumes the same song where it stopped when anyone qualifies again.
    /// Songs: those held by the most listeners (a listener without any songs doesn't count), shuffled, each played once
    /// before any repeats, never the same song twice in a row.
    /// </summary>
    public sealed class Director
    {
        public double GraceSeconds = 5, GapSeconds = 3;

        public string Song;                  // current song name; null = nothing chosen
        public double SongSeconds;           // its length
        public double StartTime;             // host clock at the song's first second (while playing)
        public bool Paused;
        public double PausedOffset;          // song position when paused
        public double LastQualifiedAt = double.NegativeInfinity;
        public int Version;                  // bumps on every change, so players can ignore stale messages

        private readonly HashSet<string> m_played = new HashSet<string>();
        private readonly Random m_rng;

        public Director(int seed) { m_rng = new Random(seed); }

        /// <summary>Where the song is at host time <paramref name="now"/> (may pass its end during the gap).</summary>
        public double Position(double now) => Song == null ? 0 : Paused ? PausedOffset : now - StartTime;

        /// <summary>One step of the playlist. Returns true when the state changed and should be sent to everyone.</summary>
        public bool Tick(double now, ICollection<long> qualifying, IReadOnlyDictionary<long, Dictionary<string, double>> catalogs)
        {
            if (qualifying.Count > 0) LastQualifiedAt = now;
            bool active = now - LastQualifiedAt <= GraceSeconds;
            if (Song == null)
                return qualifying.Count > 0 && StartNext(now, qualifying, catalogs);
            if (Paused)
            {
                if (qualifying.Count == 0) return false;
                if (PausedOffset >= SongSeconds) return StartNext(now, qualifying, catalogs);
                Paused = false; StartTime = now - PausedOffset; Version++;
                return true;
            }
            if (!active)
            {
                Paused = true; PausedOffset = Math.Min(now - StartTime, SongSeconds); Version++;
                return true;
            }
            if (now - StartTime >= SongSeconds + GapSeconds)
                return StartNext(now, qualifying.Count > 0 ? qualifying : (ICollection<long>)catalogs.Keys.ToList(), catalogs);
            return false;
        }

        /// <summary>Test tool: move on to another song now.</summary>
        public bool Skip(double now, ICollection<long> qualifying, IReadOnlyDictionary<long, Dictionary<string, double>> catalogs)
            => StartNext(now, qualifying.Count > 0 ? qualifying : (ICollection<long>)catalogs.Keys.ToList(), catalogs);

        private bool StartNext(double now, ICollection<long> listeners, IReadOnlyDictionary<long, Dictionary<string, double>> catalogs)
        {
            var pick = PickNext(listeners, catalogs, out double seconds);
            if (pick == null)
            {
                if (Song == null) return false;
                Song = null; Paused = false; Version++;
                return true;
            }
            Song = pick; SongSeconds = seconds; StartTime = now; Paused = false; PausedOffset = 0; Version++;
            m_played.Add(pick);
            return true;
        }

        /// <summary>Next song: among the songs the most listeners hold, one not played yet in this round, never the current one.</summary>
        public string PickNext(ICollection<long> listeners, IReadOnlyDictionary<long, Dictionary<string, double>> catalogs, out double seconds)
        {
            seconds = 0;
            var holders = new Dictionary<string, int>();
            var length = new Dictionary<string, double>();
            foreach (var peer in listeners)
            {
                if (!catalogs.TryGetValue(peer, out var songs) || songs == null || songs.Count == 0) continue;
                foreach (var kv in songs)
                {
                    holders[kv.Key] = holders.TryGetValue(kv.Key, out var c) ? c + 1 : 1;
                    length[kv.Key] = length.TryGetValue(kv.Key, out var l) ? Math.Max(l, kv.Value) : kv.Value;
                }
            }
            if (holders.Count == 0) return null;
            int best = holders.Values.Max();
            var candidates = holders.Where(kv => kv.Value == best).Select(kv => kv.Key).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var fresh = candidates.Where(s => !m_played.Contains(s) && s != Song).ToList();
            if (fresh.Count == 0)
            {
                m_played.Clear();
                fresh = candidates.Where(s => s != Song).ToList();
                if (fresh.Count == 0) fresh = candidates;
            }
            var pick = fresh[m_rng.Next(fresh.Count)];
            seconds = length[pick];
            return pick;
        }
    }
}
