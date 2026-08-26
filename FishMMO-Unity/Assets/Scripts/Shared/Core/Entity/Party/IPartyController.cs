using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
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
		/// Event triggered when the scene server pushes live state for the party members sharing
		/// the local character's scene.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Distinct from <see cref="OnAddPartyMember"/>, which also carries a health figure but
		/// gets it from the party database row — a row written only on connect and disconnect.
		/// This event is the live value, pushed by the scene server hosting the member.
		/// </para>
		/// <para>
		/// Raised with the WHOLE payload rather than once per member, because the payload is
		/// complete for the recipient's scene and its completeness is the point: a roster member
		/// absent from it is a member who is somewhere else. Splitting it into per-member events
		/// would throw away exactly the fact a subscriber needs to grey that member's row out.
		/// </para>
		/// </remarks>
		event Action<PartyMemberVitalsEntry[]> OnUpdatePartyVitals;

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

		/// <summary>
		/// Triggers invoked when the character joins or creates a party.
		/// </summary>
		List<Trigger> OnPartyJoinTriggers { get; }
		/// <summary>
		/// Triggers invoked when the character leaves a party.
		/// </summary>
		List<Trigger> OnPartyLeaveTriggers { get; }
	}
}