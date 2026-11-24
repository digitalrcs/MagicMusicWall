using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private ListeningSession? _queueUndo;
    private int _queueUndoPlayVersion;
    private long _queueRevision;
    private Point _queueDragStart;
    private int _queueDragIndex = -1;
    private long _queueDragRevision;
    private const string QueueDragFormat = "MagicMusicWall.QueueOccurrence";
    private sealed record QueueDrag(MainWindow Owner, long Revision, int Index);

    private void InitializeQueueInteractions()
    {
        _queue.CollectionChanged += (_, _) => _queueRevision++;
        QueueList.AllowDrop = true;
        QueueList.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _queueDragStart = e.GetPosition(QueueList);
            _queueDragIndex = QueueRowIndex(e.OriginalSource);
            _queueDragRevision = _queueRevision;
        };
        QueueList.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _queueDragIndex < 0 || _queueDragRevision != _queueRevision || _queueChanging || _closing) return;
            var point = e.GetPosition(QueueList);
            if (Math.Abs(point.X - _queueDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(point.Y - _queueDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var data = new QueueDrag(this, _queueRevision, _queueDragIndex);
            _queueDragIndex = -1;
            DragDrop.DoDragDrop(QueueList, new DataObject(QueueDragFormat, data), DragDropEffects.Move);
        };
        QueueList.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(QueueDragFormat)) return;
            e.Effects = e.Data.GetData(QueueDragFormat) is QueueDrag data && ReferenceEquals(data.Owner, this)
                && data.Revision == _queueRevision && QueueRowIndex(e.OriginalSource) >= 0 ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        QueueList.Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent(QueueDragFormat)) return;
            e.Handled = true;
            if (e.Data.GetData(QueueDragFormat) is not QueueDrag data || !ReferenceEquals(data.Owner, this) || data.Revision != _queueRevision) return;
            var target = QueueRowIndex(e.OriginalSource);
            if (target >= 0) MoveQueueItem(data.Index, target);
        };
    }

    private int QueueRowIndex(object source)
    {
        var row = source is DependencyObject element ? ItemsControl.ContainerFromElement(QueueList, element) as ListBoxItem : null;
        return row is null ? -1 : QueueList.ItemContainerGenerator.IndexFromContainer(row);
    }

    private void QueueSearch_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; FindNextQueueMatch(); } }
    private void QueueSearchNext_Click(object sender, RoutedEventArgs e) => FindNextQueueMatch();
    private void FindNextQueueMatch()
    {
        var query = QueueSearchBox.Text.Trim();
        if (query.Length == 0 || _queue.Count == 0) return;
        var start = Math.Max(0, QueueList.SelectedIndex + 1);
        for (var offset = 0; offset < _queue.Count; offset++)
        {
            var index = (start + offset) % _queue.Count;
            var item = _queue[index];
            if (!$"{item.Title} {item.Artist} {item.Album}".Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
            QueueList.SelectedIndex = index; ScrollQueueIndexIntoView(index);
            Status($"Queue match {index + 1:N0} of {_queue.Count:N0}: {item.Title}. Playback is unchanged.");
            return;
        }
        Status("No queue matches. Playback is unchanged.");
    }

    private void ScrollQueueIndexIntoView(int index)
    {
        if (index < 0 || index >= _queue.Count) return;
        QueueList.UpdateLayout();
        VirtualizingPanel? FindPanel(DependencyObject node)
        {
            if (node is VirtualizingPanel panel) return panel;
            for (var child = 0; child < VisualTreeHelper.GetChildrenCount(node); child++)
                if (FindPanel(VisualTreeHelper.GetChild(node, child)) is { } found) return found;
            return null;
        }
        FindPanel(QueueList)?.BringIndexIntoViewPublic(index);
    }

    private void RememberQueueUndo()
    {
        _queueUndo = CaptureSession();
        _queueUndoPlayVersion = _playVersion;
    }

    private async void UndoQueue_Click(object sender, RoutedEventArgs e)
    { try { await UndoQueueAsync(); } catch (Exception ex) { Error(ex); } }

    private async Task UndoQueueAsync()
    {
        if (_queueChanging || _closing || _queueUndo is null) return;
        var restore = _queueUndo.Resolve(_catalog.Document.Items.Select(i => i.Id));
        var preservePlayback = _playVersion == _queueUndoPlayVersion && restore.CurrentIndex >= 0
            && _current?.Id == restore.ItemIds[restore.CurrentIndex];
        _queueUndo = null;
        _queueChanging = true;
        try
        {
            if (!preservePlayback)
            {
                ++_playVersion;
                await Playback(() => { _player.Stop(); return true; });
            }
            var lookup = _catalog.Document.Items.ToDictionary(i => i.Id);
            _queue.Clear(); foreach (var id in restore.ItemIds) _queue.Add(lookup[id]);
            _queueIndex = restore.CurrentIndex;
            _current = _queueIndex >= 0 ? _queue[_queueIndex] : null;
            if (!preservePlayback) _pendingSessionPosition = _current is null ? null : restore.PositionMilliseconds;
            QueueList.SelectedIndex = _queueIndex;
            NowTitle.Text = _current?.Title ?? "Nothing playing yet";
            NowArtist.Text = _current?.Artist ?? "Choose something you love";
            Status(preservePlayback ? "Queue edit undone. Playback continues." : "Queue edit undone. Press Play when ready.");
        }
        finally { _queueChanging = false; }
        await SaveListeningSessionAsync();
    }

    private void PlayNext_Click(object sender, RoutedEventArgs e) => QueueNextItems(Selection());

    private async Task ActivateLibraryItem(object item)
    {
        if (_queueChanging || _closing) return;
        if (item is FolderEntry folder) { OpenFolder(folder.Path); return; }
        if (item is not MediaItem media) return;
        var index = _queueIndex >= 0 && _queueIndex < _queue.Count && _queue[_queueIndex].Id == media.Id
            ? _queueIndex : _queue.ToList().FindIndex(i => i.Id == media.Id);
        if (index < 0) { RememberQueueUndo(); _queue.Add(media); index = _queue.Count - 1; }
        await PlayIndex(index, 0);
    }

    private void QueueNextItems(IEnumerable<MediaItem> items)
    {
        if (_queueChanging || _closing) return;
        var additions = items.ToList();
        if (additions.Count == 0) return;
        RememberQueueUndo();
        var insert = Math.Clamp(_queueIndex + 1, 0, _queue.Count);
        foreach (var item in additions) _queue.Insert(insert++, item);
        Status($"Added {additions.Count:N0} items to play next.");
    }

    private void MoveQueueItem(int fromIndex, int toIndex)
    {
        if (_queueChanging || _closing || fromIndex < 0 || fromIndex >= _queue.Count || toIndex < 0 || toIndex >= _queue.Count || fromIndex == toIndex) return;
        RememberQueueUndo();
        _queue.Move(fromIndex, toIndex);
        _queueIndex = QueueEditing.MovedCurrentIndex(_queueIndex, fromIndex, toIndex);
        QueueList.SelectedIndex = toIndex;
    }

    private int[] SelectedQueueIndices()
    {
        // Read realized containers first so repeated references still identify the clicked occurrence.
        var selected = new List<int>();
        var remaining = QueueList.SelectedItems.OfType<MediaItem>().ToList();
        for (var i = 0; i < _queue.Count; i++)
            if (QueueList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem { IsSelected: true })
            { selected.Add(i); remaining.Remove(_queue[i]); }
        for (var i = 0; i < _queue.Count && remaining.Count > 0; i++)
            if (!selected.Contains(i) && QueueList.ItemContainerGenerator.ContainerFromIndex(i) is null && remaining.Remove(_queue[i])) selected.Add(i);
        return selected.Order().ToArray();
    }

    private async Task RemoveQueueItems(IEnumerable<int> indices)
    {
        if (_queueChanging || _closing) return;
        var removing = indices.Distinct().Where(i => i >= 0 && i < _queue.Count).Order().ToArray();
        if (removing.Length == 0) return;
        RememberQueueUndo();
        _queueChanging = true;
        try
        {
            var currentRemoved = removing.Contains(_queueIndex);
            var wasPlaying = _player.IsPlaying;
            if (currentRemoved)
            {
                ++_playVersion;
                await Playback(() => { _player.Stop(); return true; });
                _current = null; _pendingSessionPosition = null;
            }
            var priorIndex = _queueIndex;
            foreach (var index in removing.Reverse()) _queue.RemoveAt(index);
            _queueIndex = currentRemoved ? -1 : priorIndex - removing.Count(i => i < priorIndex);
            if (currentRemoved && _queue.Count > 0)
            {
                var next = Math.Min(priorIndex - removing.Count(i => i < priorIndex), _queue.Count - 1);
                if (wasPlaying) await PlayIndex(next, 0);
                else
                {
                    _queueIndex = next; _current = _queue[next]; _pendingSessionPosition = 0;
                    NowTitle.Text = _current.Title; NowArtist.Text = _current.Artist;
                }
            }
            if (_queue.Count == 0)
            {
                _queueIndex = -1; _current = null; _pendingSessionPosition = null;
                NowTitle.Text = "Nothing playing yet"; NowArtist.Text = "Queue is empty";
            }
            QueueList.SelectedIndex = _queueIndex;
            Status($"Removed {removing.Length:N0} queue items. Undo queue edit restores them.");
        }
        finally { _queueChanging = false; }
    }

    private async void QueueDeduplicate_Click(object sender, RoutedEventArgs e)
    { try { await RemoveQueueDuplicates(); } catch (Exception ex) { Error(ex); } }

    private Task RemoveQueueDuplicates()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Keep the current occurrence when a song appears more than once.
        if (_current is not null) seen.Add(_current.Id);
        var remove = Enumerable.Range(0, _queue.Count).Where(i => i != _queueIndex && !seen.Add(_queue[i].Id)).ToArray();
        return RemoveQueueItems(remove);
    }
}
