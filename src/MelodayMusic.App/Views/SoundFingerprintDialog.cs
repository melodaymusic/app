using MelodayMusic.Controls;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Models;
using MelodayMusic.Helpers;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Views;

public static class SoundFingerprintDialog
{
    private const int Nearest = 6;

    public static async Task ShowAsync(Track track)
    {
        var dialog = Dialogs.Create("Sound Fingerprint");
        Dialogs.Wide(dialog, 760);
        dialog.CloseButtonText = "Done";
        var body = new StackPanel { Spacing = 24, Width = Dialogs.ContentWidth(720) };
        dialog.Content = new ScrollViewer { MaxHeight = Dialogs.ContentHeight(680), Content = body, Padding = new Thickness(0, 0, 12, 0) };
        using var lifetime = new CancellationTokenSource();
        dialog.Closed += (_, _) => lifetime.Cancel();

        async Task Fill()
        {
            try { await Load(); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        async Task Load()
        {
            body.Children.Clear();
            body.Children.Add(Header(track));
            var analysis = App.Services.GetRequiredService<AudioAnalysisService>();
            var engine = App.Services.GetRequiredService<MelodayEngine>();
            var profile = await engine.CachedAsync(track, lifetime.Token);
            var head = await analysis.CachedHeadAsync(track);
            var tail = await analysis.CachedTailAsync(track);
            if (lifetime.IsCancellationRequested) return;

            if (profile is null || head is null || tail is null)
                body.Children.Add(NotYet(profile is null, head is null || tail is null, async button =>
                {
                    button.IsEnabled = false;
                    button.Content = "Analysing…";
                    try
                    {
                        if (profile is null) await analysis.WarmEngineAsync(track, lifetime.Token, fast: true);
                        if (head is null) await analysis.WarmAsync(track, head: true, lifetime.Token, fast: true);
                        if (tail is null) await analysis.WarmAsync(track, head: false, lifetime.Token, fast: true);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    if (!lifetime.IsCancellationRequested) await Fill();
                }));

            if (profile is not null) body.Children.Add(Fingerprint(profile));
            var heard = profile is not null && engine.Vocabulary is not null ? Section("What ME Hears", HeardNote) : null;
            if (heard is not null)
            {
                heard.Children.Add(Caption("Listening for words…"));
                body.Children.Add(heard);
            }
            body.Children.Add(Listened(track, profile, head, tail));
            if (head is not null || tail is not null) body.Children.Add(Rhythm(head, tail));
            if (heard is not null)
            {
                var description = await engine.DescribeAsync(track, lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                heard.Children.RemoveAt(heard.Children.Count - 1);
                if (description is { Categories.Count: > 0 }) heard.Children.Add(Words(description, engine.Vocabulary!));
                else heard.Children.Add(Caption("ME couldn't put this one into words."));
            }
            if (profile is not null)
            {
                var near = Section("Sounds Closest To", "The songs in your library whose fingerprints are nearest this one's.");
                var list = new StackPanel { Spacing = 8 };
                near.Children.Add(list);
                list.Children.Add(Caption("Comparing with the rest of your library…"));
                body.Children.Add(near);
                var matches = await Task.Run(() => engine.Snapshot().Values
                    .Where(p => p.Track.RatingKey != track.RatingKey)
                    .Select(p => (p.Track, Match: MusicSimilarity.Cosine(profile.Vector, p.Vector)))
                    .Where(p => double.IsFinite(p.Match))
                    .OrderByDescending(p => p.Match)
                    .Take(Nearest)
                    .ToList(), lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                list.Children.Clear();
                if (matches.Count == 0) list.Children.Add(Caption("No other songs are analysed yet."));
                foreach (var (other, match) in matches) list.Children.Add(MatchRow(other, match));
            }
        }

        _ = Fill();
        await dialog.ShowAsync();
    }

    private static Style Style(string key) => (Style)Application.Current.Resources[key];
    private static TextBlock Caption(string text) => new() { Text = text, Style = Style("MelodaySecondaryText"), TextWrapping = TextWrapping.WrapWholeWords };

    private static StackPanel Section(string title, string? note = null)
    {
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(new TextBlock { Text = title, Style = Style("MelodayBodyStrongText") });
        if (note is not null) section.Children.Add(Caption(note));
        return section;
    }

    private static Grid Facts(params (string Label, string? Value)[] rows)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        foreach (var (label, value) in rows.Where(r => !string.IsNullOrEmpty(r.Value)))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Caption(label);
            var text = new TextBlock { Text = value, Style = Style("MelodayBodyText"), TextWrapping = TextWrapping.WrapWholeWords, IsTextSelectionEnabled = true };
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(name);
            grid.Children.Add(text);
            row++;
        }
        return grid;
    }

    private static Grid Header(Track track)
    {
        var art = new ArtworkImage
        {
            Width = 72,
            Height = 72,
            DecodeSize = 144,
            SourcePath = track.DisplayThumb,
            PlaceholderIcon = "Music",
            ArtworkCornerRadius = (CornerRadius)Application.Current.Resources["MelodayRadiusThumb"],
        };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = track.Title, Style = Style("MelodayHeadlineText"), TextWrapping = TextWrapping.WrapWholeWords },
                Caption(string.IsNullOrEmpty(track.AlbumTitle) ? track.DisplayArtist : $"{track.DisplayArtist} · {track.AlbumTitle}"),
                Caption($"{Format.Duration(track.Duration)} · analysed by the Meloday Engine (ME), on this PC"),
            },
        };
        Grid.SetColumn(text, 1);
        var header = new Grid { ColumnSpacing = 16, Children = { art, text } };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return header;
    }

