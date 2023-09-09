using System.Collections.Generic;

public class Party
{
	public const int MAX_PARTY_SIZE = 5;

	public ulong ID;
	public long LeaderID;
	public readonly Dictionary<long, PartyController> Members = new Dictionary<long, PartyController>();

	public bool IsFull { get { return !(Members.Count < MAX_PARTY_SIZE);  } }

	public Party(ulong partyId, PartyController leader)
	{
		ID = partyId;
		Members.Add(leader.Character.ID, leader);
	}

	public PartyController RemoveMember(long memberId)
	{
		if (Members.TryGetValue(memberId, out PartyController controller))
		{
			Members.Remove(memberId);
			return controller;
		}
		return null;
	}
}