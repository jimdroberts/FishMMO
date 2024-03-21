using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct CharacterCreateBroadcast : IBroadcast
	{
		public string characterName;
		public int raceTemplateID;
		public string sceneName;
		public string spawnerName;
	}

	public struct CharacterCreateResultBroadcast : IBroadcast
	{
		public CharacterCreateResult result;
	}
}