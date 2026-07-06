namespace JackOfAll.Lib.Messaging;

public interface IMessageBus
{
    void Publish<T>(T message) where T : IToolMessage;
    IDisposable Subscribe<T>(Action<T> handler) where T : IToolMessage;
}
