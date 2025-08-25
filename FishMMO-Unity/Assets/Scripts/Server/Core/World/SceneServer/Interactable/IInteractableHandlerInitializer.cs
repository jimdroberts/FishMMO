namespace FishMMO.Server.Core.World.SceneServer
{
    /// <summary>
    /// Engine-agnostic initializer that registers interactable handlers with the server.
    /// Implementations should register platform-specific handlers during InitializeOnce.
    /// </summary>
    public interface IInteractableHandlerInitializer
    {
        void RegisterHandlers(object server);
    }
}
