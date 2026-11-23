using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Decides where a scene's housing lots go: districts beside its cities and capital, and small
	/// groups of countryside lots well away from every point of interest. Pure: the same input gives
	/// the same lots, so a cut is repeatable.
	/// </summary>
	/// <remarks>
	/// <para><b>Owned land never moves.</b> Lots a previous cut laid (<see cref="Input.Kept"/>, from the
	/// scene's <see cref="ScenePlotManifest"/>) are laid again exactly, first, and new lots only go
	/// where they do not touch them. A settlement that already has a district, and a countryside cell
	/// that already has lots, gets no more, so a re-cut on unchanged inputs lays nothing new.</para>
	///
	/// <para><b>Flat is measured, not assumed.</b> A lot is sampled every <see cref="Input.SampleStep"/>
	/// metres over its footprint and a margin; it fits when nothing there is blocked (a site, a road,
	/// water, a cliff hole, the sea) and the ground varies by no more than <see cref="Input.MaxRelief"/>.
	/// The cut levels what it accepts; the tool for an already-cut scene cannot, and asks for flatter
	/// ground instead.</para>
	///
	/// <para>Lots are axis-aligned squares because the plot volume is (<c>PlotFoundation.Bounds</c>, Housing module).
	/// A district is a lane with a row of lots on each side, each lot's deed post on the lane.</para>
	/// </remarks>
	public static class HousingPlotPlanner
	{
		/// <summary>The lot sizes, metres.</summary>
		public static readonly float[] Sizes = { 16f, 24f, 32f };

		/// <summary>What each size costs before the district multiplier.</summary>
		public static readonly long[] BasePrices = { 1500, 3500, 6000 };

		private static readonly float[] DistrictWeights = { 0.45f, 0.35f, 0.2f };
		private static readonly float[] CountryWeights = { 0.3f, 0.4f, 0.3f };

		/// <summary>Space kept between neighbouring lots, metres.</summary>
		public const float Gap = 2f;

		/// <summary>Half the width of a district's lane, metres.</summary>
		public const float LaneHalf = 3f;

		/// <summary>How far every lot keeps from the scene's edge, metres.</summary>
		public const float EdgeMargin = 48f;

		/// <summary>The fewest lots a district may have; a spot that fits fewer is not a district.</summary>
		public const int MinDistrictLots = 6;

		/// <summary>The most lots one run of a district's lane (one side, one direction) takes.</summary>
		public const int LotsPerRun = 6;

		/// <summary>
		/// How far a district stands from its settlement's edge, metres (tried in order). 150 and 220: Moron 1's
		/// capital stands on the coast, and every spot within 100 m of it was sea or shore, so it had no district at all.
		/// </summary>
		public static readonly float[] RingMetres = { 30f, 60f, 100f, 150f, 220f };

		/// <summary>Angles tried round a settlement.</summary>
		public const int RingAngles = 16;

		/// <summary>Countryside cell size, metres: each cell may hold one small group.</summary>
		public const float CountryCell = 420f;

		/// <summary>The share of countryside cells that get a group.</summary>
		public const float CountryChance = 0.35f;

		/// <summary>
		/// Spots tried in a chosen cell before it is given up: one spot was too few, as a coast or a wooded slope
		/// (Moron 1) refused every cell's single try and the scene got no lot at all.
		/// </summary>
		public const int CountryTries = 16;

		/// <summary>How far countryside lots keep from any point of interest's edge, metres.</summary>
		public const float CountryFromSites = 160f;

		/// <summary>How far countryside lots keep from a settlement's edge, metres.</summary>
		public const float CountryFromSettlements = 320f;

		/// <summary>The most countryside lots one scene gets.</summary>
		public const int MaxCountryLots = 48;

		/// <summary>A settlement whose edge has a kept district lot within this, metres, keeps that district and gets no new one.</summary>
		public const float KeptDistrictReach = 260f;

		/// <summary>A settlement that may have districts.</summary>
		public struct Settlement
		{
			public int Id;
			public string Name;
			public float X, Z, Radius;
			/// <summary>A capital gets two districts, a city one.</summary>
			public bool Capital;
		}

		/// <summary>A disc on the ground.</summary>
		public struct Disc
		{
			public float X, Z, Radius;
		}

		/// <summary>Everything the planner reads.</summary>
		public sealed class Input
		{
			public int Seed;
			/// <summary>Half the scene's extent on X and Z, metres; the scene is centred on the origin.</summary>
			public float HalfWidth, HalfDepth;
			/// <summary>Ground height at (east, north).</summary>
			public Func<float, float, float> GroundAt;
			/// <summary>True where no lot may stand (sites, roads, water, holes, sea).</summary>
			public Func<float, float, bool> Blocked;
			public List<Settlement> Settlements = new List<Settlement>();
			/// <summary>Every point of interest, settlements included.</summary>
			public List<Disc> Sites = new List<Disc>();
			/// <summary>Lots a previous cut laid; laid again first, never moved.</summary>
			public List<ScenePlotManifest.Lot> Kept = new List<ScenePlotManifest.Lot>();
			/// <summary>
			/// How far the ground may vary under a lot (and its margin), metres. 4 (was 2): the cut levels every lot to
			/// its post, so a gentler limit only refused ground the levelling handles; Moron 1 had no lot at all by its capital.
			/// </summary>
			public float MaxRelief = 4f;
			public float SampleStep = 4f;
			/// <summary>Margin sampled round a footprint, metres.</summary>
			public float Margin = 2f;
			public bool Districts = true;
			public bool Countryside = true;
		}

		/// <summary>What the planner laid.</summary>
		public sealed class Result
		{
			/// <summary>Every lot: kept ones first, in their order, then new ones.</summary>
			public readonly List<ScenePlotManifest.Lot> Lots = new List<ScenePlotManifest.Lot>();
			/// <summary>Ground to keep trees and rocks off: each lot and each district lane.</summary>
			public readonly List<Disc> Clear = new List<Disc>();
			public int Kept, NewDistricts, NewDistrictLots, NewCountryLots;
		}

		public static Result Plan(Input input)
		{
			var result = new Result();
			var occupied = new List<ScenePlotManifest.Lot>();
			foreach (ScenePlotManifest.Lot lot in input.Kept)
			{
				Add(result, occupied, lot);
				result.Kept++;
			}

			if (input.Districts && input.GroundAt != null)
			{
				var settlements = new List<Settlement>(input.Settlements);
				settlements.Sort((a, b) => a.Id.CompareTo(b.Id));
				foreach (Settlement settlement in settlements)
				{
					PlanDistricts(input, settlement, result, occupied);
				}
			}
			if (input.Countryside && input.GroundAt != null)
			{
				PlanCountryside(input, result, occupied);
			}
			return result;
		}

		// ── Districts ──────────────────────────────────────────────

		private static void PlanDistricts(Input input, Settlement settlement, Result result, List<ScenePlotManifest.Lot> occupied)
		{
			foreach (ScenePlotManifest.Lot kept in input.Kept)
			{
				if (kept.District && Distance(kept.Centre.x, kept.Centre.z, settlement.X, settlement.Z) - settlement.Radius < KeptDistrictReach)
				{
					return;
				}
			}

			var rng = new DeterministicRNG(Hash(input.Seed, settlement.Id, 0x44495354));
			var sizes = new float[LotsPerRun * 4];
			for (int i = 0; i < sizes.Length; i++)
			{
				sizes[i] = Pick(rng, DistrictWeights);
			}
			float startAngle = rng.NextFloat() * Mathf.PI * 2f;
			int wanted = settlement.Capital ? 2 : 1;
			var centres = new List<Vector2>();
			for (int district = 0; district < wanted; district++)
			{
				List<ScenePlotManifest.Lot> best = null;
				Vector2 bestCentre = default;
				int bestAxis = 0;
				foreach (float ring in RingMetres)
				{
					for (int a = 0; a < RingAngles; a++)
					{
						float angle = startAngle + a * (Mathf.PI * 2f / RingAngles);
						float distance = settlement.Radius + ring;
						var centre = new Vector2(settlement.X + Mathf.Cos(angle) * distance, settlement.Z + Mathf.Sin(angle) * distance);
						if (FarFrom(centres, centre, 140f) == false)
						{
							continue;
						}
						for (int axis = 0; axis < 2; axis++)
						{
							List<ScenePlotManifest.Lot> laid = LayDistrict(input, centre, axis, sizes, occupied, settlement);
							if (laid.Count > (best?.Count ?? 0))
							{
								best = laid;
								bestCentre = centre;
								bestAxis = axis;
							}
						}
					}
				}
				if (best == null || best.Count < MinDistrictLots)
				{
					break;
				}
				string group = $"{(string.IsNullOrEmpty(settlement.Name) ? "Settlement " + settlement.Id : settlement.Name)} housing{(district > 0 ? " " + (district + 1) : "")}";
				float minAlong = float.MaxValue, maxAlong = float.MinValue;
				foreach (ScenePlotManifest.Lot lot in best)
				{
					ScenePlotManifest.Lot named = lot;
					named.Group = group;
					Add(result, occupied, named);
					float along = bestAxis == 0 ? lot.Centre.x : lot.Centre.z;
					minAlong = Mathf.Min(minAlong, along - lot.Size.x * 0.5f);
					maxAlong = Mathf.Max(maxAlong, along + lot.Size.x * 0.5f);
				}
				// The lane between the rows is kept clear of trees too.
				for (float along = minAlong; along <= maxAlong; along += LaneHalf * 2f)
				{
					result.Clear.Add(bestAxis == 0
						? new Disc { X = along, Z = bestCentre.y, Radius = LaneHalf + 1f }
						: new Disc { X = bestCentre.x, Z = along, Radius = LaneHalf + 1f });
				}
				centres.Add(bestCentre);
				result.NewDistricts++;
				result.NewDistrictLots += best.Count;
			}
		}

		/// <summary>
		/// A lane through <paramref name="centre"/> along X (axis 0) or Z (axis 1), filled outward in
		/// four runs (each side, each direction) from the size sequence.
		/// </summary>
		private static List<ScenePlotManifest.Lot> LayDistrict(Input input, Vector2 centre, int axis, float[] sizes, List<ScenePlotManifest.Lot> occupied, Settlement settlement)
		{
			var laid = new List<ScenePlotManifest.Lot>();
			var trial = new List<ScenePlotManifest.Lot>(occupied);
			int run = 0;
			for (int side = 1; side >= -1; side -= 2)
			{
				for (int direction = 1; direction >= -1; direction -= 2, run++)
				{
					float cursor = Gap * 0.5f;
					int failures = 0, count = 0, next = run * LotsPerRun;
					while (count < LotsPerRun && failures < 3 && cursor < 120f)
					{
						float size = sizes[next % sizes.Length];
						if (TryDistrictLot(input, centre, axis, side, direction, cursor, size, trial, settlement, out ScenePlotManifest.Lot lot)
							|| (size > Sizes[0] && TryDistrictLot(input, centre, axis, side, direction, cursor, Sizes[0], trial, settlement, out lot)))
						{
							laid.Add(lot);
							trial.Add(lot);
							cursor += lot.Size.x + Gap;
							count++;
							next++;
						}
						else
						{
							cursor += 4f;
							failures++;
						}
					}
				}
			}
			return laid;
		}

		private static bool TryDistrictLot(Input input, Vector2 centre, int axis, int side, int direction, float cursor, float size,
			List<ScenePlotManifest.Lot> occupied, Settlement settlement, out ScenePlotManifest.Lot lot)
		{
			float along = direction * (cursor + size * 0.5f);
			float across = side * (LaneHalf + size * 0.5f);
			float x = centre.x + (axis == 0 ? along : across);
			float z = centre.y + (axis == 0 ? across : along);
			// The post stands on the lane: the edge facing back across it.
			int front = axis == 0 ? (side > 0 ? 3 : 1) : (side > 0 ? 2 : 0);
			long price = PriceOf(size, settlement.Capital ? 1.5f : 1f);
			return TryLot(input, x, z, size, front, price, true, occupied, out lot);
		}

		// ── Countryside ────────────────────────────────────────────

		private static void PlanCountryside(Input input, Result result, List<ScenePlotManifest.Lot> occupied)
		{
			int cellsX = Mathf.Max(1, Mathf.FloorToInt(input.HalfWidth * 2f / CountryCell));
			int cellsZ = Mathf.Max(1, Mathf.FloorToInt(input.HalfDepth * 2f / CountryCell));
			int laidTotal = 0;
			for (int cz = 0; cz < cellsZ; cz++)
			{
				for (int cx = 0; cx < cellsX; cx++)
				{
					if (laidTotal >= MaxCountryLots)
					{
						return;
					}
					float x0 = -input.HalfWidth + cx * CountryCell, z0 = -input.HalfDepth + cz * CountryCell;
					if (HasKeptCountryLot(input, x0, z0))
					{
						continue;
					}
					var rng = new DeterministicRNG(Hash(input.Seed, cx * 7919 + cz, 0x434F554E));
					if (rng.NextFloat() > CountryChance)
					{
						continue;
					}
					int wanted = 1 + rng.Next(3);
					int axis = rng.Next(2);
					int side = rng.Next(2) == 0 ? 1 : -1;
					var sizes = new float[wanted];
					for (int i = 0; i < wanted; i++)
					{
						sizes[i] = Pick(rng, CountryWeights);
					}

					// The first spot in the cell whose first lot fits holds the group (the spots are drawn from the cell's own seed).
					string group = $"Countryside {cx},{cz}";
					int laid = 0;
					for (int attempt = 0; attempt < CountryTries && laid == 0; attempt++)
					{
						float px = x0 + 40f + rng.NextFloat() * (CountryCell - 80f);
						float pz = z0 + 40f + rng.NextFloat() * (CountryCell - 80f);
						if (!AwayFromSites(input, px, pz) || NearDistrict(occupied, px, pz, 150f))
						{
							continue;
						}
						float cursor = 0f;
						for (int i = 0; i < wanted && laidTotal + laid < MaxCountryLots; i++)
						{
							float size = sizes[i];
							float along = cursor + size * 0.5f, across = side * size * 0.5f;
							float x = px + (axis == 0 ? along : across), z = pz + (axis == 0 ? across : along);
							int front = axis == 0 ? (side > 0 ? 3 : 1) : (side > 0 ? 2 : 0);
							if (!AwayFromSites(input, x, z) || !TryLot(input, x, z, size, front, PriceOf(size, 0.8f), false, occupied, out ScenePlotManifest.Lot lot))
							{
								break;
							}
							lot.Group = group;
							Add(result, occupied, lot);
							cursor += size + Gap;
							laid++;
						}
					}
					laidTotal += laid;
					result.NewCountryLots += laid;
				}
			}
		}

		private static bool HasKeptCountryLot(Input input, float x0, float z0)
		{
			foreach (ScenePlotManifest.Lot kept in input.Kept)
			{
				if (!kept.District && kept.Centre.x >= x0 && kept.Centre.x < x0 + CountryCell && kept.Centre.z >= z0 && kept.Centre.z < z0 + CountryCell)
				{
					return true;
				}
			}
			return false;
		}

		private static bool AwayFromSites(Input input, float x, float z)
		{
			foreach (Disc site in input.Sites)
			{
				if (Distance(x, z, site.X, site.Z) - site.Radius < CountryFromSites)
				{
					return false;
				}
			}
			foreach (Settlement settlement in input.Settlements)
			{
				if (Distance(x, z, settlement.X, settlement.Z) - settlement.Radius < CountryFromSettlements)
				{
					return false;
				}
			}
			return true;
		}

		private static bool NearDistrict(List<ScenePlotManifest.Lot> occupied, float x, float z, float reach)
		{
			foreach (ScenePlotManifest.Lot lot in occupied)
			{
				if (lot.District && Distance(x, z, lot.Centre.x, lot.Centre.z) < reach)
				{
					return true;
				}
			}
			return false;
		}

		// ── One lot ────────────────────────────────────────────────

		/// <summary>
		/// Tests a square lot and, when it fits, makes it: its floor is the mean ground over the footprint.
		/// </summary>
		public static bool TryLot(Input input, float x, float z, float size, int front, long price, bool district,
			List<ScenePlotManifest.Lot> occupied, out ScenePlotManifest.Lot lot)
		{
			lot = default;
			float half = size * 0.5f;
			if (Mathf.Abs(x) + half + EdgeMargin > input.HalfWidth || Mathf.Abs(z) + half + EdgeMargin > input.HalfDepth)
			{
				return false;
			}
			foreach (ScenePlotManifest.Lot other in occupied)
			{
				float otherHalf = other.Size.x * 0.5f;
				if (Mathf.Abs(x - other.Centre.x) < half + otherHalf + Gap && Mathf.Abs(z - other.Centre.z) < half + other.Size.y * 0.5f + Gap)
				{
					return false;
				}
			}

			float reach = half + input.Margin;
			int steps = Mathf.Max(2, Mathf.CeilToInt(reach * 2f / Mathf.Max(0.5f, input.SampleStep)));
			float step = reach * 2f / steps;
			float min = float.MaxValue, max = float.MinValue, sum = 0f;
			int inside = 0;
			for (int iz = 0; iz <= steps; iz++)
			{
				float sz = z - reach + iz * step;
				for (int ix = 0; ix <= steps; ix++)
				{
					float sx = x - reach + ix * step;
					if (input.Blocked != null && input.Blocked(sx, sz))
					{
						return false;
					}
					float h = input.GroundAt(sx, sz);
					if (float.IsNaN(h) || float.IsInfinity(h))
					{
						return false;
					}
					min = Mathf.Min(min, h);
					max = Mathf.Max(max, h);
					if (Mathf.Abs(sx - x) <= half && Mathf.Abs(sz - z) <= half)
					{
						sum += h;
						inside++;
					}
				}
				if (max - min > input.MaxRelief)
				{
					return false;
				}
			}
			if (inside == 0)
			{
				return false;
			}

			float floor = Mathf.Round(sum / inside * 100f) / 100f;
			lot = new ScenePlotManifest.Lot
			{
				Key = KeyFor(x, z),
				Centre = new Vector3(x, floor, z),
				Size = new Vector2(size, size),
				Front = front,
				Price = price,
				District = district,
				Group = string.Empty,
			};
			return true;
		}

		/// <summary>Where a way should reach a group of lots.</summary>
		public struct SpurEnd
		{
			public Vector2 Position;
			public bool District;
			public string Group;
		}

		/// <summary>
		/// One way end per group of lots: a district's lane end nearer its settlement (just past the last lot,
		/// on the lane's line), a countryside group's front, a few metres out from its first lot's post.
		/// </summary>
		/// <remarks>
		/// Worked out from the lots alone, so kept lots from an older manifest get their ways back on a re-cut.
		/// A district's lane runs along X when its posts face ±Z (fronts 1 and 3), along Z otherwise.
		/// </remarks>
		public static List<SpurEnd> SpurEnds(IReadOnlyList<ScenePlotManifest.Lot> lots, IReadOnlyList<Settlement> settlements)
		{
			var ends = new List<SpurEnd>();
			var order = new List<string>();
			var groups = new Dictionary<string, List<ScenePlotManifest.Lot>>();
			foreach (ScenePlotManifest.Lot lot in lots)
			{
				string key = (lot.District ? "d:" : "c:") + (lot.Group ?? string.Empty);
				if (!groups.TryGetValue(key, out List<ScenePlotManifest.Lot> list))
				{
					list = new List<ScenePlotManifest.Lot>();
					groups[key] = list;
					order.Add(key);
				}
				list.Add(lot);
			}
			foreach (string key in order)
			{
				List<ScenePlotManifest.Lot> group = groups[key];
				ScenePlotManifest.Lot first = group[0];
				if (!first.District)
				{
					Vector3 dir = FrontDirection(first.Front);
					float reach = first.Size.x * 0.5f + 4f;
					ends.Add(new SpurEnd { Position = new Vector2(first.Centre.x + dir.x * reach, first.Centre.z + dir.z * reach), District = false, Group = first.Group });
					continue;
				}
				bool alongX = (first.Front & 1) == 1;
				float lane = 0f, min = float.MaxValue, max = float.MinValue, cx = 0f, cz = 0f;
				foreach (ScenePlotManifest.Lot lot in group)
				{
					float half = lot.Size.x * 0.5f;
					switch (lot.Front & 3)
					{
						case 0: lane += lot.Centre.x + half + LaneHalf; break;
						case 1: lane += lot.Centre.z + half + LaneHalf; break;
						case 2: lane += lot.Centre.x - half - LaneHalf; break;
						default: lane += lot.Centre.z - half - LaneHalf; break;
					}
					float along = alongX ? lot.Centre.x : lot.Centre.z;
					min = Mathf.Min(min, along - half);
					max = Mathf.Max(max, along + half);
					cx += lot.Centre.x;
					cz += lot.Centre.z;
				}
				lane /= group.Count;
				var centroid = new Vector2(cx / group.Count, cz / group.Count);
				Vector2 a = alongX ? new Vector2(min - 4f, lane) : new Vector2(lane, min - 4f);
				Vector2 b = alongX ? new Vector2(max + 4f, lane) : new Vector2(lane, max + 4f);
				Vector2 toward = centroid;
				float best = float.MaxValue;
				foreach (Settlement settlement in settlements)
				{
					float d = Distance(centroid.x, centroid.y, settlement.X, settlement.Z);
					if (d < best)
					{
						best = d;
						toward = new Vector2(settlement.X, settlement.Z);
					}
				}
				ends.Add(new SpurEnd { Position = (a - toward).sqrMagnitude <= (b - toward).sqrMagnitude ? a : b, District = true, Group = first.Group });
			}
			return ends;
		}

		/// <summary>The plot key of a lot centred at (x, z): its rounded position, unique since lots never overlap.</summary>
		public static string KeyFor(float x, float z)
			=> $"lot_{Mathf.RoundToInt(x)}_{Mathf.RoundToInt(z)}";

		/// <summary>The price of a lot of this size, times a multiplier, rounded to 50.</summary>
		public static long PriceOf(float size, float multiplier)
		{
			int index = 0;
			for (int i = 0; i < Sizes.Length; i++)
			{
				if (Mathf.Abs(Sizes[i] - size) < 0.5f)
				{
					index = i;
				}
			}
			return (long)Mathf.Round(BasePrices[index] * multiplier / 50f) * 50L;
		}

		/// <summary>The direction a lot's front edge faces: 0 +X, 1 +Z, 2 −X, 3 −Z.</summary>
		public static Vector3 FrontDirection(int front)
		{
			switch (front & 3)
			{
				case 0: return Vector3.right;
				case 1: return Vector3.forward;
				case 2: return Vector3.left;
				default: return Vector3.back;
			}
		}

		private static void Add(Result result, List<ScenePlotManifest.Lot> occupied, ScenePlotManifest.Lot lot)
		{
			result.Lots.Add(lot);
			occupied.Add(lot);
			result.Clear.Add(new Disc { X = lot.Centre.x, Z = lot.Centre.z, Radius = lot.Size.x * 0.7072f + 1.5f });
		}

		private static float Pick(DeterministicRNG rng, float[] weights)
		{
			float total = 0f;
			foreach (float w in weights)
			{
				total += w;
			}
			float roll = rng.NextFloat() * total;
			for (int i = 0; i < weights.Length; i++)
			{
				roll -= weights[i];
				if (roll <= 0f)
				{
					return Sizes[i];
				}
			}
			return Sizes[Sizes.Length - 1];
		}

		private static bool FarFrom(List<Vector2> points, Vector2 p, float distance)
		{
			foreach (Vector2 q in points)
			{
				if ((q - p).sqrMagnitude < distance * distance)
				{
					return false;
				}
			}
			return true;
		}

		private static float Distance(float x0, float z0, float x1, float z1)
		{
			float dx = x0 - x1, dz = z0 - z1;
			return Mathf.Sqrt(dx * dx + dz * dz);
		}

		/// <summary>A stable 31-bit seed from three integers.</summary>
		public static int Hash(int seed, int a, int b)
		{
			unchecked
			{
				uint h = 2166136261u;
				h = (h ^ (uint)seed) * 16777619u;
				h = (h ^ (uint)a) * 16777619u;
				h = (h ^ (uint)b) * 16777619u;
				h ^= h >> 15;
				h *= 0x2C1B3C6Du;
				h ^= h >> 12;
				return (int)(h & 0x7FFFFFFF) | 1;
			}
		}
	}
}
