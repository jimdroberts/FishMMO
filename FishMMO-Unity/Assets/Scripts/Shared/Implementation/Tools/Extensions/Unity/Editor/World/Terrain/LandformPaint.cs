#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The ground the scene's sculpted landforms paint (2026-10-18, phase I2g): each stored landform whose entry names a ground layer
	/// of its biome (<see cref="BiomeLandform.PaintGround"/>) has that layer blended over the part of it the entry names (a crater's
	/// rays, a cryodome's streaks, a playa's floor, a tell's mound, a dune's sand up to its angle of repose), by the landform's own
	/// mask (<see cref="BiomeLandforms.Shape.Mask"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Right after the biome splat</b> (beside the channel margins), before the ground's arrays and the scatter read the alphamaps,
	/// at the cut and at every repaint, from the sites the cut stored (<see cref="GeneratedLandforms"/>): the same paint both times.
	/// A field landform's mask is weighted by its biome's reach in the field the scene is painted from.
	/// </para>
	/// <para>
	/// <b>Nothing else changes.</b> A scene that stores no landform, or whose landforms paint nothing, reads nothing more and changes
	/// no texel; a layer missing from the scene's palette is noted once.
	/// </para>
	/// </remarks>
	public static class LandformPaint
	{
		/// <summary>The palette layer of a biome's ground slot (0 the main layer, k detail layer k − 1; its own, not a variant's), −1 when not in the palette.</summary>
		public static int LayerOf(SceneTerrainPalette palette, BiomeTemplate biome, int ground)
		{
			if (palette == null || biome == null || ground < 0)
			{
				return -1;
			}
			foreach (SceneTerrainPalette.Entry entry in palette.Entries)
			{
				if (entry.Biome == biome && entry.Variant == null && entry.Ground == ground
					&& (ground == 0 ? entry.Role == PaletteRole.Main : entry.Role == PaletteRole.Detail))
				{
					return entry.LayerIndex;
				}
			}
			return -1;
		}

		/// <summary>Paints every stored landform's layer (see the class remarks). Returns the texels changed.</summary>
		public static int Paint(Scene scene, IReadOnlyList<Terrain> tiles, SceneTerrainPalette palette, SceneBiomeField field, List<string> notes)
		{
			List<BiomeLandformSite> sites = SceneLandforms.Sites(scene);
			if (sites.Count == 0 || palette == null || tiles == null)
			{
				return 0;
			}
			BiomeLandforms.Context context = SceneLandforms.ContextOf(field, SceneLandforms.SeaLevel(scene));
			var work = new List<(BiomeLandforms.Shape Shape, int Layer)>();
			var missing = new HashSet<string>();
			foreach (BiomeLandformSite site in sites)
			{
				if (!site.Form.Paints)
				{
					continue;
				}
				BiomeTemplate biome = BiomeTemplateArt.TemplateById(site.BiomeID);
				int layer = LayerOf(palette, biome, site.Form.PaintGround);
				if (layer < 0)
				{
					missing.Add($"{(biome != null ? biome.ResolvedDisplayName : site.BiomeID.ToString())} {site.Form.Kind} (ground {site.Form.PaintGround})");
					continue;
				}
				work.Add((new BiomeLandforms.Shape(site, context), layer));
			}
			if (missing.Count > 0)
			{
				notes?.Add($"Landform paint: not in this scene's palette, so not painted: {string.Join(", ", missing)}.");
			}
			int changed = 0;
			foreach ((BiomeLandforms.Shape shape, int layer) in work)
			{
				changed += PaintOne(tiles, shape, layer);
			}
			if (changed > 0)
			{
				notes?.Add($"Landform paint: {changed:N0} ground texel(s) painted over {work.Count} landform(s).");
			}
			return changed;
		}

		private static int PaintOne(IReadOnlyList<Terrain> tiles, BiomeLandforms.Shape shape, int layer)
		{
			Vector4 b = shape.Bounds;
			BiomeLandform form = shape.Site.Form;
			int changed = 0;
			foreach (Terrain terrain in tiles)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null || layer >= data.alphamapLayers)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position, size = data.size;
				if (b.z < origin.x || b.x > origin.x + size.x || b.w < origin.z || b.y > origin.z + size.z)
				{
					continue;
				}
				int resX = data.alphamapWidth, resZ = data.alphamapHeight;
				float texelX = size.x / resX, texelZ = size.z / resZ;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((b.x - origin.x) / texelX), 0, resX - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((b.z - origin.x) / texelX), 0, resX - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((b.y - origin.z) / texelZ), 0, resZ - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((b.w - origin.z) / texelZ), 0, resZ - 1);
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				float[,,] maps = data.GetAlphamaps(x0, z0, w, h);
				int layers = maps.GetLength(2);
				var weights = new float[layers];
				bool dirty = false;
				for (int j = 0; j < h; j++)
				{
					float v = (z0 + j + 0.5f) / resZ;
					for (int i = 0; i < w; i++)
					{
						float u = (x0 + i + 0.5f) / resX;
						float x = origin.x + u * size.x, z = origin.z + v * size.z;
						float ground = origin.y + data.GetInterpolatedHeight(u, v);
						float slope = data.GetSteepness(u, v);
						float share = form.PaintStrength * shape.Mask(form.PaintPart, x, z, ground, slope);
						if (share <= 0f)
						{
							continue;
						}
						for (int k = 0; k < layers; k++)
						{
							weights[k] = maps[j, i, k];
						}
						ChannelMarginPaint.Blend(weights, layer, share);
						for (int k = 0; k < layers; k++)
						{
							maps[j, i, k] = weights[k];
						}
						changed++;
						dirty = true;
					}
				}
				if (dirty)
				{
					data.SetAlphamaps(x0, z0, maps);
					EditorUtility.SetDirty(data);
				}
			}
			return changed;
		}
	}
}
#endif
