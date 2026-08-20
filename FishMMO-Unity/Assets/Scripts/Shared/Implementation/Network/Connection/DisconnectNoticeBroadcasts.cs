using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Why a server is about to close a connection.
	/// </summary>
	/// <remarks>
	/// Deliberately an enum rather than a message string. The reasons the servers log to
	/// themselves are internal ("Failed to get selected character", "Instance routing rate
	/// limited") and are useful to an operator, not to a player — and putting them on the wire
	/// would hand an attacker a running commentary on the server's internal state. The client
	/// maps each value to its own player-facing wording.
	/// </remarks>
	public enum DisconnectNoticeReason : byte
	{
		/// <summary>No specific reason was supplied.</summary>
		Unspecified = 0,
		/// <summary>The server hit an internal failure while handling this connection.</summary>
		ServerError = 1,
		/// <summary>The account's selected character could not be read, claimed, or spawned.</summary>
		CharacterUnavailable = 2,
		/// <summary>The scene the character belongs to could not be prepared or validated.</summary>
		SceneUnavailable = 3,
		/// <summary>The world server could not route this client to a scene server.</summary>
		RoutingFailed = 4,
		/// <summary>The client stayed on the world server past its routing deadline.</summary>
		RoutingTimedOut = 5,
		/// <summary>The client never acknowledged the scene it was sent to load.</summary>
		SceneHandshakeTimedOut = 6,
		/// <summary>Another server now owns this character's session.</summary>
		SessionSuperseded = 7,
		/// <summary>The request was refused by a rate limit or debounce.</summary>
		RateLimited = 8,
		/// <summary>The connection sent something the server would not accept.</summary>
		ProtocolViolation = 9,
		/// <summary>An operator kicked this account.</summary>
		AdministrativeKick = 10,
		/// <summary>The server is stopping for scheduled maintenance.</summary>
		/// <remarks>
		/// Terminal: the server the client was on is going away, so its reconnect loop would
		/// spend every attempt on a socket that is closing. The player is returned to the login
		/// screen with the reason, and comes back when the world does.
		/// </remarks>
		ServerMaintenance = 11,
	}

	/// <summary>
	/// Sent immediately before a server closes a connection on purpose, so the player is told
	/// why instead of simply finding themselves back on the login screen.
	/// </summary>
	/// <remarks>
	/// FishNet does not carry a kick reason to the client, and nothing else in the pipeline did
	/// either outside the two queue systems — so every deliberate disconnect (failed character
	/// claim, unroutable client, scene handshake timeout, lease lost to another server) landed
	/// the player back at login with no explanation and no way to tell a transient fault from a
	/// permanent one.
	/// <para>
	/// This must be sent with <c>NetworkConnection.Disconnect(false)</c> rather than
	/// <c>Kick</c>. <c>Kick</c> stops the transport immediately, which discards everything still
	/// in the outgoing bundle — including this message. <c>Disconnect(false)</c> flushes the
	/// tick's data first and only then closes, which is the whole reason the notice arrives at
	/// all.
	/// </para>
	/// </remarks>
	public struct DisconnectNoticeBroadcast : IBroadcast
	{
		/// <summary>Why the connection is being closed.</summary>
		public DisconnectNoticeReason Reason;
		/// <summary>
		/// True when reconnecting cannot help, so the client should abandon its retry loop and
		/// return to the login screen at once.
		/// </summary>
		/// <remarks>
		/// Only the server can judge this. A world server that could not find a scene instance
		/// expects the client to come straight back and try again, and telling it not to would
		/// turn a two-second hiccup into a forced re-login; a character that cannot be claimed
		/// at all will fail identically on every retry, and letting the loop run its full course
		/// leaves the player watching a spinner for minutes before reaching the same place.
		/// </remarks>
		public bool Terminal;
	}
}
