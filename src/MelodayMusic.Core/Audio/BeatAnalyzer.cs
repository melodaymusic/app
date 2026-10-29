namespace MelodayMusic.Core.Audio;

public sealed record AudioAnalysis
{
    public double Bpm { get; init; }
    public double TempoConfidence { get; init; }
    public IReadOnlyList<double> Beats { get; init; } = [];
    public int DownbeatPhase { get; init; }
    public int Key { get; init; } = -1;
    public double KeyConfidence { get; init; }
    public IReadOnlyList<double> BeatEnergy { get; init; } = [];
    public double From { get; init; }
    public double To { get; init; }

    public bool HasBeat => Bpm > 0 && Beats.Count >= 8 && TempoConfidence >= 0.2;

    public IEnumerable<double> Downbeats => Beats.Where((_, i) => (i - DownbeatPhase) % 4 == 0 && i >= DownbeatPhase);

    public double BeatPeriod => Bpm > 0 ? 60.0 / Bpm : 0;

    public double? NearestDownbeat(double time) =>
        Downbeats.Select(d => (double?)d).OrderBy(d => Math.Abs(d!.Value - time)).FirstOrDefault();

    public double? DownbeatAtOrAfter(double time) => Downbeats.Where(d => d >= time - 0.02).Select(d => (double?)d).FirstOrDefault();

    public static string KeyName(int key) => key < 0 ? "?" :
        new[] { "C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B" }[key % 12] + (key >= 12 ? "m" : string.Empty);

    public static (int Number, bool Major) Camelot(int key)
    {
        var tonic = key % 12;
        var major = key < 12;
        var majorTonic = major ? tonic : (tonic + 3) % 12;
        var number = ((majorTonic * 7) % 12 + 7) % 12 + 1;
        return (number, major);
    }

    public static double KeyHarmony(int a, int b)
    {
        if (a < 0 || b < 0) return 0.6;
        var (na, ma) = Camelot(a);
        var (nb, mb) = Camelot(b);
        var steps = Math.Min((na - nb + 12) % 12, (nb - na + 12) % 12);
        if (steps == 0) return 1;
        if (steps == 1 && ma == mb) return 0.9;
        if (steps == 1) return 0.6;
        if (steps == 2 && ma == mb) return 0.45;
        return 0.15;
    }
}

public static class BeatAnalyzer
{
    private const int Frame = 1024;
    private const int Hop = 128;
    private const int ChromaFrame = 8192;
    private const double BassShare = 0.7;
    private const double OnsetDelay = 0.85;

    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    public static AudioAnalysis Analyse(float[] samples, int sampleRate, double offset = 0)
    {
        var length = samples.Length / (double)sampleRate;
        if (samples.Length < Frame * 8) return new AudioAnalysis { From = offset, To = offset + length };

        var (onset, lowEnergy) = Onsets(samples, sampleRate);
        var hopSeconds = Hop / (double)sampleRate;
        var (bpm, confidence) = Tempo(onset, hopSeconds);
        var beats = bpm > 0 ? TrackBeats(onset, bpm, hopSeconds) : [];
        if (beats.Count >= 8) bpm = 60 / Slope(beats);
        var phase = beats.Count >= 8 ? DownbeatPhase(beats, lowEnergy, hopSeconds) : 0;
        var (key, keyConfidence) = KeyOf(samples, sampleRate);
        var energy = BeatEnergy(samples, sampleRate, beats);

        return new AudioAnalysis
        {
            Bpm = bpm,
            TempoConfidence = confidence,
            Beats = beats.Select(b => offset + b).ToList(),
            DownbeatPhase = phase,
            Key = key,
            KeyConfidence = keyConfidence,
            BeatEnergy = energy,
            From = offset,
            To = offset + length,
        };
    }