    private static StackPanel NotYet(bool sound, bool rhythm, Func<Button, Task> analyse)
    {
        var what = sound && rhythm ? "its sound or its rhythm" : sound ? "its sound" : "its opening and ending";
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Caption($"ME hasn't analysed {what} yet. It's on its way in the background, or you can analyse it now (it takes a few seconds)."));
        var button = new Button { Content = "Analyse Now", Style = Style("MelodaySecondaryButtonStyle"), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += async (_, _) => await analyse(button);
        panel.Children.Add(button);
        return panel;
    }

    private static StackPanel Fingerprint(EngineProfile profile)
    {
        var section = Section("Fingerprint",
            "How ME hears this song: 512 numbers, one per spoke, measured by a music-trained AI model. The accent spokes are positive " +
            "and the grey ones negative; the longer the spoke, the stronger. Songs whose rings look alike sound alike.");
        var ring = new FingerprintView { Values = profile.Vector, Width = 280, Height = 280, HorizontalAlignment = HorizontalAlignment.Left };
        var v = profile.Vector;
        var positive = v.Count(x => x > 0);
        var strongest = v.Select((x, i) => (Value: x, Index: i)).OrderByDescending(p => Math.Abs(p.Value)).Take(5)
            .Select(p => $"#{p.Index + 1} ({p.Value:+0.000;-0.000})");
        var facts = Facts(
            ("Numbers", $"{v.Length} ({positive} positive, {v.Length - positive} negative)"),
            ("Strongest", string.Join(", ", strongest)),
            ("Spread", $"from {v.Min():0.000} to {v.Max():+0.000}, typical size {Math.Sqrt(v.Average(x => (double)x * x)):0.000}"),
            ("Model", "CLAP, trained on music; pinned and checked, running on this PC"),
            ("Version", profile.Revision),
            ("Analysed", profile.AnalysedAt.LocalDateTime.ToString("d MMMM yyyy, h:mm tt")));
        var layout = new Grid { ColumnSpacing = 24 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        facts.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(facts, 1);
        layout.Children.Add(ring);
        layout.Children.Add(facts);
        layout.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 620;
            Grid.SetColumn(facts, narrow ? 0 : 1);
            Grid.SetRow(facts, narrow ? 1 : 0);
            if (narrow && layout.RowDefinitions.Count == 0)
            {
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.RowSpacing = 16;
            }
        };
        section.Children.Add(layout);
        return section;
    }

    private const string HeardNote =
        "ME's model learnt sounds and words together, so it can say what a fingerprint sounds like. Each row is the words that fit best, " +
        "with how sure ME is among that row's words, measured against the rest of your library. A row in grey is only a lean.";

