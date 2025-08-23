using UnityEngine;
using FishMMO.Shared;
using FishMMO.Server.Core;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// ScriptableObject initializer for registering interactable handlers in the FishMMO server.
	/// </summary>
	[CreateAssetMenu(fileName = "FishMMO Interactable Handler Initializer", menuName = "FishMMO/Interactables/FishMMO Interactable Handler Initializer", order = 1)]
	public class InteractableHandlerInitializer : ScriptableObject, IInteractableHandlerInitializer
	{
		/// <summary>
		/// Registers all interactable handlers with the InteractableSystem.
		/// </summary>
		public void RegisterHandlers(IServer<INetworkManagerWrapper, NetworkConnection, ServerBehaviour> server)
		{
			// Registers handler classes for each interactable type, passing the Server instance.
			InteractableSystem.RegisterInteractableHandler<AbilityCrafter>(new AbilityCrafterHandler(server));
			InteractableSystem.RegisterInteractableHandler<Banker>(new BankerHandler(server));
			InteractableSystem.RegisterInteractableHandler<DungeonEntrance>(new DungeonEntranceHandler(server));
			InteractableSystem.RegisterInteractableHandler<Merchant>(new MerchantHandler(server));
			InteractableSystem.RegisterInteractableHandler<WorldItem>(new WorldItemHandler(server));
			InteractableSystem.RegisterInteractableHandler<Bindstone>(new BindstoneHandler(server));
			InteractableSystem.RegisterInteractableHandler<Teleporter>(new TeleporterHandler(server));
		}
	}
}