namespace LokalneAI.Gateway;

/// <summary>Skąd przyszło zapytanie.</summary>
public enum RequestOrigin
{
    /// <summary>Program na tym komputerze (okno LokalneAI, Tekla, inne programy z biblioteką).</summary>
    ThisComputer,

    /// <summary>Inny komputer z podsieci biura.</summary>
    OtherComputer,
}

/// <summary>Stan zapytania w kolejce.</summary>
public enum QueueState
{
    /// <summary>Czeka na wolne miejsce w Ollamie tego PC.</summary>
    Waiting,

    /// <summary>Liczy Ollama tego PC.</summary>
    Running,

    /// <summary>Wysłane do bramy innego komputera; tam czeka albo liczy.</summary>
    Remote,
}

/// <summary>Jedno zapytanie widoczne w kolejce. Bez treści — tylko kto, czym i od kiedy.</summary>
public sealed class QueueItem
{
    private static long _nextId;

    /// <summary>Tworzy wpis.</summary>
    public QueueItem(RequestOrigin origin, string computer, string address, string program, string? model, DateTimeOffset arrived)
    {
        Id = Interlocked.Increment(ref _nextId);
        Origin = origin;
        Computer = computer;
        Address = address;
        Program = program;
        Model = model;
        Arrived = arrived;
    }

    /// <summary>Numer wpisu (do „Odrzuć”).</summary>
    public long Id { get; }

    /// <summary>Ten PC czy inny komputer.</summary>
    public RequestOrigin Origin { get; }

    /// <summary>Nazwa komputera, który pyta.</summary>
    public string Computer { get; }

    /// <summary>Adres IP, z którego przyszło zapytanie.</summary>
    public string Address { get; }

    /// <summary>Program, który pyta (nagłówek <c>X-LokalneAI-Program</c>), np. „Czat”, „Code: tekla”.</summary>
    public string Program { get; }

    /// <summary>Model z zapytania.</summary>
    public string? Model { get; }

    /// <summary>Kiedy przyszło.</summary>
    public DateTimeOffset Arrived { get; }

    /// <summary>Stan.</summary>
    public QueueState State { get; internal set; }

    /// <summary>Kiedy zaczęło liczyć (null = jeszcze czeka).</summary>
    public DateTimeOffset? Started { get; internal set; }

    /// <summary>Komputer, który liczy, gdy <see cref="State"/> = <see cref="QueueState.Remote"/>.</summary>
    public string? RemoteComputer { get; internal set; }

    internal CancellationTokenSource Cancellation { get; } = new();

    internal bool Rejected { get; set; }
}

/// <summary>
/// Kolejka zapytań do Ollamy tego PC. Zapytania z tego komputera idą przed zapytaniami innych komputerów,
/// ale zapytanie z innego komputera, które czeka dłużej niż <see cref="GatewayOptions.OthersYieldSeconds"/>,
/// już nie ustępuje. W obrębie jednej grupy kolejność przyjścia.
/// </summary>
public sealed class RequestQueue
{
    private readonly object _lock = new();
    private readonly List<(QueueItem Item, TaskCompletionSource Ready)> _waiting = [];
    private readonly List<QueueItem> _active = [];
    private readonly Func<DateTimeOffset> _now;
    private int _running;
    private long _servedForOthers;

    /// <summary>Tworzy kolejkę. <paramref name="now"/> podmienia zegar w testach.</summary>
    public RequestQueue(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.Now);

    /// <summary>Ile zapytań naraz idzie do Ollamy.</summary>
    public int Parallel { get; set; } = 1;

    /// <summary>Po ilu sekundach zapytanie z innego komputera przestaje ustępować.</summary>
    public int OthersYieldSeconds { get; set; } = 60;

    /// <summary>Kolejka się zmieniła (wątek w tle).</summary>
    public event EventHandler? Changed;

    /// <summary>Ile zapytań innych komputerów czeka.</summary>
    public int WaitingFromOthers
    {
        get
        {
            lock (_lock)
            {
                return _waiting.Count(w => w.Item.Origin == RequestOrigin.OtherComputer);
            }
        }
    }

    /// <summary>Ile zapytań innych komputerów policzył ten PC od uruchomienia.</summary>
    public long ServedForOthers => Interlocked.Read(ref _servedForOthers);

