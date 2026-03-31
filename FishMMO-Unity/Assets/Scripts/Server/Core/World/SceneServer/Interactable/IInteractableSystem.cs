using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for interactable handling and validation.
	/// Implementations validate interactions and broadcast inventory/merchant updates.
	/// Interactable behaviour is now driven entirely by ECA triggers on each prefab.
	/// </summary>
	public interface IInteractableSystem : IServerBehaviour
	{
		void OnInteractNPC(IPlayerCharacter character, IInteractable interactable);
		void StartDialogueSession(IPlayerCharacter character, ISceneObject sceneObject, IDialogueInteractable dialogue);
		bool SendNewItemBroadcast<T>(T conn, ICharacter character, IInventoryController inventoryController, Item newItem);
	}
}