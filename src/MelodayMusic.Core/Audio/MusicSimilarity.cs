namespace MelodayMusic.Core.Audio;

public enum MusicEngineKind { Meloday, Plex }

public static class MusicSimilarity
{
    public static float[] Normalize(IEnumerable<float> values)
    {
        var vector = values.ToArray();
        if (vector.Length == 0 || vector.Any(v => !float.IsFinite(v))) return [];
        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        return norm < 1e-12 ? [] : vector.Select(v => (float)(v / norm)).ToArray();
    }

    public static IReadOnlyList<string> Stretch(IReadOnlyDictionary<string, float[]> vectors, string start, string end, int count,
        Func<string, bool>? usable = null, CancellationToken ct = default)
    {
        if (count <= 0 || start == end || !vectors.TryGetValue(start, out var rawA) || !vectors.TryGetValue(end, out var rawB)) return [];
        var a = Normalize(rawA);
        var b = Normalize(rawB);
        if (a.Length == 0 || a.Length != b.Length) return [];
        var candidates = new List<(string Key, float[] Vector)>();
        foreach (var (key, vector) in vectors)
        {
            ct.ThrowIfCancellationRequested();
            if (key == start || key == end || usable?.Invoke(key) == false) continue;
            var unit = Normalize(vector);
            if (unit.Length == a.Length) candidates.Add((key, unit));
        }
        var picked = new List<string>(count);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        for (var step = 1; step <= count; step++)
        {
            var t = step / (double)(count + 1);
            var target = Normalize(a.Select((x, i) => (float)((1 - t) * x + t * b[i])));
            if (target.Length == 0) break;
            string? best = null;
            var bestScore = double.NegativeInfinity;
            foreach (var (key, vector) in candidates)
            {
                if (taken.Contains(key)) continue;
                double dot = 0;
                for (var i = 0; i < vector.Length; i++) dot += vector[i] * (double)target[i];
                if (dot > bestScore) (best, bestScore) = (key, dot);
            }
            if (best is null) break;
            taken.Add(best);
            picked.Add(best);
        }
        return picked;
    }

    public static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count == 0 || a.Count != b.Count) return double.NaN;
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Count; i++)
        {
            if (!float.IsFinite(a[i]) || !float.IsFinite(b[i])) return double.NaN;
            dot += a[i] * (double)b[i]; aa += a[i] * (double)a[i]; bb += b[i] * (double)b[i];
        }
        return aa < 1e-12 || bb < 1e-12 ? double.NaN : Math.Clamp(dot / Math.Sqrt(aa * bb), -1, 1);
    }

    public static IReadOnlyList<string> Nearest(IReadOnlyDictionary<string, float[]> vectors, string seed, int count)
    {
        if (!vectors.TryGetValue(seed, out var origin)) return [];
        return vectors.Where(p => p.Key != seed).Select(p => (p.Key, Score: Cosine(origin, p.Value)))
            .Where(p => double.IsFinite(p.Score)).OrderByDescending(p => p.Score).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(Math.Max(0, count)).Select(p => p.Key).ToArray();
    }

    public static IReadOnlyList<string> Path(IReadOnlyDictionary<string, float[]> vectors, string start, string end,
        double maxStep = 0.45, CancellationToken ct = default)
    {
        if (start == end || !vectors.TryGetValue(start, out var a) || !vectors.TryGetValue(end, out var b)) return [];
        var keys = vectors.Where(p => p.Key != start && p.Key != end)
            .OrderByDescending(p => Cosine(a, p.Value) + Cosine(b, p.Value)).Take(256).Select(p => p.Key).Prepend(start).Append(end).ToArray();
        var distances = Enumerable.Repeat(double.PositiveInfinity, keys.Length).ToArray();
        var previous = Enumerable.Repeat(-1, keys.Length).ToArray();
        var visited = new bool[keys.Length]; distances[0] = 0;
        for (var step = 0; step < keys.Length; step++)
        {
            ct.ThrowIfCancellationRequested();
            var current = Enumerable.Range(0, keys.Length).Where(i => !visited[i]).MinBy(i => distances[i]);
            if (!double.IsFinite(distances[current])) break;
            if (current == keys.Length - 1) break;
            visited[current] = true;
            for (var next = 1; next < keys.Length; next++)
            {
                if (visited[next] || current == 0 && next == keys.Length - 1) continue;
                var distance = 1 - Cosine(vectors[keys[current]], vectors[keys[next]]);
                if (!double.IsFinite(distance) || distance > maxStep) continue;
                var cost = distances[current] + distance * distance + 0.025;
                if (cost < distances[next]) { distances[next] = cost; previous[next] = current; }
            }
        }
        if (previous[^1] < 0) return [];
        var result = new List<string>();
        for (var i = keys.Length - 1; i >= 0; i = previous[i]) { result.Add(keys[i]); if (i == 0) break; }
        result.Reverse();
        return result.Count > 12 ? [] : result;
    }
}
