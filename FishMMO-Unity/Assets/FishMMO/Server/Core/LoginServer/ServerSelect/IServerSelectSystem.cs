namespace FishMMO.Server.Core.LoginServer
{
    /// <summary>
    /// Engine-agnostic public API for server selection system.
    /// Manages server selection for clients, providing the list of available
    /// world servers from the database.
    /// </summary>
    public interface IServerSelectSystem : IServerBehaviour
    {
    }
}