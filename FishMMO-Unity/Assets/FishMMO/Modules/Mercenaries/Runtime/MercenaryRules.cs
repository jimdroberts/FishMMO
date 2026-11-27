using System;

namespace FishMMO.Shared
{
	/// <summary>The answer to a mercenary request: hire, rest, return, release, stance, gear.</summary>
	public enum MercenaryResult : byte
	{
		/// <summary>Done.</summary>
		Done = 0,
		/// <summary>The player is not at a Mercenary (out of range, or it is gone).</summary>
		NotAtRecruiter = 1,
		/// <summary>The player already has a mercenary, out or resting: one at a time.</summary>
		AlreadyHaveOne = 2,
		/// <summary>The player is in a party and does not lead it: only the leader hires or brings one back.</summary>
		NotLeader = 3,
		/// <summary>The party has no free seat for a mercenary.</summary>
		PartyFull = 4,
		/// <summary>Another member already has a mercenary in the party: a party holds one.</summary>
		PartyHasMercenary = 5,
		/// <summary>The player cannot pay the hire.</summary>
		CannotAfford = 6,
		/// <summary>The player has no mercenary.</summary>
		NoMercenary = 7,
		/// <summary>The mercenary is resting; return it first.</summary>
		Resting = 8,
		/// <summary>The mercenary is already out.</summary>
		AlreadyOut = 9,
		/// <summary>That item does not go in a mercenary's gear (or not in that slot).</summary>
		DoesNotFit = 10,
		/// <summary>No room in the bags for the item taken off.</summary>
		NoRoom = 11,
		/// <summary>Another mercenary request is still being answered.</summary>
		Busy = 12,
		/// <summary>The player cannot act (dead, stunned).</summary>
		CannotAct = 13,
		/// <summary>The server could not do it; nothing changed (a hire's fee is given back).</summary>
		Failed = 14,
		/// <summary>The mercenary was killed and has left; its gear is kept.</summary>
		Died = 15,
		/// <summary>The mercenary was sent to rest because its owner left the party.</summary>
		RestedLeftParty = 16,
		/// <summary>Another player is hiring that Mercenary right now.</summary>
		RecruiterTaken = 17,
	}

	/// <summary>
	/// The Mercenaries module's rules, pure so they can be pinned without a scene: who may hire, what a gear slot takes,
	/// and what each answer says.
	/// </summary>
	public static class MercenaryRules
	{
		/// <summary>How many gear slots a mercenary has: one per equipment socket, as a character's equipment.</summary>
		public static int GearSlotCount => Enum.GetNames(typeof(ItemSlot)).Length;

		/// <summary>The longest name a hired mercenary keeps (the database column).</summary>
		public const int MaxNameLength = 32;

		/// <summary>
		/// Whether a player may hire now, before any party seat is asked for: one mercenary at a time, and inside a party
		/// only its leader hires. With no party, hiring forms one (the mercenary takes a seat in it). Money is checked last
		/// so the more useful answer comes first.
		/// </summary>
		/// <param name="hasMercenary">The player already has one, out or resting.</param>
		/// <param name="partyID">The player's party, or 0.</param>
		/// <param name="rank">The player's rank in it.</param>
		/// <param name="balance">What the player holds.</param>
		/// <param name="cost">The kit's hire cost.</param>
		public static MercenaryResult CheckHire(bool hasMercenary, long partyID, PartyRank rank, long balance, long cost)
		{
			if (hasMercenary)
			{
				return MercenaryResult.AlreadyHaveOne;
			}
			if (partyID > 0 && rank != PartyRank.Leader)
			{
				return MercenaryResult.NotLeader;
			}
			if (cost > 0 && balance < cost)
			{
				return MercenaryResult.CannotAfford;
			}
			return MercenaryResult.Done;
		}

