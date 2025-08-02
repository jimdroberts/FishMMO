using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for controllers managing party membership, invites, and rank for a character.
	/// </summary>
	public interface IPartyController : ICharacterBehaviour
	{
		/// <summary>
		/// Event triggered when a party is created. Provides the party name.
		/// </summary>
		event Action<string> OnPartyCreated;

		/// <summary>
		/// Event triggered when a party invite is received. Provides the inviter's ID.
		/// </summary>
		event Action<long> OnReceivePartyInvite;

		/// <summary>
		/// Event triggered when a party member is added. Provides member ID, rank, and health percent.
		/// </summary>
		event Action<long, PartyRank, float> OnAddPartyMember;

		/// <summary>
		/// Event triggered to validate the current set of party members.
		/// </summary>
		event Action<HashSet<long>> OnValidatePartyMembers;

		/// <summary>
		/// Event triggered when a party member is removed. Provides member ID.
		/// </summary>
		event Action<long> OnRemovePartyMember;

		/// <summary>
		/// Event triggered when the character leaves the party.
		/// </summary>
		event Action OnLeaveParty;

		/// <summary>
		/// The unique ID of the party or party member.
		/// </summary>
		long ID { get; set; }

		/// <summary>
		/// The rank of the character within the party (e.g., leader, member).
		/// </summary>
		PartyRank Rank { get; set; }
	}
}