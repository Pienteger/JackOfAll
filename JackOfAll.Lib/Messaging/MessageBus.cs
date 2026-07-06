using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;

namespace JackOfAll.Lib.Messaging;

public sealed class MessageBus : IMessageBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly Dispatcher _dispatcher;

    public MessageBus()
    {
        _dispatcher = Application.Current.Dispatcher;
    }

    public void Publish<T>(T message) where T : IToolMessage
    {
        if (!_handlers.TryGetValue(typeof(T), out var handlers))
            return;

        List<Delegate> snapshot;
        lock (handlers)
        {
            snapshot = [.. handlers];
        }

        foreach (var handler in snapshot)
        {
            var h = (Action<T>)handler;
            _dispatcher.InvokeAsync(() => h(message));
        }
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : IToolMessage
    {
        var handlers = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());
        lock (handlers)
        {
            handlers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        });
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDispose();
        }
    }
}
