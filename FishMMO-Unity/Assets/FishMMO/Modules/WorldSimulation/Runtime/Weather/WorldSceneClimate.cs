using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's own climate and biome map, for a scene that is not in the world atlas or wants to differ from its entry
	/// (a test bed). Sits beside the scene's <see cref="WorldSceneSettings"/>; the atlas entry still takes precedence.
	/// </summary>
	/// <remarks>
	/// The climate's extension block of the core's scene settings. These
	/// were fields of <see cref="WorldSceneSettings"/>, empty in every world scene; a scene without this component reads
	/// its climate from its atlas entry and its body, exactly as before. Read through <see cref="WorldSceneClimates"/>.
	/// </remarks>
	[DisallowMultipleComponent]
	public class WorldSceneClimate : MonoBehaviour
	{
		/// <summary>
		/// The climate model this scene runs under: lapse rates, humidity curve, tier boundaries and
		/// the base global offsets. Shared between scenes that share a climate; null falls back to
		/// the atlas entry's, then the body's base climate, then the built-in defaults.
		/// </summary>
		[Tooltip("Climate model for this scene. Shared between scenes with the same climate. The world atlas entry, then the body's base climate, take precedence.")]
		public ClimateSettings Climate;

		/// <summary>
		/// Which biome lies where, baked when the world was generated. Biomes are mixed through a
		/// scene; the map is how anything asks what is under a position.
		/// </summary>
		[Tooltip("Baked biome grid for this scene, imported from the world generator's biome map. The world atlas entry may override it.")]
		public SceneBiomeMap BiomeMap;
	}
}
