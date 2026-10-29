using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2f-b of the biome suite (2026-10-26): the later drafts' linear styles and forms on the I2f model. Named styles as data
	/// (BiomeLinearStyles), marker lines, windbreaks, a frontier's barrier with its ditch, breaches and forts, a hand rope, the bisse's,
	/// roggia's, great canal's and qanat gallery's channel rules. Nothing is laid until a biome names a style, so no scene changes
	/// (BiomeLinearFeatureTests.Defaults_NoBiomeNamesALinearFeature).
	/// </summary>
	/// <remarks>Pure: the pieces' meshes are built by BiomeLinearFeatureTests.Native_LinearPieces_AreWithinBudget_AndPlayTheirRoles (Unity or nativerun).</remarks>
	public class BiomeLinearFormsTests
	{
		private const string Features = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Features/";

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		// ── The model ─────────────────────────────────────────────────

		[Test]
		public void NewValues_AreAppended_InTheirKindsTens()
		{
			Assert.That((int)BiomeLinearKind.Markers, Is.EqualTo(6));
			Assert.That((int)BiomeLinearPlace.Windbreaks, Is.EqualTo(6));
			Assert.That((int)BiomeLinearPlace.Frontier, Is.EqualTo(7));
			var expected = new (BiomeLinearForm Form, int Value, BiomeLinearKind Kind)[]
			{
				(BiomeLinearForm.Dead, 3, BiomeLinearKind.Hedge), (BiomeLinearForm.BrickBarrier, 14, BiomeLinearKind.Wall), (BiomeLinearForm.SnowWall, 15, BiomeLinearKind.Wall),
				(BiomeLinearForm.HandRope, 26, BiomeLinearKind.Fence), (BiomeLinearForm.PalmRib, 27, BiomeLinearKind.Fence), (BiomeLinearForm.BoughBreak, 28, BiomeLinearKind.Fence),
				(BiomeLinearForm.Bisse, 42, BiomeLinearKind.Channel), (BiomeLinearForm.Roggia, 43, BiomeLinearKind.Channel),
				(BiomeLinearForm.GreatCanal, 44, BiomeLinearKind.Channel), (BiomeLinearForm.Gallery, 45, BiomeLinearKind.Channel),
				(BiomeLinearForm.WandLine, 60, BiomeLinearKind.Markers), (BiomeLinearForm.SergePost, 61, BiomeLinearKind.Markers),
				(BiomeLinearForm.TamgaPole, 62, BiomeLinearKind.Markers), (BiomeLinearForm.Cairn, 63, BiomeLinearKind.Markers), (BiomeLinearForm.Ahu, 64, BiomeLinearKind.Markers),
			};
			foreach ((BiomeLinearForm form, int value, BiomeLinearKind kind) in expected)
			{
				Assert.That((int)form, Is.EqualTo(value), form.ToString());
				Assert.That(BiomeLinearFeatures.KindOf(form), Is.EqualTo(kind), form.ToString());
				Assert.That(BiomeLinearFeatures.DefaultHeight(form), Is.GreaterThan(0f), form.ToString());
			}
			// The I2f forms keep their values and their own numbers.
			Assert.That((int)BiomeLinearForm.Wild, Is.EqualTo(2));
			Assert.That((int)BiomeLinearForm.UprightSlabs, Is.EqualTo(13));
			Assert.That((int)BiomeLinearForm.Turfed, Is.EqualTo(51));
			Assert.That(BiomeLinearFeatures.ChannelHalfWidth(BiomeLinearForm.EarthDitch), Is.EqualTo(0.7f));
			Assert.That(BiomeLinearFeatures.ChannelHalfWidth(BiomeLinearForm.LinedChannel), Is.EqualTo(1f));
			Assert.That(PointOfInterestLinearFeatures.ChannelHalfWidth(BiomeLinearForm.LinedChannel), Is.EqualTo(1f));
			Assert.That(BiomeLinearFeatures.IsLined(BiomeLinearForm.EarthDitch), Is.False);
			Assert.That(BiomeLinearFeatures.IsLined(BiomeLinearForm.LinedChannel), Is.True);
			Assert.That(BiomeLinearFeatures.MaxGradeOf(BiomeLinearForm.Drystone, PointOfInterestLinearFeatures.MaxGrade), Is.EqualTo(PointOfInterestLinearFeatures.MaxGrade));
			Assert.That(BiomeLinearFeatures.MaxGradeOf(BiomeLinearForm.HandRope, PointOfInterestLinearFeatures.MaxGrade), Is.GreaterThan(1f), "a hand rope is for the steep");
			Assert.That(BiomeLinearFeatures.MinSlopeOf(BiomeLinearForm.HandRope), Is.EqualTo(15f));
			Assert.That(BiomeLinearFeatures.MinSlopeOf(BiomeLinearForm.Laid), Is.Zero);
		}

		[Test]
		public void NewForms_KeepToTheirPlaces()
		{
			foreach (BiomeLinearPlace place in (BiomeLinearPlace[])Enum.GetValues(typeof(BiomeLinearPlace)))
			{
				Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.Markers, place), Is.EqualTo(place == BiomeLinearPlace.Lanes), $"markers at {place}");
				Assert.That(BiomeLinearFeatures.AllowsForm(BiomeLinearForm.BrickBarrier, place), Is.EqualTo(place == BiomeLinearPlace.Frontier), $"brick barrier at {place}");
				Assert.That(BiomeLinearFeatures.AllowsForm(BiomeLinearForm.HandRope, place), Is.EqualTo(place == BiomeLinearPlace.Lanes), $"hand rope at {place}");
				foreach (BiomeLinearForm channel in new[] { BiomeLinearForm.Bisse, BiomeLinearForm.GreatCanal, BiomeLinearForm.Gallery })
				{
					Assert.That(BiomeLinearFeatures.AllowsForm(channel, place), Is.EqualTo(place == BiomeLinearPlace.FieldEdges), $"{channel} at {place}");
				}
				// Every I2f form is allowed where it was.
				foreach (BiomeLinearForm old in new[] { BiomeLinearForm.Laid, BiomeLinearForm.Drystone, BiomeLinearForm.Rail, BiomeLinearForm.Filare, BiomeLinearForm.EarthDitch, BiomeLinearForm.Heaped })
				{
					if (place <= BiomeLinearPlace.TerraceEdges)
					{
						Assert.That(BiomeLinearFeatures.AllowsForm(old, place), Is.EqualTo(BiomeLinearFeatures.Allows(BiomeLinearFeatures.KindOf(old), place)), $"{old} at {place}");
					}
				}
			}
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.Wall, BiomeLinearPlace.Windbreaks), Is.True);
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.Channel, BiomeLinearPlace.Windbreaks), Is.False);
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.TreeRow, BiomeLinearPlace.Frontier), Is.False);
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.ClearanceRow, BiomeLinearPlace.Frontier), Is.True);
			Assert.That(BiomeLinearFeatures.AllowsForm(BiomeLinearForm.Roggia, BiomeLinearPlace.Lanes), Is.True, "a roggia also along the lanes");
			Assert.That(BiomeLinearFeatures.SceneCapOf(BiomeLinearForm.GreatCanal), Is.EqualTo(1));
			Assert.That(BiomeLinearFeatures.SceneCapOf(BiomeLinearForm.BrickBarrier), Is.EqualTo(1));
			Assert.That(BiomeLinearFeatures.SceneCapOf(BiomeLinearForm.Gallery), Is.EqualTo(4));
			Assert.That(BiomeLinearFeatures.SceneCapOf(BiomeLinearForm.Drystone), Is.Zero);
		}

		[Test]
		public void Markers_TheirFittingsAndEnds()
		{
			Assert.That(BiomeLinearFeatures.FittingEvery(BiomeLinearForm.SergePost), Is.EqualTo(5));
			Assert.That(BiomeLinearFeatures.FittingEvery(BiomeLinearForm.TamgaPole), Is.EqualTo(6));
			int lamps = 0, torches = 0;
			for (int k = 0; k < 30; k++)
			{
				lamps += BiomeLinearFeatures.MarkerVariant(BiomeLinearForm.SergePost, k, 30);
				torches += BiomeLinearFeatures.MarkerVariant(BiomeLinearForm.TamgaPole, k, 30);
			}
			Assert.That(lamps, Is.EqualTo(6), "every fifth of thirty");
			Assert.That(torches, Is.EqualTo(5), "every sixth of thirty");
			Assert.That(BiomeLinearFeatures.MarkerVariant(BiomeLinearForm.WandLine, 0, 7), Is.EqualTo(1), "a cairn at a wand line's start");
			Assert.That(BiomeLinearFeatures.MarkerVariant(BiomeLinearForm.WandLine, 6, 7), Is.EqualTo(1), "and at its end");
			Assert.That(BiomeLinearFeatures.MarkerVariant(BiomeLinearForm.WandLine, 3, 7), Is.Zero);
			Assert.That(BiomeLinearFeatures.MarkerVariant(BiomeLinearForm.Cairn, 3, 7), Is.Zero, "platy cairns");
			Assert.That(BiomeLinearFeatures.MarkerVariant(BiomeLinearForm.Ahu, 3, 7), Is.EqualTo(1), "lava ahu");
			foreach (BiomeLinearForm form in BiomeLinearFeatures.FormsOf(BiomeLinearKind.Markers))
			{
				string id = StructurePieces.MarkerPieceFor(form);
				Assert.That(StructurePieces.Find(id), Is.Not.Null, $"{form} → {id}");
				Assert.That(BiomeLinearFeatures.DefaultSpacing(form), Is.GreaterThanOrEqualTo(12f), $"{form} spaced out");
				Assert.That(BiomeLinearFeatures.DefaultGate(form), Is.Zero, $"{form} has no gate");
				Assert.That(StructurePieces.LinearPieceFor(form, false, 0f).Id, Is.Null, $"{form} is no module");
			}
		}

		// ── Named styles ──────────────────────────────────────────────

		/// <summary>The names the drafts gave (infra-master J4–J17): each must be in the table.</summary>
		private static readonly string[] DraftNames =
		{
			"Clawdd", "TuffRubble", "ChristsThorn", "Takegaki", "Chanoki", "Nozura", "SandFence", "SlateStack", "Tungardur", "MoraineDyke", "Albarrada",
			"LateriteEnclosure", "ThornBamboo", "Shilei", "Myrtle", "GenoardWall", "Kershef", "FlagFence", "HillDyke", "ZimbabweWall", "SanseongWall",
			"OcotilloFence", "RibFence", "SodWall", "FiqueRow", "PaintedPalisade", "Kuaiwi", "Plattenzaun", "Stackete", "Brolo", "BoughBreak", "ClearanceRow",
			"IyaEarthBank", "WandLine", "SergeLine", "SnowWall", "HandRope", "CairnLine", "TamgaPole", "AhuLine", "PalmRibFence", "FrondScreen", "BrickBarrier",
			"DeadHedge", "Bisse", "Roggia", "GreatCanal", "Gallery",
		};

		[Test]
		public void NamedStyles_AreEveryDraftName_LaidWhereTheyMayBe_AndFresh()
		{
			var seen = new HashSet<string>();
			foreach (BiomeNamedLinearStyle named in BiomeLinearStyles.All)
			{
				Assert.That(seen.Add(named.Name), Is.True, $"{named.Name} twice");
				Assert.That(named.Description, Is.Not.Empty, named.Name);
				BiomeLinearStyle style = named.Make();
				Assert.That(BiomeLinearFeatures.IsLaid(style), Is.True, $"{named.Name} ({BiomeLinearFeatures.Id(style)}) lays at its own place");
				Assert.That(style.Where, Is.EqualTo(named.Place));
				Assert.That(style.Form, Is.EqualTo(named.Form));
				Assert.That(style.Species, Is.Not.Null.And.Empty, "prefabs are the authoring's to resolve");
				Assert.That(style.Vine, Is.Not.Null);
				if (style.Kind == BiomeLinearKind.Hedge)
				{
					Assert.That(named.Species, Is.Not.Empty, $"{named.Name}: a hedge is grown of its bushes");
				}
				if (style.OwnMaterial)
				{
					Assert.That(StructurePieces.LinearPalettes(new[] { style }), Is.Not.Empty, $"{named.Name}: its own material reaches a piece");
				}
				// Fresh lists every time.
				BiomeLinearStyle again = named.Make(BiomeLinearPlace.Lanes, 0.5f);
				Assert.That(again.Species, Is.Not.SameAs(style.Species));
				Assert.That(again.Density, Is.EqualTo(0.5f));
				Assert.That(again.Where, Is.EqualTo(BiomeLinearPlace.Lanes));
			}
			foreach (string name in DraftNames)
			{
				Assert.That(BiomeLinearStyles.Has(name), Is.True, $"{name} is in the table");
			}
			Assert.That(BiomeLinearStyles.Has("NoSuchWall"), Is.False);
			Assert.That(BiomeLinearStyles.TryGet("Kershef", out BiomeNamedLinearStyle kershef), Is.True);
			Assert.That(kershef.Make().Material, Is.EqualTo(BiomeStructureMaterial.Adobe));
			Assert.That(BiomeLinearStyles.TryGet("Gallery", out BiomeNamedLinearStyle gallery) && gallery.Form == BiomeLinearForm.Gallery, Is.True);
		}

		[Test]
		public void SpecLines_ByName_ResolveOrLayNothing()
		{
			var clawdd = new BiomeArtSpec.Entry.Line("Clawdd");
			Assert.That(clawdd.Unknown, Is.Empty);
			Assert.That(clawdd.Style.Form, Is.EqualTo(BiomeLinearForm.EarthBank));
			Assert.That(clawdd.Style.Height, Is.EqualTo(1.5f));
			var dead = new BiomeArtSpec.Entry.Line("DeadHedge");
			Assert.That(dead.Species, Is.EqualTo(new[] { "Thornwrack" }), "its own bushes");
			Assert.That(dead.PrefabOf("Thornwrack", false), Is.EqualTo(ProceduralArtCatalogue.BushPrefab("Thornwrack")));
			Assert.That(dead.PrefabOf("Deadwood_PollardBoll", true), Is.EqualTo("Deadwood_PollardBoll"), "a deadwood prefab is kept by its name");
			var own = new BiomeArtSpec.Entry.Line("DeadHedge", "Blackthorn");
			Assert.That(own.Species, Is.EqualTo(new[] { "Blackthorn" }), "a file's own species win");
			var lanes = new BiomeArtSpec.Entry.Line("Kershef", BiomeLinearPlace.FieldEdges);
			Assert.That(lanes.Style.Where, Is.EqualTo(BiomeLinearPlace.FieldEdges));
			var typo = new BiomeArtSpec.Entry.Line("Kershaf");
			Assert.That(typo.Unknown, Is.EqualTo("Kershaf"));
			Assert.That(BiomeLinearFeatures.IsLaid(typo.Style), Is.False, "an unknown name lays nothing");
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach (BiomeArtSpec.Entry.Line line in entry.LinearLines())
				{
					Assert.That(line.Unknown, Is.Empty, $"{entry.Biome} names a linear style the table lacks");
				}
			}
		}

		// ── Frontiers, windbreaks ─────────────────────────────────────

		[Test]
		public void Breaches_EveryFortyToTwoHundredMetres_OneToThreeModules()
		{
			const float module = 4f;
			bool[] open = PointOfInterestLinearFeatures.Breaches(800, module, 1234);
			Assert.That(PointOfInterestLinearFeatures.Breaches(800, module, 1234), Is.EqualTo(open), "the same every cut");
			Assert.That(PointOfInterestLinearFeatures.Breaches(800, module, 99), Is.Not.EqualTo(open), "its own seed");
			int run = 0, gap = 0, gaps = 0;
			for (int k = 0; k < open.Length; k++)
			{
				if (open[k])
				{
					if (gap == 0 && k > 0)
					{
						Assert.That(run * module, Is.InRange(40f - module, 200f + module), $"wall before the gap at {k}");
					}
					gap++;
					run = 0;
				}
				else
				{
					if (gap > 0)
					{
						Assert.That(gap, Is.InRange(1, 3));
						gaps++;
					}
					gap = 0;
					run++;
				}
			}
			Assert.That(gaps, Is.InRange(10, 800 * (int)module / 40));
			Assert.That(PointOfInterestLinearFeatures.Breaches(0, module, 1), Is.Empty);
		}

		[Test]
		public void Ditch_LowersOnly_DeepestInItsMiddle_ContinuousAtItsEdge()
		{
			const float g = 10f, depth = 1.4f, half = 3f;
			Assert.That(PointOfInterestLinearFeatures.DitchGround(0f, g, depth, half), Is.EqualTo(g - depth).Within(1e-5f));
			Assert.That(PointOfInterestLinearFeatures.DitchGround(half, g, depth, half), Is.EqualTo(g));
			Assert.That(PointOfInterestLinearFeatures.DitchGround(half - 1e-3f, g, depth, half), Is.EqualTo(g).Within(1e-4f));
			Assert.That(PointOfInterestLinearFeatures.DitchGround(9f, g, depth, half), Is.EqualTo(g));
			float last = float.NegativeInfinity;
			for (float d = 0f; d <= half; d += 0.1f)
			{
				float at = PointOfInterestLinearFeatures.DitchGround(d, g, depth, half);
				Assert.That(at, Is.LessThanOrEqualTo(g));
				Assert.That(at, Is.GreaterThanOrEqualTo(last - 1e-5f), "rising out of it");
				last = at;
			}
		}

		[Test]
		public void Forts_ThreeToSixHundredMetresApart()
		{
			List<float> at = PointOfInterestLinearFeatures.FortStations(3000f, 77);
			Assert.That(at, Is.EqualTo(PointOfInterestLinearFeatures.FortStations(3000f, 77)));
			Assert.That(at.Count, Is.InRange(4, 10));
			Assert.That(at[0], Is.InRange(150f, 300f));
			for (int k = 1; k < at.Count; k++)
			{
				Assert.That(at[k] - at[k - 1], Is.InRange(300f, 600f));
			}
			Assert.That(at[at.Count - 1], Is.LessThan(3000f - 40f));
			Assert.That(PointOfInterestLinearFeatures.FortStations(100f, 77), Is.Empty, "a short run has none");
		}

		[Test]
		public void Windbreak_FacesTheWind()
		{
			Assert.That(PointOfInterestLinearFeatures.UpwindBearing(new Vector2(0f, -1f)), Is.EqualTo(0f).Within(1e-3f), "a north wind comes from 0°");
			Assert.That(PointOfInterestLinearFeatures.UpwindBearing(new Vector2(-1f, 0f)), Is.EqualTo(90f).Within(1e-3f), "blowing west: from the east");
			Assert.That(float.IsNaN(PointOfInterestLinearFeatures.UpwindBearing(Vector2.zero)), Is.True);
			var centre = new Vector2(100f, 50f);
			foreach (float upwind in new[] { 0f, 30f, 200f, 350f })
			{
				List<Vector2> arc = PointOfInterestLinearFeatures.WindwardArc(centre, null, 10f, 3f, upwind, PointOfInterestLinearFeatures.WindbreakHalfArc);
				Assert.That(arc.Count, Is.GreaterThanOrEqualTo(3));
				foreach (Vector2 p in arc)
				{
					Vector2 d = p - centre;
					float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
					Assert.That(Mathf.Abs(Mathf.DeltaAngle(bearing, upwind)), Is.LessThanOrEqualTo(PointOfInterestLinearFeatures.WindbreakHalfArc + 0.01f), $"{upwind}: {bearing}");
					Assert.That(d.magnitude, Is.EqualTo(13f).Within(0.01f), "the outline grown by its margin");
				}
			}
		}

		// ── Channels ──────────────────────────────────────────────────

		[Test]
		public void GridRoute_RunsOnTheGrid_TurningRarely()
		{
			const float yaw = 23f;
			float r = yaw * Mathf.Deg2Rad;
			var u = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var v = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			Func<float, float, float> flat = (x, z) => 5f;
			var from = new Vector2(3f, 7f);
			var to = new Vector2(150f, 95f);
			Assert.That(PointOfInterestLinearFeatures.GridRoute(from, to, yaw, 5f, 5f, flat, null, 4f, 40f, PointOfInterestLinearFeatures.RoggiaTurnMetres, out List<Vector2> path), Is.True);
			Assert.That(path[0], Is.EqualTo(from));
			Assert.That(path[path.Count - 1], Is.EqualTo(to));
			Assert.That(path.Count - 2, Is.InRange(1, 3), "a couple of right-angle turns at most on open ground");
			for (int i = 1; i + 2 < path.Count; i++)
			{
				Vector2 d = (path[i + 1] - path[i]).normalized;
				Assert.That(Mathf.Max(Mathf.Abs(Vector2.Dot(d, u)), Mathf.Abs(Vector2.Dot(d, v))), Is.EqualTo(1f).Within(1e-3f), "along one of the grid's axes");
			}
			Assert.That(PointOfInterestLinearFeatures.GridRoute(from, to, yaw, 5f, 5f, flat, null, 4f, 40f, PointOfInterestLinearFeatures.RoggiaTurnMetres, out List<Vector2> again), Is.True);
			Assert.That(again, Is.EqualTo(path), "deterministic");
			// A wall across the way with one gap: it goes through the gap.
			Vector2 gapAt = from + (to - from) * 0.5f;
			Func<Vector2, bool> wall = p => Mathf.Abs(Vector2.Dot(p - gapAt, (to - from).normalized)) < 6f && (p - gapAt).magnitude > 14f;
			Assert.That(PointOfInterestLinearFeatures.GridRoute(from, to, yaw, 5f, 5f, flat, wall, 4f, 80f, 10f, out List<Vector2> round), Is.True);
			for (int i = 0; i + 1 < round.Count; i++)
			{
				foreach (PointOfInterestLinearFeatures.Station st in PointOfInterestLinearFeatures.Stations(new[] { round[i], round[i + 1] }, 1f))
				{
					// The wall's core (a cell's reach inside it) is never crossed.
					bool core = Mathf.Abs(Vector2.Dot(st.At - gapAt, (to - from).normalized)) < 3f && (st.At - gapAt).magnitude > 17f;
					Assert.That(core, Is.False, $"through the wall at {st.At}");
				}
			}
		}

		[Test]
		public void QanatHead_IsTheNearestScarpFoot_ElseTheNearestRise()
		{
			// The ground rises gently north, then a scarp from z = 300 (steep), the fields' head at the origin.
			Func<float, float, float> ground = (x, z) => z < 300f ? z * 0.01f : 3f + (z - 300f) * 0.4f;
			Func<float, float, float> slope = (x, z) => z < 300f ? 0.6f : 22f;
			Assert.That(PointOfInterestLinearFeatures.GalleryHead(Vector2.zero, 2000f, ground, slope, null, out Vector2 mother), Is.True);
			Assert.That(mother.y, Is.InRange(300f, 340f), "at the scarp's foot, where it stands 6 m over the fields");
			Assert.That(ground(mother.x, mother.y), Is.GreaterThanOrEqualTo(PointOfInterestLinearFeatures.GalleryRiseMetres));
			Assert.That(Mathf.Abs(mother.x), Is.LessThan(20f), "the nearest");
			// No scarp: the nearest rise.
			Assert.That(PointOfInterestLinearFeatures.GalleryHead(Vector2.zero, 2000f, (x, z) => z * 0.01f, (x, z) => 0.6f, null, out Vector2 rise), Is.True);
			Assert.That(rise.y, Is.InRange(600f, 620f));
			// Too far: none.
			Assert.That(PointOfInterestLinearFeatures.GalleryHead(Vector2.zero, 200f, ground, slope, null, out _), Is.False);
		}

		[Test]
		public void QanatShafts_FifteenToTwentyMetresApart_ToTheMotherWell()
		{
			List<float> at = PointOfInterestLinearFeatures.ShaftStations(400f, 5);
			Assert.That(at, Is.EqualTo(PointOfInterestLinearFeatures.ShaftStations(400f, 5)));
			Assert.That(at[0], Is.Zero, "the mother well at its head");
			for (int k = 2; k < at.Count; k++)
			{
				Assert.That(at[k] - at[k - 1], Is.InRange(PointOfInterestLinearFeatures.ShaftMinMetres, PointOfInterestLinearFeatures.ShaftMaxMetres));
			}
			Assert.That(at[1], Is.GreaterThan(PointOfInterestLinearFeatures.ShaftMinMetres * 0.5f));
			Assert.That(400f - at[at.Count - 1], Is.InRange(PointOfInterestLinearFeatures.ShaftMinMetres, PointOfInterestLinearFeatures.ShaftMaxMetres), "the first behind the mouth");
			Assert.That(at.Count, Is.InRange(400 / 20, 400 / 15 + 2));
		}

		[Test]
		public void Weir_LiesAcrossTheRiverAtItsNarrowest()
		{
			// A river 20 m wide along x.
			Func<Vector2, bool> wet = p => Mathf.Abs(p.y) < 10f;
			Assert.That(PointOfInterestLinearFeatures.AcrossWater(wet, new Vector2(40f, 2f), 80f, out Vector2 a, out Vector2 b), Is.True);
			Assert.That((b - a).magnitude, Is.EqualTo(20f).Within(1.01f));
			Assert.That(Mathf.Abs(b.x - a.x), Is.LessThan(0.01f), "straight across");
			Assert.That(PointOfInterestLinearFeatures.AcrossWater(wet, new Vector2(40f, 30f), 80f, out _, out _), Is.False, "a dry point");
		}

		[Test]
		public void RouteCell_TheI2fFormsKeepTheirLattice()
		{
			var far = new Vector2(3000f, 0f);
			Assert.That(PointOfInterestLinearFeatures.RouteCell(BiomeLinearForm.EarthDitch, Vector2.zero, far), Is.EqualTo(PointOfInterestLinearFeatures.RouteCellMetres));
			Assert.That(PointOfInterestLinearFeatures.RouteCell(BiomeLinearForm.LinedChannel, Vector2.zero, far), Is.EqualTo(PointOfInterestLinearFeatures.RouteCellMetres));
			Assert.That(PointOfInterestLinearFeatures.RouteCell(BiomeLinearForm.Bisse, Vector2.zero, new Vector2(500f, 0f)), Is.EqualTo(PointOfInterestLinearFeatures.RouteCellMetres));
			float cell = PointOfInterestLinearFeatures.RouteCell(BiomeLinearForm.Bisse, Vector2.zero, far);
			Assert.That(cell, Is.GreaterThan(PointOfInterestLinearFeatures.RouteCellMetres), "coarser over kilometres");
			float extent = 3000f + 2f * PointOfInterestLinearFeatures.RouteMarginMetres;
			Assert.That(Mathf.Pow(extent / cell + 1f, 2f), Is.LessThanOrEqualTo(400000f), "inside the lattice's limit");
			Assert.That(PointOfInterestLinearFeatures.RoggiaPoundMetres(3), Is.InRange(150f, 300f));
		}

		// ── Pieces and wiring ─────────────────────────────────────────

		[Test]
		public void Pieces_EveryNewFormHasItsPieces_AndTheyAreHashed()
		{
			foreach (BiomeLinearForm form in new[] { BiomeLinearForm.Dead, BiomeLinearForm.BrickBarrier, BiomeLinearForm.SnowWall, BiomeLinearForm.HandRope, BiomeLinearForm.PalmRib, BiomeLinearForm.BoughBreak })
			{
				foreach (bool broken in new[] { false, true })
				{
					(string id, int variant) = StructurePieces.LinearPieceFor(form, broken, 0.5f);
					StructurePiece piece = StructurePieces.Find(id);
					Assert.That(piece, Is.Not.Null, $"{form} → {id}");
					Assert.That(variant, Is.InRange(0, piece.Variants - 1));
					Assert.That(piece.ModuleLength, Is.GreaterThan(0f));
					Assert.That(StructurePieces.LinearRolesOf(id, BiomeLinearFeatures.KindOf(form)), Is.Not.Empty);
				}
			}
			foreach (BiomeLinearForm form in (BiomeLinearForm[])Enum.GetValues(typeof(BiomeLinearForm)))
			{
				foreach (string extra in StructurePieces.LinearExtras(form))
				{
					StructurePiece piece = StructurePieces.Find(extra);
					Assert.That(piece, Is.Not.Null, $"{form} → {extra}");
					Assert.That(piece.Tags, Is.EqualTo(new[] { "linear" }), extra);
					Assert.That(piece.Variants, Is.EqualTo(2), extra);
				}
			}
			Assert.That(StructurePieces.LinearPostFor("LinearHandRope", 0), Is.EqualTo(5), "a hand rope's run ends on a stake");
			Assert.That(StructurePieces.PostVariants, Is.EqualTo(6));
			Assert.That(StructurePieces.LinearPostFor("LinearPalmRib", 0), Is.EqualTo(-1), "posts at both its ends");
			Assert.That(StructurePieces.LinearSolid("LinearBrickBarrier"), Is.True);
			Assert.That(StructurePieces.LinearSolid("LinearSnowWall"), Is.True);
			Assert.That(StructurePieces.Find("ChannelBracketFlume").Anchor, Is.EqualTo(StructureAnchor.Deck));
			Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain("StructurePieces.LinearForms.cs"));
			Assert.That(ProceduralArtFamilies.FamiliesOfFile("StructurePieces.LinearForms.cs"), Is.EqualTo(new[] { ArtFamily.Structures }));
		}

		[Test]
		public void Wiring_ScenePassLaysTheNewPlaces_AndNeverASitesSequence()
		{
			string linear = Read(Features + "PointOfInterestLinearFeatures.cs");
			int folds = linear.IndexOf("laid += LayFolds(scope);", StringComparison.Ordinal);
			int wind = linear.IndexOf("laid += LayWindbreaks(scope);", StringComparison.Ordinal);
			int frontier = linear.IndexOf("laid += LayFrontiers(scope, biomes);", StringComparison.Ordinal);
			int channels = linear.IndexOf("laid += LayChannels(scope);", StringComparison.Ordinal);
			Assert.That(folds, Is.GreaterThan(0));
			Assert.That(wind, Is.GreaterThan(folds));
			Assert.That(frontier, Is.GreaterThan(wind));
			Assert.That(channels, Is.GreaterThan(frontier), "the channels last, as before");
			Assert.That(linear.IndexOf("if (!AnyIn(biomes))", StringComparison.Ordinal), Is.LessThan(wind), "behind the fast path");
			foreach (string file in new[] { "PointOfInterestLinearFeatures.Forms.cs", "PointOfInterestLinearFeatures.ChannelForms.cs", "PointOfInterestLinearFeatures.Channels.cs" })
			{
				Assert.That(Read(Features + file).IndexOf("Random.", StringComparison.Ordinal), Is.LessThan(0), $"{file}: never a site's sequence");
			}
			string channelsSource = Read(Features + "PointOfInterestLinearFeatures.Channels.cs");
			Assert.That(channelsSource.IndexOf("laid += LayGreatCanal(scope);", StringComparison.Ordinal), Is.GreaterThan(0), "the scene's one canal first");
		}
	}
}
