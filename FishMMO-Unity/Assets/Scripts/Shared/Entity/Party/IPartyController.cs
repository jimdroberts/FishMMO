using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IPartyController : ICharacterBehaviour
	{
		event Action<string> OnPartyCreated;
		event Action<long> OnReceivePartyInvite;
		event Action<long, PartyRank, float> OnAddPartyMember;
		event Action<HashSet<long>> OnValidatePartyMembers;
		event Action<long> OnRemovePartyMember;
		event Action OnLeaveParty;

		long ID { get; set; }
		PartyRank Rank { get; set; }
	}
}