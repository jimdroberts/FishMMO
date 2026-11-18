namespace FishMMO.Shared
{
	/// <summary>
	/// What kind of holder owns a plot.
	/// </summary>
	/// <remarks>
	/// The discriminator for <see cref="PlotOwner"/>. It is not itself a column: the plot row
	/// stores an owning character and an owning guild in separate columns so that "every plot this
	/// guild owns" is an indexed lookup rather than a scan filtered by a type tag. This enum is how
	/// game code reads that pair back as a single answer.
	///
	/// <para>Values are explicit because they reach clients as part of plot state, and a
	/// renumbering would silently change what an already-built client displays.</para>
	/// </remarks>
	public enum PlotOwnerType
	{
		/// <summary>
		/// Nobody owns the plot. It is available to claim.
		/// </summary>
		Unowned = 0,

		/// <summary>
		/// An individual character owns the plot.
		/// </summary>
		Character = 1,

		/// <summary>
		/// A guild owns the plot.
		/// </summary>
		Guild = 2,
	}
}
