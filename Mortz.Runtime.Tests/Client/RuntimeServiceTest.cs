using Mortz.Core.Features;
using Mortz.Protocol.Net;
using Mortz.Runtime.Tests.Net;

namespace Mortz.Runtime.Tests.Client;

/// <summary>Routes and disposes client features through real feature scopes.</summary>
public abstract class RuntimeServiceTest : IDisposable
{
    private readonly List<FeatureScope> _scopes = [];

    protected TestClientSender Sender { get; } = new();

    protected NetRouter Router { get; } = new() { MatchGeneration = 0 };

    protected FeatureScope Scope { get; } = new("connection");

    /// <summary>A routed scope left unstarted, for features that must hear each other's hooks.</summary>
    protected FeatureScope OpenScope(string name)
    {
        FeatureScope scope = new(name, Router.Add, Router.Remove);
        _scopes.Add(scope);
        return scope;
    }

    protected T RegisterRuntime<T>(T feature) where T : class
    {
        FeatureScope scope = OpenScope(typeof(T).Name);
        scope.Register(feature);
        scope.Start();
        return feature;
    }

    public void Dispose()
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            _scopes[i].Dispose();
        }
        _scopes.Clear();
        Scope.Dispose();
    }
}
