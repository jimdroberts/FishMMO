namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// What became of one item write — an incremental batch, a snapshot, or a departing
	/// character's flush.
	/// </summary>
	/// <remarks>
	/// The item write used to report nothing at all: it caught every failure itself and returned a
	/// plain task, so a departure flush that failed looked exactly like one that landed, and the
	/// session it belonged to was handed back regardless. Only <see cref="Retry"/> asks the caller
	/// to act; every other value is final.
	/// </remarks>
	public enum ItemWriteOutcome : byte
	{
		/// <summary>The write committed.</summary>
		Written = 0,

		/// <summary>
		/// Not applied because a write captured later has already landed and says more. Nothing is
		/// missing and nothing needs repairing.
		/// </summary>
		Superseded = 1,

		/// <summary>
		/// Refused because this server no longer holds the character's claim. Another server is
		/// authoritative, and writing our copy over its state is the one thing that must not happen.
		/// </summary>
		NotOwned = 2,

		/// <summary>Failed for a reason another attempt cannot change. Logged where it happened.</summary>
		Rejected = 3,

		/// <summary>Failed in a way worth another attempt: a lost connection, a timeout, a failed commit.</summary>
		Retry = 4,
	}
}
