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
		/// <summary>
		/// Handles a player interacting with an NPC.
		/// </summary>
		/// <param name="character">The player character initiating the interaction.</param>
		/// <param name="interactable">The interactable NPC being engaged.</param>
		void OnInteractNPC(IPlayerCharacter character, IInteractable interactable);

		/// <summary>
		/// Starts a dialogue session between a player character and a dialogue interactable.
		/// </summary>
		/// <param name="character">The player character.</param>
		/// <param name="sceneObject">The scene object associated with the dialogue.</param>
		/// <param name="dialogue">The dialogue interactable containing the dialogue data.</param>
		void StartDialogueSession(IPlayerCharacter character, ISceneObject sceneObject, IDialogueInteractable dialogue);

		/// <summary>
		/// Sends a new item broadcast notification to the specified connection.
		/// </summary>
		/// <typeparam name="T">The connection type.</typeparam>
		/// <param name="conn">The connection to notify.</param>
		/// <param name="character">The character receiving the item.</param>
		/// <param name="inventoryController">The inventory controller managing the item.</param>
		/// <param name="newItem">The new item to broadcast.</param>
		/// <returns>True if the broadcast was sent successfully; otherwise false.</returns>
		bool SendNewItemBroadcast<T>(T conn, ICharacter character, IInventoryController inventoryController, Item newItem);
	}
}