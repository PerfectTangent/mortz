namespace Mortz.E2E.Protocol;

/// <summary>Structured stdout, seen from a handler. Main thread only.</summary>
public interface IE2EResponder
{
    void Respond(E2EResponse response);

    void Emit(E2EEvent evt);
}
