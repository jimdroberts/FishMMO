using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// The result of the group finder forming a group: the party it created, the instance it
	/// opened for them, and who is in it.
	/// </summary>
	/// <remarks>
	/// <see cref="Formed"/> is false when fewer than the requested number of eligible players
	/// were waiting. That is the ordinary outcome of most matching attempts and is reported as a
	/// successful result carrying no group, not as a failure.
	/// </remarks>
	public struct GroupFinderMatchData
	{
		/// <summary>True when a group was formed and the other fields are meaningful.</summary>
		public readonly bool Formed;
		/// <summary>The party created for the group.</summary>
		public readonly long PartyID;
		/// <summary>The instance row opened for the group, in the Pending state.</summary>
		public readonly long InstanceID;
		/// <summary>The member who leads the party: the one who had waited longest.</summary>
		public readonly long LeaderCharacterID;
		/// <summary>Every member of the group, leader first.</summary>
		public readonly IReadOnlyList<long> MemberCharacterIDs;

		/// <summary>No group formed.</summary>
		public static readonly GroupFinderMatchData None = new GroupFinderMatchData(false, 0, 0, 0, Array.Empty<long>());

		public GroupFinderMatchData(bool formed, long partyID, long instanceID, long leaderCharacterID, IReadOnlyList<long> memberCharacterIDs)
		{
			Formed = formed;
			PartyID = partyID;
			InstanceID = instanceID;
			LeaderCharacterID = leaderCharacterID;
			MemberCharacterIDs = memberCharacterIDs ?? Array.Empty<long>();
		}
	}
}
