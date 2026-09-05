namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for plot foundation interactables — the buildable land a player claims.
	/// </summary>
	/// <remarks>
	/// A foundation is authored in a scene and never spawned at runtime, so its identity is the key
	/// a designer wrote on it rather than anything the network assigns. See
	/// <c>FishMMO.Shared.PlotIdentity</c> for why.
	/// </remarks>
	public interface IPlotFoundation : IInteractable
	{
		/// <summary>
		/// The key authored on this foundation, canonicalised. Unique within its scene.
		/// </summary>
		string PlotKey { get; }

		/// <summary>
		/// What it costs to claim this plot, in the server's currency attribute.
		/// </summary>
		long Price { get; }

		/// <summary>
		/// The database identity of this plot, or zero until registration has resolved it.
		/// </summary>
		/// <remarks>
		/// Zero is the honest answer during the window between a scene loading and its plots being
		/// read back from the database. Nothing may be claimed while it is zero — there is no row
		/// to claim yet.
		/// </remarks>
		long PlotID { get; }
	}
}
