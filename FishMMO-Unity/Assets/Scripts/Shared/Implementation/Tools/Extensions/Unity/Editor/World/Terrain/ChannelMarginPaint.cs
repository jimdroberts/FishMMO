#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The wet margin along a scene's water channels (2026-10-18, phase I2j-b, decision 38): each biome's own detail ground layer
	/// (<see cref="BiomeTemplate.ChannelMargin"/>, a roggia's gley silt and cress) painted in a strip either side of every channel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Stored, then painted on the next repaint.</b> Channels are carried by the sites' linear features in the place stage, after
	/// the ground is painted, so a cut cannot paint along the channels it is about to lay. Their lines are stored with the scene
	/// (<see cref="GeneratedChannelLines"/>, written afresh by every placing, <see cref="Record"/>), and the splat stage reads them
	/// (<see cref="Paint"/>, right after the biome splat, before the ground's arrays and the scatter read the alphamaps): a repaint
	/// paints the margins of the channels the scene holds. The channels are deterministic, so the placing that follows lays the same
	/// ones again.
	/// </para>
	/// <para>
	/// <b>Nothing else changes.</b> A scene whose channels' biomes name no margin, or with no channels (every scene today), reads
	/// nothing and paints nothing; a biome whose margin layer is not in the scene's palette is noted once.
	/// </para>
	/// </remarks>
	public static class ChannelMarginPaint
	{
		/// <summary>The bucket a channel's segments are found by, metres.</summary>
		private const float BucketMetres = 8f;

		// ── The record ────────────────────────────────────────────────

		/// <summary>Drops every stored channel line in a scene (a placing about to lay its own).</summary>
		public static void Clear(Scene scene)
		{
			foreach (GeneratedChannelLines record in Records(scene))
			{
				UnityEngine.Object.DestroyImmediate(record.gameObject);
			}
		}

		/// <summary>The stored channel lines of a scene, every record's in order.</summary>
		public static List<GeneratedChannelLines.Line> Lines(Scene scene)
		{
			var lines = new List<GeneratedChannelLines.Line>();
			foreach (GeneratedChannelLines record in Records(scene))
			{
				lines.AddRange(record.Lines);
			}
			return lines;
		}

		private static List<GeneratedChannelLines> Records(Scene scene)
		{
			var records = new List<GeneratedChannelLines>();
			if (scene.IsValid())
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					records.AddRange(root.GetComponentsInChildren<GeneratedChannelLines>(true));
				}
			}
			return records;
		}

		/// <summary>
		/// Stores one channel's carried stretches (pure but for the record): each run of at least two of its stations that are cut and
		/// flooded (flumes and culverts break it), with its half width and its biome's id. Kept beside the scene's field carve record.
		/// </summary>
		public static void Record(PointOfInterestSiteContext context, int biomeID, float half, List<(Vector2 At, bool Cut)> stations)
		{
			List<List<Vector2>> runs = Runs(stations);
			if (runs.Count == 0 || biomeID == 0)
			{
				return;
			}
			Transform host = context?.Root != null ? context.Root.parent != null ? context.Root.parent : context.Root : null;
			if (host == null)
			{
				return;
			}
			GeneratedChannelLines record = null;
			Transform existing = host.Find(GeneratedChannelLines.ObjectName);
			if (existing == null || !existing.TryGetComponent(out record))
			{
				var child = new GameObject(GeneratedChannelLines.ObjectName) { tag = "EditorOnly" };
				child.transform.SetParent(host, false);
				record = child.AddComponent<GeneratedChannelLines>();
			}
			foreach (List<Vector2> run in runs)
			{
				record.Lines.Add(new GeneratedChannelLines.Line { BiomeID = biomeID, HalfWidth = half, Points = run });
			}
			EditorUtility.SetDirty(record);
		}

		/// <summary>The runs of a channel's cut stations, at least two long (pure).</summary>
		public static List<List<Vector2>> Runs(List<(Vector2 At, bool Cut)> stations)
		{
			var runs = new List<List<Vector2>>();
			var run = new List<Vector2>();
			void Flush()
			{
				if (run.Count >= 2)
				{
					runs.Add(run);
				}
				run = new List<Vector2>();
			}
			foreach ((Vector2 at, bool cut) in stations ?? new List<(Vector2, bool)>())
			{
				if (cut)
				{
					run.Add(at);
				}
				else
				{
					Flush();
				}
			}
			Flush();
			return runs;
		}

		// ── The paint ─────────────────────────────────────────────────

		/// <summary>
		/// A margin's share at a texel (pure): the most any of <paramref name="lines"/> (each with its half width) gives a point
		/// (<see cref="BiomeChannelMargin.Weight"/>), found through <paramref name="buckets"/>.
		/// </summary>
		public static float Share(Vector2 p, List<(List<Vector2> Points, float Half)> lines, Dictionary<long, List<(int Line, int Segment)>> buckets, float width)
		{
			if (!buckets.TryGetValue(Bucket(p), out List<(int Line, int Segment)> near))
			{
				return 0f;
			}
			float best = 0f;
			foreach ((int l, int s) in near)
			{
				List<Vector2> points = lines[l].Points;
				float d = PointOfInterestLinearFeatures.HedgeDistance(p, points[s], points[s + 1], out _);
				best = Mathf.Max(best, BiomeChannelMargin.Weight(d, lines[l].Half, width));
				if (best >= 1f)
				{
					break;
				}
			}
			return best;
		}

		/// <summary>Every segment of the lines filed under each bucket it comes within <paramref name="reach"/> of (pure).</summary>
		public static Dictionary<long, List<(int Line, int Segment)>> Buckets(List<(List<Vector2> Points, float Half)> lines, float reach)
		{
			var buckets = new Dictionary<long, List<(int, int)>>();
			for (int l = 0; l < lines.Count; l++)
			{
				List<Vector2> points = lines[l].Points;
				float grow = reach + lines[l].Half;
				for (int s = 0; s + 1 < points.Count; s++)
				{
					Vector2 a = points[s], b = points[s + 1];
					int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - grow) / BucketMetres), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + grow) / BucketMetres);
					int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - grow) / BucketMetres), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + grow) / BucketMetres);
					for (int z = z0; z <= z1; z++)
					{
						for (int x = x0; x <= x1; x++)
						{
							long key = Key(x, z);
							if (!buckets.TryGetValue(key, out List<(int, int)> list))
							{
								list = new List<(int, int)>();
								buckets[key] = list;
							}
							list.Add((l, s));
						}
					}
				}
			}
			return buckets;
		}

		private static long Bucket(Vector2 p) => Key(Mathf.FloorToInt(p.x / BucketMetres), Mathf.FloorToInt(p.y / BucketMetres));

		private static long Key(int x, int z) => ((long)z << 32) ^ (uint)x;

		/// <summary>
		/// Takes a margin's share <paramref name="share"/> into a texel's layer weights (pure): the layer's weight rises to its share of
		/// what the others leave, every other layer gives up the same part of itself, so the weights still sum as they did.
		/// </summary>
		public static void Blend(float[] weights, int layer, float share)
		{
			if (share <= 0f || layer < 0 || layer >= weights.Length)
			{
				return;
			}
			share = Mathf.Clamp01(share);
			for (int i = 0; i < weights.Length; i++)
			{
				weights[i] = i == layer ? weights[i] + (1f - weights[i]) * share : weights[i] * (1f - share);
			}
		}

		/// <summary>
		/// Paints the stored channels' margins into the tiles' alphamaps (see the class remarks): each channel in its biome's margin
		/// layer, as the palette places it. Returns the texels changed; 0, reading nothing more, where the scene stores no channel.
		/// </summary>
		public static int Paint(Scene scene, IReadOnlyList<Terrain> tiles, SceneTerrainPalette palette, List<string> notes)
		{
			List<GeneratedChannelLines.Line> stored = Lines(scene);
			if (stored.Count == 0 || palette == null || tiles == null)
			{
				return 0;
			}
			// The lines by the palette layer and width their biome paints along them.
			var groups = new Dictionary<(int Layer, float Width), List<(List<Vector2> Points, float Half)>>();
			var missing = new HashSet<string>();
			foreach (GeneratedChannelLines.Line line in stored)
			{
				BiomeTemplate biome = BiomeTemplateArt.TemplateById(line.BiomeID);
				BiomeChannelMargin margin = BiomeTemplateArt.ChannelMargin(biome);
				if (biome == null || !margin.Defined || line.Points == null || line.Points.Count < 2)
				{
					continue;
				}
				int layer = LayerOf(palette, biome, margin.DetailLayer);
				if (layer < 0)
				{
					missing.Add($"{biome.ResolvedDisplayName} (detail layer {margin.DetailLayer})");
					continue;
				}
				if (!groups.TryGetValue((layer, margin.Width), out var list))
				{
					list = new List<(List<Vector2>, float)>();
					groups[(layer, margin.Width)] = list;
				}
				list.Add((line.Points, line.HalfWidth));
			}
			if (missing.Count > 0)
			{
				notes?.Add($"Channel margins: not in this scene's palette, so not painted: {string.Join(", ", missing)}.");
			}
			int changed = 0;
			foreach (KeyValuePair<(int Layer, float Width), List<(List<Vector2> Points, float Half)>> group in groups)
			{
				changed += PaintGroup(tiles, group.Key.Layer, group.Key.Width, group.Value);
			}
			if (changed > 0)
			{
				notes?.Add($"Channel margins: {changed:N0} ground texel(s) painted along {stored.Count} stored channel stretch(es).");
			}
			return changed;
		}

		/// <summary>The palette layer of a biome's detail ground layer (its own, not a climate variant's), −1 where it is not in the palette.</summary>
		public static int LayerOf(SceneTerrainPalette palette, BiomeTemplate biome, int detailLayer)
		{
			foreach (SceneTerrainPalette.Entry entry in palette.Entries)
			{
				if (entry.Biome == biome && entry.Role == PaletteRole.Detail && entry.Variant == null && entry.Ground == detailLayer + 1)
				{
					return entry.LayerIndex;
				}
			}
			return -1;
		}

		private static int PaintGroup(IReadOnlyList<Terrain> tiles, int layer, float width, List<(List<Vector2> Points, float Half)> lines)
		{
			Dictionary<long, List<(int Line, int Segment)>> buckets = Buckets(lines, width);
			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			foreach ((List<Vector2> points, float half) in lines)
			{
				foreach (Vector2 p in points)
				{
					minX = Mathf.Min(minX, p.x - half - width);
					maxX = Mathf.Max(maxX, p.x + half + width);
					minZ = Mathf.Min(minZ, p.y - half - width);
					maxZ = Mathf.Max(maxZ, p.y + half + width);
				}
			}
			int changed = 0;
			foreach (Terrain terrain in tiles)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null || layer >= data.alphamapLayers)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position, size = data.size;
				if (maxX < origin.x || minX > origin.x + size.x || maxZ < origin.z || minZ > origin.z + size.z)
				{
					continue;
				}
				int resX = data.alphamapWidth, resZ = data.alphamapHeight;
				float texelX = size.x / resX, texelZ = size.z / resZ;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - origin.x) / texelX), 0, resX - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - origin.x) / texelX), 0, resX - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - origin.z) / texelZ), 0, resZ - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - origin.z) / texelZ), 0, resZ - 1);
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				float[,,] maps = data.GetAlphamaps(x0, z0, w, h);
				int layers = maps.GetLength(2);
				var weights = new float[layers];
				bool dirty = false;
				for (int j = 0; j < h; j++)
				{
					for (int i = 0; i < w; i++)
					{
						var p = new Vector2(origin.x + (x0 + i + 0.5f) * texelX, origin.z + (z0 + j + 0.5f) * texelZ);
						float share = Share(p, lines, buckets, width);
						if (share <= 0f)
						{
							continue;
						}
						for (int k = 0; k < layers; k++)
						{
							weights[k] = maps[j, i, k];
						}
						Blend(weights, layer, share);
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
