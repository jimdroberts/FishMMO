using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The glowing ground's globals (2026-10-25, phase I2r "molten ground", decision 65 (a); FishGroundGlow.hlsl, <see cref="GroundGlow"/>):
	/// whether any biome on the scene has a glowing ground layer, and the world's mean surface temperature the Whole mode reads. The
	/// layers' own glows ride the terrain's layer table (TerrainArraySet.LayerGlow, its glow row); the clock is the shared motion clock
	/// every terrain pixel already has. And the glowing ground's share of the lava's molten mask (<see cref="GroundGlowMask"/>), so the
	/// lava's light and the heat shimmer see it. Client only, presentation only.
	/// </summary>
	/// <remarks>
	/// Built with the flora map (FloraPaletteMap.Build: the same row templates the glow modes and the burn read). <c>_FishGroundGlowInfo.x</c>
	/// is 0 when no biome on the scene names a glowing layer, and the terrain then draws exactly as before; so is the mask.
	/// </remarks>
	public static class GroundGlowTable
	{
		private static readonly int InfoId = Shader.PropertyToID("_FishGroundGlowInfo");

		/// <summary>The world's temperature when no body is known, K.</summary>
		public const float FallbackKelvin = 288f;

		private static Vector4 info;
		private static bool on, published, handedMask;
		private static float worldKelvin = FallbackKelvin;
		private static readonly List<Terrain> terrains = new List<Terrain>();
		private static readonly Dictionary<TerrainArraySet, float[]> shares = new Dictionary<TerrainArraySet, float[]>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			info = Vector4.zero;
			on = published = handedMask = false;
			worldKelvin = FallbackKelvin;
			terrains.Clear();
			shares.Clear();
		}

		/// <summary>Whether a biome on the scene has a glowing ground layer (the table is on).</summary>
		public static bool On => on;

		/// <summary>The world's mean surface temperature the table was built for, K.</summary>
		public static float WorldKelvin => worldKelvin;

		/// <summary>True when a template has a main or detail ground layer that glows (<see cref="TerrainTextureLayer.glow"/>).</summary>
		public static bool Glows(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return false;
			}
			if (biome.MainTextureLayer != null && !biome.MainTextureLayer.glow.IsNone)
			{
				return true;
			}
			if (biome.DetailTextureLayers != null)
			{
				foreach (TerrainTextureLayer layer in biome.DetailTextureLayers)
				{
					if (layer != null && !layer.glow.IsNone)
					{
						return true;
					}
				}
			}
			/* And a climate variant's ground override (2026-10-26, GROUND-BANDS; I2r LEFT (a)): the bake already reads an override slot's
			 * glow (TerrainArrayBaker.GlowOf resolves variant slots), so a glowing override must switch the table on too. */
			if (biome.ClimateVariants != null)
			{
				foreach (BiomeClimateVariant variant in biome.ClimateVariants)
				{
					if (variant?.TextureLayerOverrides == null)
					{
						continue;
					}
					foreach (TerrainTextureLayer layer in variant.TextureLayerOverrides)
					{
						if (layer != null && !layer.glow.IsNone)
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		/// <summary>The table from the flora map's row templates (null for none), on the scene's world.</summary>
		public static void Build(IReadOnlyList<BiomeTemplate> rowBiomes, SolarSystemProfile system, WorldBody body)
		{
			on = false;
			int count = rowBiomes != null ? rowBiomes.Count : 0;
			for (int r = 0; r < count && !on; r++)
			{
				on = Glows(rowBiomes[r]);
			}
			worldKelvin = system != null && body != null ? (float)ClimateModel.MeanSurfaceKelvin(system, body) : FallbackKelvin;
			if (float.IsNaN(worldKelvin) || float.IsInfinity(worldKelvin))
			{
				worldKelvin = FallbackKelvin;
			}
			info = on ? new Vector4(1f, worldKelvin, 0f, 0f) : Vector4.zero;
			shares.Clear();
			terrains.Clear();
		}

		/// <summary>Hands the globals to the shaders and the glowing ground to the lava's mask (off unless built with a glowing layer and <paramref name="enabled"/>).</summary>
		public static void Publish(bool enabled)
		{
			published = enabled && on;
			Shader.SetGlobalVector(InfoId, published ? info : Vector4.zero);
			if (published)
			{
				GroundGlowMask.Set(ShareAt);
				handedMask = true;
			}
			else if (handedMask)
			{
				GroundGlowMask.Set(null);
				handedMask = false;
			}
		}

		/// <summary>Takes the table down.</summary>
		public static void Release()
		{
			on = published = false;
			info = Vector4.zero;
			shares.Clear();
			Shader.SetGlobalVector(InfoId, Vector4.zero);
			if (handedMask)
			{
				GroundGlowMask.Set(null);
				handedMask = false;
			}
		}

		/// <summary>
		/// The glowing ground's mean share of the molten mask at world (x, z): the stored index map's four layers under it, each its
		/// glow's <see cref="GroundGlow.MaskShare"/> by its weight. 0 off every terrain, or on a tile with no stored index map (the
		/// editor's live-baked tiles: their weights are on the GPU only).
		/// </summary>
		public static float ShareAt(float x, float z)
		{
			if (!published)
			{
				return 0f;
			}
			if (terrains.Count == 0 || terrains[0] == null)
			{
				terrains.Clear();
				Terrain.GetActiveTerrains(terrains);
			}
			foreach (Terrain terrain in terrains)
			{
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 o = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (x < o.x || z < o.z || x > o.x + size.x || z > o.z + size.z)
				{
					continue;
				}
				TerrainArraySet set = TerrainArrayBinder.SetFor(terrain);
				float[] layerShares = SharesOf(set);
				TerrainDetailBake.IndexMap index = SceneTerrainBake.IndexOf(terrain.terrainData);
				if (layerShares == null || index == null || index.Resolution < 2 || index.Map == null || index.Map.Length < 2 * index.Resolution * index.Resolution)
				{
					return 0f;
				}
				int res = index.Resolution;
				int tx = Mathf.Clamp(Mathf.RoundToInt((x - o.x) / Mathf.Max(1e-3f, size.x) * (res - 1)), 0, res - 1);
				int tz = Mathf.Clamp(Mathf.RoundToInt((z - o.z) / Mathf.Max(1e-3f, size.z) * (res - 1)), 0, res - 1);
				return ShareOfTexel(index.Map[2 * (tz * res + tx)], index.Map[2 * (tz * res + tx) + 1], layerShares);
			}
			return 0f;
		}

		/// <summary>One index texel's share (ids a byte each in <paramref name="ids"/>, weights a byte each in <paramref name="weights"/>).</summary>
		public static float ShareOfTexel(uint ids, uint weights, float[] layerShares)
		{
			float sum = 0f, total = 0f;
			for (int k = 0; k < 4; k++)
			{
				int id = (int)((ids >> (8 * k)) & 255u);
				float w = ((weights >> (8 * k)) & 255u) / 255f;
				total += w;
				if (layerShares != null && id < layerShares.Length)
				{
					sum += w * layerShares[id];
				}
			}
			return total > 1e-4f ? Mathf.Clamp01(sum / total) : 0f;
		}

		/// <summary>Each layer's mask share in a set (null when no layer of it glows), worked out once a set.</summary>
		private static float[] SharesOf(TerrainArraySet set)
		{
			if (set == null || set.LayerGlow == null || set.LayerGlow.Length == 0)
			{
				return null;
			}
			if (shares.TryGetValue(set, out float[] cached))
			{
				return cached;
			}
			var values = new float[set.LayerGlow.Length];
			bool any = false;
			for (int i = 0; i < values.Length; i++)
			{
				values[i] = GroundGlow.MaskShare(set.LayerGlow[i], worldKelvin);
				any |= values[i] > 0f;
			}
			float[] result = any ? values : null;
			shares[set] = result;
			return result;
		}
	}
}
