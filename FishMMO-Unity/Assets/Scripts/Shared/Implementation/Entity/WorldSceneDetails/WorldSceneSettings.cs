using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;

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
		/// The hard ceiling on clients in any one scene, anywhere in the project.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The single source of truth for the cap. The world server (routing and instance
		/// spin-up) and the scene server (channel admission) both clamp to this, so a scene
		/// authored above it is reduced rather than honoured — a designer cannot raise a scene's
		/// population past what the bandwidth and observer budgets were sized for.
		/// </para>
		/// <para>
		/// 200 is a deliberate ceiling, not a guess: per-client cost is bounded by the observer
		/// visibility budget rather than by scene population, so scaling past this is done by
		/// adding scene instances (and scene servers), never by widening one scene. See
		/// <c>SceneServerPlacementPolicy</c>.
		/// </para>
		/// </remarks>
		public const int MaximumClientsPerScene = 200;

		/// <summary>
		/// The maximum number of clients allowed in this scene.
		/// </summary>
		/// <remarks>
		/// The <see cref="RangeAttribute"/> constrains the inspector; it does not constrain a value
		/// edited into the scene YAML by hand or arriving from an older asset, so every consumer
		/// clamps to <see cref="MaximumClientsPerScene"/> on read as well.
		/// </remarks>
		[Tooltip("The maximum number of clients allowed in this scene. Clamped to 200.")]
		[Range(1, MaximumClientsPerScene)]
		public int MaxClients = MaximumClientsPerScene;

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

		/// <summary>
		/// The climate model this scene runs under: lapse rates, humidity curve, tier boundaries and
		/// the base global offsets. Shared between scenes that share a climate; null falls back to
		/// the built-in defaults.
		/// </summary>
		[Header("Climate")]
		[Tooltip("Climate model for this scene. Shared between scenes with the same climate.")]
		public ClimateSettings Climate;

		/// <summary>
		/// Which biome lies where, baked when the world was generated. Biomes are mixed through a
		/// scene; the map is how anything asks what is under a position.
		/// </summary>
		[Tooltip("Baked biome grid for this scene, imported from the world generator's biome map.")]
		public SceneBiomeMap BiomeMap;

		/// <summary>
		/// Runtime shift on top of <see cref="Climate"/>'s global temperature offset. Mutable: a
		/// weather or season system writes it, and every climate reading in the scene moves with it.
		/// </summary>
		[Tooltip("Runtime temperature shift on top of the climate asset. Driven by weather at runtime.")]
		[Range(-2f, 2f)] public float RuntimeTemperatureOffset;

		/// <summary>
		/// Runtime shift on top of <see cref="Climate"/>'s global humidity offset. Mutable, like
		/// <see cref="RuntimeTemperatureOffset"/>.
		/// </summary>
		[Tooltip("Runtime humidity shift on top of the climate asset. Driven by weather at runtime.")]
		[Range(-2f, 2f)] public float RuntimeHumidityOffset;

		private static readonly Dictionary<int, WorldSceneSettings> byScene = new Dictionary<int, WorldSceneSettings>();

		/// <summary>The settings component of a loaded scene, if it has one. Scenes load additively on a scene server, so this is keyed by scene.</summary>
		public static bool TryGetForScene(Scene scene, out WorldSceneSettings settings)
		{
			return byScene.TryGetValue(scene.handle, out settings) && settings != null;
		}

		private void OnEnable()
		{
			byScene[gameObject.scene.handle] = this;
		}

		private void OnDisable()
		{
			if (byScene.TryGetValue(gameObject.scene.handle, out WorldSceneSettings current) && current == this)
			{
				byScene.Remove(gameObject.scene.handle);
			}
		}

		/// <summary>
		/// The climate at a normalised height and latitude right now: the climate asset's reading
		/// plus this scene's runtime offsets.
		/// </summary>
		public ClimateSample SampleClimate(float height01, float latitude01)
		{
			ClimateSample sample = Climate != null
				? Climate.Evaluate(height01, latitude01)
				: new ClimateSample
				{
					Temperature = Mathf.Clamp(-height01 * 0.8f, -1f, 1f),
					Humidity = Mathf.Clamp((1f - height01) * 0.3f, -1f, 1f),
					ElevationTier = ClimateSettings.TierForHeight(height01, null),
				};
			sample.Temperature = Mathf.Clamp(sample.Temperature + RuntimeTemperatureOffset, -1f, 1f);
			sample.Humidity = Mathf.Clamp(sample.Humidity + RuntimeHumidityOffset, -1f, 1f);
			return sample;
		}

		/// <summary>The climate variant a biome shows under a reading: the biome's own, else the climate asset's defaults.</summary>
		public BiomeClimateVariant ResolveVariant(BiomeTemplate biome, ClimateSample sample)
		{
			if (biome == null)
			{
				return null;
			}
			return Climate != null
				? Climate.ResolveVariant(biome, sample)
				: biome.ResolveOwnVariant(sample.Temperature, sample.Humidity);
		}
	}
}
