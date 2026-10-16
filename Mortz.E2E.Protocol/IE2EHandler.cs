namespace Mortz.E2E.Protocol;

/// <summary>May respond later, e.g. at a tick boundary, but always on the main thread.</summary>
public interface IE2EHandler
{
    E2EProcessRole Role { get; }

    void Handle(E2ERequest request, IE2EResponder responder);
}