    private static StackPanel Words(SoundDescription description, SoundVocabulary vocabulary)
    {
        var panel = new StackPanel { Spacing = 12 };
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        foreach (var category in description.Categories.Where(c => c.Words.Count > 0))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Caption(category.Name);
            var words = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            for (var i = 0; i < category.Words.Count; i++)
            {
                var word = category.Words[i];
                words.Children.Add(new TextBlock
                {
                    Text = $"{word.Label} {word.Confidence:P0}",
                    Style = Style(i == 0 && category.Clear ? "MelodayBodyStrongText" : "MelodaySecondaryText"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(words, $"{category.Name}: " +
                string.Join(", ", category.Words.Select(w => $"{w.Label}, {w.Confidence:P0} sure")) + (category.Clear ? "" : " (only a lean)"));
            Grid.SetRow(name, row);
            Grid.SetRow(words, row);
            Grid.SetColumn(words, 1);
            grid.Children.Add(name);
            grid.Children.Add(words);
            row++;
        }
        panel.Children.Add(grid);
        panel.Children.Add(Caption($"{vocabulary.Terms.Count} words in {vocabulary.Categories.Count} groups, placed beside the songs ahead of time by the " +
            $"model's text side, which doesn't run on this PC · {vocabulary.Revision}"));
        return panel;
    }

    private static StackPanel Listened(Track track, EngineProfile? profile, AudioAnalysis? head, AudioAnalysis? tail)
    {
        var section = Section("Where ME Listened",
            "The whole song, with the three samples ME takes for the fingerprint (accent) and the opening and ending it studies for tempo and key (grey).");
        var spans = new List<ListenSpan>();
        var window = profile is { SampleOffsets.Length: > 0 } p ? p.SampledSeconds / p.SampleOffsets.Length : 0;
        if (profile is not null)
            for (var i = 0; i < profile.SampleOffsets.Length; i++)
                spans.Add(new ListenSpan(profile.SampleOffsets[i], profile.SampleOffsets[i] + window, $"ME sample {i + 1}", Accent: true));
        if (head is not null) spans.Add(new ListenSpan(head.From, head.To, "Opening analysed", Accent: false));
        if (tail is not null) spans.Add(new ListenSpan(tail.From, tail.To, "Ending analysed", Accent: false));
        var timeline = new ListenTimeline { Length = track.Duration.TotalSeconds, Spans = spans };
        section.Children.Add(timeline);
        section.Children.Add(Facts(
            ("Samples", profile is null ? "Not taken yet" : string.Join(", ", profile.SampleOffsets.Select(o => $"{BeatGrid.Time(o)}–{BeatGrid.Time(o + window)}"))),
            ("Heard in all", profile is null ? null : $"{profile.SampledSeconds:0} seconds of {track.Duration.TotalSeconds:0}"),
            ("Sample loudness", profile is null ? null : $"{profile.SampleRmsDb:0.0} dBFS average (RMS), peaking at {profile.SamplePeakDb:0.0} dBFS")));
        return section;
    }

    private static StackPanel Rhythm(AudioAnalysis? head, AudioAnalysis? tail)
    {
        var section = Section("Tempo, Key and Beats",
            "Found at the opening and the ending, where one song meets the next: AutoMix uses them to land mixes on the beat and in key. " +
            "Each tick is a beat, as tall as it is loud; the bar lines are the strong ones.");
        var columns = new Grid { ColumnSpacing = 24, RowSpacing = 24 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var opening = Stretch("Opening", head);
        var ending = Stretch("Ending", tail);
        Grid.SetColumn(ending, 1);
        columns.Children.Add(opening);
        columns.Children.Add(ending);
        columns.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 560;
            if (narrow && columns.RowDefinitions.Count == 0)
            {
                columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            Grid.SetColumn(ending, narrow ? 0 : 1);
            Grid.SetRow(ending, narrow ? 1 : 0);
            Grid.SetColumnSpan(opening, narrow ? 2 : 1);
            Grid.SetColumnSpan(ending, narrow ? 2 : 1);
        };
        section.Children.Add(columns);
        return section;
    }

    private static StackPanel Stretch(string title, AudioAnalysis? a)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = title, Style = Style("MelodayOverlineText") });
        if (a is null)
        {
            panel.Children.Add(Caption("Not analysed yet"));
            return panel;
        }
        var key = a.Key >= 0 && a.KeyConfidence >= 0.2
            ? $"{AudioAnalysis.KeyName(a.Key)} (Camelot {Camelot(a.Key)}) · {a.KeyConfidence:P0} sure"
            : "Not clear enough to name";
        panel.Children.Add(new TextBlock
        {
            Text = a.HasBeat ? $"{a.Bpm:0.0} BPM" : "No steady beat",
            Style = Style("MelodayTitleText"),
        });
        panel.Children.Add(new BeatGrid { Analysis = a });
        panel.Children.Add(Facts(
            ("Tempo", a.HasBeat ? $"{a.Bpm:0.0} BPM · {a.TempoConfidence:P0} sure · a beat every {a.BeatPeriod * 1000:0} ms" : $"Not steady enough ({a.TempoConfidence:P0} sure)"),
            ("Key", key),
            ("Beats", $"{a.Beats.Count:N0} beats, {a.Downbeats.Count():N0} bar lines"),
            ("Stretch", $"{BeatGrid.Time(a.From)} to {BeatGrid.Time(a.To)} ({a.To - a.From:0} seconds)"),
            ("Loudness", a.BeatEnergy.Count > 0 ? $"beats from {a.BeatEnergy.Min():0.0} dB to {a.BeatEnergy.Max():0.0} dB of the loudest" : null)));
        return panel;
    }

    private static string Camelot(int key)
    {
        var (number, major) = AudioAnalysis.Camelot(key);
        return $"{number}{(major ? "B" : "A")}";
    }

    private static Grid MatchRow(Track other, double match)
    {
        var art = new ArtworkImage
        {
            Width = 40,
            Height = 40,
            DecodeSize = 80,
            SourcePath = other.DisplayThumb,
            PlaceholderIcon = "Music",
            ArtworkCornerRadius = (CornerRadius)Application.Current.Resources["MelodayRadiusThumb"],
        };
        var labels = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = other.Title, Style = Style("MelodayBodyText"), TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = other.DisplayArtist, Style = Style("MelodaySecondaryText"), TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        var percent = Math.Clamp(match, 0, 1);
        var meter = new StackPanel
        {
            Width = 140,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = $"{percent:P0} match", Style = Style("MelodaySecondaryText"), HorizontalAlignment = HorizontalAlignment.Right },
                new ProgressBar { Minimum = 0, Maximum = 100, Value = percent * 100 },
            },
        };
        Grid.SetColumn(labels, 1);
        Grid.SetColumn(meter, 2);
        var row = new Grid { ColumnSpacing = 12, Children = { art, labels, meter } };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, $"{other.Title} by {other.DisplayArtist}, {percent:P0} match");
        return row;
    }
}
