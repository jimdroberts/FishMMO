using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary mapping string keys to scene teleporter details.
	/// Used to store and retrieve teleporter destinations and settings for scenes.
	/// </summary>
	[Serializable]
	public class SceneTeleporterDictionary : SerializableDictionary<string, SceneTeleporterDetails> { }
}