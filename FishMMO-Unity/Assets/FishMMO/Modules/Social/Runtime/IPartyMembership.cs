using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Social module's side of a character's party: changing the membership, invitations, live vitals and the
	/// party triggers. The core reads the membership through <see cref="IPartyController"/>.
	/// </summary>
	public interface IPartyMembership : IPartyController
	{
		/// <summary>The party's ID; 0 when the character is in no party. Set by the party system only.</summary>
		new long ID { get; set; }

		/// <summary>The character's rank in its party. Set by the party system only.</summary>
		new PartyRank Rank { get; set; }

		/// <summary>Client: the local character created a party.</summary>
		event Action OnPartyCreated;

		/// <summary>Client: an invitation arrived; the inviter's character ID.</summary>
		event Action<long> OnReceivePartyInvite;

		/// <summary>
		/// Client: live state for the party members sharing the local character's scene, the whole payload at once.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Distinct from <see cref="IPartyController.OnAddPartyMember"/>, which also carries a health figure but gets it
		/// from the party database row, written only on connect and disconnect. This is the live value, pushed by the
		/// scene server hosting the member.
		/// </para>
		/// <para>
		/// Raised with the WHOLE payload rather than once per member, because the payload is complete for the
		/// recipient's scene and its completeness is the point: a roster member absent from it is a member who is
		/// somewhere else.
		/// </para>
		/// </remarks>
		event Action<PartyMemberVitalsEntry[]> OnUpdatePartyVitals;

		/// <summary>Triggers invoked when the character joins or creates a party.</summary>
		List<Trigger> OnPartyJoinTriggers { get; }

		/// <summary>Triggers invoked when the character leaves a party.</summary>
		List<Trigger> OnPartyLeaveTriggers { get; }
	}
}
