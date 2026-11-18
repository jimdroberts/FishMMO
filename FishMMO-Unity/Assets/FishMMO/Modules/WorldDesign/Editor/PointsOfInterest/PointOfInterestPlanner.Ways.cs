#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestPlanner
	{
		/// <summary>Metres between the points along a way a wayside kind is tried at.</summary>
		private const float WaysideStep = 12f;

		/// <summary>How far past its verge a wayside site's footprint starts at the least, metres: the shoulder the way's carve left.</summary>
		private const float WaysideShoulder = 1.5f;

		/// <summary>Junctions nearer than this to one another are one, metres.</summary>
		public const float JunctionMerge = 30f;

		/// <summary>
		/// Sites the kinds that stand by the ways (<see cref="PointOfInterestKindRule.BesideWays"/>: a toll gate, an ambush, a wrecked
		/// caravan beside a way, a crossroads at a junction) against the ways the path planner laid (2026-10-27, the fantasy wave).
		/// Returns the records it added to <paramref name="plan"/> (with their keep-outs; no pads: the ways are carved already).
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why after the ways.</b> The ways are planned from the sites (PathNetworkPlanner: trunks between the settlements, spurs to
		/// the rest), routed round every keep-out and carved before the scene is painted, so the planner cannot know where they run
		/// when it sites: a corridor guessed from the settlements' straight lines is off by hundreds of metres in rough country, and a
		/// site there would stand off the road it is about. So <see cref="Plan"/> leaves these kinds out and the stage calls this once
		/// the ways are laid, on the same input (the ground as the pads and the carve left it): every other gate of the kind's rule is
		/// kept, and the sites take the gaps from every site already planned, the bridges the ways made among them.
		/// </para>
		/// <para>
		/// <b>Beside, not on.</b> A site stands off the way's verge (<see cref="WaysideShoulder"/> to its rule's
		/// <see cref="PointOfInterestKindRule.OnWayMetres"/>) facing it, its footprint clear of every way, so its props never stand
		/// on the road; a crossroads stands on a corner of its junction, facing into it. The busier the way, the better the spot.
		/// </para>
		/// <para>Pure, as the planner is: the same input, plan and ways place the same records.</para>
		/// </remarks>
		public static List<PointOfInterestRecord> PlaceBesideWays(PointOfInterestPlanInput input, PointOfInterestPlan plan, IReadOnlyList<ScenePath> ways)
		{
			var added = new List<PointOfInterestRecord>();
			if (input == null || plan == null || input.Ground == null)
			{
				return added;
			}
			input.Rules ??= PointOfInterestRules.Default();
			input.Settings ??= new PointOfInterestPlanSettings();
			if (input.Settings.NaturalOnly)
			{
				return added;
			}
			var rules = new List<PointOfInterestKindRule>();
			foreach (PointOfInterestKindRule rule in input.Rules.Ordered(PointOfInterestPlacement.Placed))
			{
				if (rule.BesideWays)
				{
					rules.Add(rule);
				}
			}
			if (rules.Count == 0)
			{
				return added;
			}
			var state = new State(input, plan);
			state.Resume();
			var index = new WayIndex(ways);
			List<Vector3> junctions = null;
			foreach (PointOfInterestKindRule rule in rules)
			{
				PointOfInterestKindRule effective = state.Effective(rule, false);
				if (effective == null)
				{
					continue;
				}
				if (effective.JunctionMetres > 0f && junctions == null)
				{
					junctions = WayJunctions(ways);
					// A junction inside a site (a town's gate, its square) is the site's, not the country's.
					junctions.RemoveAll(j => plan.IsKeptOut(j.x, j.z));
				}
				state.PlaceBesideWays(effective, index, junctions, added);
			}
			return added;
		}

		/// <summary>
		/// Where ways meet out in the country (pure): a way's end that joins another way part way along it (a spur's, a lesser way's:
		/// its end site id -1), and where two ways come together or part (one running on the other's line, or crossing it). Merged
		/// within <paramref name="merge"/> metres, in a stable order. Settlements' own streets are not ways.
		/// </summary>
		public static List<Vector3> WayJunctions(IReadOnlyList<ScenePath> ways, float merge = JunctionMerge)
		{
			var found = new List<Vector3>();
			if (ways == null)
			{
				return found;
			}
			var index = new WayIndex(ways);
			for (int p = 0; p < ways.Count; p++)
			{
				ScenePath path = ways[p];
				if (!WayIndex.Routed(path))
				{
					continue;
				}
				int n = path.Count;
				var near = new bool[n];
				for (int i = 0; i < n; i++)
				{
					near[i] = index.NearOther(p, path.Points[i], HalfAt(path, i), out _, out _);
				}
				// An end on another way's middle: a spur, a lesser way joining.
				for (int end = 0; end < 2; end++)
				{
					int i = end == 0 ? 0 : n - 1;
					if (near[i] && index.NearOther(p, path.Points[i], HalfAt(path, i), out int q, out int k) && k >= 3 && k < ways[q].Count - 3)
					{
						found.Add(path.Points[i]);
					}
				}
				// Where it comes onto another way's line or leaves it (a merge, a parting, a crossing).
				for (int i = 3; i < n - 3; i++)
				{
					if (near[i] != near[i - 1])
					{
						found.Add(path.Points[near[i] ? i : i - 1]);
					}
				}
			}
			found.Sort((a, b) =>
			{
				int c = a.x.CompareTo(b.x);
				return c != 0 ? c : a.z.CompareTo(b.z);
			});
			var merged = new List<Vector3>();
			foreach (Vector3 j in found)
			{
				bool near = false;
				foreach (Vector3 m in merged)
				{
					float dx = m.x - j.x, dz = m.z - j.z;
					if (dx * dx + dz * dz < merge * merge)
					{
						near = true;
						break;
					}
				}
				if (!near)
				{
					merged.Add(j);
				}
			}
			return merged;
		}

		private static float HalfAt(ScenePath path, int i)
			=> path.HalfWidth != null && i < path.HalfWidth.Length ? path.HalfWidth[i] : ScenePathStyle.For(path.Class).HalfWidth;

		/// <summary>How much a way's traffic makes it the place for a wayside site: the highway most, a trail least.</summary>
		private static float TrafficOf(ScenePathClass way)
		{
			switch (way)
			{
				case ScenePathClass.Highway: return 0.3f;
				case ScenePathClass.Road: return 0.25f;
				case ScenePathClass.CartTrack: return 0.15f;
				case ScenePathClass.Footpath: return 0.05f;
				default: return 0f;
			}
		}

		/// <summary>The routed ways' points bucketed for nearness tests.</summary>
		internal sealed class WayIndex
		{
			private const float Bucket = 16f;
			public readonly IReadOnlyList<ScenePath> Ways;
			private readonly Dictionary<long, List<(int path, int point)>> buckets = new Dictionary<long, List<(int, int)>>();
			public float Widest { get; }

			public WayIndex(IReadOnlyList<ScenePath> ways)
			{
				Ways = ways ?? Array.Empty<ScenePath>();
				for (int p = 0; p < Ways.Count; p++)
				{
					ScenePath path = Ways[p];
					if (!Routed(path))
					{
						continue;
					}
					for (int i = 0; i < path.Count; i++)
					{
						long key = Key(path.Points[i].x, path.Points[i].z);
						if (!buckets.TryGetValue(key, out List<(int, int)> list))
						{
							list = new List<(int, int)>();
							buckets[key] = list;
						}
						list.Add((p, i));
						Widest = Mathf.Max(Widest, HalfAt(path, i));
					}
				}
			}

			/// <summary>A way the planner routed (not a site's street, not empty).</summary>
			public static bool Routed(ScenePath path) => path != null && path.Count >= 2 && path.Class != ScenePathClass.Street;

			private static long Key(float x, float z) => ((long)Mathf.FloorToInt(x / Bucket) << 32) ^ (uint)Mathf.FloorToInt(z / Bucket);

			/// <summary>Whether any way's carriageway comes within <paramref name="clear"/> metres of (x, z).</summary>
			public bool Touches(float x, float z, float clear)
			{
				int reach = Mathf.CeilToInt((clear + Widest) / Bucket);
				int bx = Mathf.FloorToInt(x / Bucket), bz = Mathf.FloorToInt(z / Bucket);
				for (int dz = -reach; dz <= reach; dz++)
				{
					for (int dx = -reach; dx <= reach; dx++)
					{
						if (!buckets.TryGetValue(((long)(bx + dx) << 32) ^ (uint)(bz + dz), out List<(int path, int point)> list))
						{
							continue;
						}
						foreach ((int p, int i) in list)
						{
							Vector3 q = Ways[p].Points[i];
							float need = clear + HalfAt(Ways[p], i);
							float ex = q.x - x, ez = q.z - z;
							if (ex * ex + ez * ez < need * need)
							{
								return true;
							}
						}
					}
				}
				return false;
			}

			/// <summary>Whether another way than <paramref name="own"/> runs within its and this point's half-widths and a margin of it; the nearest such point.</summary>
			public bool NearOther(int own, Vector3 at, float half, out int path, out int point)
			{
				path = -1;
				point = -1;
				float best = float.PositiveInfinity;
				int bx = Mathf.FloorToInt(at.x / Bucket), bz = Mathf.FloorToInt(at.z / Bucket);
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						if (!buckets.TryGetValue(((long)(bx + dx) << 32) ^ (uint)(bz + dz), out List<(int path, int point)> list))
						{
							continue;
						}
						foreach ((int p, int i) in list)
						{
							if (p == own)
							{
								continue;
							}
							Vector3 q = Ways[p].Points[i];
							float tol = Mathf.Max(half, HalfAt(Ways[p], i)) + 1.5f;
							float ex = q.x - at.x, ez = q.z - at.z, d2 = ex * ex + ez * ez;
							if (d2 < tol * tol && d2 < best)
							{
								best = d2;
								path = p;
								point = i;
							}
						}
					}
				}
				return path >= 0;
			}
		}

		internal sealed partial class State
		{
			/// <summary>
			/// Takes up a plan already made (its records' ids, the placed sites and the settlements among them), so a later pass sites
			/// on among them exactly as <see cref="Plan"/> would have.
			/// </summary>
			public void Resume()
			{
				foreach (PointOfInterestRecord record in Plan.Records)
				{
					if (record == null)
					{
						continue;
					}
					ids.Add(record.Id);
					PointOfInterestKindRule rule = Input.Rules.RuleFor(record.Kind);
					if (rule != null && rule.Placement == PointOfInterestPlacement.Detected)
					{
						continue;
					}
					placed.Add(record);
					if (PointOfInterestKinds.Info(record.Kind).Has(PointOfInterestTraits.Settlement) || record.Kind == POIType.Capital || record.Kind == POIType.City)
					{
						settlements.Add(record);
					}
				}
			}

			/// <summary>Sites a wayside kind (<see cref="PointOfInterestPlanner.PlaceBesideWays"/>) by its ways or junctions.</summary>
			public void PlaceBesideWays(PointOfInterestKindRule rule, WayIndex ways, List<Vector3> junctions, List<PointOfInterestRecord> added)
			{
				int budget = Budget(rule, Input.Settings, HostedAreaKm2(rule), Input.Seed);
				if (budget <= 0 || (rule.NearSettlementMetres > 0f && settlements.Count == 0))
				{
					return;
				}
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(rule.Kind);
				var candidates = new List<Site>();
				if (rule.JunctionMetres > 0f)
				{
					if (junctions == null || junctions.Count == 0)
					{
						return;
					}
					foreach (Vector3 j in junctions)
					{
						// On each corner of the junction in turn, out until its footprint clears every arm.
						for (int k = 0; k < 12; k++)
						{
							float a = (k + 0.5f) * (Mathf.PI / 6f);
							float sx = Mathf.Sin(a), sz = Mathf.Cos(a);
							uint hash = Hash(Input.Seed, (int)rule.Kind, Mathf.RoundToInt(j.x) * 12 + k, Mathf.RoundToInt(j.z));
							float radius = Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, Unit(Mix(hash ^ 0x7A11u)));
							for (float d = radius + WaysideShoulder; d <= radius + rule.JunctionMetres + ways.Widest; d += 2f)
							{
								float x = j.x + sx * d, z = j.z + sz * d;
								if (ways.Touches(x, z, radius + WaysideShoulder))
								{
									continue;
								}
								if (Wayside(rule, info, x, z, radius, hash, 0.2f, out Site site))
								{
									// Facing into the junction; nearer corners first.
									site.Yaw = Mathf.Round(YawOf(-sx, -sz));
									site.Score -= 0.01f * (d - radius);
									candidates.Add(site);
								}
								break;
							}
						}
					}
				}
				else
				{
					for (int p = 0; p < ways.Ways.Count; p++)
					{
						ScenePath path = ways.Ways[p];
						if (!WayIndex.Routed(path))
						{
							continue;
						}
						int step = Mathf.Max(1, Mathf.RoundToInt(WaysideStep / PathNetworkPlanner.PointSpacing));
						for (int i = step; i < path.Count - step; i += step)
						{
							Vector3 c = path.Points[i];
							float tx = path.Points[i + 1].x - path.Points[i - 1].x, tz = path.Points[i + 1].z - path.Points[i - 1].z;
							float tl = Mathf.Sqrt(tx * tx + tz * tz);
							if (tl < 1e-3f)
							{
								continue;
							}
							float nx = -tz / tl, nz = tx / tl, half = HalfAt(path, i);
							for (int side = -1; side <= 1; side += 2)
							{
								uint hash = Hash(Input.Seed, (int)rule.Kind, Mathf.RoundToInt(c.x) * 2 + (side > 0 ? 1 : 0), Mathf.RoundToInt(c.z));
								float radius = Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, Unit(Mix(hash ^ 0x7A11u)));
								float gap = WaysideShoulder + Unit(Mix(hash ^ 0x6A9u)) * Mathf.Max(0f, rule.OnWayMetres - WaysideShoulder);
								float off = half + radius + gap;
								float x = c.x + nx * side * off, z = c.z + nz * side * off;
								if (ways.Touches(x, z, radius + WaysideShoulder * 0.5f))
								{
									continue;
								}
								if (Wayside(rule, info, x, z, radius, hash, TrafficOf(path.Class), out Site site))
								{
									// Its front to the way.
									site.Yaw = Mathf.Round(YawOf(-nx * side, -nz * side));
									candidates.Add(site);
								}
							}
						}
					}
				}
				candidates.Sort((a, b) =>
				{
					int c = b.Score.CompareTo(a.Score);
					if (c != 0) return c;
					c = a.X.CompareTo(b.X);
					return c != 0 ? c : a.Z.CompareTo(b.Z);
				});
				int taken = 0;
				foreach (Site candidate in candidates)
				{
					if (taken >= budget)
					{
						break;
					}
					Site site = candidate;
					if (!Clear(rule, ref site) || !Footprint(rule, info, ref site))
					{
						continue;
					}
					PointOfInterestRecord record = Add(rule, site, true);
					if (Builds(rule.Kind))
					{
						Plan.KeepOuts.Add(new PointOfInterestKeepOut { Id = record.Id, X = record.Position.x, Z = record.Position.z, Radius = record.Radius + 2f });
					}
					added.Add(record);
					taken++;
				}
				if (taken < budget)
				{
					Plan.Notes.Add(candidates.Count == 0
						? $"{info.DisplayName}: no {(rule.JunctionMetres > 0f ? "junction of the ways" : "way")} in the scene has ground it may stand on beside it."
						: $"{info.DisplayName}: {taken} of {budget} sited by the ways; no more wayside ground fits.");
				}
			}

			/// <summary>
			/// Whether a wayside kind may stand at (x, z) with <paramref name="radius"/>, by every ground gate of its rule a placed kind's
			/// cell is read by (<see cref="SiteKind"/>), and the candidate scored: biome, flatness, the way's traffic, a seeded tie-break.
			/// </summary>
			private bool Wayside(PointOfInterestKindRule rule, PointOfInterestKindInfo info, float x, float z, float radius, uint hash, float traffic, out Site site)
			{
				site = default;
				if (!Inside(x, z, Input.EdgeMarginMetres + radius))
				{
					return false;
				}
				int i = G.CellAt(x, z);
				if (G.Blocked[i] || (Input.Blocked != null && Input.Blocked(x, z)))
				{
					return false;
				}
				float weight = BiomeWeight(rule, i);
				if (weight <= 0f)
				{
					return false;
				}
				float h = Input.Ground(x, z);
				float altitude = h - Input.SeaLevel;
				if (altitude < rule.MinAltitude || altitude > rule.MaxAltitude || !DryLand(x, z, h) || G.Sea[i])
				{
					return false;
				}
				if (G.WaterDistance[i] < radius + 2f || (Input.HasSea && G.SeaDistance[i] < radius))
				{
					return false;
				}
				float slope = G.Sample(G.Slope, x, z);
				if (slope > rule.MaxSlopeDegrees || (rule.MinHardness > 0f && G.Hardness[i] < rule.MinHardness))
				{
					return false;
				}
				if (rule.CoastMetres > 0f && (!Input.HasSea || G.OpenSeaDistance[i] > rule.CoastMetres))
				{
					return false;
				}
				if (rule.NearWaterMetres > 0f && Mathf.Min(G.WaterDistance[i], G.SeaDistance[i]) > rule.NearWaterMetres)
				{
					return false;
				}
				float landform = 0f;
				if (rule.NearDetector != PointOfInterestDetector.None)
				{
					float[] near = DetectedDistance(rule);
					if (near == null || near[i] > rule.NearDetectedMetres)
					{
						return false;
					}
					landform = 0.4f * (1f - near[i] / rule.NearDetectedMetres);
				}
				float flat = 1f - Mathf.Clamp01(slope / Mathf.Max(1f, rule.MaxSlopeDegrees));
				float score = weight * (0.6f + 0.4f * flat) + traffic + landform + 0.5f * Unit(Mix(hash ^ 0x5C0Eu));
				site = Site.At(x, z, h, score);
				site.Radius = radius;
				site.Cell = i;
				return true;
			}
		}
	}
}
#endif
