using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Decisions 23 and 29 of the biome suite (2026-10-18, phase I2f): one linear-feature system for every biome (hedges, walls,
	/// fences, rows of trees, water channels, clearance rows) laid round fields, along lanes, along woods' edges, round small sites,
	/// as folds and along terraces. Every default is exactly what was cut before: no biome names a style, so fields keep the kit's
	/// fence and nothing else is laid.
	/// </summary>
	/// <remarks>
	/// Pure but for the last region (Native): those build meshes (Quaternion, Matrix4x4) and run in Unity or under nativerun.
	/// </remarks>
	public class BiomeLinearFeatureTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string StageSource = World + "/PointsOfInterest/PointOfInterestStage.cs";
		private const string FieldsSource = World + "/PointsOfInterest/Features/PointOfInterestFields.cs";
		private const string LinearSource = World + "/PointsOfInterest/Features/PointOfInterestLinearFeatures.cs";
		private const string AuthoringSource = World + "/ProceduralArt/BiomeArtAuthoring.cs";
		private const string ReaderSource = World + "/Terrain/BiomeTemplateArt.cs";
		private const string StructuresGeneratorSource = World + "/ProceduralArt/BiomeArtGenerator.Structures.cs";
		private const string PiecesSource = World + "/ProceduralArt/StructurePieces.cs";
		private const string TemplateSource = "Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs";

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		// ── Defaults: nothing changes ─────────────────────────────────

		[Test]
		public void Defaults_NoBiomeNamesALinearFeature()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.LinearLines(), Is.Empty, $"{entry.Biome} names no linear feature yet");
				var missing = new List<string>();
				Assert.That(entry.LinearFeatures(_ => null, missing), Is.Empty, entry.Biome);
				Assert.That(missing, Is.Empty, entry.Biome);
				Assert.That(StructurePieces.LinearPalettes(entry.LinearFeatures(null)), Is.Empty, entry.Biome);
			}
			int templates = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				templates++;
				Assert.That(Regex.IsMatch(text, @"^  linearFeatures:\s*\n  - ", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} lays a linear feature");
			}
			Assert.That(templates, Is.EqualTo(83));
			Assert.That(BiomeTemplateArt.LinearFeatures(null), Is.Empty);
			Assert.That(PointOfInterestLinearFeatures.AnyIn(Array.Empty<BiomeTemplate>()), Is.False);
			Assert.That(BiomeLinearFeatures.Any(BiomeLinearFeatures.None()), Is.False);
			StringAssert.Contains("private List<BiomeLinearStyle> linearFeatures = new List<BiomeLinearStyle>();", Read(TemplateSource));
		}

		[Test]
		public void Values_AreAppendOnly_AndFormsBelongToTheirKind()
		{
			Assert.That((int)BiomeLinearKind.Hedge, Is.EqualTo(0));
			Assert.That((int)BiomeLinearKind.ClearanceRow, Is.EqualTo(5));
			Assert.That((int)BiomeLinearPlace.FieldEdges, Is.EqualTo(0));
			Assert.That((int)BiomeLinearPlace.TerraceEdges, Is.EqualTo(5));
			Assert.That((int)BiomeLinearForm.Drystone, Is.EqualTo(10));
			Assert.That((int)BiomeLinearForm.Palisade, Is.EqualTo(25));
			Assert.That((int)BiomeLinearForm.Piantata, Is.EqualTo(32));
			Assert.That((int)BiomeLinearForm.LinedChannel, Is.EqualTo(41));
			Assert.That((int)BiomeLinearForm.Turfed, Is.EqualTo(51));
			foreach (BiomeLinearKind kind in (BiomeLinearKind[])Enum.GetValues(typeof(BiomeLinearKind)))
			{
				List<BiomeLinearForm> forms = BiomeLinearFeatures.FormsOf(kind);
				Assert.That(forms, Is.Not.Empty, $"{kind} has forms");
				foreach (BiomeLinearForm form in forms)
				{
					Assert.That(BiomeLinearFeatures.KindOf(form), Is.EqualTo(kind));
					Assert.That(BiomeLinearFeatures.DefaultHeight(form), Is.GreaterThan(0f), $"{form} height");
				}
			}
			Assert.That(BiomeLinearFeatures.FormsOf(BiomeLinearKind.Fence), Is.EqualTo(new[]
			{
				BiomeLinearForm.Rail, BiomeLinearForm.LeaningPole, BiomeLinearForm.Wattle, BiomeLinearForm.Picket, BiomeLinearForm.Slab, BiomeLinearForm.Palisade,
			}));
		}

		[Test]
		public void Styles_PlacesTheyAllow_TheirIdentity_AndTheirSignature()
		{
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.Channel, BiomeLinearPlace.FieldEdges), Is.True);
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.Channel, BiomeLinearPlace.Lanes), Is.True);
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.Channel, BiomeLinearPlace.ClearingEdges), Is.False);
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.Channel, BiomeLinearPlace.Folds), Is.False);
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.TreeRow, BiomeLinearPlace.Folds), Is.False);
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.Wall, BiomeLinearPlace.Folds), Is.True);
			Assert.That(BiomeLinearFeatures.Allows(BiomeLinearKind.Hedge, BiomeLinearPlace.ClearingEdges), Is.True);

			var hedge = new BiomeLinearStyle(BiomeLinearForm.Laid, BiomeLinearPlace.FieldEdges);
			Assert.That(hedge.Kind, Is.EqualTo(BiomeLinearKind.Hedge));
			Assert.That(BiomeLinearFeatures.IsLaid(hedge), Is.True);
			Assert.That(BiomeLinearFeatures.Id(hedge), Is.EqualTo("Hedge/Laid/FieldEdges"));
			BiomeLinearStyle wrong = hedge;
			wrong.Kind = BiomeLinearKind.Wall;
			Assert.That(BiomeLinearFeatures.IsLaid(wrong), Is.False, "a form not its kind's");
			BiomeLinearStyle none = hedge;
			none.Density = 0f;
			Assert.That(BiomeLinearFeatures.IsLaid(none), Is.False, "density 0 lays nothing");
			Assert.That(BiomeLinearFeatures.Any(new List<BiomeLinearStyle> { none }), Is.False);
			Assert.That(BiomeLinearFeatures.Any(new List<BiomeLinearStyle> { none, hedge }), Is.True);
			Assert.That(BiomeLinearFeatures.Any(new List<BiomeLinearStyle> { hedge }, BiomeLinearPlace.Lanes), Is.False);

			// The signature moves with every value the authoring marks.
			string printed = hedge.ToString();
			foreach (Func<BiomeLinearStyle, BiomeLinearStyle> edit in new Func<BiomeLinearStyle, BiomeLinearStyle>[]
			{
				s => { s.Density = 0.5f; return s; }, s => { s.Height = 2f; return s; }, s => { s.Spacing = 3f; return s; }, s => { s.GateWidth = 4f; return s; },
				s => { s.Breaks = 0.2f; return s; }, s => { s.Sides = BiomeLinearSides.One; return s; }, s => { s.Ways = BiomeLinearWays.Roads; return s; },
				s => { s.Reach = 100f; return s; }, s => { s.Vine = "Ivy"; return s; }, s => { s.OwnMaterial = true; return s; },
			})
			{
				Assert.That(edit(hedge).ToString(), Is.Not.EqualTo(printed));
			}
			Assert.That(BiomeLinearFeatures.HeightOf(hedge), Is.EqualTo(BiomeLinearFeatures.DefaultHeight(BiomeLinearForm.Laid)));
			Assert.That(BiomeLinearFeatures.GateOf(new BiomeLinearStyle(BiomeLinearForm.Filare, BiomeLinearPlace.Lanes)), Is.EqualTo(0f), "a row has no gate");
		}

		[Test]
		public void Ways_ALaneStyleFollows()
		{
			Assert.That(BiomeLinearFeatures.Follows(BiomeLinearWays.All, ScenePathClass.CartTrack), Is.True);
			Assert.That(BiomeLinearFeatures.Follows(BiomeLinearWays.All, ScenePathClass.Street), Is.False);
			Assert.That(BiomeLinearFeatures.Follows(BiomeLinearWays.All, ScenePathClass.Yard), Is.False);
			Assert.That(BiomeLinearFeatures.Follows(BiomeLinearWays.Tracks, ScenePathClass.CartTrack), Is.True);
			Assert.That(BiomeLinearFeatures.Follows(BiomeLinearWays.Tracks, ScenePathClass.Road), Is.False);
			Assert.That(BiomeLinearFeatures.Follows(BiomeLinearWays.Roads, ScenePathClass.Highway), Is.True);
			Assert.That(BiomeLinearFeatures.Follows(BiomeLinearWays.Roads, ScenePathClass.AncientRoad), Is.True);
			Assert.That(BiomeLinearFeatures.Follows(BiomeLinearWays.Paths, ScenePathClass.Trail), Is.True);
			Assert.That(BiomeLinearFeatures.Follows(BiomeLinearWays.Paths | BiomeLinearWays.Tracks, ScenePathClass.Footpath), Is.True);
		}

		// ── Lines ─────────────────────────────────────────────────────

		[Test]
		public void Stations_AreEven_CoverTheLine_AndKeepBothEnds()
		{
			var line = new List<Vector2> { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 7f) };
			Assert.That(PointOfInterestLinearFeatures.Length(line), Is.EqualTo(17f).Within(1e-4f));
			List<PointOfInterestLinearFeatures.Station> st = PointOfInterestLinearFeatures.Stations(line, 3f);
			Assert.That(st.Count, Is.EqualTo(7), "ceil(17 / 3) = 6 intervals");
			Assert.That(st[0].At, Is.EqualTo(line[0]));
			Assert.That((st[st.Count - 1].At - line[2]).magnitude, Is.LessThan(1e-4f));
			for (int k = 1; k < st.Count; k++)
			{
				Assert.That(st[k].Along - st[k - 1].Along, Is.EqualTo(17f / 6f).Within(1e-4f));
				Assert.That(st[k].Along - st[k - 1].Along, Is.LessThanOrEqualTo(3f));
			}
			// The point at a distance lies on the line.
			float[] along = PointOfInterestLinearFeatures.Along(line);
			Assert.That((PointOfInterestLinearFeatures.PointAt(line, along, 13f) - new Vector2(10f, 3f)).magnitude, Is.LessThan(1e-4f));
			Assert.That(PointOfInterestLinearFeatures.Stations(new List<Vector2> { Vector2.zero, Vector2.zero }, 3f), Is.Empty, "no length, no stations");
			Assert.That(PointOfInterestLinearFeatures.Stations(line, 100f).Count, Is.EqualTo(2), "one interval at least");
		}

		[Test]
		public void Offset_KeepsItsDistance_LeftPositive_AndMitresBends()
		{
			var line = new List<Vector2> { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 10f) };
			List<Vector2> left = PointOfInterestLinearFeatures.Offset(line, 2f);
			Assert.That((left[0] - new Vector2(0f, 2f)).magnitude, Is.LessThan(1e-4f), "left of an eastward line is north");
			Assert.That((left[2] - new Vector2(8f, 10f)).magnitude, Is.LessThan(1e-4f));
			// At the bend the mitre keeps both segments two metres away.
			Assert.That((left[1] - new Vector2(8f, 2f)).magnitude, Is.LessThan(1e-3f));
			List<Vector2> right = PointOfInterestLinearFeatures.Offset(line, -2f);
			Assert.That((right[0] - new Vector2(0f, -2f)).magnitude, Is.LessThan(1e-4f));
		}

		[Test]
		public void Kept_LeavesTheGate_TheBlocked_AndItsBreaks_TheSameEveryTime()
		{
			bool[] all = PointOfInterestLinearFeatures.Kept(9, null, -1, 7, 0f);
			Assert.That(Array.TrueForAll(all, k => k), Is.True);
			bool[] gated = PointOfInterestLinearFeatures.Kept(9, null, PointOfInterestLinearFeatures.GateModule(9), 7, 0f);
			Assert.That(gated[4], Is.False, "the middle module is the gate's gap");
			Assert.That(Array.FindAll(gated, k => !k).Length, Is.EqualTo(1));
			bool[] blocked = PointOfInterestLinearFeatures.Kept(9, k => k == 2 || k == 3, -1, 7, 0f);
			Assert.That(blocked[2] || blocked[3], Is.False);
			bool[] broken = PointOfInterestLinearFeatures.Kept(4000, null, -1, 11, 0.3f);
			int gaps = Array.FindAll(broken, k => !k).Length;
			Assert.That(gaps / 4000f, Is.EqualTo(0.3f).Within(0.03f), "a share of breaks");
			Assert.That(PointOfInterestLinearFeatures.Kept(4000, null, -1, 11, 0.3f), Is.EqualTo(broken), "deterministic");
			Assert.That(PointOfInterestLinearFeatures.Kept(4000, null, -1, 12, 0.3f), Is.Not.EqualTo(broken), "its own seed");
			Assert.That(PointOfInterestLinearFeatures.GateModule(1), Is.EqualTo(0));
			Assert.That(PointOfInterestLinearFeatures.GateModule(0), Is.EqualTo(-1));
		}

		[Test]
		public void Runs_SplitWhereNotAllowed_AndDropTheShort()
		{
			var line = new List<Vector2>();
			for (int i = 0; i <= 50; i++)
			{
				line.Add(new Vector2(i, 0f));
			}
			// Blocked from 20 to 24 and from 45 on: runs 0..19 (19 m) and 25..44 (19 m); the 0..4-metre pieces are dropped at 12 m.
			List<List<Vector2>> runs = PointOfInterestLinearFeatures.Runs(line, p => !(p.x >= 20f && p.x <= 24f) && p.x < 45f, 12f);
			Assert.That(runs.Count, Is.EqualTo(2));
			Assert.That(runs[0][0].x, Is.EqualTo(0f));
			Assert.That(runs[0][runs[0].Count - 1].x, Is.EqualTo(19f));
			Assert.That(runs[1][0].x, Is.EqualTo(25f));
			Assert.That(PointOfInterestLinearFeatures.Runs(line, p => p.x < 10f, 12f), Is.Empty, "a run of 9 m is dropped");
		}

		// ── Field edges, bounds and woods' edges ──────────────────────

		[Test]
		public void FieldEdges_AreTheFieldsSides_TheGateTowardTheSettlement_AsTheKitFence()
		{
			var centre = new Vector2(100f, 50f);
			var size = new Vector2(40f, 24f);
			foreach (float yaw in new[] { 0f, 30f, 90f, 217f })
			{
				foreach (Vector2 site in new[] { new Vector2(100f, 200f), new Vector2(-50f, 50f), new Vector2(300f, -40f) })
				{
					PointOfInterestLinearFeatures.Side[] sides = PointOfInterestLinearFeatures.RectangleSides(centre, yaw, size, site);
					Assert.That(sides.Length, Is.EqualTo(4));
					Assert.That((sides[0].To - sides[0].From).magnitude, Is.EqualTo(size.x).Within(1e-3f));
					Assert.That((sides[2].To - sides[2].From).magnitude, Is.EqualTo(size.y).Within(1e-3f));
					Assert.That(Array.FindAll(sides, s => s.Gate).Length, Is.EqualTo(1), "one gate");
					// The kit fence's rule (PointOfInterestFields.Fence), written out: the side whose outward normal faces the site most.
					float r = yaw * Mathf.Deg2Rad;
					var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
					var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
					Vector2 toSite = (site - centre).normalized;
					Vector2[] normals = { axisZ, -axisZ, axisX, -axisX };
					int expect = 0;
					for (int s = 1; s < 4; s++)
					{
						if (Vector2.Dot(normals[s], toSite) > Vector2.Dot(normals[expect], toSite))
						{
							expect = s;
						}
					}
					Assert.That(sides[expect].Gate, Is.True, $"yaw {yaw}, site {site}");
					// Every side's middle is the field's edge, its outward normal pointing away from the centre.
					foreach (PointOfInterestLinearFeatures.Side side in sides)
					{
						Vector2 mid = 0.5f * (side.From + side.To);
						Assert.That(PointOfInterestPaddies.Inside(centre, yaw, size, mid.x, mid.y), Is.EqualTo(0f).Within(1e-3f));
						Assert.That(Vector2.Dot(mid - centre, side.Outward), Is.GreaterThan(0f));
					}
				}
			}
			PointOfInterestLinearFeatures.Side[] out1 = PointOfInterestLinearFeatures.RectangleSides(centre, 0f, size, Vector2.zero, 0.8f);
			Vector2 m = 0.5f * (out1[0].From + out1[0].To);
			Assert.That(PointOfInterestPaddies.Inside(centre, 0f, size, m.x, m.y), Is.EqualTo(-0.8f).Within(1e-3f), "moved out by the outset");
			Assert.That(PointOfInterestLinearFeatures.OutsetOf(BiomeLinearKind.Fence), Is.EqualTo(0f), "a fence stands on the line, as the kit's did");
		}

		[Test]
		public void SiteBounds_RingTheOutline_Closed()
		{
			var centre = new Vector2(10f, 20f);
			List<Vector2> ring = PointOfInterestLinearFeatures.Bounds(centre, Array.Empty<float>(), 30f, 4f);
			Assert.That(ring[0], Is.EqualTo(ring[ring.Count - 1]), "closed");
			foreach (Vector2 p in ring)
			{
				Assert.That((p - centre).magnitude, Is.EqualTo(34f).Within(1e-3f), "a round site's ring at its radius and the margin");
			}
		}

		[Test]
		public void WoodEdges_ContoursFollowTheLevel_Deterministically()
		{
			var c = new Vector2(103f, 97f);
			Func<float, float, float> field = (x, z) => 40f - (new Vector2(x, z) - c).magnitude;
			var area = Rect.MinMaxRect(0f, 0f, 200f, 200f);
			List<List<Vector2>> lines = PointOfInterestLinearFeatures.Contours(field, area, 6f);
			Assert.That(lines.Count, Is.EqualTo(1), "one closed ring");
			List<Vector2> ring = lines[0];
			Assert.That(ring[0], Is.EqualTo(ring[ring.Count - 1]), "closed back on itself");
			Assert.That(ring.Count, Is.GreaterThan(30));
			foreach (Vector2 p in ring)
			{
				Assert.That((p - c).magnitude, Is.EqualTo(40f).Within(0.6f), "on the level line");
			}
			List<List<Vector2>> again = PointOfInterestLinearFeatures.Contours(field, area, 6f);
			Assert.That(again[0], Is.EqualTo(ring), "deterministic");
			// A level line running off the area is an open chain from edge to edge.
			List<List<Vector2>> open = PointOfInterestLinearFeatures.Contours((x, z) => x - 50.5f, area, 6f);
			Assert.That(open.Count, Is.EqualTo(1));
			Assert.That(open[0][0], Is.Not.EqualTo(open[0][open[0].Count - 1]));
			foreach (Vector2 p in open[0])
			{
				Assert.That(p.x, Is.EqualTo(50.5f).Within(1e-3f));
			}
		}

		// ── Channels ──────────────────────────────────────────────────

		[Test]
		public void ChannelIds_LieInThePaddiesRange_AndNoPaddysIdIsOne()
		{
			int id = PointOfInterestLinearFeatures.ChannelLakeId(5, 3);
			Assert.That(PointOfInterestPaddies.IsPaddy(id), Is.True, "cleared by the place stage and left out of the cut's water as a paddy's");
			Assert.That(PointOfInterestLinearFeatures.IsChannel(id), Is.True);
			Assert.That(PointOfInterestLinearFeatures.ChannelLakeId(0x7FFF, 0xFFF) < CaveShaper.PoolIdBase, Is.True);
			Assert.That(PointOfInterestLinearFeatures.ChannelLakeId(5, 4), Is.Not.EqualTo(id));
			Assert.That(PointOfInterestLinearFeatures.ChannelLakeId(6, 3), Is.Not.EqualTo(id));
			foreach (int site in new[] { 0, 1, 0x12345678, int.MaxValue, -1 })
			{
				for (int field = 0; field < 16; field++)
				{
					Assert.That(PointOfInterestLinearFeatures.IsChannel(PointOfInterestPaddies.LakeId(site, field)), Is.False);
				}
			}
			Assert.That(PointOfInterestLinearFeatures.IsChannel(MoatShaper.LakeIdBase | 3) || PointOfInterestLinearFeatures.IsChannel(CaveShaper.PoolIdBase | 3), Is.False);
		}

		private static float Hill(float x, float z)
		{
			// A slope rising east, a round hill standing on it.
			float d2 = (x - 100f) * (x - 100f) + (z - 150f) * (z - 150f);
			return 0.1f * x + 12f * Mathf.Exp(-d2 / (35f * 35f));
		}

		[Test]
		public void Route_KeepsToTheLevel_AroundAHill_AndIsDeterministic()
		{
			var from = new Vector2(100f, 40f);
			var to = new Vector2(100f, 260f);
			Assert.That(PointOfInterestLinearFeatures.Route(from, to, Hill(from.x, from.y), Hill(to.x, to.y), Hill, null, 4f, 100f, out List<Vector2> route), Is.True);
			Assert.That(route[0], Is.EqualTo(from));
			Assert.That(route[route.Count - 1], Is.EqualTo(to));
			float worst = 0f, straightWorst = 0f;
			foreach (Vector2 p in route)
			{
				worst = Mathf.Max(worst, Mathf.Abs(Hill(p.x, p.y) - 10f));
			}
			for (int k = 0; k <= 100; k++)
			{
				Vector2 p = Vector2.Lerp(from, to, k / 100f);
				straightWorst = Mathf.Max(straightWorst, Mathf.Abs(Hill(p.x, p.y) - 10f));
			}
			Assert.That(straightWorst, Is.GreaterThan(10f), "the straight line climbs over the hill");
			Assert.That(worst, Is.LessThan(2.5f), "the route keeps to the contour round it");
			Assert.That(PointOfInterestLinearFeatures.Route(from, to, Hill(from.x, from.y), Hill(to.x, to.y), Hill, null, 4f, 100f, out List<Vector2> again), Is.True);
			Assert.That(again, Is.EqualTo(route), "deterministic");
			// A blocked band it cannot cross fails; one with a gap goes through the gap.
			Func<Vector2, bool> wall = p => p.y > 140f && p.y < 160f;
			Assert.That(PointOfInterestLinearFeatures.Route(from, to, 10f, 10f, (x, z) => 10f, wall, 4f, 60f, out _), Is.False);
			Func<Vector2, bool> gapped = p => p.y > 140f && p.y < 160f && Mathf.Abs(p.x - 140f) > 6f;
			Assert.That(PointOfInterestLinearFeatures.Route(from, to, 10f, 10f, (x, z) => 10f, gapped, 4f, 60f, out List<Vector2> through), Is.True);
			foreach (Vector2 p in through)
			{
				Assert.That(gapped(p) && (p - from).magnitude > 5f && (p - to).magnitude > 5f, Is.False, $"{p} is in the blocked band");
			}
		}

		[Test]
		public void Pounds_FallInSteps_NeverRise_AndBreakAtCulvertsAndFlumes()
		{
			var line = new List<Vector2> { new Vector2(0f, 0f), new Vector2(600f, 0f) };
			Func<float, float, float> plain = (x, z) => 12f - 0.002f * x;
			List<PointOfInterestLinearFeatures.ChannelStation> st = PointOfInterestLinearFeatures.Pounds(line, plain, true, 11.6f, 10.2f);
			Assert.That(st.Count, Is.EqualTo(301));
			float poundStart = 0f;
			for (int i = 1; i < st.Count; i++)
			{
				Assert.That(st[i].Level, Is.LessThanOrEqualTo(st[i - 1].Level + 1e-5f), "water never rises along a leat");
				if (st[i].Pound != st[i - 1].Pound)
				{
					Assert.That(st[i].Pound, Is.EqualTo(st[i - 1].Pound + 1));
					Assert.That(st[i - 1].Level - st[i].Level, Is.LessThanOrEqualTo(PointOfInterestLinearFeatures.MaxStep + 0.01f), "a step no deeper than a sluice holds");
					poundStart = st[i].Along;
				}
				else
				{
					Assert.That(st[i].Level, Is.EqualTo(st[i - 1].Level), "a pound is level");
					Assert.That(st[i].Along - poundStart, Is.LessThanOrEqualTo(PointOfInterestLinearFeatures.PoundMetres + 1e-3f));
				}
				Assert.That(st[i].Part, Is.EqualTo(PointOfInterestLinearFeatures.ChannelPart.Cut));
			}
			Assert.That(st[st.Count - 1].Pound, Is.GreaterThanOrEqualTo(4));
			Assert.That(st[st.Count - 1].Level, Is.EqualTo(10.2f).Within(PointOfInterestLinearFeatures.MaxStep), "it reaches the head's level");

			// A road across it (stations 100–110 m): a culvert, and the cut after it a fresh pound.
			List<PointOfInterestLinearFeatures.ChannelStation> roaded = PointOfInterestLinearFeatures.Pounds(line, plain, true, 11.6f, 10.2f, p => p.x >= 100f && p.x <= 110f);
			Assert.That(roaded[52].Part, Is.EqualTo(PointOfInterestLinearFeatures.ChannelPart.Culvert));
			Assert.That(roaded[56].Pound, Is.GreaterThan(roaded[49].Pound));
			// Ground falling away beside it past what a bank holds: a flume.
			List<PointOfInterestLinearFeatures.ChannelStation> flumed = PointOfInterestLinearFeatures.Pounds(line, plain, true, 11.6f, 10.2f, null,
				(p, dir) => p.x >= 300f && p.x <= 320f ? plain(p.x, p.y) - 2f : plain(p.x, p.y));
			Assert.That(flumed[155].Part, Is.EqualTo(PointOfInterestLinearFeatures.ChannelPart.Flume));
			Assert.That(flumed[140].Part, Is.EqualTo(PointOfInterestLinearFeatures.ChannelPart.Cut));
			Assert.That(flumed[165].Pound, Is.GreaterThan(flumed[145].Pound), "a pound ends at a flume");

			// A standing ditch keeps its water under its ground, whichever way the ground goes.
			Func<float, float, float> rolling = (x, z) => 10f + 2f * Mathf.Sin(x / 60f);
			foreach (PointOfInterestLinearFeatures.ChannelStation s in PointOfInterestLinearFeatures.Pounds(line, rolling, false, 0f, 0f))
			{
				Assert.That(s.Level, Is.LessThan(s.Ground - 0.05f), $"at {s.Along}");
				Assert.That(s.Level, Is.GreaterThan(s.Ground - 0.9f), $"at {s.Along}");
			}
		}

		[Test]
		public void ChannelGround_DigsTheTrench_RaisesOnlyTheBanks_AndIsContinuous()
		{
			const float level = 10f, half = 0.7f, depth = 0.45f;
			float bank = level + PointOfInterestLinearFeatures.ChannelFreeboard;
			Assert.That(PointOfInterestLinearFeatures.ChannelGround(0f, 10.4f, level, half, depth), Is.EqualTo(bank - depth).Within(1e-4f), "the floor");
			Assert.That(PointOfInterestLinearFeatures.ChannelGround(0f, 9.5f, level, half, depth), Is.EqualTo(bank - depth).Within(1e-4f), "a low bed built up to its floor");
			Assert.That(PointOfInterestLinearFeatures.ChannelGround(1.5f, 9.5f, level, half, depth), Is.EqualTo(bank), "the bank built up");
			Assert.That(PointOfInterestLinearFeatures.ChannelGround(1.5f, 10.5f, level, half, depth), Is.EqualTo(10.5f), "a bank never lowers the ground");
			Assert.That(PointOfInterestLinearFeatures.ChannelGround(1.5f, 12f, level, half, depth), Is.EqualTo(bank + 0.8f).Within(1e-4f), "a deep cut's side at one in one");
			Assert.That(PointOfInterestLinearFeatures.ChannelGround(10f, 9f, level, half, depth), Is.EqualTo(9f), "beyond, the ground as it was");
			foreach (float g in new[] { 9.6f, 10.1f, 10.5f, 11.2f })
			{
				float previous = PointOfInterestLinearFeatures.ChannelGround(0f, g, level, half, depth);
				for (float d = 0.01f; d < 6f; d += 0.01f)
				{
					float at = PointOfInterestLinearFeatures.ChannelGround(d, g, level, half, depth);
					Assert.That(Mathf.Abs(at - previous), Is.LessThan(0.05f), $"no step at {d} on ground {g}");
					previous = at;
				}
			}
		}

		[Test]
		public void PoundWater_IsHiddenUnderTheBanks_WhereverTheRenderersSheetReaches()
		{
			// A leat across ground falling 6 % across its line (a hillside), carved as the cut carves it.
			var line = new List<Vector2> { new Vector2(0.3f, 0.2f), new Vector2(80.3f, 12.2f) };
			Func<float, float, float> ground = (x, z) => 20f + 0.06f * z - 0.0025f * x;
			List<PointOfInterestLinearFeatures.ChannelStation> st = PointOfInterestLinearFeatures.Pounds(line, ground, true, ground(0.3f, 0.2f) - 0.3f, ground(80.3f, 12.2f) - 0.3f);
			const float half = 0.7f, depth = 0.45f;
			Dictionary<long, List<int>> grid = PointOfInterestLinearFeatures.CutGrid(st, 6f, 6f);
			float Carved(float x, float z)
			{
				float g = ground(x, z);
				float d = PointOfInterestLinearFeatures.NearestCut(st, grid, 6f, new Vector2(x, z), out int k);
				return k < 0 || d > half + PointOfInterestLinearFeatures.BankMetres + PointOfInterestLinearFeatures.BankFallMetres ? g
					: PointOfInterestLinearFeatures.ChannelGround(d, g, st[k].Level, half, depth);
			}
			int pounds = st[st.Count - 1].Pound + 1, cells = 0;
			for (int p = 0; p < pounds; p++)
			{
				SceneHydrology.Lake lake = PointOfInterestLinearFeatures.PoundLake(PointOfInterestLinearFeatures.ChannelLakeId(0, p), st, p, half);
				Assert.That(lake, Is.Not.Null, $"pound {p} holds water");
				Assert.That(lake.MaskCell, Is.EqualTo(1f));
				Assert.That(lake.Level, Is.EqualTo(st.Find(s => s.Pound == p).Level));
				// The renderer draws the mask grown by a cell (InlandWaterRenderer.LakeMesh): wherever that sheet lies outside the trench,
				// the carved ground stands over the water.
				for (int z = -1; z <= lake.MaskHeight; z++)
				{
					for (int x = -1; x <= lake.MaskWidth; x++)
					{
						if (!Covered(lake, x, z))
						{
							continue;
						}
						cells++;
						for (int c = 0; c < 4; c++)
						{
							float px = lake.MaskOrigin.x + (x + (c & 1)) * lake.MaskCell, pz = lake.MaskOrigin.y + (z + (c >> 1)) * lake.MaskCell;
							float d = PointOfInterestLinearFeatures.NearestCut(st, grid, 6f, new Vector2(px, pz), out _);
							if (d > half)
							{
								Assert.That(Carved(px, pz), Is.GreaterThanOrEqualTo(lake.Level - 1e-4f), $"pound {p}: the sheet shows over the ground at ({px}, {pz})");
							}
						}
					}
				}
			}
			Assert.That(cells, Is.GreaterThan(100));
		}

		private static bool Covered(SceneHydrology.Lake lake, int x, int z)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int nx = x + dx, nz = z + dz;
					if (nx < 0 || nz < 0 || nx >= lake.MaskWidth || nz >= lake.MaskHeight)
					{
						continue;
					}
					int bit = nz * lake.MaskWidth + nx;
					if ((lake.Mask[bit >> 3] & (1 << (bit & 7))) != 0)
					{
						return true;
					}
				}
			}
			return false;
		}

		[Test]
		public void Channel_IsTheSameFromTheSameGround()
		{
			var line = new List<Vector2> { new Vector2(0f, 0f), new Vector2(40f, 30f), new Vector2(90f, 35f) };
			Func<float, float, float> ground = (x, z) => 15f + 0.03f * z - 0.004f * x + 0.3f * Mathf.Sin(x * 0.1f);
			List<PointOfInterestLinearFeatures.ChannelStation> a = PointOfInterestLinearFeatures.Pounds(line, ground, true, 14.5f, 14f);
			List<PointOfInterestLinearFeatures.ChannelStation> b = PointOfInterestLinearFeatures.Pounds(line, ground, true, 14.5f, 14f);
			Assert.That(b.Count, Is.EqualTo(a.Count));
			for (int i = 0; i < a.Count; i++)
			{
				Assert.That(b[i].Level, Is.EqualTo(a[i].Level));
				Assert.That(b[i].Pound, Is.EqualTo(a[i].Pound));
				Assert.That(b[i].At, Is.EqualTo(a[i].At));
			}
			SceneHydrology.Lake la = PointOfInterestLinearFeatures.PoundLake(1, a, 0, 0.7f), lb = PointOfInterestLinearFeatures.PoundLake(1, b, 0, 0.7f);
			Assert.That(lb.Mask, Is.EqualTo(la.Mask));
			Assert.That(lb.MaskOrigin, Is.EqualTo(la.MaskOrigin));
		}

		// ── Pieces, palettes, the spec ────────────────────────────────

		private static readonly string[] LinearPieces =
		{
			"LinearHedge", "LinearDrystone", "LinearMortared", "LinearEarthBank", "LinearSlabs", "LinearPoleFence", "LinearWattle", "LinearPickets",
			"LinearSlabFence", "LinearGate", "LinearClearance", "ChannelSluice", "ChannelFlume", "ChannelBridge",
		};

		[Test]
		public void Pieces_AreLast_TaggedLinear_Hashed_AndNoTemplateQueriesThem()
		{
			IReadOnlyList<StructurePiece> all = StructurePieces.All;
			for (int i = 0; i < LinearPieces.Length; i++)
			{
				StructurePiece p = all[all.Count - LinearPieces.Length + i];
				Assert.That(p.Id, Is.EqualTo(LinearPieces[i]), "registered last, in order");
				Assert.That(p.Tags, Is.EqualTo(new[] { "linear" }));
				Assert.That(p.Collides, Is.True, $"{p.Id} collides");
			}
			Assert.That(Array.IndexOf(StructurePieces.Tags, "linear"), Is.GreaterThanOrEqualTo(0));
			Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain("StructurePieces.Linear.cs"));
			Assert.That(ProceduralArtFamilies.FamiliesOfFile("StructurePieces.Linear.cs"), Is.EqualTo(new[] { ArtFamily.Structures }));
			string pieces = Read(PiecesSource);
			Assert.That(pieces.IndexOf("AddLinear(list);", StringComparison.Ordinal), Is.GreaterThan(pieces.IndexOf("AddBoardwalk(list);", StringComparison.Ordinal)));
			foreach (string path in Directory.GetFiles("Assets/Templates/World/PointsOfInterest", "*.asset", SearchOption.AllDirectories))
			{
				string text = File.ReadAllText(path);
				Assert.That(Regex.IsMatch(text, @"\blinear\b"), Is.False, $"{path} queries the linear pieces");
			}
		}

		[Test]
		public void EveryModuleForm_HasAPiece_ItsHeight_AndItsRoles()
		{
			foreach (BiomeLinearForm form in (BiomeLinearForm[])Enum.GetValues(typeof(BiomeLinearForm)))
			{
				BiomeLinearKind kind = BiomeLinearFeatures.KindOf(form);
				foreach (bool broken in new[] { false, true })
				{
					foreach (float roll in new[] { 0f, 0.4f, 0.9f })
					{
						(string id, int variant) = StructurePieces.LinearPieceFor(form, broken, roll);
						if (kind == BiomeLinearKind.TreeRow || kind == BiomeLinearKind.Channel)
						{
							Assert.That(id, Is.Null, $"{form} is no module");
							continue;
						}
						StructurePiece piece = StructurePieces.Find(id);
						Assert.That(piece, Is.Not.Null, $"{form} → {id}");
						Assert.That(variant, Is.InRange(0, piece.Variants - 1));
						Assert.That(piece.ModuleLength, Is.GreaterThan(0f), $"{id} is laid in modules");
						Assert.That(StructurePieces.LinearBuiltHeight(id), Is.GreaterThan(0.5f), id);
						Assert.That(StructurePieces.LinearRolesOf(id, kind), Is.Not.Empty, id);
					}
				}
			}
			Assert.That(StructurePieces.LinearPieceFor(BiomeLinearForm.Rail, false, 0f).Id, Is.EqualTo("FieldFence"), "the kit's own fence for rails");
			Assert.That(StructurePieces.LinearPieceFor(BiomeLinearForm.Palisade, false, 0f).Id, Is.EqualTo("PalisadeSegment"));
		}

		[Test]
		public void Palette_IsTheBiomesUnlessTheStyleNamesItsOwn()
		{
			BiomeStructurePalette kit = BiomeStructurePalette.Kit();
			var wall = new BiomeLinearStyle(BiomeLinearForm.Mortared, BiomeLinearPlace.SiteBounds);
			Assert.That(StructurePieces.LinearPalette(kit, wall, "LinearMortared"), Is.SameAs(kit), "the biome's own");
			Assert.That(StructurePieces.LinearPalettes(new[] { wall }), Is.Empty);
			wall.OwnMaterial = true;
			wall.Material = BiomeStructureMaterial.Sandstone;
			wall.Tint = new Color(0.8f, 0.4f, 0.3f, 1f);
			BiomeStructurePalette own = StructurePieces.LinearPalette(kit, wall, "LinearMortared");
			Assert.That(own, Is.Not.SameAs(kit));
			BiomeStructureSurface s = own.For(BiomeStructureStyle.Stone, BiomeStructureRole.Fence);
			Assert.That(s.Material, Is.EqualTo(BiomeStructureMaterial.Sandstone));
			Assert.That(s.Tint, Is.EqualTo(wall.Tint));
			Assert.That(own.For(BiomeStructureStyle.Stone, BiomeStructureRole.Wall).Material, Is.EqualTo(kit.For(BiomeStructureStyle.Stone, BiomeStructureRole.Wall).Material), "nothing else moves");
			Assert.That(kit.For(BiomeStructureStyle.Stone, BiomeStructureRole.Fence).Material, Is.EqualTo(BiomeStructureMaterial.Stone), "the biome's palette untouched");
			// A wall's gate takes the wall's stone on its piers, never on its timber leaf.
			BiomeStructurePalette gate = StructurePieces.LinearPalette(kit, wall, "LinearGate");
			Assert.That(gate.For(BiomeStructureStyle.Timber, BiomeStructureRole.Dressing).Material, Is.EqualTo(BiomeStructureMaterial.Sandstone));
			Assert.That(gate.For(BiomeStructureStyle.Timber, BiomeStructureRole.Fence).Material, Is.EqualTo(BiomeStructureMaterial.Timber));
			List<BiomeStructurePalette> generated = new List<BiomeStructurePalette>(StructurePieces.LinearPalettes(new[] { wall }));
			Assert.That(generated.Count, Is.EqualTo(2), "the wall's and its gate's");
			Assert.That(StructurePalettes.KeysOf(generated), Is.Not.Empty, "the generator makes their materials");
		}

		[Test]
		public void Spec_LinesResolveTheirSpecies_ByKind()
		{
			var hedge = new BiomeArtSpec.Entry.Line(BiomeLinearForm.Laid, BiomeLinearPlace.FieldEdges, "Hawthorn", "Hazel") { Standards = new[] { "Oak" } };
			Assert.That(hedge.PrefabOf("Hawthorn", false), Is.EqualTo(ProceduralArtCatalogue.BushPrefab("Hawthorn")));
			Assert.That(hedge.PrefabOf("Oak", true), Is.EqualTo(ProceduralArtCatalogue.TreePrefab("Oak")));
			var row = new BiomeArtSpec.Entry.Line(BiomeLinearForm.Filare, BiomeLinearPlace.Lanes, "LombardyPoplar");
			Assert.That(row.PrefabOf("LombardyPoplar", false), Is.EqualTo(ProceduralArtCatalogue.TreePrefab("LombardyPoplar")));
			Assert.That(row.PrefabOf("Detail_Bush_Box", false), Is.EqualTo("Detail_Bush_Box"), "a full name is kept");
			BiomeArtSpec.Entry entry = new BiomeArtSpec.Entry().Lines(hedge, row);
			var missing = new List<string>();
			List<BiomeLinearStyle> styles = entry.LinearFeatures(_ => null, missing);
			Assert.That(styles.Count, Is.EqualTo(2));
			Assert.That(styles[0].Kind, Is.EqualTo(BiomeLinearKind.Hedge));
			Assert.That(styles[0].Species, Is.Empty, "not generated: left out");
			Assert.That(missing, Is.EqualTo(new[] { "Detail_Bush_Hawthorn", "Detail_Bush_Hazel", "Tree_Oak", "Tree_LombardyPoplar" }));
		}

		// ── Wiring ────────────────────────────────────────────────────

		[Test]
		public void Wiring_FieldsStageAuthoringReaderGenerator()
		{
			string fields = Read(FieldsSource);
			int lined = fields.IndexOf("bool lined = PointOfInterestLinearFeatures.LayFieldEdges(context, field.Centre, field.Yaw, field.Size, site, abandoned, f);", StringComparison.Ordinal);
			Assert.That(lined, Is.GreaterThan(0));
			int fence = fields.IndexOf("Fence(context, field, site, abandoned, ", lined, StringComparison.Ordinal);
			Assert.That(fence, Is.GreaterThan(lined));
			StringAssert.Contains("dry: lined);", fields.Substring(fence, 300), "the kit fence's draws still made");

			string stage = Read(StageSource);
			int walks = stage.IndexOf("PathBoardwalks.LayWays(asset.Paths", StringComparison.Ordinal);
			int linear = stage.IndexOf("PointOfInterestLinearFeatures.LayScene(", StringComparison.Ordinal);
			int bake = stage.IndexOf("ScenePathBake.Write(", StringComparison.Ordinal);
			Assert.That(walks, Is.GreaterThan(0));
			Assert.That(linear, Is.GreaterThan(walks), "after the sites and the boardwalks");
			Assert.That(bake, Is.GreaterThan(linear), "before the ways are baked (a channel's bed is one)");
			int restore = stage.IndexOf("PointOfInterestFieldCarve.Restore(scene, terrains, notes);", StringComparison.Ordinal);
			int clear = stage.IndexOf("PointOfInterestPaddies.ClearWater(TerrainFolder, Request.SceneName);", StringComparison.Ordinal);
			Assert.That(restore, Is.GreaterThan(0).And.LessThan(linear), "the last run's channels' carve taken out first");
			Assert.That(clear, Is.GreaterThan(0).And.LessThan(linear), "and their water");

			string linearSource = Read(LinearSource);
			StringAssert.Contains("if (!AnyIn(biomes))", linearSource, "the fast path");
			Assert.That(linearSource.IndexOf("Random.", StringComparison.Ordinal), Is.LessThan(0), "never a site's sequence");
			StringAssert.Contains("AuthorLinearFeatures(biome, entry, source, report, apply);", Read(AuthoringSource));
			StringAssert.Contains("public static IReadOnlyList<BiomeLinearStyle> LinearFeatures(BiomeTemplate biome)", Read(ReaderSource));
			StringAssert.Contains("StructurePieces.LinearPalettes(biome.LinearFeatures)", Read(StructuresGeneratorSource));
			StringAssert.Contains("StructurePieces.LinearPalettes(entry.LinearFeatures(null))", Read(StructuresGeneratorSource));
		}

		// ── Native (Unity or nativerun) ───────────────────────────────

		/// <summary>
		/// Every linear piece builds closed and within its size's budget at every level (StructureKitTests checks the same for the whole
		/// kit), and every role a style's own material is laid on is one its body really plays.
		/// </summary>
		[Test]
		public void Native_LinearPieces_AreWithinBudget_AndPlayTheirRoles()
		{
			foreach (string id in LinearPieces)
			{
				StructurePiece p = StructurePieces.Find(id);
				for (int v = 0; v < p.Variants; v++)
				{
					int previous = int.MaxValue;
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						StructureMesh m = StructurePieces.Build(p, v, lod, ProceduralArtCatalogue.DefaultSeed);
						int tris = m.Mesh.TriangleCount;
						Assert.That(tris, Is.GreaterThan(0).And.LessThanOrEqualTo(previous), $"{id} v{v} LOD{lod}");
						if (lod == 0)
						{
							Assert.That(tris, Is.LessThanOrEqualTo(p.TriangleBudget), $"{id} v{v}: {tris} over {p.TriangleBudget}");
						}
						previous = tris;
					}
				}
			}
			foreach (BiomeLinearForm form in (BiomeLinearForm[])Enum.GetValues(typeof(BiomeLinearForm)))
			{
				(string id, int variant) = StructurePieces.LinearPieceFor(form, false, 0f);
				if (id == null)
				{
					continue;
				}
				var played = new HashSet<BiomeStructureRole>();
				foreach (StructureSolid s in StructurePieces.BuildSolids(StructurePieces.Find(id), variant, 0, ProceduralArtCatalogue.DefaultSeed))
				{
					if (s.Part.HasValue)
					{
						played.Add(s.Part.Value);
					}
				}
				Assert.That(played, Does.Contain(StructurePieces.LinearRolesOf(id, BiomeLinearFeatures.KindOf(form))[0]), $"{form}: {id}'s body plays its role");
			}
		}
	}
}