		/// <summary>
		/// Whether a player may bring a resting mercenary back: only a resting one, and inside a party only its leader. With
		/// no party, returning forms one. Free: the hire was paid once.
		/// </summary>
		public static MercenaryResult CheckReturn(bool hasMercenary, bool resting, long partyID, PartyRank rank)
		{
			if (!hasMercenary)
			{
				return MercenaryResult.NoMercenary;
			}
			if (!resting)
			{
				return MercenaryResult.AlreadyOut;
			}
			if (partyID > 0 && rank != PartyRank.Leader)
			{
				return MercenaryResult.NotLeader;
			}
			return MercenaryResult.Done;
		}

		/// <summary>
		/// Whether a live mercenary keeps its seat: its owner is still in the party it was seated in. An owner who left (or
		/// was removed, or joined another party) took the seat with the membership row, so the mercenary rests.
		/// </summary>
		public static bool KeepsSeat(long seatedPartyID, long ownerPartyID)
		{
			return seatedPartyID > 0 && seatedPartyID == ownerPartyID;
		}

		/// <summary>
		/// Whether a mercenary wears what goes in <paramref name="slot"/>: everything a player wears but a saddle (a
		/// mercenary does not ride). The container keeps one slot per socket all the same, so its indexes stay the sockets'.
		/// </summary>
		public static bool Wears(ItemSlot slot)
		{
			return slot != ItemSlot.MountSaddle;
		}

		/// <summary>The gear slot an item goes in, or -1 when a mercenary does not wear it (only equippable items, no saddle).</summary>
		public static int GearSlotFor(BaseItemTemplate template)
		{
			return template is EquippableItemTemplate equippable && Wears(equippable.Slot) ? (int)equippable.Slot : -1;
		}

		/// <summary>True when the item goes in <paramref name="gearSlot"/>.</summary>
		public static bool Fits(Item item, int gearSlot)
		{
			return item != null && item.Template != null && gearSlot >= 0 && gearSlot < GearSlotCount && GearSlotFor(item.Template) == gearSlot;
		}

		/// <summary>A name cut to <see cref="MaxNameLength"/>; "Mercenary" for none.</summary>
		public static string CleanName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return "Mercenary";
			}
			name = name.Trim();
			return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
		}

		/// <summary>What the player is told. <paramref name="name"/> is the mercenary's (or the kit's) name.</summary>
		public static string Describe(MercenaryResult result, string name)
		{
			string who = string.IsNullOrEmpty(name) ? "Your mercenary" : name;
			switch (result)
			{
				case MercenaryResult.Done: return string.Empty;
				case MercenaryResult.NotAtRecruiter: return "You are too far from the mercenary.";
				case MercenaryResult.AlreadyHaveOne: return "You already have a mercenary. Release them first (Mercenary window).";
				case MercenaryResult.NotLeader: return "Only the party leader can bring a mercenary into the party.";
				case MercenaryResult.PartyFull: return "Your party has no room for a mercenary.";
				case MercenaryResult.PartyHasMercenary: return "Your party already has a mercenary. A party can hold one.";
				case MercenaryResult.CannotAfford: return "You cannot afford to hire this mercenary.";
				case MercenaryResult.NoMercenary: return "You have no mercenary.";
				case MercenaryResult.Resting: return $"{who} is resting. Return them first.";
				case MercenaryResult.AlreadyOut: return $"{who} is already with you.";
				case MercenaryResult.DoesNotFit: return "A mercenary cannot wear that.";
				case MercenaryResult.NoRoom: return "Your bags are full.";
				case MercenaryResult.Busy: return "Please wait a moment.";
				case MercenaryResult.CannotAct: return "You cannot do that right now.";
				case MercenaryResult.Died: return $"{who} has fallen and left your service. Their gear is kept for your next mercenary.";
				case MercenaryResult.RestedLeftParty: return $"{who} is resting: you left the party. Return them from the Mercenary window.";
				case MercenaryResult.RecruiterTaken: return $"{who} has just been hired by someone else.";
				default: return "That could not be done. Nothing was changed.";
			}
		}
	}
}
