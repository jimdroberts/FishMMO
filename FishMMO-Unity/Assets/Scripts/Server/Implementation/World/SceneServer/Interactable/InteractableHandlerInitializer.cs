using UnityEngine;
using FishMMO.Shared;
using FishMMO.Server.Core;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
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
		/// <param name="server">Server context passed to handler instances.</param>
		/// <param name="system">Target interactable system instance.</param>
		public void RegisterHandlers(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server, InteractableSystem system)
		{
			if (system == null)
			{
				return;
			}

			// Registers handler classes for each interactable type, passing the Server instance.
			system.RegisterInteractableHandler<AbilityCrafter>(new AbilityCrafterHandler(server));
			system.RegisterInteractableHandler<Banker>(new BankerHandler(server));
			system.RegisterInteractableHandler<DungeonEntrance>(new DungeonEntranceHandler(server));
			system.RegisterInteractableHandler<Merchant>(new MerchantHandler(server));
			system.RegisterInteractableHandler<WorldItem>(new WorldItemHandler(server));
			system.RegisterInteractableHandler<Bindstone>(new BindstoneHandler(server));
			system.RegisterInteractableHandler<Teleporter>(new TeleporterHandler(server));
		}
	}
}