using System.Text;

namespace Mortz.Core.Features;

/// <summary>One lifetime's features, hooks and per-player tables. Scopes chain: root, then one live child.</summary>
public sealed class FeatureScope : IDisposable
{
    private readonly List<object> _features = [];
    private readonly List<object> _attached = [];
    private readonly List<Action> _cleanup = [];
    private readonly List<IPlayerTable> _tables = [];
    private readonly Dictionary<Type, object> _contracts = [];
    private readonly FeatureScope? _parent;
    private readonly Action<object>? _attach;
    private readonly Action<object>? _detach;
    private bool _started;
    private bool _closed;

    /// <summary>A root scope.</summary>
    public FeatureScope(string name, Action<object>? attach = null, Action<object>? detach = null)
    {
        Name = name;
        _attach = attach;
        _detach = detach;
    }

    private FeatureScope(string name, FeatureScope parent)
        : this(name, parent._attach, parent._detach)
    {
        _parent = parent;
    }

    public string Name { get; }

    public FeatureScope? Child { get; private set; }

    public bool IsClosed => _closed;

    private FeatureScope Root => _parent?.Root ?? this;

    public T Register<T>(T feature) where T : class
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_started)
            throw new InvalidOperationException("Feature registration is closed.");
        if (_features.Any(existing => ReferenceEquals(existing, feature)))
            throw new InvalidOperationException("The feature is already registered.");
        _features.Add(feature);
        _contracts.Clear();
        return feature;
    }

    public void Own(Action cleanup)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _cleanup.Add(cleanup);
    }

    /// <summary>Closes registration and attaches every feature to the router.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_started)
            throw new InvalidOperationException("The scope has already started.");
        _started = true;
        if (_attach == null)
            return;
        try
        {
            foreach (object feature in _features)
            {
                _attach(feature);
                _attached.Add(feature);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Closes the current child and opens a new one.</summary>
    public FeatureScope OpenChild(string name)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        CloseChild();
        Child = new FeatureScope(name, this);
        return Child;
    }

    public void CloseChild() => Child?.Dispose();

    /// <summary>Root first, then down the chain. A scope closed mid-raise is skipped.</summary>
    public void Raise<THook>(Action<THook> call) where THook : class => RaiseDown(Root, call);

    /// <summary>Deepest scope first, each in reverse registration order.</summary>
    public void RaiseReverse<THook>(Action<THook> call) where THook : class => RaiseUp(Root, call);

    private static void RaiseDown<THook>(FeatureScope scope, Action<THook> call) where THook : class
    {
        if (!scope.Live)
            return;
        IReadOnlyList<THook> features = scope.Implementing<THook>();
        FeatureScope? child = scope.Child;
        for (int i = 0; i < features.Count && !scope._closed; i++)
        {
            call(features[i]);
        }
        if (child != null && !scope._closed)
            RaiseDown(child, call);
    }

    private static void RaiseUp<THook>(FeatureScope scope, Action<THook> call) where THook : class
    {
        if (!scope.Live)
            return;
        IReadOnlyList<THook> features = scope.Implementing<THook>();
        if (scope.Child is FeatureScope child)
            RaiseUp(child, call);
        for (int i = features.Count - 1; i >= 0 && !scope._closed; i--)
        {
            call(features[i]);
        }
    }

    /// <summary>The one feature of type T in this scope or the nearest ancestor that has one.</summary>
    public T Feature<T>() where T : class
    {
        for (FeatureScope? scope = this; scope != null; scope = scope._parent)
        {
            IReadOnlyList<T> matches = scope.Implementing<T>();
            if (matches.Count == 1)
                return matches[0];
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"{matches.Count} features match {typeof(T).Name} in scope '{scope.Name}': " +
                    string.Join(", ", matches.Select(feature => feature.GetType().Name)));
            }
        }
        throw new InvalidOperationException($"No {typeof(T).Name} in scope {Chain()}.");
    }

    private string Chain() => _parent == null ? $"'{Name}'" : $"'{Name}' -> {_parent.Chain()}";

    private bool Live => _started && !_closed;

    private IReadOnlyList<T> Implementing<T>()
    {
        if (!_contracts.TryGetValue(typeof(T), out object? contracts))
        {
            contracts = Array.AsReadOnly(_features.OfType<T>().ToArray());
            _contracts.Add(typeof(T), contracts);
        }
        return (IReadOnlyList<T>)contracts;
    }

    /// <summary>Per-player state, dropped by Forget and disposed when the scope closes.</summary>
    public PlayerTable<TPlayer, TState> Table<TPlayer, TState>()
        where TPlayer : class
        where TState : class, new()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        PlayerTable<TPlayer, TState> table = new();
        _tables.Add(table);
        return table;
    }

    /// <summary>Drops the player from every table here and below.</summary>
    public void Forget(object player)
    {
        if (_closed)
            return;
        Child?.Forget(player);
        for (int i = _tables.Count - 1; i >= 0; i--)
        {
            _tables[i].Forget(player);
        }
    }

    /// <summary>Snapshot of the scope chain, features and table sizes.</summary>
    public string Describe()
    {
        StringBuilder text = new();
        Describe(text, 0);
        return text.ToString();
    }

    private void Describe(StringBuilder text, int depth)
    {
        string indent = new(' ', depth * 2);
        text.Append(indent).Append(Name).AppendLine(_closed ? " (closed)" : "");
        text.Append(indent).Append("  features: ")
            .AppendLine(string.Join(", ", _features.Select(feature => feature.GetType().Name)));
        if (_tables.Count > 0)
        {
            text.Append(indent).Append("  tables: ")
                .AppendLine(string.Join(", ", _tables.Select(table => table.Describe())));
        }
        Child?.Describe(text, depth + 1);
    }

    /// <summary>Closes the child, then this scope. Everything is released even if a step throws.</summary>
    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        List<Exception> errors = [];
        void Release(Action action)
        {
            try { action(); }
            catch (Exception exception) { errors.Add(exception); }
        }
        if (Child != null)
            Release(Child.Dispose);
        foreach (object feature in _attached)
        {
            Release(() => _detach?.Invoke(feature));
        }
        _attached.Clear();
        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            Release(_cleanup[i]);
        }
        for (int i = _tables.Count - 1; i >= 0; i--)
        {
            _tables[i].Close(errors);
        }
        for (int i = _features.Count - 1; i >= 0; i--)
        {
            if (_features[i] is IDisposable disposable)
                Release(disposable.Dispose);
        }
        if (_parent != null && ReferenceEquals(_parent.Child, this))
            _parent.Child = null;
        if (errors.Count > 0)
            throw new AggregateException("Scope cleanup failed.", errors);
    }
}
