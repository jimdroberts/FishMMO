using System.IO;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The Moron 2 round (2026-10-27): the fences laid along the high country's ways, and the ground the sites enclose. Alpine
	/// Meadow's leaning-pole fence lines only its villages' drove lanes (the cart tracks and roads near a settlement), never the
	/// pasture's footpaths and trails; a field, a fold, a farmstead or a holding is cleared of the scatter's trees, bushes and
	/// boulders, a turf one of its wild details too, its grass kept (PointOfInterestEnclosureClearance).
	/// </summary>
	public class LaneFencesAndEnclosureClearanceTests
	{
		private const string Features = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/";

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		// ── Lane fences ───────────────────────────────────────────────

		[Test]
		public void AlpineMeadow_DroveLaneFence_KeepsToTheVillagesTracks()
		{
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Alpine Meadow");
			Assert.That(entry, Is.Not.Null);
			int lanes = 0;
			foreach (BiomeArtSpec.Entry.Line line in entry.LinearLines())
			{
				BiomeLinearStyle style = line.Style;
				if (style.Where != BiomeLinearPlace.Lanes)
				{
					continue;
				}
				lanes++;
				Assert.That(style.Form, Is.EqualTo(BiomeLinearForm.LeaningPole));
				Assert.That(style.Reach, Is.EqualTo(BiomeArtSpec.DroveLaneReach).And.GreaterThan(0f), "fenced only near a settlement");
				Assert.That(BiomeLinearFeatures.Follows(style.Ways, ScenePathClass.CartTrack), Is.True);
				Assert.That(BiomeLinearFeatures.Follows(style.Ways, ScenePathClass.Road), Is.True);
				Assert.That(BiomeLinearFeatures.Follows(style.Ways, ScenePathClass.Footpath), Is.False, "the pasture's footpaths are open");
				Assert.That(BiomeLinearFeatures.Follows(style.Ways, ScenePathClass.Trail), Is.False, "the high trails are open");
			}
			Assert.That(lanes, Is.EqualTo(1), "one drove-lane fence");
		}

		[Test]
		public void SnowyUplands_LayOnlySpacedMarkersAlongTheirWays()
		{
			// The snowy high country of the Moron scenes: a style laid along every way of these biomes is a marker line laid singly
			// (cairns, wands, serge posts), or a rope only up genuinely steep ways; never a continuous fence along an ordinary road.
			string[] uplands = { "Tundra", "Ice Sheet", "Glacier", "Mountain Slope", "Scree", "Rocky Terrain" };
			foreach (string name in uplands)
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				Assert.That(entry, Is.Not.Null, name);
				foreach (BiomeArtSpec.Entry.Line line in entry.LinearLines())
				{
					BiomeLinearStyle style = line.Style;
					if (style.Where != BiomeLinearPlace.Lanes || !BiomeLinearFeatures.IsLaid(style))
					{
						continue;
					}
					bool marker = style.Kind == BiomeLinearKind.Markers || style.Form == BiomeLinearForm.Cairn;
					bool rope = style.Form == BiomeLinearForm.HandRope && BiomeLinearFeatures.MinSlopeOf(style.Form) >= 15f;
					Assert.That(marker || rope || style.Reach > 0f, Is.True, $"{name} lays {style.Form} along every way");
					if (marker)
					{
						Assert.That(style.Spacing, Is.GreaterThanOrEqualTo(10f), $"{name}'s {style.Form} are spaced, not continuous");
					}
				}
			}
		}

		// ── Enclosures ────────────────────────────────────────────────

		[Test]
		public void Trees_GoFromInsideAndUnderTheWall_NotBeyond()
		{
			var field = new PointOfInterestEnclosureClearance.Enclosure(new Vector2(100f, 50f), 0f, new Vector2(40f, 20f), turf: false);
			Assert.That(PointOfInterestEnclosureClearance.ClearsTree(field, new Vector2(100f, 50f)), Is.True, "the middle");
			Assert.That(PointOfInterestEnclosureClearance.ClearsTree(field, new Vector2(119f, 59f)), Is.True, "a corner, inside");
			Assert.That(PointOfInterestEnclosureClearance.ClearsTree(field, new Vector2(121f, 50f)), Is.True, "in the wall's line, 1 m out");
			Assert.That(PointOfInterestEnclosureClearance.ClearsTree(field, new Vector2(100f, 50f + 10f + PointOfInterestEnclosureClearance.TreeMarginMetres + 0.1f)),
				Is.False, "past the wall");
			Assert.That(PointOfInterestEnclosureClearance.ClearsTree(field, new Vector2(130f, 50f)), Is.False, "the wood beyond");
			// Turned a quarter: its long side runs north.
			var turned = new PointOfInterestEnclosureClearance.Enclosure(new Vector2(0f, 0f), 90f, new Vector2(40f, 20f), turf: true);
			Assert.That(PointOfInterestEnclosureClearance.ClearsTree(turned, new Vector2(0f, 19f)), Is.True);
			Assert.That(PointOfInterestEnclosureClearance.ClearsTree(turned, new Vector2(19f, 0f)), Is.False);
		}

		[Test]
		public void Details_TurfKeepsItsGrass_SownGroundIsLeftToItsCrop()
		{
			var pasture = new PointOfInterestEnclosureClearance.Enclosure(Vector2.zero, 0f, new Vector2(30f, 20f), turf: true);
			var sown = new PointOfInterestEnclosureClearance.Enclosure(Vector2.zero, 0f, new Vector2(30f, 20f), turf: false);
			Vector2 middle = Vector2.zero;
			Assert.That(PointOfInterestEnclosureClearance.ClearsDetail(pasture, middle, "Detail_Bush_Knieholz"), Is.True, "bushes go");
			Assert.That(PointOfInterestEnclosureClearance.ClearsDetail(pasture, middle, "Detail_Rocks_Flysch"), Is.True, "rocks go");
			Assert.That(PointOfInterestEnclosureClearance.ClearsDetail(pasture, middle, "Detail_CarlineThistle"), Is.True, "wild herbs go");
			Assert.That(PointOfInterestEnclosureClearance.ClearsDetail(pasture, middle, "Detail_GrassBent"), Is.False, "the sward stays");
			Assert.That(PointOfInterestEnclosureClearance.ClearsDetail(pasture, middle, "Detail_FlowersPolonyna"), Is.False, "the meadow's blade flowers stay");
			Assert.That(PointOfInterestEnclosureClearance.ClearsDetail(sown, middle, "Detail_Bush_Knieholz"), Is.False, "a crop's ground was laid by its crop");
			// The hedge's leaves and the wall foot's tufts, at the edge, stay.
			Assert.That(PointOfInterestEnclosureClearance.ClearsDetail(pasture, new Vector2(14.5f, 0f), "Detail_Bush_HawthornWoodland"), Is.False);
			Assert.That(PointOfInterestEnclosureClearance.ClearsDetail(pasture, new Vector2(13.5f, 0f), "Detail_Bush_HawthornWoodland"), Is.True);
			Assert.That(PointOfInterestEnclosureClearance.ClearsDetail(pasture, new Vector2(16f, 0f), "Detail_Bush_HawthornWoodland"), Is.False, "outside");
		}

		[Test]
		public void Claims_CarryTheEnclosures_InTheOrderLaid()
		{
			var claims = new PointOfInterestFieldClaims();
			Assert.That(claims.Enclosures, Is.Empty);
			claims.Enclose(new Vector2(1f, 2f), 15f, new Vector2(30f, 20f), turf: true);
			claims.Enclose(new Vector2(-5f, 0f), 0f, new Vector2(14f, 10f), turf: false);
			Assert.That(claims.Enclosures.Count, Is.EqualTo(2));
			Assert.That(claims.Enclosures[0].Turf, Is.True);
			Assert.That(claims.Enclosures[0].Yaw, Is.EqualTo(15f));
			Assert.That(claims.Enclosures[1].Size, Is.EqualTo(new Vector2(14f, 10f)));
			Assert.That(claims.All, Is.Empty, "enclosing claims no ground: the fields' fit is unchanged");
			Assert.That(PointOfInterestEnclosureClearance.Clear(null, claims.Enclosures, null), Is.EqualTo(0), "no terrain, nothing read");
		}

		[Test]
		public void Wiring_EveryEnclosureIsRecorded_AndClearedBeforeTheBake()
		{
			string fields = Read(Features + "Features/PointOfInterestFields.cs");
			Assert.That(fields, Does.Contain("context.FieldClaims?.Enclose(field.Centre, field.Yaw, field.Size, turf: !painted);"));
			Assert.That(fields, Does.Contain("PaintDetails(terrains, field, crop, biome, surface);\n\t\t\t\t\t\tpainted = true;"));
			Assert.That(Read(Features + "Features/PointOfInterestLinearFeatures.cs"), Does.Contain("scope.Claims?.Enclose(centre, bearing, FoldSize, turf: true);"));
			Assert.That(Read(Features + "Features/PointOfInterestFarmsteads.cs"), Does.Contain("context.FieldClaims?.Enclose(centre, yaw, size, turf: true);"));
			Assert.That(Read(Features + "Features/PointOfInterestOutskirts.cs"), Does.Contain("context.FieldClaims?.Enclose(centre, yaw, size, turf: true);"));
			string stage = Read(Features + "PointOfInterestStage.cs");
			int lay = stage.IndexOf("PointOfInterestLinearFeatures.LayScene(", System.StringComparison.Ordinal);
			int clear = stage.IndexOf("PointOfInterestEnclosureClearance.Clear(terrains, fieldClaims.Enclosures, notes);", System.StringComparison.Ordinal);
			int floors = stage.IndexOf("PointOfInterestTreeClearance.Clear(", System.StringComparison.Ordinal);
			Assert.That(lay, Is.GreaterThan(0));
			Assert.That(clear, Is.GreaterThan(lay), "after the folds are laid");
			Assert.That(floors, Is.GreaterThan(clear), "beside the floors' clearance, before the trees are baked");
		}
	}
}
