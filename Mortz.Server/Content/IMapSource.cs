namespace Mortz.Server.Content;

/// <summary>Loads maps by id.</summary>
public interface IMapSource
{
    MapSnapshot? Load(string mapId);
}
