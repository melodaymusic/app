using System.Diagnostics;
using MelodayMusic.Core;
using MelodayMusic.Core.Caching;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Security;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;

using var loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
var settings = new SettingsStore(AppPaths.Settings, loggers.CreateLogger<SettingsStore>());
var identity = new PlexClientIdentity(settings.Current.ClientIdentifier, "1.0.0", Environment.MachineName, Environment.OSVersion.Version.ToString());
var httpClient = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
var http = new PlexHttp(httpClient, identity, loggers.CreateLogger<PlexHttp>());
var plexTv = new PlexTvClient(http);
var creds = new DpapiCredentialStore(AppPaths.Credentials, loggers.CreateLogger<DpapiCredentialStore>());
var session = new PlexSession(plexTv, http, new ConnectionSelector(http, loggers.CreateLogger<ConnectionSelector>()), creds, settings, new ResponseCache(), loggers);

var cmd = args.FirstOrDefault() ?? "status";
switch (cmd)
{
    case "signin":
    {
        var pin = await session.CreatePinAsync();
        var url = session.BuildAuthUri(pin);
        Console.WriteLine("AUTH_URL " + url);
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        await session.CompleteSignInAsync(pin, CancellationToken.None);
        Console.WriteLine($"STATE {session.State} {session.StatusMessage}");
        break;
    }
    case "status":
    {
        await session.InitializeAsync();
        Console.WriteLine($"STATE {session.State} {session.StatusMessage}");
        Console.WriteLine($"ACCOUNT {session.Account?.Username}");
        foreach (var s in session.Servers)
        {
            Console.WriteLine($"SERVER {s.Name} owned={s.Owned} presence={s.Presence} v={s.ProductVersion}");
            foreach (var c in ConnectionSelector.Order(s.Connections)) Console.WriteLine($"   {c}");
        }
        Console.WriteLine($"CONNECTED {session.Client?.Connection}");
        foreach (var sec in session.Sections) Console.WriteLine($"SECTION {sec.Key} {sec.Title}");
        break;
    }
    case "subscription":
    {
        var stored = creds.Load() ?? throw new InvalidOperationException("Not signed in");
        using var resp = await http.SendAsync(HttpMethod.Get, new Uri("https://plex.tv/api/v2/user"), stored.AccountToken, CancellationToken.None);
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.TryGetProperty("subscription", out var sub)) Console.WriteLine("subscription " + sub.GetRawText()[..Math.Min(1500, sub.GetRawText().Length)]);
        if (root.TryGetProperty("roles", out var roles)) Console.WriteLine("roles " + roles.GetRawText());
        break;
    }
    case "raw":
    {
        await session.InitializeAsync();
        var client = session.Client ?? throw new InvalidOperationException("Not connected: " + session.State);
        var path = args[1];
        using var resp = await http.SendAsync(HttpMethod.Get, new Uri(client.BaseUri, path.TrimStart('/')), client.Server.AccessToken, CancellationToken.None);
        var body = await resp.Content.ReadAsStringAsync();
        var max = args.Length > 2 ? int.Parse(args[2]) : 6000;
        Console.WriteLine(PlexHttp.Redact(body.Length > max ? body[..max] + "…" : body));
        break;
    }
    case "put":
    {
        await session.InitializeAsync();
        var client = session.Client ?? throw new InvalidOperationException("Not connected: " + session.State);
        using var resp = await http.SendAsync(HttpMethod.Put, new Uri(client.BaseUri, args[1].TrimStart('/')), client.Server.AccessToken, CancellationToken.None);
        Console.WriteLine($"STATUS {(int)resp.StatusCode}");
        break;
    }
    case "send":
    {
        await session.InitializeAsync();
        var client = session.Client ?? throw new InvalidOperationException("Not connected: " + session.State);
        var path = args[2].Replace("{machine}", client.Server.MachineIdentifier).Replace("%7Bmachine%7D", client.Server.MachineIdentifier);
        using var resp = await http.SendAsync(new HttpMethod(args[1]), new Uri(client.BaseUri, path.TrimStart('/')), client.Server.AccessToken, CancellationToken.None);
        var body = await resp.Content.ReadAsStringAsync();
        var max = args.Length > 3 ? int.Parse(args[3]) : 6000;
        Console.WriteLine($"STATUS {(int)resp.StatusCode}");
        Console.WriteLine(PlexHttp.Redact(body.Length > max ? body[..max] + "…" : body));
        break;
    }
    case "url":
    {
        await session.InitializeAsync();
        var client = session.Client!;
        var track = await client.GetTrackAsync(args[1]);
        Console.WriteLine("DIRECT " + client.BuildDirectPlayUri(track!));
        Console.WriteLine("TRANSCODE " + client.BuildTranscodeUri(track!, 320, Guid.NewGuid().ToString("N")));
        break;
    }
    case "lyrics-survey":
    {
        await session.InitializeAsync();
        var client = session.Client ?? throw new InvalidOperationException("Not connected");
        var section = session.Section ?? throw new InvalidOperationException("No library");
        var count = args.Length > 1 ? int.Parse(args[1]) : 30;
        var sample = (await client.GetTracksAsync(section.Key, 0, count, "random")).Items;
        var lrclib = new MelodayMusic.Core.Lyrics.LrcLibClient(new HttpClient(), "dev");
        int plexSynced = 0, plexAny = 0, libSynced = 0, libAny = 0, either = 0;
        foreach (var t in sample)
        {
            MelodayMusic.Core.Models.TrackLyrics? p = null, l = null;
            try { p = await client.GetLyricsAsync(t.RatingKey); } catch (Exception) { }
            try { l = await lrclib.GetAsync(t.DisplayArtist, t.Title, t.AlbumTitle, t.Duration); } catch (Exception) { }
            if (p is not null) plexAny++;
            if (p is { IsSynced: true }) plexSynced++;
            if (l is not null) libAny++;
            if (l is { IsSynced: true } || l is { IsInstrumental: true }) libSynced++;
            if (p is { IsSynced: true } || l is { IsSynced: true } || l is { IsInstrumental: true }) either++;
            Console.WriteLine($"{(p is { IsSynced: true } ? "P" : p is null ? "-" : "p")}{(l is { IsInstrumental: true } ? "I" : l is { IsSynced: true } ? "L" : l is null ? "-" : "l")}  {t.DisplayArtist} - {t.Title}");
        }
        Console.WriteLine($"of {sample.Count}: Plex synced {plexSynced} (any {plexAny}); LRCLIB synced/instrumental {libSynced} (any {libAny}); synced from either {either}");
        break;
    }
    case "analyze":
    {
        await session.InitializeAsync();
        var client = session.Client ?? throw new InvalidOperationException("Not connected");
        var section = session.Section ?? throw new InvalidOperationException("No library");
        foreach (var title in args.Skip(1))
        {
            var found = (await client.SearchAsync(title, section.Key, 3)).Tracks.FirstOrDefault(t => t.Title.StartsWith(title, StringComparison.OrdinalIgnoreCase));
            if (found is null) { Console.WriteLine($"{title}: not found"); continue; }
            var full = await client.GetTrackAsync(found.RatingKey) ?? found;
            var uri = client.BuildDirectPlayUri(full);
            if (uri is null) { Console.WriteLine($"{title}: no file"); continue; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var head = MelodayMusic.Core.Audio.BeatAnalyzer.Analyse(MelodayMusic.Core.Audio.AudioDecoder.ReadWindow(uri.ToString(), TimeSpan.Zero, TimeSpan.FromSeconds(45)), MelodayMusic.Core.Audio.AudioDecoder.AnalysisRate);
            var tailFrom = full.Duration - TimeSpan.FromSeconds(60);
            var tail = MelodayMusic.Core.Audio.BeatAnalyzer.Analyse(MelodayMusic.Core.Audio.AudioDecoder.ReadWindow(uri.ToString(), tailFrom, TimeSpan.FromSeconds(60)), MelodayMusic.Core.Audio.AudioDecoder.AnalysisRate, tailFrom.TotalSeconds);
            Console.WriteLine($"{full.DisplayArtist} - {full.Title} ({full.Media?.Codec}): head {head.Bpm:0.0} bpm ({head.TempoConfidence:0.00}) {MelodayMusic.Core.Audio.AudioAnalysis.KeyName(head.Key)}; tail {tail.Bpm:0.0} bpm ({tail.TempoConfidence:0.00}) {MelodayMusic.Core.Audio.AudioAnalysis.KeyName(tail.Key)}; first bar {head.Downbeats.FirstOrDefault():0.00} s; {clock.ElapsedMilliseconds} ms");
        }
        break;
    }
    case "pipeline-test":
    {
        await session.InitializeAsync();
        var client = session.Client ?? throw new InvalidOperationException("Not connected");
        var section = session.Section ?? throw new InvalidOperationException("No library");
        var found = (await client.SearchAsync(args[1], section.Key, 5)).Tracks.First(t => t.DisplayArtist.Contains(args[2], StringComparison.OrdinalIgnoreCase));
        var full = await client.GetTrackAsync(found.RatingKey) ?? found;
        var uri = client.BuildDirectPlayUri(full)!.ToString();
        NAudio.MediaFoundation.MediaFoundationApi.Startup();
        using var reader = new NAudio.Wave.MediaFoundationReader(uri, new NAudio.Wave.MediaFoundationReader.MediaFoundationReaderSettings { RequestFloatOutput = true, RepositionInRead = true });
        Console.WriteLine($"reader format: {reader.WaveFormat} encoding {reader.WaveFormat.Encoding}");
        NAudio.Wave.ISampleProvider samples = NAudio.Wave.WaveExtensionMethods.ToSampleProvider(reader);
        Console.WriteLine($"sample provider: {samples.WaveFormat}");
        if (samples.WaveFormat.SampleRate != 48000) samples = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(samples, 48000);
        if (args.Length > 3)
        {
            using var w = new NAudio.Wave.WaveFileWriter(args[3], NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
            var buf = new float[48000 * 2];
            for (var i = 0; i < 90; i++) { var g = samples.Read(buf, 0, buf.Length); w.WriteSamples(buf, 0, g); }
            break;
        }
        var filter = new MelodayMusic.Core.Audio.HighPass(48000, 2);
        var block = new float[4096 * 2];
        for (var second = 0; second < 20; second += 2)
        {
            double sumRaw = 0, sumFiltered = 0, dc = 0; long n = 0;
            for (var k = 0; k < 2 * 48000 / 4096; k++)
            {
                var got = samples.Read(block, 0, block.Length);
                for (var i = 0; i < got; i++) { sumRaw += block[i] * block[i]; dc += block[i]; }
                filter.Process(block, 0, got / 2);
                for (var i = 0; i < got; i++) sumFiltered += block[i] * block[i];
                n += got;
            }
            Console.WriteLine($"t={second,2}: raw rms {Math.Sqrt(sumRaw / n):0.000} dc {dc / n:+0.000;-0.000}, after filter {Math.Sqrt(sumFiltered / n):0.000}");
        }
        break;
    }
    case "render-mix":
    {
        await session.InitializeAsync();
        var client = session.Client ?? throw new InvalidOperationException("Not connected");
        var section = session.Section ?? throw new InvalidOperationException("No library");
        async Task<MelodayMusic.Core.Models.Track> Find(string title, string artist)
        {
            var hit = (await client.SearchAsync(title, section.Key, 8)).Tracks.First(t => t.Title.StartsWith(title, StringComparison.OrdinalIgnoreCase) && t.DisplayArtist.Contains(artist, StringComparison.OrdinalIgnoreCase));
            return await client.GetTrackAsync(hit.RatingKey) ?? hit;
        }
        var a = await Find(args[2], args[3]);
        var b = await Find(args[4], args[5]);
        var la = await client.GetLoudnessAsync(a.RatingKey, Math.Clamp((int)(a.Duration.TotalSeconds * 2), 128, 1200));
        var lb = await client.GetLoudnessAsync(b.RatingKey, Math.Clamp((int)(b.Duration.TotalSeconds * 2), 128, 1200));
        var loudness = MelodayMusic.Core.Playback.AutoMix.Plan(la, a.Duration, lb, b.Duration) ?? throw new InvalidOperationException("no loudness plan");
        var uriA = client.BuildDirectPlayUri(a)!.ToString();
        var uriB = client.BuildDirectPlayUri(b)!.ToString();
        var tailFrom = a.Duration - TimeSpan.FromSeconds(60);
        var tail = MelodayMusic.Core.Audio.BeatAnalyzer.Analyse(MelodayMusic.Core.Audio.AudioDecoder.ReadWindow(uriA, tailFrom, TimeSpan.FromSeconds(60)), MelodayMusic.Core.Audio.AudioDecoder.AnalysisRate, tailFrom.TotalSeconds);
        var head = MelodayMusic.Core.Audio.BeatAnalyzer.Analyse(MelodayMusic.Core.Audio.AudioDecoder.ReadWindow(uriB, TimeSpan.Zero, TimeSpan.FromSeconds(50)), MelodayMusic.Core.Audio.AudioDecoder.AnalysisRate);
        var plan = MelodayMusic.Core.Playback.BeatMix.Plan(loudness, tail, head, la?.Vocals, lb?.Vocals)
                   ?? new MelodayMusic.Core.Playback.BeatMixPlan { StartAt = loudness.StartAt, IncomingStart = loudness.IncomingStart, Shape = loudness.Shape, Summary = "loudness plan" };
        Console.WriteLine($"plan: {plan.Summary}; at {plan.StartAt.TotalSeconds:0.00} s, overlap {plan.Shape.Overlap.TotalSeconds:0.0} s, swap {plan.BassSwap?.TotalSeconds:0.0}, ramp {plan.RampBack.TotalSeconds:0.0} s");

        const int rate = 48000;
        float[] Decode(string uri, TimeSpan from, TimeSpan length)
        {
            NAudio.MediaFoundation.MediaFoundationApi.Startup();
            using var reader = new NAudio.Wave.MediaFoundationReader(uri, new NAudio.Wave.MediaFoundationReader.MediaFoundationReaderSettings { RequestFloatOutput = true, RepositionInRead = true });
            reader.CurrentTime = from;
            NAudio.Wave.ISampleProvider sp = NAudio.Wave.WaveExtensionMethods.ToSampleProvider(reader);
            if (sp.WaveFormat.Channels == 1) sp = new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(sp);
            if (sp.WaveFormat.SampleRate != rate) sp = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(sp, rate);
            var data = new float[(int)(length.TotalSeconds * rate) * 2];
            var read = 0;
            while (read < data.Length) { var g = sp.Read(data, read, Math.Min(16384, data.Length - read)); if (g <= 0) break; read += g; }
            return data;
        }
        const double lead = 6;
        var span = plan.Shape.Overlap + plan.RampBack + TimeSpan.FromSeconds(8);
        var dataA = Decode(uriA, plan.StartAt - TimeSpan.FromSeconds(lead), TimeSpan.FromSeconds(lead) + plan.Shape.Overlap + TimeSpan.FromSeconds(1));
        var dataB = Decode(uriB, plan.IncomingStart, TimeSpan.FromSeconds(span.TotalSeconds * 1.2));
        float[] Render(bool processed)
        {
            int posA = 0, posB = 0;
            Func<float[], int, int> Puller(float[] data, Func<int> get, Action<int> set) => (into, frames) =>
            {
                var n = Math.Min(frames * 2, data.Length - get());
                if (n <= 0) return 0;
                Array.Copy(data, get(), into, 0, n);
                set(get() + n);
                return n / 2;
            };
            var voiceA = new MelodayMusic.Core.Audio.DeckVoice(rate, 2, Puller(dataA, () => posA, v => posA = v));
            var voiceB = new MelodayMusic.Core.Audio.DeckVoice(rate, 2, Puller(dataB, () => posB, v => posB = v)) { Gain = 0 };
            var totalFrames = (int)((lead + span.TotalSeconds) * rate);
            var result = new float[totalFrames * 2];
            var slice = new float[256 * 2];
            var leadFrames = (int)(lead * rate);
            for (var at = 0; at < totalFrames; at += 256)
            {
                var n = Math.Min(256, totalFrames - at);
                if (at < leadFrames)
                {
                    voiceA.Read(slice, n);
                    Array.Copy(slice, 0, result, at * 2, n * 2);
                    continue;
                }
                var moment = MelodayMusic.Core.Audio.MixTimeline.At(plan, TimeSpan.FromSeconds((at - leadFrames) / (double)rate));
                if (!moment.OutgoingDone)
                {
                    voiceA.Gain = (float)moment.OutGain; voiceA.Cutoff = processed ? moment.OutCutoff : 0;
                    voiceA.Read(slice, n);
                    for (var i = 0; i < n * 2; i++) result[at * 2 + i] += slice[i];
                }
                voiceB.Gain = (float)moment.InGain; voiceB.Cutoff = processed ? moment.InCutoff : 0; voiceB.Rate = processed ? moment.InRate : 1;
                voiceB.Read(slice, n);
                for (var i = 0; i < n * 2; i++) result[at * 2 + i] += slice[i];
            }
            return result;
        }
        var output = Render(true);
        var plain = Render(false);
        var total = output.Length / 2;
        using (var writer = new NAudio.Wave.WaveFileWriter(args[1], NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)))
            writer.WriteSamples(output, 0, output.Length);
        using (var writer = new NAudio.Wave.WaveFileWriter(args[1] + ".plain.wav", NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)))
            writer.WriteSamples(plain, 0, plain.Length);

        List<double> Spikes(float[] x)
        {
            var win = rate / 200;
            var sharp = new List<double>();
            for (var w = 0; w + win < x.Length / 2; w += win)
            {
                double e = 0;
                for (var i = w + 2; i < w + win; i++)
                {
                    var d = x[i * 2] - 2 * x[(i - 1) * 2] + x[(i - 2) * 2];
                    e += d * d;
                }
                sharp.Add(Math.Sqrt(e / win));
            }
            var found = new List<double>();
            for (var k = 20; k < sharp.Count - 20; k++)
            {
                var around = sharp.Skip(k - 20).Take(41).Where((_, j) => j < 18 || j > 22).OrderBy(v => v).ToList();
                if (sharp[k] > 6 * around[around.Count / 2] && sharp[k] > 0.01) found.Add(k * 0.005 - lead);
            }
            return found;
        }
        var heard = Spikes(output);
        var natural = Spikes(plain);
        var made = heard.Where(t => t >= -0.5 && !natural.Any(n => Math.Abs(n - t) < 0.3)).ToList();
        Console.WriteLine($"rendered {total / (double)rate:0.0} s; sharp moments {heard.Count} (the songs' own: {natural.Count}); made by the mixing: {(made.Count == 0 ? "none" : string.Join(", ", made.Take(20).Select(t => $"{t:+0.00;-0.00} s")))}");
        break;
    }
    case "automix":
    {
        await session.InitializeAsync();
        var client = session.Client ?? throw new InvalidOperationException("Not connected");
        var a = await client.GetTrackAsync(args[1]);
        var b = await client.GetTrackAsync(args[2]);
        var la = await client.GetLoudnessAsync(args[1], Math.Clamp((int)(a!.Duration.TotalSeconds * 2), 128, 1200));
        var lb = await client.GetLoudnessAsync(args[2], Math.Clamp((int)(b!.Duration.TotalSeconds * 2), 128, 1200));
        Console.WriteLine($"{a?.Title} ({a?.Duration.TotalSeconds:0}) vocals {la?.Vocals?.First.TotalSeconds:0.0}-{la?.Vocals?.LastEnd.TotalSeconds:0.0}  ->  {b?.Title} vocals from {lb?.Vocals?.First.TotalSeconds:0.0}");
        var plan = MelodayMusic.Core.Playback.AutoMix.Plan(la, a!.Duration, lb, b!.Duration);
        Console.WriteLine($"points {la?.Levels.Count} / {lb?.Levels.Count}");
        static string Span(MelodayMusic.Core.Playback.TrackLoudness? l, TimeSpan duration, double from, double to) => l is null ? "-" :
            string.Join(" ", Enumerable.Range(0, (int)(to - from)).Select(s => l.Levels[Math.Clamp((int)((from + s) / duration.TotalSeconds * l.Levels.Count), 0, l.Levels.Count - 1)].ToString("0")));
        Console.WriteLine($"end   ({a.Duration.TotalSeconds - 30:0}..): {Span(la, a.Duration, a.Duration.TotalSeconds - 30, a.Duration.TotalSeconds)}");
        Console.WriteLine($"start (0..25): {Span(lb, b.Duration, 0, 25)}");
        Console.WriteLine(plan is null ? "no plan" : $"hand over at {plan.StartAt.TotalSeconds:0.0} s ({(a.Duration - plan.StartAt).TotalSeconds:0.0} s before the end), overlap {plan.Fade.TotalSeconds:0.0} s, in over {plan.Shape.InFade.TotalSeconds:0.0} s, out from {plan.Shape.OutFadeStart.TotalSeconds:0.0} s over {plan.Shape.OutFade.TotalSeconds:0.0} s; next starts at {plan.IncomingStart.TotalSeconds:0.0} s");
        var plain = MelodayMusic.Core.Playback.AutoMix.Plan(la is null ? null : la with { Vocals = null }, a.Duration, lb is null ? null : lb with { Vocals = null }, b.Duration);
        Console.WriteLine(plain is null ? "(loudness only: no plan)" : $"(loudness only: at {plain.StartAt.TotalSeconds:0.0} s over {plain.Fade.TotalSeconds:0.0} s)");
        break;
    }
    case "cast-find":
    {
        using var web = new HttpClient();
        foreach (var d in await MelodayMusic.Core.Cast.CastDiscovery.FindAsync(web, TimeSpan.FromSeconds(3)))
            Console.WriteLine($"{d.Kind,-10} {d.Name,-30} {d.Host}:{d.Port}  {d.Model}  control={d.ControlUrl} volume={d.VolumeUrl}");
        break;
    }
    case "fetch":
    {
        await session.InitializeAsync();
        var client = session.Client!;
        var track = await client.GetTrackAsync(args[1]);
        var uri = args.Length > 2 && args[2] == "transcode"
            ? client.BuildTranscodeUri(track!, 320, Guid.NewGuid().ToString("N"))
            : client.BuildDirectPlayUri(track!)!;
        using var resp = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        Console.WriteLine($"STATUS {(int)resp.StatusCode} {resp.Content.Headers.ContentType} len={resp.Content.Headers.ContentLength}");
        var buf = new byte[65536];
        var stream = await resp.Content.ReadAsStreamAsync();
        var n = await stream.ReadAtLeastAsync(buf, buf.Length, throwOnEndOfStream: false);
        Console.WriteLine($"READ {n} bytes; head={Convert.ToHexString(buf, 0, Math.Min(n, 16))}");
        break;
    }
    default:
        Console.WriteLine("commands: signin | status | raw <path> [max] | url <ratingKey> | fetch <ratingKey> [transcode]");
        break;
}
