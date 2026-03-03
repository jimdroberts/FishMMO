using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for interactable handling and validation.
	/// Implementations validate interactions, manage handlers, and broadcast inventory/merchant updates.
	/// </summary>
	public interface IInteractableSystem : IServerBehaviour
	{
		void OnInteractNPC(IPlayerCharacter character, IInteractable interactable);
		void StartDialogueSession(IPlayerCharacter character, ISceneObject sceneObject, IDialogueInteractable dialogue);
		bool SendNewItemBroadcast<T>(T conn, ICharacter character, IInventoryController inventoryController, Item newItem);
		void RegisterInteractableHandler<T>(IInteractableHandler handler) where T : IInteractable;
		bool UnregisterInteractableHandler<T>() where T : IInteractable;
		IInteractableHandler GetInteractableHandler<T>() where T : IInteractable;
		void ClearAllHandlers();
	}
}