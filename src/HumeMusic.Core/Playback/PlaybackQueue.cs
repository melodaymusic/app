using HumeMusic.Core.Models;
using HumeMusic.Core.Settings;

namespace HumeMusic.Core.Playback;

/// <summary>One occurrence of a track in the queue. The same track may be queued several times.</summary>
public sealed class QueueEntry
{
    public QueueEntry(Track track, Guid? id = null)
    {
        Track = track;
        Id = id ?? Guid.NewGuid();
    }

    public Guid Id { get; }
    public Track Track { get; }
    public override string ToString() => Track.Title;
}

/// <summary>
/// The play queue: ordering, current position, shuffle and repeat. Pure logic with no playback or
/// UI dependencies so it can be fully unit tested. Not thread-safe; the owner serialises access.
/// </summary>
public sealed class PlaybackQueue
{
    private readonly List<QueueEntry> _items = [];
    // Order the user built, used to restore when shuffle is turned off.
    private readonly List<QueueEntry> _originalOrder = [];
    private readonly Random _random;

    public PlaybackQueue(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    /// <summary>Raised after any change to entries, order or current position.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<QueueEntry> Items => _items;
    public int CurrentIndex { get; private set; } = -1;
    public QueueEntry? Current => CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;
    public bool IsShuffled { get; private set; }
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;
    public int Count => _items.Count;

    public IEnumerable<QueueEntry> History => CurrentIndex > 0 ? _items.Take(CurrentIndex) : [];
    public IEnumerable<QueueEntry> UpNext => CurrentIndex >= 0 ? _items.Skip(CurrentIndex + 1) : _items;

    public bool HasNext => CurrentIndex + 1 < _items.Count || (Repeat == RepeatMode.All && _items.Count > 0);
    public bool HasPrevious => CurrentIndex > 0 || (Repeat == RepeatMode.All && _items.Count > 0);

    /// <summary>Replaces the queue. When <paramref name="shuffle"/> is set the start track plays first.</summary>
    public QueueEntry? SetQueue(IEnumerable<Track> tracks, int startIndex = 0, bool shuffle = false)
    {
        _items.Clear();
        _originalOrder.Clear();
        foreach (var t in tracks) _items.Add(new QueueEntry(t));
        _originalOrder.AddRange(_items);
        IsShuffled = false;
        CurrentIndex = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
        if (shuffle)
        {
            if (startIndex < 0 && _items.Count > 0) CurrentIndex = _random.Next(_items.Count);
            ApplyShuffle();
        }
        OnChanged();
        return Current;
    }

    /// <summary>Restores a persisted queue without reshuffling.</summary>
    public void Restore(IReadOnlyList<Track> items, IReadOnlyList<Track> originalOrder, int currentIndex, bool shuffled)
    {
        _items.Clear();
        _originalOrder.Clear();
        _items.AddRange(items.Select(t => new QueueEntry(t)));
        if (shuffled && originalOrder.Count == items.Count)
        {
            // Map original order onto the same entry instances (tracks can repeat, so match by occurrence).
            var pool = _items.ToList();
            foreach (var t in originalOrder)
            {
                var match = pool.FirstOrDefault(e => e.Track.RatingKey == t.RatingKey);
                if (match is null) break;
                pool.Remove(match);
                _originalOrder.Add(match);
            }
            if (_originalOrder.Count != _items.Count)
            {
                _originalOrder.Clear();
                _originalOrder.AddRange(_items);
            }
        }
        else
        {
            _originalOrder.AddRange(_items);
        }
        IsShuffled = shuffled;
        CurrentIndex = _items.Count == 0 ? -1 : Math.Clamp(currentIndex, 0, _items.Count - 1);
        OnChanged();
    }

    public IReadOnlyList<QueueEntry> OriginalOrder => _originalOrder;

    /// <summary>
    /// Advances the queue. <paramref name="userInitiated"/> distinguishes the Next button (always
    /// moves on) from a track ending naturally (honours repeat-one).
    /// Returns the new current entry, or null when playback should stop.
    /// </summary>
    public QueueEntry? MoveNext(bool userInitiated)
    {
        if (_items.Count == 0) return null;
        if (!userInitiated && Repeat == RepeatMode.One && Current is not null)
        {
            return Current;
        }
        if (CurrentIndex + 1 < _items.Count)
        {
            CurrentIndex++;
        }
        else if (Repeat == RepeatMode.All || (userInitiated && Repeat == RepeatMode.One))
        {
            CurrentIndex = 0;
        }
        else
        {
            return null;
        }
        OnChanged();
        return Current;
    }

    public QueueEntry? MovePrevious()
    {
        if (_items.Count == 0) return null;
        if (CurrentIndex > 0) CurrentIndex--;
        else if (Repeat != RepeatMode.Off) CurrentIndex = _items.Count - 1;
        else CurrentIndex = 0;
        OnChanged();
        return Current;
    }

    public QueueEntry? JumpTo(Guid entryId)
    {
        var index = _items.FindIndex(e => e.Id == entryId);
        if (index < 0) return null;
        CurrentIndex = index;
        OnChanged();
        return Current;
    }

    /// <summary>Appends to the end of the queue.</summary>
    public void Enqueue(IEnumerable<Track> tracks)
    {
        var entries = tracks.Select(t => new QueueEntry(t)).ToList();
        if (entries.Count == 0) return;
        _items.AddRange(entries);
        _originalOrder.AddRange(entries);
        if (CurrentIndex < 0) CurrentIndex = 0;
        OnChanged();
    }

    /// <summary>
    /// Inserts tracks at a position in the play order (clamped to the queue), keeping the current entry
    /// playing. Inserting before the current entry shifts it along.
    /// </summary>
    public void InsertAt(int index, IEnumerable<Track> tracks)
    {
        var entries = tracks.Select(t => new QueueEntry(t)).ToList();
        if (entries.Count == 0) return;
        index = Math.Clamp(index, 0, _items.Count);
        _items.InsertRange(index, entries);
        _originalOrder.AddRange(entries);
        if (CurrentIndex < 0) CurrentIndex = 0;
        else if (index <= CurrentIndex) CurrentIndex += entries.Count;
        OnChanged();
    }

    /// <summary>Inserts immediately after the current entry.</summary>
    public void PlayNext(IEnumerable<Track> tracks)
    {
        var entries = tracks.Select(t => new QueueEntry(t)).ToList();
        if (entries.Count == 0) return;
        var insertAt = CurrentIndex < 0 ? 0 : CurrentIndex + 1;
        _items.InsertRange(insertAt, entries);

        var current = Current;
        var originalIndex = current is null ? -1 : _originalOrder.IndexOf(current);
        _originalOrder.InsertRange(originalIndex + 1, entries);

        if (CurrentIndex < 0) CurrentIndex = 0;
        OnChanged();
    }

    /// <summary>Removes an entry. Returns true when the removed entry was the current one.</summary>
    public bool Remove(Guid entryId)
    {
        var index = _items.FindIndex(e => e.Id == entryId);
        if (index < 0) return false;
        var entry = _items[index];
        _items.RemoveAt(index);
        _originalOrder.Remove(entry);

        var wasCurrent = index == CurrentIndex;
        if (index < CurrentIndex) CurrentIndex--;
        if (_items.Count == 0) CurrentIndex = -1;
        else if (CurrentIndex >= _items.Count) CurrentIndex = Repeat == RepeatMode.All ? 0 : _items.Count - 1;
        OnChanged();
        return wasCurrent;
    }

    /// <summary>Moves an entry to a new index in the visible order, keeping the current entry current.</summary>
    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _items.Count) return;
        toIndex = Math.Clamp(toIndex, 0, _items.Count - 1);
        if (fromIndex == toIndex) return;
        var current = Current;
        var entry = _items[fromIndex];
        _items.RemoveAt(fromIndex);
        _items.Insert(toIndex, entry);
        if (current is not null) CurrentIndex = _items.IndexOf(current);
        if (!IsShuffled)
        {
            _originalOrder.Clear();
            _originalOrder.AddRange(_items);
        }
        OnChanged();
    }

    /// <summary>Clears everything except (optionally) the current entry.</summary>
    public void Clear(bool keepCurrent)
    {
        var current = keepCurrent ? Current : null;
        _items.Clear();
        _originalOrder.Clear();
        if (current is not null)
        {
            _items.Add(current);
            _originalOrder.Add(current);
            CurrentIndex = 0;
        }
        else
        {
            CurrentIndex = -1;
        }
        OnChanged();
    }

    /// <summary>Removes upcoming entries only, keeping history and the current entry.</summary>
    public void ClearUpNext()
    {
        if (CurrentIndex < 0) { Clear(false); return; }
        var removed = _items.Skip(CurrentIndex + 1).ToList();
        _items.RemoveRange(CurrentIndex + 1, _items.Count - CurrentIndex - 1);
        foreach (var e in removed) _originalOrder.Remove(e);
        OnChanged();
    }

    public void SetShuffle(bool enabled)
    {
        if (enabled == IsShuffled) return;
        if (enabled)
        {
            ApplyShuffle();
        }
        else
        {
            var current = Current;
            _items.Clear();
            _items.AddRange(_originalOrder);
            IsShuffled = false;
            CurrentIndex = current is null ? (_items.Count > 0 ? 0 : -1) : _items.IndexOf(current);
        }
        OnChanged();
    }

    /// <summary>
    /// Shuffles everything except the current entry, which moves to the front so the listener
    /// hears every other track exactly once before repeat-all wraps.
    /// </summary>
    private void ApplyShuffle()
    {
        var current = Current;
        var rest = _items.Where(e => !ReferenceEquals(e, current)).ToList();
        for (var i = rest.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (rest[i], rest[j]) = (rest[j], rest[i]);
        }
        _items.Clear();
        if (current is not null) _items.Add(current);
        _items.AddRange(rest);
        CurrentIndex = _items.Count == 0 ? -1 : 0;
        IsShuffled = true;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
