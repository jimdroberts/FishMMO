namespace FishMMO.Shared
{
	/// <summary>
	/// Flags representing movement actions for KCC input replication.
	/// Used as a bitmask for jump, crouch, sprint, and other actions.
	/// <para>
	/// <b>One-shot vs. continuous flags:</b> <see cref="Jump"/> is a one-shot impulse that
	/// should fire once and be cleared. <see cref="Crouch"/> and <see cref="Sprint"/> are
	/// continuous (held) flags that persist while the key is held.
	/// </para>
	/// <para>
	/// This used to add "observer prediction clears one-shot flags to prevent indefinite
	/// re-prediction. When adding new one-shot flags, include them in OneShotMask." There is no
	/// OneShotMask and there never was one in this project, and the clearing it described belonged
	/// to the observer-prediction path — which does not run: state forwarding is off, so FishNet
	/// never invokes a replicate body on a non-owner. A one-shot flag is cleared by its producer,
	/// <c>KCCPlayer.PopulateInput</c>, on the tick after it is consumed.
	/// </para>
	/// </summary>
	public enum KCCMoveFlags : int
	{
		/// <summary>
		/// Indicates this is actual movement data (default value).
		/// </summary>
		IsActualData = 0,
		/// <summary>
		/// Jump action flag (one-shot impulse, cleared on observer prediction).
		/// </summary>
		Jump,
		/// <summary>
		/// Crouch action flag (continuous, held while key is down).
		/// </summary>
		Crouch,
		/// <summary>
		/// Sprint action flag (continuous, held while key is down).
		/// </summary>
		Sprint,
	}

	/// <summary>
	/// Helper for clearing one-shot flags on observer prediction.
	/// If new one-shot flags are added to <see cref="KCCMoveFlags"/>,
	/// include them here so observer future-state prediction clears them.
	/// </summary>
	public static class KCCMoveFlagsHelper
	{
		/// <summary>
		/// Clears all one-shot flags from the given flag set.
		/// Currently only <see cref="KCCMoveFlags.Jump"/> is one-shot.
		/// </summary>
		public static void ClearOneShotFlags(ref int flags)
		{
			flags.DisableBit(KCCMoveFlags.Jump);
			// Add new one-shot flags here (e.g., Dodge, DashBurst).
		}
	}
}