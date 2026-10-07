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

    protected T RegisterRuntime<T>(T feature) where T : class
    {
        FeatureScope scope = new(typeof(T).Name, Router.Add, Router.Remove);
        scope.Register(feature);
        scope.Start();
        _scopes.Add(scope);
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
