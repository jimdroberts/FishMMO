using System;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// A party seat held by a member's companion rather than by a character: the Mercenaries module's hired mercenary. The
	/// party module counts such a seat wherever it counts members, lists it on the roster under its owner, and pumps its
	/// vitals; it resolves the live companion through here and names no module.
	/// </summary>
	/// <remarks>
	/// The seat itself is recorded on the owner's membership row (the database's <c>character_party.mercenary_kit_id</c>),
	/// so leaving the party takes the seat with it. A companion is not a character with a row of its own, so on the roster
	/// and in the vitals payload it goes by <see cref="RosterKey"/>, the negated owner ID: character IDs are positive, so
	/// the two can never collide, and the owner is recoverable from the key.
	/// </remarks>
	public static class PartySeatCompanions
	{
		/// <summary>
		/// The live companion seated by the member with this character ID on this peer, or null: set by the module that
		/// seats companions. The server's vitals pump reads the companion's health and buffs through it; a client's party
		/// frame pins it through it.
		/// </summary>
		public static Func<long, ICharacter> Resolve;

		/// <summary>The roster key of the companion seated by <paramref name="ownerID"/>.</summary>
		public static long RosterKey(long ownerID) => -ownerID;

		/// <summary>True when <paramref name="rosterKey"/> names a companion seat rather than a character.</summary>
		public static bool IsCompanionKey(long rosterKey) => rosterKey < 0;

		/// <summary>The owner of the companion seat <paramref name="rosterKey"/>, or 0 when it names a character.</summary>
		public static long OwnerOf(long rosterKey) => rosterKey < 0 ? -rosterKey : 0;

		/// <summary>The live companion seated by the member <paramref name="ownerID"/>, or null.</summary>
		public static ICharacter ResolveFor(long ownerID)
		{
			if (ownerID <= 0)
			{
				return null;
			}
			Func<long, ICharacter> resolve = Resolve;
			return resolve != null ? resolve(ownerID) : null;
		}
	}
}
