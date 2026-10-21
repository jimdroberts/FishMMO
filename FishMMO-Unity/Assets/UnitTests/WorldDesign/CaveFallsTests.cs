using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using FishMMO.Water;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Where a cave's falling water and lava end up (2026-10-21: "Cave waterfalls and lava flows don't always flow properly into
	/// the little underground river areas"): every fall, shower, spring and vent written into the scene's hydrology lands in the
	/// water it feeds (its curtain, traced as the inland water's renderer traces it, comes down in that stream's channel or that
	/// pool, at its surface; a lava pour's foot in the river or lake it pours into), every river that ends in another body ends
	/// on it (a lake's mask at its level, a joined river's line at its surface), and no stream or lava river sinks into its bed
	/// a few metres from water it could have run on into. Over the escarpment fixture of CaveStreamsTests and CaveLavaTests.
	/// </summary>
	[TestFixture]
	public class CaveFallsTests
	{
		private const float CaveLift = 60f;
		private const int Site = 0x00ABCDE;
		/// <summary>How near another body of the same liquid a river may not sink into its bed, metres (in plan, to its water's edge).</summary>
		public const float NearMetres = 4f;
		/// <summary>How far off a body's surface a fall's landing or a river's end may lie and still be in it, metres.</summary>
		public const float LevelTolerance = 0.12f;
		/// <summary>How far over a river's end another body's surface may stand and still be water it could have run on into, metres.</summary>
		public const float RunOnTolerance = 0.02f;

		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		private static CaveSystem PlanCave(CaveForm form, int size, int seed)
		{
			float tilt = seed * 13f % 40f - 20f;
			Func<float, float, float> face = Escarpment(tilt, seed);
			Func<float, float, float> g = (px, pz) => face(px, pz) + CaveLift;
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed,
				CaveGround.Sample(g, -560f, -560f, 560f, 560f, 2f), 0f, out string problem);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			return system;
		}

		/// <summary>A grotto at the foot of a sea cliff, its pool the sea's level (as CaveStreamsTests plans one).</summary>
		private static CaveSystem PlanGrotto(int seed)
		{
			Func<float, float, float> g = (x, z) =>
			{
				float t = Mathf.Clamp01(z / 25f);
				return -1.5f + 2.5f * Mathf.Clamp01((z + 12f) / 10f) + 39f * (t * t * (3f - 2f * t)) + 0.8f * Mathf.Sin(x * 0.07f) * Mathf.Clamp01(z / 10f);
			};
			CaveSystem system = CaveSystem.Plan(CaveForm.Grotto, 1, new Vector3(0f, 0f, -4f), 180f, seed * 7919, seed,
				CaveGround.Sample(g, -560f, -560f, 560f, 560f, 2f), 0f, out string problem, 0f);
			Assert.That(system, Is.Not.Null, $"grotto seed {seed} was refused: {problem}");
			return system;
		}

		private static List<CaveSystem> caves;

		/// <summary>Wet caves of every size, mines, cooled and molten lava tubes and grottos over several seeds, planned once.</summary>
		private static List<CaveSystem> Caves()
		{
			if (caves == null)
			{
				caves = new List<CaveSystem>();
				foreach ((CaveForm form, int size, int seeds) in new[]
				{
					(CaveForm.Cave, 0, 6), (CaveForm.Cave, 1, 8), (CaveForm.Cave, 2, 6), (CaveForm.Mine, 1, 5), (CaveForm.Mine, 2, 3),
					(CaveForm.LavaTube, 0, 8), (CaveForm.LavaTube, 1, 10), (CaveForm.LavaTube, 2, 6),
				})
				{
					for (int seed = 1; seed <= seeds; seed++)
					{
						CaveSystem s = PlanCave(form, size, seed);
						if (CaveStreams.Plan(s).Active && !CaveStreams.Frozen(s) || CaveLava.Molten(s))
						{
							caves.Add(s);
						}
					}
				}
				for (int seed = 1; seed <= 6; seed++)
				{
					caves.Add(PlanGrotto(seed));
				}
			}
			return caves;
		}

		private static string What(CaveSystem s) => $"{s.Form} {s.SizeClass} seed {s.Seed / 7919} ({s.Character})";

		/// <summary>
		/// A system's water and lava as the cave's shaper writes them into the scene's hydrology (CaveStreamWater, CaveLavaWater),
		/// with an awash cave's own pool (CaveShaper.WritePool) over every void under its level.
		/// </summary>
		public static void Written(CaveSystem s, int siteId, List<SceneHydrology.Lake> lakes, List<SceneHydrology.River> rivers)
		{
			if (!float.IsNaN(s.WaterLevel))
			{
				SceneHydrology.Lake pool = CaveLavaWater.Lake(CaveShaper.PoolIdBase | (siteId & 0x00FFFFFF), s.Origin + new Vector3(0f, s.WaterLevel - s.Origin.y, 0f),
					Mathf.Max(s.Extent.extents.x, s.Extent.extents.z) + Mathf.Max(Mathf.Abs(s.Extent.center.x), Mathf.Abs(s.Extent.center.z)),
					p => s.Void(p - s.Origin) < 0f, 2f);
				if (pool != null)
				{
					pool.Lava = false;
					lakes.Add(pool);
				}
			}
			CaveStreamWater.Write(lakes, rivers, siteId, s, CaveStreams.Plan(s));
			CaveLavaWater.Write(lakes, rivers, siteId, s, CaveLava.Plan(s));
		}

		/// <summary>
		/// What is wrong with where a site's falls land and its rivers end (empty for nothing): read from its hydrology as written,
		/// the curtains traced free (FallHydraulics.Throw) as the renderer traces them.
		/// </summary>
		public static List<string> Problems(List<SceneHydrology.Lake> lakes, List<SceneHydrology.River> rivers, float near = NearMetres)
		{
			var problems = new List<string>();
			foreach (SceneHydrology.River r in rivers)
			{
				string problem = EndProblem(r, rivers, lakes, near);
				if (problem != null)
				{
					problems.Add(problem);
				}
				if (!r.Lava)
				{
					problems.AddRange(CurtainProblems(r, rivers, lakes));
				}
			}
			return problems;
		}

		private static string Name(SceneHydrology.River r)
		{
			int n = r.Points.Length;
			return $"{(r.Lava ? "lava" : "water")} river {r.Id:X8} ({n} points{(EndsFalling(r) ? ", ending in a fall" : "")}) ending at {r.Points[n - 1]:F2}";
		}

		/// <summary>
		/// Whether a river's last stretch is a planned fall: a water river's point before its last marked falling; a lava fall (a
		/// vent's, a chasm's), every point falling (a lava river's own steep runs are marked falling too, poured down its channel).
		/// </summary>
		public static bool EndsFalling(SceneHydrology.River r)
		{
			int n = r.Points.Length;
			if (n < 2 || r.Reach == null || r.Reach.Length != n)
			{
				return false;
			}
			return r.Lava ? Array.TrueForAll(r.Reach, x => x == InlandWaterRenderer.FallReach) : r.Reach[n - 2] == InlandWaterRenderer.FallReach;
		}

		private static string EndProblem(SceneHydrology.River r, List<SceneHydrology.River> rivers, List<SceneHydrology.Lake> lakes, float near)
		{
			int n = r.Points.Length;
			Vector3 end = r.Points[n - 1];
			switch (r.Finish)
			{
				case SceneHydrology.End.Lake:
				{
					SceneHydrology.Lake lake = lakes.Find(l => l.Id == r.EndLake);
					if (lake == null || lake.Lava != r.Lava || !lake.Covers(end.x, end.z) || Mathf.Abs(lake.Level - end.y) > LevelTolerance)
					{
						return $"{Name(r)}: into lake {r.EndLake:X8} but not in it (covered {lake?.Covers(end.x, end.z)}, level {lake?.Level:F2})";
					}
					return null;
				}
				case SceneHydrology.End.Confluence:
				{
					SceneHydrology.River joined = rivers.Find(o => o.Id == r.JoinsRiver);
					if (joined == null || joined == r || joined.Lava != r.Lava)
					{
						return $"{Name(r)}: joins river {r.JoinsRiver:X8}, not written";
					}
					Line(joined, end, 0, out float off, out float half, out float surface);
					if (off > half + 0.05f || Mathf.Abs(surface - end.y) > LevelTolerance)
					{
						return $"{Name(r)}: joins {joined.Id:X8} off its water ({off:F2} m out of {half:F2}, surface {surface:F2})";
					}
					return null;
				}
			}
			if (EndsFalling(r))
			{
				return $"{Name(r)}: a fall into nothing ({r.Finish})";
			}
			// Water it could have run on into: another body of its liquid near its end, its surface no higher (nor another level's,
			// further down than it lies off).
			foreach (SceneHydrology.River o in rivers)
			{
				// (Not water running into it: that is upstream of where it sinks.)
				if (o == r || o.Lava != r.Lava || o.Finish == SceneHydrology.End.Confluence && o.JoinsRiver == r.Id)
				{
					continue;
				}
				Line(o, end, 0, out float off, out float half, out float surface);
				if (off - half < near && surface <= end.y + RunOnTolerance && surface >= end.y - near)
				{
					return $"{Name(r)}: sinks {off - half:F2} m from river {o.Id:X8} (its surface {surface:F2})";
				}
			}
			foreach (SceneHydrology.Lake lake in lakes)
			{
				if (lake.Lava != r.Lava || lake.Level > end.y + RunOnTolerance || lake.Level < end.y - near)
				{
					continue;
				}
				float d = LakeDistance(lake, end);
				if (d < near)
				{
					return $"{Name(r)}: sinks {d:F2} m from lake {lake.Id:X8} (its level {lake.Level:F2})";
				}
			}
			return null;
		}

		/// <summary>
		/// Each fall in a water river (as InlandWaterRenderer.FindFalls finds one), its curtain traced free from its lip the way it
		/// pours: where it comes down to its pool's level must be water at that level (this river past its foot, another river,
		/// a lake).
		/// </summary>
		private static IEnumerable<string> CurtainProblems(SceneHydrology.River r, List<SceneHydrology.River> rivers, List<SceneHydrology.Lake> lakes)
		{
			int n = r.Points.Length;
			if (n < 3 || r.Reach == null || r.Reach.Length != n)
			{
				yield break;
			}
			int i = 0;
			while (i < n)
			{
				if (r.Reach[i] != InlandWaterRenderer.FallReach)
				{
					i++;
					continue;
				}
				int last = i;
				while (last + 1 < n && (r.Reach[last + 1] == InlandWaterRenderer.FallReach || last + 2 < n && r.Reach[last + 2] == InlandWaterRenderer.FallReach))
				{
					last++;
				}
				int lip = Mathf.Max(0, i - 1), foot = Mathf.Min(n - 1, last + 1);
				i = last + 1;
				if (r.Points[lip].y - r.Points[foot].y < 1.5f)
				{
					continue;
				}
				int plunge = foot;
				for (int k = lip + 1; k <= foot; k++)
				{
					if (r.Points[k].y <= r.Points[foot].y + 0.5f)
					{
						plunge = k;
						break;
					}
				}
				float level = r.Points[plunge].y;
				Vector3 landing = Landing(r, lip, level);
				if (!InWater(landing, level, r, foot, rivers, lakes, out string where))
				{
					yield return $"{Name(r)}: its fall from {r.Points[lip]:F2} lands at {landing:F2}, out of the water ({where})";
				}
			}
		}

		/// <summary>Where a fall's curtain comes down to <paramref name="level"/>: its lip, out the way it pours by its free throw (FallHydraulics.Throw).</summary>
		public static Vector3 Landing(SceneHydrology.River r, int lip, float level)
		{
			int n = r.Points.Length;
			Vector3 ahead = r.Points[Mathf.Min(n - 1, lip + 1)] - r.Points[Mathf.Max(0, lip - 1)];
			ahead.y = 0f;
			ahead = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.forward;
			float q = FallHydraulics.UnitDischarge(r.Discharge[lip], r.Width[lip], r.Depth[lip], r.Speed[lip]);
			Vector3 landing = r.Points[lip] + ahead * FallHydraulics.Throw(q, r.Points[lip].y - level);
			landing.y = level;
			return landing;
		}

		/// <summary>Whether a point is in water at a level: in a river's channel (<paramref name="self"/> only from <paramref name="from"/> on), or a lake.</summary>
		private static bool InWater(Vector3 p, float level, SceneHydrology.River self, int from, List<SceneHydrology.River> rivers, List<SceneHydrology.Lake> lakes, out string where)
		{
			where = "no water near";
			float nearest = float.MaxValue;
			foreach (SceneHydrology.River o in rivers)
			{
				if (o.Lava)
				{
					continue;
				}
				Line(o, p, o == self ? from : 0, out float off, out float half, out float surface);
				if (off <= half + 0.05f && Mathf.Abs(surface - level) <= 0.3f)
				{
					return true;
				}
				if (off - half < nearest)
				{
					nearest = off - half;
					where = $"{off - half:F2} m from river {o.Id:X8}'s water, its surface {surface:F2}";
				}
			}
			foreach (SceneHydrology.Lake lake in lakes)
			{
				if (!lake.Lava && lake.Covers(p.x, p.z) && Mathf.Abs(lake.Level - level) <= 0.3f)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The nearest point of a river's line to a point in plan (from point <paramref name="from"/> on): how far off, its half-width and its surface there.</summary>
		public static void Line(SceneHydrology.River r, Vector3 p, int from, out float off, out float half, out float surface)
		{
			off = float.MaxValue;
			half = 0f;
			surface = float.NaN;
			for (int i = Mathf.Max(0, from); i + 1 < r.Points.Length; i++)
			{
				Vector3 a = r.Points[i], b = r.Points[i + 1];
				var ab = new Vector2(b.x - a.x, b.z - a.z);
				var ap = new Vector2(p.x - a.x, p.z - a.z);
				float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				float d = (ap - ab * t).magnitude;
				if (d < off)
				{
					off = d;
					half = 0.5f * Mathf.Lerp(r.Width[i], r.Width[i + 1], t);
					surface = Mathf.Lerp(a.y, b.y, t);
				}
			}
		}

		/// <summary>How far a point lies from a lake's water in plan (0 over it), metres.</summary>
		public static float LakeDistance(SceneHydrology.Lake lake, Vector3 p)
		{
			if (lake.Covers(p.x, p.z))
			{
				return 0f;
			}
			float best = float.MaxValue;
			for (int z = 0; z < lake.MaskHeight; z++)
			{
				for (int x = 0; x < lake.MaskWidth; x++)
				{
					float cx = lake.MaskOrigin.x + (x + 0.5f) * lake.MaskCell, cz = lake.MaskOrigin.y + (z + 0.5f) * lake.MaskCell;
					if (lake.Covers(cx, cz))
					{
						best = Mathf.Min(best, Mathf.Max(0f, new Vector2(cx - p.x, cz - p.z).magnitude - 0.5f * lake.MaskCell));
					}
				}
			}
			return best;
		}

		// ── The tests ─────────────────────────────────────────────

		[Test]
		public void EveryFallLandsInTheWaterItFeedsAndNoRiverSinksBesideWater()
		{
			int systems = 0, rivers = 0, falls = 0, joins = 0, intoLakes = 0;
			var failures = new List<string>();
			foreach (CaveSystem s in Caves())
			{
				var lakeList = new List<SceneHydrology.Lake>();
				var riverList = new List<SceneHydrology.River>();
				Written(s, Site, lakeList, riverList);
				systems++;
				rivers += riverList.Count;
				foreach (SceneHydrology.River r in riverList)
				{
					falls += Array.IndexOf(r.Reach, InlandWaterRenderer.FallReach) >= 0 ? 1 : 0;
					joins += r.Finish == SceneHydrology.End.Confluence ? 1 : 0;
					intoLakes += r.Finish == SceneHydrology.End.Lake ? 1 : 0;
				}
				foreach (string problem in Problems(lakeList, riverList))
				{
					failures.Add($"{What(s)}: {problem}");
				}
			}
			Console.WriteLine($"{systems} systems: {rivers} rivers ({falls} with falls), {joins} joining another, {intoLakes} into a lake; {failures.Count} wrong");
			foreach (string failure in failures)
			{
				Console.WriteLine(failure);
			}
			Assert.That(systems, Is.GreaterThan(10));
			Assert.That(failures, Is.Empty);
		}

		[Test]
		public void EveryCaveLakeKnowsHowDeepItsBedLies()
		{
			// The water's height-aware lookup answers for a lake only over its floor (SceneHydrology.Lake.Floor): each cave pool and
			// lava lake written knows it, at the bed cut for it or under it, never far under (a winze's water is the deepest).
			int lakes = 0;
			foreach (CaveSystem s in Caves())
			{
				var lakeList = new List<SceneHydrology.Lake>();
				Written(s, Site, lakeList, new List<SceneHydrology.River>());
				CaveStreamPlan water = CaveStreams.Plan(s);
				for (int i = 0; i < water.Bodies.Count && i < CaveStreamWater.MaxBodies; i++)
				{
					CaveStreamBody b = water.Bodies[i];
					SceneHydrology.Lake lake = lakeList.Find(l => l.Id == CaveStreamWater.IdOf(Site, i));
					if (b.Kind != CaveStreamKind.Pool || lake == null)
					{
						continue;
					}
					string what = $"{What(s)} pool {i}";
					Assert.That(float.IsInfinity(lake.Floor), Is.False, what);
					Assert.That(lake.Floor, Is.LessThanOrEqualTo(lake.Level - b.Depth + 0.2f), $"{what}: its floor at its bed or under it");
					Assert.That(lake.Floor, Is.GreaterThanOrEqualTo(lake.Level - CaveLavaWater.MaxFloorReach), what);
					lakes++;
				}
				CaveLavaPlan lava = CaveLava.Plan(s);
				for (int i = 0; i < lava.Bodies.Count && i < CaveLavaWater.MaxBodies; i++)
				{
					SceneHydrology.Lake lake = lakeList.Find(l => l.Id == CaveLavaWater.IdOf(Site, i));
					if (lava.Bodies[i].Kind != CaveLavaKind.Lake || lake == null)
					{
						continue;
					}
					Assert.That(lake.Floor, Is.LessThan(lake.Level - 0.2f).And.GreaterThanOrEqualTo(lake.Level - CaveLavaWater.MaxFloorReach), $"{What(s)} lava lake {i}");
					lakes++;
				}
			}
			Console.WriteLine($"{lakes} cave lakes");
			Assert.That(lakes, Is.GreaterThan(10));
		}

		[Test]
		public void AFallsThrowIsTheCurtainsOwnPath()
		{
			// Thrown out level at the brink's speed, falling under gravity: a stone's throw before the air takes hold (2 m of drop at
			// most is within a centimetre or two of it), never more than it, and further the more water and the further it falls.
			foreach (float q in new[] { 0.1f, 0.3f, 1f, 3f })
			{
				float vb = FallHydraulics.BrinkSpeed(q);
				float drop = 1.5f;
				float free = vb * Mathf.Sqrt(2f * (drop - 0.5f * FallHydraulics.BrinkDepth(q)) / FallHydraulics.Gravity);
				float thrown = FallHydraulics.Throw(q, drop);
				Assert.That(thrown, Is.LessThanOrEqualTo(free + 0.02f), $"q {q}");
				Assert.That(thrown, Is.GreaterThan(0.7f * free), $"q {q}");
				Assert.That(FallHydraulics.Throw(q, 12f), Is.GreaterThan(thrown), $"q {q}: further for a longer fall");
				Assert.That(FallHydraulics.Throw(q * 2f, drop), Is.GreaterThan(thrown), $"q {q}: further for more water");
			}
			var path = new List<Vector2>();
			float x = FallHydraulics.Throw(1f, 6f, FallHydraulics.DefaultTurbulence, path);
			Assert.That(path[path.Count - 1].x, Is.EqualTo(x).Within(1e-4f));
			Assert.That(path[path.Count - 1].y, Is.EqualTo(6f).Within(1e-4f));
			for (int k = 1; k < path.Count; k++)
			{
				Assert.That(path[k].x, Is.GreaterThanOrEqualTo(path[k - 1].x));
				Assert.That(path[k].y, Is.GreaterThanOrEqualTo(path[k - 1].y));
			}
		}
	}
}
