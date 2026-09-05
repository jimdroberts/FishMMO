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

		public bool HasBiome => Biome != null;
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

			if (TrySampleTerrainHeight(worldPosition, out float height))
			{
				reading.Height = height;
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
				reading.Biome = BiomeResolver.Select(reading.Height, reading.Climate);
			}
			if (reading.Biome != null)
			{
				reading.Variant = settings != null
					? settings.ResolveVariant(reading.Biome, reading.Climate)
					: reading.Biome.ResolveOwnVariant(reading.Climate.Temperature, reading.Climate.Humidity);
			}
			return reading;
		}

		/// <summary>Normalised height of the terrain under a position, from whichever active terrain contains it.</summary>
		public static bool TrySampleTerrainHeight(Vector3 worldPosition, out float normalizedHeight)
		{
			normalizedHeight = 0f;
			Terrain[] terrains = Terrain.activeTerrains;
			if (terrains == null)
			{
				return false;
			}
			for (int i = 0; i < terrains.Length; i++)
			{
				Terrain terrain = terrains[i];
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (worldPosition.x < origin.x || worldPosition.x > origin.x + size.x
					|| worldPosition.z < origin.z || worldPosition.z > origin.z + size.z)
				{
					continue;
				}
				float worldY = terrain.SampleHeight(worldPosition) + origin.y;
				normalizedHeight = size.y > 0f ? Mathf.Clamp01((worldY - origin.y) / size.y) : 0f;
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
