using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct CharacterCreateBroadcast : IBroadcast
	{
		public string characterName;
		public int raceIndex;
		public CharacterInitialSpawnPositionDetails initialSpawnPosition;
	}

	public struct CharacterCreateResultBroadcast : IBroadcast
	{
		public CharacterCreateResult result;
	}
}