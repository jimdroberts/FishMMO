using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary of a scene's waypoints, keyed by authored waypoint index.
	/// </summary>
	[Serializable]
	public class SceneWaypointDictionary : SerializableDictionary<int, SceneWaypointDetails> { }
}
