#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Registers the fantasy wave's shapers (2026-10-27) after the cave family's.</summary>
	[InitializeOnLoad]
	internal static class FantasyShapersRegistration
	{
		static FantasyShapersRegistration()
		{
			PointOfInterestShapers.Register(CenoteShaper.Instance);
			PointOfInterestShapers.Register(SandFunnelShaper.Instance);
			PointOfInterestShapers.Register(CausewayShaper.Instance);
			PointOfInterestShapers.Register(FaceSnapShaper.Instance);
		}
	}

	/// <summary>Bits the fantasy wave's shapers share.</summary>
	internal static class FantasyShaping
	{
		public static Vector2 Dir(float yaw) => new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));

		public static float YawOf(Vector2 d) => Mathf.Repeat(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 360f);

		/// <summary>The lowest ground on a ring about (x, z): what a cut's level is measured from, outside anything the cut changes.</summary>
		public static float LowestOnRing(Func<float, float, float> ground, float x, float z, float radius, int samples = 24)
		{
			float low = float.PositiveInfinity;
			for (int k = 0; k < samples; k++)
			{
				float a = k * Mathf.PI * 2f / samples;
				low = Mathf.Min(low, ground(x + Mathf.Sin(a) * radius, z + Mathf.Cos(a) * radius));
			}
			return low;
		}

		/// <summary>The mean ground on a ring about (x, z).</summary>
		public static float MeanOnRing(Func<float, float, float> ground, float x, float z, float radius, int samples = 24)
		{
			float sum = 0f;
			for (int k = 0; k < samples; k++)
			{
				float a = k * Mathf.PI * 2f / samples;
				sum += ground(x + Mathf.Sin(a) * radius, z + Mathf.Cos(a) * radius);
			}
			return sum / samples;
		}

		/// <summary>Whether any river or lake lies within the disc (the scene's water as planned).</summary>
		public static bool Wet(SceneWater water, float x, float z, float radius)
		{
			if (water == null)
			{
				return false;
			}
			for (int k = 0; k <= 16; k++)
			{
				float a = k * Mathf.PI / 8f, r = k == 16 ? 0f : radius;
				float px = x + Mathf.Sin(a) * r, pz = z + Mathf.Cos(a) * r;
				if (!float.IsNegativeInfinity(water.RiverSurfaceNear(px, pz, 0f)) || !float.IsNegativeInfinity(water.LakeSurfaceNear(px, pz)))
				{
					return true;
				}
			}
			return false;
		}
	}

	// ── The cenote ─────────────────────────────────────────────────────

	/// <summary>
	/// A cenote (2026-10-27, the fantasy wave): a round, sheer-sided well in the rock cut down to its water, a pool at the bottom, a
	/// ledge a little over the pool with the site's altar on it, a stair cut down into the wall round to the ledge, and roots hanging
	/// from the lip over the drop.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The cut</b> (<see cref="CutHeight"/>, pure and idempotent: it only lowers, to bounds that depend on the plan alone): the
	/// pit's floor under the pool inside <see cref="Plan.Radius"/>, its wall rising at <see cref="WallGrade"/> (79°, as sheer as the
	/// heightmap holds) to the rim; the stair a shelf <see cref="StairWidth"/> wide cut into the wall, falling from the rim at
	/// <see cref="StairGrade"/> round to the ledge, its inner edge the pit's sheer drop. The water stands where a karst well's water
	/// table would put it: <see cref="Plan.Depth"/> under the lowest of the rim, never within 1 m of the sea.
	/// </para>
	/// <para>
	/// <b>The site</b> moves to the ledge (the props lay the template's altar there, facing the pool) and keeps a small radius, so
	/// nothing of its template is strewn down the walls; the pit, the stair and the rim are kept clear of the scatter. The pool is a
	/// lake of the scene's (its id <see cref="PoolIdBase"/> with the site's low bits), written with the hydrology like a moat's.
	/// </para>
	/// </remarks>
	public sealed class CenoteShaper : IPointOfInterestShaper
	{
		public static readonly CenoteShaper Instance = new CenoteShaper();
		public const string ShaperName = "Cenote";
		private const int Format = 1;

		/// <summary>The wall's grade (rise over run) and the stair's, the stair's width, the ledge's depth and half-width, metres.</summary>
		public const float WallGrade = 5f, StairGrade = 0.5f, StairWidth = 2.2f, LedgeDepth = 3.4f, LedgeHalf = 3f;
		/// <summary>How deep the pool stands, how far the ledge stands over it, metres.</summary>
		public const float PoolDepth = 3f, LedgeRise = 1.2f;
		/// <summary>The pit's share of the site's radius (the rest is the wall and the rim).</summary>
		public const float PitShare = 0.62f;
		/// <summary>The shallowest a cenote's water stands under its rim, metres: shallower, and it is a pond.</summary>
		public const float MinDepth = 6f;
		/// <summary>The site's radius once it stands on its ledge, metres.</summary>
		public const float LedgeSiteRadius = 4f;
		/// <summary>Lake ids cenote pools take: the moats' base with bit 27, and the site id's low 24 bits.</summary>
		public const int PoolIdBase = MoatShaper.LakeIdBase | (1 << 27);

		public bool Handles(POIType kind) => kind == POIType.Cenote;

		/// <summary>What a planned cenote stores.</summary>
		public struct Plan
		{
			public Vector3 Centre;
			/// <summary>The pit's radius at its floor (and its wall's foot), metres.</summary>
			public float Radius;
			/// <summary>The lowest of the rim, the pool's surface and the pit's floor.</summary>
			public float Rim, Pool;
			/// <summary>The bearing from the centre to the ledge, degrees.</summary>
			public float LedgeYaw;

			public float Floor => Pool - PoolDepth;
			public float Ledge => Pool + LedgeRise;
			public float Depth => Rim - Pool;
			/// <summary>The stair's run round the wall, radians, from the rim down to the ledge's end.</summary>
			public float StairArc => Mathf.Min(1.7f * Mathf.PI, Mathf.Max(0f, Rim - Ledge) / StairGrade / (Radius + 0.5f * StairWidth));
			public float LedgeArc => LedgeHalf / Radius;
			/// <summary>How far out from the centre the cut can reach.</summary>
			public float Reach => Radius + Mathf.Max(LedgeDepth, StairWidth) + (Rim - Floor) / WallGrade + 1f;

			public float[] ToValues() => new[] { Format, Centre.x, Centre.y, Centre.z, Radius, Rim, Pool, LedgeYaw };

			public static bool TryFrom(float[] v, out Plan plan)
			{
				plan = default;
				if (v == null || v.Length != 8 || Mathf.RoundToInt(v[0]) != Format)
				{
					return false;
				}
				plan = new Plan { Centre = new Vector3(v[1], v[2], v[3]), Radius = v[4], Rim = v[5], Pool = v[6], LedgeYaw = v[7] };
				return true;
			}

			/// <summary>Where the ledge's middle stands (world, y its floor).</summary>
			public Vector3 LedgeSpot
			{
				get
				{
					Vector2 d = FantasyShaping.Dir(LedgeYaw);
					float r = Radius + 0.5f * LedgeDepth;
					return new Vector3(Centre.x + d.x * r, Ledge, Centre.z + d.y * r);
				}
			}
		}

		/// <summary>
		/// The plan for a cenote at (x, z) of the given radius (pure): its pit, its rim measured on a ring outside the cut, its water
		/// <paramref name="depthShare"/> × the pit's radius under the rim (8 … 16 m), kept a metre over the sea. False where the well
		/// would be shallower than <see cref="MinDepth"/>.
		/// </summary>
		public static bool TryPlan(Func<float, float, float> ground, float x, float z, float siteRadius, float ledgeYaw, float seaLevel, out Plan plan)
		{
			float radius = Mathf.Max(4f, siteRadius * PitShare);
			float rim = FantasyShaping.LowestOnRing(ground, x, z, siteRadius + 6f);
			float pool = Mathf.Max(rim - Mathf.Clamp(radius * 1.2f, 8f, 16f), seaLevel + 1f);
			plan = new Plan { Centre = new Vector3(x, pool, z), Radius = radius, Rim = rim, Pool = pool, LedgeYaw = Mathf.Repeat(ledgeYaw, 360f) };
			return rim - pool >= MinDepth;
		}

		/// <summary>The ground at (x, z) once the cenote is cut, from <paramref name="height"/>: only ever lowered (idempotent).</summary>
		public static float CutHeight(in Plan plan, float x, float z, float height)
		{
			float dx = x - plan.Centre.x, dz = z - plan.Centre.z;
			float r = Mathf.Sqrt(dx * dx + dz * dz);
			if (r > plan.Reach)
			{
				return height;
			}
			float target;
			if (r <= plan.Radius)
			{
				target = plan.Floor;
			}
			else
			{
				// The angle from the ledge's bearing, signed: the stair comes down to it from the negative side.
				float a = Mathf.DeltaAngle(plan.LedgeYaw, Mathf.Atan2(dx, dz) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
				float shelf = float.NaN, shelfOut = 0f;
				if (Mathf.Abs(a) <= plan.LedgeArc)
				{
					shelf = plan.Ledge;
					shelfOut = plan.Radius + LedgeDepth;
				}
				else if (a < -plan.LedgeArc && a >= -plan.LedgeArc - plan.StairArc)
				{
					float t = (-plan.LedgeArc - a) / Mathf.Max(1e-4f, plan.StairArc);
					shelf = Mathf.Lerp(plan.Ledge, plan.Rim, t);
					shelfOut = plan.Radius + StairWidth;
				}
				target = float.IsNaN(shelf)
					? plan.Floor + (r - plan.Radius) * WallGrade
					: r <= shelfOut ? shelf : shelf + (r - shelfOut) * WallGrade;
			}
			return Mathf.Min(height, target);
		}

		void IPointOfInterestShaper.Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			if (!TryPlan(context.GroundAt, site.Position.x, site.Position.z, site.Radius, site.Yaw, CaveShaper.SeaLevel, out Plan plan))
			{
				context.Reject(site, $"its ground stands too low over the sea for a well {MinDepth:0} m deep");
				return;
			}
			if (FantasyShaping.Wet(context.Water, plan.Centre.x, plan.Centre.z, plan.Reach))
			{
				context.Reject(site, "a river or a lake runs through where its well would be cut");
				return;
			}
			Plan cut = plan;
			float reach = plan.Reach + 1f;
			PointOfInterestTerrain.EditHeights(context.Terrains, context.Tiles, new Rect(plan.Centre.x - reach, plan.Centre.z - reach, 2f * reach, 2f * reach),
				(x, z, h) => CutHeight(cut, x, z, h));
			AddPool(context, site, plan);
			context.AddKeepOut(site, plan.Centre.x, plan.Centre.z, plan.Reach + 2f);
			context.AddShape(new PointOfInterestShape
			{
				SiteId = site.Id,
				Shaper = ShaperName,
				Position = plan.Centre,
				Yaw = plan.LedgeYaw,
				Size = new Vector3(2f * plan.Radius, plan.Depth, 2f * plan.Radius),
				Seed = site.SiteSeed,
				Values = plan.ToValues(),
			});
			// The site is its ledge: the altar stands there facing the water, and nothing else is strewn down the walls.
			site.Position = plan.LedgeSpot;
			site.Yaw = Mathf.Repeat(plan.LedgeYaw + 180f, 360f);
			site.Radius = LedgeSiteRadius;
		}

		/// <summary>The pool: a lake of the scene's over the pit's floor, seeded at its middle, kept to the pit.</summary>
		private static void AddPool(PointOfInterestPlanContext context, PointOfInterestRecord site, Plan plan)
		{
			SceneWater water = context.Water;
			SceneWaterGrid grid = water?.Grid;
			if (grid == null)
			{
				return;
			}
			int id = PoolIdBase | (site.Id & 0x00FFFFFF);
			MoatShaper.ClearWater(water, id);
			Vector3 c = plan.Centre;
			float radius = plan.Radius, level = plan.Pool;
			int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - radius - grid.OriginX) / grid.Spacing)), x1 = Mathf.Min(grid.Width - 1, Mathf.CeilToInt((c.x + radius - grid.OriginX) / grid.Spacing));
			int z0 = Mathf.Max(0, Mathf.FloorToInt((c.z - radius - grid.OriginZ) / grid.Spacing)), z1 = Mathf.Min(grid.Depth - 1, Mathf.CeilToInt((c.z + radius - grid.OriginZ) / grid.Spacing));
			int covered = 0;
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					float east = grid.EastOf(x), north = grid.NorthOf(z);
					int i = z * grid.Width + x;
					if ((east - c.x) * (east - c.x) + (north - c.z) * (north - c.z) > radius * radius || grid.Kind[i] != WaterKind.None || context.GroundAt(east, north) >= level)
					{
						continue;
					}
					grid.Kind[i] = WaterKind.Lake;
					grid.Level[i] = level;
					grid.Body[i] = id;
					covered++;
				}
			}
			if (covered == 0)
			{
				return;
			}
			bool frozen = BiomeTemplateArt.MoatWater(BiomeRegistry.TryGetByID(site.BiomeID, out BiomeTemplate biome) ? biome : null) == BiomeMoatWater.Frozen
				&& !(biome != null && biome.WaterLook.Hot);
			float cx = c.x, cz = c.z;
			water.Lakes.Add(new SceneLake
			{
				Id = id,
				PlanetLake = -1,
				Level = level,
				SpillLevel = level,
				Frozen = frozen,
				Seeds = new List<Vector2> { new Vector2(cx, cz), new Vector2(cx + radius * 0.4f, cz), new Vector2(cx - radius * 0.4f, cz), new Vector2(cx, cz + radius * 0.4f) },
				Bounds = Rect.MinMaxRect(cx - radius - 1f, cz - radius - 1f, cx + radius + 1f, cz + radius + 1f),
				MayCover = (e, n) => (e - cx) * (e - cx) + (n - cz) * (n - cz) < (radius + 0.5f) * (radius + 0.5f),
				Covered = covered,
			});
		}

		/// <summary>The plan stored for a site; false for none.</summary>
		public static bool TryPlan(IEnumerable<PointOfInterestShape> shapes, int siteId, out Plan plan)
		{
			plan = default;
			if (shapes != null)
			{
				foreach (PointOfInterestShape shape in shapes)
				{
					if (shape != null && shape.SiteId == siteId && shape.Shaper == ShaperName && Plan.TryFrom(shape.Values, out plan))
					{
						return true;
					}
				}
			}
			return false;
		}

		void IPointOfInterestShaper.Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
			if (!TryPlan(context.ShapesOf(site), site.Id, out Plan plan) || context.GroundAt == null)
			{
				return;
			}
			// Roots hang from the lip over the drop: curtains of the biome's vines along chords of the rim, the stair and the ledge left open.
			VineBiomes.Habitat habitat = BiomeTemplateArt.Vines(BiomeRegistry.TryGetByID(site.BiomeID, out BiomeTemplate biome) ? biome : null);
			if (habitat == null)
			{
				return;
			}
			var rng = new DeterministicRNG((int)PointOfInterestPlanner.Mix(unchecked((uint)site.SiteSeed ^ 0x2007u)));
			const int Chords = 12;
			float lip = plan.Radius + 0.6f;
			int hung = 0, missing = 0;
			for (int k = 0; k < Chords; k++)
			{
				float a0 = plan.LedgeYaw + (k + 0.5f) * 360f / Chords, a1 = a0 + 360f / Chords;
				float mid = Mathf.DeltaAngle(plan.LedgeYaw, 0.5f * (a0 + a1)) * Mathf.Deg2Rad;
				// Not over the stair or the ledge (the way down stays open), and not every chord.
				if (mid > -plan.LedgeArc - plan.StairArc - 0.2f && mid < plan.LedgeArc + 0.2f || rng.NextFloat() < 0.35f)
				{
					continue;
				}
				Vector2 d0 = FantasyShaping.Dir(a0), d1 = FantasyShaping.Dir(a1);
				var left = new Vector3(plan.Centre.x + d0.x * lip, plan.Rim, plan.Centre.z + d0.y * lip);
				var right = new Vector3(plan.Centre.x + d1.x * lip, plan.Rim, plan.Centre.z + d1.y * lip);
				Vector2 inward = -(d0 + d1).normalized;
				foreach (VineSpot spot in VineRules.CaveMouth(left, right, new Vector3(inward.x, 0f, inward.y), plan.Rim - plan.Pool, habitat, rng, null, context.GroundAt, out _))
				{
					if (spot.OnGround)
					{
						continue;
					}
					GameObject prefab = VinePlacement.PrefabFor(in spot);
					if (prefab == null)
					{
						missing++;
						continue;
					}
					context.AddProp(prefab, spot.Position, VinePlacement.RotationOf(in spot), spot.Scale);
					hung++;
				}
			}
			if (hung > 0 || missing > 0)
			{
				context.Notes?.Add($"{site.Name}: {hung} root curtain(s) hung over the well" + (missing > 0 ? $"; {missing} left out (no vine art: Generate Biome Art)." : "."));
			}
		}
	}

	// ── The sandworm's funnel ──────────────────────────────────────────

	/// <summary>
	/// A sandworm's pit (2026-10-27): a funnel of loose sand cut into the open desert, its sides at the sand's angle of repose
	/// (<see cref="SideDegrees"/>), its throat <see cref="ThroatRadius"/> across where the worm lies (the site's boss lair). The lip is the
	/// site's radius, the depth what the cone reaches at it under the ring's mean height. Pure cut, idempotent (only lowers to a cone
	/// fixed by the plan).
	/// </summary>
	public sealed class SandFunnelShaper : IPointOfInterestShaper
	{
		public static readonly SandFunnelShaper Instance = new SandFunnelShaper();
		public const string ShaperName = "SandFunnel";
		private const int Format = 1;
		/// <summary>The funnel's sides, degrees: loose dry sand's angle of repose.</summary>
		public const float SideDegrees = 30f;
		public const float ThroatRadius = 2.5f;

		public static float Grade => Mathf.Tan(SideDegrees * Mathf.Deg2Rad);

		public bool Handles(POIType kind) => kind == POIType.SandwormPit;

		public struct Plan
		{
			public Vector3 Centre;
			/// <summary>The lip's radius, the throat's floor (world y).</summary>
			public float Lip, Throat;

			public float[] ToValues() => new[] { Format, Centre.x, Centre.y, Centre.z, Lip, Throat };

			public static bool TryFrom(float[] v, out Plan plan)
			{
				plan = default;
				if (v == null || v.Length != 6 || Mathf.RoundToInt(v[0]) != Format)
				{
					return false;
				}
				plan = new Plan { Centre = new Vector3(v[1], v[2], v[3]), Lip = v[4], Throat = v[5] };
				return true;
			}
		}

		/// <summary>The funnel for a site (pure): its lip at the site's radius, the throat down the cone from the lip ring's mean ground.</summary>
		public static Plan PlanFor(Func<float, float, float> ground, float x, float z, float radius)
		{
			float lip = Mathf.Max(ThroatRadius + 4f, radius);
			float rim = FantasyShaping.MeanOnRing(ground, x, z, lip + 3f);
			float throat = rim - (lip - ThroatRadius) * Grade;
			return new Plan { Centre = new Vector3(x, throat, z), Lip = lip, Throat = throat };
		}

		/// <summary>The ground once the funnel is cut: the cone (level in the throat), never raised.</summary>
		public static float CutHeight(in Plan plan, float x, float z, float height)
		{
			float r = Mathf.Sqrt((x - plan.Centre.x) * (x - plan.Centre.x) + (z - plan.Centre.z) * (z - plan.Centre.z));
			return Mathf.Min(height, plan.Throat + Mathf.Max(0f, r - ThroatRadius) * Grade);
		}

		void IPointOfInterestShaper.Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			Plan plan = PlanFor(context.GroundAt, site.Position.x, site.Position.z, site.Radius);
			if (plan.Throat < CaveShaper.SeaLevel + 1f)
			{
				context.Reject(site, "its funnel would reach down to the sea");
				return;
			}
			if (FantasyShaping.Wet(context.Water, plan.Centre.x, plan.Centre.z, plan.Lip + 4f))
			{
				context.Reject(site, "water stands where its funnel would be cut");
				return;
			}
			Plan cut = plan;
			float reach = plan.Lip + 8f;
			PointOfInterestTerrain.EditHeights(context.Terrains, context.Tiles, new Rect(plan.Centre.x - reach, plan.Centre.z - reach, 2f * reach, 2f * reach),
				(x, z, h) => CutHeight(cut, x, z, h));
			context.AddKeepOut(site, plan.Centre.x, plan.Centre.z, plan.Lip + 2f);
			context.AddShape(new PointOfInterestShape
			{
				SiteId = site.Id,
				Shaper = ShaperName,
				Position = plan.Centre,
				Size = new Vector3(2f * plan.Lip, (plan.Lip - ThroatRadius) * Grade, 2f * plan.Lip),
				Seed = site.SiteSeed,
				Values = plan.ToValues(),
			});
			// The worm's lair is the throat.
			site.Position = plan.Centre;
		}

		void IPointOfInterestShaper.Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
			// The ground is cut at plan time; the bones round the lip are the template's.
		}
	}

	// ── The tidal causeway ─────────────────────────────────────────────

	/// <summary>
	/// A tidal island's causeway (2026-10-27): a bank of sand from the island's shore to the mainland's, its crest
	/// <see cref="Crest"/> over the mean sea, so the high tide covers it and the low uncovers it (the sea's tide is measured from the
	/// coast's biomes only after the sites are planned, so the crest is set for any tide). The site (the abbey) is the planner's, on the
	/// island's heart facing the crossing; the shaper finds the shortest crossing round that heading on the ground as it stands.
	/// </summary>
	public sealed class CausewayShaper : IPointOfInterestShaper
	{
		public static readonly CausewayShaper Instance = new CausewayShaper();
		public const string ShaperName = "Causeway";
		private const int Format = 1;
		/// <summary>The crest over the mean sea, the crest's half-width, the banks' grade, metres.</summary>
		public const float Crest = 0.3f, HalfWidth = 2.5f, BankGrade = 0.3f;
		/// <summary>How far onto each shore the causeway runs, metres.</summary>
		public const float Landing = 6f;

		public bool Handles(POIType kind) => kind == POIType.TidalIslandAbbey;

		public struct Plan
		{
			/// <summary>Its two ends on dry land (the island's, the mainland's), world xz; y the crest.</summary>
			public Vector3 From, To;

			public float[] ToValues() => new[] { Format, From.x, From.y, From.z, To.x, To.y, To.z };

			public static bool TryFrom(float[] v, out Plan plan)
			{
				plan = default;
				if (v == null || v.Length != 7 || Mathf.RoundToInt(v[0]) != Format)
				{
					return false;
				}
				plan = new Plan { From = new Vector3(v[1], v[2], v[3]), To = new Vector3(v[4], v[5], v[6]) };
				return true;
			}
		}

		/// <summary>
		/// The shortest crossing from the island at (x, z) to land past the sea within <paramref name="reach"/>, tried on headings within
		/// 60° of <paramref name="yaw"/> (pure): from the island's last dry point to the mainland's first, each run on by
		/// <see cref="Landing"/>; false where no crossing is shallow enough (<see cref="PointOfInterestPlanner.MaxCausewayDepth"/>).
		/// </summary>
		public static bool TryPlan(Func<float, float, float> ground, float seaLevel, float x, float z, float yaw, float reach, out Plan plan)
		{
			plan = default;
			float best = float.PositiveInfinity;
			var start = new Vector2(x, z);
			for (int k = -12; k <= 12; k++)
			{
				float heading = yaw + k * 5f;
				Vector2 d = FantasyShaping.Dir(heading);
				float shore = float.NaN, land = float.NaN, deepest = 0f;
				for (float t = 2f; t <= reach + 400f; t += 2f)
				{
					Vector2 p = start + d * t;
					float h = ground(p.x, p.y);
					if (float.IsNaN(shore))
					{
						if (h < seaLevel)
						{
							shore = t;
						}
						continue;
					}
					deepest = Mathf.Max(deepest, seaLevel - h);
					if (deepest > PointOfInterestPlanner.MaxCausewayDepth || t - shore > reach)
					{
						break;
					}
					if (h >= seaLevel + Crest)
					{
						land = t;
						break;
					}
				}
				if (float.IsNaN(land) || land - shore >= best)
				{
					continue;
				}
				best = land - shore;
				Vector2 a = start + d * Mathf.Max(2f, shore - Landing), b = start + d * (land + Landing);
				plan = new Plan { From = new Vector3(a.x, seaLevel + Crest, a.y), To = new Vector3(b.x, seaLevel + Crest, b.y) };
			}
			return !float.IsPositiveInfinity(best);
		}

		/// <summary>The ground once the causeway is laid: raised to its crest along the line, banks down to the sea floor, never lowered.</summary>
		public static float CutHeight(in Plan plan, float x, float z, float height)
		{
			var a = new Vector2(plan.From.x, plan.From.z);
			var b = new Vector2(plan.To.x, plan.To.z);
			Vector2 ab = b - a;
			float t = Mathf.Clamp01(Vector2.Dot(new Vector2(x, z) - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
			float off = Vector2.Distance(new Vector2(x, z), a + ab * t);
			float crest = plan.From.y - Mathf.Max(0f, off - HalfWidth) * BankGrade;
			return Mathf.Max(height, crest);
		}

		void IPointOfInterestShaper.Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			float reach = PointOfInterestRules.Default().RuleFor(site.Kind)?.TidalIslandMetres ?? 150f;
			if (!TryPlan(context.GroundAt, CaveShaper.SeaLevel, site.Position.x, site.Position.z, site.Yaw, reach + 40f, out Plan plan))
			{
				context.Notes?.Add($"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0}): no shallow crossing to the shore was found; it stands on its island with no causeway.");
				return;
			}
			Plan cut = plan;
			float grow = HalfWidth + (PointOfInterestPlanner.MaxCausewayDepth + Crest) / BankGrade + 1f;
			var area = Rect.MinMaxRect(Mathf.Min(plan.From.x, plan.To.x) - grow, Mathf.Min(plan.From.z, plan.To.z) - grow, Mathf.Max(plan.From.x, plan.To.x) + grow, Mathf.Max(plan.From.z, plan.To.z) + grow);
			PointOfInterestTerrain.EditHeights(context.Terrains, context.Tiles, area, (x, z, h) => CutHeight(cut, x, z, h));
			// Nothing is strewn on it: keep-outs along its line.
			float length = Vector3.Distance(plan.From, plan.To);
			for (float s = 0f; s <= length; s += 10f)
			{
				Vector3 p = Vector3.Lerp(plan.From, plan.To, s / Mathf.Max(1f, length));
				context.AddKeepOut(site, p.x, p.z, HalfWidth + 2f);
			}
			context.AddShape(new PointOfInterestShape
			{
				SiteId = site.Id,
				Shaper = ShaperName,
				Position = plan.From,
				Yaw = FantasyShaping.YawOf(new Vector2(plan.To.x - plan.From.x, plan.To.z - plan.From.z)),
				Size = new Vector3(2f * HalfWidth, Crest, length),
				Seed = site.SiteSeed,
				Values = plan.ToValues(),
			});
			// The abbey faces its causeway.
			site.Yaw = Mathf.Round(FantasyShaping.YawOf(new Vector2(plan.To.x - site.Position.x, plan.To.z - site.Position.z)));
		}

		void IPointOfInterestShaper.Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
			// The causeway is ground, laid at plan time.
		}
	}

	// ── Set against a face ─────────────────────────────────────────────

	/// <summary>
	/// Snaps a face kind's site onto its rock face (2026-10-27): the foot found along the planner's heading and the heading turned square
	/// to the face (DungeonEntranceShaper.FindFace), with no cut. A hanging monastery (its pieces carry their own beams into the rock
	/// and stilts to the foot) then stands where the face at its deck's height is <see cref="StructurePieces.HangingBackReach"/> behind
	/// it, and needs a face sheer enough to hang from (<see cref="SheerRise"/> over the 8 m behind its foot); a site whose face is not
	/// there is dropped. The cliff kinds whose pieces carry their own rock behind them (a rock-cut tomb, a cliff palace, carved kings)
	/// are set at the foot when the face is found and left where the planner stood them when it is not.
	/// </summary>
	public sealed class FaceSnapShaper : IPointOfInterestShaper
	{
		public static readonly FaceSnapShaper Instance = new FaceSnapShaper();
		/// <summary>The rise a hanging monastery's face needs over the 8 m behind its foot, metres (≈ 56°).</summary>
		public const float SheerRise = 12f;

		public bool Handles(POIType kind) => kind == POIType.HangingMonastery || Snaps(kind);

		/// <summary>The face kinds that are only set at their foot.</summary>
		public static bool Snaps(POIType kind) => kind == POIType.RockCutFacade || kind == POIType.CliffPalace || kind == POIType.CliffRelief;

		/// <summary>
		/// Where a site stands on its face (pure): at the foot, square to the face; for a hanging site, moved in or out along its
		/// heading so the face at <paramref name="deck"/> over the foot stands <paramref name="back"/> behind it. False with a reason.
		/// </summary>
		public static bool TrySnap(Func<float, float, float> ground, Vector3 start, float yaw, float deck, float back, float sheer, out Vector3 at, out float heading, out string problem)
		{
			at = start;
			heading = yaw;
			if (!DungeonEntranceShaper.FindFace(ground, start, yaw, out DungeonEntranceShaper.Plan face, out problem))
			{
				return false;
			}
			heading = face.Yaw;
			Vector2 o = FantasyShaping.Dir(heading);
			Vector2 foot = new Vector2(face.Threshold.x, face.Threshold.z) + o * DungeonEntranceShaper.Recess;
			float footHeight = ground(foot.x, foot.y);
			at = new Vector3(foot.x, footHeight, foot.y);
			if (deck <= 0f)
			{
				return true;
			}
			Vector2 eight = foot - o * 8f;
			if (ground(eight.x, eight.y) - footHeight < sheer)
			{
				problem = $"its face is not sheer enough to hang from ({ground(eight.x, eight.y) - footHeight:0.0} m over 8 m)";
				return false;
			}
			// The face at the deck's height: marched back from the foot.
			for (float t = 0f; t <= 20f; t += 0.25f)
			{
				Vector2 p = foot - o * t;
				if (ground(p.x, p.y) >= footHeight + deck)
				{
					// To a centimetre or so between the last step and this one.
					float lo = Mathf.Max(0f, t - 0.25f), hi = t;
					for (int i = 0; i < 8; i++)
					{
						float m = 0.5f * (lo + hi);
						Vector2 q = foot - o * m;
						if (ground(q.x, q.y) >= footHeight + deck)
						{
							hi = m;
						}
						else
						{
							lo = m;
						}
					}
					p = foot - o * hi;
					Vector2 origin = p + o * back;
					at = new Vector3(origin.x, footHeight, origin.y);
					return true;
				}
			}
			problem = $"its face does not reach {deck:0} m over its foot";
			return false;
		}

		void IPointOfInterestShaper.Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			bool hanging = site.Kind == POIType.HangingMonastery;
			if (!TrySnap(context.GroundAt, site.Position, site.Yaw, hanging ? StructurePieces.HangingDeckHeight : 0f, StructurePieces.HangingBackReach, SheerRise,
				out Vector3 at, out float heading, out string problem))
			{
				if (hanging)
				{
					context.Reject(site, problem);
				}
				return;
			}
			site.Position = at;
			site.Yaw = Mathf.Round(heading);
		}

		void IPointOfInterestShaper.Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
		}
	}
}
#endif
