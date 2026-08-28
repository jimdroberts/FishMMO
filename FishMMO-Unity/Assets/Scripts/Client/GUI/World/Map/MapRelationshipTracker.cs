using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Keeps track of which character IDs are in the local player's party and guild, and answers
	/// what the local player's relationship to another character is.
	/// </summary>
	/// <remarks>
	/// <para><b>Why a tracker and not a lookup.</b> Neither <c>IPartyController</c> nor
	/// <c>IGuildController</c> exposes its membership: both are event streams, and every panel
	/// that needs the roster builds its own copy from the broadcasts. The map needs the same
	/// answer several times a second for every marker in view, so it keeps two hash sets fed by
	/// those same events rather than asking a panel that may not be open.</para>
	///
	/// <para><b>Why it is not read off the other character.</b> The obvious implementation — read
	/// the other character's own <c>PartyController.ID</c> and compare — does not work and would
	/// fail quietly. Those controllers are filled from broadcasts sent to their owner, so on this
	/// client every remote character's party ID is zero. Comparing zeros would put every stranger
	/// in the player's party.</para>
	/// </remarks>
	public static class MapRelationshipTracker
	{
		/// <summary>Character IDs currently in the local player's party.</summary>
		private static readonly HashSet<long> partyMembers = new HashSet<long>();

		/// <summary>Character IDs currently in the local player's guild.</summary>
		private static readonly HashSet<long> guildMembers = new HashSet<long>();

		/// <summary>The character whose party and guild are being tracked.</summary>
		private static IPlayerCharacter tracked;

		/// <summary>Character IDs currently in the local player's party. Do not mutate.</summary>
		public static IReadOnlyCollection<long> PartyMembers => partyMembers;

		/// <summary>Character IDs currently in the local player's guild. Do not mutate.</summary>
		public static IReadOnlyCollection<long> GuildMembers => guildMembers;

		/// <summary>
		/// Starts tracking a character's party and guild.
		/// </summary>
		/// <param name="character">The local player character, or null to stop tracking.</param>
		/// <remarks>
		/// Idempotent, and safe to call with a different character: the previous subscriptions are
		/// dropped first. World entry hands every panel a character at once and a panel that has
		/// not built its visual tree yet is handed it again later, so this is called more than
		/// once per character as a matter of course.
		/// </remarks>
		public static void Track(IPlayerCharacter character)
		{
			if (ReferenceEquals(tracked, character))
			{
				return;
			}

			Untrack();

			tracked = character;
			if (character == null)
			{
				return;
			}

			if (character.TryGet(out IPartyController partyController))
			{
				partyController.OnAddPartyMember += Party_OnAddMember;
				partyController.OnRemovePartyMember += Party_OnRemoveMember;
				partyController.OnValidatePartyMembers += Party_OnValidateMembers;
				partyController.OnLeaveParty += Party_OnLeave;
			}

			if (character.TryGet(out IGuildController guildController))
			{
				guildController.OnAddGuildMember += Guild_OnAddMember;
				guildController.OnRemoveGuildMember += Guild_OnRemoveMember;
				guildController.OnValidateGuildMembers += Guild_OnValidateMembers;
				guildController.OnLeaveGuild += Guild_OnLeave;
			}
		}

		/// <summary>
		/// Stops tracking and forgets both rosters.
		/// </summary>
		public static void Untrack()
		{
			if (tracked != null)
			{
				if (tracked.TryGet(out IPartyController partyController))
				{
					partyController.OnAddPartyMember -= Party_OnAddMember;
					partyController.OnRemovePartyMember -= Party_OnRemoveMember;
					partyController.OnValidatePartyMembers -= Party_OnValidateMembers;
					partyController.OnLeaveParty -= Party_OnLeave;
				}

				if (tracked.TryGet(out IGuildController guildController))
				{
					guildController.OnAddGuildMember -= Guild_OnAddMember;
					guildController.OnRemoveGuildMember -= Guild_OnRemoveMember;
					guildController.OnValidateGuildMembers -= Guild_OnValidateMembers;
					guildController.OnLeaveGuild -= Guild_OnLeave;
				}

				tracked = null;
			}

			partyMembers.Clear();
			guildMembers.Clear();
		}

		/// <summary>
		/// Works out how the local player stands towards another character.
		/// </summary>
		/// <param name="local">The local player character.</param>
		/// <param name="other">The character being classified. May be null.</param>
		/// <returns>The relationship, most trusted match first.</returns>
		public static MapRelationship Resolve(IPlayerCharacter local, ICharacter other)
		{
			if (other == null)
			{
				return MapRelationship.NonPlayer;
			}

			if (local != null && other.ID == local.ID)
			{
				return MapRelationship.Self;
			}

			if (!(other is IPlayerCharacter))
			{
				return MapRelationship.NonPlayer;
			}

			if (partyMembers.Contains(other.ID))
			{
				return MapRelationship.Party;
			}

			if (guildMembers.Contains(other.ID))
			{
				return MapRelationship.Guild;
			}

			/* Faction decides the rest. A character with no faction controller — which is every
			 * character until its faction state has arrived — is neutral rather than hostile:
			 * treating an unknown as an enemy would flash a red marker on every player the moment
			 * they streamed in and then correct itself, which reads as an ambush that is not
			 * happening. */
			if (local != null &&
				local.TryGet(out IFactionController localFactions) &&
				other.TryGet(out IFactionController otherFactions))
			{
				switch (localFactions.GetAllianceLevel(otherFactions))
				{
					case FactionAllianceLevel.Ally:
						return MapRelationship.FriendlyPlayer;
					case FactionAllianceLevel.Enemy:
						return MapRelationship.HostilePlayer;
				}
			}

			return MapRelationship.NeutralPlayer;
		}

		/// <summary>Adds a party member.</summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <param name="rank">The member's party rank. Unused here.</param>
		/// <param name="healthPCT">The member's health fraction. Unused here.</param>
		private static void Party_OnAddMember(long characterID, PartyRank rank, float healthPCT)
		{
			partyMembers.Add(characterID);
		}

		/// <summary>Removes a party member.</summary>
		/// <param name="characterID">The member's character ID.</param>
		private static void Party_OnRemoveMember(long characterID)
		{
			partyMembers.Remove(characterID);
		}

		/// <summary>Replaces the party roster with the server's authoritative list.</summary>
		/// <param name="members">The full membership.</param>
		private static void Party_OnValidateMembers(HashSet<long> members)
		{
			partyMembers.Clear();
			if (members != null)
			{
				partyMembers.UnionWith(members);
			}
		}

		/// <summary>Clears the party roster.</summary>
		private static void Party_OnLeave()
		{
			partyMembers.Clear();
		}

		/// <summary>Adds a guild member.</summary>
		/// <param name="broadcast">The add broadcast carrying the member's character ID.</param>
		private static void Guild_OnAddMember(GuildAddBroadcast broadcast)
		{
			guildMembers.Add(broadcast.CharacterID);
		}

		/// <summary>Removes a guild member.</summary>
		/// <param name="characterID">The member's character ID.</param>
		private static void Guild_OnRemoveMember(long characterID)
		{
			guildMembers.Remove(characterID);
		}

		/// <summary>Replaces the guild roster with the server's authoritative list.</summary>
		/// <param name="members">The full membership.</param>
		private static void Guild_OnValidateMembers(HashSet<long> members)
		{
			guildMembers.Clear();
			if (members != null)
			{
				guildMembers.UnionWith(members);
			}
		}

		/// <summary>Clears the guild roster.</summary>
		private static void Guild_OnLeave()
		{
			guildMembers.Clear();
		}
	}
}
