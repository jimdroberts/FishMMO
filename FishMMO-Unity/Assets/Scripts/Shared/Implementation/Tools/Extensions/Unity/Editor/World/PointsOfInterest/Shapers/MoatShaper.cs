#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Digs a moat round a keep, castle or fortress (2026-10-11: "motes, draw bridges, palisade, portcullis, walkable
	/// Castle walls"): a ring ditch just outside the site's footprint, crossed at each gate by a drawbridge
	/// (PropsFeature.AddDrawbridges, built by PointOfInterestDoors).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A ditch, mostly wet.</b> A ring <see cref="Width"/> across and <see cref="Depth"/> deep with 45° banks (the
	/// NavMesh agent's limit, so it is a barrier to a charge, not to a climb) and a flat bottom, filled with water a little
	/// under its lower bank (<see cref="AddWater"/>) except in a dry climate or on a slope that leaves most of it above
	/// the level. Not every stronghold has one: a fortress most often, a keep least, and none on ground too steep to ring
	/// (a moat on a hillside is a terrace) or where a river already runs round it.
	/// </para>
	/// <para>
	/// <b>Idempotent.</b> The banks' levels are read from the ground a little inside and outside the ring, which the ditch
	/// never touches, and the ground is only ever lowered to the ditch's profile between them: cutting again digs the same
	/// ditch, never a deeper one. The roads keep out of the ring (they would fill it as a causeway): their carve skips it
	/// (<see cref="Contains"/>) and they cross on the drawbridge.
	/// </para>
	/// </remarks>
	public sealed class MoatShaper : IPointOfInterestShaper
	{
		public static readonly MoatShaper Instance = new MoatShaper();

		public const string ShaperName = "Moat";
		private const int Format = 1;
		/// <summary>The berm between the footprint's edge and the ditch's inner bank, metres.</summary>
		public const float Berm = 1.5f;

		public bool Handles(POIType kind) => kind == POIType.Keep || kind == POIType.Castle || kind == POIType.Fortress;

		/// <summary>How likely a stronghold of a kind is to be moated.</summary>
		private static float Chance(POIType kind) => kind == POIType.Fortress ? 0.8f : kind == POIType.Castle ? 0.65f : 0.45f;

		/// <summary>The ditch's width across the top and its depth at a size, metres.</summary>
		public static float Width(int size) => 6.5f + 1.5f * Mathf.Clamp(size, 0, 2);
		public static float Depth(int size) => 2.5f + 0.5f * Mathf.Clamp(size, 0, 2);

		public void Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			DeterministicRNG rng = context.RandomFor(site, 0x40A7);
			if (rng.NextFloat() >= Chance(site.Kind))
			{
				return;
			}
			float inner = site.Radius + Berm, width = Width(site.SizeClass), depth = Depth(site.SizeClass);
			float outer = inner + width;
			Vector3 c = site.Position;
			// The banks' levels round the ring, from ground the ditch never reaches; refused where the ring is too steep or wet.
			const int Spokes = 180;
			var rimIn = new float[Spokes];
			var rimOut = new float[Spokes];
			float lo = float.MaxValue, hi = float.MinValue;
			for (int i = 0; i < Spokes; i++)
			{
				float a = i * Mathf.PI * 2f / Spokes;
				var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
				rimIn[i] = context.GroundAt(c.x + dir.x * (inner - 1f), c.z + dir.y * (inner - 1f));
				rimOut[i] = context.GroundAt(c.x + dir.x * (outer + 2f), c.z + dir.y * (outer + 2f));
				lo = Mathf.Min(lo, Mathf.Min(rimIn[i], rimOut[i]));
				hi = Mathf.Max(hi, Mathf.Max(rimIn[i], rimOut[i]));
				if (context.Water != null && (context.Water.SurfaceAt(c.x + dir.x * (inner + 0.5f * width), c.z + dir.y * (inner + 0.5f * width)) > float.NegativeInfinity))
				{
					context.Notes?.Add($"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({c.x:F0}, {c.z:F0}): no moat, water already runs round it.");
					return;
				}
			}
			if (hi - lo > Mathf.Max(4f, 0.25f * outer) || lo < CaveShaper.SeaLevel + 1f)
			{
				return;
			}
			var area = Rect.MinMaxRect(c.x - outer - 1f, c.z - outer - 1f, c.x + outer + 1f, c.z + outer + 1f);
			PointOfInterestTerrain.EditHeights(context.Terrains, context.Tiles, area, (x, z, h) =>
			{
				float target = Ditch(x - c.x, z - c.z, inner, width, depth, rimIn, rimOut);
				return float.IsNaN(target) ? h : Mathf.Min(h, target);
			});
			bool wet = AddWater(context, site, inner, width, depth, rimIn, rimOut, rng);
			context.AddKeepOut(site, c.x, c.z, outer + 1f);
			context.AddShape(new PointOfInterestShape
			{
				SiteId = site.Id,
				Shaper = ShaperName,
				Position = c,
				Yaw = site.Yaw,
				Size = new Vector3(width, depth, inner),
				Values = new float[] { Format, inner, width, depth, wet ? 1f : 0f },
			});
		}

		/// <summary>
		/// Takes a moat's water (or, with <paramref name="id"/> negative, every moat's) out of a scene's water: a plan made
		/// again over water read back from its hydrology starts from no moats (PointOfInterestStage.PlanSites).
		/// </summary>
		public static void ClearWater(SceneWater water, int id = -1)
		{
			if (water == null)
			{
				return;
			}
			// Every moat's, and every grotto pool's (CaveShaper.PoolIdBase): the cave writes its pool again when it is built.
			bool Moat(int body) => id < 0 ? body >= CaveShaper.PoolIdBase : body == id;
			water.Lakes.RemoveAll(l => Moat(l.Id));
			SceneWaterGrid grid = water.Grid;
			if (grid == null)
			{
				return;
			}
			for (int i = 0; i < grid.Kind.Length; i++)
			{
				if (grid.Kind[i] == WaterKind.Lake && Moat(grid.Body[i]))
				{
					grid.Kind[i] = WaterKind.None;
					grid.Level[i] = float.NegativeInfinity;
					grid.Body[i] = -1;
				}
			}
		}

		/// <summary>Lake ids moats take, past any the planet's lakes use: this with the site id's low 24 bits (site ids are 32-bit hashes; adding would overflow).</summary>
		public const int LakeIdBase = 1 << 30;
		/// <summary>How far under the lower bank the moat's water stands, metres.</summary>
		public const float Freeboard = 0.7f;

		/// <summary>
		/// Fills the ditch with water (a lake of the scene's, written with its hydrology so it is drawn, swum and kept by a
		/// repaint), level a little under its lowest bank: where the climate is not dry, most of the time, and where at least
		/// half the ring lies under that level (on a slope the ditch's high side stays dry, as a real one would). Frozen
		/// where the biome is cold. False when it stays a dry ditch.
		/// </summary>
		private static bool AddWater(PointOfInterestPlanContext context, PointOfInterestRecord site, float inner, float width, float depth,
			float[] rimIn, float[] rimOut, DeterministicRNG rng)
		{
			SceneWater water = context.Water;
			SceneWaterGrid grid = water?.Grid;
			if (grid == null || rng.NextFloat() >= 0.75f)
			{
				return false;
			}
			bool frozen = false;
			if (BiomeRegistry.TryGetByID(site.BiomeID, out BiomeTemplate biome) && biome != null)
			{
				if (0.5f * (biome.MinHumidity + biome.MaxHumidity) < -0.3f)
				{
					return false;
				}
				frozen = 0.5f * (biome.MinTemperature + biome.MaxTemperature) < -0.5f;
			}
			float level = float.MaxValue;
			for (int i = 0; i < rimIn.Length; i++)
			{
				level = Mathf.Min(level, Mathf.Min(rimIn[i], rimOut[i]));
			}
			level -= Freeboard;
			int wetSpokes = 0;
			var seeds = new List<Vector2>();
			Vector3 c = site.Position;
			for (int i = 0; i < rimIn.Length; i++)
			{
				if (0.5f * (rimIn[i] + rimOut[i]) - depth < level - 0.5f)
				{
					wetSpokes++;
					if (seeds.Count < 8 && i % 12 == 0)
					{
						float a = i * Mathf.PI * 2f / rimIn.Length;
						seeds.Add(new Vector2(c.x + Mathf.Sin(a) * (inner + 0.5f * width), c.z + Mathf.Cos(a) * (inner + 0.5f * width)));
					}
				}
			}
			if (wetSpokes * 2 < rimIn.Length)
			{
				return false;
			}
			float outer = inner + width;
			int id = LakeIdBase | (site.Id & 0x00FFFFFF);
			ClearWater(water, id);
			// Its water on the scene's water grid: every sample in the ring whose dug ground is under the level.
			int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - outer - grid.OriginX) / grid.Spacing)), x1 = Mathf.Min(grid.Width - 1, Mathf.CeilToInt((c.x + outer - grid.OriginX) / grid.Spacing));
			int z0 = Mathf.Max(0, Mathf.FloorToInt((c.z - outer - grid.OriginZ) / grid.Spacing)), z1 = Mathf.Min(grid.Depth - 1, Mathf.CeilToInt((c.z + outer - grid.OriginZ) / grid.Spacing));
			int covered = 0;
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					float east = grid.EastOf(x), north = grid.NorthOf(z);
					float r = Mathf.Sqrt((east - c.x) * (east - c.x) + (north - c.z) * (north - c.z));
					int i = z * grid.Width + x;
					if (r <= inner || r >= outer || grid.Kind[i] != WaterKind.None || context.GroundAt(east, north) >= level)
					{
						continue;
					}
					grid.Kind[i] = WaterKind.Lake;
					grid.Level[i] = level;
					grid.Body[i] = id;
					covered++;
				}
			}
			if (covered == 0 || seeds.Count == 0)
			{
				return false;
			}
			float cx = c.x, cz = c.z, lo = inner - 0.5f, hi = outer + 0.5f;
			water.Lakes.Add(new SceneLake
			{
				Id = id,
				PlanetLake = -1,
				Level = level,
				SpillLevel = level,
				Frozen = frozen,
				Seeds = seeds,
				Bounds = Rect.MinMaxRect(c.x - outer - 1f, c.z - outer - 1f, c.x + outer + 1f, c.z + outer + 1f),
				MayCover = (e, n) =>
				{
					float d = Mathf.Sqrt((e - cx) * (e - cx) + (n - cz) * (n - cz));
					return d > lo && d < hi;
				},
				Covered = covered,
			});
			return true;
		}

		/// <summary>The ditch's floor at an offset from the centre, or NaN outside the ring.</summary>
		private static float Ditch(float dx, float dz, float inner, float width, float depth, float[] rimIn, float[] rimOut)
		{
			float r = Mathf.Sqrt(dx * dx + dz * dz);
			float s = (r - inner) / width;
			if (s <= 0f || s >= 1f)
			{
				return float.NaN;
			}
			float a = Mathf.Repeat(Mathf.Atan2(dx, dz), Mathf.PI * 2f) / (Mathf.PI * 2f) * rimIn.Length;
			int i0 = (int)a % rimIn.Length, i1 = (i0 + 1) % rimIn.Length;
			float f = a - Mathf.Floor(a);
			float top = Mathf.Lerp(Mathf.Lerp(rimIn[i0], rimIn[i1], f), Mathf.Lerp(rimOut[i0], rimOut[i1], f), s);
			// 45° banks (a run of the depth each side), a flat bottom between, the bank's lip softened over half a metre.
			float bank = depth / width;
			float t = s < bank ? s / bank : s > 1f - bank ? (1f - s) / bank : 1f;
			float lip = Mathf.Clamp01(Mathf.Min(s, 1f - s) * width / 0.5f);
			return top - depth * Mathf.Min(t, lip * t + (1f - lip) * t * t);
		}

		/// <summary>The moat stored for a site: its inner radius, width and depth; false when it has none.</summary>
		public static bool TryMoat(ScenePointsOfInterest points, int siteId, out Vector3 centre, out float inner, out float width, out float depth)
			=> TryMoat(points?.Shapes, siteId, out centre, out inner, out width, out depth);

		/// <summary>As <see cref="TryMoat(ScenePointsOfInterest, int, out Vector3, out float, out float, out float)"/>, over a plan's shapes.</summary>
		public static bool TryMoat(IEnumerable<PointOfInterestShape> shapes, int siteId, out Vector3 centre, out float inner, out float width, out float depth)
		{
			centre = Vector3.zero;
			inner = width = depth = 0f;
			if (shapes == null)
			{
				return false;
			}
			foreach (PointOfInterestShape shape in shapes)
			{
				if (shape != null && shape.SiteId == siteId && shape.Shaper == ShaperName && shape.Values != null && shape.Values.Length >= 4)
				{
					centre = shape.Position;
					inner = shape.Values[1];
					width = shape.Values[2];
					depth = shape.Values[3];
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether (x, z) is in any of the moats among <paramref name="shapes"/>: the roads' carve keeps out.</summary>
		public static bool Contains(IReadOnlyList<PointOfInterestShape> shapes, float x, float z)
		{
			if (shapes == null)
			{
				return false;
			}
			foreach (PointOfInterestShape shape in shapes)
			{
				if (shape == null || shape.Shaper != ShaperName || shape.Values == null || shape.Values.Length < 4)
				{
					continue;
				}
				float dx = x - shape.Position.x, dz = z - shape.Position.z;
				float r = Mathf.Sqrt(dx * dx + dz * dz);
				if (r > shape.Values[1] - 0.5f && r < shape.Values[1] + shape.Values[2] + 0.5f)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Over a moat among <paramref name="shapes"/>: the level of its banks there (read from the ground just inside and
		/// outside it, which the ditch never touches), lerped across; false elsewhere.
		/// </summary>
		public static bool BankLevel(IReadOnlyList<PointOfInterestShape> shapes, float x, float z, System.Func<float, float, float> ground, out float level)
		{
			level = 0f;
			if (shapes == null)
			{
				return false;
			}
			foreach (PointOfInterestShape shape in shapes)
			{
				if (shape == null || shape.Shaper != ShaperName || shape.Values == null || shape.Values.Length < 4)
				{
					continue;
				}
				float dx = x - shape.Position.x, dz = z - shape.Position.z;
				float r = Mathf.Sqrt(dx * dx + dz * dz);
				float inner = shape.Values[1], width = shape.Values[2];
				if (r <= inner - 0.5f || r >= inner + width + 0.5f || r < 1e-3f)
				{
					continue;
				}
				float ux = dx / r, uz = dz / r;
				float a = ground(shape.Position.x + ux * (inner - 1f), shape.Position.z + uz * (inner - 1f));
				float b = ground(shape.Position.x + ux * (inner + width + 2f), shape.Position.z + uz * (inner + width + 2f));
				level = Mathf.Lerp(a, b, Mathf.Clamp01((r - inner) / width));
				return true;
			}
			return false;
		}

		public void Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
			// The ditch is the terrain's; its crossings are the drawbridges PropsFeature lays at the gates.
		}
	}
}
#endif
