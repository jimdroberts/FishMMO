using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour holding per-scene server-facing configuration and the link to the scene's
	/// <see cref="WorldMapDefinition"/>. Day/night cycle authoring has moved to
	/// <see cref="WorldDayNightCycle"/> on its own component so a scene can mix and match (a
	/// dungeon may want only the settings, a surface zone may want both).
	/// </summary>
	public class WorldSceneSettings : MonoBehaviour
	{
		/// <summary>
		/// The maximum number of clients allowed in this scene.
		/// </summary>
		[Tooltip("The maximum number of clients allowed in this scene.")]
		public int MaxClients = 100;

		/// <summary>
		/// The scene's map, loading image and player-facing name.
		/// </summary>
		/// <remarks>
		/// Created and filled in by <c>FishMMO/World Map/Bake Maps</c>, which also assigns it here
		/// if the field is empty. Left assignable by hand so a set of scenes that share a map — a
		/// dungeon and its instanced twin — can point at one definition.
		/// </remarks>
		[Tooltip("The scene's map definition. Created and assigned automatically by FishMMO/World Map/Bake Maps.")]
		public WorldMapDefinition MapDefinition;

		/// <summary>
		/// Legacy home of the scene's loading image, migrated into <see cref="MapDefinition"/>.
		/// </summary>
		/// <remarks>
		/// The image belongs with the rest of a scene's presentation, and living on a component
		/// meant it could only be found by opening the scene. The map bake moves any value still
		/// here into the definition and clears this field, so a scene keeps its image without
		/// anybody re-authoring it; the reader still falls back to this for a scene that has not
		/// been baked since the move. Remove once every world scene has been baked.
		/// </remarks>
		[Tooltip("Deprecated. Migrated into the Map Definition by FishMMO/World Map/Bake Maps.")]
		public Sprite SceneTransitionImage;
	}
}
