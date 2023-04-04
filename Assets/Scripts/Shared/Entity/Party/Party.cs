using System.Collections.Generic;

public class Party
{
	public const int MAX_PARTY_SIZE = 5;

	public ulong id;
	public readonly List<PartyController> members = new List<PartyController>();

	public bool IsFull { get { return !(members.Count < MAX_PARTY_SIZE);  } }

	public Party(ulong partyId, PartyController leader)
	{
		id = partyId;
		members.Add(leader);
	}

	public PartyController RemoveMember(int memberId)
	{
		foreach (PartyController member in members)
		{
			if (member.character.OwnerId == memberId)
			{
				members.Remove(member);
				return member;
			}
		}
		return null;
	}
}