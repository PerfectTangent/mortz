namespace Mortz.Client.Hooks;

/// <summary>A render frame passed while connected.</summary>
public interface IClientFrame
{
    void Frame(float delta);
}
