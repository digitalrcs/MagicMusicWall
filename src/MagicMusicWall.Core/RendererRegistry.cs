using System.ComponentModel;
using System.Net;

namespace MagicMusicWall.Core;

public sealed record RendererDescriptor(string Name, string Type, string? IconUri);

/// <summary>
/// A bounded discovery history. Call registry operations from one UI thread.
/// Resource leases may be released from any thread after queued native work completes.
/// </summary>
public sealed class RendererRegistry<T> : IDisposable where T : class, IDisposable
{
    private readonly List<RendererEntry<T>> _entries = [];
    private readonly int _capacity;
    private long _sequence;
    private bool _disposed;
    public RendererRegistry(int capacity = 64) => _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public IReadOnlyList<RendererEntry<T>> Entries => _entries;
    public int Generation { get; private set; }
    public string? SelectedId { get; private set; }

    public int BeginDiscovery()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ++Generation;
        foreach (var entry in _entries) entry.SetAvailable(false);
        return Generation;
    }

    public void Select(string? id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (id is not null && !_entries.Any(e => e.Id == id)) throw new ArgumentException("Unknown receiver.", nameof(id));
        SelectedId = id;
    }

    /// <summary>Takes ownership of the incoming reference, including duplicate/stale callbacks.</summary>
    public RendererEntry<T>? Add(int generation, long nativeId, RendererDescriptor descriptor, T resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (_disposed || generation != Generation || nativeId == 0) { resource.Dispose(); return null; }
        var duplicate = _entries.FirstOrDefault(e => e.Generation == generation && e.NativeId == nativeId && e.IsAvailable);
        if (duplicate is not null) { resource.Dispose(); return duplicate; }

        var name = string.IsNullOrWhiteSpace(descriptor.Name) ? "Unnamed receiver" : descriptor.Name.Trim();
        var type = descriptor.Type?.Trim() ?? "";
        var endpoint = Endpoint(descriptor.IconUri);
        var sameName = _entries.Where(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase)).ToList();
        RendererEntry<T>? entry = null;
        if (endpoint is not null)
            entry = sameName.FirstOrDefault(e => e.Endpoint == endpoint);
        else if (sameName.Count == 1 && !sameName[0].IsAvailable && !sameName[0].AmbiguousName && sameName[0].Endpoint is null && sameName[0].Id != SelectedId)
            // Without an address/UUID, a same-name discovery must not silently replace
            // the selected target. The user can explicitly choose its new available row.
            entry = sameName[0];

        if (entry is null)
        {
            if (_entries.Count >= _capacity)
            {
                var oldest = _entries.Where(e => !e.IsAvailable && e.Id != SelectedId).OrderBy(e => e.LastSeen).FirstOrDefault();
                if (oldest is null) { resource.Dispose(); return null; }
                _entries.Remove(oldest); oldest.Retire();
            }
            entry = new RendererEntry<T>(Guid.NewGuid().ToString("N"), name, type, endpoint, ++_sequence);
            _entries.Add(entry);
            foreach (var other in sameName.Where(_entries.Contains)) { other.MarkAmbiguous(); entry.MarkAmbiguous(); }
        }
        entry.Replace(generation, nativeId, resource, ++_sequence);
        return entry;
    }

    public RendererEntry<T>? Remove(int generation, long nativeId)
    {
        if (_disposed || generation != Generation) return null;
        var entry = _entries.FirstOrDefault(e => e.Generation == generation && e.NativeId == nativeId);
        entry?.MarkRemoved();
        return entry;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries) entry.Retire();
        _entries.Clear();
    }

    private static string? Endpoint(string? iconUri)
    {
        if (!Uri.TryCreate(iconUri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)) return null;
        // VLC's mDNS plugin builds icon URLs from the receiver address. Shared CDN
        // icon names and friendly names alone are not strong receiver identities.
        return address.ToString();
    }
}

public sealed class RendererEntry<T> : INotifyPropertyChanged where T : class, IDisposable
{
    private RendererReference<T>? _reference;
    internal RendererEntry(string id, string name, string type, string? endpoint, long sequence)
    { Id = id; Name = name; Type = type; Endpoint = endpoint; Ordinal = sequence; }
    public string Id { get; }
    public string Name { get; }
    public string Type { get; }
    public string? Endpoint { get; }
    private long Ordinal { get; }
    internal long LastSeen { get; private set; }
    internal long NativeId { get; private set; }
    internal int Generation { get; private set; }
    internal bool AmbiguousName { get; private set; }
    public bool IsAvailable { get; private set; }
    public string DisplayName => Name + (AmbiguousName ? $" · {Endpoint ?? $"receiver {Ordinal}"}" : "") + (IsAvailable ? "" : " · unavailable");
    // Metadata checks only. Use Acquire() to pass a resource to asynchronous native work.
    public T? CurrentResource => _reference?.Value;
    public RendererLease<T>? Acquire() => IsAvailable ? _reference?.Acquire() : null;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void SetAvailable(bool available)
    {
        if (available == IsAvailable) return;
        IsAvailable = available;
        PropertyChanged?.Invoke(this, new(nameof(IsAvailable)));
        PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
    }
    internal void MarkAmbiguous()
    {
        AmbiguousName = true;
        PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
    }
    internal void MarkRemoved() { NativeId = 0; SetAvailable(false); }
    internal void Replace(int generation, long nativeId, T resource, long lastSeen)
    {
        var previous = _reference;
        _reference = new(resource); Generation = generation; NativeId = nativeId; LastSeen = lastSeen;
        SetAvailable(true);
        previous?.Retire();
    }
    internal void Retire() { var previous = _reference; _reference = null; previous?.Retire(); }
}

internal sealed class RendererReference<T>(T value) where T : class, IDisposable
{
    private readonly object _gate = new();
    private int _leases;
    private bool _retired, _disposed;
    public T Value { get; } = value;
    public RendererLease<T>? Acquire()
    {
        lock (_gate)
        {
            if (_retired) return null;
            ++_leases;
            return new(this);
        }
    }
    public void Retire()
    {
        bool dispose;
        lock (_gate) { _retired = true; dispose = _leases == 0 && !_disposed; if (dispose) _disposed = true; }
        if (dispose) Value.Dispose();
    }
    public void Release()
    {
        bool dispose;
        lock (_gate) { --_leases; dispose = _retired && _leases == 0 && !_disposed; if (dispose) _disposed = true; }
        if (dispose) Value.Dispose();
    }
}

public sealed class RendererLease<T> : IDisposable where T : class, IDisposable
{
    private RendererReference<T>? _reference;
    internal RendererLease(RendererReference<T> reference) => _reference = reference;
    public T Value => _reference?.Value ?? throw new ObjectDisposedException(nameof(RendererLease<T>));
    public void Dispose() => Interlocked.Exchange(ref _reference, null)?.Release();
}
