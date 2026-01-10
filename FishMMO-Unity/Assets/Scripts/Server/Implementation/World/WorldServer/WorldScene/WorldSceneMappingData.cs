using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Runtime data container for world scene connection queuing and scene assignment.
	/// Manages all world scene connection state separately from WorldSceneSystem logic.
	/// </summary>
	public class WorldSceneMappingData : RuntimeDataContainer, IWorldSceneMappingData<NetworkConnection>
	{
		/// <summary>
		/// Connections waiting for an open world scene to finish loading, mapped by scene name.
		/// </summary>
		public Dictionary<string, HashSet<NetworkConnection>> WaitingOpenWorldConnections { get; private set; }

		/// <summary>
		/// Maps connections to the open world scene name they are waiting for.
		/// </summary>
		public Dictionary<NetworkConnection, string> OpenWorldConnectionScenes { get; private set; }

		/// <summary>
		/// Connections waiting for an instanced scene to finish loading, mapped by instance ID.
		/// </summary>
		public Dictionary<long, HashSet<NetworkConnection>> WaitingInstanceConnections { get; private set; }

		/// <summary>
		/// Maps connections to the instance scene ID they are waiting for.
		/// </summary>
		public Dictionary<NetworkConnection, long> InstanceConnectionScenes { get; private set; }

		/// <summary>
		/// Total number of connections managed by this system (waiting + active).
		/// </summary>
		public int ConnectionCount { get; set; }

		/// <summary>
		/// Initializes the world scene mapping data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			WaitingOpenWorldConnections = new Dictionary<string, HashSet<NetworkConnection>>();
			OpenWorldConnectionScenes = new Dictionary<NetworkConnection, string>();
			WaitingInstanceConnections = new Dictionary<long, HashSet<NetworkConnection>>();
			InstanceConnectionScenes = new Dictionary<NetworkConnection, long>();
			ConnectionCount = 0;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all world scene mapping data.
		/// </summary>
		public override void Clear()
		{
			WaitingOpenWorldConnections?.Clear();
			OpenWorldConnectionScenes?.Clear();
			WaitingInstanceConnections?.Clear();
			InstanceConnectionScenes?.Clear();
			ConnectionCount = 0;
		}

		/// <summary>
		/// Deinitializes the world scene mapping data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}