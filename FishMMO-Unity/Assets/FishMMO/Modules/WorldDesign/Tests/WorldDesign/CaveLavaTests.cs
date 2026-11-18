using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Molten lava in the lava tubes (<see cref="CaveLava"/>, CaveSystem.Lava.cs, 2026-10-20: "Lava caves/tunnels/tubes should
	/// have more lava? lava pits? magma? etc?"): most tubes run molten and the rest stay as they were; the plan is the same
	/// each time and after storage; rivers run downhill in their passages; every body lies in the bed the field cut for it,
	/// under the floor beside it; every way through a molten tube is still walked, dry; and a cooled tube, or any other cave,
	/// is cut exactly as before. On the same synthetic escarpment as PointOfInterestCaveTests.
	/// </summary>
	[TestFixture]
	public class CaveLavaTests
	{
		private const float CaveLift = 60f;
		/// <summary>The terrain's hole grid at 1200 m tiles and 512 holes a tile: 2.34 m quads from the scene's corner.</summary>
		private const float HoleStep = 1200f / 512f, GridX = -2400f, GridZ = -2400f;

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

		private static CaveSystem PlanCave(CaveForm form, int size, int seed) => PlanCave(form, size, seed, out _);

		private static CaveSystem PlanCave(CaveForm form, int size, int seed, out Func<float, float, float> g)
		{
			float tilt = seed * 13f % 40f - 20f;
			Func<float, float, float> face = Escarpment(tilt, seed);
			g = (px, pz) => face(px, pz) + CaveLift;
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed,
				CaveGround.Sample(g, -560f, -560f, 560f, 560f, 2f), 0f, out string problem);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			return system;
		}

		private static List<CaveSystem> tubes;

		/// <summary>Lava tubes of every size over several seeds, planned once for the fixture.</summary>
		private static List<CaveSystem> Tubes()
		{
			if (tubes == null)
			{
				tubes = new List<CaveSystem>();
				foreach ((int size, int seeds) in new[] { (0, 10), (1, 14), (2, 10) })
				{
					for (int seed = 1; seed <= seeds; seed++)
					{
						tubes.Add(PlanCave(CaveForm.LavaTube, size, seed));
					}
				}
			}
			return tubes;
		}

		private static IEnumerable<CaveSystem> Molten()
		{
			foreach (CaveSystem s in Tubes())
			{
				if (CaveLava.Molten(s))
				{
					yield return s;
				}
			}
		}

		private static string What(CaveSystem s) => $"{s.Form} {s.SizeClass} seed {s.Seed / 7919} ({s.Character}; {s.Traits})";

		// ── The field, read the fixture's own way ─────────────────

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

		/// <summary>The molten surface under a point in plan, where it lies in a body's footprint; NaN where it does not.</summary>
		private static float LavaUnder(CaveLavaPlan plan, Vector3 p)
		{
			float surface = float.NaN;
			foreach (CaveLavaBody b in plan.Bodies)
			{
				if (b.Kind == CaveLavaKind.Lake)
				{
					if (new Vector2(p.x - b.Path[0].x, p.z - b.Path[0].z).magnitude < b.Width)
					{
						surface = float.IsNaN(surface) ? b.Path[0].y : Mathf.Max(surface, b.Path[0].y);
					}
					continue;
				}
				if (b.Kind != CaveLavaKind.River)
				{
					continue;
				}
				for (int i = 0; i + 1 < b.Path.Length; i++)
				{
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
		/// Walks a chain of points every half metre on the noisy field: 2.2 m of headroom, no step steeper than 42°, no drop of
		/// more than 3 m (as CaveLayoutVarietyTests' walk), and — given a plan — never onto lava (a floor at or under a body's
		/// surface there). Null where it can be walked, else why, and <paramref name="at"/> where.
		/// </summary>
		private static string WalkFailure(CaveSystem system, IList<Vector3> points, CaveLavaPlan plan, out Vector3 at)
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
						float lava = LavaUnder(plan, p);
						// Any floor under the lava's surface, however far under (another level's): the scene's water is read in plan,
						// the highest surface over a point answering, so lava over a lower way would burn whoever walked there.
						if (!float.IsNaN(lava) && floor < lava + 0.3f)
						{
							return $"lava at {p} (floor {floor:0.00}, surface {lava:0.00})";
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

		/// <summary>The ways through a system: every passage's line on its floor, and from each passage's end in a chamber to the chamber's middle.</summary>
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

		/// <summary>The index of a passage's node nearest a point in plan.</summary>
		private static int Nearest(CavePassage passage, Vector3 p)
		{
			int best = 0;
			float nearest = float.MaxValue;
			for (int n = 0; n < passage.Nodes.Count; n++)
			{
				float d = new Vector2(passage.Nodes[n].Centre.x - p.x, passage.Nodes[n].Centre.z - p.z).sqrMagnitude;
				if (d < nearest)
				{
					nearest = d;
					best = n;
				}
			}
			return best;
		}

		// ── Which tubes run molten ────────────────────────────────

		[Test]
		public void MostLavaTubesRunMoltenAndNothingElseDoes()
		{
			int molten = 0, total = 600;
			for (int seed = 1; seed <= total; seed++)
			{
				var tube = new CaveSystem { Form = CaveForm.LavaTube, Seed = seed * 7919 };
				if (CaveLava.Molten(tube))
				{
					molten++;
				}
				foreach (CaveForm other in new[] { CaveForm.Cave, CaveForm.Grotto, CaveForm.SeaCave, CaveForm.IceCave, CaveForm.Mine })
				{
					var cave = new CaveSystem { Form = other, Seed = seed * 7919 };
					Assert.That(CaveLava.Molten(cave), Is.False, $"{other} seed {seed}");
					Assert.That(CaveLava.Plan(cave), Is.SameAs(CaveLavaPlan.Cooled));
				}
			}
			float share = molten / (float)total;
			Console.WriteLine($"{molten} of {total} lava tubes molten ({share:P0})");
			Assert.That(share, Is.InRange(0.64f, 0.76f), "about 70% of lava tubes run molten");
			int planned = 0, plannedMolten = 0;
			foreach (CaveSystem s in Tubes())
			{
				planned++;
				plannedMolten += CaveLava.Molten(s) ? 1 : 0;
			}
			Console.WriteLine($"of the fixture's {planned} planned tubes, {plannedMolten} molten");
			Assert.That(plannedMolten, Is.GreaterThan(0).And.LessThan(planned), "the fixture has molten and cooled tubes both");
		}

		[Test]
		public void AMoltenTubeHasRiversFallsAndLakes()
		{
			int rivers = 0, lakes = 0, pits = 0, falls = 0, chasmFalls = 0, systems = 0, most = 0;
			foreach (CaveSystem s in Molten())
			{
				CaveLavaPlan plan = CaveLava.Plan(s);
				systems++;
				Assert.That(plan.Active, Is.True, What(s));
				Assert.That(plan.Bodies.Count, Is.LessThanOrEqualTo(CaveLava.MaxBodies), What(s));
				Assert.That(s.LavaCut, Is.EqualTo(plan.Bodies.Exists(b => b.Banks != null)), $"{What(s)}: its field cut for its plan");
				int r = 0, l = 0, f = 0;
				foreach (CaveLavaBody b in plan.Bodies)
				{
					r += b.Kind == CaveLavaKind.River ? 1 : 0;
					l += b.Kind == CaveLavaKind.Lake ? 1 : 0;
					pits += b.Kind == CaveLavaKind.Lake && b.Pit >= 0 ? 1 : 0;
					f += b.Kind == CaveLavaKind.Fall ? 1 : 0;
					// (A vent's fall pours into its passage's river; a chasm's down the chasm under its chamber.)
					chasmFalls += b.Kind == CaveLavaKind.Fall && b.Chamber >= 0 ? 1 : 0;
				}
				rivers += r;
				lakes += l;
				falls += f;
				most = Mathf.Max(most, plan.Bodies.Count);
				Console.WriteLine($"{What(s)}: {r} river runs, {l} lakes, {f} falls");
				Assert.That(r, Is.GreaterThan(0), $"{What(s)}: a molten tube runs a river");
			}
			Console.WriteLine($"{systems} molten tubes: {rivers} river runs, {lakes} lakes ({pits} in the variety's basins), {falls} falls ({chasmFalls} down chasms); most bodies {most}");
			Assert.That(systems, Is.GreaterThan(0));
			Assert.That(lakes, Is.GreaterThan(0), "lakes in the chambers");
			Assert.That(falls, Is.GreaterThan(0), "falls");
		}

		// ── The plan ──────────────────────────────────────────────

		[Test]
		public void ThePlanIsTheSameEachTimeAndAfterStorage()
		{
			foreach (CaveSystem s in Molten())
			{
				CaveLavaPlan a = CaveLava.Plan(s), b = CaveLava.Plan(s);
				CaveSystem back = CaveSystem.FromValues(s.ToValues(), s.Origin, s.Seed);
				CaveLavaPlan c = CaveLava.Plan(back);
				foreach (CaveLavaPlan other in new[] { b, c })
				{
					Assert.That(other.Bodies.Count, Is.EqualTo(a.Bodies.Count), What(s));
					for (int i = 0; i < a.Bodies.Count; i++)
					{
						Assert.That(other.Bodies[i].Kind, Is.EqualTo(a.Bodies[i].Kind), $"{What(s)} body {i}");
						Assert.That(other.Bodies[i].Path, Is.EqualTo(a.Bodies[i].Path), $"{What(s)} body {i}");
						Assert.That(other.Bodies[i].Width, Is.EqualTo(a.Bodies[i].Width), $"{What(s)} body {i}");
						Assert.That(other.Bodies[i].Banks, Is.EqualTo(a.Bodies[i].Banks), $"{What(s)} body {i}");
					}
				}
				// The stored cave is the one planned: its lava comes from it, nothing of the lava is stored.
				foreach (CaveLavaBody body in a.Bodies)
				{
					Vector3 p = body.Path[0] + Vector3.down * 0.3f;
					Assert.That(back.Void(p), Is.EqualTo(s.Void(p)), $"{What(s)}: the same field at {p}");
				}
			}
		}

		[Test]
		public void RiversRunDownhillAlongTheirPassagesAndFallsFallForward()
		{
			foreach (CaveSystem s in Molten())
			{
				CaveLavaPlan plan = CaveLava.Plan(s);
				for (int i = 0; i < plan.Bodies.Count; i++)
				{
					CaveLavaBody b = plan.Bodies[i];
					string what = $"{What(s)} body {i} ({b.Kind})";
					Assert.That(b.Path, Is.Not.Null.And.Not.Empty, what);
					switch (b.Kind)
					{
						case CaveLavaKind.River:
						{
							Assert.That(b.Path.Length, Is.GreaterThanOrEqualTo(2), what);
							Assert.That(b.Banks, Has.Length.EqualTo(b.Path.Length), what);
							Assert.That(b.Width, Is.GreaterThanOrEqualTo(CaveLava.MinWidth), what);
							CavePassage passage = s.Passages[b.Passage];
							for (int k = 0; k < b.Path.Length; k++)
							{
								if (k > 0)
								{
									Assert.That(b.Path[k].y, Is.LessThanOrEqualTo(b.Path[k - 1].y + 1e-4f), $"{what}: never rising, at point {k}");
								}
								// (Its last point the next run's head, where it ran on under the rock into it: no bank, nothing cut.)
								if (!float.IsNaN(b.Banks[k]) || k < b.Path.Length - 1)
								{
									Assert.That(b.Banks[k] - b.Path[k].y, Is.InRange(CaveLava.Drop - 1e-3f, CaveLava.MaxSink + 1e-3f), $"{what}: under its bank, at point {k}");
								}
							}
							// Along its passage (but where it runs on over a chamber's floor to a chasm's lip), beside the lane.
							int beside = 0;
							foreach (Vector3 p in b.Path)
							{
								float nearest = float.MaxValue;
								for (int n = 0; n + 1 < passage.Nodes.Count; n++)
								{
									Vector3 a = passage.Nodes[n].Centre, c = passage.Nodes[n + 1].Centre;
									var ab = new Vector2(c.x - a.x, c.z - a.z);
									var ap = new Vector2(p.x - a.x, p.z - a.z);
									float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
									nearest = Mathf.Min(nearest, (ap - ab * t).magnitude);
								}
								beside += nearest >= CaveLava.LaneClearLeast + 0.5f * b.Width - 0.3f && nearest <= CaveLava.LaneClear + 0.5f * b.Width + 0.3f ? 1 : 0;
							}
							Assert.That(beside, Is.GreaterThanOrEqualTo(b.Path.Length * 0.6f), $"{what}: beside its passage's lane");
							// Downhill: it runs toward its passage's lower end, and its banks never rise past what its surface sinks under them.
							int from = Nearest(passage, b.Path[0]), to = Nearest(passage, b.Path[b.Path.Length - 1]);
							float upper = passage.Nodes[0].Floor, lower = passage.Nodes[passage.Nodes.Count - 1].Floor;
							if (to < from)
							{
								(upper, lower) = (lower, upper);
							}
							Assert.That(lower, Is.LessThanOrEqualTo(upper + 0.3f), $"{what}: running toward its passage's lower end");
							Assert.That(b.Banks[b.Banks.Length - 1] - b.Banks[0], Is.LessThanOrEqualTo(CaveLava.MaxSink - CaveLava.Drop + 1e-3f), what);
							break;
						}
						case CaveLavaKind.Fall:
						{
							Assert.That(b.Path.Length, Is.GreaterThanOrEqualTo(2), what);
							Vector3 lip = b.Path[0], land = b.Path[b.Path.Length - 1];
							float drop = lip.y - land.y, run = new Vector2(land.x - lip.x, land.z - lip.z).magnitude;
							Assert.That(drop, Is.GreaterThan(0.5f), $"{what}: it falls");
							Assert.That(run, Is.GreaterThanOrEqualTo(0.45f * drop), $"{what}: forward half as far as it drops ({run:0.0} m in {drop:0.0} m), as a fall is drawn");
							break;
						}
						default:
							Assert.That(b.Path.Length, Is.EqualTo(1), what);
							Assert.That(b.Width, Is.GreaterThan(0.5f), what);
							if (b.Pit >= 0)
							{
								Assert.That(b.Banks, Is.Null, $"{what}: a variety basin is not cut again");
								Assert.That(b.Path[0].y, Is.LessThan(s.Pits[b.Pit].Rim.y - CaveLava.Drop + 1e-3f), what);
							}
							else
							{
								Assert.That(b.Banks, Has.Length.EqualTo(1), what);
							}
							break;
					}
				}
			}
		}

		[Test]
		public void EveryBodyLiesInTheBedCutForItUnderTheFloorBesideIt()
		{
			foreach (CaveSystem s in Molten())
			{
				CaveLavaPlan plan = CaveLava.Plan(s);
				for (int i = 0; i < plan.Bodies.Count; i++)
				{
					CaveLavaBody b = plan.Bodies[i];
					string what = $"{What(s)} body {i} ({b.Kind})";
					if (b.Kind == CaveLavaKind.Fall)
					{
						Vector3 land = b.Path[b.Path.Length - 1];
						Assert.That(s.Void(land + Vector3.up * 0.2f), Is.LessThan(0f), $"{what}: it lands in the open");
						Assert.That(s.Void(b.Path[0]), Is.LessThan(0.8f), $"{what}: its lip (a vent) at the rock's face, not out in a room or deep in the rock");
						continue;
					}
					for (int k = 0; k < b.Path.Length; k++)
					{
						Vector3 p = b.Path[k];
						Assert.That(s.Void(p + Vector3.down * 0.3f), Is.LessThan(0f), $"{what}: open under its surface at point {k} {p}");
						Assert.That(s.Void(p + Vector3.up * 0.2f), Is.LessThan(0f), $"{what}: open over its surface at point {k} {p}");
					}
					if (b.Kind == CaveLavaKind.River)
					{
						// The bench beside it: the floor across it on one side at least (its passage's lane's) stands a drop over the lava.
						for (int k = 0; k < b.Path.Length; k += 4)
						{
							Vector3 p = b.Path[k];
							if (float.IsNaN(b.Banks[k]))
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
								// (The wall's side, rock at a stride over the bank, is no bench.)
								if (s.Void(start) < 0f)
								{
									floor = Mathf.Max(floor, FloorUnder(s, start));
								}
							}
							Assert.That(floor, Is.GreaterThan(p.y + 0.4f), $"{what}: the bench beside point {k} stands over the lava");
						}
					}
				}
			}
		}

		// ── The ways ──────────────────────────────────────────────

		[Test]
		public void EveryWayThroughAMoltenTubeIsStillWalkedDry()
		{
			int walked = 0, layouts = 0;
			foreach (CaveSystem s in Molten())
			{
				CaveLavaPlan plan = CaveLava.Plan(s);
				foreach ((string name, List<Vector3> points) in Ways(s))
				{
					string molten = WalkFailure(s, points, plan, out Vector3 at);
					walked++;
					if (molten == null)
					{
						continue;
					}
					// A way the layout itself cannot walk (without the lava, CaveLayoutVarietyTests logs those) is not the lava's.
					s.LavaHeld = true;
					string cooled = WalkFailure(s, points, null, out _);
					s.LavaHeld = false;
					if (cooled != null)
					{
						Console.WriteLine($"the layout's own: {What(s)} {name}: {cooled}");
						layouts++;
						continue;
					}
					Assert.Fail($"{What(s)} {name}: {molten}");
				}
			}
			Console.WriteLine($"{walked} ways walked, {layouts} of the layout's own faults");
			Assert.That(walked, Is.GreaterThan(0));
		}

		// ── Unchanged where cooled ────────────────────────────────

		[Test]
		public void ACooledTubeAndEveryOtherCaveAreCutAsBefore()
		{
			var others = new List<CaveSystem>();
			foreach (CaveSystem s in Tubes())
			{
				if (!CaveLava.Molten(s))
				{
					others.Add(s);
				}
			}
			others.Add(PlanCave(CaveForm.Cave, 1, 2));
			others.Add(PlanCave(CaveForm.Mine, 1, 2));
			Assert.That(others.Exists(o => o.Form == CaveForm.LavaTube), "a cooled tube among them");
			foreach (CaveSystem s in others)
			{
				Assert.That(s.LavaCut, Is.False, What(s));
				var probes = Probes(s);
				var before = new float[probes.Count];
				for (int i = 0; i < probes.Count; i++)
				{
					before[i] = s.Void(probes[i]);
				}
				s.LavaHeld = true;
				for (int i = 0; i < probes.Count; i++)
				{
					Assert.That(s.Void(probes[i]), Is.EqualTo(before[i]), $"{What(s)}: the same field at {probes[i]}");
				}
				s.LavaHeld = false;
			}
			// A molten tube only gains void, and only at its lava.
			foreach (CaveSystem s in Molten())
			{
				CaveLavaPlan plan = CaveLava.Plan(s);
				var probes = Probes(s);
				var cut = new float[probes.Count];
				for (int i = 0; i < probes.Count; i++)
				{
					cut[i] = s.Void(probes[i]);
				}
				s.LavaHeld = true;
				int changed = 0, opened = 0;
				for (int i = 0; i < probes.Count; i++)
				{
					float held = s.Void(probes[i]);
					if (cut[i] == held)
					{
						continue;
					}
					changed++;
					Assert.That(cut[i], Is.LessThan(held), $"{What(s)}: only void gained, at {probes[i]}");
					if (cut[i] >= 0f || held < 0f)
					{
						// The field's distance in the rock round the bed shifts with it; the cave's face moves only at the lava.
						continue;
					}
					opened++;
					bool near = false;
					foreach (CaveLavaBody b in plan.Bodies)
					{
						foreach (Vector3 p in b.Path)
						{
							near |= new Vector2(p.x - probes[i].x, p.z - probes[i].z).magnitude < 0.5f * b.Width + (b.Kind == CaveLavaKind.Lake ? 0.5f * b.Width : 0f) + 2f;
						}
					}
					Assert.That(near, Is.True, $"{What(s)}: opened only at its lava, not at {probes[i]}");
				}
				s.LavaHeld = false;
				Console.WriteLine($"{What(s)}: {opened} of {probes.Count} probes opened ({changed} changed)");
			}
		}

		// ── The rock ──────────────────────────────────────────────

		[Test]
		public void AMoltenTubesRockIsStillClosed()
		{
			// The most bodied molten tube of a size: its channels and basins cut into the rock the mesher closes as before.
			foreach (int size in new[] { 1, 2 })
			{
				CaveSystem system = null;
				int seed = 0, most = -1;
				for (int s = 1; s <= 8; s++)
				{
					CaveSystem candidate = Tubes().Find(t => t.SizeClass == size && t.Seed == s * 7919);
					if (candidate != null && CaveLava.Molten(candidate) && CaveLava.Plan(candidate).Bodies.Count > most)
					{
						system = candidate;
						seed = s;
						most = CaveLava.Plan(candidate).Bodies.Count;
					}
				}
				Assert.That(system, Is.Not.Null, $"a molten tube of size {size}");
				system = PlanCave(CaveForm.LavaTube, size, seed, out Func<float, float, float> g);
				system.HoleCells(0, g, HoleStep, GridX, GridZ);
				Bounds e = system.Extent;
				CaveGround ground = CaveGround.Sample(g, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f, system.Origin.x + e.max.x + 4f, system.Origin.z + e.max.z + 4f, HoleStep, GridX, GridZ);
				List<CaveMesher.Piece> pieces = CaveMesher.Build(system, ground, out _, true);
				var all = new MeshBuilder(1);
				long triangles = 0;
				int biggest = 0;
				foreach (CaveMesher.Piece piece in pieces)
				{
					all.Append(piece.Mesh);
					triangles += piece.Mesh.TriangleCount;
					biggest = Mathf.Max(biggest, piece.Mesh.TriangleCount);
				}
				(int odd, int pinched) = OpenEdges(all);
				Console.WriteLine($"{What(system)}: {most} bodies, {triangles:N0} triangles (hidden faces kept), biggest piece {biggest:N0}; {odd} odd edges, {pinched} pinched");
				Assert.That(odd, Is.Zero, $"{What(system)}: edges on an odd number of faces (a seam between pieces, or a hole)");
				Assert.That(pinched, Is.LessThanOrEqualTo(8), $"{What(system)}: pinched edges (on four or more faces)");
			}
		}

		/// <summary>Edges on an odd number of faces, and on four or more (vertices welded by position), as PointOfInterestCaveTests counts them.</summary>
		private static (int odd, int pinched) OpenEdges(MeshBuilder m)
		{
			var weld = new Dictionary<Vector3, int>();
			var use = new Dictionary<(int, int), int>();
			List<int> t = m.Submeshes[0];
			int Id(int v)
			{
				if (!weld.TryGetValue(m.Positions[v], out int id))
				{
					weld[m.Positions[v]] = id = weld.Count;
				}
				return id;
			}
			for (int i = 0; i < t.Count; i += 3)
			{
				int a = Id(t[i]), b = Id(t[i + 1]), c = Id(t[i + 2]);
				foreach ((int x, int y) in new[] { (a, b), (b, c), (c, a) })
				{
					var key = x < y ? (x, y) : (y, x);
					use[key] = use.TryGetValue(key, out int n) ? n + 1 : 1;
				}
			}
			int odd = 0, pinched = 0;
			foreach (int n in use.Values)
			{
				odd += n % 2 == 1 ? 1 : 0;
				pinched += n > 2 && n % 2 == 0 ? 1 : 0;
			}
			return (odd, pinched);
		}

		/// <summary>Points over and round a system's floors: along every passage across its bore at heights from under its floor to its roof.</summary>
		private static List<Vector3> Probes(CaveSystem s)
		{
			var probes = new List<Vector3>();
			foreach (CavePassage p in s.Passages)
			{
				for (int i = 0; i < p.Nodes.Count; i += 2)
				{
					CaveNode n = p.Nodes[i];
					Vector3 run = (p.Nodes[Mathf.Min(p.Nodes.Count - 1, i + 1)].Centre - p.Nodes[Mathf.Max(0, i - 1)].Centre);
					run.y = 0f;
					run = run.sqrMagnitude > 1e-6f ? run.normalized : Vector3.forward;
					var side = new Vector3(run.z, 0f, -run.x);
					for (float x = -1.1f; x <= 1.1f; x += 0.275f)
					{
						for (float y = -3f; y <= 2f; y += 0.5f)
						{
							probes.Add(new Vector3(n.Centre.x, n.Floor, n.Centre.z) + side * (x * n.Radius) + Vector3.up * y);
						}
					}
				}
			}
			foreach (CaveChamber c in s.Chambers)
			{
				for (int k = 0; k < 16; k++)
				{
					float a = k * Mathf.PI / 8f;
					for (float f = 0f; f <= 0.9f; f += 0.3f)
					{
						var at = c.Centre + new Vector3(Mathf.Sin(a) * c.Radii.x * f, 0f, Mathf.Cos(a) * c.Radii.z * f);
						at.y = CaveSystem.FloorOf(c, at);
						probes.Add(at + Vector3.down * 1f);
						probes.Add(at + Vector3.up * 0.5f);
					}
				}
			}
			return probes;
		}
	}
}
