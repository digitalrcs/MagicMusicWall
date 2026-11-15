namespace MagicMusicWall.Core;

public sealed record ConstellationNodeSpec(string Id, double Width, double Height, bool IsFocus);
public sealed record ConstellationLink(string SourceId, string TargetId, double Strength);
public sealed record ConstellationPoint(double X, double Y);

/// <summary>X and Y are the label's center coordinates in canvas units.</summary>
public sealed class ConstellationNode
{
    public string Id { get; }
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Width { get; }
    public double Height { get; }
    public bool IsFocus { get; }
    internal double Vx, Vy, Ax, Ay;
    internal bool Dragged;
    internal ConstellationNode(ConstellationNodeSpec spec)
    { Id = spec.Id; Width = spec.Width; Height = spec.Height; IsFocus = spec.IsFocus; }
}

/// <summary>
/// Deterministic, bounded spring layout of a small visible constellation. Stronger
/// relationships have shorter resting lengths; label spacing and other edges also
/// affect the final geometry. It does not embed an entire library or imply exact distances.
/// </summary>
public sealed class ConstellationLayout
{
    private const double Tick = 1.0 / 120, Gap = 8;
    private readonly double _width, _height;
    private readonly ConstellationNode[] _nodes;
    private readonly Dictionary<string, ConstellationNode> _byId;
    private readonly Spring[] _springs;
    private double _pending, _age;
    private int _quietTicks;
    public IReadOnlyList<ConstellationNode> Nodes { get; }
    public bool IsSettled { get; private set; }

