using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2l-b (2026-10-26): crossing styles chosen per biome, the straight-road way class with its raised and cut profiles and the
	/// royal road's ruts, cut stairs, and ways jogging at strike-slip traces. Pure (no Unity native calls), so purerun runs them.
	/// </summary>
	[TestFixture]
	public class PathWaysAndCrossingsTests
	{
		// ── Defaults: no biome names any of it, so every cut is as before ──

		[Test]
		public void NoBiomeNamesAStyleOrStraightRoads_SoNothingChanges()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			// An authored round names its own (legacy equality only): Scree's goblins' bridges, 2026-10-26.
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)))
			{
				Assert.That(entry.CrossingStyle, Is.EqualTo(BiomeCrossingStyle.Default), entry.Biome);
				Assert.That(entry.StraightRoads.IsSet, Is.False, entry.Biome);
			}
			// And no biome asset carries either field set (an asset written before the fields existed reads the defaults).
			string folder = Path.Combine("Assets", "Templates", "Entity", "Biomes");
			int assets = 0;
			foreach (string file in Directory.GetFiles(folder, "*.asset"))
			{
				assets++;
				if (BiomeAuthoredRounds.Contains(Path.GetFileNameWithoutExtension(file)))
				{
					continue;
				}
				foreach (string line in File.ReadAllLines(file))
				{
					string t = line.Trim();
					if (t.StartsWith("crossingStyle:", StringComparison.Ordinal))
					{
						Assert.That(t, Is.EqualTo("crossingStyle: 0"), file);
					}
					if (t.StartsWith("Form:", StringComparison.Ordinal) && File.ReadAllText(file).Contains("straightRoads:"))
					{
						Assert.That(t, Is.EqualTo("Form: 0"), file);
					}
				}
			}
			Assert.That(assets, Is.GreaterThanOrEqualTo(83));
		}

		[Test]
		public void TheStoredEnumsAreAppendOnly()
		{
			Assert.That((int)BiomeCrossingStyle.Default, Is.EqualTo(0));
			Assert.That((int)BiomeCrossingStyle.Liana, Is.EqualTo(1));
			Assert.That((int)BiomeCrossingStyle.CorbelledLaterite, Is.EqualTo(2));
			Assert.That((int)BiomeCrossingStyle.WindAndRain, Is.EqualTo(3));
			Assert.That((int)BiomeCrossingStyle.Cantilever, Is.EqualTo(4));
			Assert.That((int)BiomeCrossingStyle.GoblinsBridge, Is.EqualTo(5));
			Assert.That((int)BiomeCrossingStyle.SnowBridge, Is.EqualTo(6));
			Assert.That((int)BiomeStraightRoadForm.None, Is.EqualTo(0));
			Assert.That((int)BiomeStraightRoadForm.Cut, Is.EqualTo(3));
			Assert.That((int)ScenePathForm.Routed, Is.EqualTo(0));
			Assert.That((int)ScenePathForm.Straight, Is.EqualTo(1));
			Assert.That((int)ScenePathForm.Raised, Is.EqualTo(2));
			Assert.That((int)ScenePathForm.Cut, Is.EqualTo(3));
			Assert.That((int)ScenePathPointFlags.Ferry, Is.EqualTo(1 << 6), "the flags before it keep their bits");
			Assert.That((int)ScenePathPointFlags.Stairs, Is.EqualTo(1 << 7));
			Assert.That(new ScenePath().Form, Is.EqualTo(ScenePathForm.Routed), "a way stored before forms is routed");
		}

		[Test]
		public void ARoutedWaysNumbersAreItsClassesExactly()
		{
			foreach (ScenePathClass kind in Enum.GetValues(typeof(ScenePathClass)))
			{
				ScenePathStyle a = ScenePathStyle.For(kind), b = ScenePathStyle.For(kind, ScenePathForm.Routed);
				Assert.That(b.HalfWidth, Is.EqualTo(a.HalfWidth));
				Assert.That(b.Shoulder, Is.EqualTo(a.Shoulder));
				Assert.That(b.Dip, Is.EqualTo(a.Dip));
				Assert.That(b.MaxCut, Is.EqualTo(a.MaxCut));
				Assert.That(b.SmoothMetres, Is.EqualTo(a.SmoothMetres));
				Assert.That(b.Clearance, Is.EqualTo(a.Clearance));
				Assert.That(b.MaxGrade, Is.EqualTo(a.MaxGrade));
				Assert.That(b.Surface, Is.EqualTo(a.Surface));
			}
			ScenePathStyle raised = ScenePathStyle.For(ScenePathClass.Road, ScenePathForm.Raised), cut = ScenePathStyle.For(ScenePathClass.Road, ScenePathForm.Cut);
			Assert.That(raised.Dip, Is.EqualTo(-ScenePathStyle.RaisedMetres), "an embankment over the ground");
			Assert.That(cut.Dip, Is.EqualTo(ScenePathStyle.CutMetres), "a sunk way");
			Assert.That(cut.Shoulder, Is.LessThan(ScenePathStyle.For(ScenePathClass.Road).Shoulder), "between near-sheer walls");
		}

		// ── Crossing styles ──────────────────────────────────────────

		[Test]
		public void EveryStyleHasItsSpansTemplateAndPiece_AndWeighsNothingInAnyPick()
		{
			List<PointOfInterestTemplateSpec> specs = PointOfInterestTemplateSpecs.All();
			var kit = new HashSet<string>(StructurePieces.All.Select(p => p.Id));
			Assert.That(PathCrossings.StyleVariant(BiomeCrossingStyle.Default), Is.Null);
			Assert.That(PathCrossings.StylePiece(BiomeCrossingStyle.Default), Is.Null);
			foreach (BiomeCrossingStyle style in Enum.GetValues(typeof(BiomeCrossingStyle)))
			{
				if (style == BiomeCrossingStyle.Default)
				{
					continue;
				}
				Vector2 span = PathCrossings.StyleSpan(style);
				Assert.That(span.x, Is.GreaterThan(0f), $"{style}");
				Assert.That(span.y, Is.GreaterThan(span.x), $"{style}");
				string variant = PathCrossings.StyleVariant(style), piece = PathCrossings.StylePiece(style);
				PointOfInterestTemplateSpec spec = specs.Find(s => s.Kind == POIType.Bridge && s.Variant == variant);
				Assert.That(spec, Is.Not.Null, $"{style}: a Bridge template '{variant}'");
				Assert.That(spec.DefaultBiomeWeight, Is.EqualTo(0f), $"{style}: no seeded pick draws it");
				Assert.That(spec.BiomeWeights, Is.Empty, $"{style}: no biome weighs it");
				Assert.That(spec.Pieces.Exists(p => p.Tag == "=" + piece && p.Role == PointOfInterestSlotRole.Span), Is.True, $"{style}: its span is its piece");
				Assert.That(kit.Contains(piece), Is.True, $"{style}: {piece} is in the kit");
				StructurePiece p0 = StructurePieces.Find(piece);
				Assert.That(p0.Tags, Is.EqualTo(new[] { "crossing" }), $"{piece}: no older query draws it");
				Assert.That(p0.Anchor, Is.EqualTo(StructureAnchor.Deck));
				Assert.That(p0.ModuleLength, Is.GreaterThan(0f));
			}
			// The kit's own bridge still answers its template, which still weighs everywhere.
			PointOfInterestTemplateSpec timber = specs.Find(s => s.Kind == POIType.Bridge && s.Variant == "Timber");
			Assert.That(timber.DefaultBiomeWeight, Is.EqualTo(1f));
			Assert.That(timber.Pieces[0].Tag, Is.EqualTo("bridge"));
		}

		[Test]
		public void AStyleBridgesOnlyItsSpans_AndNeverAGorge()
		{
			Assert.That(PathCrossings.StyleFits(BiomeCrossingStyle.Default, 10f, false), Is.False, "the kit's bridge is the seeded pick");
			Assert.That(PathCrossings.StyleFits(BiomeCrossingStyle.Liana, 20f, false), Is.True);
			Assert.That(PathCrossings.StyleFits(BiomeCrossingStyle.Liana, 20f, true), Is.False, "a gorge is crossed lip to lip by an arch or ropes");
			Assert.That(PathCrossings.StyleFits(BiomeCrossingStyle.Liana, 200f, false), Is.False, "no vine cable over a broad river");
			Assert.That(PathCrossings.StyleFits(BiomeCrossingStyle.SnowBridge, 6f, false), Is.True);
			Assert.That(PathCrossings.StyleFits(BiomeCrossingStyle.SnowBridge, 14f, false), Is.False, "a snow bridge only over a narrow channel");
			Assert.That(PathCrossings.StyleFits(BiomeCrossingStyle.WindAndRain, 90f, false), Is.True);
			Assert.That(PathCrossings.StyleFits(BiomeCrossingStyle.Cantilever, 3f, false), Is.False, "no cribs over a brook");
			Assert.That(PathCrossings.SpanOfRecord(PathBridgeSite.RecordRadius(20f)), Is.EqualTo(20f).Within(1e-4f), "a site's span from its radius");
		}

		// ── The straight-road network ────────────────────────────────

		private static PathStraightRoads.Link L(int a, int b, float length, BiomeStraightRoadForm form)
			=> new PathStraightRoads.Link { A = a, B = b, Length = length, Roads = new BiomeStraightRoads(form) };

		[Test]
		public void OnlyTheLongestConnectedSetIsTheNetwork()
		{
			var links = new List<PathStraightRoads.Link>
			{
				L(1, 2, 900f, BiomeStraightRoadForm.Straight),
				L(2, 3, 800f, BiomeStraightRoadForm.Straight),
				L(3, 4, 700f, BiomeStraightRoadForm.None),
				L(4, 5, 1000f, BiomeStraightRoadForm.Straight),
			};
			Assert.That(PathStraightRoads.Network(links), Is.EqualTo(new[] { 0, 1 }), "1–2–3 (1700 m) over 4–5 (1000 m); the unset link joins nothing");
			Assert.That(PathStraightRoads.Network(new List<PathStraightRoads.Link> { L(1, 2, 500f, BiomeStraightRoadForm.None) }), Is.Empty);
			Assert.That(PathStraightRoads.Join(new BiomeStraightRoads(BiomeStraightRoadForm.Raised, true), BiomeStraightRoads.None).IsSet, Is.False, "both ends lay them");
			BiomeStraightRoads mixed = PathStraightRoads.Join(new BiomeStraightRoads(BiomeStraightRoadForm.Raised, true), new BiomeStraightRoads(BiomeStraightRoadForm.Cut, false));
			Assert.That(mixed.Form, Is.EqualTo(BiomeStraightRoadForm.Straight));
			Assert.That(mixed.Ruts, Is.False);
		}

		[Test]
		public void AtMostTwoWaysAreCut_ThoseThroughTheHighestGround()
		{
			var links = new List<PathStraightRoads.Link>
			{
				L(1, 2, 500f, BiomeStraightRoadForm.Cut), L(2, 3, 500f, BiomeStraightRoadForm.Cut),
				L(3, 4, 500f, BiomeStraightRoadForm.Cut), L(4, 5, 500f, BiomeStraightRoadForm.Cut),
			};
			List<int> network = PathStraightRoads.Network(links);
			ScenePathForm[] forms = PathStraightRoads.Forms(links, network, new[] { 3f, 9f, 1f, 9f });
			Assert.That(forms.Count(f => f == ScenePathForm.Cut), Is.EqualTo(PathStraightRoads.MaxCutWays));
			Assert.That(forms[1], Is.EqualTo(ScenePathForm.Cut));
			Assert.That(forms[3], Is.EqualTo(ScenePathForm.Cut));
			Assert.That(forms[0], Is.EqualTo(ScenePathForm.Straight), "the rest laid plain straight");
		}

		// ── The profile: grade, cut and fill, stairs ─────────────────

		private static List<float> Ground(int n, Func<int, float> h) => Enumerable.Range(0, n).Select(h).ToList();

		[Test]
		public void AStraightLineHoldsItsGrade_ClimbsACliffOnStairs_AndGivesUpOnAWall()
		{
			ScenePathStyle road = ScenePathStyle.For(ScenePathClass.Road, ScenePathForm.Straight);
			var stairs = new bool[201];
			Assert.That(PathStraightRoads.Profile(Ground(201, i => 10f), 2f, road, stairs), Is.True);
			Assert.That(stairs.Any(s => s), Is.False, "flat ground: no stairs");
			// A gentle hill the cut and fill even out.
			Assert.That(PathStraightRoads.Profile(Ground(201, i => 10f + 3f * Mathf.Sin(i * 0.05f)), 2f, road, stairs), Is.True);
			Assert.That(stairs.Any(s => s), Is.False);
			// An 8 m step the cut and fill take whole (4 m cut into its top, 4 m built at its foot): no stairs.
			stairs = new bool[201];
			Assert.That(PathStraightRoads.Profile(Ground(201, i => i < 100 ? 10f : 18f), 2f, road, stairs), Is.True);
			Assert.That(stairs.Any(s => s), Is.False);
			// A 14 m cliff across the line: one flight of stairs for what the cut and fill leave.
			stairs = new bool[201];
			Assert.That(PathStraightRoads.Profile(Ground(201, i => i < 100 ? 10f : 24f), 2f, road, stairs), Is.True);
			int first = Array.IndexOf(stairs, true), last = Array.LastIndexOf(stairs, true);
			Assert.That(first, Is.GreaterThan(0));
			Assert.That(last - first + 1, Is.EqualTo(stairs.Count(s => s)), "one flight");
			Assert.That(first, Is.LessThanOrEqualTo(100));
			Assert.That(last, Is.GreaterThanOrEqualTo(99));
			// A 40 m wall: neither the cut and fill nor a flight takes it.
			Assert.That(PathStraightRoads.Profile(Ground(201, i => i < 100 ? 10f : 50f), 2f, road, null), Is.False);
			// A long steady climb steeper than the class's grade is no cliff: it is routed instead.
			Assert.That(PathStraightRoads.Profile(Ground(201, i => 10f + 0.3f * 2f * i), 2f, road, null), Is.False);
		}

		[Test]
		public void TheCarveLaysAStairAsOneFlight_AndARaisedRoadOnItsEmbankment()
		{
			ScenePath path = ScenePathFieldTests.Line(new Vector3(0f, 0f, 0f), new Vector3(80f, 0f, 0f), 2.4f, ScenePathClass.Road, 41);
			var target = new float[41];
			for (int i = 0; i < 41; i++)
			{
				target[i] = i < 20 ? 10f : 18f;
			}
			for (int i = 15; i <= 25; i++)
			{
				path.Flags[i] = (byte)ScenePathPointFlags.Stairs;
			}
			PathCarver.Stairs(path, target);
			for (int i = 14; i <= 26; i++)
			{
				Assert.That(target[i], Is.EqualTo(Mathf.Lerp(10f, 18f, (i - 14) / 12f)).Within(1e-4f), $"point {i} on the flight");
			}
			Assert.That(target[0], Is.EqualTo(10f));
			Assert.That(target[40], Is.EqualTo(18f));

			// A raised road over flat ground stands its embankment's height up, and carving it again lays it at the same level: the
			// ground is read past its shoulders.
			ScenePath raised = ScenePathFieldTests.Line(new Vector3(0f, 0f, 0f), new Vector3(200f, 0f, 0f), 2.4f, ScenePathClass.Road, 101);
			raised.Form = ScenePathForm.Raised;
			float reach = 2.4f + ScenePathStyle.Of(raised).Shoulder;
			Func<float, float, float> flat = (x, z) => 10f;
			Func<float, float, float> carved = (x, z) => Mathf.Abs(z) < reach ? 10f + ScenePathStyle.RaisedMetres : 10f;
			float[] once = PathCarver.Targets(new[] { raised }, flat, null)[0];
			float[] twice = PathCarver.Targets(new[] { raised }, carved, null)[0];
			for (int i = 0; i < 101; i++)
			{
				Assert.That(once[i], Is.EqualTo(10f + ScenePathStyle.RaisedMetres).Within(1e-4f));
				Assert.That(twice[i], Is.EqualTo(once[i]).Within(1e-4f), "idempotent");
			}
			// A routed road over the same ground is laid as before (no embankment).
			ScenePath routed = ScenePathFieldTests.Line(new Vector3(0f, 0f, 0f), new Vector3(200f, 0f, 0f), 2.4f, ScenePathClass.Road, 101);
			Assert.That(PathCarver.Targets(new[] { routed }, flat, null)[0][50], Is.EqualTo(10f).Within(1e-4f));
		}

		// ── The planner ──────────────────────────────────────────────

		private static PointOfInterestRecord Site(int id, POIType kind, float x, float z, float radius)
			=> new PointOfInterestRecord { Id = id, Kind = kind, Position = new Vector3(x, 10f, z), Radius = radius, Yaw = 0f };

		private static PathPlanInput Input(List<PointOfInterestRecord> records, Func<float, float, float> ground = null, Func<float, float, PathCellWater> water = null)
		{
			var keepOuts = records.Select(r => new PointOfInterestKeepOut { Id = r.Id, X = r.Position.x, Z = r.Position.z, Radius = r.Radius }).ToList();
			return new PathPlanInput
			{
				WidthMetres = 3200f,
				DepthMetres = 3200f,
				Ground = ground ?? ((x, z) => 10f),
				WaterAt = water,
				WaterDepth = (x, z) => 0f,
				Records = records,
				KeepOuts = keepOuts,
				HasStreets = r => false,
				Seed = 11,
			};
		}

		/// <summary>Two towns off each other's axis, and a hill between them a routed way swings round.</summary>
		private static List<PointOfInterestRecord> TwoTowns() => new List<PointOfInterestRecord>
		{
			Site(1, POIType.Town, -800f, -300f, 40f),
			Site(2, POIType.Town, 800f, 300f, 40f),
		};

		private static float Hill(float x, float z) => 10f + 6f * Mathf.Exp(-(x * x + z * z) / (2f * 160f * 160f));

		/// <summary>The farthest any of a way's points off its sites lies from the straight line between its first and last such point, metres.</summary>
		private static float OffLine(ScenePath path)
		{
			var outside = new List<Vector2>();
			for (int i = 0; i < path.Count; i++)
			{
				if ((path.FlagsAt(i) & ScenePathPointFlags.InSite) == 0)
				{
					outside.Add(new Vector2(path.Points[i].x, path.Points[i].z));
				}
			}
			Vector2 a = outside[0], b = outside[outside.Count - 1], d = (b - a).normalized;
			float worst = 0f;
			foreach (Vector2 p in outside)
			{
				worst = Mathf.Max(worst, Mathf.Abs(d.x * (p.y - a.y) - d.y * (p.x - a.x)));
			}
			return worst;
		}

		[Test]
		public void NoBiomeLayingStraightRoads_PlansExactlyAsBefore()
		{
			PathPlan before = PathNetworkPlanner.Plan(Input(TwoTowns(), Hill));
			PathPlanInput none = Input(TwoTowns(), Hill);
			none.StraightRoadsAt = (x, z) => BiomeStraightRoads.None;
			PathPlan after = PathNetworkPlanner.Plan(none);
			Assert.That(after.Paths.Count, Is.EqualTo(before.Paths.Count));
			for (int p = 0; p < before.Paths.Count; p++)
			{
				Assert.That(after.Paths[p].Form, Is.EqualTo(ScenePathForm.Routed));
				Assert.That(after.Paths[p].Points, Is.EqualTo(before.Paths[p].Points), $"way {p}");
				Assert.That(after.Paths[p].Flags, Is.EqualTo(before.Paths[p].Flags), $"way {p}");
				Assert.That(after.Paths[p].Surface, Is.EqualTo(before.Paths[p].Surface), $"way {p}");
			}
		}

		[Test]
		public void AStraightRoadRunsDeadStraight_RaisedWithRutsWhereItsBiomeSaysSo()
		{
			PathPlanInput input = Input(TwoTowns(), Hill);
			input.StraightRoadsAt = (x, z) => new BiomeStraightRoads(BiomeStraightRoadForm.Raised, true);
			PathPlan plan = PathNetworkPlanner.Plan(input);
			ScenePath road = plan.Paths.Find(p => p.FromId > 0 && p.ToId > 0);
			Assert.That(road, Is.Not.Null, string.Join("\n", plan.Notes));
			Assert.That(road.Form, Is.EqualTo(ScenePathForm.Raised), string.Join("\n", plan.Notes));
			Assert.That(OffLine(road), Is.LessThan(1.5f), "dead straight between its ends");
			for (int i = 0; i < road.Count; i++)
			{
				Assert.That(road.Surface[i], Is.EqualTo((byte)ScenePathSurface.Track), "a royal road's ox-cart ruts");
			}
			// The same towns with no straight roads are routed.
			PathPlan routed = PathNetworkPlanner.Plan(Input(TwoTowns(), Hill));
			Assert.That(routed.Paths.TrueForAll(p => p.Form == ScenePathForm.Routed), Is.True);
		}

		[Test]
		public void AStraightRoadOverALakeIsRoutedInstead()
		{
			PathPlanInput input = Input(TwoTowns(), null, (x, z) => x * x + z * z < 120f * 120f ? PathCellWater.Open : PathCellWater.Dry);
			input.StraightRoadsAt = (x, z) => new BiomeStraightRoads(BiomeStraightRoadForm.Straight);
			PathPlan plan = PathNetworkPlanner.Plan(input);
			ScenePath road = plan.Paths.Find(p => p.FromId > 0 && p.ToId > 0);
			Assert.That(road, Is.Not.Null, string.Join("\n", plan.Notes));
			Assert.That(road.Form, Is.EqualTo(ScenePathForm.Routed), "the line crosses open water");
			foreach (Vector3 p in road.Points)
			{
				Assert.That(p.x * p.x + p.z * p.z, Is.GreaterThan(110f * 110f), "round the lake");
			}
		}

		// ── Strike-slip jogs ─────────────────────────────────────────

		[Test]
		public void AWayJogsAlongATraceForItsSlip()
		{
			// A trace along +z through x = 0, 2 km long, its left side (−x) slipped 30 m along it.
			var trace = new PathJogs.Trace(new Vector2(0f, -1000f), Vector2.up, 2000f, 30f);
			Assert.That(trace.OnSlippedSide(new Vector2(-5f, 0f)), Is.True);
			Assert.That(trace.SlipAt(1000f), Is.EqualTo(30f).Within(1e-3f));
			Assert.That(trace.SlipAt(-600f), Is.EqualTo(0f).Within(1e-3f), "faded out past the end");

			List<Vector2> Way() => PathStraightRoads.Line(new Vector2(200f, 0f), new Vector2(-200f, 0f), 4f);
			List<Vector2> line = Way();
			var pins = Enumerable.Repeat(false, line.Count).ToList();
			Assert.That(PathJogs.Apply(line, pins, new[] { trace }, null), Is.EqualTo(1));
			Assert.That(pins.Count, Is.EqualTo(line.Count), "the pins kept in step");
			Assert.That(line.Exists(p => Mathf.Abs(p.x) < 0.01f && Mathf.Abs(p.y - 30f) < 0.01f), Is.True, "along the trace for the slip");
			Assert.That(line.Exists(p => Mathf.Abs(p.x) < 0.01f && Mathf.Abs(p.y) < 0.01f), Is.True, "from where it crossed");
			Assert.That(line[0], Is.EqualTo(new Vector2(200f, 0f)));
			Assert.That(line[line.Count - 1], Is.EqualTo(new Vector2(-200f, 0f)), "its ends stay");

			// Too small a slip, a pinned stretch, or ground that will not carry the legs: no jog.
			line = Way();
			Assert.That(PathJogs.Apply(line, null, new[] { new PathJogs.Trace(new Vector2(0f, -1000f), Vector2.up, 2000f, 2f) }, null), Is.EqualTo(0));
			line = Way();
			pins = Enumerable.Range(0, line.Count).Select(i => line[i].x < 0f && line[i].x > -20f).ToList();
			Assert.That(PathJogs.Apply(line, pins, new[] { trace }, null), Is.EqualTo(0));
			line = Way();
			Assert.That(PathJogs.Apply(line, null, new[] { trace }, (a, b) => false), Is.EqualTo(0));
			Assert.That(line.Count, Is.EqualTo(Way().Count), "left as it was");
		}

		// ── Wiring ───────────────────────────────────────────────────

		private static string Source(string relative) => File.ReadAllText(Path.Combine("Assets", "Scripts", "Shared", "Implementation", relative));

		[Test]
		public void TheCutAsksTheStylesAndTheRoads_AndTheArtIsHashed()
		{
			string stage = Source(Path.Combine("Tools", "Extensions", "Unity", "Editor", "World", "PointsOfInterest", "PointOfInterestStage.cs"));
			Assert.That(stage, Does.Contain("CrossingTemplate(library, biome, site.Span, site.Gorge)"), "a way's bridge in its ends' style");
			Assert.That(stage, Does.Contain("CrossingTemplate(library, biome, PathCrossings.SpanOfRecord(record.Radius), false)"), "a placed bridge too");
			Assert.That(stage, Does.Contain("StraightRoadsAt = StraightRoadsAt(input, biomes)"));
			Assert.That(stage, Does.Contain("Faults = Faults.Count > 0 ? Faults : null"));
			string generator = Source(Path.Combine("Tools", "Extensions", "Unity", "Editor", "World", "SceneGenerator.cs"));
			Assert.That(generator, Does.Contain("poi.Faults.AddRange(SceneLandforms.StrikeSlipTraces(landforms.Sites))"));
			string authoring = Source(Path.Combine("Tools", "Extensions", "Unity", "Editor", "World", "ProceduralArt", "BiomeArtAuthoring.cs"));
			Assert.That(authoring, Does.Contain("if (crossing != BiomeCrossingStyle.Default || biome.CrossingStyle != BiomeCrossingStyle.Default || biome.IsAuthored(BiomeTemplate.Authored.CrossingStyle))"),
				"the style's mark is written only when set");
			Assert.That(authoring, Does.Contain("if (straight.IsSet || biome.StraightRoads.IsSet || biome.IsAuthored(BiomeTemplate.Authored.StraightRoads))"));
			Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain("StructurePieces.Crossings.cs"));
			Assert.That(StructurePieces.Tags, Does.Contain("crossing"));
		}
	}
}
