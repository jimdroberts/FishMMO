using System.Collections.Generic;

namespace FishMMO.Server.Core.World.WorldServer
{
	/// <summary>
	/// Runtime data container for world scene connection queuing and scene assignment.
	/// Provides read-only access to connection queue collections.
	/// </summary>
	public interface IWorldSceneMappingData<TConnection> : IRuntimeDataContainer
	{
		/// <summary>
		/// Connections waiting for an open world scene to finish loading, mapped by scene name.
		/// </summary>
		Dictionary<string, HashSet<TConnection>> WaitingOpenWorldConnections { get; }

		/// <summary>
		/// Maps connections to the open world scene name they are waiting for.
		/// </summary>
		Dictionary<TConnection, string> OpenWorldConnectionScenes { get; }

		/// <summary>
		/// Connections waiting for an instanced scene to finish loading, mapped by instance ID.
		/// </summary>
		Dictionary<long, HashSet<TConnection>> WaitingInstanceConnections { get; }

		/// <summary>
		/// Maps connections to the instance scene ID they are waiting for.
		/// </summary>
		Dictionary<TConnection, long> InstanceConnectionScenes { get; }

		/// <summary>
		/// Total number of connections managed by this system (waiting + active).
		/// </summary>
		int ConnectionCount { get; set; }
	}
}