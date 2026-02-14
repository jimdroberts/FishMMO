using System;
using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for guild system state.
	/// Manages guild invitations and database synchronization state separately from GuildSystem logic.
	/// </summary>
	public class GuildSystemRuntimeData : RuntimeDataContainer, IGuildSystemRuntimeData
	{
		/// <summary>
		/// Tracks pending guild invitations.
		/// Key: TargetCharacterID (the character being invited), Value: GuildID (the guild being invited to).
		/// </summary>
		public Dictionary<long, long> PendingInvitations { get; private set; }

		/// <summary>
		/// Timestamp of the last successful database fetch for guild updates.
		/// </summary>
		public DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Initializes the guild runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			PendingInvitations = new Dictionary<long, long>();
			LastFetchTime = DateTime.UtcNow;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all guild runtime data.
		/// </summary>
		public override void Clear()
		{
			PendingInvitations?.Clear();
			LastFetchTime = DateTime.UtcNow;
		}

		/// <summary>
		/// Deinitializes the guild runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}