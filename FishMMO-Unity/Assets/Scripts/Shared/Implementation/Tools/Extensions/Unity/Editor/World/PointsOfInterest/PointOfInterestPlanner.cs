#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Decides a scene's points of interest from its ground: finds the ones the data already holds (falls, lakes,
	/// peaks, a biome's heart) and sites the ones a budget asks for (villages, caves, shrines) on a scored grid.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Pure.</b> It reads only <see cref="PointOfInterestPlanInput"/>: functions of (east, north) and plain values.
	/// No scene, no terrain, no asset, no editor call, so the same input plans the same records on any machine and a
	/// test can plan a scene drawn by hand. The generator (<see cref="PointOfInterestGenerator"/>) feeds it the
	/// finished ground and does everything that touches Unity.
	/// </para>
	/// <para>
	/// <b>Detected kinds</b> are marked once each where the data puts them, thinned by their spacing and capped. They
	/// change nothing: no pad, no keep-out.
	/// </para>
	/// <para>
	/// <b>Placed kinds</b> are sited tier by tier (<see cref="PointOfInterestRules.TierOf"/>): the capital, the places
	/// people live and hold, the dungeons and caves, everything else, the resources. Each kind scores every cell of a
	/// 16 m grid it may stand on (biome weight, flatness, its own rule's bonus, a little seeded noise to break ties
	/// between equal ground), and takes the best that keep its spacing from its own kind and a clear gap from every
	/// site already placed: a best-first Poisson disc. Its count is its budget per km² × the scene's area × density ×
	/// multiplier, rounded by a seeded draw (so a rare kind turns up in some scenes rather than none) and clamped.
	/// </para>
	/// <para>
	/// <b>Stable.</b> Each kind draws from its own stream (seed, kind), so a kind's sites do not move because another
	/// kind was added after it; ids are a hash of the kind and its 16 m cell, never a list index.
	/// </para>
	/// </remarks>
	public static partial class PointOfInterestPlanner
	{
		/// <summary>Ground steeper than this (on the 16 m grid) counts as a face for the face kinds' quick test, degrees.</summary>
		private const float FaceCellDegrees = 30f;

		/// <summary>A face kind's site is taken only where <see cref="FaceScore"/> reaches this.</summary>
		public const float MinFaceScore = 0.25f;

		public static PointOfInterestPlan Plan(PointOfInterestPlanInput input)
		{
			if (input == null)
			{
				throw new ArgumentNullException(nameof(input));
			}
			if (input.Ground == null)
			{
				throw new ArgumentException("The planner needs the ground.", nameof(input));
			}
			input.Rules ??= PointOfInterestRules.Default();
			input.Settings ??= new PointOfInterestPlanSettings();
			var plan = new PointOfInterestPlan { Seed = input.Seed };
			var state = new State(input, plan);

			foreach (PointOfInterestKindRule rule in input.Rules.Ordered(PointOfInterestPlacement.Detected))
			{
				PointOfInterestKindRule effective = state.Effective(rule, true);
				if (effective != null)
				{
					state.Detect(effective);
				}
			}

			if (!input.Settings.NaturalOnly)
			{
				foreach (PointOfInterestKindRule rule in input.Rules.Ordered(PointOfInterestPlacement.Placed))
				{
					PointOfInterestKindRule effective = state.Effective(rule, false);
					if (effective != null)
					{
						state.Place(effective);
					}
				}
			}
			else
			{
				plan.Notes.Add("Natural only: the detected kinds were marked and nothing was placed.");
			}
			return plan;
		}

		// ── Hashing ─────────────────────────────────────────────────

		internal static uint Mix(uint h)
		{
			h ^= h >> 16;
			h *= 0x85EBCA6Bu;
			h ^= h >> 13;
			h *= 0xC2B2AE35u;
			h ^= h >> 16;
			return h;
		}

		internal static uint Hash(int seed, int a, int b, int c)
			=> Mix(unchecked((uint)seed ^ Mix((uint)a * 0x9E3779B1u ^ Mix((uint)b * 0x85EBCA6Bu ^ Mix((uint)c + 0x27D4EB2Fu)))));

		internal static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

		/// <summary>A record's id: the kind and its 16 m cell, hashed; positive and never 0.</summary>
		public static int StableId(POIType kind, float x, float z)
		{
			int cx = Mathf.RoundToInt(x / 16f), cz = Mathf.RoundToInt(z / 16f);
			int id = (int)(Hash(0x504F49, (int)kind, cx, cz) & 0x7FFFFFFFu);
			return id == 0 ? 1 : id;
		}

		/// <summary>The seed every built choice for a site is drawn from.</summary>
		public static int SiteSeed(int sceneSeed, int id) => (int)(Hash(sceneSeed, id, 0x5173, 0) & 0x7FFFFFFFu);

		/// <summary>Degrees about +y of a direction in the ground plane: 0 = +z (north), 90 = +x (east).</summary>
		public static float YawOf(float dx, float dz) => Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f);

		// ── The face score ──────────────────────────────────────────

		/// <summary>
		/// How good a cave mouth, mine adit or overhang (x, z) would be: 0 … 1, high where a steep face of hard rock
		/// rises behind standable ground. <paramref name="yaw"/> is the direction the site faces, out of the face (the
		/// face lies behind it, at yaw + 180°).
		/// </summary>
		/// <remarks>
		/// <para>
		/// Sixteen headings are tried. Along each, the face must rise at least 12 m within 16 m of the foot (≈37° or
		/// steeper on average) and 4 m within the first 8 m (so a gentle slope that steepens far back is not a face);
		/// its height counts up to 25 m. The foot must be standable: the ground within 3 m no steeper than 28°, and the
		/// ground 6 m out in front within 3.5 m of the foot's height. Rock softer than <paramref name="minHardness"/>
		/// scores half and less.
		/// </para>
		/// <para>Public so the cave and overhang shapers can score a site they move.</para>
		/// </remarks>
		public static float FaceScore(Func<float, float, float> ground, Func<float, float, float, float> hardness, float x, float z, float minHardness,
			out float yaw)
		{
			yaw = 0f;
			float h0 = ground(x, z);
			float ex = ground(x + 3f, z) - ground(x - 3f, z), ez = ground(x, z + 3f) - ground(x, z - 3f);
			float footSlope = Mathf.Atan(Mathf.Sqrt(ex * ex + ez * ez) / 6f) * Mathf.Rad2Deg;
			if (footSlope > 28f)
			{
				return 0f;
			}
			float best = 0f;
			for (int k = 0; k < 16; k++)
			{
				float a = k * (Mathf.PI / 8f);
				float dx = Mathf.Sin(a), dz = Mathf.Cos(a);
				float front = ground(x - dx * 6f, z - dz * 6f);
				if (Mathf.Abs(front - h0) > 3.5f)
				{
					continue;
				}
				float rise8 = ground(x + dx * 8f, z + dz * 8f) - h0;
				float rise16 = ground(x + dx * 16f, z + dz * 16f) - h0;
				if (rise8 < 4f || rise16 < 12f)
				{
					continue;
				}
				float height = rise16;
				for (float t = 20f; t <= 32f; t += 4f)
				{
					height = Mathf.Max(height, ground(x + dx * t, z + dz * t) - h0);
				}
				float score = Mathf.Clamp01((rise16 - 8f) / 16f) * Mathf.Clamp01(height / 25f);
				if (minHardness > 0f && hardness != null)
				{
					float hard = hardness(x + dx * 10f, z + dz * 10f, h0 + rise8);
					if (hard < minHardness)
					{
						score *= 0.5f * hard / minHardness;
					}
				}
				if (score > best)
				{
					best = score;
					yaw = YawOf(-dx, -dz);
				}
			}
			return best;
		}

		// ── The grid ────────────────────────────────────────────────

		/// <summary>The scene sampled once on the scoring grid: everything a cell's score reads.</summary>
		internal sealed class Grid
		{
			public readonly int Width, Depth;
			public readonly float Cell, OriginX, OriginZ;
			public readonly float[] Height, Slope, WaterDistance, SeaDistance, Hardness, Plateau;
			public readonly WaterKind[] Water;
			public readonly int[] Biome;
			public readonly bool[] Blocked, Sea;
			public readonly float Lowest, Highest;

			public Grid(PointOfInterestPlanInput input)
			{
				Cell = Mathf.Max(4f, input.CellMetres);
				Width = Mathf.Max(1, Mathf.FloorToInt(input.WidthMetres / Cell));
				Depth = Mathf.Max(1, Mathf.FloorToInt(input.DepthMetres / Cell));
				OriginX = -0.5f * Width * Cell + 0.5f * Cell;
				OriginZ = -0.5f * Depth * Cell + 0.5f * Cell;
				int n = Width * Depth;
				Height = new float[n];
				Slope = new float[n];
				WaterDistance = new float[n];
				SeaDistance = new float[n];
				Hardness = new float[n];
				Plateau = new float[n];
				Water = new WaterKind[n];
				Biome = new int[n];
				Blocked = new bool[n];
				Sea = new bool[n];
				Lowest = float.PositiveInfinity;
				Highest = float.NegativeInfinity;
				PointOfInterestWater water = input.Water;
				for (int z = 0; z < Depth; z++)
				{
					float north = NorthOf(z);
					for (int x = 0; x < Width; x++)
					{
						float east = EastOf(x);
						int i = z * Width + x;
						float h = input.Ground(east, north);
						Height[i] = h;
						Lowest = Mathf.Min(Lowest, h);
						Highest = Mathf.Max(Highest, h);
						Water[i] = water?.KindAt != null ? water.KindAt(east, north) : WaterKind.None;
						Biome[i] = input.BiomeAt != null ? input.BiomeAt(east, north) : -1;
						Hardness[i] = input.HardnessAt != null ? input.HardnessAt(east, north, h) : 0.5f;
						Plateau[i] = input.PlateauAt != null ? input.PlateauAt(east, north) : 0f;
						Blocked[i] = input.Blocked != null && input.Blocked(east, north);
						Sea[i] = input.HasSea && h < input.SeaLevel;
					}
				}
				for (int z = 0; z < Depth; z++)
				{
					for (int x = 0; x < Width; x++)
					{
						int i = z * Width + x;
						float gx = (Height[z * Width + Math.Min(Width - 1, x + 1)] - Height[z * Width + Math.Max(0, x - 1)]) / (Cell * (x > 0 && x < Width - 1 ? 2f : 1f));
						float gz = (Height[Math.Min(Depth - 1, z + 1) * Width + x] - Height[Math.Max(0, z - 1) * Width + x]) / (Cell * (z > 0 && z < Depth - 1 ? 2f : 1f));
						Slope[i] = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
					}
				}
				// Distance to inland water: the water's own field when it has one, else measured here from the cells.
				if (water?.DistanceAt != null)
				{
					for (int z = 0; z < Depth; z++)
					{
						for (int x = 0; x < Width; x++)
						{
							WaterDistance[z * Width + x] = water.DistanceAt(EastOf(x), NorthOf(z));
						}
					}
				}
				else
				{
					Chamfer(WaterDistance, i => Water[i] == WaterKind.River || Water[i] == WaterKind.Lake);
				}
				Chamfer(SeaDistance, i => Sea[i]);
			}

			public float EastOf(int x) => OriginX + x * Cell;
			public float NorthOf(int z) => OriginZ + z * Cell;

			/// <summary>The cell holding (east, north), clamped to the grid.</summary>
			public int CellAt(float east, float north)
			{
				int x = Mathf.Clamp(Mathf.RoundToInt((east - OriginX) / Cell), 0, Width - 1);
				int z = Mathf.Clamp(Mathf.RoundToInt((north - OriginZ) / Cell), 0, Depth - 1);
				return z * Width + x;
			}

			/// <summary>Bilinear over a per-cell field.</summary>
			public float Sample(float[] field, float east, float north)
			{
				float gx = (east - OriginX) / Cell, gz = (north - OriginZ) / Cell;
				int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, Math.Max(0, Width - 2)), z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, Math.Max(0, Depth - 2));
				int x1 = Math.Min(Width - 1, x0 + 1), z1 = Math.Min(Depth - 1, z0 + 1);
				float fx = Mathf.Clamp01(gx - x0), fz = Mathf.Clamp01(gz - z0);
				float south = Mathf.Lerp(field[z0 * Width + x0], field[z0 * Width + x1], fx);
				float northRow = Mathf.Lerp(field[z1 * Width + x0], field[z1 * Width + x1], fx);
				return Mathf.Lerp(south, northRow, fz);
			}

			/// <summary>Chamfer distance, metres, from every cell <paramref name="source"/> holds; far where none does.</summary>
			private void Chamfer(float[] distance, Func<int, bool> source)
			{
				const float far = 1e6f;
				for (int i = 0; i < distance.Length; i++)
				{
					distance[i] = source(i) ? 0f : far;
				}
				float straight = Cell, diagonal = Cell * 1.41421356f;
				for (int z = 0; z < Depth; z++)
				{
					for (int x = 0; x < Width; x++)
					{
						int i = z * Width + x;
						float d = distance[i];
						if (x > 0) d = Mathf.Min(d, distance[i - 1] + straight);
						if (z > 0) d = Mathf.Min(d, distance[i - Width] + straight);
						if (x > 0 && z > 0) d = Mathf.Min(d, distance[i - Width - 1] + diagonal);
						if (x < Width - 1 && z > 0) d = Mathf.Min(d, distance[i - Width + 1] + diagonal);
						distance[i] = d;
					}
				}
				for (int z = Depth - 1; z >= 0; z--)
				{
					for (int x = Width - 1; x >= 0; x--)
					{
						int i = z * Width + x;
						float d = distance[i];
						if (x < Width - 1) d = Mathf.Min(d, distance[i + 1] + straight);
						if (z < Depth - 1) d = Mathf.Min(d, distance[i + Width] + straight);
						if (x < Width - 1 && z < Depth - 1) d = Mathf.Min(d, distance[i + Width + 1] + diagonal);
						if (x > 0 && z < Depth - 1) d = Mathf.Min(d, distance[i + Width - 1] + diagonal);
						distance[i] = d;
					}
				}
			}
		}

		/// <summary>A candidate site before it is accepted.</summary>
		internal struct Site
		{
			public float X, Z, Y, Yaw, Score, Radius;
			public int Cell;
			public int RiverId, PlanetRiver, LakeId, ParentId;

			public static Site At(float x, float z, float y, float score)
				=> new Site { X = x, Z = z, Y = y, Score = score, RiverId = -1, PlanetRiver = -1, LakeId = -1, ParentId = -1, Cell = -1 };
		}

		// ── The planning state ──────────────────────────────────────

		internal sealed partial class State
		{
			public readonly PointOfInterestPlanInput Input;
			public readonly PointOfInterestPlan Plan;
			public readonly Grid G;
			private readonly HashSet<int> ids = new HashSet<int>();
			/// <summary>Every placed site so far: what a new one keeps its gap from.</summary>
			private readonly List<PointOfInterestRecord> placed = new List<PointOfInterestRecord>();
			private readonly List<PointOfInterestRecord> settlements = new List<PointOfInterestRecord>();
			private readonly float areaKm2;

			public State(PointOfInterestPlanInput input, PointOfInterestPlan plan)
			{
				Input = input;
				Plan = plan;
				G = new Grid(input);
				areaKm2 = input.WidthMetres * input.DepthMetres / 1e6f;
			}

			/// <summary>A rule with the scene's override applied; null when the scene turned the kind off.</summary>
			public PointOfInterestKindRule Effective(PointOfInterestKindRule rule, bool detected)
			{
				PointOfInterestKindOverride over = Input.Settings.OverrideFor(rule.Kind);
				if (over == null)
				{
					return rule;
				}
				if (!over.Enabled)
				{
					return null;
				}
				PointOfInterestKindRule copy = rule.Clone();
				if (!detected && over.PerKm2 >= 0f)
				{
					copy.PerKm2 = over.PerKm2;
				}
				if (over.Min >= 0)
				{
					copy.Min = over.Min;
				}
				if (over.Max >= 0)
				{
					copy.Max = over.Max;
				}
				if (over.SpacingMetres >= 0f)
				{
					copy.SpacingMetres = over.SpacingMetres;
				}
				return copy;
			}

			public bool Builds(POIType kind) => Input.Builds == null || Input.Builds(kind);

			public string BiomeName(int cell)
			{
				int b = G.Biome[cell];
				return b >= 0 && b < Input.Biomes.Count ? Input.Biomes[b]?.Name : null;
			}

			public int BiomeId(int cell)
			{
				int b = G.Biome[cell];
				return b >= 0 && b < Input.Biomes.Count && Input.Biomes[b] != null ? Input.Biomes[b].Id : 0;
			}

			public float BiomeWeight(PointOfInterestKindRule rule, int cell) => Mathf.Max(0f, rule.BiomeWeight(BiomeName(cell)));

			public bool Inside(float x, float z, float margin)
				=> Mathf.Abs(x) <= Input.WidthMetres * 0.5f - margin && Mathf.Abs(z) <= Input.DepthMetres * 0.5f - margin;

			/// <summary>Whether land at (x, z) stands clear of every water: no river, lake or wash, and above the sea's reach.</summary>
			public bool DryLand(float x, float z, float height)
			{
				WaterKind kind = Input.Water?.KindAt != null ? Input.Water.KindAt(x, z) : WaterKind.None;
				if (kind != WaterKind.None && kind != WaterKind.Bank)
				{
					return false;
				}
				return !Input.HasSea || height >= Input.SeaLevel + Input.LandClearMetres;
			}

			// ── Records ─────────────────────────────────────────────

			/// <summary>Turns an accepted site into a record, its id unique in the scene.</summary>
			public PointOfInterestRecord Add(PointOfInterestKindRule rule, in Site site, bool isPlaced)
			{
				int id = StableId(rule.Kind, site.X, site.Z);
				while (!ids.Add(id))
				{
					id = id == int.MaxValue ? 1 : id + 1;
				}
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(rule.Kind);
				int cell = G.CellAt(site.X, site.Z);
				float t = rule.FootprintMax > rule.FootprintMin ? Mathf.InverseLerp(rule.FootprintMin, rule.FootprintMax, site.Radius) : 0.5f;
				var record = new PointOfInterestRecord
				{
					Id = id,
					Kind = rule.Kind,
					Position = new Vector3(Round(site.X), Round(site.Y), Round(site.Z)),
					Yaw = Mathf.Round(site.Yaw),
					Radius = Round(site.Radius),
					SizeClass = (byte)(rule.Kind == POIType.Capital ? 2 : t < 0.34f ? 0 : t < 0.67f ? 1 : 2),
					BiomeID = BiomeId(cell),
					DetailTier = info.DetailTier,
					RequiresDiscovery = true,
					ParentId = site.ParentId,
					RiverId = site.RiverId,
					PlanetRiver = site.PlanetRiver,
					LakeId = site.LakeId,
					Template = string.Empty,
					Race = string.Empty,
					Name = string.Empty,
					Description = string.Empty,
					SiteSeed = SiteSeed(Input.Seed, id),
					UnlockIndex = -1,
				};
				Plan.Records.Add(record);
				if (isPlaced)
				{
					placed.Add(record);
					if (info.Has(PointOfInterestTraits.Settlement) || rule.Kind == POIType.Capital || rule.Kind == POIType.City)
					{
						settlements.Add(record);
					}
				}
				return record;
			}

			/// <summary>Centimetres: what is stored is what was compared, on any machine.</summary>
			private static float Round(float v) => Mathf.Round(v * 100f) / 100f;

			/// <summary>Detected sites in score order, thinned by spacing and capped.</summary>
			public void Accept(PointOfInterestKindRule rule, List<Site> sites)
			{
				// Highest score first; ties by position so the order never rests on how the list was gathered.
				sites.Sort((a, b) =>
				{
					int c = b.Score.CompareTo(a.Score);
					if (c != 0) return c;
					c = a.X.CompareTo(b.X);
					return c != 0 ? c : a.Z.CompareTo(b.Z);
				});
				var taken = new List<Site>();
				float margin = Mathf.Min(Input.EdgeMarginMetres, 24f);
				foreach (Site site in sites)
				{
					if (taken.Count >= rule.Max)
					{
						break;
					}
					if (!Inside(site.X, site.Z, margin))
					{
						continue;
					}
					bool clear = true;
					foreach (Site other in taken)
					{
						float dx = other.X - site.X, dz = other.Z - site.Z;
						if (dx * dx + dz * dz < rule.SpacingMetres * rule.SpacingMetres)
						{
							clear = false;
							break;
						}
					}
					if (!clear)
					{
						continue;
					}
					taken.Add(site);
					Site marked = site;
					if (marked.Radius <= 0f)
					{
						marked.Radius = Mathf.Max(rule.FootprintMin, 10f);
					}
					Add(rule, marked, false);
				}
			}
		}
	}
}
#endif
