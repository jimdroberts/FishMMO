using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// How deep caves go (2026-10-21: "We want multiple levels and extremely deep caves but also randomness so there are also
	/// shallow caves"; "randomness in depth up to 500m but also really small caves"; "Stacked tubes + big lakes"; "Cold traps
	/// freeze deep"): a heavy-tailed depth from each cave's seed, levels that follow it all reached from the mouth, the deepest
	/// great chamber of a molten tube holding a broad lake, no lava over a lower level, more lava bodies than four bits held, and
	/// an ice cave frozen wherever it lies under its mouth's sill. On a synthetic escarpment lifted high over the sea, so the sea's
	/// cap does not bind.
	/// </summary>
	[TestFixture]
	public class CaveDepthTests
	{
		/// <summary>How far the escarpment is lifted over the sea, metres: room for the deepest system.</summary>
		private const float Lift = 600f;

		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return Lift + face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		private static readonly Dictionary<(CaveForm, int, int), CaveSystem> planned = new Dictionary<(CaveForm, int, int), CaveSystem>();

		private static CaveSystem Plan(CaveForm form, int size, int seed)
		{
			if (planned.TryGetValue((form, size, seed), out CaveSystem system))
			{
				return system;
			}
			system = PlanFresh(form, size, seed);
			planned[(form, size, seed)] = system;
			return system;
		}

		private static CaveSystem PlanFresh(CaveForm form, int size, int seed)
		{
			float tilt = seed * 13f % 40f - 20f;
			CaveGround ground = CaveGround.Sample(Escarpment(tilt, seed), -700f, -700f, 700f, 700f, 2f);
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed, ground, 0f, out string problem);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			return system;
		}

		private static string What(CaveSystem s) => $"{s.Form} {s.SizeClass} seed {s.Seed / 7919} ({s.Character}, {s.Depth:0} m)";

		/// <summary>The depth a system was planned to (its roll, an abyss's quarter more, the deepest a form goes).</summary>
		private static float Planned(CaveSystem s)
		{
			float depth = CaveSystem.DepthFor(s.Form, s.SizeClass, s.Seed) * (s.Character == CaveCharacter.Abyss ? 1.25f : 1f);
			return Mathf.Min(depth, CaveSystem.MaxDepthOf(s.Form));
		}

		// ── Depth ─────────────────────────────────────────────────

		[Test]
		public void DepthsRunFromPocketsToHundredsOfMetres_TheSameEachTime()
		{
			foreach (int size in new[] { 0, 1, 2 })
			{
				int pockets = 0, deep = 0;
				for (int seed = 1; seed <= 600; seed++)
				{
					float d = CaveSystem.DepthFor(CaveForm.Cave, size, seed * 7919);
					Assert.That(d, Is.InRange(CaveSystem.LeastDepth, CaveSystem.MaxCaveDepth), $"size {size} seed {seed}");
					Assert.That(CaveSystem.DepthFor(CaveForm.Cave, size, seed * 7919), Is.EqualTo(d), "the same seed, the same depth");
					pockets += d < CaveSystem.PocketDepth ? 1 : 0;
					deep += d >= 300f ? 1 : 0;
				}
				Assert.That(pockets, Is.GreaterThan(0), $"size {size}: some caves are pockets a few metres under the hill");
				Assert.That(deep, Is.GreaterThan(0), $"size {size}: some caves go 300 m and more");
				Assert.That(deep, Is.LessThan(pockets + 120), $"size {size}: the deep ones are rare");
			}
			for (int seed = 1; seed <= 300; seed++)
			{
				Assert.That(CaveSystem.DepthFor(CaveForm.LavaTube, 2, seed * 7919), Is.InRange(5f, CaveSystem.MaxLavaDepth), "a lava tube's stack");
			}
		}

		[Test]
		public void ASceneRangeHoldsEveryDepth_AndTheFullRangeChangesNothing()
		{
			// 2026-10-21: "options during the cut window to decide minimum and maximum cave depths" (the pre-cut prompt's range).
			foreach (CaveForm form in new[] { CaveForm.Cave, CaveForm.IceCave, CaveForm.LavaTube })
			{
				foreach (int size in new[] { 0, 1, 2 })
				{
					for (int seed = 1; seed <= 300; seed++)
					{
						int s = seed * 7919;
						Assert.That(CaveSystem.DepthFor(form, size, s, CaveSystem.LeastDepth, CaveSystem.MaxCaveDepth), Is.EqualTo(CaveSystem.DepthFor(form, size, s)),
							$"{form} {size} seed {seed}: the full range is the planner's own");
						foreach ((float least, float most) in new[] { (3f, 15f), (40f, 90f), (200f, 500f), (90f, 40f) })
						{
							CaveSystem.RangeOf(form, least, most, out float lo, out float hi);
							float d = CaveSystem.DepthFor(form, size, s, least, most);
							Assert.That(d, Is.InRange(lo, hi), $"{form} {size} seed {seed} in {least}–{most} m");
						}
					}
				}
			}
			// A range past a lava tube's own (5–120 m) takes the tube's deepest; one under a cave's least, the cave's least.
			Assert.That(CaveSystem.DepthFor(CaveForm.LavaTube, 1, 7919, 200f, 500f), Is.EqualTo(CaveSystem.MaxLavaDepth));
			Assert.That(CaveSystem.DepthFor(CaveForm.Cave, 1, 7919, 0f, 1f), Is.EqualTo(CaveSystem.LeastDepth));
		}

		[Test]
		public void PlannedCavesKeepToTheScenesRange()
		{
			// A tight range may leave a hillside no cave fits in (one that would have dived deeper for its rock): that site is
			// refused, never planned past the range. Most still are.
			var refused = new List<string>();
			int planned = 0;
			for (int seed = 1; seed <= 12; seed++)
			{
				float tilt = seed * 13f % 40f - 20f;
				CaveGround ground = CaveGround.Sample(Escarpment(tilt, seed), -700f, -700f, 700f, 700f, 2f);
				foreach ((float least, float most) in new[] { (3f, 20f), (150f, 260f) })
				{
					CaveSystem system = CaveSystem.Plan(CaveForm.Cave, 1, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed, ground, 0f,
						out string problem, depthLeast: least, depthMost: most);
					if (system == null)
					{
						refused.Add($"seed {seed} in {least}–{most} m: {problem}");
						continue;
					}
					planned++;
					// An abyss goes a quarter deeper than its roll, never past the range's deepest.
					float rolled = CaveSystem.DepthFor(CaveForm.Cave, 1, system.Seed, least, most);
					Assert.That(rolled, Is.InRange(least, most));
					Assert.That(Mathf.Min(rolled * (system.Character == CaveCharacter.Abyss ? 1.25f : 1f), most), Is.LessThanOrEqualTo(most));
					// Its deepest floor too, side branches and alcoves included: the range's deepest is a floor, as the sea's is. To half a
					// metre, the layout's own margin: a branch's first node is its parent's floor, which a sloping hall carries a few
					// centimetres under (Halls seed 11: 20.19 m in 3–20 m).
					Assert.That(system.Depth, Is.LessThanOrEqualTo(most + 0.5f), $"seed {seed} ({system.Character}) planned {system.Depth:0.0} m in {least}–{most} m");
				}
			}
			foreach (string r in refused)
			{
				Console.WriteLine("refused " + r);
			}
			Console.WriteLine($"{planned} planned, {refused.Count} refused");
			Assert.That(planned, Is.GreaterThanOrEqualTo(20), "most sites still hold a cave within the range");
		}

		[Test]
		public void PlannedCavesAreSometimesPocketsAndSometimesHundredsOfMetresDeep()
		{
			CaveSystem shallow = null, deepest = null;
			for (int seed = 1; seed <= 16 && (shallow == null || deepest == null); seed++)
			{
				CaveSystem s = Plan(CaveForm.Cave, 2, seed);
				if (s.Depth < CaveSystem.PocketDepth && shallow == null)
				{
					shallow = s;
				}
				if (s.Depth >= 300f && deepest == null)
				{
					deepest = s;
				}
				if (shallow == null && s.Depth >= CaveSystem.PocketDepth)
				{
					CaveSystem small = Plan(CaveForm.Cave, 0, seed);
					if (small.Depth < CaveSystem.PocketDepth)
					{
						shallow = small;
					}
				}
			}
			Assert.That(shallow, Is.Not.Null, "no pocket among the seeds");
			Assert.That(shallow.Chambers.Count, Is.EqualTo(1), $"{What(shallow)}: a pocket is one chamber");
			Assert.That(shallow.Passages.Count, Is.EqualTo(1), $"{What(shallow)}: off one short passage");
			Assert.That(deepest, Is.Not.Null, "no system 300 m deep among the seeds");
			Assert.That(deepest.Origin.y + deepest.Depth * -1f, Is.GreaterThanOrEqualTo(deepest.LowestFloor - 0.01f), "still over the sea");
			// The same seed plans the same system again.
			CaveSystem again = PlanFresh(deepest.Form, deepest.SizeClass, deepest.Seed / 7919);
			Assert.That(again.ToValues(), Is.EqualTo(deepest.ToValues()), $"{What(deepest)} planned twice");
		}

		[Test]
		public void TheSeaKeepsALowMouthsCaveShallower()
		{
			for (int seed = 1; seed <= 8; seed++)
			{
				float tilt = seed * 13f % 40f - 20f;
				Func<float, float, float> high = Escarpment(tilt, seed);
				// The same escarpment let down so its foot stands 40 m over the sea.
				Func<float, float, float> low = (x, z) => high(x, z) - Lift + 30f;
				CaveSystem s = CaveSystem.Plan(CaveForm.Cave, 2, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed,
					CaveGround.Sample(low, -700f, -700f, 700f, 700f, 2f), 0f, out string problem);
				if (s == null)
				{
					continue;
				}
				foreach (CavePassage p in s.Passages)
				{
					foreach (CaveNode n in p.Nodes)
					{
						Assert.That(n.Floor + s.Origin.y, Is.GreaterThanOrEqualTo(CaveSystem.AboveSea - 0.01f), $"{What(s)}: a floor under the sea's cap");
					}
				}
				foreach (CaveChamber c in s.Chambers)
				{
					Assert.That(c.Floor + s.Origin.y, Is.GreaterThanOrEqualTo(CaveSystem.AboveSea - 0.01f), $"{What(s)}: a chamber under the sea's cap");
				}
			}
		}

		// ── Levels ────────────────────────────────────────────────

		[Test]
		public void LevelsFollowTheDepth_AndEveryChamberIsReachedFromTheMouth()
		{
			int deepSystems = 0;
			for (int seed = 1; seed <= 12; seed++)
			{
				CaveSystem s = Plan(CaveForm.Cave, 2, seed);
				if (s.Character == CaveCharacter.Classic)
				{
					continue;
				}
				float depth = Planned(s);
				int levels = CaveSystem.LevelsForDepth(s.Form, depth, s.Seed);
				Assert.That(levels, Is.InRange(1, 8), What(s));
				if (depth >= 150f)
				{
					Assert.That(levels, Is.GreaterThanOrEqualTo(3), $"{What(s)}: a deep system goes down through several levels");
					deepSystems++;
				}
				// Each level has a chamber near its floor (a drop's landing taken as the level's way in stands partway).
				float[] floors = CaveSystem.LevelFloors(depth, levels, s.Seed, s.LowestFloor - s.Origin.y + 1f);
				float gap = depth / levels;
				for (int i = 0; i < levels; i++)
				{
					bool near = false;
					foreach (CaveChamber c in s.Chambers)
					{
						near |= Mathf.Abs(c.Floor - floors[i]) <= 0.6f * gap + 6f;
					}
					Assert.That(near, Is.True, $"{What(s)}: no chamber near level {i}'s floor {floors[i]:0}");
				}
				bool[] reached = Reached(s);
				for (int c = 0; c < s.Chambers.Count; c++)
				{
					Assert.That(reached[c], Is.True, $"{What(s)}: the {s.Chambers[c].Kind} at {s.Chambers[c].Floor:0} is not reached from the mouth");
				}
			}
			Assert.That(deepSystems, Is.GreaterThan(0), "no seed planned a deep system");
		}

		/// <summary>
		/// Which chambers are reached from the mouth (the first passage's first node): along each passage, from a passage's node
		/// into a chamber it opens into and onto another passage it meets at its floor, up and down every climb (foot and head
		/// each onto the floor nearest it), and round every pit's ledge (its rim's way to the way on from its bottom).
		/// </summary>
		private static bool[] Reached(CaveSystem s)
		{
			var points = new List<Vector3>();
			var radius = new List<float>();
			var first = new List<int>();
			foreach (CavePassage p in s.Passages)
			{
				first.Add(points.Count);
				foreach (CaveNode n in p.Nodes)
				{
					points.Add(new Vector3(n.Centre.x, n.Floor, n.Centre.z));
					radius.Add(n.Radius);
				}
			}
			int nodes = points.Count;
			var edges = new List<int>[nodes + s.Chambers.Count];
			for (int i = 0; i < edges.Length; i++)
			{
				edges[i] = new List<int>();
			}
			void Join(int a, int b)
			{
				edges[a].Add(b);
				edges[b].Add(a);
			}
			float Plan(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
			for (int p = 0; p < s.Passages.Count; p++)
			{
				for (int i = 1; i < s.Passages[p].Nodes.Count; i++)
				{
					Join(first[p] + i - 1, first[p] + i);
				}
			}
			for (int a = 0; a < nodes; a++)
			{
				for (int b = a + 1; b < nodes; b++)
				{
					if (Plan(points[a], points[b]) < radius[a] + radius[b] && Mathf.Abs(points[a].y - points[b].y) < 1.5f)
					{
						Join(a, b);
					}
				}
				for (int c = 0; c < s.Chambers.Count; c++)
				{
					CaveChamber ch = s.Chambers[c];
					if (Plan(points[a], ch.Centre) < Mathf.Max(ch.Radii.x, ch.Radii.z) + radius[a] + 2f && Mathf.Abs(points[a].y - CaveSystem.FloorOf(ch, points[a])) < 3f)
					{
						Join(a, nodes + c);
					}
				}
			}
			// A hall's lobes open into each other (their terraces ramped).
			for (int a = 0; a < s.Chambers.Count; a++)
			{
				for (int b = a + 1; b < s.Chambers.Count; b++)
				{
					CaveChamber ca = s.Chambers[a], cb = s.Chambers[b];
					if (Mathf.Abs(ca.Floor - cb.Floor) < 8f && Plan(ca.Centre, cb.Centre) < 0.85f * (Mathf.Max(ca.Radii.x, ca.Radii.z) + Mathf.Max(cb.Radii.x, cb.Radii.z)))
					{
						Join(nodes + a, nodes + b);
					}
				}
			}
			int Nearest(Vector3 q)
			{
				int best = -1;
				float bestD = 8f;
				for (int i = 0; i < nodes; i++)
				{
					float d = Plan(points[i], q) - radius[i];
					if (Mathf.Abs(points[i].y - q.y) < 2.5f && d < bestD)
					{
						bestD = d;
						best = i;
					}
				}
				for (int c = 0; c < s.Chambers.Count; c++)
				{
					CaveChamber ch = s.Chambers[c];
					float d = Plan(ch.Centre, q) - Mathf.Max(ch.Radii.x, ch.Radii.z);
					if (Mathf.Abs(CaveSystem.FloorOf(ch, q) - q.y) < 2.5f && d < bestD)
					{
						bestD = d;
						best = nodes + c;
					}
				}
				return best;
			}
			float Segment(Vector3 q, Vector3 a, Vector3 b)
			{
				var ab = new Vector2(b.x - a.x, b.z - a.z);
				var aq = new Vector2(q.x - a.x, q.z - a.z);
				float t = Mathf.Clamp01(Vector2.Dot(aq, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				return (aq - ab * t).magnitude;
			}
			// A chasm's floor is walked end to end: every way on it joined, and a climb whose foot stands in it onto them all.
			var inRift = new List<int>[s.Rifts.Count];
			for (int r = 0; r < s.Rifts.Count; r++)
			{
				CaveRift rift = s.Rifts[r];
				inRift[r] = new List<int>();
				for (int i = 0; i < nodes; i++)
				{
					if (Segment(points[i], rift.A, rift.B) < rift.Width + radius[i] + 1f && Mathf.Abs(points[i].y - rift.A.y) < 2.5f)
					{
						inRift[r].Add(i);
					}
				}
				for (int k = 1; k < inRift[r].Count; k++)
				{
					Join(inRift[r][0], inRift[r][k]);
				}
			}
			foreach (CaveClimb climb in s.Climbs)
			{
				int foot = Nearest(climb.Bottom), head = Nearest(climb.Top);
				if (head < 0)
				{
					continue;
				}
				if (foot >= 0)
				{
					Join(foot, head);
				}
				for (int r = 0; r < s.Rifts.Count; r++)
				{
					CaveRift rift = s.Rifts[r];
					if (Segment(climb.Bottom, rift.A, rift.B) < rift.Width + 1.5f && Mathf.Abs(climb.Bottom.y - rift.A.y) < 2.5f)
					{
						foreach (int i in inRift[r])
						{
							Join(head, i);
						}
					}
				}
			}
			foreach (CaveSolid ledge in s.Solids)
			{
				if (ledge.Kind != CaveSolidKind.Spiral)
				{
					continue;
				}
				int top = -1, bottom = -1;
				for (int i = 0; i < nodes; i++)
				{
					if (Plan(points[i], ledge.A) > ledge.Depth + radius[i] + 2f)
					{
						continue;
					}
					top = Mathf.Abs(points[i].y - ledge.B.y) < 2.5f ? i : top;
					bottom = Mathf.Abs(points[i].y - ledge.A.y) < 2.5f ? i : bottom;
				}
				if (top >= 0 && bottom >= 0)
				{
					Join(top, bottom);
				}
			}
			var seen = new bool[edges.Length];
			var queue = new Queue<int>();
			seen[0] = true;
			queue.Enqueue(0);
			while (queue.Count > 0)
			{
				foreach (int next in edges[queue.Dequeue()])
				{
					if (!seen[next])
					{
						seen[next] = true;
						queue.Enqueue(next);
					}
				}
			}
			var reached = new bool[s.Chambers.Count];
			for (int c = 0; c < reached.Length; c++)
			{
				reached[c] = seen[nodes + c];
			}
			return reached;
		}

		[Test]
		public void EveryClimbStillFits()
		{
			for (int seed = 1; seed <= 12; seed++)
			{
				CaveSystem s = Plan(CaveForm.Cave, 2, seed);
				s.Prepare();
				foreach (CaveClimb climb in s.Climbs)
				{
					Assert.That(s.ClimbFits(climb), Is.True, $"{What(s)}: a climb {climb.Bottom.y:0} → {climb.Top.y:0} no longer fits");
				}
			}
		}

		// ── Lava ──────────────────────────────────────────────────

		[Test]
		public void TheDeepestCavernOfAMoltenTubeHoldsABroadLake()
		{
			int molten = 0, broad = 0;
			float broadest = 0f;
			for (int seed = 1; seed <= 16; seed++)
			{
				CaveSystem s = Plan(CaveForm.LavaTube, 2, seed);
				if (!CaveLava.Molten(s))
				{
					continue;
				}
				molten++;
				List<int> deep = s.DeepChambers();
				CaveLavaPlan plan = CaveLava.Plan(s);
				float across = 0f;
				foreach (CaveLavaBody b in plan.Bodies)
				{
					if (b.Kind == CaveLavaKind.Lake && deep.Contains(b.Chamber))
					{
						across = Mathf.Max(across, 2f * b.Width);
					}
				}
				broad += across >= 15f ? 1 : 0;
				broadest = Mathf.Max(broadest, across);
				Assert.That(across, Is.LessThanOrEqualTo(2f * CaveSystem.LakeMost + 0.01f), What(s));
			}
			Assume.That(molten, Is.GreaterThan(4));
			Assert.That(broad, Is.GreaterThanOrEqualTo(molten * 2 / 3), $"{broad} of {molten} molten tubes hold a lake 15 m across in their deepest great chambers");
			Assert.That(broadest, Is.GreaterThanOrEqualTo(25f), "the broadest lakes are 25–30 m across");
		}

		[Test]
		public void LavaTubesStack_AndNoLavaLiesOverALowerLevel()
		{
			int stacked = 0;
			for (int seed = 1; seed <= 16; seed++)
			{
				CaveSystem s = Plan(CaveForm.LavaTube, 2, seed);
				if (s.Character != CaveCharacter.Classic && CaveSystem.LevelsForDepth(s.Form, Planned(s), s.Seed) >= 2)
				{
					stacked++;
				}
				if (!CaveLava.Molten(s))
				{
					continue;
				}
				foreach (CaveLavaBody b in CaveLava.Plan(s).Bodies)
				{
					if (b.Kind == CaveLavaKind.Fall)
					{
						continue;
					}
					float reach = b.Kind == CaveLavaKind.Lake ? b.Width : 0.5f * b.Width;
					foreach (Vector3 p in b.Path)
					{
						float floor = p.y + CaveLava.Drop;
						foreach (CaveChamber c in s.Chambers)
						{
							if (c.Floor < floor - 4f && InPlan(c, p, reach))
							{
								Assert.Fail($"{What(s)}: a {b.Kind} at {p} lies over the chamber at {c.Floor:0}");
							}
						}
						foreach (CavePassage passage in s.Passages)
						{
							for (int i = 0; i + 1 < passage.Nodes.Count; i++)
							{
								CaveNode a = passage.Nodes[i], n = passage.Nodes[i + 1];
								float lower = Mathf.Max(a.Floor, n.Floor);
								if (lower < floor - 4f && PlanDistance(p, a.Centre, n.Centre) < Mathf.Max(a.Radius, n.Radius) + reach - 0.5f)
								{
									Assert.Fail($"{What(s)}: a {b.Kind} at {p} lies over a passage at {lower:0}");
								}
							}
						}
					}
				}
			}
			Assert.That(stacked, Is.GreaterThan(4), "large lava tubes stack their tubes in levels");
		}

		private static bool InPlan(in CaveChamber c, Vector3 p, float grow)
		{
			float yr = c.Yaw * Mathf.Deg2Rad;
			Vector3 o = p - c.Centre;
			float along = o.x * Mathf.Sin(yr) + o.z * Mathf.Cos(yr), across = o.x * Mathf.Cos(yr) - o.z * Mathf.Sin(yr);
			// Well inside its floor's outline (shrunk a little: a neighbour's wall is not under the lava).
			float sx = Mathf.Max(0.5f, 0.85f * c.Radii.x - 1f) + grow, sz = Mathf.Max(0.5f, 0.85f * c.Radii.z - 1f) + grow;
			return along * along / (sx * sx) + across * across / (sz * sz) < 1f;
		}

		private static float PlanDistance(Vector3 p, Vector3 a, Vector3 b)
		{
			var ab = new Vector2(b.x - a.x, b.z - a.z);
			var ap = new Vector2(p.x - a.x, p.z - a.z);
			float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
			return (ap - ab * t).magnitude;
		}

		[Test]
		public void MoreThanSixteenLavaBodiesAreWrittenWithIdsOfTheirOwn()
		{
			const int site = 0x1234567;
			var ids = new HashSet<int>();
			for (int body = 0; body < CaveLavaWater.MaxBodies; body++)
			{
				int id = CaveLavaWater.IdOf(site, body);
				Assert.That(ids.Add(id), Is.True, $"body {body} shares an id");
				Assert.That(CaveLavaWater.OfSite(id, site) && CaveLavaWater.IsCaveLava(id), Is.True);
				Assert.That(CaveStreamWater.IsCaveStream(id), Is.False);
			}
			Assert.That(CaveLavaWater.MaxBodies, Is.GreaterThan(16));
			// A large molten tube's plan past sixteen bodies, every one written.
			CaveSystem many = null;
			CaveLavaPlan plan = null;
			for (int seed = 1; seed <= 16 && many == null; seed++)
			{
				CaveSystem s = Plan(CaveForm.LavaTube, 2, seed);
				CaveLavaPlan p = CaveLava.Plan(s);
				if (p.Bodies.Count > 16)
				{
					many = s;
					plan = p;
				}
			}
			Assert.That(many, Is.Not.Null, "no tube planned more than sixteen bodies of lava");
			var lakes = new List<SceneHydrology.Lake>();
			var rivers = new List<SceneHydrology.River>();
			int written = CaveLavaWater.Write(lakes, rivers, site, many, plan);
			var seen = new HashSet<int>();
			foreach (SceneHydrology.Lake l in lakes)
			{
				seen.Add(l.Id);
			}
			foreach (SceneHydrology.River r in rivers)
			{
				seen.Add(r.Id);
			}
			Assert.That(written, Is.GreaterThan(16), What(many));
			Assert.That(seen.Count, Is.EqualTo(written), $"{What(many)}: every body its own id");
		}

		// ── Ice ───────────────────────────────────────────────────

		[Test]
		public void ADeepIceCaveIsFrozenWhereverItLiesUnderItsSill()
		{
			int deepChambers = 0, systems = 0;
			for (int seed = 1; seed <= 10; seed++)
			{
				CaveSystem s = Plan(CaveForm.IceCave, 2, seed);
				CaveHabitat habitat = CaveHabitat.For(s);
				bool any = false;
				foreach (CaveChamber c in s.Chambers)
				{
					var p = new Vector3(c.Centre.x, c.Floor + 0.5f, c.Centre.z);
					if (p.y < CaveIce.SillOf(s, p) - CaveIce.ColdSinkDepth)
					{
						Assert.That(CaveIce.Cold(habitat, s, p), Is.EqualTo(1f), $"{What(s)}: the chamber at {c.Floor:0}, {habitat.WalkedFromMouth(p):0} m in, is not frozen");
						Assert.That(habitat.Sample(p - Vector3.up * 0.5f, Vector3.up).Wet, Is.LessThanOrEqualTo(0.1f), $"{What(s)}: the floor at {c.Floor:0} is not frozen dry");
						deepChambers++;
						any = true;
					}
				}
				systems += any ? 1 : 0;
				if (s.Chambers.Count >= 24)
				{
					CaveIceLayout ice = CaveIce.Lay(s, habitat, CaveStreams.Plan(s), null, null);
					Assert.That(ice.Curtains + ice.Falls + ice.Sheets, Is.GreaterThan(20), $"{What(s)}: {s.Chambers.Count} chambers' ice is held to a small cave's");
				}
			}
			Assert.That(deepChambers, Is.GreaterThan(10), "deep ice caves have chambers far under their sills");
			Assert.That(systems, Is.GreaterThan(3));
		}
	}
}
