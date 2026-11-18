using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// The individual powers a guild rank may hold.
	/// </summary>
	/// <remarks>
	/// This replaces <see cref="GuildRank"/> as the thing every server-side permission check
	/// consults. The enum remains, but only as the identity of the three DEFAULT ranks a guild is
	/// seeded with — it no longer decides anything.
	///
	/// Stored as a 64-bit integer in <c>guild_rank.permissions</c>. The width is deliberate: a
	/// 32-bit mask would be two thirds spent already, and widening a column that every permission
	/// check reads is not a migration anybody wants to run twice. Values are explicit rather than
	/// <c>1 &lt;&lt; n</c> so that reordering the members can never silently re-map a stored mask.
	///
	/// New flags must be APPENDED. A guild's stored mask is a set of bit positions, and inserting
	/// a value in the middle would hand every existing rank a permission its members never had.
	/// </remarks>
	[Flags]
	public enum GuildPermissions : long
	{
		/// <summary>Holds nothing. The default rank's default.</summary>
		None = 0,

		/// <summary>May invite characters to the guild.</summary>
		Invite = 1L << 0,

		/// <summary>May remove members ranked strictly below the holder.</summary>
		Kick = 1L << 1,

		/// <summary>May move members between ranks strictly below the holder's own.</summary>
		Promote = 1L << 2,

		/// <summary>May edit the message of the day.</summary>
		EditMessageOfTheDay = 1L << 3,

		/// <summary>May edit the notice.</summary>
		EditNotice = 1L << 4,

		/// <summary>May rename ranks and change their permissions, below the holder's own rank.</summary>
		EditRanks = 1L << 5,

		/// <summary>
		/// May withdraw from and administer the guild bank.
		/// </summary>
		/// <remarks>
		/// Reserved. The guild bank (E9) is a separate agent's work and is blocked behind the item
		/// persistence rework; the flag is defined now so that the bank does not have to widen the
		/// permission mask — and therefore re-migrate every guild — when it lands.
		/// </remarks>
		ManageBank = 1L << 6,

		/// <summary>May see and accept or decline recruitment applications.</summary>
		ManageApplications = 1L << 7,

		/// <summary>May disband the guild.</summary>
		Disband = 1L << 8,

		/// <summary>May edit the recruitment advertisement (blurb, tags, recruiting flag).</summary>
		EditRecruitment = 1L << 9,

		/// <summary>May read the officer-only note on a member.</summary>
		ViewOfficerNotes = 1L << 10,

		/// <summary>May write the officer-only note on a member.</summary>
		EditOfficerNotes = 1L << 11,

		/// <summary>May write the public note on a member. Everyone may READ the public note.</summary>
		EditPublicNotes = 1L << 12,

		/// <summary>May hand leadership to another member. Costs the holder their own seat.</summary>
		TransferLeadership = 1L << 13,

		/// <summary>
		/// Every currently defined permission.
		/// </summary>
		/// <remarks>
		/// Written out rather than as <c>~0</c>. A stored <c>-1</c> would silently acquire every
		/// permission added afterwards, which is exactly the behaviour a leader wants and exactly
		/// the behaviour nobody else should get by accident — and it would also make an equality
		/// comparison against this constant fail the moment a flag is appended.
		/// </remarks>
		All =
			Invite |
			Kick |
			Promote |
			EditMessageOfTheDay |
			EditNotice |
			EditRanks |
			ManageBank |
			ManageApplications |
			Disband |
			EditRecruitment |
			ViewOfficerNotes |
			EditOfficerNotes |
			EditPublicNotes |
			TransferLeadership,
	}
}