    /// <summary>Teraz w kolejce: najpierw liczone, potem czekające w kolejności obsługi.</summary>
    public IReadOnlyList<QueueItem> Snapshot()
    {
        lock (_lock)
        {
            var now = _now();
            var waiting = new List<QueueItem>(_waiting.Select(w => w.Item));
            var ordered = new List<QueueItem>();
            while (waiting.Count > 0)
            {
                var next = PickNext(waiting, now);
                ordered.Add(next);
                waiting.Remove(next);
            }

            return _active.Concat(ordered).ToList();
        }
    }

    /// <summary>
    /// Czeka na swoją kolej. Zwraca przepustkę, której zwolnienie (Dispose) wpuszcza następne zapytanie.
    /// Anulowanie <paramref name="cancellationToken"/> (klient się rozłączył) albo „Odrzuć” usuwa zapytanie z kolejki.
    /// </summary>
    public async Task<IDisposable> EnterAsync(QueueItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            item.State = QueueState.Waiting;
            _waiting.Add((item, ready));
            Dispatch();
        }

        RaiseChanged();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, item.Cancellation.Token);
        using (linked.Token.Register(() => Abandon(item, ready)))
        {
            await ready.Task.ConfigureAwait(false);
        }

        return new Lease(this, item);
    }

    /// <summary>Wpis o zapytaniu wysłanym do innego komputera (bez czekania tutaj). Dispose usuwa wpis.</summary>
    public IDisposable TrackRemote(QueueItem item, string remoteComputer)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_lock)
        {
            item.State = QueueState.Remote;
            item.RemoteComputer = remoteComputer;
            item.Started = _now();
            _active.Add(item);
        }

        RaiseChanged();
        return new Lease(this, item, countsAsRunning: false);
    }

    /// <summary>Odrzuca zapytanie innego komputera (czekające albo liczone). Zapytań z tego PC nie odrzuca.</summary>
    public bool Reject(long id)
    {
        QueueItem? item;
        lock (_lock)
        {
            item = _waiting.Select(w => w.Item).Concat(_active).FirstOrDefault(i => i.Id == id && i.Origin == RequestOrigin.OtherComputer);
            if (item is null)
            {
                return false;
            }

            item.Rejected = true;
        }

        item.Cancellation.Cancel();
        return true;
    }

    private void Abandon(QueueItem item, TaskCompletionSource ready)
    {
        bool removed;
        lock (_lock)
        {
            removed = _waiting.RemoveAll(w => w.Item == item) > 0;
        }

        if (removed)
        {
            ready.TrySetCanceled();
            RaiseChanged();
        }
    }

    private void Release(QueueItem item, bool countsAsRunning)
    {
        lock (_lock)
        {
            if (!_active.Remove(item))
            {
                return;
            }

            if (countsAsRunning)
            {
                _running--;
                if (item.Origin == RequestOrigin.OtherComputer && !item.Rejected)
                {
                    Interlocked.Increment(ref _servedForOthers);
                }
            }

            Dispatch();
        }

        RaiseChanged();
    }

    /// <summary>Wpuszcza czekające zapytania, dopóki są wolne miejsca. Wywoływać pod blokadą.</summary>
    private void Dispatch()
    {
        var now = _now();
        while (_running < Math.Max(1, Parallel) && _waiting.Count > 0)
        {
            var next = PickNext(_waiting.Select(w => w.Item).ToList(), now);
            var index = _waiting.FindIndex(w => w.Item == next);
            var ready = _waiting[index].Ready;
            _waiting.RemoveAt(index);
            next.State = QueueState.Running;
            next.Started = now;
            _active.Add(next);
            _running++;
            ready.TrySetResult();
        }
    }

    private QueueItem PickNext(List<QueueItem> waiting, DateTimeOffset now)
    {
        var firstOther = waiting.FirstOrDefault(i => i.Origin == RequestOrigin.OtherComputer);
        var firstMine = waiting.FirstOrDefault(i => i.Origin == RequestOrigin.ThisComputer);
        if (firstMine is null)
        {
            return firstOther!;
        }

        // Zapytanie innego komputera, które czeka już długo, wraca do zwykłej kolejności przyjścia.
        var otherWaitedLong = firstOther is not null && now - firstOther.Arrived >= TimeSpan.FromSeconds(OthersYieldSeconds);
        return otherWaitedLong && firstOther!.Arrived <= firstMine.Arrived ? firstOther : firstMine;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class Lease(RequestQueue queue, QueueItem item, bool countsAsRunning = true) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                queue.Release(item, countsAsRunning);
            }
        }
    }
}
