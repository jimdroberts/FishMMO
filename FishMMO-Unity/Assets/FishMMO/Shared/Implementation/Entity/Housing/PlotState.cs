namespace FishMMO.Shared
{
	/// <summary>
	/// Where a plot is in its lifecycle.
	/// </summary>
	/// <remarks>
	/// Ownership alone cannot answer this. "Nobody owns this" covers both land that has never been
	/// claimed and land somebody lost, and those are not the same place: one is a bare lot and the
	/// other is a house whose owner stopped paying, standing empty with its contents in a vault. A
	/// channel loading the scene has to render them differently, and a player walking up to one has
	/// to be told a different thing.
	///
	/// <para>Persisted on the plot row rather than derived, because it is the thing channels agree
	/// on. Channels are ephemeral copies of a scene that hold no world state of their own, so a
	/// state each one recomputed from whatever it happened to have loaded would be a state that
	/// differed between them.</para>
	///
	/// <para>Values are explicit and must not be renumbered: they are stored, and they reach
	/// clients as part of plot state.</para>
	/// </remarks>
	public enum PlotState
	{
		/// <summary>
		/// No owner and nothing built. Available to claim.
		/// </summary>
		Empty = 0,

		/// <summary>
		/// Claimed, with the house going up. Closed to everybody but the owner.
		/// </summary>
		/// <remarks>
		/// A plot enters this the moment it is claimed and leaves it when the owner says the house
		/// is finished. It is deliberately a persisted phase rather than the in-memory build
		/// session: the session is one person standing there with the editor open, and it ends when
		/// they walk away, whereas this survives their logging out and every channel shows it.
		/// </remarks>
		Building = 1,

		/// <summary>
		/// Built, paid up, and open to the owner and whoever they have let in.
		/// </summary>
		Occupied = 2,

		/// <summary>
		/// Unpaid past its grace period. Unowned, closed, and claimable.
		/// </summary>
		/// <remarks>
		/// The owner has been cleared and everything that was built has gone to their vault, but the
		/// lot is not <see cref="Empty"/>: it renders as an abandoned house rather than bare ground
		/// until somebody claims it. Nobody may enter one — there is no owner to admit them, and the
		/// structures that made it enterable are gone.
		/// </remarks>
		Abandoned = 3,
	}

	/// <summary>
	/// Convenience tests over <see cref="PlotState"/>.
	/// </summary>
	public static class PlotStateExtensions
	{
		/// <summary>
		/// Reads a stored state column back into the enum.
		/// </summary>
		/// <param name="stored">The integer held in the plot row.</param>
		/// <remarks>
		/// A plain cast is not safe here. The column is an integer, and a row written by a newer
		/// build can hold a value this one has no name for — cast blindly, that produces a
		/// <see cref="PlotState"/> matching none of the branches that decide access, and the
		/// resulting plot answers "no" to every question including the ones that should be yes.
		///
		/// <para>Unrecognised values become <see cref="PlotState.Empty"/>, which is the reading that
		/// grants the least: an unclaimed lot people may walk over and nothing more. The alternative
		/// — trusting the number — is how a state nobody in this build understands ends up
		/// admitting strangers to a house.</para>
		/// </remarks>
		public static PlotState FromStored(int stored)
		{
			switch (stored)
			{
				case (int)PlotState.Building:
					return PlotState.Building;
				case (int)PlotState.Occupied:
					return PlotState.Occupied;
				case (int)PlotState.Abandoned:
					return PlotState.Abandoned;
				default:
					return PlotState.Empty;
			}
		}

		/// <summary>
		/// True when a plot in this state may be claimed by somebody.
		/// </summary>
		/// <remarks>
		/// Both of the unowned states, not just <see cref="PlotState.Empty"/>. An abandoned plot
		/// that could not be claimed would be land permanently removed from the world by one player
		/// having stopped paying for it.
		/// </remarks>
		public static bool IsClaimable(this PlotState state)
		{
			return state == PlotState.Empty || state == PlotState.Abandoned;
		}

		/// <summary>
		/// True when this state describes land somebody holds.
		/// </summary>
		public static bool IsHeld(this PlotState state)
		{
			return state == PlotState.Building || state == PlotState.Occupied;
		}

		/// <summary>
		/// The state a plot enters when it is claimed.
		/// </summary>
		public static PlotState OnClaimed() => PlotState.Building;

		/// <summary>
		/// The state a plot returns to when its owner gives it up deliberately.
		/// </summary>
		/// <remarks>
		/// <see cref="PlotState.Empty"/> rather than <see cref="PlotState.Abandoned"/>. Abandoned is
		/// what unpaid land becomes, and it carries the visuals of a house somebody lost; a player
		/// who tidied up and handed the deed back should leave a bare lot behind them.
		/// </remarks>
		public static PlotState OnReleased() => PlotState.Empty;
	}
}
