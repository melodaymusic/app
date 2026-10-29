using System.Security.Cryptography;
using System.Text;

namespace MelodayMusic.Core.Covers;

public sealed record CoverRadial(int Side, double Position, double Radius = 0.70)
{
    public double X => Side switch { 0 => 0, 1 => 1, _ => Position };
    public double Y => Side switch { 2 => 0, 3 => 1, _ => Position };
}

public sealed record PlaylistCoverLayout(int Palette, CoverRadial First, CoverRadial Second)
{
    public CoverRadial Third => new(Enumerable.Range(0, 4).First(side => side != First.Side && side != Second.Side), 0.25 + ((First.Position + Second.Position) % 0.5));
    public IReadOnlyList<int> Colours => ColourGroups.First(group => group.Contains(Palette));
    private static readonly int[][] ColourGroups = [[0, 1, 22, 23, 24, 25], [2, 3, 4, 5, 6, 7, 8], [9, 10, 11, 12, 13], [14, 15, 16, 17, 18], [19, 20, 21]];
    public static PlaylistCoverLayout For(string identity)
    {
        var random = new Random(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(identity)), 0));
        var first = new CoverRadial(random.Next(4), 0.25 + random.NextDouble() * 0.5);
        var secondSide = (first.Side + 1 + random.Next(3)) % 4;
        var second = new CoverRadial(secondSide, 0.25 + random.NextDouble() * 0.5);
        return new(random.Next(26), first, second);
    }

    public static double Overlap(CoverRadial a, CoverRadial b)
    {
        var distance = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        var radius = a.Radius;
        if (distance >= 2 * radius) return 0;
        return (2 * radius * radius * Math.Acos(distance / (2 * radius))
            - distance / 2 * Math.Sqrt(4 * radius * radius - distance * distance)) / (Math.PI * radius * radius);
    }
}
