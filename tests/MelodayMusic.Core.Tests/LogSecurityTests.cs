using MelodayMusic.Core.Logging;
using MelodayMusic.Core.Plex;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Tests;

public class LogSecurityTests
{
    [Theory]
    [InlineData("Authorization: Bearer SECRET")]
    [InlineData("Authorization: Basic SECRET")]
    [InlineData("Authorization: Token SECRET")]
    [InlineData("Token SECRET")]
    [InlineData("""{"api_key":"SECRET","api_secret":"SECRET","session_key":"SECRET"}""")]
    [InlineData("<User authToken='SECRET' />")]
    [InlineData("url=https%3A%2F%2Fserver%2F%3FX-Plex-Token%3DSECRET%26size%3D300")]
    [InlineData("X-Plex-Token=SECRET&code=SECRET&refresh_token=SECRET")]
    [InlineData("""{"SharedSecret":"SECRET","SessionKey":"SECRET"}""")]
    [InlineData("""{"password":"words SECRET words"}""")]
    [InlineData("Authorization: SECRET")]
    public void Credentials_are_redacted_in_headers_json_xml_and_nested_urls(string input)
    {
        var result = PlexHttp.Redact(input);
        Assert.DoesNotContain("SECRET", result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void Diagnostic_fields_do_not_record_listening_names()
    {
        var message = LogRedaction.RemovePersonalFields("Playing Private Song by Private Artist (direct)",
            [new("Track", "Private Song"), new("Artist", "Private Artist"), new("Mode", "direct")]);
        Assert.Equal("Playing [private] by [private] (direct)", message);
    }

    [Fact]
    public void Clearing_logs_preserves_other_files_and_new_logs_still_work()
    {
        var folder = Path.Combine(Path.GetTempPath(), "meloday-log-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "meloday-old.log"), "old");
            File.WriteAllText(Path.Combine(folder, "unrelated.log"), "keep");
            using (var provider = new FileLoggerProvider(folder))
            {
                var log = provider.CreateLogger("Test");
                log.LogInformation("api_key=SECRET Playing {Track}", "Private Song");
                provider.ClearLogs();
                log.LogInformation("New diagnostic");
            }
            Assert.False(File.Exists(Path.Combine(folder, "meloday-old.log")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(folder, "unrelated.log")));
            var content = File.ReadAllText(Directory.GetFiles(folder, "meloday-*.log").Single());
            Assert.Contains("New diagnostic", content);
            Assert.DoesNotContain("SECRET", content);
            Assert.DoesNotContain("Private Song", content);
        }
        finally { Directory.Delete(folder, true); }
    }
}
