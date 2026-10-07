using System.Diagnostics.CodeAnalysis;

namespace Mortz.Core.Features;

internal interface IPlayerTable
{
    void Forget(object player);

    void Close(List<Exception> errors);

    string Describe();
}

/// <summary>One feature's per-player state, keyed by reference. The scope drops and disposes entries.</summary>
public sealed class PlayerTable<TPlayer, TState> : IPlayerTable
    where TPlayer : class
    where TState : class, new()
{
    private readonly Dictionary<TPlayer, TState> _entries = new(ReferenceEqualityComparer.Instance);
    private bool _closed;

    internal PlayerTable()
    {
    }

    public TState this[TPlayer player]
    {
        get
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!_entries.TryGetValue(player, out TState? state))
            {
                state = new TState();
                _entries.Add(player, state);
            }
            return state;
        }
    }

    public int Count => _entries.Count;

    public IEnumerable<KeyValuePair<TPlayer, TState>> Entries => _entries;

    public bool TryGet(TPlayer player, [NotNullWhen(true)] out TState? state)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        return _entries.TryGetValue(player, out state);
    }

    void IPlayerTable.Forget(object player)
    {
        if (player is TPlayer key && _entries.Remove(key, out TState? state) && state is IDisposable disposable)
            disposable.Dispose();
    }

    void IPlayerTable.Close(List<Exception> errors)
    {
        if (_closed)
            return;
        _closed = true;
        foreach (TState state in _entries.Values)
        {
            if (state is not IDisposable disposable)
                continue;
            try { disposable.Dispose(); }
            catch (Exception exception) { errors.Add(exception); }
        }
        _entries.Clear();
    }

    string IPlayerTable.Describe() => $"{typeof(TState).Name}[{_entries.Count}]";
}
