using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The large-rock cliffs (<see cref="CliffRocks"/>, <see cref="CliffRockPlacement"/>): the rules Jim
	/// chose them by, checked on a synthetic cliff band — size graded by height, the base rule, burial
	/// graded by size, stable resting debris, orientation by structure — and the meshes themselves.
	/// </summary>
	[TestFixture]
	public class CliffRockTests
	{
		private const int Seed = 20261002;

		/// <summary>A 240 × 140 m slope at 18° with a 28 m wide band at 60° across it: a 48 m cliff.</summary>
		private sealed class Slope : ICliffRockGround
		{
			public bool TryHeight(float x, float z, out float h)
			{
				h = Profile(z) + 0.3f * Mathf.Sin(x * 0.07f) * Mathf.Cos(z * 0.05f);
				return x >= 0f && z >= 0f && x <= 240f && z <= 140f;
			}

			private static float Profile(float z)
			{
				float gentle = Mathf.Tan(18f * Mathf.Deg2Rad), steep = Mathf.Tan(60f * Mathf.Deg2Rad);
				float a = 50f, b = 78f;
				if (z <= a) return z * gentle;
				if (z <= b) return a * gentle + (z - a) * steep;
				return a * gentle + (b - a) * steep + (z - b) * gentle;
			}
		}

		private static readonly Dictionary<(string, CliffPiece), MeshBuilder> seating = new Dictionary<(string, CliffPiece), MeshBuilder>();

		private static MeshBuilder Seating(CliffPiece piece)
		{
			if (!seating.TryGetValue((piece.Type, piece), out MeshBuilder m))
			{
				seating[(piece.Type, piece)] = m = CliffRocks.Build(in piece, CliffRocks.CollisionLod, Seed);
			}
			return m;
		}

		private static CliffRockPlan Plan(string type, int roundness = 0, CliffRockPlacementOptions options = null)
		{
			CliffRockSiteAt site = (float x, float z, float y, out CliffRockSite s) =>
			{
				s = new CliffRockSite { Type = type, Roundness = roundness, MinAngle = 40f };
				return true;
			};
			return CliffRockPlacement.Plan(new Slope(), new Rect(0f, 0f, 240f, 140f), site, 777u, Seating, options);
		}

		private static readonly Dictionary<string, CliffRockPlan> plans = new Dictionary<string, CliffRockPlan>();
		private static CliffRockPlan PlanOf(string type)
		{
			if (!plans.TryGetValue(type, out CliffRockPlan p))
			{
				plans[type] = p = Plan(type);
			}
			return p;
		}

		private static float Longest(in PlacedCliffRock r) => r.Length;

		private static Vector3 Up(in PlacedCliffRock r) => r.Rotation.GetColumn(1);

		// ── Placement rules ───────────────────────────────────────────

		[Test]
		public void Plan_PlacesAFootingAndTalus_OnASteepBand([Values("Granite", "Sandstone", "Basalt", "Slate", "Ice")] string type)
		{
			CliffRockPlan plan = PlanOf(type);
			Assert.That(plan.Stats.CliffLength, Is.GreaterThan(150f), "the band runs across the slope");
			Assert.That(plan.Stats.Footing, Is.GreaterThan(3), "a footing of big rocks along the foot line");
			Assert.That(plan.Rocks.Count(r => !r.Talus), Is.GreaterThan(10));
			Assert.That(plan.Cones.Count, Is.GreaterThan(0), "talus cones below a 48 m cliff");
			Assert.That(plan.Rocks.Any(r => r.Talus), Is.True);
		}

		[Test]
		public void Plan_IsDeterministic()
		{
			CliffRockPlan a = Plan("Granite"), b = Plan("Granite");
			Assert.That(b.Rocks.Count, Is.EqualTo(a.Rocks.Count));
			for (int i = 0; i < a.Rocks.Count; i++)
			{
				Assert.That(b.Rocks[i].Piece, Is.EqualTo(a.Rocks[i].Piece));
				Assert.That(b.Rocks[i].Position, Is.EqualTo(a.Rocks[i].Position));
			}
		}

		[Test]
		public void SizeIsGradedByHeight_BigRocksLow_SmallHigh([Values("Granite", "Sandstone", "Basalt")] string type)
		{
			CliffRockPlan plan = PlanOf(type);
			float baseLength = CliffRocks.ShapesOf(type, CliffRole.Base)[0].Length;
			var foot = plan.Rocks.Where(r => !r.Talus && r.BandT < 1f / 3f).Select(r => Longest(in r)).ToList();
			var crest = plan.Rocks.Where(r => !r.Talus && r.BandT >= 2f / 3f && r.BandT < 1f).Select(r => Longest(in r)).ToList();
			Assert.That(foot, Is.Not.Empty);
			if (crest.Count > 0)
			{
				Assert.That(foot.Average(), Is.GreaterThan(crest.Average()), "the foot holds the bigger rocks");
			}
			foreach (PlacedCliffRock r in plan.Rocks.Where(r => !r.Talus && !r.Base && r.BandT > 0.45f && r.BandT < 1f))
			{
				Assert.That(Longest(in r), Is.LessThanOrEqualTo(CliffRockPlacement.HeightCap(r.BandT, baseLength, 1f) * 1.02f),
					$"{r.Piece} at band height {r.BandT:0.00} is over the cap");
			}
		}

		[Test]
		public void HeightCap_IsNoneLow_ThenShrinksTowardTheCrest()
		{
			Assert.That(CliffRockPlacement.HeightCap(0.2f, 30f, 0f), Is.GreaterThan(100f));
			float mid = CliffRockPlacement.HeightCap(0.5f, 30f, 0f), high = CliffRockPlacement.HeightCap(0.9f, 30f, 0f);
			Assert.That(mid, Is.LessThan(0.65f * 30f + 1e-3f));
			Assert.That(high, Is.LessThan(mid));
			Assert.That(high, Is.GreaterThanOrEqualTo(0.25f * 30f - 1e-3f));
		}

		[Test]
		public void BaseRule_NoFootingRockShowsItsUnderside([Values("Granite", "Sandstone", "Basalt")] string type)
		{
			foreach (PlacedCliffRock r in PlanOf(type).Rocks.Where(r => r.Base))
			{
				// 1.5 % is the rule's stop; ten 4 % steps may end just short of it.
				Assert.That(r.Underside, Is.LessThanOrEqualTo(0.03f), $"{r.Piece} shows its underside at the foot");
			}
		}

		[Test]
		public void Burial_GrowsWithSize_InItsBands()
		{
			Assert.That(CliffRockPlacement.EmbedFor(5f, 0f), Is.InRange(0.05f, 0.2f));
			Assert.That(CliffRockPlacement.EmbedFor(16f, 0f), Is.InRange(0.2f, 0.4f));
			Assert.That(CliffRockPlacement.EmbedFor(35f, 0f), Is.InRange(0.35f, 0.6f));
			Assert.That(CliffRockPlacement.EmbedFor(5f, 0f), Is.LessThan(CliffRockPlacement.EmbedFor(16f, 0f)));
			Assert.That(CliffRockPlacement.EmbedFor(16f, 0f), Is.LessThan(CliffRockPlacement.EmbedFor(35f, 0f)));
			for (float noise = -1f; noise <= 1f; noise += 0.5f)
			{
				Assert.That(CliffRockPlacement.EmbedFor(40f, noise), Is.InRange(0.35f, 0.6f), "noise never leaves the size's band");
			}
		}

		[Test]
		public void Burial_EveryRockIsAtLeastAsDeepAsItsSizeAsks([Values("Granite", "Basalt")] string type)
		{
			foreach (PlacedCliffRock r in PlanOf(type).Rocks)
			{
				Assert.That(r.EmbedAchieved, Is.GreaterThanOrEqualTo(r.Embed - 0.01f), $"{r.Piece}");
			}
		}

		[Test]
		public void Debris_RestsOnItsBroadestFace_LongAxisDownTheFallLine()
		{
			var piece = new CliffPiece("Granite", CliffRole.Debris, 1, -1, 0);
			MeshBuilder mesh = Seating(piece);
			Vector3 com = Vector3.zero;
			foreach (Vector3 p in mesh.Positions) com += p;
			com /= mesh.VertexCount;
			var scale = new Vector3(1.1f, 0.9f, 1f);
			Vector3 down = new Vector3(0f, 0f, -1f);
			const float slope = 25f;
			float sl = slope * Mathf.Deg2Rad;
			Vector3 n = (Vector3.up * Mathf.Cos(sl) - down * Mathf.Sin(sl)).normalized;
			var rng = new DeterministicRNG(5);
			Matrix4x4 pose = CliffRockPlacement.StablePose(mesh, com, scale, down, slope, rng);
			float rest = CliffRockPlacement.ComHeight(mesh, com, scale, pose, n);
			var heights = new List<float>();
			var other = new DeterministicRNG(99);
			for (int i = 0; i < 40; i++)
			{
				float y = other.Range(-1f, 1f), a = other.NextFloat() * 6.2831853f, h = Mathf.Sqrt(1f - y * y);
				heights.Add(CliffRockPlacement.ComHeight(mesh, com, scale, CliffRockPlacement.Rot(new Vector3(h * Mathf.Cos(a), y, h * Mathf.Sin(a)), other.NextFloat() * 360f), n));
			}
			heights.Sort();
			Assert.That(rest, Is.LessThanOrEqualTo(heights[heights.Count / 4] + 1e-3f), "the resting pose sits lower than three quarters of random ones");
		}

		[Test]
		public void Orientation_BedsShareOneDip()
		{
			var ups = PlanOf("Sandstone").Rocks.Where(r => !r.Talus).Select(r => Up(in r)).ToList();
			Assert.That(ups.Count, Is.GreaterThan(5));
			foreach (Vector3 a in ups)
			{
				Assert.That(Vector3.Angle(a, Vector3.up), Is.LessThan(10f + 6f), "a regional dip of at most 10°, ±3° per rock");
				Assert.That(Vector3.Angle(a, ups[0]), Is.LessThan(12f), "every bed parallel to the cliff's bedding");
			}
		}

		[Test]
		public void Orientation_ColumnsStandVertical()
		{
			foreach (PlacedCliffRock r in PlanOf("Basalt").Rocks.Where(r => !r.Talus))
			{
				Assert.That(Vector3.Angle(Up(in r), Vector3.up), Is.LessThanOrEqualTo(4.01f), $"{r.Piece}");
			}
		}

		[Test]
		public void Orientation_FoliationIsOneSteepPlane()
		{
			var ups = PlanOf("Slate").Rocks.Where(r => !r.Talus).Select(r => Up(in r)).ToList();
			Assert.That(ups.Count, Is.GreaterThan(5));
			foreach (Vector3 a in ups)
			{
				Assert.That(Vector3.Angle(a, Vector3.up), Is.InRange(30f, 90f), "foliation stands steep (45–80° ± the rock's own wander)");
				Assert.That(Vector3.Angle(a, ups[0]), Is.LessThan(22f), "every slab on the cliff's one foliation");
			}
		}

		// ── Variety and budget (Jim 2026-10-04: "sparse, and all identical") ──

		[Test]
		public void FaceRoles_HaveSeveralVariants([Values("Granite", "Sandstone", "Basalt", "Slate", "Ice")] string type)
		{
			foreach (CliffRole role in new[] { CliffRole.Base, CliffRole.Mid, CliffRole.Fill })
			{
				int variants = CliffRocks.ShapesOf(type, role).Sum(s => s.Variants);
				Assert.That(variants, Is.GreaterThanOrEqualTo(3), $"{type} {role}: the roles a cliff mostly shows need more than one or two meshes");
			}
			Assert.That(CliffRocks.ShapesOf(type, CliffRole.Titan).Sum(s => s.Variants), Is.GreaterThanOrEqualTo(2), $"{type} titans");
		}

		[Test]
		public void Variants_HaveTheirOwnProportions_AndMeshes()
		{
			var v0 = new CliffPiece("Sandstone", CliffRole.Base, 0, -1, 0);
			var v1 = new CliffPiece("Sandstone", CliffRole.Base, 0, -1, 1);
			var v2 = new CliffPiece("Sandstone", CliffRole.Base, 0, -1, 2);
			CliffRocks.Proportions(in v0, out float s0, out float h0, out float e0);
			Assert.That((s0, h0, e0), Is.EqualTo((1f, 1f, 1f)), "variant 0 is the shape as declared");
			CliffRocks.Proportions(in v1, out float s1, out float h1, out _);
			CliffRocks.Proportions(in v2, out float s2, out float h2, out _);
			Assert.That(s1, Is.InRange(0.88f, 1.12f));
			Assert.That(h1, Is.InRange(0.82f, 1.22f));
			Assert.That(Mathf.Abs(s1 - s2) + Mathf.Abs(h1 - h2), Is.GreaterThan(1e-3f), "two variants, two sets of proportions");
			CliffRocks.Proportions(in v1, out float again, out _, out _);
			Assert.That(again, Is.EqualTo(s1), "proportions come from the piece, never from a running generator");

			Vector3 a = Seating(v0).Bounds.size, b = Seating(v1).Bounds.size, c = Seating(v2).Bounds.size;
			Assert.That(Mathf.Max((a - b).magnitude, (a - c).magnitude), Is.GreaterThan(0.02f * a.magnitude), "variants are different rocks, not one rock re-seeded into the same box");
		}

		[Test]
		public void Plan_UsesManyPiecesSizesAndTurns()
		{
			List<PlacedCliffRock> face = PlanOf("Sandstone").Rocks.Where(r => !r.Talus).ToList();
			Assert.That(face.Select(r => r.Piece).Distinct().Count(), Is.GreaterThanOrEqualTo(4), "a cliff of one or two meshes reads as a stamp");
			var volumes = face.Select(r => r.Scale.x * r.Scale.y * r.Scale.z).ToList();
			Assert.That(volumes.Max() / volumes.Min(), Is.GreaterThan(1.3f), "the rocks of a cliff take different sizes");
		}

		[Test]
		public void Budget_GrowsWithTheCliff_WithinItsFloorAndCeiling()
		{
			var o = new CliffRockPlacementOptions();
			Assert.That(CliffRockPlacement.BudgetFor(500f, o), Is.EqualTo(o.MinBudget), "a small cliff keeps the old ceiling");
			Assert.That(CliffRockPlacement.BudgetFor(20000f, o), Is.EqualTo(Mathf.CeilToInt(20000f / 100f * o.RocksPer100Metres)));
			Assert.That(CliffRockPlacement.BudgetFor(1e7f, o), Is.EqualTo(o.MaxRocks), "never above the hard ceiling");
		}

		[Test]
		public void Budget_WhenItBites_ThinsEveryClassEvenly()
		{
			var options = new CliffRockPlacementOptions { MinBudget = 0, MaxRocks = 30 };
			CliffRockPlan plan = Plan("Sandstone", 0, options);
			Assert.That(plan.Stats.Budget, Is.EqualTo(30));
			Assert.That(plan.Rocks.Count, Is.LessThanOrEqualTo(30));
			var footing = plan.Rocks.Where(r => r.Base && r.BandT == 0f && !r.Talus).ToList();
			Assert.That(footing.Any(r => r.Position.x < 120f) && footing.Any(r => r.Position.x >= 120f), Is.True,
				"the footing is thinned along the whole foot line, not used up on the first stretch the tracer found");
			Assert.That(plan.Stats.Face, Is.GreaterThan(plan.Stats.Footing), "the footing leaves room for the face rocks");
		}

		[Test]
		public void Footing_IsSizedToTheBand()
		{
			Assert.That(CliffRockPlacement.FootRoleFor(3f), Is.EqualTo(CliffRole.Fill));
			Assert.That(CliffRockPlacement.FootRoleFor(7f), Is.EqualTo(CliffRole.Mid));
			Assert.That(CliffRockPlacement.FootRoleFor(25f), Is.EqualTo(CliffRole.Base));
		}

		[Test]
		public void Roundness_FollowsTheClimate()
		{
			Assert.That(CliffRocks.RoundnessFor(-0.6f, 0.5f), Is.EqualTo(0.3f).Within(1e-3f), "glacial");
			Assert.That(CliffRocks.RoundnessFor(0.6f, -0.8f), Is.EqualTo(0.3f).Within(1e-3f), "arid");
			Assert.That(CliffRocks.RoundnessFor(0.4f, 0.4f), Is.EqualTo(0.7f).Within(0.02f), "humid temperate");
			Assert.That(CliffRocks.RoundnessLevelFor(CliffRocks.RoundnessFor(0.75f, 0.6f)), Is.EqualTo(2), "tropical: the roundest level");
			Assert.That(CliffRocks.RoundnessLevelFor(0.3f), Is.EqualTo(0));
		}

		// ── Meshes and mapping ────────────────────────────────────────

		[Test]
		public void EveryType_HasAStructure_AndMeshNamesAreUnique()
		{
			var names = new HashSet<string>();
			foreach ((CliffPiece piece, int lod) in CliffRocks.AllMeshes())
			{
				Assert.That(names.Add(CliffRocks.MeshName(in piece, lod)), Is.True, CliffRocks.MeshName(in piece, lod));
			}
			foreach (RockType t in RockTypes.All)
			{
				Assert.That(CliffRocks.PiecesOf(t.Name).Any(), Is.True, t.Name);
				Assert.That(CliffRocks.MaterialName(t.Name), Is.Not.Null.And.Not.Empty, t.Name);
			}
			Assert.That(CliffRocks.StructureOf("Granite"), Is.EqualTo(CliffStructure.Jointed));
			Assert.That(CliffRocks.StructureOf("Sandstone"), Is.EqualTo(CliffStructure.Bedded));
			Assert.That(CliffRocks.StructureOf("Slate"), Is.EqualTo(CliffStructure.Foliated));
			Assert.That(CliffRocks.StructureOf("Basalt"), Is.EqualTo(CliffStructure.Columnar));
			Assert.That(CliffRocks.StructureOf(CliffRocks.Ice), Is.EqualTo(CliffStructure.Ice));
		}

		[Test]
		public void Families_MapToRocks()
		{
			Assert.That(CliffRocks.RockTypeFor(Ground.Basalt, null), Is.EqualTo("Basalt"));
			Assert.That(CliffRocks.RockTypeFor(Ground.Sandstone, null), Is.EqualTo("Sandstone"));
			Assert.That(CliffRocks.RockTypeFor(Ground.Ice, null), Is.EqualTo(CliffRocks.Ice));
			Assert.That(CliffRocks.RockTypeFor(Ground.Rock, null), Is.EqualTo("Granite"));
			Assert.That(CliffRocks.RockTypeFor(Ground.CliffRock, null), Is.EqualTo("Limestone"));
			Assert.That(CliffRocks.RockTypeFor(Ground.Soil, null), Is.Null, "soil banks stay terrain");
			BiomeArtSpec.Entry hills = BiomeArtSpec.For("Hills");
			if (hills != null)
			{
				Assert.That(CliffRocks.RockTypeFor(Ground.Rock, hills), Is.EqualTo("Granite"), "Hills scatter granite tors: their cliffs are granite");
			}
		}

		[Test]
		public void Meshes_AreClosed_AtEveryLevel_ForOnePieceOfEachRole([Values("Granite", "Sandstone", "Basalt", "Slate", "Ice")] string type)
		{
			foreach (CliffRole role in (CliffRole[])Enum.GetValues(typeof(CliffRole)))
			{
				CliffPiece piece = CliffRocks.PiecesOf(type).First(p => p.Role == role);
				int previous = int.MaxValue;
				for (int lod = 0; lod < CliffRocks.LodHeights.Length; lod++)
				{
					MeshBuilder m = CliffRocks.Build(in piece, lod, Seed);
					List<string> problems = m.Validate(true);
					Assert.That(problems, Is.Empty, $"{CliffRocks.MeshName(in piece, lod)}: {(problems.Count > 0 ? problems[0] : "")}");
					Assert.That(m.TriangleCount, Is.LessThanOrEqualTo(previous), $"{CliffRocks.MeshName(in piece, lod)} is no lighter than the level above");
					previous = m.TriangleCount;
				}
			}
		}

		[Test]
		public void Columns_HaveNoDomes_EveryColumnTopsNearTheClusterOrBroken()
		{
			CliffPiece piece = CliffRocks.PiecesOf("Basalt").First(p => p.Role == CliffRole.Base);
			MeshBuilder m = CliffRocks.Build(in piece, 0, Seed);
			int[] shell = CliffRocks.Shells(m);
			var top = new Dictionary<int, float>();
			float ground = float.MaxValue;
			for (int i = 0; i < m.VertexCount; i++)
			{
				ground = Mathf.Min(ground, m.Positions[i].y);
				top[shell[i]] = top.TryGetValue(shell[i], out float t) ? Mathf.Max(t, m.Positions[i].y) : m.Positions[i].y;
			}
			float highest = top.Values.Max(), span = highest - ground;
			int full = top.Values.Count(t => t - ground >= 0.85f * span);
			Assert.That(full, Is.GreaterThanOrEqualTo(top.Count / 2), "most columns reach the flow top (a dome would have one)");
			Assert.That(top.Values.All(t => t - ground >= 0.55f * span), Is.True, "no column is a stub");
		}
	}
}
