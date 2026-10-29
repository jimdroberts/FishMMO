using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Every place a cave is open to the sky is an opening the cut made (2026-10-27, Moron 2: "we are still seeing holes in cave
	/// walls from the interior facing out ... unique holes into caves are fine but we need to ensure they are proper cave
	/// entrances"): wherever the cave's air rises into the skin under the ground the terrain over it is holed (and the collar
	/// frames the hole), mouth zones included; nowhere is the air left under unholed terrain, which is drawn from above only.
	/// On the same synthetic escarpment as CaveWallClosureTests.
	/// </summary>
	[TestFixture]
	public class CaveOpeningTests
	{
		private const float CaveLift = 60f;
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

		private static CaveSystem PlanCave(CaveForm form, int size, int seed, out Func<float, float, float> g)
		{
			float tilt = seed * 13f % 40f - 20f;
			Func<float, float, float> face = Escarpment(tilt, seed);
			g = (px, pz) => face(px, pz) + CaveLift;
			return CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed,
				CaveGround.Sample(g, -560f, -560f, 560f, 560f, 2f), 0f, out _);
		}

		/// <summary>What of the system a point (system frame) is nearest, for the report.</summary>
		private static string Where(CaveSystem system, Vector3 p)
		{
			float best = float.MaxValue;
			string what = "?";
			for (int i = 0; i < system.Chambers.Count; i++)
			{
				CaveChamber c = system.Chambers[i];
				float d = new Vector2(p.x - c.Centre.x, p.z - c.Centre.z).magnitude - Mathf.Max(c.Radii.x, c.Radii.z);
				if (d < best)
				{
					best = d;
					what = $"chamber {i} ({c.Kind})";
				}
			}
			for (int i = 0; i < system.Passages.Count; i++)
			{
				foreach (CaveNode n in system.Passages[i].Nodes)
				{
					float d = (p - new Vector3(n.Centre.x, n.Floor, n.Centre.z)).magnitude - n.Radius;
					if (d < best)
					{
						best = d;
						what = $"passage {i} ({system.Passages[i].Kind})";
					}
				}
			}
			float mouth = float.MaxValue;
			foreach (CaveMouth m in system.Mouths)
			{
				mouth = Mathf.Min(mouth, new Vector2(p.x - m.Floor.x, p.z - m.Floor.z).magnitude);
			}
			return $"{what}, {mouth:0} m from a mouth";
		}

		/// <summary>A ridge running east–west, its faces 150 m either side of the line z = 0 (PointOfInterestCaveTests' ridge).</summary>
		private static Func<float, float, float> Ridge(float half, int variant)
			=> (x, z) =>
			{
				float u = half - Mathf.Abs(z);
				float t = Mathf.Clamp01(u / 30f);
				float roll = 3f * Mathf.Sin(x * 0.05f + variant) + 2f * Mathf.Sin(z * 0.031f + 0.7f * variant);
				return 70f + 60f * (t * t * (3f - 2f * t)) + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};

		/// <summary>
		/// The synthetic caves the cut would make: on the escarpment (with its great chambers' skylights) and through the ridge
		/// (a second mouth on its far face), each with the ground before every mouth graded as the shaper grades it.
		/// </summary>
		private static IEnumerable<(string What, CaveSystem System, Func<float, float, float> Ground)> Caves()
		{
			foreach (CaveForm form in new[] { CaveForm.Cave, CaveForm.IceCave, CaveForm.LavaTube, CaveForm.Mine })
			{
				foreach (int size in new[] { 0, 1, 2 })
				{
					for (int seed = 1; seed <= 4; seed++)
					{
						CaveSystem system = PlanCave(form, size, seed, out Func<float, float, float> g);
						if (system == null)
						{
							continue;
						}
						system.TryAddSkylights(CaveGround.Sample(g, -700f, -700f, 700f, 700f, 2f), seed, seed * 31 + 7, null, out _);
						yield return ($"{form} size {size} seed {seed} (escarpment)", system, Graded(system, g));
						if (!CaveSystem.GoesThrough(form))
						{
							continue;
						}
						Func<float, float, float> ridge = Ridge(150f, seed);
						Func<float, float, float> lifted = (x, z) => ridge(x, z) + CaveLift;
						CaveGround wide = CaveGround.Sample(lifted, -760f, -760f, 760f, 760f, 2f);
						CaveSystem through = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -151f), 180f, seed * 7919, seed, wide, 0f, out _);
						if (through == null || !through.TryAddThrough(seed, wide, seed * 31 + 5, null, out _))
						{
							continue;
						}
						yield return ($"{form} size {size} seed {seed} (through a ridge)", through, Graded(through, lifted));
					}
				}
			}
		}

		/// <summary>The ground with every mouth's approach graded (CaveShaper.GradeApproach), as the holes are reckoned from it.</summary>
		private static Func<float, float, float> Graded(CaveSystem system, Func<float, float, float> g)
		{
			if (!system.GradesApproach)
			{
				return g;
			}
			return (x, z) =>
			{
				float h = g(x, z);
				for (int m = 0; m < system.Mouths.Count; m++)
				{
					if (system.Mouths[m].Kind != CaveMouthKind.Skylight)
					{
						h = system.Approach(m, x, z, h);
					}
				}
				return h;
			};
		}

		/// <summary>The 8-connected pieces of a set of hole cells, largest first.</summary>
		private static List<List<Vector2Int>> Pieces(ICollection<Vector2Int> cells)
		{
			var left = new HashSet<Vector2Int>(cells);
			var pieces = new List<List<Vector2Int>>();
			while (left.Count > 0)
			{
				var piece = new List<Vector2Int>();
				var stack = new Stack<Vector2Int>();
				foreach (Vector2Int c in left)
				{
					stack.Push(c);
					break;
				}
				left.Remove(stack.Peek());
				while (stack.Count > 0)
				{
					Vector2Int c = stack.Pop();
					piece.Add(c);
					for (int dz = -1; dz <= 1; dz++)
					{
						for (int dx = -1; dx <= 1; dx++)
						{
							var n = new Vector2Int(c.x + dx, c.y + dz);
							if (left.Remove(n))
							{
								stack.Push(n);
							}
						}
					}
				}
				pieces.Add(piece);
			}
			pieces.Sort((a, b) => b.Count.CompareTo(a.Count));
			return pieces;
		}

		[Test]
		public void EachMouthIsOneHole()
		{
			int mouths = 0, stray = 0;
			var report = new List<string>();
			foreach ((string what, CaveSystem system, Func<float, float, float> g) in Caves())
			{
				for (int m = 0; m < system.Mouths.Count; m++)
				{
					mouths++;
					List<List<Vector2Int>> pieces = Pieces(system.HoleCells(m, g, HoleStep, GridX, GridZ));
					if (pieces.Count > 1)
					{
						stray += pieces.Count - 1;
						Vector2Int c = pieces[1][0];
						var at = new Vector3(GridX + (c.x + 0.5f) * HoleStep, 0f, GridZ + (c.y + 0.5f) * HoleStep) - system.Origin;
						at.y = g(at.x + system.Origin.x, at.z + system.Origin.z) - system.Origin.y;
						report.Add($"{what} mouth {m} ({system.Mouths[m].Kind}): {pieces.Count} holes ({string.Join("/", pieces.ConvertAll(p => p.Count))} cells), "
							+ $"a stray one at {at:F1} ({Where(system, at)})");
					}
				}
			}
			foreach (string line in report)
			{
				Console.WriteLine(line);
			}
			Console.WriteLine($"{mouths} mouths, {stray} stray holes beside them");
			Assert.That(stray, Is.Zero, "each mouth is one hole in the ground");
		}

		/// <summary>Every hole cell the cut makes at a system's mouths on ground <paramref name="g"/> (CaveSystem.HoleCells).</summary>
		private static HashSet<Vector2Int> Holed(CaveSystem system, Func<float, float, float> g)
		{
			var holed = new HashSet<Vector2Int>();
			for (int m = 0; m < system.Mouths.Count; m++)
			{
				foreach (Vector2Int c in system.HoleCells(m, g, HoleStep, GridX, GridZ))
				{
					holed.Add(c);
				}
			}
			return holed;
		}

		/// <summary>The hole cells' centres (world east, north).</summary>
		private static List<Vector2> Centres(HashSet<Vector2Int> holed)
		{
			var centres = new List<Vector2>();
			foreach (Vector2Int c in holed)
			{
				centres.Add(new Vector2(GridX + (c.x + 0.5f) * HoleStep, GridZ + (c.y + 0.5f) * HoleStep));
			}
			return centres;
		}

		/// <summary>
		/// The metre columns over a system where its air rises into the skin under ground <paramref name="g"/> (CaveHoleProbe's
		/// roofless test) away from every hole cell (<paramref name="clear"/> metres round them left out).
		/// </summary>
		private static int OpenColumns(CaveSystem system, Func<float, float, float> g, HashSet<Vector2Int> holed, float clear, out string first)
		{
			var openings = new PointOfInterestCaveOpeningClearance.OpeningIndex(Centres(holed), HoleStep);
			Bounds e = system.Extent;
			int open = 0;
			first = null;
			for (float x = e.min.x; x <= e.max.x; x += 1f)
			{
				for (float z = e.min.z; z <= e.max.z; z += 1f)
				{
					float wx = x + system.Origin.x, wz = z + system.Origin.z;
					if (openings.Within(new Vector2(wx, wz), clear))
					{
						continue;
					}
					var p = new Vector3(x, g(wx, wz) - system.Origin.y - 0.5f * system.Skin, z);
					if (system.Void(p) < -0.15f)
					{
						open++;
						first ??= $"{p:F1} ({Where(system, p)})";
					}
				}
			}
			return open;
		}

		[Test]
		public void TheCaveIsOpenToTheSkyOnlyWhereTheTerrainIsHoled()
		{
			int caves = 0, total = 0, raised = 0, unsealed = 0;
			var report = new List<string>();
			foreach ((string what, CaveSystem system, Func<float, float, float> g) in Caves())
			{
				caves++;
				HashSet<Vector2Int> holed = Holed(system, g);
				int open = OpenColumns(system, g, holed, HoleStep, out string first);
				total += open;
				if (open > 0)
				{
					report.Add($"{what}: {open} m² of air under unholed ground (first at {first})");
				}
				// And the seal the shaper runs before meshing finds nothing to raise on the ground the cave was planned on.
				var openings = new PointOfInterestCaveOpeningClearance.OpeningIndex(Centres(holed), HoleStep);
				var seal = new CaveShaper.RoofSeal(system, g, (x, z) => openings.Within(new Vector2(x, z), CaveShaper.SealOpeningClear), 1f);
				raised += seal.Raised;
				unsealed += seal.Unsealed;
				if (seal.Raised + seal.Unsealed > 0)
				{
					report.Add($"{what}: the seal raised {seal.Raised} column(s) (up to {seal.Most:0.00} m), {seal.Unsealed} unsealed, on the plan's own ground");
				}
			}
			foreach (string line in report)
			{
				Console.WriteLine(line);
			}
			Console.WriteLine($"{caves} caves, {total} m² open to the sky under unholed terrain; the seal raised {raised} column(s), {unsealed} unsealed");
			Assert.That(caves, Is.GreaterThan(30));
			Assert.That(total, Is.Zero, "the cave is open to the sky only through a hole the cut made");
			Assert.That(unsealed, Is.Zero, "nothing the plan laid stands out of its ground");
		}

		[Test]
		public void GroundCarvedDownOverACaveAfterItsPlanIsRaisedBackOverItsRock()
		{
			// 2026-10-27 (Moron 2): the ground is not left as the cave was planned on: the ways are carved, other sites' pads levelled,
			// hot ground dug, after the cave is laid out, and the layout's cover tests sample its bores at a few points. A hollow dug
			// down to a fifth of a metre over a cave's air, away from its mouths, opens its air to the sky under unholed terrain; the seal the shaper
			// runs before meshing (CaveShaper.RoofSeal) raises the ground there till every column is roofed again, and nowhere else.
			int caves = 0, opened = 0, before = 0, after = 0, unsealed = 0;
			float most = 0f;
			var report = new List<string>();
			foreach ((string what, CaveSystem system, Func<float, float, float> g) in Caves())
			{
				HashSet<Vector2Int> holed = Holed(system, g);
				// The hollow over the void's highest node at least 40 m from any mouth.
				Vector3 best = default;
				float bestTop = float.NegativeInfinity;
				foreach (CavePassage passage in system.Passages)
				{
					foreach (CaveNode n in passage.Nodes)
					{
						bool far = true;
						foreach (CaveMouth m in system.Mouths)
						{
							far &= new Vector2(n.Centre.x - m.Floor.x, n.Centre.z - m.Floor.z).magnitude > 40f + m.Zone;
						}
						float cover = g(n.Centre.x + system.Origin.x, n.Centre.z + system.Origin.z) - system.Origin.y - n.Centre.y;
						if (far && -cover > bestTop)
						{
							bestTop = -cover;
							best = n.Centre + system.Origin;
						}
					}
				}
				if (float.IsNegativeInfinity(bestTop))
				{
					continue;
				}
				caves++;
				float coverAt = -bestTop;
				// Levelled down to a fifth of a metre over the highest air under it (marched up through the void), 12 m across, eased
				// over 6 m: the ground lowered, never under the cave's air, so a rise of a few metres always roofs it again.
				float roofTop = float.NegativeInfinity;
				for (float dx = -12f; dx <= 12f; dx += 1f)
				{
					for (float dz = -12f; dz <= 12f; dz += 1f)
					{
						var q = new Vector3(best.x + dx, 0f, best.z + dz) - system.Origin;
						float y = g(q.x + system.Origin.x, q.z + system.Origin.z) - system.Origin.y;
						// From the ground down to the first air, then up through it to its top.
						while (y > q.y - 200f && system.Void(new Vector3(q.x, y, q.z)) >= 0f && y > best.y - system.Origin.y - 20f)
						{
							y -= 0.25f;
						}
						if (system.Void(new Vector3(q.x, y, q.z)) < 0f)
						{
							while (system.Void(new Vector3(q.x, y, q.z)) < 0f)
							{
								y += 0.05f;
							}
							roofTop = Mathf.Max(roofTop, y + system.Origin.y);
						}
					}
				}
				float level = roofTop + 0.2f, dig = g(best.x, best.z) - level;
				Func<float, float, float> dug = (x, z) =>
				{
					float d = new Vector2(x - best.x, z - best.z).magnitude;
					float t = 1f - Mathf.Clamp01((d - 12f) / 6f);
					float h = g(x, z);
					return Mathf.Min(h, Mathf.Lerp(h, level, t * t * (3f - 2f * t)));
				};
				int open = OpenColumns(system, dug, holed, CaveShaper.SealOpeningClear, out string first);
				if (open == 0)
				{
					continue;
				}
				opened++;
				before += open;
				var openings = new PointOfInterestCaveOpeningClearance.OpeningIndex(Centres(holed), HoleStep);
				var seal = new CaveShaper.RoofSeal(system, dug, (x, z) => openings.Within(new Vector2(x, z), CaveShaper.SealOpeningClear), 1f);
				Func<float, float, float> sealedGround = (x, z) => dug(x, z) + seal.Raise(x, z);
				int still = OpenColumns(system, sealedGround, holed, CaveShaper.SealOpeningClear, out string left);
				after += still;
				unsealed += seal.Unsealed;
				most = Mathf.Max(most, seal.Most);
				report.Add($"{what}: dug {dig:0.0} m over cover {coverAt:0.0} m: {open} open (first {first}), {seal.Raised} raised (up to {seal.Most:0.00} m), "
					+ $"{still} still open{(still > 0 ? $" (first {left})" : "")}, {seal.Unsealed} unsealed");
				// Nothing far from the hollow moved.
				Assert.That(seal.Raise(best.x + 200f, best.z + 200f), Is.Zero, $"{what}: the ground far off the hollow is left as it is");
			}
			foreach (string line in report)
			{
				Console.WriteLine(line);
			}
			Console.WriteLine($"{caves} caves dug over, {opened} opened by it: {before} columns open before the seal, {after} after, {unsealed} unsealed, most raised {most:0.00} m");
			Assert.That(opened, Is.GreaterThan(5), "the hollow opens the rock of many caves");
			Assert.That(after, Is.Zero, "every column is roofed again once sealed");
			Assert.That(unsealed, Is.Zero, "a hollow no deeper than the most the seal raises is always sealed");
			Assert.That(most, Is.LessThanOrEqualTo(CaveSystem.MaxSeal + 1e-3f));
		}

		[Test]
		public void AnOpeningsMarginIsMeasuredToItsCellsEdge()
		{
			// One opening cell 2 m across at (10, 10): its square's edge 1 m from its centre (a half diagonal √2 m to its corner).
			var openings = new PointOfInterestCaveOpeningClearance.OpeningIndex(new[] { new Vector2(10f, 10f), new Vector2(10f, 10f) }, 2f);
			Assert.That(openings.Count, Is.EqualTo(1), "the same cell found round two mouths counts once");
			Assert.That(openings.Within(new Vector2(10f, 13f), 1.6f), Is.True, "3 m off the centre, within 1.6 m of the cell's corner reach");
			Assert.That(openings.Within(new Vector2(10f, 14f), 1.5f), Is.False);
			Assert.That(openings.Within(new Vector2(500f, 500f), 10f), Is.False, "far off: no bucket holds it");
			// A detail cell is emptied when a plant anywhere in it stands within the margin of the opening.
			float m = PointOfInterestCaveOpeningClearance.DetailMargin;
			Assert.That(PointOfInterestCaveOpeningClearance.DetailCellCleared(openings, new Vector2(10f, 10f + m + 1f), 1f), Is.True);
			Assert.That(PointOfInterestCaveOpeningClearance.DetailCellCleared(openings, new Vector2(10f, 10f + m + 3f), 1f), Is.False);
			Assert.That(PointOfInterestCaveOpeningClearance.DetailCellCleared(null, new Vector2(10f, 10f), 1f), Is.False);
			// A tree is kept off by its crown and the margin, its crown held to the most reckoned.
			Assert.That(PointOfInterestCaveOpeningClearance.TreeClearance(1.2f), Is.EqualTo(PointOfInterestCaveOpeningClearance.TreeMargin + 1.2f).Within(1e-5f));
			Assert.That(PointOfInterestCaveOpeningClearance.TreeClearance(40f),
				Is.EqualTo(PointOfInterestCaveOpeningClearance.TreeMargin + PointOfInterestCaveOpeningClearance.MaxCrownMetres).Within(1e-5f));
		}

		[Test]
		public void ThePlantsMaskKeepsAMarginRoundEveryHole_AndNeverSpreadsFurther()
		{
			// The client's plant mask (TerrainHoleMask.KeepClear): 1 m cells, one hole at (5, 5), a 1.5 m margin to its square's edge.
			const int res = 11;
			var solid = new bool[res * res];
			for (int i = 0; i < solid.Length; i++)
			{
				solid[i] = true;
			}
			solid[5 * res + 5] = false;
			FishMMO.Client.TerrainHoleMask.KeepClear(solid, res, 1f, 1.5f);
			bool Hole(int x, int z) => !solid[z * res + x];
			Assert.That(Hole(5, 5) && Hole(6, 5) && Hole(7, 5) && Hole(5, 3), Is.True, "within 1.5 m of the hole's square");
			Assert.That(Hole(7, 7), Is.True, "a corner 1.41 m off");
			Assert.That(Hole(8, 5) || Hole(5, 2), Is.False, "2 m off is kept");
			Assert.That(Hole(8, 7), Is.False);
			int holes = 0;
			foreach (bool s in solid)
			{
				holes += s ? 0 : 1;
			}
			Assert.That(holes, Is.EqualTo(25), "the 5 × 5 square round it: the margin is reckoned from the holes as they were, never from the cells it turned");
		}

		[Test]
		public void TheCutSealsTheRoofBeforeMeshing_AndBaresTheOpeningsAfterEverySite()
		{
			const string folder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/";
			string shaper = File.ReadAllText(folder + "Shapers/CaveShaper.cs");
			int place = shaper.IndexOf("private static void PlaceSystem(", StringComparison.Ordinal);
			Assert.That(place, Is.GreaterThan(0));
			int seal = shaper.IndexOf("SealRoof(context, system, label);", place, StringComparison.Ordinal);
			int ground = shaper.IndexOf("GroundAsCut(system, context.GroundAt, context.Terrains, context.Tiles);", place, StringComparison.Ordinal);
			Assert.That(seal, Is.GreaterThan(place), "the cave's Place seals its roof");
			Assert.That(seal, Is.LessThan(ground), "before the ground the rock is meshed against is read");
			string stage = File.ReadAllText(folder + "PointOfInterestStage.cs");
			int bare = stage.IndexOf("PointOfInterestCaveOpeningClearance.Clear(terrains, tiles, asset, notes);", StringComparison.Ordinal);
			Assert.That(bare, Is.GreaterThan(stage.IndexOf("PointOfInterestLinearFeatures.LayScene(", StringComparison.Ordinal)), "after every site has written its ground");
			Assert.That(bare, Is.LessThan(stage.IndexOf("ScenePathBake.Write(scene", StringComparison.Ordinal)), "before the ground is baked for the client");
			// The client's two plant builders (every detail kind is the blades' or the scatter's since the CPU chunk renderer went,
			// 2026-10-27) read the plants' mask, the clouds and the ambient life the terrain's own holes.
			Assert.That(File.ReadAllText("Assets/Scripts/Client/World/Terrain/Grass/GrassTerrain.cs"), Does.Contain("cut.KeepPlantsClear();"));
			Assert.That(File.ReadAllText("Assets/Scripts/Client/World/Terrain/Scatter/DetailScatterTerrain.cs"), Does.Contain("PlantsClear(cut).ToTexels("));
			Assert.That(File.Exists("Assets/Scripts/Client/World/Terrain/TerrainDetailField.cs"), Is.False, "no CPU chunk renderer places plants of its own");
		}
	}
}
