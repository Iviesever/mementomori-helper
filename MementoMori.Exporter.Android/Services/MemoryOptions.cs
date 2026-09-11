using MementoMori.Option;

namespace MementoMori.Exporter.Android.Services;

// Deliberately not WritableOptions<T>: the latter writes plaintext credentials to disk.
public sealed class MemoryOptions<T>(T initial) : IWritableOptions<T> where T : class, new()
{
    private readonly object gate = new();
    public T Value { get; private set; } = initial;
    public void Replace(T value) { lock (gate) Value = value; }
    public void Update(Action<T> applyChanges) { lock (gate) applyChanges(Value); }
}
