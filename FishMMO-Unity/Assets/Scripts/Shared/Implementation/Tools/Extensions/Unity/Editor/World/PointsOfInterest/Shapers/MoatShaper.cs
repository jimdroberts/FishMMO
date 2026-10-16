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
	/// <b>A wet ditch.</b> A ring <see cref="Width"/> across with 45° banks (the NavMesh agent's limit, so it is a barrier to a
	/// charge, not to a climb) down to one level floor, <see cref="Depth"/> under the lowest point of its rim: on a slope the
	/// uphill side is dug deeper, so the whole ring holds water at one level, a little under that lowest point
	/// (<see cref="AddWater"/>), frozen or left a dry ditch as the biome's template says (its Moat Water; until 2026-10-16
	/// read from its climate: frozen where cold, dry where arid). The level is
	/// measured again on the finished ground when the scene's water is saved (WaterSettle), so whatever later cut its rim,
	/// the water stands under it. Not every stronghold has one: a fortress most often, a keep least, and none on ground too
	/// steep to ring (a moat on a hillside is a terrace) or where a river already runs round it.
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
			float floor = lo - depth;
			var area = Rect.MinMaxRect(c.x - outer - 1f, c.z - outer - 1f, c.x + outer + 1f, c.z + outer + 1f);
			PointOfInterestTerrain.EditHeights(context.Terrains, context.Tiles, area, (x, z, h) =>
			{
				float target = Ditch(x - c.x, z - c.z, inner, width, floor, rimIn, rimOut);
				return float.IsNaN(target) ? h : Mathf.Min(h, target);
			});
			bool wet = AddWater(context, site, inner, width, rimIn, rimOut);
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
		/// repaint), level <see cref="Freeboard"/> under its lowest bank, all round the ring (its floor is level under that bank,
		/// <see cref="Ditch"/>). Frozen or a dry ditch (false) as the site's biome template says (BiomeTemplateArt.MoatWater).
		/// </summary>
		private static bool AddWater(PointOfInterestPlanContext context, PointOfInterestRecord site, float inner, float width,
			float[] rimIn, float[] rimOut)
		{
			SceneWater water = context.Water;
			SceneWaterGrid grid = water?.Grid;
			if (grid == null)
			{
				return false;
			}
			// What stands in it is the site biome's (its template's Moat Water, 2026-10-16), no longer worked out here from the
			// template's climate envelope (which is what the authoring wrote onto every template).
			BiomeMoatWater moat = BiomeTemplateArt.MoatWater(BiomeRegistry.TryGetByID(site.BiomeID, out BiomeTemplate biome) ? biome : null);
			if (moat == BiomeMoatWater.Dry)
			{
				return false;
			}
			bool frozen = moat == BiomeMoatWater.Frozen;
			float level = float.MaxValue;
			for (int i = 0; i < rimIn.Length; i++)
			{
				level = Mathf.Min(level, Mathf.Min(rimIn[i], rimOut[i]));
			}
			level -= Freeboard;
			// Seeded down the floor's middle all round (it lies Depth under the lowest bank, so under the level everywhere).
			var seeds = new List<Vector2>();
			Vector3 c = site.Position;
			const int SeedCount = 8;
			for (int k = 0; k < SeedCount; k++)
			{
				float a = k * Mathf.PI * 2f / SeedCount;
				seeds.Add(new Vector2(c.x + Mathf.Sin(a) * (inner + 0.5f * width), c.z + Mathf.Cos(a) * (inner + 0.5f * width)));
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

		/// <summary>
		/// The ditch's ground at an offset from the centre, or NaN outside the ring: a bank down from each lip (the inner and
		/// outer bank's own level there) to the level <paramref name="floor"/>, at 45° or, on a slope's high side where the ring
		/// is too narrow for that, steeper, so the middle <see cref="FloorShare"/> of the ring is always floor. A floor following
		/// the ground instead (each spoke its own depth under its own banks) held no one level on a slope: the water filled the
		/// low side and left the high side a dry trench above it.
		/// </summary>
		public static float Ditch(float dx, float dz, float inner, float width, float floor, float[] rimIn, float[] rimOut)
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
			float lipIn = Mathf.Lerp(rimIn[i0], rimIn[i1], f), lipOut = Mathf.Lerp(rimOut[i0], rimOut[i1], f);
			// A metre down for each metre in from either lip, or as steep as reaches the floor within its share of the width.
			float run = 0.5f * (1f - FloorShare) * width;
			float slopeIn = Mathf.Max(1f, (lipIn - floor) / run), slopeOut = Mathf.Max(1f, (lipOut - floor) / run);
			return Mathf.Max(floor, Mathf.Max(lipIn - s * width * slopeIn, lipOut - (1f - s) * width * slopeOut));
		}

		/// <summary>The share of the ditch's width, across its middle, that is always its level floor.</summary>
		public const float FloorShare = 0.2f;

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

		/// <summary>How far out from the ditch a crossing's landing runs level, metres.</summary>
		public const float LandingMetres = 4f;
		/// <summary>How far past the drawbridge's leaf a landing runs each side, metres.</summary>
		public const float LandingSideMetres = 1f;
		/// <summary>The grade a way ramps from a landing back to the ground at (rise over run).</summary>
		public const float LandingRampGrade = 0.25f;
		/// <summary>The most a landing's ramp is carried to meet the ground, metres of height either way.</summary>
		public const float LandingMaxRiseMetres = 10f;
		/// <summary>The widest drawbridge leaf a landing is laid for, metres (PropsFeature: a 3.5 m passage at its stature and some).</summary>
		public const float LandingLeafMetres = 4.2f;

		/// <summary>
		/// Levels the far bank where each way crosses a moat to the height of the ditch's inner lip there (the drawbridge's hinge,
		/// PropsFeature.AddDrawbridges): a lowered drawbridge lies level, and laid from a lip 3.7 m under the far bank it ran
		/// into the bank (Flo Monolith's Mossy Elkring Keep, 2026-10-15). A landing <see cref="LandingMetres"/> out and the leaf's
		/// width and <see cref="LandingSideMetres"/> each side across; past it the way ramps back to the ground at
		/// <see cref="LandingRampGrade"/>, its sides at 45°. Cut or filled, the ditch itself untouched; idempotent (the lip's
		/// height is read inside the ditch's inner rim, which this never reaches). Returns the crossings landed.
		/// </summary>
		public static int Landings(Terrain[,] terrains, TerrainTilePlan tiles, IReadOnlyList<PointOfInterestShape> shapes, IReadOnlyList<ScenePath> paths,
			System.Func<float, float, float> ground, List<Landing> laid = null)
		{
			if (shapes == null || paths == null || ground == null)
			{
				return 0;
			}
			int landed = 0;
			foreach (PointOfInterestShape shape in shapes)
			{
				if (shape == null || shape.Shaper != ShaperName || shape.Values == null || shape.Values.Length < 4)
				{
					continue;
				}
				Vector3 c = shape.Position;
				float inner = shape.Values[1], width = shape.Values[2], outer = inner + width;
				var done = new List<Vector2>();
				foreach (ScenePath path in paths)
				{
					if (path == null || (path.FromId != shape.SiteId && path.ToId != shape.SiteId) || path.Class == ScenePathClass.Street)
					{
						continue;
					}
					Vector2 sum = Vector2.zero;
					for (int i = 0; i < path.Count; i++)
					{
						var d = new Vector2(path.Points[i].x - c.x, path.Points[i].z - c.z);
						float r = d.magnitude;
						if (r > inner - 0.5f && r < outer + 0.5f && r > 1e-3f)
						{
							sum += d / r;
						}
					}
					if (sum.sqrMagnitude < 1e-6f)
					{
						continue;
					}
					Vector2 dir = sum.normalized;
					if (done.Exists(o => (o - dir).magnitude * inner < LandingLeafMetres))
					{
						continue;
					}
					done.Add(dir);
					float hinge = ground(c.x + dir.x * (inner - 0.25f), c.z + dir.y * (inner - 0.25f));
					var side = new Vector2(dir.y, -dir.x);
					// As wide as the leaf and a metre, or the way and its shoulders, whichever is wider.
					float half = Mathf.Max(0.5f * LandingLeafMetres + LandingSideMetres, path.MaxHalfWidth + ScenePathStyle.For(path.Class).Shoulder);
					// Where the ramp meets the ground again along the way: the landing's edits end there.
					float ramp = 0f;
					for (float u = LandingMetres; u <= LandingMetres + LandingMaxRiseMetres / LandingRampGrade; u += 1f)
					{
						float g = ground(c.x + dir.x * (outer + u), c.z + dir.y * (outer + u));
						ramp = u - LandingMetres;
						if (Mathf.Abs(g - hinge) <= (u - LandingMetres) * LandingRampGrade)
						{
							break;
						}
					}
					// Out to where a far bank up to LandingMaxRiseMetres from the hinge has ramped back to the ground.
					float reach = LandingMetres + LandingMaxRiseMetres / LandingRampGrade, wide = half + LandingMaxRiseMetres + 2f;
					var area = Rect.MinMaxRect(c.x + Mathf.Min(dir.x * outer, dir.x * (outer + reach)) - wide, c.z + Mathf.Min(dir.y * outer, dir.y * (outer + reach)) - wide,
						c.x + Mathf.Max(dir.x * outer, dir.x * (outer + reach)) + wide, c.z + Mathf.Max(dir.y * outer, dir.y * (outer + reach)) + wide);
					laid?.Add(new Landing { Centre = new Vector2(c.x, c.z), Dir = dir, Outer = outer, Half = half, Hinge = hinge, Ramp = ramp });
					PointOfInterestTerrain.EditHeights(terrains, tiles, area, (x, z, h) =>
					{
						var p = new Vector2(x - c.x, z - c.z);
						float u = Vector2.Dot(p, dir) - outer, v = Mathf.Abs(Vector2.Dot(p, side));
						if (u < 0f)
						{
							/* The ditch's own far bank, across the crossing: its top brought down to the landing, at 45° on into the
							 * ditch (only ever lowered, never under its floor's own ground). Cut from the far bank's height, it stood
							 * as a lip between the lowered drawbridge and the landing behind it. Only the far half of the ditch: carried on
							 * across a narrow one it dug into the inner bank under the drawbridge's hinge. */
							if (p.magnitude <= inner || u < -0.5f * width)
							{
								return h;
							}
							return Mathf.Min(h, hinge + u + Mathf.Max(0f, v - half));
						}
						float along = Mathf.Max(0f, u - LandingMetres), across = Mathf.Max(0f, v - half);
						float give = along * LandingRampGrade + across;
						return Mathf.Clamp(h, hinge - give, hinge + give);
					});
					landed++;
				}
			}
			return landed;
		}

		/// <summary>A moat crossing's landing: the level ground past the ditch where its drawbridge comes down.</summary>
		public struct Landing
		{
			public Vector2 Centre;
			public Vector2 Dir;
			public float Outer;
			public float Half;
			public float Hinge;
			/// <summary>How far past the landing its ramp runs back up or down to the ground, metres.</summary>
			public float Ramp;

			/// <summary>Whether (x, z) is on the landing's level ground.</summary>
			public bool Contains(float x, float z) => Within(x, z, LandingMetres);

			/// <summary>Whether (x, z) is on the landing or the ramp from it, which the way's carve leaves as they were laid.</summary>
			public bool ContainsRamp(float x, float z) => Within(x, z, LandingMetres + Ramp);

			private bool Within(float x, float z, float reach)
			{
				var p = new Vector2(x - Centre.x, z - Centre.y);
				float u = Vector2.Dot(p, Dir) - Outer, v = Mathf.Abs(p.x * Dir.y - p.y * Dir.x);
				return u >= 0f && u <= reach && v <= Half && p.magnitude >= Outer;
			}
		}

		/// <summary>Whether (x, z) lies on any of <paramref name="landings"/> or their ramps.</summary>
		public static bool OnLandingOrRamp(IReadOnlyList<Landing> landings, float x, float z)
		{
			if (landings != null)
			{
				foreach (Landing l in landings)
				{
					if (l.ContainsRamp(x, z))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>The landing (x, z) lies on among <paramref name="landings"/>, false for none.</summary>
		public static bool OnLanding(IReadOnlyList<Landing> landings, float x, float z, out Landing landing)
		{
			landing = default;
			if (landings == null)
			{
				return false;
			}
			foreach (Landing l in landings)
			{
				if (l.Contains(x, z))
				{
					landing = l;
					return true;
				}
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
