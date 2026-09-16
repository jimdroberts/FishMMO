using FishMMO.Server.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// The scene server's NPC brain system: runs one <see cref="AIBrainHost"/> for the process.
	/// </summary>
	/// <remarks>
	/// Every NPC the scene server spawns gets its <see cref="AIController"/> from here, with the
	/// archetype the <see cref="Catalogue"/> names for its prefab. Spawners and the pet system
	/// reach the host through <see cref="AIBrainHost.TryGet"/>.
	/// </remarks>
	[CreateAssetMenu(fileName = "AISystem", menuName = "FishMMO/Server/Scene Server/AI System", order = 1)]
	public class AISystem : ServerBehaviour
	{
		/// <summary>
		/// Which brain each NPC prefab runs.
		/// </summary>
		[Tooltip("Which brain each NPC prefab runs.")]
		public AIBrainCatalogue Catalogue;

		/// <summary>
		/// The running host, or null before initialisation.
		/// </summary>
		public AIBrainHost Host { get; private set; }

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				return ServerComponentInitializationStatus.FailedToFindServer;
			}

			FishNet.Managing.NetworkManager networkManager = Server.NetworkWrapper?.NetworkManager;
			if (networkManager == null)
			{
				return ServerComponentInitializationStatus.FailedToFindServerManager;
			}

			if (Catalogue == null)
			{
				FishMMO.Logging.Log.Warning("AISystem", "No AI brain catalogue is assigned; NPCs will spawn without a brain unless a spawner names one.");
			}

			Host = new AIBrainHost(networkManager, Catalogue);
			Host.Start();

			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
			Host?.Stop();
			Host = null;
			AggressionDispatcher.Clear();
		}
	}
}
