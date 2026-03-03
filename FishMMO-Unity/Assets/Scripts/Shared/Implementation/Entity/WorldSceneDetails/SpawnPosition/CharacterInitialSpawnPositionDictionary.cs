using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary mapping string keys to character initial spawn position details.
	/// Used to store and retrieve initial spawn locations and settings for characters.
	/// </summary>
	[Serializable]
	public class CharacterInitialSpawnPositionDictionary : SerializableDictionary<string, CharacterInitialSpawnPositionDetails> { }
}