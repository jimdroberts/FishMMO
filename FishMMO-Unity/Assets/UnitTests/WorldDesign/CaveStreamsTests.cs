using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Water in the caves (<see cref="CaveStreams"/>, CaveSystem.Streams.cs, CaveStreamWater; 2026-10-20, caves livened:
	/// "underground streams & waterfalls & pools"): which systems hold water (never a molten lava tube; an ice cave's frozen);
	/// the plan is the same each time and after storage; streams run downhill beside their ways, fed by springs falling out of
	/// the walls; falls drop far enough to be drawn as falls; every body lies in the bed the field cut for it, under the floor
	/// beside it; every way is still walked, dry; a dry cave, a molten tube and an ice cave are cut exactly as before; and the
	/// water is written to the scene's hydrology with ids of its own. On the same synthetic escarpment as CaveLavaTests.
	/// </summary>
	[TestFixture]
	public class CaveStreamsTests
	{
		private const float CaveLift = 60f;

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

		/// <summary>A grotto at the foot of a sea cliff, its pool the sea's level (as PointOfInterestCaveTests plans one).</summary>
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

		/// <summary>Caves of every size, mines, cooled lava tubes, ice caves and grottos over several seeds, planned once for the fixture.</summary>
		private static List<CaveSystem> Caves()
		{
			if (caves == null)
			{
				caves = new List<CaveSystem>();
				foreach ((CaveForm form, int size, int seeds) in new[]
				{
					(CaveForm.Cave, 0, 6), (CaveForm.Cave, 1, 8), (CaveForm.Cave, 2, 6), (CaveForm.Mine, 1, 5), (CaveForm.Mine, 2, 3),
					(CaveForm.LavaTube, 1, 6), (CaveForm.IceCave, 1, 4),
				})
				{
					for (int seed = 1; seed <= seeds; seed++)
					{
						caves.Add(PlanCave(form, size, seed));
					}
				}
				for (int seed = 1; seed <= 6; seed++)
				{
					caves.Add(PlanGrotto(seed));
				}
			}
			return caves;
		}

		/// <summary>The fixture's systems with running water (not frozen).</summary>
		private static IEnumerable<CaveSystem> Wet()
		{
			foreach (CaveSystem s in Caves())
			{
				CaveStreamPlan plan = CaveStreams.Plan(s);
				if (plan.Active && !plan.Frozen)
				{
					yield return s;
				}
			}
		}

		private static string What(CaveSystem s) => $"{s.Form} {s.SizeClass} seed {s.Seed / 7919} ({s.Character}; {s.Traits})";

		private static float RoofOver(CaveSystem system, Vector3 p)
		{
			float y = p.y;
			while (system.Void(new Vector3(p.x, y, p.z)) < 0f && y < p.y + 80f)
			{
				y += 0.05f;
			}
			return y;
		}

		private static float FloorUnder(CaveSystem system, Vector3 p)
		{
			float y = p.y;
			while (system.Void(new Vector3(p.x, y, p.z)) < 0f && y > p.y - 60f)
			{
				y -= 0.02f;
			}
			return y;
		}

		/// <summary>Whether a pool is a flooded dead end (it floods the end of a way on purpose: nothing lies beyond it).</summary>
		private static bool FloodedEnd(CaveStreamBody b) => b.Kind == CaveStreamKind.Pool && b.Passage >= 0 && b.Banks == null;

		/// <summary>The water's surface over a point in plan, where it lies in a body's footprint (falls aside); NaN where none does.</summary>
		private static float WaterOver(CaveStreamPlan plan, Vector3 p)
		{
			float surface = float.NaN;
			foreach (CaveStreamBody b in plan.Bodies)
			{
				if (FloodedEnd(b))
				{
					continue;
				}
				if (b.Kind == CaveStreamKind.Pool)
				{
					if (new Vector2(p.x - b.Path[0].x, p.z - b.Path[0].z).magnitude < b.Width)
					{
						surface = float.IsNaN(surface) ? b.Path[0].y : Mathf.Max(surface, b.Path[0].y);
					}
					continue;
				}
				for (int i = 0; i + 1 < b.Path.Length; i++)
				{
					if (b.Falls != null && b.Falls[i + 1])
					{
						continue;
					}
					var ab = new Vector2(b.Path[i + 1].x - b.Path[i].x, b.Path[i + 1].z - b.Path[i].z);
					var ap = new Vector2(p.x - b.Path[i].x, p.z - b.Path[i].z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					if ((ap - ab * t).magnitude < 0.5f * b.Width)
					{
						float y = Mathf.Lerp(b.Path[i].y, b.Path[i + 1].y, t);
						surface = float.IsNaN(surface) ? y : Mathf.Max(surface, y);
					}
				}
			}
			return surface;
		}

		/// <summary>
		/// Walks a chain of points every half metre on the noisy field (as CaveLavaTests' walk): 2.2 m of headroom, no step
		/// steeper than 42°, no drop of 3 m; given a plan, never into water (a floor under a body's surface there).
		/// </summary>
		private static string WalkFailure(CaveSystem system, IList<Vector3> points, CaveStreamPlan plan, out Vector3 at)
		{
			float previous = float.NaN;
			Vector3 last = default;
			at = default;
			for (int i = 0; i < points.Count; i++)
			{
				Vector3 a = i == 0 ? points[0] : points[i - 1], b = points[i];
				int steps = i == 0 ? 1 : Mathf.Max(1, Mathf.CeilToInt(new Vector2(b.x - a.x, b.z - a.z).magnitude / 0.5f));
				for (int s = i == 0 ? 0 : 1; s <= steps; s++)
				{
					Vector3 p = i == 0 ? b : Vector3.Lerp(a, b, s / (float)steps);
					at = p;
					float from = float.IsNaN(previous) ? p.y + 1.2f : previous + 1.2f;
					var start = new Vector3(p.x, from, p.z);
					if (system.Void(start) >= 0f)
					{
						return $"rock where the way should be open at {start}";
					}
					float floor = FloorUnder(system, start);
					if (plan != null)
					{
						float water = WaterOver(plan, p);
						if (!float.IsNaN(water) && floor < water - 0.05f)
						{
							return $"water at {p} (floor {floor:0.00}, surface {water:0.00})";
						}
					}
					float room = RoofOver(system, new Vector3(p.x, floor + 0.1f, p.z)) - floor;
					if (room < 2.2f)
					{
						return $"{room:0.00} m of headroom at {p}";
					}
					if (!float.IsNaN(previous))
					{
						if (previous - floor >= 3f)
						{
							return $"a drop of {previous - floor:0.0} m at {p}";
						}
						float run = new Vector2(p.x - last.x, p.z - last.z).magnitude;
						float slope = Mathf.Atan2(Mathf.Abs(floor - previous), run) * Mathf.Rad2Deg;
						if (run > 0.2f && slope > 42f)
						{
							return $"{slope:0.0}° too steep to walk at {p}";
						}
					}
					previous = floor;
					last = p;
				}
			}
			return null;
		}

		/// <summary>The ways through a system: every passage's line on its floor, and from each passage's end in a chamber to its middle.</summary>
		private static List<(string what, List<Vector3> points)> Ways(CaveSystem s)
		{
			var ways = new List<(string, List<Vector3>)>();
			for (int p = 0; p < s.Passages.Count; p++)
			{
				var points = new List<Vector3>();
				foreach (CaveNode n in s.Passages[p].Nodes)
				{
					points.Add(new Vector3(n.Centre.x, n.Floor + 0.3f, n.Centre.z));
				}
				ways.Add(($"passage {p} ({s.Passages[p].Kind})", points));
				for (int c = 0; c < s.Chambers.Count; c++)
				{
					CaveChamber ch = s.Chambers[c];
					foreach (CaveNode n in new[] { s.Passages[p].Nodes[0], s.Passages[p].Nodes[s.Passages[p].Nodes.Count - 1] })
					{
						var end = new Vector3(n.Centre.x, n.Floor, n.Centre.z);
						Vector3 o = end - ch.Centre;
						float yr = ch.Yaw * Mathf.Deg2Rad;
						float along = o.x * Mathf.Sin(yr) + o.z * Mathf.Cos(yr), across = o.x * Mathf.Cos(yr) - o.z * Mathf.Sin(yr);
						if (along * along / (ch.Radii.x * ch.Radii.x) + across * across / (ch.Radii.z * ch.Radii.z) < 0.7f
							&& Mathf.Abs(n.Floor - CaveSystem.FloorOf(ch, end)) < 1f)
						{
							ways.Add(($"passage {p}'s end into chamber {c} ({ch.Kind})", new List<Vector3> { end + Vector3.up * 0.3f, new Vector3(ch.Centre.x, ch.Floor + 0.3f, ch.Centre.z) }));
						}
					}
				}
			}
			return ways;
		}

		/// <summary>Points over a system's floors and walls, for comparing its field cut two ways.</summary>
		private static List<Vector3> Probes(CaveSystem s)
		{
			var probes = new List<Vector3>();
			foreach (CavePassage p in s.Passages)
			{
				for (int i = 0; i < p.Nodes.Count; i += 2)
				{
					CaveNode n = p.Nodes[i];
					for (int k = -3; k <= 3; k++)
					{
						for (int h = -2; h <= 3; h++)
						{
							probes.Add(new Vector3(n.Centre.x + k * 0.7f * n.Radius / 2f, n.Floor + h * 0.6f, n.Centre.z - k * 0.7f * n.Radius / 2f));
						}
					}
				}
			}
			foreach (CaveChamber c in s.Chambers)
			{
				for (int k = -4; k <= 4; k++)
				{
					for (int h = -2; h <= 2; h++)
					{
						probes.Add(new Vector3(c.Centre.x + k * 0.22f * c.Radii.x, c.Floor + h * 0.7f, c.Centre.z + k * 0.17f * c.Radii.z));
					}
				}
			}
			return probes;
		}

		// ── Which systems hold water ──────────────────────────────

		[Test]
		public void MostCavesHoldWaterAndNoMoltenTubeOrSeaCaveDoes()
		{
			const int total = 600;
			foreach (CaveForm form in new[] { CaveForm.Cave, CaveForm.Grotto, CaveForm.IceCave, CaveForm.LavaTube, CaveForm.Mine, CaveForm.SeaCave })
			{
				int wet = 0, molten = 0;
				for (int seed = 1; seed <= total; seed++)
				{
					var s = new CaveSystem { Form = form, Seed = seed * 7919 };
					bool isWet = CaveStreams.Wet(s);
					wet += isWet ? 1 : 0;
					if (CaveLava.Molten(s))
					{
						molten++;
						Assert.That(isWet, Is.False, $"a molten lava tube holds no water (seed {seed})");
					}
				}
				float share = wet / (float)total, expected = CaveStreams.WetShare(form) * (form == CaveForm.LavaTube ? 1f - CaveLava.ActiveShare : 1f);
				Console.WriteLine($"{form}: {wet} of {total} wet ({share:P0}), {molten} molten");
				Assert.That(share, Is.InRange(expected - 0.08f, expected + 0.08f), $"{form}'s share of wet systems");
			}
			Assert.That(CaveStreams.Plan(new CaveSystem { Form = CaveForm.SeaCave, Seed = 7919 }), Is.SameAs(CaveStreamPlan.Dry));
		}

		[Test]
		public void WetCavesHaveStreamsFallsAndPools()
		{
			int systems = 0, streams = 0, springs = 0, chasm = 0, avens = 0, showers = 0, pools = 0, basins = 0, winzes = 0, ends = 0, into = 0, most = 0;
			foreach (CaveSystem s in Wet())
			{
				CaveStreamPlan plan = CaveStreams.Plan(s);
				systems++;
				Assert.That(plan.Bodies.Count, Is.LessThanOrEqualTo(CaveStreams.MaxBodies), What(s));
				Assert.That(s.StreamsCut, Is.EqualTo(plan.Bodies.Exists(b => b.Banks != null && Array.Exists(b.Banks, v => !float.IsNaN(v)))), $"{What(s)}: its field cut for its plan");
				int st = 0, f = 0, po = 0;
				foreach (CaveStreamBody b in plan.Bodies)
				{
					if (b.Kind == CaveStreamKind.Stream)
					{
						st++;
						into += b.Into >= 0 ? 1 : 0;
						if (b.HasFall)
						{
							f++;
							springs += b.Falls[1] && b.Passage >= 0 ? 1 : 0;
							chasm += b.Chamber >= 0 && b.Banks != null && !float.IsNaN(b.Banks[0]) ? 1 : 0;
							avens += b.Passage < 0 && b.Path.Length == 3 ? 1 : 0;
							showers += b.Depth <= CaveSystem.ShowerDepth + 1e-4f ? 1 : 0;
						}
					}
					else
					{
						po++;
						basins += b.Pit >= 0 && s.Pits[b.Pit].Kind != CavePitKind.Winze ? 1 : 0;
						winzes += b.Pit >= 0 && s.Pits[b.Pit].Kind == CavePitKind.Winze ? 1 : 0;
						ends += FloodedEnd(b) ? 1 : 0;
					}
				}
				streams += st;
				pools += po;
				most = Mathf.Max(most, plan.Bodies.Count);
				Console.WriteLine($"{What(s)}: {st} streams ({f} with falls), {po} pools");
			}
			Console.WriteLine($"{systems} wet systems: {streams} streams ({into} into pools), {springs} springs, {chasm} fed chasm falls, {avens} falls on their own ({showers} aven showers); "
				+ $"{pools} pools ({basins} basins and gours, {winzes} winzes, {ends} flooded ends); most bodies {most}");
			Assert.That(systems, Is.GreaterThan(0));
			Assert.That(streams, Is.GreaterThan(0), "streams down the passages");
			Assert.That(springs + chasm + avens, Is.GreaterThan(0), "falls");
			Assert.That(pools, Is.GreaterThan(0), "pools");
		}

		// ── The plan ──────────────────────────────────────────────

		[Test]
		public void ThePlanIsTheSameEachTimeAndAfterStorage()
		{
			foreach (CaveSystem s in Caves())
			{
				CaveStreamPlan a = CaveStreams.Plan(s), b = CaveStreams.Plan(s);
				CaveSystem back = CaveSystem.FromValues(s.ToValues(), s.Origin, s.Seed);
				CaveStreamPlan c = CaveStreams.Plan(back);
				foreach (CaveStreamPlan other in new[] { b, c })
				{
					Assert.That(other.Bodies.Count, Is.EqualTo(a.Bodies.Count), What(s));
					for (int i = 0; i < a.Bodies.Count; i++)
					{
						Assert.That(other.Bodies[i].Kind, Is.EqualTo(a.Bodies[i].Kind), $"{What(s)} body {i}");
						Assert.That(other.Bodies[i].Path, Is.EqualTo(a.Bodies[i].Path), $"{What(s)} body {i}");
						Assert.That(other.Bodies[i].Width, Is.EqualTo(a.Bodies[i].Width), $"{What(s)} body {i}");
						Assert.That(other.Bodies[i].Banks, Is.EqualTo(a.Bodies[i].Banks), $"{What(s)} body {i}");
						Assert.That(other.Bodies[i].Falls, Is.EqualTo(a.Bodies[i].Falls), $"{What(s)} body {i}");
					}
				}
				foreach (CaveStreamBody body in a.Bodies)
				{
					Vector3 p = body.Path[body.Path.Length - 1] + Vector3.down * 0.3f;
					Assert.That(back.Void(p), Is.EqualTo(s.Void(p)), $"{What(s)}: the same field at {p}");
				}
			}
		}

		[Test]
		public void StreamsRunDownhillAndTheirFallsFallFarEnoughToBeDrawn()
		{
			foreach (CaveSystem s in Wet())
			{
				CaveStreamPlan plan = CaveStreams.Plan(s);
				for (int i = 0; i < plan.Bodies.Count; i++)
				{
					CaveStreamBody b = plan.Bodies[i];
					string what = $"{What(s)} body {i} ({b.Kind})";
					Assert.That(b.Path, Is.Not.Null.And.Not.Empty, what);
					// Never down in an awash cave's own water (a grotto's pool floods every void under its level): every bed over it.
					for (int k = 0; !float.IsNaN(s.WaterLevel) && k < b.Path.Length; k++)
					{
						bool falling = b.Falls != null && (b.Falls[k] || k + 1 < b.Path.Length && b.Falls[k + 1]);
						if (!falling)
						{
							Assert.That(s.Origin.y + b.Path[k].y - b.Depth, Is.GreaterThanOrEqualTo(s.WaterLevel + CaveSystem.AwashClear - 1e-3f), $"{what}: its bed over the grotto's pool at {k}");
						}
					}
					if (b.Kind == CaveStreamKind.Pool)
					{
						Assert.That(b.Path.Length, Is.EqualTo(1), what);
						Assert.That(b.Width, Is.GreaterThan(0.5f), what);
						Assert.That(b.Depth, Is.GreaterThan(0.1f), what);
						if (b.Banks != null)
						{
							Assert.That(b.Banks[0] - b.Path[0].y, Is.GreaterThanOrEqualTo(CaveStreams.PoolDrop - 1e-3f), $"{what}: under its rim");
						}
						continue;
					}
					Assert.That(b.Path.Length, Is.GreaterThanOrEqualTo(3), what);
					Assert.That(b.Banks, Has.Length.EqualTo(b.Path.Length), what);
					if (b.Falls != null)
					{
						Assert.That(b.Falls, Has.Length.EqualTo(b.Path.Length), what);
						Assert.That(b.Falls[0], Is.False, $"{what}: a stream never starts falling");
					}
					for (int k = 1; k < b.Path.Length; k++)
					{
						Assert.That(b.Path[k].y, Is.LessThanOrEqualTo(b.Path[k - 1].y + 1e-4f), $"{what}: never rising, at point {k}");
						if (!float.IsNaN(b.Banks[k]))
						{
							Assert.That(b.Banks[k] - b.Path[k].y, Is.InRange(CaveStreams.Drop - 1e-3f, CaveStreams.MaxSink + 1e-3f), $"{what}: under its bank, at point {k}");
						}
					}
					// Each fall: from its lip (the point before the first falling one) to its foot drops far enough to be drawn as one.
					for (int k = 1; b.Falls != null && k < b.Path.Length; k++)
					{
						if (!b.Falls[k] || b.Falls[k - 1])
						{
							continue;
						}
						int foot = k;
						while (foot + 1 < b.Path.Length && b.Falls[foot + 1])
						{
							foot++;
						}
						Assert.That(foot, Is.GreaterThan(k), $"{what}: a fall has a point midway (the inland water finds a fall by one)");
						Assert.That(b.Path[k - 1].y - b.Path[foot].y, Is.GreaterThanOrEqualTo(CaveStreams.MinFall - 1e-3f), $"{what}: its fall at {k} drops far enough to be drawn");
					}
					if (b.Into >= 0)
					{
						CaveStreamBody pool = plan.Bodies[b.Into];
						Vector3 end = b.Path[b.Path.Length - 1];
						Assert.That(pool.Kind, Is.EqualTo(CaveStreamKind.Pool), what);
						Assert.That(new Vector2(end.x - pool.Path[0].x, end.z - pool.Path[0].z).magnitude, Is.LessThanOrEqualTo(pool.Width + 0.25f), $"{what}: ends in its pool");
						Assert.That(pool.Path[0].y, Is.LessThanOrEqualTo(b.Path[b.Path.Length - 2].y + 1e-3f), $"{what}: its pool lies under it");
					}
				}
			}
		}

		[Test]
		public void EveryBodyLiesInTheBedCutForItUnderTheFloorBesideIt()
		{
			foreach (CaveSystem s in Wet())
			{
				CaveStreamPlan plan = CaveStreams.Plan(s);
				for (int i = 0; i < plan.Bodies.Count; i++)
				{
					CaveStreamBody b = plan.Bodies[i];
					string what = $"{What(s)} body {i} ({b.Kind})";
					for (int k = 0; k < b.Path.Length; k++)
					{
						Vector3 p = b.Path[k];
						bool lip = b.Falls != null && !b.Falls[k] && k + 1 < b.Path.Length && b.Falls[k + 1];
						bool falling = b.Falls != null && b.Falls[k];
						if (lip)
						{
							// A spring's crack or a chasm's lip: at the rock's face, not out in a room nor deep in the rock.
							Assert.That(s.Void(p), Is.LessThan(0.3f), $"{what}: its lip at the rock's face, point {k} {p}");
							continue;
						}
						Assert.That(s.Void(p + Vector3.up * 0.2f), Is.LessThan(0f), $"{what}: open over its surface at point {k} {p}");
						if (!falling)
						{
							Assert.That(s.Void(p + Vector3.down * 0.2f), Is.LessThan(0f), $"{what}: open under its surface at point {k} {p}");
						}
					}
					if (b.Kind != CaveStreamKind.Stream)
					{
						continue;
					}
					// The bench beside it: the floor across it on one side at least stands over the water.
					for (int k = 0; k < b.Path.Length; k += 4)
					{
						Vector3 p = b.Path[k];
						// (Where it runs into its pool, the pool's bowl is round it.)
						bool pooled = b.Into >= 0 && new Vector2(p.x - plan.Bodies[b.Into].Path[0].x, p.z - plan.Bodies[b.Into].Path[0].z).magnitude < plan.Bodies[b.Into].Width + 1.5f;
						if (float.IsNaN(b.Banks[k]) || pooled)
						{
							continue;
						}
						Vector3 run = b.Path[Mathf.Min(b.Path.Length - 1, k + 1)] - b.Path[Mathf.Max(0, k - 1)];
						run.y = 0f;
						run.Normalize();
						var across = new Vector3(run.z, 0f, -run.x) * (0.5f * b.Width + 0.5f);
						float floor = float.NegativeInfinity;
						foreach (float side in new[] { 1f, -1f })
						{
							var start = new Vector3(p.x + side * across.x, b.Banks[k] + 1.2f, p.z + side * across.z);
							if (s.Void(start) < 0f)
							{
								floor = Mathf.Max(floor, FloorUnder(s, start));
							}
						}
						Assert.That(floor, Is.GreaterThan(p.y + 0.3f), $"{what}: the bench beside point {k} stands over the water");
					}
				}
			}
		}

		// ── The ways ──────────────────────────────────────────────

		[Test]
		public void EveryWayThroughAWetCaveIsStillWalkedDry()
		{
			int walked = 0, layouts = 0;
			foreach (CaveSystem s in Wet())
			{
				CaveStreamPlan plan = CaveStreams.Plan(s);
				foreach ((string name, List<Vector3> points) in Ways(s))
				{
					string wet = WalkFailure(s, points, plan, out Vector3 at);
					walked++;
					if (wet == null)
					{
						continue;
					}
					s.StreamsHeld = true;
					string dry = WalkFailure(s, points, null, out _);
					s.StreamsHeld = false;
					if (dry != null)
					{
						Console.WriteLine($"the layout's own: {What(s)} {name}: {dry}");
						layouts++;
						continue;
					}
					Assert.Fail($"{What(s)} {name}: {wet}");
				}
			}
			Console.WriteLine($"{walked} ways walked, {layouts} of the layout's own faults");
			Assert.That(walked, Is.GreaterThan(0));
		}

		// ── Unchanged where dry or frozen ─────────────────────────

		[Test]
		public void ADryCaveAMoltenTubeAndAnIceCaveAreCutAsBefore()
		{
			var others = new List<CaveSystem>();
			foreach (CaveSystem s in Caves())
			{
				CaveStreamPlan plan = CaveStreams.Plan(s);
				if (!plan.Active || plan.Frozen)
				{
					others.Add(s);
				}
			}
			for (int seed = 1; seed <= 6; seed++)
			{
				CaveSystem tube = PlanCave(CaveForm.LavaTube, 1, 100 + seed);
				if (CaveLava.Molten(tube))
				{
					others.Add(tube);
					break;
				}
			}
			Assert.That(others.Exists(o => o.Form == CaveForm.IceCave), "an ice cave among them");
			Assert.That(others.Exists(o => CaveLava.Molten(o)), "a molten tube among them");
			foreach (CaveSystem s in others)
			{
				Assert.That(s.StreamsCut, Is.False, What(s));
				var probes = Probes(s);
				var before = new float[probes.Count];
				for (int i = 0; i < probes.Count; i++)
				{
					before[i] = s.Void(probes[i]);
				}
				s.StreamsHeld = true;
				for (int i = 0; i < probes.Count; i++)
				{
					Assert.That(s.Void(probes[i]), Is.EqualTo(before[i]), $"{What(s)}: the same field at {probes[i]}");
				}
				s.StreamsHeld = false;
			}
		}

		[Test]
		public void HoldingTheLavaLeavesTheWaterCut()
		{
			foreach (CaveSystem s in Wet())
			{
				Assert.That(s.StreamsCut, Is.True, What(s));
				s.LavaHeld = true;
				Assert.That(s.StreamsCut, Is.True, $"{What(s)}: a check of the lava's leaves the water be");
				s.LavaHeld = false;
				return;
			}
		}

		[Test]
		public void AnIceCavesWaterIsFrozenOnItsFloors()
		{
			int frozen = 0;
			foreach (CaveSystem s in Caves())
			{
				if (s.Form != CaveForm.IceCave)
				{
					continue;
				}
				CaveStreamPlan plan = CaveStreams.Plan(s);
				if (!plan.Active)
				{
					continue;
				}
				frozen++;
				Assert.That(plan.Frozen, Is.True, What(s));
				foreach (CaveStreamBody b in plan.Bodies)
				{
					for (int k = 0; b.Banks != null && k < b.Path.Length; k++)
					{
						if (!float.IsNaN(b.Banks[k]))
						{
							Assert.That(b.Path[k].y, Is.EqualTo(b.Banks[k]), $"{What(s)}: its ice on the floor");
						}
					}
				}
				var lakes = new List<SceneHydrology.Lake>();
				var rivers = new List<SceneHydrology.River>();
				Assert.That(CaveStreamWater.Write(lakes, rivers, 1234, s, plan), Is.EqualTo(0), $"{What(s)}: no water written");
			}
			Console.WriteLine($"{frozen} frozen ice caves");
		}

		// ── The dressing ──────────────────────────────────────────
		[Test]
		public void TheDressingKnowsWhereTheWaterIs()
		{
			int checkedPoints = 0;
			foreach (CaveSystem s in Wet())
			{
				CaveStreamPlan plan = CaveStreams.Plan(s);
				foreach (CaveStreamBody b in plan.Bodies)
				{
					for (int k = 0; k < b.Path.Length; k++)
					{
						bool falling = b.Falls != null && (b.Falls[k] || k + 1 < b.Path.Length && b.Falls[k + 1]);
						if (falling)
						{
							continue;
						}
						Vector3 p = b.Path[k];
						Assert.That(CaveStreamDressing.InWater(plan, p + Vector3.down * 0.1f), Is.True, $"{What(s)}: in its water at {p}");
						// (A steep stream's surface a metre upstream stands nearly half a metre higher.)
						Assert.That(CaveStreamDressing.InWater(plan, p + Vector3.up * 1.2f), Is.False, $"{What(s)}: over its water at {p}");
						checkedPoints++;
					}
				}
			}
			Assert.That(checkedPoints, Is.GreaterThan(0));
			// Nothing for a dry plan, nor for a frozen one (its ice is the ice's pass's).
			Assert.That(CaveStreamDressing.InWater(CaveStreamPlan.Dry, Vector3.zero), Is.False);
			foreach (CaveSystem s in Caves())
			{
				CaveStreamPlan plan = CaveStreams.Plan(s);
				if (plan.Frozen && plan.Bodies.Count > 0)
				{
					Assert.That(CaveStreamDressing.InWater(plan, plan.Bodies[0].Path[0] + Vector3.down * 0.1f), Is.False, What(s));
				}
			}
		}

		// ── The scene's water ─────────────────────────────────────

		[Test]
		public void TheWaterIsWrittenWithIdsOfItsOwn()
		{
			const int site = 0x12345678;
			int pool = CaveShaper.PoolIdBase | (site & 0x00FFFFFF);
			Assert.That(CaveStreamWater.IsCaveStream(pool), Is.False, "the grotto's own pool is not a stream's");
			for (int body = 0; body < CaveStreamWater.MaxBodies; body++)
			{
				int id = CaveStreamWater.IdOf(site, body);
				Assert.That(CaveStreamWater.IsCaveStream(id), Is.True);
				Assert.That(CaveStreamWater.OfSite(id, site), Is.True);
				Assert.That(CaveLavaWater.IsCaveLava(id), Is.False, "not lava");
				Assert.That(id, Is.Not.EqualTo(pool));
				Assert.That(id, Is.GreaterThanOrEqualTo(CaveShaper.PoolIdBase).And.LessThan(MoatShaper.LakeIdBase), "in the grotto pools' range");
			}
			Assert.That(CaveStreamWater.IsCaveStream(CaveLavaWater.IdOf(site, 3)), Is.False, "lava is not water");
			Assert.That(CaveStreams.MaxBodies, Is.EqualTo(CaveStreamWater.MaxBodies));

			int systems = 0;
			foreach (CaveSystem s in Wet())
			{
				CaveStreamPlan plan = CaveStreams.Plan(s);
				var lakes = new List<SceneHydrology.Lake>();
				var rivers = new List<SceneHydrology.River>();
				// Another site's water, the grotto's pool and the lava are left alone.
				lakes.Add(new SceneHydrology.Lake { Id = CaveStreamWater.IdOf(site + 1, 0) });
				lakes.Add(new SceneHydrology.Lake { Id = pool });
				int written = CaveStreamWater.Write(lakes, rivers, site, s, plan);
				Assert.That(written, Is.GreaterThan(0), What(s));
				Assert.That(lakes.Exists(l => l.Id == pool) && lakes.Exists(l => l.Id == CaveStreamWater.IdOf(site + 1, 0)), Is.True, What(s));
				Assert.That(CaveStreamWater.Write(lakes, rivers, site, s, plan), Is.EqualTo(written), $"{What(s)}: written again, the same");
				foreach (SceneHydrology.Lake lake in lakes)
				{
					Assert.That(lake.Lava, Is.False);
				}
				for (int r = 0; r < rivers.Count; r++)
				{
					SceneHydrology.River river = rivers[r];
					Assert.That(river.Lava, Is.False);
					Assert.That(CaveStreamWater.OfSite(river.Id, site), Is.True);
					int n = river.Points.Length;
					Assert.That(river.Reach, Has.Length.EqualTo(n));
					for (int k = 1; k < n; k++)
					{
						Assert.That(river.Points[k].y, Is.LessThanOrEqualTo(river.Points[k - 1].y + 1e-4f));
					}
					// A fall as the inland water finds one: a run of points marked falling, its lip before and foot after a drawn drop.
					for (int k = 0; k < n; k++)
					{
						if (river.Reach[k] != 4 || k > 0 && river.Reach[k - 1] == 4)
						{
							continue;
						}
						int last = k;
						while (last + 1 < n && river.Reach[last + 1] == 4)
						{
							last++;
						}
						Assert.That(k, Is.GreaterThan(0), "a lip before the fall");
						Assert.That(last + 1, Is.LessThan(n), "a foot after it");
						Assert.That(river.Points[k - 1].y - river.Points[last + 1].y, Is.GreaterThanOrEqualTo(1.5f), "drawn as a fall (MinFallMetres)");
					}
					if (river.Finish == SceneHydrology.End.Lake)
					{
						Assert.That(lakes.Exists(l => l.Id == river.EndLake), Is.True, "into a pool written");
					}
				}
				systems++;
			}
			Assert.That(systems, Is.GreaterThan(0));
		}
	}
}
