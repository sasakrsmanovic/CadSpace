namespace CadSpace.Rendering;

/// <summary>Per-vertex selection flags, separate from immutable geometry. Tracks only contiguous ranges affected by changed IDs.</summary>
public sealed class SelectionStream
{
    public readonly record struct Range(int Start, int Count);
    private readonly Dictionary<Guid, List<Range>> _ranges = new();
    private HashSet<Guid> _selected = new();
    private Guid _last;
    private int _count;
    public float[] Values { get; private set; } = [];
    public void Clear() { _ranges.Clear(); _selected.Clear(); _count=0; Values=[]; }
    public void AddVertex(Guid id)
    {
        if (!_ranges.TryGetValue(id,out var ranges)) _ranges[id]=ranges=new();
        if (_count>0 && _last==id && ranges.Count>0) {var old=ranges[^1];ranges[^1]=old with{Count=old.Count+1};}
        else ranges.Add(new(_count,1));
        _last=id;_count++;
    }
    public void Allocate() => Values=new float[_count];
    public IReadOnlyList<Range> Update(IReadOnlySet<Guid> selected)
    {
        var changed=new List<Range>();
        foreach(var id in _selected.Except(selected).Concat(selected.Except(_selected)))
            if(_ranges.TryGetValue(id,out var ranges)) foreach(var range in ranges)
            {
                Array.Fill(Values,selected.Contains(id)?1f:0f,range.Start,range.Count); changed.Add(range);
            }
        _selected=selected.ToHashSet(); changed.Sort((a,b)=>a.Start.CompareTo(b.Start)); return changed;
    }
}
