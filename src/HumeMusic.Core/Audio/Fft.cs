namespace HumeMusic.Core.Audio;

/// <summary>An in-place radix-2 fast Fourier transform, enough for analysing music.</summary>
public static class Fft
{
    /// <summary>Transforms <paramref name="re"/>/<paramref name="im"/> (length a power of two) in place.</summary>
    public static void Transform(double[] re, double[] im)
    {
        var n = re.Length;
        if (n == 0 || (n & (n - 1)) != 0) throw new ArgumentException("Length must be a power of two.", nameof(re));
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wRe = Math.Cos(angle);
            var wIm = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var a = i + k;
                    var b = a + len / 2;
                    var tRe = re[b] * curRe - im[b] * curIm;
                    var tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    var next = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = next;
                }
            }
        }
    }

    /// <summary>A Hann window of <paramref name="size"/> points.</summary>
    public static double[] Hann(int size)
    {
        var w = new double[size];
        for (var i = 0; i < size; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size);
        return w;
    }
}
