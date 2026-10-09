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

		private static CliffRockPlan Plan(string type, CliffRockPlacementOptions options = null)
		{
			CliffRockSiteAt site = (float x, float z, float y, out CliffRockSite s) =>
			{
				s = new CliffRockSite { Type = type, MinAngle = 40f };
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

		/// <summary>
		/// Slid out of a convex brow, a face or fill section hung its flat floor over the falling ground and looked poised
		/// in the air (Jim, 2026-10-08). Every section, not only the footing, is lowered until no underside shows.
		/// </summary>
		[Test]
		public void NoSectionShowsItsUnderside([Values("Granite", "Sandstone", "Basalt")] string type)
		{
			int sections = 0;
			foreach (PlacedCliffRock r in PlanOf(type).Rocks.Where(r => !r.Talus && CliffRocks.IsSection(in r.Piece)))
			{
				sections++;
				Assert.That(r.Underside, Is.LessThanOrEqualTo(CliffRockPlacement.SectionMostUnderside), $"{r.Piece} hangs its underside over the ground");
			}
			Assert.That(sections, Is.GreaterThan(5), "the band still takes its sections");
		}

		/// <summary>
		/// Debris is cliff sections now, blocks with broad flat faces, and the best of 24 random orientations stood them
		/// on an edge or a corner (Jim, 2026-10-08: "randomly rotating … levitating"). A block rests flat on a face: its
		/// lowest points are a face's worth of vertices, not one corner, and no orientation sits its centre lower.
		/// </summary>
		[Test]
		public void Debris_RestsFlatOnAFace([Values(0, 1)] int kind, [Values(0f, 25f, 35f)] float slope)
		{
			var piece = new CliffPiece("Granite", CliffRole.Debris, kind, 0);
			MeshBuilder mesh = Seating(piece);
			Vector3 com = Vector3.zero;
			foreach (Vector3 p in mesh.Positions) com += p;
			com /= mesh.VertexCount;
			var scale = new Vector3(1.1f, 0.9f, 1f);
			Vector3 down = new Vector3(0f, 0f, -1f);
			float sl = slope * Mathf.Deg2Rad;
			// The ground falling toward −z at the slope: h = tan θ · z, normal (−∇h, 1), worked out apart from the planner.
			Vector3 n = new Vector3(0f, 1f, -Mathf.Tan(sl)).normalized;
			Matrix4x4 pose = CliffRockPlacement.StablePose(mesh, com, scale, down, slope, new DeterministicRNG(5));
			float size = 0f, lowest = float.MaxValue;
			var heights = new List<Vector3>();
			foreach (Vector3 v in mesh.Positions)
			{
				Vector3 p = pose.MultiplyVector(new Vector3(v.x * scale.x, v.y * scale.y, v.z * scale.z));
				heights.Add(p);
				lowest = Mathf.Min(lowest, Vector3.Dot(p, n));
				size = Mathf.Max(size, p.magnitude);
			}
			// The points on the ground: within 2 % of the rock's size of its lowest. They span an area, not a corner or an edge.
			List<Vector3> contact = heights.Where(p => Vector3.Dot(p, n) - lowest <= 0.02f * size).ToList();
			Assert.That(contact.Count, Is.GreaterThanOrEqualTo(3), "rests on more than a corner");
			float spread = 0f;
			for (int i = 0; i < contact.Count; i++)
			{
				for (int j = i + 1; j < contact.Count; j++)
				{
					for (int k = j + 1; k < contact.Count; k++)
					{
						spread = Mathf.Max(spread, Vector3.Cross(contact[j] - contact[i], contact[k] - contact[i]).magnitude);
					}
				}
			}
			Assert.That(spread, Is.GreaterThan(0.05f * size * size), "the contact is a face, not an edge");
			float rest = CliffRockPlacement.ComHeight(mesh, com, scale, pose, n);
			var other = new DeterministicRNG(99);
			float random = float.MaxValue;
			for (int i = 0; i < 200; i++)
			{
				float y = other.Range(-1f, 1f), a = other.NextFloat() * 6.2831853f, h = Mathf.Sqrt(1f - y * y);
				random = Mathf.Min(random, CliffRockPlacement.ComHeight(mesh, com, scale, CliffRockPlacement.Rot(new Vector3(h * Mathf.Cos(a), y, h * Mathf.Sin(a)), other.NextFloat() * 360f), n));
			}
			Assert.That(rest, Is.LessThanOrEqualTo(random + 0.02f * size), "no random orientation sits its centre lower");
		}

		/// <summary>
		/// The slope normal leans down the hill. It was built leaning up it (a mirror of the slope, 2θ off), so every
		/// talus rock was rested on a slope that was not there and stood on end on its cone (Jim, 2026-10-08).
		/// </summary>
		[Test]
		public void SlopeNormal_LeansDownTheHill()
		{
			Vector3 n = CliffRockPlacement.SlopeNormal(new Vector3(0f, 0f, -1f), 30f);
			Assert.That(n.z, Is.LessThan(0f), "toward the fall line");
			Assert.That(Vector3.Angle(n, new Vector3(0f, 1f, -Mathf.Tan(30f * Mathf.Deg2Rad))), Is.LessThan(0.01f));
		}

		/// <summary>
		/// Every talus rock of a planned cliff lies on the ground it was planned on (the slope plus the cones raised into
		/// it): its centre of mass over that ground's plane no higher than the steadiness allowance over the lowest it can
		/// rest at. Before the slope normal's sign was fixed the median was 2.2 times it.
		/// </summary>
		[Test]
		public void Talus_RestsOnTheGroundItLiesOn([Values("Granite", "Sandstone")] string type)
		{
			CliffRockPlan plan = PlanOf(type);
			var ground = new Slope();
			float G(float x, float z) { ground.TryHeight(x, z, out float h); return h + plan.HeightDelta(x, z); }
			var ratios = new List<float>();
			foreach (PlacedCliffRock r in plan.Rocks.Where(r => r.Talus))
			{
				MeshBuilder m = Seating(r.Piece);
				var w = m.Positions.Select(v => r.Rotation.MultiplyVector(new Vector3(v.x * r.Scale.x, v.y * r.Scale.y, v.z * r.Scale.z))).ToList();
				Vector3 c = Vector3.zero;
				foreach (Vector3 v in w) c += v;
				c /= w.Count;
				float size = w.Max(v => (v - c).magnitude), e = Mathf.Max(1f, 0.5f * size);
				Vector3 n = new Vector3(-(G(r.Position.x + e, r.Position.z) - G(r.Position.x - e, r.Position.z)) / (2f * e), 1f,
					-(G(r.Position.x, r.Position.z + e) - G(r.Position.x, r.Position.z - e)) / (2f * e)).normalized;
				float actual = Vector3.Dot(c, n) - w.Min(v => Vector3.Dot(v, n));
				float best = float.MaxValue;
				for (int k = 0; k < 400; k++)
				{
					float y = 1f - 2f * (k + 0.5f) / 400f, rr = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), a = k * 2.3999632f;
					var u = new Vector3(rr * Mathf.Cos(a), y, rr * Mathf.Sin(a));
					best = Mathf.Min(best, w.Max(v => Vector3.Dot(v, u)) - Vector3.Dot(c, u));
				}
				ratios.Add(actual / best);
			}
			Assert.That(ratios.Count, Is.GreaterThan(10));
			ratios.Sort();
			Assert.That(ratios[ratios.Count / 2], Is.LessThan(1.15f), "the median talus rock rests low on the ground under it");
			Assert.That(ratios[ratios.Count * 9 / 10], Is.LessThan(1.5f), "and nine in ten nearly so (the cone's curvature under a big block)");
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
			var piece = new CliffPiece("Granite", CliffRole.Debris, 1, 0);
			MeshBuilder mesh = Seating(piece);
			Vector3 com = Vector3.zero;
			foreach (Vector3 p in mesh.Positions) com += p;
			com /= mesh.VertexCount;
			var scale = new Vector3(1.1f, 0.9f, 1f);
			Vector3 down = new Vector3(0f, 0f, -1f);
			const float slope = 25f;
			float sl = slope * Mathf.Deg2Rad;
			// The ground falling toward −z at the slope: h = tan θ · z, normal (−∇h, 1), worked out apart from the planner.
			Vector3 n = new Vector3(0f, 1f, -Mathf.Tan(sl)).normalized;
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

		/// <summary>
		/// Every rock type's face is cliff sections, their front (−z as built) down the fall line: the test slope
		/// rises toward +z, so the front faces −z. Footing and crest sections stand upright (leaning back at most
		/// 15 % of the slope, 9° on the 60° band); those on the face lean back with it, 30–50 % (at most 30°).
		/// Every one turns ±15° (plus what the smoothed fall line wanders).
		/// </summary>
		[Test]
		public void Orientation_SectionsStandUpright_FrontDownTheFallLine([Values("Granite", "Sandstone", "Basalt", "Slate")] string type)
		{
			List<PlacedCliffRock> face = PlanOf(type).Rocks.Where(r => !r.Talus).ToList();
			Assert.That(face.Count, Is.GreaterThan(5));
			foreach (PlacedCliffRock r in face)
			{
				Assert.That(CliffRocks.IsSection(in r.Piece), Is.True, $"{r.Piece} is a face rock but not a section");
				float most = (r.Base || r.Piece.Role == CliffRole.Crest ? 0.15f : 0.5f) * 60f + 0.5f;
				Assert.That(Vector3.Angle(Up(in r), Vector3.up), Is.LessThanOrEqualTo(most), $"{r.Piece} leans too far");
				Vector3 front = r.Rotation.MultiplyVector(Vector3.back);
				front.y = 0f;
				Assert.That(Vector3.Angle(front, Vector3.back), Is.LessThan(35f), $"{r.Piece} faces down the slope");
			}
		}

		/// <summary>
		/// A section shows its face: buried along the slope normal as a boulder is, sections on a steep face went in
		/// flush — three quarters of them underground, the face a flat cut patch (Flo Monolith, 2026-10-08).
		/// </summary>
		[Test]
		public void Sections_ShowTheirFace_NeverAFlatPatchOnTheSlope([Values("Granite", "Sandstone")] string type)
		{
			List<float> hidden = PlanOf(type).Rocks.Where(r => !r.Talus && !r.Backdrop).Select(r => r.Hidden).OrderBy(h => h).ToList();
			Assert.That(hidden, Is.Not.Empty);
			Assert.That(hidden.Max(), Is.LessThanOrEqualTo(CliffRockPlacement.SectionMostHidden + 1e-3f));
			Assert.That(hidden[hidden.Count / 2], Is.LessThanOrEqualTo(0.5f), "the median section is mostly out of the ground");
		}

		/// <summary>
		/// The sheer backdrop (Jim, 2026-10-08: "larger variants as background ... the height scale based on the
		/// height of the cliff itself"): scarps stand along the foot of the 48 m band, sized to it — the test band
		/// takes one tier to near the crest, or a lower wall and a second on it — and stand upright, their back in
		/// the hill and their face out of it.
		/// </summary>
		[Test]
		public void Backdrops_StandAlongTheFoot_AsTallAsTheCliff([Values("Granite", "Sandstone", "Basalt")] string type)
		{
			List<PlacedCliffRock> walls = PlanOf(type).Rocks.Where(r => r.Backdrop).ToList();
			Assert.That(walls.Count, Is.GreaterThanOrEqualTo(5), "a wall every ~25 m of a 214 m cliff");
			foreach (PlacedCliffRock r in walls)
			{
				Assert.That(r.Piece.Role, Is.EqualTo(CliffRole.Backdrop));
				Assert.That(Vector3.Angle(Up(in r), Vector3.up), Is.LessThanOrEqualTo(0.08f * 90f + 0.5f), $"{r.Piece} stands upright");
				Assert.That(r.Hidden, Is.LessThanOrEqualTo(0.8f), $"{r.Piece} is mostly underground");
			}
			// Heights: the tallest wall reaches most of the 48 m cliff, none passes it by much.
			var heights = walls.Select(r => Seating(r.Piece).Bounds.size.y * r.Scale.y).ToList();
			Assert.That(heights.Max(), Is.InRange(0.75f * 48f, 1.15f * 48f));
		}

		[Test]
		public void Budget_WhenItBites_BackdropsKeepToTheirShare()
		{
			var options = new CliffRockPlacementOptions { MinBudget = 0, MaxRocks = 30 };
			CliffRockPlan plan = Plan("Sandstone", options);
			Assert.That(plan.Stats.Backdrops, Is.InRange(1, Mathf.RoundToInt(30 * options.BackdropShare)));
			var walls = plan.Rocks.Where(r => r.Backdrop).ToList();
			Assert.That(walls.Any(r => r.Position.x < 120f) && walls.Any(r => r.Position.x >= 120f), Is.True, "spread along the whole foot line");
		}

		[Test]
		public void Orientation_IceKeepsItsSeracs()
		{
			List<PlacedCliffRock> face = PlanOf("Ice").Rocks.Where(r => !r.Talus).ToList();
			Assert.That(face, Is.Not.Empty);
			Assert.That(face.All(r => !CliffRocks.IsSection(in r.Piece)), Is.True, "glacier ice is not jointed, bedded rock");
		}

		/// <summary>One section mesh serves every rock type, face and talus alike: the type is the material, not the geometry.</summary>
		[Test]
		public void Sections_AreSharedByEveryType_FaceAndTalus()
		{
			foreach (CliffRole role in new[] { CliffRole.Mid, CliffRole.Debris })
			{
				var granite = new CliffPiece("Granite", role, 0, 1);
				var sandstone = new CliffPiece("Sandstone", role, 0, 1);
				Assert.That(CliffRocks.IsSection(in granite), Is.True, $"{role}");
				Assert.That(CliffRocks.MeshName(in granite, 0), Is.EqualTo(CliffRocks.MeshName(in sandstone, 0)), $"{role}");
			}
			Assert.That(CliffRocks.MeshName(new CliffPiece("Granite", CliffRole.Mid, 0, 1), 0), Is.EqualTo(CliffSections.MeshName("Wall", 1, 0)));
			Assert.That(CliffRocks.MeshName(new CliffPiece("Granite", CliffRole.Debris, 0, 0), 0), Is.EqualTo(CliffSections.MeshName("Rubble", 0, 0)));
			Assert.That(CliffRocks.MaterialName("Granite"), Is.Not.EqualTo(CliffRocks.MaterialName("Sandstone")));
			Assert.That(CliffRocks.AllMeshes().All(m => m.Piece.Type == CliffRocks.Ice), Is.True, "sections are written once, by CliffSections; only ice is the cliff rocks' own");
		}

		/// <summary>No talus of a rock cliff is a formation any more (sandstone's were round bedded "cakes": the old cliff look).</summary>
		[Test]
		public void Talus_IsFallenBlocksOfTheWall([Values("Granite", "Sandstone", "Basalt", "Slate")] string type)
		{
			List<PlacedCliffRock> talus = PlanOf(type).Rocks.Where(r => r.Talus).ToList();
			Assert.That(talus, Is.Not.Empty);
			foreach (PlacedCliffRock r in talus)
			{
				Assert.That(CliffRocks.IsSection(in r.Piece), Is.True, $"{r.Piece}");
				Assert.That(CliffSections.IsDebris(CliffRocks.ShapeOf(in r.Piece).Section), Is.True, $"{r.Piece} is a face section on the talus");
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
			// Ice seracs (sections have their own, CliffSections.StyleOf(style, variant)).
			var v0 = new CliffPiece("Ice", CliffRole.Base, 0, 0);
			var v1 = new CliffPiece("Ice", CliffRole.Base, 0, 1);
			var v2 = new CliffPiece("Ice", CliffRole.Base, 0, 2);
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
			CliffRockPlan plan = Plan("Sandstone", options);
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
				if (!CliffRocks.PiecesOf(type).Any(p => p.Role == role))
				{
					Assert.That(type == CliffRocks.Ice && role == CliffRole.Backdrop, Is.True, $"{type} has no {role} rock");
					continue;
				}
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
	}
}
