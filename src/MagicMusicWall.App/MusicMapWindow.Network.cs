using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

internal sealed partial class MusicMapWindow
{
    private readonly TextBlock _networkCaption = Text("Your library connections", 12, true);
    private readonly DispatcherTimer _motionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _motionClock = new();
    private readonly Dictionary<string, Button> _networkButtons = new(StringComparer.Ordinal);
    private readonly List<(Line Line, MusicMapConnection Connection)> _networkLines = [];
    private ConstellationLayout? _layout;
    private Button _motionButton = null!;
    private bool _motionPaused = !SystemParameters.ClientAreaAnimation;
    private string? _grabbedId, _hoveredId;
    private Point _grabOrigin;
    private Vector _grabOffset;
    private bool _nodeDragged;
    private TimeSpan _previousFrame;

    private void InitializeNetworkMotion()
    {
        _motionButton.Content = _motionPaused ? "Resume motion" : "Pause motion";
        AutomationProperties.SetName(_motionButton, (string)_motionButton.Content);
        _motionTimer.Tick += (_, _) =>
        {
            if (_layout is null || _closed || _busy || !IsVisible || WindowState == WindowState.Minimized)
            { StopNetworkMotion(); return; }
            var now = _motionClock.Elapsed;
            var elapsed = Math.Clamp((now - _previousFrame).TotalSeconds, 1.0 / 240, 1.0 / 30);
            _previousFrame = now;
            _layout.Step(elapsed);
            RenderNetworkFrame();
            if (_layout.IsSettled && _grabbedId is null) StopNetworkMotion();
        };
        StateChanged += (_, _) => UpdateNetworkMotion();
        IsVisibleChanged += (_, _) => UpdateNetworkMotion();
    }

    private void ToggleMotion()
    {
        _motionPaused = !_motionPaused;
        _motionButton.Content = _motionPaused ? "Resume motion" : "Pause motion";
        AutomationProperties.SetName(_motionButton, (string)_motionButton.Content);
        if (!_motionPaused) _layout?.Reheat();
        UpdateNetworkMotion();
    }

    private void StopNetworkMotion() { _motionTimer.Stop(); _motionClock.Stop(); }

    private void UpdateNetworkMotion()
    {
        if (_layout is null || _motionPaused || _closed || _busy || !IsVisible || WindowState == WindowState.Minimized || _layout.IsSettled)
        { StopNetworkMotion(); return; }
        if (_motionTimer.IsEnabled) return;
        _motionClock.Restart(); _previousFrame = TimeSpan.Zero; _motionTimer.Start();
    }

    private void ClearNetwork()
    {
        StopNetworkMotion();
        // Releasing a captured old button also ends its drag before replacing the model.
        foreach (var button in _networkButtons.Values) if (button.IsMouseCaptured) button.ReleaseMouseCapture();
        _grabbedId = _hoveredId = null; _nodeDragged = false;
        _layout = null; _networkButtons.Clear(); _networkLines.Clear(); _map.Children.Clear();
        _networkCaption.Text = "Your library connections";
    }

    private void RenderNetwork(MusicMapEntity selected, IReadOnlyList<MusicMapMatch> matches)
    {
        var previous = _layout?.Nodes.ToDictionary(n => n.Id, n => new ConstellationPoint(n.X, n.Y), StringComparer.Ordinal);
        ClearNetwork();
        _map.Width = Math.Max(760, _mapScroll.ActualWidth - 22);
        _map.Height = Math.Max(520, _mapScroll.ActualHeight - 22);
        var entities = new[] { selected }.Concat(matches.Select(m => m.Entity)).ToArray();
        var allConnections = _index!.Connections(entities.Select(e => e.Id), Similarity);
        var links = allConnections.Where(c => c.SourceId == selected.Id || c.TargetId == selected.Id).ToList();
        var crossLinks = allConnections.Where(c => c.SourceId != selected.Id && c.TargetId != selected.Id).ToArray();
        // A sparse wireframe preserves real neighbor-to-neighbor relationships without an unreadable complete mesh.
        foreach (var entity in entities.Skip(1))
            foreach (var link in crossLinks.Where(c => c.SourceId == entity.Id || c.TargetId == entity.Id)
                .OrderByDescending(c => c.Strength).ThenBy(c => c.SourceId, StringComparer.Ordinal).ThenBy(c => c.TargetId, StringComparer.Ordinal).Take(2))
                if (!links.Contains(link)) links.Add(link);
        var specs = entities.Select(e => new ConstellationNodeSpec(e.Id, e.Id == selected.Id ? 180 : 150, e.Id == selected.Id ? 58 : 46, e.Id == selected.Id)).ToArray();
        _layout = new ConstellationLayout(specs, links.Select(c => new ConstellationLink(c.SourceId, c.TargetId, c.Strength)).ToArray(), _map.Width, _map.Height, previous);
        _networkCaption.Text = $"{selected.Kind} · {Similarity} · {matches.Count} related · {links.Count} links · stronger links pull closer";
        foreach (var link in links)
        {
            var focusLink = link.SourceId == selected.Id || link.TargetId == selected.Id;
            var line = new Line
            {
                Stroke = ConnectionBrush(link.Reason), StrokeThickness = (focusLink ? 1.15 : .65) + 1.5 * link.Strength,
                Opacity = focusLink ? .52 : .22, IsHitTestVisible = false
            };
            _networkLines.Add((line, link)); _map.Children.Add(line);
        }
        foreach (var entity in entities)
        {
            var focused = entity.Id == selected.Id;
            var match = allConnections.FirstOrDefault(c => (c.SourceId == selected.Id && c.TargetId == entity.Id) || (c.TargetId == selected.Id && c.SourceId == entity.Id));
            var reason = focused ? "Selected music" : match is null ? "Related music" : $"Similarity strength {match.Strength * 100:0}/100 (relative relevance, not a probability)\n{match.Reason}";
            AddNetworkNode(entity, focused, match is null ? Brush("#B8A5FF") : ConnectionBrush(match.Reason), reason);
        }
        // Reduced motion still gets a similarity-based layout, computed without visible animation.
        if (_motionPaused) for (var i = 0; i < 1200 && !_layout.IsSettled; i++) _layout.Step(1.0 / 60);
        RenderNetworkFrame(); UpdateNetworkMotion();
    }

