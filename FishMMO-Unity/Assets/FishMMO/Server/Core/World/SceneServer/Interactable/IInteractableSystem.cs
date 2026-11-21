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
		/// Raised for every interaction the system accepts — validated, in range, rate limited — before the
		/// interactable's own triggers run.
		/// </summary>
		/// <remarks>
		/// How a module handles an interaction kind the interactable carries intrinsically rather than through
		/// a trigger list: the Commerce module opens a corpse's loot window here, so any NPC that can die is
		/// lootable without a content author having to remember a trigger, and a game without Commerce has
		/// corpses nobody can open. Handlers run on the main thread and must not throw.
		/// </remarks>
		event System.Action<IPlayerCharacter, IInteractable> OnPlayerInteracting;

		/// <summary>
		/// Handles a player interacting with an NPC.
		/// </summary>
		/// <param name="character">The player character initiating the interaction.</param>
		/// <param name="interactable">The interactable NPC being engaged.</param>
		void OnInteractNPC(IPlayerCharacter character, IInteractable interactable);

		/// <summary>The scene details interactions are validated against.</summary>
		WorldSceneDetailsCache WorldSceneDetails { get; }
		/// <summary>
		/// Takes the one per-connection interaction guard every interaction kind shares (the debounce: a player interacts
		/// with one thing at a time). A module's own interaction requests (the World Interactables module's dialogue choices
		/// and container takes) take it too, and release it with <see cref="EndInteraction"/>.
		/// </summary>
		/// <param name="connectionId">The client connection ID.</param>
		/// <param name="guardKey">When successful, the key to release.</param>
		/// <returns>False while the debounce is active.</returns>
		bool TryBeginInteraction(int connectionId, out long guardKey);
		/// <summary>Releases a guard taken by <see cref="TryBeginInteraction"/>.</summary>
		/// <param name="guardKey">The key it returned.</param>
		void EndInteraction(long guardKey);
		/// <summary>
		/// Resolves a client's scene object ID as the interaction path does: registered, live (a pooled, despawned object keeps
		/// its ID), and in the character's scene.
		/// </summary>
		/// <param name="sceneObjectID">The ID the client named.</param>
		/// <param name="characterSceneHandle">The handle of the scene the character is in.</param>
		/// <param name="sceneObject">The scene object, when it resolves.</param>
		/// <returns>True when it resolves.</returns>
		bool TryResolveSceneObject(long sceneObjectID, UnityEngine.SceneManagement.SceneHandle characterSceneHandle, out ISceneObject sceneObject);

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