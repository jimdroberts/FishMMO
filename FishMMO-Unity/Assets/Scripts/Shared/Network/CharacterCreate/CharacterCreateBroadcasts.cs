using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct CharacterCreateBroadcast : IBroadcast
	{
		public string CharacterName;
		public int RaceTemplateID;
		public string SceneName;
		public string SpawnerName;
	}

	public struct CharacterCreateResultBroadcast : IBroadcast
	{
		public CharacterCreateResult Result;
	}
}