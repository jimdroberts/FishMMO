using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Decision 29 (2026-10-18): ground layers' climate bands and climate variants' ground overrides, written by the authoring
	/// from the spec and painted by the palette; decision 23: the description and naming columns. Unity-only: templates and
	/// terrain layers are native objects (the pure parts are BiomeClimateGroundTests).
	/// </summary>
	public partial class BiomeProceduralArtTests
	{
		[Test]
		public void Authoring_WritesClimateBandsAndVariantGrounds_OnceAndKeepsHandEdits()
		{
			// The Valley (unauthored): no bands or climate grounds of its own (the Plains round, 2026-10-26, authored both).
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Valley");
			BiomeArtSpec.Layer main = entry.Main, grass = entry.Details[0];
			try
			{
				// A spec entry is shared, so its bands and variants are set here and put back after.
				grass.Temp(0.2f, 1f);
				var snow = new BiomeArtSpec.Layer { Family = Ground.Snow };
				entry.ClimateVariant("Cold", -1f, -0.3f).ClimateGround("Cold", snow);
				BiomeTemplate biome = MakeBiome("Valley");
				var source = new FakeSource(created);

				var first = new BiomeArtAuthoring.Report();
				Assert.IsTrue(BiomeArtAuthoring.Author(biome, entry, source, first, true));
				TerrainTextureLayer slot = biome.DetailTextureLayers[0];
				Assert.That(slot.useClimateBand, Is.True, "the detail's band:\n" + first);
				Assert.That(slot.temperatureRange, Is.EqualTo(new Vector2(0.2f, 1f)));
				Assert.That(biome.MainTextureLayer.useClimateBand, Is.False, "a layer the spec bands not stays unbanded");
				Assert.That(biome.AuthoredFingerprint(BiomeTemplate.Authored.GroundClimate("main")), Is.Null, "no band, no mark");
				BiomeClimateVariant cold = biome.FindOwnVariant("Cold");
				Assert.That(cold, Is.Not.Null, "the declared variant was added");
				Assert.That(cold.MaxTemperature, Is.EqualTo(-0.3f));
				Assert.AreSame(source.Layer(Ground.Snow), cold.TextureOverrideFor(0).terrainLayer, "its main ground");
				Assert.That(BiomeLocalArt.SlotsOf(biome), Does.Contain("variant/cold/main"));
				Assert.AreSame(cold.TextureOverrideFor(0), BiomeLocalArt.CommittedLayer(biome, "variant/cold/main"));

				var second = new BiomeArtAuthoring.Report();
				Assert.IsFalse(BiomeArtAuthoring.Author(biome, entry, source, second, true), "a second run changed something:\n" + second);

				// Hand edits: the band widened, the variant's envelope moved. Both kept; the spec then moves its own band.
				slot.temperatureRange = new Vector2(0f, 1f);
				cold.MaxTemperature = -0.5f;
				grass.Temp(0.3f, 1f);
				var third = new BiomeArtAuthoring.Report();
				BiomeArtAuthoring.Author(biome, entry, source, third, true);
				Assert.That(slot.temperatureRange, Is.EqualTo(new Vector2(0f, 1f)), "a hand-tuned band was replaced:\n" + third);
				Assert.That(cold.MaxTemperature, Is.EqualTo(-0.5f), "a hand-moved envelope was replaced");

				// A variant the tool added and somebody deleted stays deleted.
				biome.ClimateVariants.Remove(cold);
				BiomeArtAuthoring.Author(biome, entry, source, new BiomeArtAuthoring.Report(), true);
				Assert.That(biome.FindOwnVariant("Cold"), Is.Null);
			}
			finally
			{
				grass.Temperature = null;
				entry.Variants.Clear();
				entry.VariantLayers.Clear();
				Assert.That(main.HasClimateBand, Is.False);
			}
		}

		[Test]
		public void Authoring_WritesDescriptionAndNaming_OverTheOriginalOnce_ThenKeepsHandEdits()
		{
			// The Valley (unauthored): no description or naming column (the Hills round, 2026-10-26, rewrote both).
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Valley");
			try
			{
				BiomeTemplate biome = MakeBiome("Valley");
				biome.Description = "Rolling hills — moss-covered barrows";
				biome.Naming.Phonology.Onsets = new[] { "Hil", "Bre" };
				biome.Naming.Phonology.Codas = new[] { "hill" };
				var source = new FakeSource(created);

				// No column: nothing touched, nothing marked.
				BiomeArtAuthoring.Author(biome, entry, source, new BiomeArtAuthoring.Report(), true);
				Assert.That(biome.Description, Is.EqualTo("Rolling hills — moss-covered barrows"));
				Assert.That(biome.AuthoredFingerprint(BiomeTemplate.Authored.Description), Is.Null);

				entry.Describe("Terraced schist hills").Names(codas: new[] { "edo", "eira" }, poiWords: new[] { ("spring", new[] { "Fonte" }) });
				var first = new BiomeArtAuthoring.Report();
				BiomeArtAuthoring.Author(biome, entry, source, first, true);
				Assert.That(biome.Description, Is.EqualTo("Terraced schist hills"), "the original text is replaced once:\n" + first);
				Assert.That(biome.Naming.Phonology.Codas, Is.EqualTo(new[] { "edo", "eira" }));
				Assert.That(biome.Naming.Phonology.Onsets, Is.EqualTo(new[] { "Hil", "Bre" }), "a part not set is left as it was");
				Assert.That(biome.Naming.Phonology.POITypeWords.Count, Is.EqualTo(1));

				var second = new BiomeArtAuthoring.Report();
				Assert.IsFalse(BiomeArtAuthoring.Author(biome, entry, source, second, true), "a second run changed something:\n" + second);

				biome.Description = "My hills";
				biome.Naming.Phonology.Codas = new[] { "ac" };
				entry.Describe("Terraced schist hills, again").Names(codas: new[] { "argues" });
				var third = new BiomeArtAuthoring.Report();
				BiomeArtAuthoring.Author(biome, entry, source, third, true);
				Assert.That(biome.Description, Is.EqualTo("My hills"), "a hand edit was replaced:\n" + third);
				Assert.That(biome.Naming.Phonology.Codas, Is.EqualTo(new[] { "ac" }));
			}
			finally
			{
				entry.Description = null;
				entry.Naming = null;
			}
		}

		[Test]
		public void Palette_PaintsAVariantsOverride_AndTakesNoneWhenItListsNone()
		{
			BiomeTemplate plains = MakeBiome("Plains");
			var grass = new TerrainLayer { name = "Ground_GrassDry", diffuseTexture = MakeTexture("grass") };
			var snow = new TerrainLayer { name = "Ground_Snow", diffuseTexture = MakeTexture("snow") };
			created.AddRange(new Object[] { grass, snow });
			plains.MainTextureLayer.terrainLayer = grass;
			var frozen = new BiomeClimateVariant { Name = "Frozen", MinTemperature = -1f, MaxTemperature = -0.45f };
			plains.ClimateVariants.Add(frozen);
			SceneBiomeField field = SceneBiomeField.FromCells(4, 4, 16f, Vector2.zero, new[] { plains }, new byte[16], 16f);

			SceneTerrainPalette before = SceneTerrainPalette.Build(field, (b, s, l) => l?.terrainLayer, null);
			Assert.That(before.Layers.Count, Is.EqualTo(1));
			Assert.That(before.NeedsClimate, Is.False, "a variant listing no grounds changes nothing");

			frozen.TextureLayerOverrides.Add(new TerrainTextureLayer { terrainLayer = snow });
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, (b, s, l) => l?.terrainLayer, null);
			Assert.That(palette.Layers.Count, Is.EqualTo(2));
			Assert.That(palette.NeedsClimate, Is.True);
			SceneTerrainPalette.Entry over = palette.Entries[1];
			Assert.That(over.Slot, Is.EqualTo("variant/frozen/main"));
			Assert.That(over.Role, Is.EqualTo(PaletteRole.Main));
			Assert.That(over.Variant, Is.SameAs(frozen));
			Assert.That(SceneTerrainPalette.SlotLayer(plains, over.Slot), Is.SameAs(frozen.TextureLayerOverrides[0]));

			var weigher = new BiomeSplatPainter.Weigher(palette, field, new BiomeSplatOptions());
			var w = new float[2];
			weigher.Weigh(30f, 5f, 30f, 1f, w, new Vector2(-0.8f, 0f));
			Assert.That(w[1], Is.EqualTo(1f).Within(1e-5f));
			weigher.Weigh(30f, 5f, 30f, 1f, w, new Vector2(0.3f, 0f));
			Assert.That(w[0], Is.EqualTo(1f).Within(1e-5f));
		}
	}
}
