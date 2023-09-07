using System.Collections.Generic;

public class Party
{
	public const int MAX_PARTY_SIZE = 5;

	public ulong ID;
	public readonly List<PartyController> Members = new List<PartyController>();

	public bool IsFull { get { return !(Members.Count < MAX_PARTY_SIZE);  } }

	public Party(ulong partyId, PartyController leader)
	{
		ID = partyId;
		Members.Add(leader);
	}

	public PartyController RemoveMember(long memberId)
	{
		foreach (PartyController member in Members)
		{
			if (member.Character.ID == memberId)
			{
				Members.Remove(member);
				return member;
			}
		}
		return null;
	}

	public PartyController RemoveMember(string memberName)
	{
		foreach (PartyController member in Members)
		{
			if (member.Character.CharacterName.Equals(memberName))
			{
				Members.Remove(member);
				return member;
			}
		}
		return null;
	}
}