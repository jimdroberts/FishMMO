using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Which path a due plot takes through the tax sweep.
	/// </summary>
	public enum PlotTaxRoute : byte
	{
		/// <summary>Nothing to do.</summary>
		None = 0,

		/// <summary>Past its grace: taken back and its contents vaulted, whoever holds the owner.</summary>
		Reclaim = 1,

		/// <summary>Guild land: the date moves on and nothing is charged.</summary>
		Defer = 2,

		/// <summary>The owner is held by this server: charged through their in-memory balance.</summary>
		ChargeHere = 3,

		/// <summary>
		/// Not held by this server: handed to the batched offline charge, which charges the stored
		/// row only if no server holds the owner and defers the plot if one does.
		/// </summary>
		ChargeOffline = 4,
	}

	/// <summary>
	/// Routes a plot the tax rule has decided on to the path that carries it out.
	/// </summary>
	/// <remarks>
	/// <see cref="PlotTaxDecision"/> says what a plot is owed; this says who may act on it, and the
	/// one rule in it is the one the housing README calls correctness rather than optimisation: an
	/// owner this server holds is charged through their in-memory balance and never through the
	/// stored row, because their next save would write the row back over the debit and the money
	/// would quietly return. Everything not held here goes to the offline batch, whose own
	/// assertion under the row lock is what decides between charging the row and leaving the plot
	/// to whichever server does hold the owner.
	///
	/// <para>Reclamation does not care who holds the owner. It is decided by the grace clock alone
	/// (see <see cref="PlotTaxDecision"/>), and it takes the land, not the money.</para>
	/// </remarks>
	public static class PlotTaxRouting
	{
		/// <summary>
		/// The path for one due plot.
		/// </summary>
		/// <param name="action">What <see cref="PlotTaxDecision.Decide"/> said to do.</param>
		/// <param name="ownerHeldHere">Whether this server holds the owning character.</param>
		public static PlotTaxRoute Route(PlotTaxAction action, bool ownerHeldHere)
		{
			switch (action)
			{
				case PlotTaxAction.Reclaim:
					return PlotTaxRoute.Reclaim;
				case PlotTaxAction.Defer:
					return PlotTaxRoute.Defer;
				case PlotTaxAction.Charge:
					return ownerHeldHere ? PlotTaxRoute.ChargeHere : PlotTaxRoute.ChargeOffline;
				default:
					return PlotTaxRoute.None;
			}
		}
	}
}
