using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary mapping string keys to character respawn position details.
	/// Used to store and retrieve respawn locations and orientations for characters.
	/// </summary>
	[Serializable]
	public class CharacterRespawnPositionDictionary : SerializableDictionary<string, CharacterRespawnPositionDetails> { }
}