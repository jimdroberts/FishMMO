using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Where a trade session is in its life.
	/// </summary>
	public enum TradePhase : byte
	{
		/// <summary>Both parties may change their offers and accept.</summary>
		Open = 0,

		/// <summary>
		/// Both accepted and the exchange is being applied. No request from either party is
		/// honoured; the session ends in <see cref="Closed"/> whether the exchange landed or not.
		/// </summary>
		Committing = 1,

		/// <summary>Over. The object is kept only until the system drops its references.</summary>
		Closed = 2,
	}

	/// <summary>
	/// One item on the table.
	/// </summary>
	public readonly struct TradeOffer
	{
		/// <summary>The offering character's inventory slot.</summary>
		public readonly int Slot;

		/// <summary>The item's identity at the time it was offered. Re-checked at completion.</summary>
		public readonly long ItemID;

		/// <summary>Template, so the partner can draw it and so completion can refuse a swapped item.</summary>
		public readonly int TemplateID;

		/// <summary>Generation seed, for the partner's tooltip.</summary>
		public readonly int Seed;

		/// <summary>Quantity offered.</summary>
		public readonly uint Amount;

		public TradeOffer(int slot, long itemID, int templateID, int seed, uint amount)
		{
			Slot = slot;
			ItemID = itemID;
			TemplateID = templateID;
			Seed = seed;
			Amount = amount;
		}

		/// <summary>The wire form of this offer.</summary>
		public TradeOfferEntry ToEntry()
		{
			return new TradeOfferEntry
			{
				Slot = Slot,
				ItemID = ItemID,
				TemplateID = TemplateID,
				Seed = Seed,
				Amount = Amount,
			};
		}
	}

	/// <summary>
	/// Why an offer change was refused.
	/// </summary>
	public enum TradeOfferRefusal : byte
	{
		None = 0,

		/// <summary>The session is not open for changes.</summary>
		NotOpen,

		/// <summary>The character is not a party to this session.</summary>
		NotAParty,

		/// <summary>That slot is already on the table.</summary>
		AlreadyOffered,

		/// <summary>The table is full.</summary>
		TableFull,

		/// <summary>No such offer to withdraw.</summary>
		NotOffered,

		/// <summary>A negative currency offer.</summary>
		NegativeCurrency,

		/// <summary>The accept quoted a version that is no longer current.</summary>
		StaleVersion,
	}

	/// <summary>
	/// The state of one open trade between two characters.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Pure state with the rules that govern it, and nothing else: no network, no containers,
	/// no Unity. <c>TradeSystem</c> resolves characters and inventories, validates what a
	/// request refers to, and then asks this object whether the change is allowed and records
	/// it here. That split is what lets the acceptance rules be tested as a truth table.
	/// </para>
	/// <para>
	/// THE INVARIANT: <b>every change to either offer clears both acceptances and bumps
	/// <see cref="Version"/>.</b> There is no way to alter what is on the table that leaves an
	/// acceptance standing, because the classic exploit is exactly that — swap the goods after
	/// the other party has said yes. <see cref="TryAccept"/> additionally refuses an accept that
	/// quotes any version but the current one, so a click that was in flight while the table
	/// changed lands as a no-op rather than as consent to something the player never saw.
	/// </para>
	/// </remarks>
	public sealed class TradeSession
	{
		/// <summary>One side of the table.</summary>
		public sealed class Party
		{
			public readonly long CharacterID;

			/// <summary>Items on the table, in the order they were offered.</summary>
			public readonly List<TradeOffer> Offers = new List<TradeOffer>();

			/// <summary>Currency on the table.</summary>
			public long Currency;

			/// <summary>Whether this side has accepted the current <see cref="TradeSession.Version"/>.</summary>
			public bool Accepted;

			public Party(long characterID)
			{
				CharacterID = characterID;
			}

			/// <summary>Index of the offer made from <paramref name="slot"/>, or -1.</summary>
			public int IndexOfSlot(int slot)
			{
				for (int i = 0; i < Offers.Count; ++i)
				{
					if (Offers[i].Slot == slot)
					{
						return i;
					}
				}
				return -1;
			}

			/// <summary>True when nothing at all is on this side of the table.</summary>
			public bool IsEmpty => Offers.Count == 0 && Currency <= 0;

			/// <summary>The wire form of this side's items.</summary>
			public TradeOfferEntry[] ToEntries()
			{
				var entries = new TradeOfferEntry[Offers.Count];
				for (int i = 0; i < Offers.Count; ++i)
				{
					entries[i] = Offers[i].ToEntry();
				}
				return entries;
			}
		}

		/// <summary>Process-unique session number, for log lines.</summary>
		public readonly long ID;

		public readonly Party First;
		public readonly Party Second;

		/// <summary>
		/// Increments on every change to either side. Quoted back by accepts.
		/// </summary>
		/// <remarks>
		/// Starts at 1 so a zeroed <c>TradeAcceptBroadcast</c> — one a client never filled in —
		/// can never match.
		/// </remarks>
		public uint Version { get; private set; } = 1;

		public TradePhase Phase { get; private set; } = TradePhase.Open;

		/// <summary>The most slots one side may offer, fixed at creation from the system asset.</summary>
		public readonly int MaxOfferSlots;

		/// <summary>Server time of the next range check. Owned by the system's tick.</summary>
		public double NextRangeCheckTime;

		public TradeSession(long id, long firstCharacterID, long secondCharacterID, int maxOfferSlots)
		{
			ID = id;
			First = new Party(firstCharacterID);
			Second = new Party(secondCharacterID);
			MaxOfferSlots = maxOfferSlots < 1 ? 1 : maxOfferSlots;
		}

		/// <summary>True when the character is one of the two parties.</summary>
		public bool Involves(long characterID)
		{
			return First.CharacterID == characterID || Second.CharacterID == characterID;
		}

		/// <summary>The side belonging to <paramref name="characterID"/>, or null.</summary>
		public Party PartyOf(long characterID)
		{
			if (First.CharacterID == characterID) return First;
			if (Second.CharacterID == characterID) return Second;
			return null;
		}

		/// <summary>The side that is NOT <paramref name="characterID"/>, or null when they are not a party.</summary>
		public Party PartnerOf(long characterID)
		{
			if (First.CharacterID == characterID) return Second;
			if (Second.CharacterID == characterID) return First;
			return null;
		}

		/// <summary>True when both sides have accepted the current version.</summary>
		public bool BothAccepted => First.Accepted && Second.Accepted;

		/// <summary>
		/// Records that the table changed: bumps the version and clears both acceptances.
		/// </summary>
		/// <remarks>
		/// The one place the invariant is enforced. Every mutator below calls it; a mutator
		/// that did not would be the exploit.
		/// </remarks>
		private void Touch()
		{
			Version++;
			First.Accepted = false;
			Second.Accepted = false;
		}

		/// <summary>
		/// Puts an item on the table for <paramref name="characterID"/>.
		/// </summary>
		public TradeOfferRefusal TryAddOffer(long characterID, TradeOffer offer)
		{
			if (Phase != TradePhase.Open)
			{
				return TradeOfferRefusal.NotOpen;
			}

			Party party = PartyOf(characterID);
			if (party == null)
			{
				return TradeOfferRefusal.NotAParty;
			}

			if (party.IndexOfSlot(offer.Slot) >= 0)
			{
				return TradeOfferRefusal.AlreadyOffered;
			}

			if (party.Offers.Count >= MaxOfferSlots)
			{
				return TradeOfferRefusal.TableFull;
			}

			party.Offers.Add(offer);
			Touch();
			return TradeOfferRefusal.None;
		}

		/// <summary>
		/// Takes the offer made from <paramref name="slot"/> back off the table.
		/// </summary>
		public TradeOfferRefusal TryRemoveOffer(long characterID, int slot, out TradeOffer removed)
		{
			removed = default;

			if (Phase != TradePhase.Open)
			{
				return TradeOfferRefusal.NotOpen;
			}

			Party party = PartyOf(characterID);
			if (party == null)
			{
				return TradeOfferRefusal.NotAParty;
			}

			int index = party.IndexOfSlot(slot);
			if (index < 0)
			{
				return TradeOfferRefusal.NotOffered;
			}

			removed = party.Offers[index];
			party.Offers.RemoveAt(index);
			Touch();
			return TradeOfferRefusal.None;
		}

		/// <summary>
		/// Sets the currency on the table for <paramref name="characterID"/>.
		/// </summary>
		/// <remarks>
		/// Setting the same amount again is still a change: it bumps the version and clears
		/// acceptances. A client that re-sends its own value has told the server "I changed
		/// something", and the cost of honouring that literally is one extra round of accepts —
		/// far cheaper than a rule that lets some writes through without clearing consent.
		/// </remarks>
		public TradeOfferRefusal TrySetCurrency(long characterID, long amount)
		{
			if (Phase != TradePhase.Open)
			{
				return TradeOfferRefusal.NotOpen;
			}

			Party party = PartyOf(characterID);
			if (party == null)
			{
				return TradeOfferRefusal.NotAParty;
			}

			if (amount < 0)
			{
				return TradeOfferRefusal.NegativeCurrency;
			}

			party.Currency = amount;
			Touch();
			return TradeOfferRefusal.None;
		}

		/// <summary>
		/// Accepts, or withdraws acceptance of, the table as it stands at <paramref name="version"/>.
		/// </summary>
		/// <remarks>
		/// Withdrawing never fails on version: a player taking consent back must always be
		/// able to, whatever they were looking at. Only granting it is version-gated.
		/// </remarks>
		public TradeOfferRefusal TryAccept(long characterID, bool accept, uint version)
		{
			if (Phase != TradePhase.Open)
			{
				return TradeOfferRefusal.NotOpen;
			}

			Party party = PartyOf(characterID);
			if (party == null)
			{
				return TradeOfferRefusal.NotAParty;
			}

			if (!accept)
			{
				party.Accepted = false;
				return TradeOfferRefusal.None;
			}

			if (version != Version)
			{
				return TradeOfferRefusal.StaleVersion;
			}

			party.Accepted = true;
			return TradeOfferRefusal.None;
		}

		/// <summary>
		/// Moves an open session into <see cref="TradePhase.Committing"/>.
		/// </summary>
		/// <returns>False unless the session was open and both sides had accepted.</returns>
		public bool TryBeginCommit()
		{
			if (Phase != TradePhase.Open || !BothAccepted)
			{
				return false;
			}
			Phase = TradePhase.Committing;
			return true;
		}

		/// <summary>
		/// Ends the session. Idempotent.
		/// </summary>
		public void Close()
		{
			Phase = TradePhase.Closed;
		}

		/// <summary>
		/// The state message for <paramref name="characterID"/>'s side of the table.
		/// </summary>
		public TradeStateBroadcast BuildStateFor(long characterID)
		{
			Party own = PartyOf(characterID);
			Party partner = PartnerOf(characterID);
			if (own == null || partner == null)
			{
				return default;
			}

			return new TradeStateBroadcast
			{
				Version = Version,
				OwnOffer = own.ToEntries(),
				OwnCurrency = own.Currency,
				OwnAccepted = own.Accepted,
				PartnerOffer = partner.ToEntries(),
				PartnerCurrency = partner.Currency,
				PartnerAccepted = partner.Accepted,
			};
		}
	}
}
