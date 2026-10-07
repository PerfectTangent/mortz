namespace Mortz.Client.Hooks;

/// <summary>The admin flow has a line for the local player.</summary>
public interface IObserveAdminStatus
{
    void AdminStatus(string line);
}
