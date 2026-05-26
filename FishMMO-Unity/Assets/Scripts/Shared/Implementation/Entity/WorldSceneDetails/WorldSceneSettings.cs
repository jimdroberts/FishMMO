using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour holding per-scene client-facing configuration (max client count, transition
	/// visuals). Day/night cycle authoring has moved to <see cref="WorldDayNightCycle"/> on its
	/// own component so a scene can mix and match (a dungeon may want only the settings, a
	/// surface zone may want both).
	/// </summary>
	public class WorldSceneSettings : MonoBehaviour
	{
		/// <summary>
		/// The maximum number of clients allowed in this scene.
		/// </summary>
		[Tooltip("The maximum number of clients allowed in this scene.")]
		public int MaxClients = 100;

		/// <summary>
		/// The image that will be displayed when entering this scene.
		/// </summary>
		[Tooltip("The image that will be displayed when entering this scene.")]
		public Sprite SceneTransitionImage;
	}
}