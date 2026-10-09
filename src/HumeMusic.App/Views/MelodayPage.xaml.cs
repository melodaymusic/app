using System.Numerics;
using CommunityToolkit.Mvvm.Input;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using HumeMusic.Core.Services;
using HumeMusic.Helpers;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class MelodayPage : Page
{
    public MelodayPage()
    {
        ViewModel = App.Services.GetRequiredService<MelodayViewModel>();
        MarkDupeCommand = new AsyncRelayCommand<Track>(MarkDupeAsync);
        InitializeComponent();
    }

    /// <summary>"This is a dupe": a row's button asks which song it is the same as, then merges the two.</summary>
    public IAsyncRelayCommand<Track> MarkDupeCommand { get; }

    public MelodayViewModel ViewModel { get; }

    private LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void OnPlay(object? sender, EventArgs e)
    {
        Actions.RecordPlayed(MelodayService.Station);
        if (ViewModel.Tracks.Count > 0) _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(ViewModel.Tracks.ToList());
    }

    private void OnShuffle(object? sender, EventArgs e)
    {
        Actions.RecordPlayed(MelodayService.Station);
        if (ViewModel.Tracks.Count > 0) _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(ViewModel.Tracks.ToList(), 0, true);
    }

    private async void OnRetry(object? sender, EventArgs e) => await ViewModel.LoadAsync(fresh: true);

    private async Task MarkDupeAsync(Track? marked)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        if (marked is null || session.Server is not { } server) return;
        var choice = await DupeDialog.ShowAsync(marked, ViewModel.Tracks.ToList());
        if (choice is null) return;
        var pair = new DupePair(server.MachineIdentifier, Side(choice.Keeper), Side(choice.Dupe), DateTimeOffset.Now);
        var store = App.Services.GetRequiredService<DupeStore>();
        if (!store.Add(pair))
        {
            App.Services.GetRequiredService<NotificationService>().ShowWarning("Can't merge those", "Those two are already merged the other way round. Unmerge them in Settings, under Dupes, first.");
            return;
        }
        var saved = App.Services.GetRequiredService<MelodayService>().ApplyDupesToCurrentAsync();
        await MergeRowsAsync(choice);
        ViewModel.ShowCount();
        await saved;
    }

    private static DupeSide Side(Track t) => new(t.RatingKey, t.Title, t.DisplayArtist, t.AlbumTitle, t.DisplayThumb);

    private int IndexOf(Track t)
    {
        for (var i = 0; i < ViewModel.Tracks.Count; i++)
            if (ViewModel.Tracks[i].RatingKey == t.RatingKey) return i;
        return -1;
    }

    private ListViewItem? Container(int index) => index < 0 ? null : TrackList.ContainerFromIndex(index) as ListViewItem;

    /// <summary>
    /// The two rows becoming one: the lower one slides up into the upper, shrinking and fading, the upper one
    /// swells as it takes it in, and then the list holds one row where there were two (the keeper, wherever the pair first stood).
    /// A song whose twin isn't in the Meloday just changes into the keeper, or, if it is the keeper, pulses.
    /// </summary>
    private async Task MergeRowsAsync(DupeChoice choice)
    {
        var tracks = ViewModel.Tracks;
        var keeperAt = IndexOf(choice.Keeper);
        var dupeAt = IndexOf(choice.Dupe);

        if (keeperAt >= 0 && dupeAt >= 0)
        {
            var upper = Math.Min(keeperAt, dupeAt);
            var lower = Math.Max(keeperAt, dupeAt);
            var upperRow = Container(upper);
            var lowerRow = Container(lower);
            if (upperRow is not null && lowerRow is not null)
                await Task.WhenAll(SlideInto(lowerRow, upperRow), Swell(upperRow));
            Reset(upperRow);
            Reset(lowerRow);
            if (dupeAt < keeperAt) tracks[upper] = choice.Keeper;
            tracks.RemoveAt(lower);
            return;
        }
        if (dupeAt >= 0)
        {
            var row = Container(dupeAt);
            if (row is not null) await Fade(row, 0);
            tracks[dupeAt] = choice.Keeper;
            Reset(row);
            if (row is not null) _ = Fade(row, 1, from: 0);
            return;
        }
        if (keeperAt >= 0 && Container(keeperAt) is { } keeperRow)
        {
            await Swell(keeperRow);
            Reset(keeperRow);
        }
    }

    private static Visual Prepare(ListViewItem row)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(row, true);
        var visual = ElementCompositionPreview.GetElementVisual(row);
        visual.CenterPoint = new Vector3((float)row.ActualWidth / 2, (float)row.ActualHeight / 2, 0);
        return visual;
    }

    /// <summary>Rows are recycled for other songs: whatever the animation did to one is undone.</summary>
    private static void Reset(ListViewItem? row)
    {
        if (row is null) return;
        var visual = ElementCompositionPreview.GetElementVisual(row);
        visual.StopAnimation("Translation");
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Scale");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Opacity = 1;
        visual.Scale = Vector3.One;
    }

    /// <summary>Starts animations on a row's visual; completes when they have all finished (at once, with motion off).</summary>
    private static Task Run(Visual visual, Action<Compositor, Visual> animate)
    {
        if (!Motion.IsEnabled) return Task.CompletedTask;
        var done = new TaskCompletionSource();
        var batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => done.TrySetResult();
        animate(visual.Compositor, visual);
        batch.End();
        return done.Task;
    }

    private static CubicBezierEasingFunction Ease(Compositor c) =>
        c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f));

    /// <summary>The row slides to where another row is, shrinking a little and fading as it goes.</summary>
    private static Task SlideInto(ListViewItem row, ListViewItem target)
    {
        var distance = (float)target.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        var visual = Prepare(row);
        return Run(visual, (c, v) =>
        {
            var duration = TimeSpan.FromMilliseconds(Motion.Deliberate);
            var move = c.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(1, new Vector3(0, distance, 0), Ease(c));
            move.Duration = duration;
            v.StartAnimation("Translation", move);
            var fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0.55f, 1);
            fade.InsertKeyFrame(1, 0, Ease(c));
            fade.Duration = duration;
            v.StartAnimation("Opacity", fade);
            var shrink = c.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(1, new Vector3(0.96f, 0.96f, 1), Ease(c));
            shrink.Duration = duration;
            v.StartAnimation("Scale", shrink);
        });
    }

    /// <summary>The row swells and settles, taking something in.</summary>
    private static Task Swell(ListViewItem row)
    {
        var visual = Prepare(row);
        return Run(visual, (c, v) =>
        {
            var swell = c.CreateVector3KeyFrameAnimation();
            swell.InsertKeyFrame(0.6f, new Vector3(1.025f, 1.025f, 1), Ease(c));
            swell.InsertKeyFrame(1, Vector3.One, Ease(c));
            swell.Duration = TimeSpan.FromMilliseconds(Motion.Deliberate);
            v.StartAnimation("Scale", swell);
        });
    }

    private static Task Fade(ListViewItem row, float to, float? from = null)
    {
        var visual = Prepare(row);
        if (!Motion.IsEnabled)
        {
            visual.Opacity = to;
            return Task.CompletedTask;
        }
        return Run(visual, (c, v) =>
        {
            var fade = c.CreateScalarKeyFrameAnimation();
            if (from is { } start) fade.InsertKeyFrame(0, start);
            fade.InsertKeyFrame(1, to);
            fade.Duration = TimeSpan.FromMilliseconds(Motion.Standard);
            v.StartAnimation("Opacity", fade);
        });
    }
}
