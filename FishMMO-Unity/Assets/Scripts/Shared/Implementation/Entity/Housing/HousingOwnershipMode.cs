namespace FishMMO.Shared
{
	/// <summary>
	/// Who may own land and housing on this server.
	/// </summary>
	/// <remarks>
	/// Chosen by the developer rather than fixed by the implementation, because the answer is a
	/// design decision about the game rather than a technical one: a guild-centred server and a
	/// solo-friendly one want different answers, and a server that does not want housing at all
	/// should not have to carry it.
	///
	/// <para>The mode is the single gate every other part of the housing system asks. Purchase,
	/// building permissions, tax liability and reclamation all resolve to "who owns this plot",
	/// so putting the choice here keeps that question in one place instead of spreading
	/// equivalent checks across each system.</para>
	/// </remarks>
	public enum HousingOwnershipMode
	{
		/// <summary>
		/// Housing is disabled. No land may be claimed, and the systems built on top of it do
		/// nothing at all.
		/// </summary>
		/// <remarks>
		/// The default deliberately. Housing brings persistent world state, a recurring tax and
		/// destruction of unpaid plots; a server should opt into that rather than discover it.
		/// </remarks>
		Neither = 0,

		/// <summary>
		/// Individual characters may own land. Guilds may not.
		/// </summary>
		Player = 1,

		/// <summary>
		/// Guilds may own land. Individual characters may not.
		/// </summary>
		Guild = 2,

		/// <summary>
		/// Both characters and guilds may own land, independently of one another.
		/// </summary>
		Both = 3,
	}

	/// <summary>
	/// Convenience tests over <see cref="HousingOwnershipMode"/>.
	/// </summary>
	/// <remarks>
	/// Extension methods rather than properties on a system, so the questions can be asked from
	/// shared code and from tests without a server instance to hand.
	/// </remarks>
	public static class HousingOwnershipModeExtensions
	{
		/// <summary>
		/// True when housing is enabled in any form.
		/// </summary>
		public static bool IsHousingEnabled(this HousingOwnershipMode mode)
		{
			return mode != HousingOwnershipMode.Neither;
		}

		/// <summary>
		/// True when an individual character may own land.
		/// </summary>
		public static bool AllowsPlayerOwnership(this HousingOwnershipMode mode)
		{
			return mode == HousingOwnershipMode.Player ||
				   mode == HousingOwnershipMode.Both;
		}

		/// <summary>
		/// True when a guild may own land.
		/// </summary>
		public static bool AllowsGuildOwnership(this HousingOwnershipMode mode)
		{
			return mode == HousingOwnershipMode.Guild ||
				   mode == HousingOwnershipMode.Both;
		}
	}
}
