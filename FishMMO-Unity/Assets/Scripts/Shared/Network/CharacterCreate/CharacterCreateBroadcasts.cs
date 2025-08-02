using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for creating a new character.
	/// Contains character name, race, model, and spawn location details.
	/// </summary>
	public struct CharacterCreateBroadcast : IBroadcast
	{
		/// <summary>Name of the character to create.</summary>
		public string CharacterName;
		/// <summary>Template ID for the character's race.</summary>
		public int RaceTemplateID;
		/// <summary>Index of the character model to use.</summary>
		public int ModelIndex;
		/// <summary>Name of the scene where the character will be spawned.</summary>
		public string SceneName;
		/// <summary>Name of the spawner to use for character placement.</summary>
		public string SpawnerName;
	}

	/// <summary>
	/// Broadcast for reporting the result of a character creation attempt.
	/// Contains the result status.
	/// </summary>
	public struct CharacterCreateResultBroadcast : IBroadcast
	{
		/// <summary>Result of the character creation operation.</summary>
		public CharacterCreateResult Result;
	}
}