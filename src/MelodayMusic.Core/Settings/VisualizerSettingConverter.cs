using System.Text.Json;
using System.Text.Json.Serialization;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Settings;

/// <summary>
/// Reads the saved visualizer by name, falling back to Off for one the app no longer offers, so a removed visualizer
/// never makes the rest of the settings unreadable.
/// </summary>
public sealed class VisualizerSettingConverter : JsonConverter<VisualizerStyle>
{
    public override VisualizerStyle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        VisualizerStyle style;
        if (reader.TokenType == JsonTokenType.String)
        {
            if (!Enum.TryParse(reader.GetString(), ignoreCase: true, out style)) return VisualizerStyle.Off;
        }
        else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            style = (VisualizerStyle)number;
        else
        {
            reader.Skip();
            return VisualizerStyle.Off;
        }
        return VisualizerStyles.All.Contains(style) ? style : VisualizerStyle.Off;
    }

    public override void Write(Utf8JsonWriter writer, VisualizerStyle value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