    private void AddNetworkNode(MusicMapEntity entity, bool focused, Brush color, string reason)
    {
        var width = focused ? 180d : 150d; var height = focused ? 58d : 46d;
        var content = new Grid { Width = width, Height = height, Background = Brushes.Transparent };
        var dot = new Ellipse { Width = focused ? 11 : 7, Height = focused ? 11 : 7, Fill = color, Stroke = Brush("#141923"), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
        content.Children.Add(dot);
        var label = Text(entity.Name, focused ? 21 : 18);
        label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.TextAlignment = TextAlignment.Center; label.FontWeight = focused ? FontWeights.SemiBold : FontWeights.Normal;
        label.Foreground = focused ? Brush("#D8C9FF") : Brush("#F4F3F8");
        var labelBackground = new Border { Background = Brush("#141923"), Padding = new Thickness(3, 0, 3, 0), Child = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        content.Children.Add(labelBackground);
        var button = new Button { Content = content, Width = width, Height = height, MinHeight = 44, Background = Brushes.Transparent, Padding = new Thickness(0), ToolTip = entity.Name + "\n" + entity.Artist + "\n" + reason + "\nClick to explore. Drag to move." };
        AutomationProperties.SetName(button, focused ? "Selected " + entity.Kind + " " + entity.Name : "Explore related " + entity.Name);
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _grabbedId = entity.Id; _grabOrigin = e.GetPosition(_mapScroll); _nodeDragged = false;
            var node = _layout?.Nodes.FirstOrDefault(n => n.Id == entity.Id);
            if (node is not null)
            {
                _grabOffset = new Point(node.X, node.Y) - e.GetPosition(_map);
                _layout!.Drag(entity.Id, node.X, node.Y);
            }
        };
        button.PreviewMouseMove += (_, e) =>
        {
            if (_grabbedId != entity.Id || e.LeftButton != MouseButtonState.Pressed || _layout is null) return;
            var movement = e.GetPosition(_mapScroll) - _grabOrigin;
            if (!_nodeDragged && movement.Length < 5) return;
            _nodeDragged = true;
            var point = e.GetPosition(_map);
            _layout.Drag(entity.Id, point.X + _grabOffset.X, point.Y + _grabOffset.Y);
            RenderNetworkFrame(); UpdateNetworkMotion(); e.Handled = true;
        };
        button.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_grabbedId != entity.Id) return;
            var dragged = _nodeDragged;
            if (dragged) e.Handled = true; // Suppress Click even if dragged back to the starting point.
            _layout?.Release(entity.Id); _grabbedId = null;
            // Ordinary release must reach ButtonBase with capture intact so it can raise Click.
            if (dragged) button.ReleaseMouseCapture();
            _nodeDragged = false; UpdateNetworkMotion();
        };
        button.LostMouseCapture += (_, _) =>
        {
            if (_grabbedId != entity.Id) return;
            _layout?.Release(entity.Id); _grabbedId = null; _nodeDragged = false; UpdateNetworkMotion();
        };
        button.Click += (_, _) => { if (!focused) Reveal(entity); };
        button.MouseEnter += (_, _) => { _hoveredId = entity.Id; label.FontWeight = FontWeights.SemiBold; RenderNetworkFrame(); };
        button.MouseLeave += (_, _) => { if (_hoveredId == entity.Id) _hoveredId = null; label.FontWeight = focused ? FontWeights.SemiBold : FontWeights.Normal; RenderNetworkFrame(); };
        _networkButtons.Add(entity.Id, button); _map.Children.Add(button);
    }

    private void RenderNetworkFrame()
    {
        if (_layout is null) return;
        var nodes = _layout.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var node in _layout.Nodes)
            if (_networkButtons.TryGetValue(node.Id, out var button))
            { Canvas.SetLeft(button, node.X - node.Width / 2); Canvas.SetTop(button, node.Y - node.Height / 2); }
        foreach (var (line, link) in _networkLines)
        {
            var a = nodes[link.SourceId]; var b = nodes[link.TargetId];
            line.X1 = a.X; line.Y1 = a.Y - a.Height / 2 + 7;
            line.X2 = b.X; line.Y2 = b.Y - b.Height / 2 + 7;
            line.Opacity = _hoveredId is not null ? (a.Id == _hoveredId || b.Id == _hoveredId ? .9 : .1) : a.IsFocus || b.IsFocus ? .52 : .22;
        }
    }
}
