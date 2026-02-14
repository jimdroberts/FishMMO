using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for guild system state.
	/// Provides read-only access to guild invitations and database synchronization state.
	/// </summary>
	public interface IGuildSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Tracks pending guild invitations.
		/// Key: TargetCharacterID (the character being invited), Value: InviterCharacterID (the character who sent the invite).
		/// </summary>
		Dictionary<long, long> PendingInvitations { get; }

		/// <summary>
		/// Timestamp of the last successful database fetch for guild updates.
		/// </summary>
		DateTime LastFetchTime { get; set; }
	}
}