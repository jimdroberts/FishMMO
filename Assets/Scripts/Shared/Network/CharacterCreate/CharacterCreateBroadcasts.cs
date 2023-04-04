using FishNet.Broadcast;

public struct CharacterCreateBroadcast : IBroadcast
{
	public string characterName;
	public string raceName;
	public CharacterInitialSpawnPosition initialSpawnPosition;
}

public struct CharacterCreateResultBroadcast : IBroadcast
{
	public CharacterCreateResult result;
}