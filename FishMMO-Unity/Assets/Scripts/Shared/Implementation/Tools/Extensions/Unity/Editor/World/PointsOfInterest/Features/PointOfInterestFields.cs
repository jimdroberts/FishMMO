#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The farmland round a settlement (2026-10-11): fields of wheat, ploughed plots and fenced pasture on the gentle,
	/// dry ground outside it, clear of its ways, the water and every other site.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Wheat is grown, not placed.</b> A field's ground has its other details cleared and the wheat detail
	/// (Detail_GrassWheat, the grass blades' hand-masked crop) painted in, edge to edge but a headland at its rim, so the
	/// procedural grass draws the crop with the same wind, light and season as the meadow beside it. A ploughed plot is
	/// cleared and laid as bare yard earth (the path overlay's tilled soil); pasture keeps its grass and is only fenced.
	/// </para>
	/// <para>
	/// <b>Fenced</b> with the kit's field fence, post and rails, a gap left in the side facing the settlement for the gate.
	/// An abandoned farm's fields are fallow (their grass let back in) and its fences broken.
	/// </para>
	/// <para>Seeded by the site: a re-cut lays the same fields.</para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestFields : IPointOfInterestSiteBuilder
	{
		static PointOfInterestFields() => PointOfInterestSiteBuilders.Register(new PointOfInterestFields());

		/// <summary>The steepest ground a field is ploughed on, degrees.</summary>
		public const float MaxSlopeDegrees = 7f;
		/// <summary>The wheat's coverage in a field (0..255, the terrain's coverage mode).</summary>
		private const int WheatCoverage = 235;
		/// <summary>The unsown rim of a crop field, metres.</summary>
		private const float Headland = 1.5f;
		private const string WheatPrefab = "Detail_GrassWheat";

		private enum FieldKind { Wheat, Ploughed, Pasture, Fallow }

		private struct Field
		{
			public Vector2 Centre;
			public float Yaw;
			public Vector2 Size;
			public FieldKind Kind;
		}

		public bool Handles(PointOfInterestRecord record)
		{
			switch (record.Kind)
			{
				case POIType.Village:
				case POIType.Town:
				case POIType.City:
				case POIType.Capital:
				case POIType.Monastery:
				case POIType.AbandonedFarm:
				case POIType.Windmill:
					return true;
				default:
					return false;
			}
		}

		private static (int min, int max) CountFor(POIType kind)
		{
			switch (kind)
			{
				case POIType.Town: return (5, 9);
				case POIType.City: return (8, 12);
				case POIType.Capital: return (10, 14);
				case POIType.Monastery: return (2, 4);
				case POIType.AbandonedFarm: return (2, 3);
				// The fields a post mill grinds for, round its mound.
				case POIType.Windmill: return (3, 5);
				default: return (3, 6);
			}
		}

		public void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestRecord record = context.Record;
			DeterministicRNG random = context.Random;
			(int min, int max) = CountFor(record.Kind);
			int wanted = min + (int)(random.NextDouble() * (max - min + 1));
			bool abandoned = record.Kind == POIType.AbandonedFarm;

			var ways = context.Points != null && context.Points.Paths != null ? new PathExclusion(context.Points.Paths) : null;
			var site = new Vector2(record.Position.x, record.Position.z);
			var fields = new List<Field>();
			for (int attempt = 0; attempt < wanted * 8 && fields.Count < wanted; attempt++)
			{
				float angle = (float)(random.NextDouble() * Math.PI * 2.0);
				float distance = record.Radius * 1.15f + 12f + (float)random.NextDouble() * Mathf.Max(30f, record.Radius * 1.6f);
				var field = new Field
				{
					Centre = site + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * distance,
					Yaw = record.Yaw + (random.NextDouble() < 0.5 ? 0f : 90f) + (float)(random.NextDouble() - 0.5) * 16f,
					Size = new Vector2(Mathf.Lerp(28f, 72f, (float)random.NextDouble()), Mathf.Lerp(18f, 44f, (float)random.NextDouble())),
				};
				double roll = random.NextDouble();
				field.Kind = abandoned ? FieldKind.Fallow : roll < 0.55 ? FieldKind.Wheat : roll < 0.75 ? FieldKind.Ploughed : FieldKind.Pasture;
				if (Fits(context, field, fields, ways))
				{
					fields.Add(field);
				}
			}
			if (fields.Count == 0)
			{
				return;
			}

			List<Terrain> terrains = TerrainsOf(context);
			int crops = 0;
			foreach (Field field in fields)
			{
				if (field.Kind == FieldKind.Wheat || field.Kind == FieldKind.Ploughed)
				{
					PaintDetails(terrains, field, field.Kind == FieldKind.Wheat);
					crops++;
				}
				if (field.Kind == FieldKind.Ploughed)
				{
					AddPloughedGround(context, field);
				}
				Fence(context, field, site, abandoned);
			}
			context.Notes?.Add($"{record.Name}: {fields.Count} field(s) laid round it ({crops} sown or ploughed).");
		}

		// ── Siting ────────────────────────────────────────────────────

		/// <summary>The field's corners, edge middles and centre, world xz.</summary>
		private static IEnumerable<Vector2> Samples(Field field)
		{
			float r = field.Yaw * Mathf.Deg2Rad;
			var x = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var z = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			for (int i = -1; i <= 1; i++)
			{
				for (int j = -1; j <= 1; j++)
				{
					yield return field.Centre + x * (i * field.Size.x * 0.5f) + z * (j * field.Size.y * 0.5f);
				}
			}
		}

		private static bool Fits(PointOfInterestSiteContext context, Field field, List<Field> placed, PathExclusion ways)
		{
			foreach (Field other in placed)
			{
				if (PointOfInterestLayouts.Overlaps(field.Centre, field.Yaw, field.Size + Vector2.one * 6f, other.Centre, other.Yaw, other.Size + Vector2.one * 6f))
				{
					return false;
				}
			}
			foreach (Vector2 p in Samples(field))
			{
				float ground = context.GroundAt(p.x, p.y);
				if (float.IsNaN(ground) || float.IsInfinity(ground) || (context.SkyAt != null && context.SkyAt(p.x, p.y)))
				{
					return false;
				}
				if (context.SlopeAt != null && context.SlopeAt(p.x, p.y) > MaxSlopeDegrees)
				{
					return false;
				}
				if (context.Water != null && context.Water.KindAt(p.x, p.y) != WaterKind.None)
				{
					return false;
				}
				if (context.Points != null && context.Points.IsKeptOut(p.x, p.y))
				{
					return false;
				}
				if (ways != null && ways.Trees(p.x, p.y))
				{
					return false;
				}
			}
			return true;
		}

		// ── The ground ────────────────────────────────────────────────

		private static List<Terrain> TerrainsOf(PointOfInterestSiteContext context)
		{
			var terrains = new List<Terrain>();
			if (context.Scene.IsValid())
			{
				foreach (GameObject root in context.Scene.GetRootGameObjects())
				{
					terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
				}
			}
			return terrains;
		}

		/// <summary>
		/// Clears every detail inside the field and, for a crop, paints the wheat in (a headland left round it), on every
		/// tile the field crosses.
		/// </summary>
		private static void PaintDetails(List<Terrain> terrains, Field field, bool wheat)
		{
			float r = field.Yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			float halfX = field.Size.x * 0.5f, halfZ = field.Size.y * 0.5f;
			float reach = new Vector2(halfX, halfZ).magnitude;
			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null || data.detailResolution <= 0)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position;
				Vector3 size = data.size;
				int res = data.detailResolution;
				float texel = size.x / res;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((field.Centre.x - reach - origin.x) / texel), 0, res - 1);
				int x1 = Mathf.Clamp(Mathf.CeilToInt((field.Centre.x + reach - origin.x) / texel), 0, res - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((field.Centre.y - reach - origin.z) / texel), 0, res - 1);
				int z1 = Mathf.Clamp(Mathf.CeilToInt((field.Centre.y + reach - origin.z) / texel), 0, res - 1);
				if (x1 <= x0 || z1 <= z0)
				{
					continue;
				}
				int wheatLayer = wheat ? WheatLayer(data) : -1;
				int layers = data.detailPrototypes.Length;
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				for (int layer = 0; layer < layers; layer++)
				{
					int[,] map = data.GetDetailLayer(x0, z0, w, h, layer);
					bool changed = false;
					for (int j = 0; j < h; j++)
					{
						for (int i = 0; i < w; i++)
						{
							var p = new Vector2(origin.x + (x0 + i + 0.5f) * texel, origin.z + (z0 + j + 0.5f) * texel) - field.Centre;
							float u = Vector2.Dot(p, axisX), v = Vector2.Dot(p, axisZ);
							if (Mathf.Abs(u) > halfX || Mathf.Abs(v) > halfZ)
							{
								continue;
							}
							bool sown = layer == wheatLayer && Mathf.Abs(u) < halfX - Headland && Mathf.Abs(v) < halfZ - Headland;
							int value = sown ? WheatCoverage : 0;
							if (map[j, i] != value)
							{
								map[j, i] = value;
								changed = true;
							}
						}
					}
					if (changed)
					{
						data.SetDetailLayer(x0, z0, layer, map);
					}
				}
				EditorUtility.SetDirty(data);
			}
		}

		/// <summary>The wheat's detail layer on a tile, added (copying a grass prototype's settings) when the tile has none.</summary>
		private static int WheatLayer(TerrainData data)
		{
			DetailPrototype[] prototypes = data.detailPrototypes;
			int grass = -1;
			for (int i = 0; i < prototypes.Length; i++)
			{
				GameObject prefab = prototypes[i].prototype;
				if (prefab != null && prefab.name == WheatPrefab)
				{
					return i;
				}
				if (grass < 0 && prefab != null && prefab.name.StartsWith("Detail_Grass", StringComparison.Ordinal))
				{
					grass = i;
				}
			}
			var wheat = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(ProceduralArtCatalogue.DetailPrefab("GrassWheat")));
			if (wheat == null)
			{
				return -1;
			}
			DetailPrototype copy = grass >= 0 ? prototypes[grass] : new DetailPrototype { usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true };
			var added = new DetailPrototype
			{
				prototype = wheat,
				usePrototypeMesh = true,
				useInstancing = copy.useInstancing,
				renderMode = copy.renderMode,
				minWidth = 0.9f,
				maxWidth = 1.1f,
				minHeight = 0.9f,
				maxHeight = 1.2f,
				noiseSpread = copy.noiseSpread,
				healthyColor = Color.white,
				dryColor = Color.white,
				density = copy.density,
				alignToGround = copy.alignToGround,
				positionJitter = copy.positionJitter,
				useDensityScaling = copy.useDensityScaling,
			};
			var grown = new DetailPrototype[prototypes.Length + 1];
			Array.Copy(prototypes, grown, prototypes.Length);
			grown[prototypes.Length] = added;
			data.detailPrototypes = grown;
			data.RefreshPrototypes();
			return prototypes.Length;
		}

		/// <summary>A ploughed plot: bare tilled earth over the whole field, as the path overlay draws worn ground.</summary>
		private static void AddPloughedGround(PointOfInterestSiteContext context, Field field)
		{
			if (context.Points == null)
			{
				return;
			}
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			float r = field.Yaw * Mathf.Deg2Rad;
			bool alongX = field.Size.x >= field.Size.y;
			Vector2 axis = alongX ? new Vector2(Mathf.Cos(r), -Mathf.Sin(r)) : new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			float shortest = Mathf.Min(field.Size.x, field.Size.y), longest = Mathf.Max(field.Size.x, field.Size.y);
			// Squared, end midpoint to end midpoint, half a metre inside the fence all round.
			Vector2 a = field.Centre - axis * (longest * 0.5f - 0.5f), b = field.Centre + axis * (longest * 0.5f - 0.5f);
			float half = shortest * 0.5f - 0.5f;
			context.Points.Paths.Add(new ScenePath
			{
				Id = id + 1,
				Class = ScenePathClass.Yard,
				Points = new[] { new Vector3(a.x, context.GroundAt(a.x, a.y), a.y), new Vector3(b.x, context.GroundAt(b.x, b.y), b.y) },
				HalfWidth = new[] { half, half },
				Wear = new[] { 0.9f, 0.9f },
				Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
				// The plot is ploughed to its fence's corners, not rounded off inside them.
				Flags = new[] { (byte)(ScenePathPointFlags.InSite | ScenePathPointFlags.Squared), (byte)(ScenePathPointFlags.InSite | ScenePathPointFlags.Squared) },
			});
		}

		/// <summary>The fence round a field: module by module along each side, a gap in the side nearest the settlement.</summary>
		private static void Fence(PointOfInterestSiteContext context, Field field, Vector2 site, bool broken)
		{
			GameObject[] prefabs =
			{
				PointOfInterestPieceResolver.Resolve(new PointOfInterestPieceRequest("=FieldFence", "timber", 0)),
				PointOfInterestPieceResolver.Resolve(new PointOfInterestPieceRequest("=FieldFence", "timber", 1)),
			};
			if (prefabs[0] == null && prefabs[1] == null)
			{
				return;
			}
			const float module = 3f;
			float r = field.Yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			Vector2 toSite = (site - field.Centre).normalized;
			// Each side: its middle, its direction along, its length, and its outward normal.
			var sides = new[]
			{
				(field.Centre + axisZ * (field.Size.y * 0.5f), axisX, field.Size.x, axisZ),
				(field.Centre - axisZ * (field.Size.y * 0.5f), axisX, field.Size.x, -axisZ),
				(field.Centre + axisX * (field.Size.x * 0.5f), axisZ, field.Size.y, axisX),
				(field.Centre - axisX * (field.Size.x * 0.5f), axisZ, field.Size.y, -axisX),
			};
			int gateSide = 0;
			float best = float.NegativeInfinity;
			for (int s = 0; s < 4; s++)
			{
				float facing = Vector2.Dot(sides[s].Item4, toSite);
				if (facing > best)
				{
					best = facing;
					gateSide = s;
				}
			}
			DeterministicRNG random = context.Random;
			for (int s = 0; s < 4; s++)
			{
				(Vector2 middle, Vector2 along, float length, Vector2 _) = sides[s];
				int count = Mathf.Max(1, Mathf.RoundToInt(length / module));
				float step = length / count;
				int gate = s == gateSide ? count / 2 : -1;
				float yaw = Mathf.Atan2(along.y, -along.x) * Mathf.Rad2Deg + 180f;
				for (int k = 0; k < count; k++)
				{
					if (k == gate || broken && random.NextDouble() < 0.35)
					{
						continue;
					}
					Vector2 p = middle + along * ((k + 0.5f) * step - length * 0.5f);
					GameObject prefab = prefabs[random.NextDouble() < 0.5 || prefabs[1] == null ? 0 : 1] ?? prefabs[1];
					float tilt = broken ? (float)(random.NextDouble() - 0.5) * 20f : 0f;
					context.AddProp(prefab, new Vector3(p.x, context.GroundAt(p.x, p.y), p.y), Quaternion.Euler(0f, yaw, tilt), new Vector3(step / module, 1f, 1f));
				}
			}
		}
	}
}
#endif
