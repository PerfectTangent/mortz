namespace Mortz.Client.Hooks;

/// <summary>A fixed physics tick passed while connected.</summary>
public interface IClientTick
{
    void Tick();
}
