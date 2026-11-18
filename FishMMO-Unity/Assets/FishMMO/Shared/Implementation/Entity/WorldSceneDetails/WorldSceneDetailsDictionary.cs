using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary mapping scene names to their configuration details.
	/// Used to store and access all world scene details for the game.
	/// </summary>
	[Serializable]
	public class WorldSceneDetailsDictionary : SerializableDictionary<string, WorldSceneDetails> { }
}