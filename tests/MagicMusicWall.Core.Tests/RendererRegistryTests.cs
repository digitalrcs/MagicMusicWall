using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class RendererRegistryTests
{
    private static RendererDescriptor Living(string? icon = "http://192.0.2.10:8008/icon.png") => new("Living Room", "chromecast", icon);
    private sealed class Reference : IDisposable { public int Disposals { get; private set; } public void Dispose() => ++Disposals; }

    [Fact]
    public void RemovedSelectedReceiverRemainsVisibleAndSelectedButCannotStartNewWork()
    {
        using var registry = new RendererRegistry<Reference>();
        var generation = registry.BeginDiscovery();
        var row = registry.Add(generation, 101, Living(), new Reference())!;
        registry.Select(row.Id);
        registry.Remove(generation, 101);
        Assert.Same(row, Assert.Single(registry.Entries));
        Assert.Equal(row.Id, registry.SelectedId);
        Assert.False(row.IsAvailable);
        Assert.Contains("unavailable", row.DisplayName);
        Assert.Null(row.Acquire());
    }

    [Fact]
    public void ReappearanceUpdatesSameRowAndSelectionWhileQueuedOldLeasesRemainValid()
    {
        using var registry = new RendererRegistry<Reference>();
        var generation = registry.BeginDiscovery();
        var first = new Reference(); var next = new Reference();
        var row = registry.Add(generation, 101, Living(), first)!;
        registry.Select(row.Id);
        var leaseA = row.Acquire()!; var leaseB = row.Acquire()!;
        registry.Remove(generation, 101);
        var refreshed = registry.Add(generation, 201, Living("http://192.0.2.10:8008/new-icon.png"), next);
        Assert.Same(row, refreshed); Assert.Single(registry.Entries); Assert.Equal(row.Id, registry.SelectedId);
        Assert.True(row.IsAvailable); Assert.Equal(0, first.Disposals);
        Assert.Same(first, leaseB.Value);
        leaseA.Dispose(); Assert.Equal(0, first.Disposals);
        leaseB.Dispose(); leaseB.Dispose(); Assert.Equal(1, first.Disposals);
        using var fresh = row.Acquire(); Assert.Same(next, fresh!.Value);
        registry.Remove(generation, 101); Assert.True(row.IsAvailable); // stale delete after replacement
    }

    [Fact]
    public void SameNamedReceiversAtDifferentAddressesStayDistinct()
    {
        using var registry = new RendererRegistry<Reference>();
        var generation = registry.BeginDiscovery();
        var rowA = registry.Add(generation, 101, Living(), new Reference())!;
        registry.Select(rowA.Id);
        var rowB = registry.Add(generation, 102, Living("http://192.0.2.11:8008/icon.png"), new Reference())!;
        Assert.Equal(2, registry.Entries.Count); Assert.NotEqual(rowA.Id, rowB.Id);
        Assert.NotEqual(rowA.DisplayName, rowB.DisplayName); Assert.Equal(rowA.Id, registry.SelectedId);
    }

    [Fact]
    public void MissingAddressCannotSilentlyRebindSelectedReceiverByFriendlyName()
    {
        using var registry = new RendererRegistry<Reference>();
        var generation = registry.BeginDiscovery();
        var selected = registry.Add(generation, 101, Living(null), new Reference())!;
        registry.Select(selected.Id); registry.Remove(generation, 101);
        var incoming = registry.Add(generation, 201, Living(null), new Reference())!;
        Assert.NotSame(selected, incoming); Assert.Equal(selected.Id, registry.SelectedId);
        Assert.False(selected.IsAvailable); Assert.True(incoming.IsAvailable);
        Assert.NotEqual(selected.DisplayName, incoming.DisplayName);
    }

    [Fact]
    public void RefreshPreservesSelectionAndIgnoresOldGenerationCallbacksAndPointerReuse()
    {
        using var registry = new RendererRegistry<Reference>();
        var oldGeneration = registry.BeginDiscovery();
        var selected = registry.Add(oldGeneration, 101, Living(), new Reference())!;
        registry.Select(selected.Id);
        var generation = registry.BeginDiscovery();
        Assert.False(selected.IsAvailable); Assert.Equal(selected.Id, registry.SelectedId);
        var stale = new Reference();
        Assert.Null(registry.Add(oldGeneration, 102, Living(), stale)); Assert.Equal(1, stale.Disposals);
        var bedroom = registry.Add(generation, 101, new("Bedroom", "chromecast", "http://192.0.2.20:8008/icon.png"), new Reference())!;
        Assert.NotSame(selected, bedroom);
        Assert.Null(registry.Remove(oldGeneration, 101)); Assert.True(bedroom.IsAvailable);
        var reappeared = registry.Add(generation, 201, Living(), new Reference());
        Assert.Same(selected, reappeared);
    }

    [Fact]
    public void NativePointerReuseAfterRemovalMarksTheNewReceiverUnavailable()
    {
        using var registry = new RendererRegistry<Reference>();
        var generation = registry.BeginDiscovery();
        registry.Add(generation, 101, Living(), new Reference()); registry.Remove(generation, 101);
        var bedroom = registry.Add(generation, 101, new("Bedroom", "chromecast", "http://192.0.2.20/icon.png"), new Reference())!;
        Assert.Same(bedroom, registry.Remove(generation, 101)); Assert.False(bedroom.IsAvailable);
    }

    [Fact]
    public void DuplicateNativeAnnouncementReleasesOnlyItsExtraReference()
    {
        using var registry = new RendererRegistry<Reference>();
        var generation = registry.BeginDiscovery();
        var original = new Reference(); var duplicate = new Reference();
        var row = registry.Add(generation, 101, Living(), original);
        Assert.Same(row, registry.Add(generation, 101, Living(), duplicate));
        Assert.Single(registry.Entries); Assert.Equal(0, original.Disposals); Assert.Equal(1, duplicate.Disposals);
    }

    [Fact]
    public void ChurnDoesNotAccumulateRetiredNativeReferences()
    {
        using var registry = new RendererRegistry<Reference>();
        var generation = registry.BeginDiscovery();
        var retired = new List<Reference>();
        RendererEntry<Reference>? row = null;
        for (var i = 1; i <= 100; ++i)
        {
            var resource = new Reference(); retired.Add(resource);
            row = registry.Add(generation, i, Living(), resource);
            registry.Remove(generation, i);
        }
        Assert.Single(registry.Entries);
        Assert.All(retired.Take(99), r => Assert.Equal(1, r.Disposals));
        Assert.Equal(0, retired[^1].Disposals);
        registry.Dispose(); Assert.All(retired, r => Assert.Equal(1, r.Disposals));
    }

    [Fact]
    public void BoundedHistoryProtectsSelectedOfflineRowAndReleasesEvictedResources()
    {
        using var registry = new RendererRegistry<Reference>(2);
        var generation = registry.BeginDiscovery();
        var selectedResource = new Reference(); var evicted = new Reference();
        var selected = registry.Add(generation, 101, Living(), selectedResource)!;
        registry.Select(selected.Id); registry.Remove(generation, 101);
        registry.Add(generation, 102, new("Bedroom", "chromecast", null), evicted); registry.Remove(generation, 102);
        registry.Add(generation, 201, new("Kitchen", "chromecast", null), new Reference());
        Assert.Equal(2, registry.Entries.Count); Assert.Contains(selected, registry.Entries);
        Assert.Equal(0, selectedResource.Disposals); Assert.Equal(1, evicted.Disposals);
        var rejected = new Reference();
        Assert.Null(registry.Add(generation, 301, new("Office", "chromecast", null), rejected));
        Assert.Equal(1, rejected.Disposals);
    }

    [Fact]
    public void RegistryShutdownWaitsForAnAlreadyCapturedLeaseBeforeRelease()
    {
        var registry = new RendererRegistry<Reference>();
        var generation = registry.BeginDiscovery(); var resource = new Reference();
        var lease = registry.Add(generation, 101, Living(), resource)!.Acquire()!;
        registry.Dispose(); registry.Dispose(); Assert.Equal(0, resource.Disposals);
        Assert.Same(resource, lease.Value); lease.Dispose(); Assert.Equal(1, resource.Disposals);
        var late = new Reference(); Assert.Null(registry.Add(generation, 201, Living(), late)); Assert.Equal(1, late.Disposals);
    }
}
