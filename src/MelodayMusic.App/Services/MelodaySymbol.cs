using System.Xml.Linq;

namespace MelodayMusic.Services;

// The Meloday wave from Assets/MelodayMusic-symbol.svg. Covers and controls draw it as a vector so it stays sharp at small sizes.
internal static class MelodaySymbol
{
    internal const double Aspect = 512.0 / 226;

    private static readonly Lazy<XDocument> Source = new(() =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "MelodayMusic-symbol.svg")));

    private static readonly Lazy<string> Data = new(() =>
        string.Join(" ", Source.Value.Descendants().Where(e => e.Name.LocalName == "path").Select(e => (string?)e.Attribute("d"))));

    internal static string PathData => Data.Value;

    // The SVG in one colour, sized by the viewport it is drawn into.
    internal static string Xml(uint rgb)
    {
        var document = new XDocument(Source.Value);
        var root = document.Root!;
        root.Attribute("width")?.Remove();
        root.Attribute("height")?.Remove();
        var colour = $"#{rgb & 0xFFFFFF:x6}";
        foreach (var path in root.Descendants().Where(e => e.Name.LocalName == "path"))
        {
            path.SetAttributeValue("fill", colour);
            if (path.Attribute("stroke") is not null) path.SetAttributeValue("stroke", colour);
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }
}
