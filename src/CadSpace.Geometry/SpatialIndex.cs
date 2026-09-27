namespace CadSpace.Geometry;

/// <summary>Immutable flat bounding-volume hierarchy. Queries append original item indices to caller-owned storage.</summary>
public sealed class SpatialIndex
{
    private readonly record struct Node(Bounds3 Bounds, int Start, int Count, int Escape);
    private readonly Bounds3[] _bounds;
    private readonly int[] _items;
    private readonly Node[] _nodes;
    public int Count => _bounds.Length;
    public SpatialIndex(IEnumerable<Bounds3> bounds)
    {
        _bounds = bounds.ToArray();
        if (_bounds.Length > 2_000_000 || _bounds.Any(b => b.IsEmpty || !b.Min.IsFinite || !b.Max.IsFinite))
            throw new ArgumentException("The spatial index needs finite nonempty bounds, at most two million items.");
        _items = Enumerable.Range(0, _bounds.Length).ToArray();
        var nodes = new List<Node>();
        var comparers = Enumerable.Range(0, 3).Select(axis => new CenterComparer(_bounds, axis)).ToArray();
        int Build(int start, int count)
        {
            var box = Bounds3.Empty; for (var i = start; i < start + count; i++) box = box.Union(_bounds[_items[i]]);
            var index = nodes.Count; nodes.Add(default);
            if (count <= 8) nodes[index] = new(box, start, count, index + 1);
            else
            {
                var size = box.Size; var axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
                var half = count / 2;
                SelectMedian(_items, start, count, start + half, comparers[axis]);
                Build(start, half); Build(start + half, count - half);
                nodes[index] = new(box, 0, 0, nodes.Count);
            }
            return index;
        }
        if (_bounds.Length > 0) Build(0, _bounds.Length);
        _nodes = nodes.ToArray();
    }
    private sealed class CenterComparer(Bounds3[] bounds, int axis) : IComparer<int>
    {
        private double Center(int item)
        {
            var b = bounds[item];
            return axis == 0 ? b.Min.X / 2 + b.Max.X / 2 : axis == 1 ? b.Min.Y / 2 + b.Max.Y / 2 : b.Min.Z / 2 + b.Max.Z / 2;
        }
        public int Compare(int a, int b) { var c = Center(a).CompareTo(Center(b)); return c == 0 ? a.CompareTo(b) : c; }
    }
    // Partition at the exact median rather than re-sorting every full subtree. The iteration
    // budget falls back to the platform introsort for pathological inputs; tree depth stays bounded.
    private static void SelectMedian(int[] items, int start, int count, int median, IComparer<int> compare)
    {
        var left = start; var right = start + count - 1; var budget = 2 * (int)Math.Log2(count) + 2;
        void Swap(int a, int b) => (items[a], items[b]) = (items[b], items[a]);
        while (left < right)
        {
            if (right - left < 24 || budget-- == 0) { Array.Sort(items, left, right - left + 1, compare); return; }
            var middle = left + (right - left) / 2;
            if (compare.Compare(items[left], items[middle]) > 0) Swap(left, middle);
            if (compare.Compare(items[left], items[right]) > 0) Swap(left, right);
            if (compare.Compare(items[middle], items[right]) > 0) Swap(middle, right);
            var pivot = items[middle]; var i = left; var j = right;
            while (i <= j)
            {
                while (compare.Compare(items[i], pivot) < 0) i++;
                while (compare.Compare(items[j], pivot) > 0) j--;
                if (i <= j) { Swap(i, j); i++; j--; }
            }
            if (median <= j) right = j;
            else if (median >= i) left = i;
            else return;
        }
    }
    public int Query(Bounds3 box, List<int> result, bool xyOnly = false)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Visit(b => b.IntersectsXY(box) && (xyOnly || b.Max.Z >= box.Min.Z && b.Min.Z <= box.Max.Z), result);
    }
    /// <summary>Conservative custom broad phase. Return true for any bounds that might contain a hit.</summary>
    public int Visit(Func<Bounds3, bool> intersects, List<int> result)
    {
        ArgumentNullException.ThrowIfNull(intersects); ArgumentNullException.ThrowIfNull(result);
        var tested = 0;
        for (var i = 0; i < _nodes.Length;)
        {
            var node = _nodes[i]; tested++;
            if (!intersects(node.Bounds)) { i = node.Escape; continue; }
            for (var j = node.Start; j < node.Start + node.Count; j++)
                if (intersects(_bounds[_items[j]])) result.Add(_items[j]);
            i++;
        }
        return tested;
    }
    public int Query(Ray3 ray, List<int> result, double maximumDistance = double.PositiveInfinity)
    {
        if (!ray.Origin.IsFinite || !ray.Direction.IsFinite || ray.Direction.Length == 0 || double.IsNaN(maximumDistance) || maximumDistance < 0) throw new ArgumentException("Invalid ray.");
        return Visit(b => Intersects(ray, b, maximumDistance), result);
    }
    public static bool Intersects(Ray3 ray, Bounds3 box, double maximumDistance = double.PositiveInfinity)
    {
        double near = 0, far = maximumDistance;
        bool Slab(double origin, double direction, double min, double max)
        {
            if (direction == 0) return origin >= min && origin <= max;
            var a = (min - origin) / direction; var b = (max - origin) / direction;
            if (a > b) (a, b) = (b, a);
            near = Math.Max(near, a); far = Math.Min(far, b); return far >= near;
        }
        return Slab(ray.Origin.X, ray.Direction.X, box.Min.X, box.Max.X) && Slab(ray.Origin.Y, ray.Direction.Y, box.Min.Y, box.Max.Y) && Slab(ray.Origin.Z, ray.Direction.Z, box.Min.Z, box.Max.Z);
    }
}
