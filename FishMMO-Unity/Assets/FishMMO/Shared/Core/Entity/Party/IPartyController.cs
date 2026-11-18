using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// A character's party membership, as the rest of the game reads it: which party, what rank, and who is in it.
	/// </summary>
	/// <remarks>
	/// The core contract of the Social module's party (Phase 3, 2026-11-16). Read-only: the group finder, the dungeon
	/// finder, arenas, instances, chat, the map, the harm rule and observer streaming all ask "are these two in the
	/// same party?" and "does this one lead it?", and none of them change it. Joining, leaving, invitations and the
	/// roster window are the module's (<c>IPartyMembership</c>, <c>IPartySystem</c>). Without the module no character
	/// has this, and every one of those reads finds no party.
	/// </remarks>
	public interface IPartyController : ICharacterBehaviour
	{
		/// <summary>The party's ID; 0 when the character is in no party.</summary>
		long ID { get; }

		/// <summary>The character's rank in its party (<see cref="PartyRank.None"/> outside one).</summary>
		PartyRank Rank { get; }

		/// <summary>
		/// Client: a member was added to the local character's roster (member ID, rank, health fraction from the party's
		/// database row).
		/// </summary>
		event Action<long, PartyRank, float> OnAddPartyMember;

		/// <summary>Client: the complete set of member IDs, so a listener can drop anyone not in it.</summary>
		event Action<HashSet<long>> OnValidatePartyMembers;

		/// <summary>Client: a member left the local character's party.</summary>
		event Action<long> OnRemovePartyMember;

		/// <summary>Client: the local character left its party.</summary>
		event Action OnLeaveParty;
	}
}