    public ConstellationLayout(IReadOnlyList<ConstellationNodeSpec> nodes, IReadOnlyList<ConstellationLink> links,
        double width, double height, IReadOnlyDictionary<string, ConstellationPoint>? previous = null)
    {
        ArgumentNullException.ThrowIfNull(nodes); ArgumentNullException.ThrowIfNull(links);
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas dimensions must be finite and positive.");
        if (nodes.Count > 32) throw new ArgumentException("A visible constellation is limited to 32 nodes.", nameof(nodes));
        if (nodes.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || !double.IsFinite(x.Width) || x.Width <= 0
            || !double.IsFinite(x.Height) || x.Height <= 0) || nodes.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != nodes.Count
            || nodes.Count(x => x.IsFocus) > 1)
            throw new ArgumentException("Nodes need unique IDs, positive finite dimensions, and at most one focus.", nameof(nodes));
        _width = width; _height = height;
        _nodes = nodes.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => new ConstellationNode(x)).ToArray();
        Nodes = Array.AsReadOnly(_nodes);
        _byId = _nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var node in _nodes)
        {
            var hash = Hash(node.Id);
            node.X = width * (.12 + .76 * ((hash & 65535) / 65535.0));
            node.Y = height * (.12 + .76 * ((hash >> 16) / 65535.0));
            if (previous is not null && previous.TryGetValue(node.Id, out var position)
                && position is not null && double.IsFinite(position.X) && double.IsFinite(position.Y))
            { node.X = position.X; node.Y = position.Y; }
            if (node.IsFocus) { node.X = width / 2; node.Y = height / 2; }
            Bound(node);
        }
        var unique = new Dictionary<(string, string), ConstellationLink>();
        foreach (var link in links)
        {
            if (link is null || link.SourceId is null || link.TargetId is null || link.SourceId == link.TargetId
                || !_byId.ContainsKey(link.SourceId) || !_byId.ContainsKey(link.TargetId) || !double.IsFinite(link.Strength) || link.Strength <= 0) continue;
            var key = string.CompareOrdinal(link.SourceId, link.TargetId) < 0 ? (link.SourceId, link.TargetId) : (link.TargetId, link.SourceId);
            if (!unique.TryGetValue(key, out var existing) || existing.Strength < link.Strength) unique[key] = link;
        }
        _springs = unique.OrderBy(x => x.Key.Item1, StringComparer.Ordinal).ThenBy(x => x.Key.Item2, StringComparer.Ordinal).Select(pair =>
        {
            var a = _byId[pair.Key.Item1]; var b = _byId[pair.Key.Item2]; var strength = Math.Clamp(pair.Value.Strength, 0, 1);
            var focus = a.IsFocus || b.IsFocus;
            var rest = Math.Min(width, height) * (.18 + .38 * (1 - strength));
            return new Spring(a, b, rest, focus ? 26 + 12 * strength : 2 + 3 * strength);
        }).ToArray();
        IsSettled = _nodes.Length == 0;
    }

    public void Step(double seconds)
    {
        if (IsSettled || !double.IsFinite(seconds) || seconds <= 0) return;
        _pending += Math.Min(seconds, .05);
        while (_pending >= Tick)
        {
            _pending -= Tick;
            Advance();
            if (IsSettled) { _pending = 0; break; }
        }
    }

    public void Drag(string id, double x, double y)
    {
        if (id is null || !_byId.TryGetValue(id, out var node) || !double.IsFinite(x) || !double.IsFinite(y)) return;
        node.Dragged = true; node.X = x; node.Y = y; node.Vx = node.Vy = 0; Bound(node); Reheat();
    }

    public void Release(string id)
    {
        if (id is null || !_byId.TryGetValue(id, out var node)) return;
        node.Dragged = false;
        if (node.IsFocus) { node.X = _width / 2; node.Y = _height / 2; node.Vx = node.Vy = 0; }
        Reheat();
    }

    public void Reheat() { _age = 0; _quietTicks = 0; _pending = 0; IsSettled = _nodes.Length == 0; }

    private void Advance()
    {
        _age += Tick;
        var heat = _age <= 3 ? 1 : Math.Exp(-( _age - 3) / 3);
        var old = _nodes.Select(n => (n.X, n.Y)).ToArray();
        foreach (var node in _nodes)
        {
            node.Ax = (_width / 2 - node.X) * .08;
            node.Ay = (_height / 2 - node.Y) * .08;
        }
        foreach (var spring in _springs)
        {
            Direction(spring.A, spring.B, out var x, out var y, out var distance);
            var force = Math.Clamp((distance - spring.Rest) * spring.Stiffness, -12000, 12000);
            spring.A.Ax += x * force; spring.A.Ay += y * force;
            spring.B.Ax -= x * force; spring.B.Ay -= y * force;
        }
        for (var i = 0; i < _nodes.Length; i++)
            for (var j = i + 1; j < _nodes.Length; j++)
            {
                var a = _nodes[i]; var b = _nodes[j];
                Direction(a, b, out var x, out var y, out var distance);
                var force = 1_800_000 / (distance * distance + 1600);
                a.Ax -= x * force; a.Ay -= y * force; b.Ax += x * force; b.Ay += y * force;
            }
        var damping = Math.Exp(-4.8 * Tick);
        foreach (var node in _nodes)
        {
            if (Fixed(node)) { node.Vx = node.Vy = 0; continue; }
            node.Vx = Math.Clamp((node.Vx + node.Ax * heat * Tick) * damping, -1000, 1000);
            node.Vy = Math.Clamp((node.Vy + node.Ay * heat * Tick) * damping, -1000, 1000);
            node.X += node.Vx * Tick; node.Y += node.Vy * Tick;
            Bound(node);
        }
        Separate();
        double movement = 0;
        for (var i = 0; i < _nodes.Length; i++)
            movement = Math.Max(movement, Math.Abs(_nodes[i].X - old[i].X) + Math.Abs(_nodes[i].Y - old[i].Y));
        _quietTicks = movement < .025 && _age > 1 ? _quietTicks + 1 : 0;
        if (_quietTicks > 60 || _age > 18)
        {
            ResolveRemainingOverlaps();
            IsSettled = true;
            foreach (var node in _nodes) node.Vx = node.Vy = 0;
        }
    }

    private void Separate()
    {
        // Project label rectangles apart; the focus and a held label have zero mobility.
        // Eight passes also resolve chains against canvas edges without an unbounded loop.
        for (var pass = 0; pass < 8; pass++)
        {
            var changed = false;
            for (var i = 0; i < _nodes.Length; i++)
                for (var j = i + 1; j < _nodes.Length; j++)
                {
                    var a = _nodes[i]; var b = _nodes[j];
                    var overlapX = (a.Width + b.Width) / 2 + Gap - Math.Abs(b.X - a.X);
                    var overlapY = (a.Height + b.Height) / 2 + Gap - Math.Abs(b.Y - a.Y);
                    if (overlapX <= .001 || overlapY <= .001) continue;
                    var movableA = Fixed(a) ? 0 : 1; var movableB = Fixed(b) ? 0 : 1;
                    var total = movableA + movableB;
                    if (total == 0) continue;
                    changed = true;
                    if (overlapX < overlapY)
                    {
                        var direction = b.X >= a.X ? 1 : -1;
                        a.X -= direction * overlapX * movableA / total; b.X += direction * overlapX * movableB / total;
                        if (movableA > 0) a.Vx *= .45; if (movableB > 0) b.Vx *= .45;
                    }
                    else
                    {
                        var direction = b.Y >= a.Y ? 1 : -1;
                        a.Y -= direction * overlapY * movableA / total; b.Y += direction * overlapY * movableB / total;
                        if (movableA > 0) a.Vy *= .45; if (movableB > 0) b.Vy *= .45;
                    }
                    Bound(a); Bound(b);
                }
            if (!changed) break;
        }
    }

    private void Bound(ConstellationNode node)
    {
        var x = node.Width >= _width ? _width / 2 : Math.Clamp(node.X, node.Width / 2, _width - node.Width / 2);
        var y = node.Height >= _height ? _height / 2 : Math.Clamp(node.Y, node.Height / 2, _height - node.Height / 2);
        if (x != node.X) node.Vx *= -.2; if (y != node.Y) node.Vy *= -.2;
        node.X = x; node.Y = y;
    }

    private void ResolveRemainingOverlaps()
    {
        // Dense contact chains can jam against a boundary even though free space exists
        // alongside them. At rest, move only intersecting labels to the nearest legal
        // rectangle boundary intersection. This finishes contact resolution without
        // letting a cooled simulation freeze overlapping text.
        var placed = new List<ConstellationNode>();
        foreach (var node in _nodes.OrderByDescending(Fixed).ThenBy(n =>
            (n.X - _width / 2) * (n.X - _width / 2) + (n.Y - _height / 2) * (n.Y - _height / 2)).ThenBy(n => n.Id, StringComparer.Ordinal))
        {
            bool Clear(double x, double y) => placed.All(other => Math.Abs(other.X - x) >= (other.Width + node.Width) / 2 + Gap - .001
                || Math.Abs(other.Y - y) >= (other.Height + node.Height) / 2 + Gap - .001);
            if (!Fixed(node) && node.Width <= _width && node.Height <= _height && !Clear(node.X, node.Y))
            {
                var xs = new List<double> { node.X, node.Width / 2, _width - node.Width / 2 };
                var ys = new List<double> { node.Y, node.Height / 2, _height - node.Height / 2 };
                foreach (var other in placed)
                {
                    xs.Add(other.X - (other.Width + node.Width) / 2 - Gap); xs.Add(other.X + (other.Width + node.Width) / 2 + Gap);
                    ys.Add(other.Y - (other.Height + node.Height) / 2 - Gap); ys.Add(other.Y + (other.Height + node.Height) / 2 + Gap);
                }
                var bestX = node.X; var bestY = node.Y; var best = double.PositiveInfinity;
                foreach (var x in xs.Where(x => x >= node.Width / 2 && x <= _width - node.Width / 2))
                    foreach (var y in ys.Where(y => y >= node.Height / 2 && y <= _height - node.Height / 2))
                    {
                        var distance = (x - node.X) * (x - node.X) + (y - node.Y) * (y - node.Y);
                        if (distance < best && Clear(x, y)) { best = distance; bestX = x; bestY = y; }
                    }
                node.X = bestX; node.Y = bestY;
            }
            placed.Add(node);
        }
    }

    private static bool Fixed(ConstellationNode node) => node.IsFocus || node.Dragged;
    private static void Direction(ConstellationNode a, ConstellationNode b, out double x, out double y, out double distance)
    {
        x = b.X - a.X; y = b.Y - a.Y; distance = Math.Sqrt(x * x + y * y);
        if (distance < .001)
        { var angle = Hash(a.Id + "|" + b.Id) / (double)uint.MaxValue * Math.PI * 2; x = Math.Cos(angle); y = Math.Sin(angle); distance = 0; }
        else { x /= distance; y /= distance; }
    }

    private static uint Hash(string value)
    {
        uint hash = 2166136261;
        foreach (var character in value) { hash ^= character; hash *= 16777619; }
        hash ^= hash >> 16; hash *= 0x7feb352d; hash ^= hash >> 15; hash *= 0x846ca68b; hash ^= hash >> 16;
        return hash;
    }

    private sealed record Spring(ConstellationNode A, ConstellationNode B, double Rest, double Stiffness);
}
