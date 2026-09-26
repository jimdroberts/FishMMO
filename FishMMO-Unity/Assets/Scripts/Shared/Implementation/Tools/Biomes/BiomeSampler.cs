using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Biomes
{
	/// <summary>What the world is at a position: its biome, the climate reading, and the variant the biome shows under it.</summary>
	public struct BiomeReading
	{
		public BiomeTemplate Biome;
		public BiomeClimateVariant Variant;
		public ClimateSample Climate;
		/// <summary>Normalised terrain height the reading was taken at, when a terrain was found.</summary>
		public float Height;
		/// <summary>True when the biome came from the scene's baked map rather than being chosen from height and climate.</summary>
		public bool FromMap;
		/// <summary>True when a terrain was under the position, so <see cref="Height"/> is measured rather than assumed.</summary>
		public bool HeightKnown;

		public bool HasBiome => Biome != null;

		/// <summary>
		/// True when the biome rests on something real: the baked map, or a measured terrain
		/// height. Otherwise it was picked from a height of 0 — the sea floor — by default.
		/// </summary>
		public bool IsGrounded => FromMap || HeightKnown;
	}

	/// <summary>
	/// Answers "what biome is here?" for anything in a scene — namers, spawners, later systems.
	///
	/// <para>The biome comes from the scene's baked <see cref="SceneBiomeMap"/>, which is what
	/// generation fixed; a position the map does not cover falls back to choosing a biome from
	/// the terrain height and the current climate. The climate variant always comes from the
	/// current climate on <see cref="WorldSceneSettings"/>, so a frozen winter reads as such
	/// without the biome itself changing.</para>
	/// </summary>
	public static class BiomeSampler
	{
		/// <summary>Reads the biome at a world position in the given scene.</summary>
		public static BiomeReading Read(Vector3 worldPosition, Scene scene)
		{
			WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings settings);
			return Read(worldPosition, settings);
		}

		/// <summary>Reads the biome at a world position under the given scene settings (null = no map, default climate).</summary>
		public static BiomeReading Read(Vector3 worldPosition, WorldSceneSettings settings)
		{
			var reading = new BiomeReading();

			SceneBiomeMap map = settings != null ? settings.BiomeMap : null;
			float latitude = map != null ? map.Latitude01(worldPosition) : 0.5f;

			/* Measured within this scene's own terrains. Scene servers load world scenes
			 * additively and every scene is built around its own origin, so their terrains overlap
			 * in world space; an unscoped search answers with whichever tile the global array lists
			 * first, which may belong to a different zone entirely. The component resolves its
			 * scene once (WorldSceneSettings.OwnScene) rather than this asking the engine on every
			 * sample. */
			Scene scene = settings != null ? settings.OwnScene : default;
			if (TrySampleTerrainHeight(worldPosition, scene, out float height))
			{
				reading.Height = height;
				reading.HeightKnown = true;
			}
			else if (map != null && map.Contains(worldPosition))
			{
				// No terrain under the point: assume the map's biome sits at its band's centre.
				reading.Height = 0.5f;
			}

			reading.Climate = settings != null
				? settings.SampleClimate(reading.Height, latitude)
				: DefaultClimate(reading.Height, latitude);

			if (map != null)
			{
				reading.Biome = map.Sample(worldPosition);
				reading.FromMap = reading.Biome != null;
			}
			if (reading.Biome == null)
			{
				/* Filtered by the world this scene is on before the climate is scored. A painted
				 * biome map is left alone — if a designer has put a jungle on an airless moon they
				 * meant it, and second-guessing a hand-painted map would be worse than the mistake. */
				reading.Biome = settings != null
					? BiomeResolver.Select(reading.Height, reading.Climate, settings.WorldConditions)
					: BiomeResolver.Select(reading.Height, reading.Climate);
			}
			if (reading.Biome != null)
			{
				reading.Variant = settings != null
					? settings.ResolveVariant(reading.Biome, reading.Climate)
					: reading.Biome.ResolveOwnVariant(reading.Climate.Temperature, reading.Climate.Humidity);
			}
			return reading;
		}

		/// <summary>Normalised height of the terrain under a position, across every loaded terrain.</summary>
		/// <remarks>Prefer the overload that names a scene; this one cannot tell two scenes' terrains apart.</remarks>
		public static bool TrySampleTerrainHeight(Vector3 worldPosition, out float normalizedHeight)
		{
			return TrySampleTerrainHeight(worldPosition, default, out normalizedHeight);
		}

		/// <summary>
		/// Normalised height of the terrain under a position, measured against the scene's whole
		/// landmass.
		/// </summary>
		/// <param name="worldPosition">The world position to measure under.</param>
		/// <param name="scene">The scene whose terrains to consider. Invalid measures them all.</param>
		/// <param name="normalizedHeight">0 at the landmass's lowest possible ground, 1 at its highest.</param>
		/// <remarks>
		/// <para>
		/// <b>Against the landmass, not against the tile.</b> A scene may be one terrain or a grid
		/// of them stitched together, and stitched tiles do not have to share a base height or a
		/// height range — a plateau tile is commonly raised, and a tile over flat ground is
		/// commonly given a shorter range so its heightmap keeps its precision. Normalising each
		/// tile against its own range made the same world altitude read as a different height on
		/// either side of a seam, so temperature and biome stepped as you crossed the join.
		/// </para>
		/// <para>
		/// For a scene with one terrain the answer is arithmetically identical to the old one —
		/// the landmass's base and span <em>are</em> that tile's — so nothing moves where the old
		/// code was already right.
		/// </para>
		/// <para>
		/// <b>Walks this scene's tiles, not every loaded terrain.</b> The tiles and their bounds
		/// come from <see cref="SceneTerrainExtent.TilesOf"/>, measured once per change in the
		/// loaded terrains rather than per sample, so a sample costs one engine call — the height
		/// itself — and no allocation, however many other scenes are loaded. This runs per
		/// character per second for weather exposure, on the server and the owning client alike.
		/// </para>
		/// </remarks>
		public static bool TrySampleTerrainHeight(Vector3 worldPosition, Scene scene, out float normalizedHeight)
		{
			normalizedHeight = 0f;
			SceneTerrainExtent.Tile[] tiles = SceneTerrainExtent.TilesOf(scene, out SceneTerrainExtent extent);
			for (int i = 0; i < tiles.Length; i++)
			{
				SceneTerrainExtent.Tile tile = tiles[i];
				if (!tile.Covers(worldPosition))
				{
					continue;
				}
				Terrain terrain = tile.Terrain;
				if (terrain == null)
				{
					// Destroyed since it was measured; the next measurement drops it.
					continue;
				}
				// SampleHeight is relative to the tile's own transform, so the tile's origin puts
				// it back into world space before the landmass normalises it.
				float worldY = terrain.SampleHeight(worldPosition) + tile.Origin.y;
				normalizedHeight = extent.Found ? extent.Normalize(worldY)
					: tile.Size.y > 0f ? Mathf.Clamp01((worldY - tile.Origin.y) / tile.Size.y) : 0f;
				return true;
			}
			return false;
		}

		private static ClimateSample DefaultClimate(float height, float latitude)
		{
			float temperature = Mathf.Clamp(-height * 0.8f, -1f, 1f);
			return new ClimateSample
			{
				Temperature = temperature,
				Humidity = Mathf.Clamp((1f - height) * 0.3f, -1f, 1f),
				ElevationTier = ClimateSettings.TierForHeight(height, null),
			};
		}
	}
}
