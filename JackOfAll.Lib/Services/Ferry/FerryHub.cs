using System.Text.Json;

namespace JackOfAll.Lib.Services.Ferry;

/// <summary>
/// The shared feed both sides see. Phones long-poll <see cref="WaitState"/>, which returns as soon as anything
/// changes, so new items show up on the phone without a refresh.
/// </summary>
public sealed class FerryHub
{
    private readonly object _gate = new();
    private readonly List<FerryItem> _items = [];
    private int _rev = 1;
    private int _nextId = 1;
    private DateTime _lastSeen = DateTime.MinValue;
    private string _lastDevice = "";

    /// <summary>Raised on the thread that added the item (often a server thread).</summary>
    public event Action<FerryItem>? Added;

    /// <summary>Raised when items are removed or a download is recorded.</summary>
    public event Action? Changed;

    public FerryItem Add(FerryItem item)
    {
        lock (_gate)
        {
            item.Id = _nextId++;
            item.Time = DateTime.Now;
            _items.Add(item);
            _rev++;
            Monitor.PulseAll(_gate);
        }
        Added?.Invoke(item);
        return item;
    }

    public void Remove(int id)
    {
        lock (_gate) { _items.RemoveAll(item => item.Id == id); Bump(); }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate) { _items.Clear(); Bump(); }
        Changed?.Invoke();
    }

    /// <summary>Wakes waiting phones without changing the feed (used when the link changes or the server stops).</summary>
    public void Wake() { lock (_gate) Bump(); }

    private void Bump()
    {
        _rev++;
        Monitor.PulseAll(_gate);
    }

    internal void Downloaded(FerryItem item)
    {
        lock (_gate) item.Downloads++;
        Changed?.Invoke();
    }

    public FerryItem? Find(int id) { lock (_gate) return _items.Find(item => item.Id == id); }

    /// <summary>Newest first.</summary>
    public List<FerryItem> Snapshot() { lock (_gate) return Enumerable.Reverse(_items).ToList(); }

    internal void Seen(string device) { lock (_gate) { _lastSeen = DateTime.Now; _lastDevice = device; } }

    public (DateTime When, string Device) LastSeen { get { lock (_gate) return (_lastSeen, _lastDevice); } }

    /// <summary>Long poll: returns at once if the phone is behind, otherwise waits up to <paramref name="waitMs"/> for a change.</summary>
    internal string WaitState(int since, int waitMs)
    {
        lock (_gate)
        {
            if (since == _rev && waitMs > 0) Monitor.Wait(_gate, waitMs);
            var items = Enumerable.Reverse(_items).Select(static item => new
            {
                id = item.Id,
                text = item.IsText,
                toPhone = item.ToPhone,
                name = item.Name,
                size = item.Size,
                time = new DateTimeOffset(item.Time).ToUnixTimeMilliseconds(),
                image = item.CanThumb,
                body = item.IsText ? item.Text : null,
            });
            return JsonSerializer.Serialize(new { rev = _rev, pc = Environment.MachineName, items });
        }
    }
}
