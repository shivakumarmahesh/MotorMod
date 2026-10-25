using System;
using System.Collections.Generic;
using System.Linq;
using MotorMod.Soundtrack;

static class Program
{
    static int failed, passed;
    static void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL: " + what); } }

    static RiderReport R(long peer, bool water, float x, float z = 0, double at = 100) => new RiderReport { Peer = peer, OnWater = water, X = x, Z = z, ReportedAt = at };
    static Dictionary<string, double> Songs(params string[] names) => names.ToDictionary(n => n, n => 200.0);

    static int Main()
    {
        // ---------- who listens ----------
        var q = Listeners.Qualifying(new[] { R(1, true, 0) }, 1, 500, 100, 4);
        Check(q.SetEquals(new long[] { 1 }), "solo rider, only player on the server: listens");
        q = Listeners.Qualifying(new[] { R(1, true, 0), R(2, false, 10) }, 2, 500, 100, 4);
        Check(q.Count == 0, "solo rider with another player on the server (on land): nobody listens");
        q = Listeners.Qualifying(new[] { R(1, true, 0), R(2, true, 400) }, 2, 500, 100, 4);
        Check(q.SetEquals(new long[] { 1, 2 }), "two riders 400 m apart: both listen");
        q = Listeners.Qualifying(new[] { R(1, true, 0), R(2, true, 600) }, 2, 500, 100, 4);
        Check(q.Count == 0, "two riders 600 m apart: nobody listens");
        q = Listeners.Qualifying(new[] { R(1, true, 0), R(2, true, 400), R(3, true, 800) }, 3, 500, 100, 4);
        Check(q.SetEquals(new long[] { 1, 2, 3 }), "chain A-B-C 400 m steps (A-C 800 m): all three listen");
        q = Listeners.Qualifying(new[] { R(1, true, 0), R(2, true, 400), R(3, true, 5000), R(4, true, 5300, 0) }, 4, 500, 100, 4);
        Check(q.SetEquals(new long[] { 1, 2, 3, 4 }), "two separate pairs far apart: all four listen (one shared playlist)");
        q = Listeners.Qualifying(new[] { R(1, true, 0), R(2, true, 100, 0, 90) }, 2, 500, 100, 4);
        Check(q.Count == 0, "a stale report (10 s old) is ignored");
        q = Listeners.Qualifying(new[] { R(1, true, 0), R(2, true, 300, 400) }, 2, 500, 100, 4);
        Check(q.Count == 2, "distance is measured on the ground plane (300, 400) = 500 m: listens");

        // ---------- playlist ----------
        var all = new Dictionary<long, Dictionary<string, double>> { [1] = Songs("a", "b", "c", "d") };
        var one = new long[] { 1 };
        var none = new long[0];
        var d = new Director(7) { GraceSeconds = 5, GapSeconds = 3 };
        Check(!d.Tick(0, none, all) && d.Song == null, "nobody on water: nothing starts");
        Check(d.Tick(10, one, all) && d.Song != null && d.StartTime == 10, "first rider on water: a song starts now");
        string first = d.Song;
        Check(!d.Tick(13, none, all) && !d.Paused && d.Song == first, "3 s off the water (inside the grace period): same song keeps going");
        Check(!d.Tick(14, one, all) && d.Song == first && Math.Abs(d.Position(14) - 4) < 1e-9, "back on water within the grace: same song, position continued");
        Check(!d.Tick(18, none, all) && !d.Paused, "4 s gone: still inside the grace");
        Check(d.Tick(19.5, none, all) && d.Paused && Math.Abs(d.PausedOffset - 9.5) < 1e-9, "grace over: paused at the current position");
        Check(!d.Tick(60, none, all) && d.Paused, "still nobody: stays paused");
        Check(d.Tick(100, one, all) && !d.Paused && d.Song == first && Math.Abs(d.Position(100) - 9.5) < 1e-9, "someone back on water: the same song resumes where it paused");
        Check(Math.Abs(d.Position(130) - 39.5) < 1e-9, "a late joiner computes the right position from the start time");
        double end = d.StartTime + d.SongSeconds;
        Check(!d.Tick(end + 2.9, one, all) && d.Song == first, "song over: 3 s gap before the next");
        Check(d.Tick(end + 3.0, one, all) && d.Song != first && d.StartTime == end + 3.0, "after the gap: a different song starts");

        // shuffle: every song once before any repeats, never twice in a row
        var d2 = new Director(42);
        var seen = new List<string>();
        double t = 0;
        d2.Tick(t, one, all); seen.Add(d2.Song);
        for (int i = 0; i < 11; i++) { t = d2.StartTime + d2.SongSeconds + d2.GapSeconds; d2.Tick(t, one, all); seen.Add(d2.Song); }
        Check(seen.Take(4).Distinct().Count() == 4 && seen.Skip(4).Take(4).Distinct().Count() == 4 && seen.Skip(8).Take(4).Distinct().Count() == 4,
            "shuffle: each round of 4 plays all 4 songs: " + string.Join(",", seen));
        Check(Enumerable.Range(1, seen.Count - 1).All(i => seen[i] != seen[i - 1]), "never the same song twice in a row");

        // only songs everyone listening has; a listener with no songs doesn't block
        var mixed = new Dictionary<long, Dictionary<string, double>> { [1] = Songs("a", "b", "c"), [2] = Songs("a", "b"), [3] = Songs() };
        var d3 = new Director(1);
        var picks = new HashSet<string>();
        for (int i = 0; i < 200; i++) { var p = d3.PickNext(new long[] { 1, 2, 3 }, mixed, out _); picks.Add(p); }
        Check(picks.SetEquals(new[] { "a", "b" }), "picks only songs both listeners have: " + string.Join(",", picks));
        var d4 = new Director(1);
        Check(!d4.Tick(0, new long[] { 3 }, mixed) && d4.Song == null, "a listener with no songs alone: nothing plays");

        // the song ends while nobody listens (inside the grace): stops instead of picking for nobody
        var d5 = new Director(3) { GraceSeconds = 5, GapSeconds = 3 };
        d5.Tick(0, one, all);
        double len = d5.SongSeconds;
        d5.Tick(len - 1, one, all);
        Check(d5.Tick(len + 3, none, all) && d5.Song != null, "song over with nobody listening (in grace): the next one is chosen from everyone's songs");
        // paused after the end: resume picks the next song
        var d6 = new Director(5) { GraceSeconds = 5, GapSeconds = 3 };
        d6.Tick(0, one, all); string s6 = d6.Song; double l6 = d6.SongSeconds;
        d6.Tick(l6 - 2, one, all);
        d6.Tick(l6 + 4, none, all);  // grace over at l6+3 -> pause, offset clamped to the song length
        Check(d6.Paused && d6.PausedOffset <= l6, "paused at or before the song's end");
        Check(d6.Tick(500, one, all) && !d6.Paused, "resume after a pause at the end: playing again");

        // skip
        var d7 = new Director(9);
        d7.Tick(0, one, all); string s7 = d7.Song;
        Check(d7.Skip(5, one, all) && d7.Song != s7 && d7.StartTime == 5, "skip: another song starts now");
        int v = d7.Version; d7.Tick(6, one, all);
        Check(d7.Version == v, "no change, no new version");

        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
