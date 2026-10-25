using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace MotorMod.Soundtrack
{
    /// <summary>One song in the music folder.</summary>
    public sealed class Song
    {
        public string Name;          // the file name without its extension: how players' songs are matched
        public string File;          // full path
        public long Size, Stamp;     // file size and last-write time, to notice a changed file
        public double Seconds;       // decoded length
        public float Lufs, GainDb;   // measured loudness and the gain that brings it to the common level
    }

    /// <summary>
    /// The soundtrack's songs: every MP3, OGG and WAV file in the music folder. Drop files in and the mod measures each new
    /// one once, in the background: its exact length, and its loudness (ITU-R BS.1770, the broadcast standard) so every song
    /// plays equally loud. Results are kept in .motormod-index.tsv in the same folder. Songs are matched between players by
    /// file name, so friends who drop in the same files hear the same song at the same moment.
    /// </summary>
    public sealed class MusicLibrary
    {
        public const float TargetLufs = -18f;
        private const string IndexName = ".motormod-index.tsv";
        private const long MaxMeasureBytes = 40L * 1024 * 1024;     // longer files (long mixes) are played as they are: decoding them whole costs too much memory
        private static readonly string[] Extensions = { ".mp3", ".ogg", ".wav" };
        // AudioClip.GetData(float[], int), bound without naming it: Unity 6 adds a Span overload that a .NET Framework build cannot resolve
        private static readonly System.Reflection.MethodInfo s_getData = typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });

        public string Folder { get; }
        public IReadOnlyDictionary<string, Song> Songs => m_ready;
        public int Pending => m_queue.Count + (m_measuring != null ? 1 : 0);
        public int Unreadable { get; private set; }
        public bool Busy => m_measuring != null || m_queue.Count > 0;

        private readonly Dictionary<string, Song> m_ready = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<Song> m_queue = new Queue<Song>();
        private Song m_measuring;

        public MusicLibrary(string folder) { Folder = folder; }

        /// <summary>Looks at the folder again: known files keep their measurements, new or changed ones are queued.</summary>
        public void Scan()
        {
            try { Directory.CreateDirectory(Folder); WriteReadme(); }
            catch (Exception e) { MotorModPlugin.Log.LogWarning($"Soundtrack: cannot use the music folder {Folder}: {e.Message}"); return; }
            var cached = ReadIndex();
            m_ready.Clear(); m_queue.Clear(); Unreadable = 0;
            var files = Directory.GetFiles(Folder).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                var info = new FileInfo(path);
                var song = new Song { Name = Path.GetFileNameWithoutExtension(path), File = path, Size = info.Length, Stamp = info.LastWriteTimeUtc.Ticks };
                if (m_ready.ContainsKey(song.Name) || m_queue.Any(q => string.Equals(q.Name, song.Name, StringComparison.OrdinalIgnoreCase))) continue;   // same name, another format
                if (cached.TryGetValue(Path.GetFileName(path), out var known) && known.Size == song.Size && known.Stamp == song.Stamp)
                {
                    song.Seconds = known.Seconds; song.Lufs = known.Lufs; song.GainDb = known.GainDb;
                    m_ready[song.Name] = song;
                }
                else m_queue.Enqueue(song);
            }
            MotorModPlugin.Log.LogInfo($"Soundtrack: {m_ready.Count} songs ready{(m_queue.Count > 0 ? $", {m_queue.Count} new to measure" : "")} in {Folder}");
        }

        /// <summary>Measures the queued songs one after another, a few milliseconds per frame.</summary>
        public IEnumerator MeasureQueued()
        {
            while (m_queue.Count > 0)
            {
                m_measuring = m_queue.Dequeue();
                yield return Measure(m_measuring);
                if (m_measuring.Seconds > 0) { m_ready[m_measuring.Name] = m_measuring; WriteIndex(); }
                m_measuring = null;
            }
        }

        private IEnumerator Measure(Song song)
        {
            float t0 = Time.realtimeSinceStartup;
            bool whole = song.Size <= MaxMeasureBytes;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(song.File).AbsoluteUri, TypeOf(song.File)))
            {
                var handler = (DownloadHandlerAudioClip)req.downloadHandler;
                handler.compressed = !whole; handler.streamAudio = false;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Unreadable++; MotorModPlugin.Log.LogWarning($"Soundtrack: cannot read {Path.GetFileName(song.File)}: {req.error}");
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null || clip.samples <= 0) { Unreadable++; MotorModPlugin.Log.LogWarning($"Soundtrack: {Path.GetFileName(song.File)} has no audio"); yield break; }
                song.Seconds = (double)clip.samples / clip.frequency;
                if (whole)
                {
                    var getData = (Func<float[], int, bool>)Delegate.CreateDelegate(typeof(Func<float[], int, bool>), clip, s_getData);
                    var meter = new LoudnessMeter(clip.frequency, clip.channels);
                    var buffer = new float[clip.frequency / 2 * clip.channels];           // half a second at a time
                    int frames = clip.samples, at = 0;
                    float sliceStart = Time.realtimeSinceStartup;
                    while (at < frames)
                    {
                        int n = Math.Min(buffer.Length / clip.channels, frames - at);
                        if (n * clip.channels != buffer.Length) buffer = new float[n * clip.channels];
                        if (!getData(buffer, at)) break;
                        meter.Add(buffer);
                        at += n;
                        if (Time.realtimeSinceStartup - sliceStart > 0.006f) { yield return null; sliceStart = Time.realtimeSinceStartup; }   // keep frames smooth
                    }
                    song.Lufs = meter.Integrated();
                    song.GainDb = float.IsNegativeInfinity(song.Lufs) ? 0f : Mathf.Clamp(TargetLufs - song.Lufs, -20f, 10f);
                }
                else { song.Lufs = float.NaN; song.GainDb = 0f; }
                UnityEngine.Object.Destroy(clip);
            }
            MotorModPlugin.Log.LogInfo($"Soundtrack: measured {Path.GetFileName(song.File)}: {song.Seconds:0.0} s, {(float.IsNaN(song.Lufs) ? "too long to measure, played as is" : $"{song.Lufs:0.0} LUFS, gain {song.GainDb:+0.0;-0.0} dB")} ({(Time.realtimeSinceStartup - t0):0.0} s)");
        }

        public static AudioType TypeOf(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".wav" ? AudioType.WAV : AudioType.MPEG;
        }

        // ---------- the index next to the songs ----------
        private Dictionary<string, Song> ReadIndex()
        {
            var result = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(Folder, IndexName);
            if (!System.IO.File.Exists(path)) return result;
            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split('\t');
                if (f.Length < 6) continue;
                if (!long.TryParse(f[1], out long size) || !long.TryParse(f[2], out long stamp)) continue;
                if (!double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double sec)) continue;
                float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float lufs);
                float.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float gain);
                result[f[0]] = new Song { Size = size, Stamp = stamp, Seconds = sec, Lufs = lufs, GainDb = gain };
            }
            return result;
        }

        private void WriteIndex()
        {
            try
            {
                var lines = new List<string> { "# MotorMod soundtrack: measured once per file (file, size, modified, seconds, LUFS, gain dB). Safe to delete." };
                foreach (var s in m_ready.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                    lines.Add(string.Join("\t", Path.GetFileName(s.File), s.Size.ToString(CultureInfo.InvariantCulture), s.Stamp.ToString(CultureInfo.InvariantCulture),
                        s.Seconds.ToString("0.###", CultureInfo.InvariantCulture), s.Lufs.ToString("0.##", CultureInfo.InvariantCulture), s.GainDb.ToString("0.##", CultureInfo.InvariantCulture)));
                System.IO.File.WriteAllLines(Path.Combine(Folder, IndexName), lines);
            }
            catch (Exception e) { MotorModPlugin.Log.LogWarning($"Soundtrack: cannot write the song index: {e.Message}"); }
        }

        private void WriteReadme()
        {
            string path = Path.Combine(Folder, "README.txt");
            if (System.IO.File.Exists(path)) return;
            System.IO.File.WriteAllText(path,
                "MotorMod sea soundtrack\r\n\r\n" +
                "Drop MP3, OGG or WAV files in this folder. While you ride a bike on water with friends nearby\r\n" +
                "(or alone on your own world), they play as a shared soundtrack.\r\n\r\n" +
                "Everyone hears the same song at the same moment when they have the same files (matched by file name),\r\n" +
                "so share this folder with your friends: zip it and send it, or put it on a shared drive.\r\n" +
                "New files are picked up when you join a world, or at once with the console command: motor music rescan\r\n" +
                "The mod measures each new song once (length and loudness, so all songs play equally loud).\r\n");
        }

        /// <summary>
        /// Integrated loudness after ITU-R BS.1770-4: K-weighting (a high shelf and a high-pass filter per channel), mean
        /// square over 400 ms blocks overlapping by 75 %, an absolute gate at -70 LUFS and a relative gate 10 LU below the
        /// ungated mean. Fed in pieces; the 400 ms blocks are built from 100 ms steps.
        /// </summary>
        public sealed class LoudnessMeter
        {
            private readonly int m_channels, m_stepFrames;
            private readonly Biquad[] m_shelf, m_highPass;
            private readonly List<double> m_steps = new List<double>();     // sum of squares over all channels per 100 ms step
            private double m_stepSum; private int m_stepFill;

            public LoudnessMeter(int sampleRate, int channels)
            {
                m_channels = Math.Max(1, channels);
                m_stepFrames = Math.Max(1, sampleRate / 10);
                m_shelf = new Biquad[m_channels]; m_highPass = new Biquad[m_channels];
                for (int c = 0; c < m_channels; c++) { m_shelf[c] = Biquad.HighShelf(sampleRate, 1681.974450955533, 3.999843853973347, 0.7071752369554196); m_highPass[c] = Biquad.HighPass(sampleRate, 38.13547087602444, 0.5003270373238773); }
            }

            public void Add(float[] interleaved)
            {
                int frames = interleaved.Length / m_channels;
                for (int i = 0; i < frames; i++)
                {
                    double sum = 0;
                    for (int c = 0; c < m_channels; c++)
                    {
                        double y = m_highPass[c].Run(m_shelf[c].Run(interleaved[i * m_channels + c]));
                        sum += y * y;
                    }
                    m_stepSum += sum;
                    if (++m_stepFill == m_stepFrames) { m_steps.Add(m_stepSum); m_stepSum = 0; m_stepFill = 0; }
                }
            }

            public float Integrated()
            {
                if (m_steps.Count < 4) return float.NegativeInfinity;
                var blocks = new List<double>(m_steps.Count);
                double frames = 4.0 * m_stepFrames;
                for (int i = 0; i + 3 < m_steps.Count; i++) blocks.Add((m_steps[i] + m_steps[i + 1] + m_steps[i + 2] + m_steps[i + 3]) / frames);
                double Lufs(double z) => -0.691 + 10.0 * Math.Log10(z);
                var aboveAbsolute = blocks.Where(z => z > 0 && Lufs(z) > -70.0).ToList();
                if (aboveAbsolute.Count == 0) return float.NegativeInfinity;
                double relative = Lufs(aboveAbsolute.Average()) - 10.0;
                var gated = aboveAbsolute.Where(z => Lufs(z) > relative).ToList();
                return gated.Count == 0 ? float.NegativeInfinity : (float)Lufs(gated.Average());
            }

            private struct Biquad
            {
                private double b0, b1, b2, a1, a2, x1, x2, y1, y2;
                public double Run(double x)
                {
                    double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                    x2 = x1; x1 = x; y2 = y1; y1 = y;
                    return y;
                }

                public static Biquad HighShelf(double rate, double f0, double gainDb, double q)
                {
                    double A = Math.Pow(10, gainDb / 40), w0 = 2 * Math.PI * f0 / rate, cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q), sq = 2 * Math.Sqrt(A) * alpha;
                    double a0 = (A + 1) - (A - 1) * cos + sq;
                    return new Biquad
                    {
                        b0 = A * ((A + 1) + (A - 1) * cos + sq) / a0, b1 = -2 * A * ((A - 1) + (A + 1) * cos) / a0, b2 = A * ((A + 1) + (A - 1) * cos - sq) / a0,
                        a1 = 2 * ((A - 1) - (A + 1) * cos) / a0, a2 = ((A + 1) - (A - 1) * cos - sq) / a0,
                    };
                }

                public static Biquad HighPass(double rate, double f0, double q)
                {
                    double w0 = 2 * Math.PI * f0 / rate, cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q), a0 = 1 + alpha;
                    return new Biquad { b0 = (1 + cos) / 2 / a0, b1 = -(1 + cos) / a0, b2 = (1 + cos) / 2 / a0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0 };
                }
            }
        }
    }
}
