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

	public PartyController RemoveMember(long memberId)
	{
		foreach (PartyController member in members)
		{
			if (member.Character.ID == memberId)
			{
				members.Remove(member);
				return member;
			}
		}
		return null;
	}

	public PartyController RemoveMember(string memberName)
	{
		foreach (PartyController member in members)
		{
			if (member.Character.CharacterName.Equals(memberName))
			{
				members.Remove(member);
				return member;
			}
		}
		return null;
	}
}