    private static (double[] Onset, double[] Low) Onsets(float[] samples, int sampleRate)
    {
        var frames = (samples.Length - Frame) / Hop + 1;
        var window = Fft.Hann(Frame);
        var re = new double[Frame];
        var im = new double[Frame];
        var previous = new double[Frame / 2];
        var lowFlux = new double[frames];
        var highFlux = new double[frames];
        var low = new double[frames];
        var lowBins = Math.Max(2, (int)(150.0 * Frame / sampleRate));
        var splitBin = Math.Max(lowBins + 1, (int)(250.0 * Frame / sampleRate));
        var highBin = Math.Min(Frame / 2, (int)(8000.0 * Frame / sampleRate));
        for (var f = 0; f < frames; f++)
        {
            var start = f * Hop;
            for (var i = 0; i < Frame; i++)
            {
                re[i] = samples[start + i] * window[i];
                im[i] = 0;
            }
            Fft.Transform(re, im);
            double lows = 0, highs = 0, bass = 0;
            for (var k = 1; k < highBin; k++)
            {
                var magnitude = Math.Log(1 + 1000 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                var rise = magnitude - previous[k];
                if (rise > 0)
                {
                    if (k < splitBin) lows += rise;
                    else highs += rise;
                }
                if (k <= lowBins) bass += magnitude;
                previous[k] = magnitude;
            }
            lowFlux[f] = lows;
            highFlux[f] = highs;
            low[f] = bass;
        }

        var lowOnset = Rises(lowFlux, sampleRate);
        var highOnset = Rises(highFlux, sampleRate);
        var onset = new double[frames];
        for (var f = 0; f < frames; f++) onset[f] = BassShare * lowOnset[f] + (1 - BassShare) * highOnset[f];
        return (onset, low);
    }

    private static double[] Rises(double[] flux, int sampleRate)
    {
        var span = Math.Max(1, (int)(0.5 * sampleRate / Hop));
        var result = new double[flux.Length];
        double running = 0;
        for (var f = 0; f < flux.Length; f++)
        {
            running += flux[f];
            if (f >= span) running -= flux[f - span];
            result[f] = Math.Max(0, flux[f] - running / Math.Min(f + 1, span));
        }
        var std = Math.Sqrt(result.Select(v => v * v).Average());
        if (std > 0) for (var f = 0; f < result.Length; f++) result[f] /= std;
        return result;
    }

    private static (double Bpm, double Confidence) Tempo(double[] onset, double hopSeconds)
    {
        var minLag = (int)Math.Floor(60.0 / 180 / hopSeconds);
        var maxLag = (int)Math.Ceiling(60.0 / 70 / hopSeconds);
        if (onset.Length < maxLag * 4) return (0, 0);
        var ac = new double[maxLag * 2 + 2];
        for (var lag = 1; lag < ac.Length && lag < onset.Length; lag++)
        {
            double s = 0;
            for (var i = lag; i < onset.Length; i++) s += onset[i] * onset[i - lag];
            ac[lag] = s / (onset.Length - lag);
        }
        double zero = 0;
        for (var i = 0; i < onset.Length; i++) zero += onset[i] * onset[i];
        zero /= onset.Length;

        double bestScore = 0;
        var best = 0;
        var scores = new double[maxLag + 1];
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var bpm = 60 / (lag * hopSeconds);
            var prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 118) / 0.7, 2));
            var comb = ac[lag] + 0.5 * At(ac, 2 * lag) + 0.25 * At(ac, lag * 3 > ac.Length - 1 ? ac.Length - 1 : lag * 3);
            var score = comb * prior;
            scores[lag] = score;
            if (score > bestScore)
            {
                bestScore = score;
                best = lag;
            }
        }
        if (best == 0 || zero <= 0) return (0, 0);
        var l = best;
        double fine = l;
        if (l > minLag && l < maxLag)
        {
            var (a, b, c) = (scores[l - 1], scores[l], scores[l + 1]);
            var denominator = a - 2 * b + c;
            if (Math.Abs(denominator) > 1e-12) fine = l + 0.5 * (a - c) / denominator;
        }
        var tempo = 60 / (fine * hopSeconds);
        var confidence = Math.Clamp(ac[best] / zero, 0, 1);
        return (tempo, confidence);
    }

    private static double At(double[] a, int i) => i < a.Length ? a[i] : 0;

    private static List<double> TrackBeats(double[] onset, double bpm, double hopSeconds)
    {
        var period = 60 / bpm / hopSeconds;
        var n = onset.Length;
        var score = new double[n];
        var back = new int[n];
        const double tightness = 300;
        for (var t = 0; t < n; t++)
        {
            var from = (int)Math.Round(t - 2 * period);
            var to = (int)Math.Round(t - period / 2);
            var bestPrev = -1;
            var bestValue = double.NegativeInfinity;
            for (var p = Math.Max(0, from); p <= to && p < t; p++)
            {
                var penalty = Math.Log((t - p) / period);
                var value = score[p] - tightness * penalty * penalty;
                if (value > bestValue)
                {
                    bestValue = value;
                    bestPrev = p;
                }
            }
            score[t] = onset[t] + (bestPrev >= 0 ? Math.Max(0, bestValue) : 0);
            back[t] = bestPrev;
        }
        var end = n - 1;
        double endScore = double.NegativeInfinity;
        for (var t = Math.Max(0, n - (int)Math.Ceiling(period)); t < n; t++)
            if (score[t] > endScore)
            {
                endScore = score[t];
                end = t;
            }
        var beats = new List<double>();
        for (var t = end; t >= 0; t = back[t])
        {
            beats.Add(t * hopSeconds + OnsetDelay * Frame * hopSeconds / Hop);
            if (back[t] < 0) break;
        }
        beats.Reverse();
        return beats;
    }

    private static double Slope(List<double> beats)
    {
        var n = beats.Count;
        double meanX = (n - 1) / 2.0, meanY = beats.Average(), num = 0, den = 0;
        for (var i = 0; i < n; i++)
        {
            num += (i - meanX) * (beats[i] - meanY);
            den += (i - meanX) * (i - meanX);
        }
        return num / den;
    }

    private static int DownbeatPhase(List<double> beats, double[] low, double hopSeconds)
    {
        var sums = new double[4];
        var counts = new int[4];
        for (var i = 0; i < beats.Count; i++)
        {
            var frame = (int)Math.Round(beats[i] / hopSeconds);
            if (frame < 0 || frame >= low.Length) continue;
            double after = 0, before = 0;
            for (var k = 0; k < 4 && frame + k < low.Length; k++) after += low[frame + k];
            for (var k = 1; k <= 4 && frame - k >= 0; k++) before += low[frame - k];
            sums[i % 4] += after - before * 0.5;
            counts[i % 4]++;
        }
        var bestPhase = 0;
        for (var p = 1; p < 4; p++)
            if (counts[p] > 0 && sums[p] / counts[p] > sums[bestPhase] / Math.Max(1, counts[bestPhase])) bestPhase = p;
        return bestPhase;
    }

    private static (int Key, double Confidence) KeyOf(float[] samples, int sampleRate)
    {
        var chroma = new double[12];
        var window = Fft.Hann(ChromaFrame);
        var re = new double[ChromaFrame];
        var im = new double[ChromaFrame];
        for (var start = 0; start + ChromaFrame <= samples.Length; start += ChromaFrame / 2)
        {
            for (var i = 0; i < ChromaFrame; i++)
            {
                re[i] = samples[start + i] * window[i];
                im[i] = 0;
            }
            Fft.Transform(re, im);
            for (var k = 1; k < ChromaFrame / 2; k++)
            {
                var frequency = k * (double)sampleRate / ChromaFrame;
                if (frequency < 60 || frequency > 2000) continue;
                var note = (int)Math.Round(12 * Math.Log2(frequency / 440.0)) + 69;
                var magnitude = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                chroma[((note % 12) + 12) % 12] += Math.Log(1 + magnitude);
            }
        }
        if (chroma.Sum() <= 0) return (-1, 0);
        var correlations = new double[24];
        for (var tonic = 0; tonic < 12; tonic++)
        {
            correlations[tonic] = Correlation(chroma, MajorProfile, tonic);
            correlations[12 + tonic] = Correlation(chroma, MinorProfile, tonic);
        }
        var ordered = correlations.Select((c, i) => (c, i)).OrderByDescending(x => x.c).ToList();
        var confidence = Math.Clamp((ordered[0].c - ordered[1].c) * 5 + ordered[0].c * 0.5, 0, 1);
        return (ordered[0].i, confidence);
    }

    private static double Correlation(double[] chroma, double[] profile, int tonic)
    {
        double meanC = chroma.Average(), meanP = profile.Average();
        double num = 0, dc = 0, dp = 0;
        for (var i = 0; i < 12; i++)
        {
            var c = chroma[(i + tonic) % 12] - meanC;
            var p = profile[i] - meanP;
            num += c * p;
            dc += c * c;
            dp += p * p;
        }
        return dc <= 0 || dp <= 0 ? 0 : num / Math.Sqrt(dc * dp);
    }

    private static List<double> BeatEnergy(float[] samples, int sampleRate, List<double> beats)
    {
        var energy = new List<double>();
        for (var i = 0; i + 1 < beats.Count; i++)
        {
            var a = (int)(beats[i] * sampleRate);
            var b = Math.Min(samples.Length, (int)(beats[i + 1] * sampleRate));
            double sum = 0;
            for (var s = Math.Max(0, a); s < b; s++) sum += samples[s] * samples[s];
            energy.Add(10 * Math.Log10(sum / Math.Max(1, b - a) + 1e-10));
        }
        if (energy.Count == 0) return energy;
        var max = energy.Max();
        return energy.Select(e => e - max).ToList();
    }
}
