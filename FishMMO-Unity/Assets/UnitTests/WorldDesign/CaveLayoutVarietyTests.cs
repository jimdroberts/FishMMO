using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A cave system's personality and the variety it lays (<see cref="CaveSystem.TraitsFor"/>, CaveSystem.Variety.cs,
	/// 2026-10-20: "We would like more random terraces, pits, etc in caves too. Realism and deterministic randomness is good
	/// too. We want caves to feel unique"): traits differ from seed to seed and within one character; terraces are walked up
	/// onto and along; every pit has a way out; the ways stay walkable; the rock stays within its budget; and the same seed
	/// gives the same system, stored and rebuilt exactly. On the same synthetic escarpment as PointOfInterestCaveTests.
	/// </summary>
	[TestFixture]
	public class CaveLayoutVarietyTests
	{
		private const float HoleStep = 1200f / 512f, GridX = -2400f, GridZ = -2400f;
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

		private static List<CaveSystem> planned;

		/// <summary>Caves of every size, ice caves, lava tubes and mines over several seeds each, planned once for the fixture.</summary>
		private static List<CaveSystem> Planned()
		{
			if (planned == null)
			{
				planned = new List<CaveSystem>();
				foreach ((CaveForm form, int size, int seeds) in new[]
				{
					(CaveForm.Cave, 0, 6), (CaveForm.Cave, 1, 14), (CaveForm.Cave, 2, 7), (CaveForm.IceCave, 1, 4), (CaveForm.LavaTube, 1, 4), (CaveForm.LavaTube, 2, 2),
					(CaveForm.Mine, 1, 4), (CaveForm.Mine, 2, 4),
				})
				{
					for (int seed = 1; seed <= seeds; seed++)
					{
						planned.Add(PlanCave(form, size, seed, out _));
					}
				}
			}
			return planned;
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

		/// <summary>
		/// Walks a chain of points every half metre on the noisy field: 2.2 m of headroom, no step steeper than 42° (the NavMesh
		/// agent walks 45°), and no drop of more than 3 m (it would be a fall, not a way) — as PointOfInterestCaveTests' walk.
		/// Null where it can be walked, else why, and <paramref name="at"/> where.
		/// </summary>
		private static string WalkFailure(CaveSystem system, IList<Vector3> points, out Vector3 at)
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

		private static void AssertWalkable(CaveSystem system, IList<Vector3> points, string what)
		{
			string why = WalkFailure(system, points, out _);
			Assert.That(why, Is.Null, $"{what}: {why}");
		}

		/// <summary>Whether a point lies within <paramref name="reach"/> of anything the variety pass laid (its hollows, terraces and their ramps, mounds, arches, pits).</summary>
		private static bool NearVariety(CaveSystem s, Vector3 p, float reach)
		{
			bool Near(Vector3 a, Vector3 b, float r)
			{
				var ab = new Vector2(b.x - a.x, b.z - a.z);
				var ap = new Vector2(p.x - a.x, p.z - a.z);
				float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				return (ap - ab * t).magnitude < r + reach && p.y > Mathf.Min(a.y, b.y) - r - reach && p.y < Mathf.Max(a.y, b.y) + r + reach;
			}
			foreach (CaveHollow h in s.Hollows)
			{
				if (Near(h.A, h.Kind == CaveHollowKind.Trench ? h.B : h.A, h.Radius + h.Depth))
				{
					return true;
				}
			}
			foreach (CaveSolid o in s.Solids)
			{
				if ((o.Kind == CaveSolidKind.Terrace || o.Kind == CaveSolidKind.Rubble || o.Kind == CaveSolidKind.Arch) && Near(o.A, o.Kind == CaveSolidKind.Rubble ? o.A : o.B, o.Width + o.Depth))
				{
					return true;
				}
			}
			foreach (CaveTerrace t in s.Terraces)
			{
				if (t.Ramp >= 0 && Near(s.Solids[t.Ramp].A, s.Solids[t.Ramp].B, s.Solids[t.Ramp].Width))
				{
					return true;
				}
			}
			foreach (CavePit pit in s.Pits)
			{
				if (Near(pit.Rim, new Vector3(pit.Rim.x, pit.Bottom, pit.Rim.z), pit.Radius))
				{
					return true;
				}
			}
			return false;
		}


		// ── Personality ───────────────────────────────────────────

		[Test]
		public void TraitsDifferFromSeedToSeedAndStayInTheirBounds()
		{
			foreach ((CaveForm form, int size) in new[] { (CaveForm.Cave, 0), (CaveForm.Cave, 1), (CaveForm.Cave, 2), (CaveForm.IceCave, 1), (CaveForm.LavaTube, 1), (CaveForm.Mine, 1), (CaveForm.Grotto, 1) })
			{
				var seen = new HashSet<CaveTrait>();
				for (int seed = 1; seed <= 60; seed++)
				{
					CaveCharacter character = CaveSystem.CharacterFor(form, size, (seed * 0.137f) % 1f);
					CaveTrait traits = CaveSystem.TraitsFor(form, size, character, seed * 7919);
					Assert.That(CaveSystem.TraitsFor(form, size, character, seed * 7919), Is.EqualTo(traits), "the same seed, the same traits");
					int count = 0;
					for (int bit = 0; bit < 16; bit++)
					{
						count += ((int)traits >> bit) & 1;
					}
					bool awash = form == CaveForm.Grotto || form == CaveForm.SeaCave;
					Assert.That(count, Is.InRange(awash || form == CaveForm.Mine ? 1 : 2, awash ? 2 : form == CaveForm.Mine ? 3 : 5), $"{form} {size} seed {seed}: {traits}");
					// Each form keeps to its own processes: a mine only its workings, a lava tube no solution features.
					const CaveTrait workings = CaveTrait.Stoped | CaveTrait.Collapsed | CaveTrait.Sumped;
					if (form == CaveForm.Mine)
					{
						Assert.That(traits & ~workings, Is.EqualTo(CaveTrait.None), $"mine seed {seed}: {traits}");
					}
					else
					{
						Assert.That(traits & workings, Is.EqualTo(CaveTrait.None), $"{form} seed {seed}: {traits}");
					}
					if (form == CaveForm.LavaTube)
					{
						Assert.That(traits & (CaveTrait.Rimstone | CaveTrait.Fissured | CaveTrait.ColdSink), Is.EqualTo(CaveTrait.None), $"lava tube seed {seed}: {traits}");
					}
					seen.Add(traits);
				}
				Assert.That(seen.Count, Is.GreaterThanOrEqualTo(form == CaveForm.Mine || form == CaveForm.Grotto ? 4 : 10), $"{form} {size}: only {seen.Count} personalities in 60 seeds");
			}
		}

		[Test]
		public void CavesOfOneCharacterStillDiffer()
		{
			var byCharacter = new Dictionary<CaveCharacter, HashSet<CaveTrait>>();
			foreach (CaveSystem s in Planned())
			{
				Assert.That(s.Traits, Is.EqualTo(CaveSystem.TraitsFor(s.Form, s.SizeClass, s.Character, s.Seed)), $"{What(s)}: its traits are its seed's");
				if (!byCharacter.TryGetValue(s.Character, out HashSet<CaveTrait> set))
				{
					byCharacter[s.Character] = set = new HashSet<CaveTrait>();
				}
				set.Add(s.Traits);
			}
			int varied = 0;
			foreach (KeyValuePair<CaveCharacter, HashSet<CaveTrait>> pair in byCharacter)
			{
				varied += pair.Value.Count >= 2 ? 1 : 0;
			}
			Assert.That(varied, Is.GreaterThanOrEqualTo(2), "two characters each seen with more than one personality");
		}

		[Test]
		public void TheVarietyLaysItsFeatures()
		{
			var terraces = new Dictionary<CaveTerraceKind, int>();
			var pits = new Dictionary<CavePitKind, int>();
			var hollows = new Dictionary<CaveHollowKind, int>();
			int rubble = 0, arches = 0, climbedTerraces = 0;
			foreach (CaveSystem s in Planned())
			{
				foreach (CaveTerrace t in s.Terraces)
				{
					terraces[t.Kind] = terraces.TryGetValue(t.Kind, out int n) ? n + 1 : 1;
					climbedTerraces += t.Climb >= 0 ? 1 : 0;
				}
				foreach (CavePit p in s.Pits)
				{
					pits[p.Kind] = pits.TryGetValue(p.Kind, out int n) ? n + 1 : 1;
				}
				foreach (CaveHollow h in s.Hollows)
				{
					hollows[h.Kind] = hollows.TryGetValue(h.Kind, out int n) ? n + 1 : 1;
				}
				rubble += s.Solids.FindAll(o => o.Kind == CaveSolidKind.Rubble).Count;
				arches += s.Solids.FindAll(o => o.Kind == CaveSolidKind.Arch).Count;
			}
			// Per form and size: how much each kind of cave gets, on average a system.
			var groups = new SortedDictionary<string, (int n, int terraces, int pits, int hollows, int solids, int passages)>();
			foreach (CaveSystem s in Planned())
			{
				string key = $"{s.Form} {s.SizeClass}";
				groups.TryGetValue(key, out var g);
				int laid = s.Solids.FindAll(o => o.Kind >= CaveSolidKind.Terrace || o.Kind == CaveSolidKind.Column).Count;
				groups[key] = (g.n + 1, g.terraces + s.Terraces.Count, g.pits + s.Pits.Count, g.hollows + s.Hollows.Count, g.solids + laid, g.passages + s.Passages.Count);
			}
			foreach (KeyValuePair<string, (int n, int terraces, int pits, int hollows, int solids, int passages)> pair in groups)
			{
				(int n, int terraces, int pits, int hollows, int solids, int passages) g = pair.Value;
				Console.WriteLine($"{pair.Key,-12} x{g.n}: per system {g.terraces / (float)g.n:0.0} terraces, {g.pits / (float)g.n:0.0} pits, {g.hollows / (float)g.n:0.0} hollows, {g.solids / (float)g.n:0.0} terrace/mound/arch/column solids, {g.passages / (float)g.n:0.0} passages");
			}
			string tally = $"terraces [{string.Join(", ", Pairs(terraces))}] ({climbedTerraces} climbed), pits [{string.Join(", ", Pairs(pits))}], hollows [{string.Join(", ", Pairs(hollows))}], {rubble} mounds, {arches} arches";
			Console.WriteLine(tally);
			Assert.That(terraces.ContainsKey(CaveTerraceKind.Step) && terraces.ContainsKey(CaveTerraceKind.Bench), Is.True, $"steps and benches: {tally}");
			Assert.That(pits.ContainsKey(CavePitKind.Pothole), Is.True, $"potholes: {tally}");
			Assert.That(pits.ContainsKey(CavePitKind.Shaft) || pits.ContainsKey(CavePitKind.Ledged), Is.True, $"a shaft or a ledged pit: {tally}");
			Assert.That(hollows.ContainsKey(CaveHollowKind.Dome) && rubble > 0, Is.True, $"a breakdown dome and its mound: {tally}");
		}

		private static IEnumerable<string> Pairs<T>(Dictionary<T, int> d)
		{
			foreach (KeyValuePair<T, int> pair in d)
			{
				yield return $"{pair.Value} {pair.Key}";
			}
		}

		// ── Walkable, and never a trap ────────────────────────────

		[Test]
		public void EveryTerraceIsWalkedUpOntoAndAlong()
		{
			int checkedCount = 0;
			foreach (CaveSystem s in Planned())
			{
				for (int i = 0; i < s.Terraces.Count; i++)
				{
					CaveTerrace t = s.Terraces[i];
					string what = $"{What(s)} terrace {i} ({t.Kind}, {t.Height:0.0} m)";
					Assert.That(s.Solids[t.Solid].Kind, Is.EqualTo(CaveSolidKind.Terrace), what);
					Assert.That(t.Ramp >= 0 || t.Climb >= 0, Is.True, $"{what}: a way up");
					if (t.Ramp >= 0)
					{
						CaveSolid ramp = s.Solids[t.Ramp];
						Assert.That(ramp.Kind, Is.EqualTo(CaveSolidKind.Ramp), what);
						float run = new Vector2(ramp.B.x - ramp.A.x, ramp.B.z - ramp.A.z).magnitude;
						Assert.That(Mathf.Atan2(ramp.B.y - ramp.A.y, run) * Mathf.Rad2Deg, Is.LessThanOrEqualTo(20f), $"{what}: its ramp's grade");
					}
					if (t.Climb >= 0)
					{
						Assert.That(s.ClimbFits(s.Climbs[t.Climb]), Is.True, $"{what}: its climb is clear and stepped off onto its top");
					}
					AssertWalkable(s, s.TerraceWalk(t), what);
					Assert.That(t.Height, Is.InRange(0.4f, 8.01f), what);
					checkedCount++;
				}
			}
			Assert.That(checkedCount, Is.GreaterThan(0));
		}

		[Test]
		public void EveryPitHasAWayOut()
		{
			int checkedCount = 0;
			foreach (CaveSystem s in Planned())
			{
				for (int i = 0; i < s.Pits.Count; i++)
				{
					CavePit pit = s.Pits[i];
					string what = $"{What(s)} pit {i} ({pit.Kind}, {pit.Rim.y - pit.Bottom:0.0} m deep)";
					Assert.That(s.PitWayOut(pit), Is.Null, what);
					Assert.That(s.Origin.y + pit.Bottom, Is.GreaterThanOrEqualTo(s.LowestFloor), $"{what}: its bottom stays over the lowest floor (the sea's reach)");
					switch (pit.Kind)
					{
						case CavePitKind.Shaft:
						case CavePitKind.Winze:
						{
							CaveClimb c = s.Climbs[pit.Way];
							Assert.That(c.Top.y - c.Bottom.y, Is.GreaterThan(3f), $"{what}: its climb reaches up out of it");
							break;
						}
						case CavePitKind.Ledged:
							Assert.That(s.Solids[pit.Way].Kind, Is.EqualTo(CaveSolidKind.Spiral), what);
							break;
						default:
						{
							// Walked out of its bottom up its side, on the fixture's own walk.
							bool walkedOut = false;
							string why = null;
							for (int k = 0; k < 8 && !walkedOut; k++)
							{
								float a = k * Mathf.PI / 4f;
								var path = new List<Vector3> { new Vector3(pit.Rim.x, pit.Bottom + 0.2f, pit.Rim.z), pit.Rim + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (pit.Radius + 1.5f) };
								why = WalkFailure(s, path, out _);
								walkedOut = why == null;
							}
							Assert.That(walkedOut, Is.True, $"{what}: no side walked out of it: {why}");
							break;
						}
					}
					if (!float.IsNaN(pit.Water))
					{
						Assert.That(pit.Kind, Is.EqualTo(CavePitKind.Winze), $"{what}: only a mine's sump stands flooded");
						Assert.That(pit.Water, Is.InRange(s.Origin.y + pit.Bottom, s.Origin.y + pit.Rim.y), $"{what}: its water stands in it");
					}
					checkedCount++;
				}
				foreach (CaveClimb c in s.Climbs)
				{
					Assert.That(s.ClimbFits(c), Is.True, $"{What(s)}: the climb from {c.Bottom} to {c.Top} still fits");
				}
			}
			Assert.That(checkedCount, Is.GreaterThan(0));
		}

		[Test]
		public void TheVarietyLeavesEveryWayWalkable()
		{
			// Every passage walked; a fault within 4 m of anything the variety laid is the variety's. One elsewhere is the
			// layout's own (2026-10-20: seven over this set before the variety ran, two fixed at their root), logged, not failed.
			int own = 0;
			foreach (CaveSystem s in Planned())
			{
				for (int p = 0; p < s.Passages.Count; p++)
				{
					var points = new List<Vector3>();
					foreach (CaveNode n in s.Passages[p].Nodes)
					{
						points.Add(new Vector3(n.Centre.x, n.Floor + 0.3f, n.Centre.z));
					}
					string why = WalkFailure(s, points, out Vector3 at);
					if (why == null)
					{
						continue;
					}
					string what = $"{What(s)} passage {p} ({s.Passages[p].Kind}): {why}";
					Assert.That(NearVariety(s, at, 4f), Is.False, $"{what}, beside a feature the variety laid");
					Console.WriteLine($"the layout's own: {what}");
					own++;
				}
				foreach (CaveChamber c in s.Chambers)
				{
					Vector3 floor = new Vector3(c.Centre.x, c.Floor + 1.5f, c.Centre.z);
					float room = RoofOver(s, floor) - FloorUnder(s, floor);
					if (room >= 2.6f)
					{
						continue;
					}
					// (A hall's own ramp run through a lower lobe's middle is the layout's.)
					string what = $"{What(s)}: {room:0.00} m of headroom in the middle of the {c.Kind} at {c.Centre}";
					Assert.That(NearVariety(s, floor, 4f), Is.False, $"{what}, beside a feature the variety laid");
					Console.WriteLine($"the layout's own: {what}");
					own++;
				}
			}
			Console.WriteLine($"{own} fault(s) of the layout's own, none beside the variety's features");
		}

		[Test]
		public void AMineWorksItsOwnWays()
		{
			bool raise = false, ore = false, choke = false, winze = false;
			foreach (CaveSystem s in Planned())
			{
				if (s.Form != CaveForm.Mine)
				{
					continue;
				}
				raise |= s.Hollows.Exists(h => h.Kind == CaveHollowKind.Raise);
				ore |= s.Chambers.Exists(c => c.Kind == CaveChamberKind.Stope && c.Radii.y >= 4f);
				choke |= s.Solids.Exists(o => o.Kind == CaveSolidKind.Rubble);
				winze |= s.Pits.Exists(p => p.Kind == CavePitKind.Winze);
				Assert.That(s.Timbered(0, 5f), Is.True, $"{What(s)}: timbered by its portal");
			}
			Console.WriteLine($"mines: raise {raise}, ore chamber {ore}, choke {choke}, winze {winze}");
			Assert.That((raise ? 1 : 0) + (ore ? 1 : 0) + (choke ? 1 : 0) + (winze ? 1 : 0), Is.GreaterThanOrEqualTo(3), "a mine's workings vary");
		}

		// ── Budget, determinism, storage ──────────────────────────

		[Test]
		public void TheRockStaysWithinItsBudget()
		{
			// The fixture's caps (PointOfInterestCaveTests.TheRockStaysWithinItsBudget), on the most featured caves planned here.
			var ranked = new List<CaveSystem>(Planned());
			ranked.Sort((a, b) => (b.Terraces.Count + b.Pits.Count + b.Hollows.Count + b.Solids.Count).CompareTo(a.Terraces.Count + a.Pits.Count + a.Hollows.Count + a.Solids.Count));
			for (int i = 0; i < Mathf.Min(4, ranked.Count); i++)
			{
				CaveSystem system = ranked[i];
				int seed = system.Seed / 7919;
				float tilt = seed * 13f % 40f - 20f;
				Func<float, float, float> face = Escarpment(tilt, seed);
				Func<float, float, float> g = (px, pz) => face(px, pz) + CaveLift;
				Bounds e = system.Extent;
				CaveGround ground = CaveGround.Sample(g, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f, system.Origin.x + e.max.x + 4f, system.Origin.z + e.max.z + 4f, HoleStep, GridX, GridZ);
				List<CaveMesher.Piece> pieces = CaveMesher.Build(system, ground, out _);
				long triangles = 0, vertices = 0;
				int biggest = 0;
				foreach (CaveMesher.Piece piece in pieces)
				{
					triangles += piece.Mesh.TriangleCount;
					vertices += piece.Mesh.VertexCount;
					biggest = Mathf.Max(biggest, piece.Mesh.TriangleCount);
				}
				double megabytes = (vertices * 52.0 + triangles * 12.0 + triangles * 40.0) / 1048576.0;
				string what = $"{What(system)}: {system.Terraces.Count} terraces, {system.Pits.Count} pits, {system.Hollows.Count} hollows";
				Console.WriteLine($"{what}: {triangles:N0} triangles, biggest piece {biggest:N0}, ~{megabytes:0.0} MB");
				Assert.That(triangles, Is.LessThanOrEqualTo(250000), what);
				Assert.That(biggest, Is.LessThanOrEqualTo(40000), what);
				Assert.That(megabytes, Is.LessThanOrEqualTo(16.0), what);
			}
		}

		[Test]
		public void TheSameSeedGivesTheSameSystem()
		{
			foreach ((CaveForm form, int size, int seed) in new[] { (CaveForm.Cave, 1, 3), (CaveForm.Cave, 2, 5), (CaveForm.LavaTube, 1, 2), (CaveForm.Mine, 2, 3) })
			{
				CaveSystem a = PlanCave(form, size, seed, out _), b = PlanCave(form, size, seed, out _);
				Assert.That(b.ToValues(), Is.EqualTo(a.ToValues()), $"{form} {size} seed {seed}: planned twice");
				CaveSystem c = PlanCave(form, size, seed + 1, out _);
				Assert.That(c.ToValues(), Is.Not.EqualTo(a.ToValues()), $"{form} {size}: another seed, another system");
			}
		}

		[Test]
		public void AVariedCaveStoresAndRebuildsExactly()
		{
			foreach (CaveSystem s in Planned())
			{
				float[] values = s.ToValues();
				CaveSystem back = CaveSystem.FromValues(values, s.Origin, s.Seed);
				Assert.That(back, Is.Not.Null, What(s));
				Assert.That(back.ToValues(), Is.EqualTo(values), $"{What(s)}: round trip");
				Assert.That(back.Traits, Is.EqualTo(s.Traits));
				Assert.That(back.Terraces.Count, Is.EqualTo(s.Terraces.Count));
				Assert.That(back.Pits.Count, Is.EqualTo(s.Pits.Count));
				Assert.That(back.Hollows.Count, Is.EqualTo(s.Hollows.Count));
				var probes = new List<Vector3>();
				foreach (CavePit p in s.Pits)
				{
					probes.Add(new Vector3(p.Rim.x, p.Bottom + 0.5f, p.Rim.z));
				}
				foreach (CaveTerrace t in s.Terraces)
				{
					probes.Add(s.Solids[t.Solid].A + Vector3.up * 0.5f);
				}
				foreach (CaveHollow h in s.Hollows)
				{
					probes.Add(h.A + Vector3.up * 2f);
				}
				foreach (Vector3 p in probes)
				{
					Assert.That(back.Void(p), Is.EqualTo(s.Void(p)), $"{What(s)}: the same field at {p}");
				}
			}
			// Older formats are cut again, not misread; their mouths are still found to fill.
			float[] old = Planned()[0].ToValues();
			old[0] = 6f;
			Assert.That(CaveSystem.FromValues(old, Vector3.zero, 1), Is.Null, "a format 6 cave says re-cut");
			Assert.That(CaveSystem.StoredMouths(old).Count, Is.EqualTo(Planned()[0].Mouths.Count), "its mouths are still read to refill");
		}
	}
}
