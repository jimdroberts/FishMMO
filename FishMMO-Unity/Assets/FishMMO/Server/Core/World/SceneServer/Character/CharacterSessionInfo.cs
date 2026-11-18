using System;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Stores the session ownership token and server ID for a claimed character.
	/// Used to properly release character sessions via ReleaseAsync.
	/// </summary>
	public readonly struct CharacterSessionInfo
	{
		/// <summary>
		/// The session ownership token returned by TryClaimAsync.
		/// Required for ReleaseAsync and RefreshSessionLeaseAsync.
		/// </summary>
		public readonly Guid Token;

		/// <summary>
		/// The server ID that owns this session.
		/// </summary>
		public readonly long ServerID;

		/// <summary>
		/// Initializes a new session info with the given ownership token and server ID.
		/// </summary>
		/// <param name="token">The session ownership token returned by TryClaimAsync.</param>
		/// <param name="serverID">The server ID that owns this session.</param>
		public CharacterSessionInfo(Guid token, long serverID)
		{
			Token = token;
			ServerID = serverID;
		}
	}
}