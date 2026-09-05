using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// What somebody who is not the owner may do on a plot.
	/// </summary>
	/// <remarks>
	/// Flags rather than a rank, because these are not ordered. Trusting a friend to redecorate is
	/// not a superset of trusting them to bring other people round, and a server that forced them
	/// onto one scale would make every grant a choice between too little and too much.
	///
	/// <para>The owner is never represented here. Ownership is not a grant — it cannot be revoked
	/// by an access row, and a plot whose owner appeared in its own access list would be one bad
	/// delete away from a house its owner could not enter. <see cref="PlotAccess"/> answers for the
	/// owner separately.</para>
	///
	/// <para>Stored as an integer bitmask, so the values must not be renumbered.</para>
	/// </remarks>
	[Flags]
	public enum PlotPermission
	{
		/// <summary>
		/// No access at all. What a stranger has, and what a revoked friend goes back to.
		/// </summary>
		None = 0,

		/// <summary>
		/// May pass through the plot's boundary and into the house.
		/// </summary>
		Enter = 1 << 0,

		/// <summary>
		/// May place structures and decorations.
		/// </summary>
		PlaceItems = 1 << 1,

		/// <summary>
		/// May take structures and decorations away again.
		/// </summary>
		/// <remarks>
		/// Deliberately separate from <see cref="PlaceItems"/>, and the more dangerous of the two:
		/// removal is what destroys work, and what sends things to the owner's vault where they cost
		/// money to get back. A friend helping decorate needs one; only a trusted one needs both.
		/// </remarks>
		RemoveItems = 1 << 2,

		/// <summary>
		/// May grant other players access.
		/// </summary>
		/// <remarks>
		/// Never lets a friend grant more than they hold themselves — see
		/// <see cref="PlotAccess.ClampGrant"/>. Otherwise the weakest permission on the list is the
		/// only one that matters, because whoever holds it can hand themselves the rest.
		/// </remarks>
		InviteFriends = 1 << 3,

		/// <summary>
		/// Every permission that may be granted.
		/// </summary>
		/// <remarks>
		/// The mask unknown bits are cleaned against, so a client that invents a flag cannot store
		/// one and a permission removed in a later version stops meaning anything rather than
		/// silently becoming whatever now occupies its bit.
		/// </remarks>
		All = Enter | PlaceItems | RemoveItems | InviteFriends,
	}
}
