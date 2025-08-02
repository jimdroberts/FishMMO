using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for scene objects with a unique ID and associated GameObject in FishMMO.
	/// </summary>
	public interface ISceneObject
	{
		/// <summary>
		/// Unique identifier for the scene object.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// The Unity GameObject associated with this scene object.
		/// </summary>
		public GameObject GameObject { get; }
	}
}