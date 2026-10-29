using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2g-c of the biome suite (2026-10-25): the structure COATS every biome draft's new finish asked for, drawn on the GPU
	/// (BiomeStructureCoats → FloraPaletteMap's _FishCoatTable → FishStructureCoat.hlsl). A weathering with no coat writes the same mark,
	/// a row of zeros and a table switched off, so every building draws as before.
	/// </summary>
	public class BiomeStructureCoatTests
	{
		/// <summary>
		/// Every new finish a biome draft names (the drafts live outside the repository; their spellings, "Plume-rimed" included), by
		/// the biome that asked for it. Lichen, Rimed, Epiphytic and Sunburnt from decisions 25, 31 and 33; the rest from each round.
		/// </summary>
		private static readonly (string Biome, string Finish)[] DraftFinishes =
		{
			("Tundra", "Lichen"), ("Alpine", "Rimed"), ("Mountain Slope", "Epiphytic"), ("Alpine Meadow", "Sunburnt"), ("Valley", "Streaked"),
			("Plains", "Dusted"), ("Farmland", "Saltpetre"), ("Wetlands", "Silted"), ("Badlands", "Ashworn"), ("Beach", "Sandblasted"),
			("Castle", "Limeworn"), ("Abyssal Plain", "Noduled"), ("Abyss", "Encrusted"), ("Cave", "Dripcrust"), ("River", "Tarred"),
			("Coastal Water", "Coralline"), ("Coral Reef", "Reefgrown"), ("Crater", "Efflorescent"), ("Desert", "Drifted"),
			("Dust Sea", "Staticveil"), ("Cryovolcanic Plain", "Engulfed"), ("Estuary", "Saltbloom"), ("Forest Ruins", "Reclaimed"),
			("Geyser Basin", "Sintered"), ("Fortress", "Sooted"), ("Glacier", "Floured"), ("Ice Cave", "Glazed"), ("High Desert", "Spalled"),
			("Jungle", "Rainwashed"), ("Jungle Ruins", "Stuccofall"), ("Ice Palace", "Sublimed"), ("Ice Geyser Field", "Plume-rimed"),
			("Lava Tube", "Cinderdrift"), ("Impact Basin", "Starworn"), ("Deep Ocean", "Gooseneck"), ("Ice Ruin", "Icejacked"),
			("Mangrove", "Tideline"), ("Jungle Temple", "Rootheave"), ("Methane Lake", "Waxed"), ("Marble Palace", "Ochred"),
			("Ice Sheet", "Windpacked"), ("Molten Surface", "Spatterglazed"), ("Oasis", "Saltmelt"), ("Ice Shelf", "Sprayglazed"),
			("Ocean", "Coldreef"), ("Permanent Ice", "Pristine"), ("Radiation Plain", "Sputtered"), ("Regolith Plain", "Sunsplit"),
			("Salt Flat", "Dewfluted"), ("Rocky Coast", "Saltscoured"), ("Savanna", "Termitecast"), ("Rille", "Riftstepped"),
			("Scrubland", "Rainmelt"), ("Rocky Terrain", "Sunfrost"), ("Scree", "Stoneshot"), ("Seamount", "Currentgrown"), ("Snow", "Snowladen"),
			// I2g-e: the drafts of decisions 82–96.
			("Steppe", "Burandrift"), ("Sulphur Flats", "Fumed"), ("Sulphuric Cloud Deck", "Acidbitten"), ("Swamp Cave", "Teastain"),
			("Swamp Ruins", "Sinkline"), ("Swamp Temple", "Mistmould"), ("Runaway Greenhouse Plain", "Kilnbaked"), ("Tholin Plain", "Hazefall"),
			("Tidal Fracture", "Sheared"), ("Underwater Canyon", "Flowscoured"), ("Volcanic", "Cinderdusted"), ("Volcanic Cave", "Heatspall"),
			("Volcanic Temple", "Rewashed"), ("Wasteland", "Sapless"), ("Subsurface Ocean Vent", "Smokecrust"),
		};

		[Test]
		public void EveryDraftFinish_IsACoatWithARecipeThatDraws()
		{
			var seen = new HashSet<BiomeStructureCoat>();
			foreach ((string biome, string finish) in DraftFinishes)
			{
				Assert.That(BiomeStructureCoats.TryParse(finish, out BiomeStructureCoat coat), Is.True, $"{biome}: {finish} is no coat");
				Assert.That(BiomeStructureCoats.Knows(coat), Is.True, $"{finish}: no recipe");
				Assert.That(BiomeStructureCoats.Recipe(coat).Draws, Is.True, $"{finish}: its recipe draws nothing");
				Assert.That(seen.Add(coat), Is.True, $"{finish} named twice");
			}
			// And every coat is one a draft named: no orphan recipe.
			foreach (BiomeStructureCoat coat in (BiomeStructureCoat[])Enum.GetValues(typeof(BiomeStructureCoat)))
			{
				if (coat != BiomeStructureCoat.None)
				{
					Assert.That(seen.Contains(coat), Is.True, $"{coat}: no draft names it");
				}
			}
			Assert.That(BiomeStructureCoats.Knows(BiomeStructureCoat.None), Is.False);
			Assert.That(BiomeStructureCoats.TryParse("Not A Finish", out _), Is.False);
			Assert.That((int)BiomeStructureCoat.None, Is.Zero, "None stays 0: every template's stored value");
		}

		[Test]
		public void TheCoatsAreNotTheKitsFinishes()
		{
			// A coat dedupes against the material finishes: none of the kit's own finishes is redrawn as a coat.
			foreach (BiomeStructureFinish finish in (BiomeStructureFinish[])Enum.GetValues(typeof(BiomeStructureFinish)))
			{
				Assert.That(BiomeStructureCoats.TryParse(finish.ToString(), out _), Is.False, $"{finish} is a finish and a coat");
			}
		}

		[Test]
		public void NoCoat_WritesTheOldMark_AZeroRow_AndATableSwitchedOff()
		{
			var legacy = new BiomeWeathering(BiomeStructureFinish.Mossy, 0f, 0.5f);
			Assert.That(legacy.Signature(), Is.EqualTo("Mossy|0|0.5"), "the mark every template was authored with");
			Assert.That(legacy.ToString(), Is.EqualTo("Mossy 0 + 0.5 × decay"));
			Assert.That(legacy.Coats, Is.False);
			foreach (AtmosphereKind air in (AtmosphereKind[])Enum.GetValues(typeof(AtmosphereKind)))
			{
				foreach (Vector4 texel in BiomeStructureCoats.Texels(legacy, air))
				{
					Assert.That(texel, Is.EqualTo(Vector4.zero));
				}
			}
			Assert.That(BiomeStructureCoats.Texels(legacy, AtmosphereKind.Standard).Length, Is.EqualTo(BiomeStructureCoats.TexelsPerBiome));
			Assert.That(BiomeStructureCoats.AnyCoat(new[] { legacy, default(BiomeWeathering) }), Is.False);
			Assert.That(BiomeStructureCoats.AnyCoat(null), Is.False);
			// A coat named with no cover or no strength draws nothing either.
			Assert.That(legacy.WithCoat(BiomeStructureCoat.Lichen, 0f).Coats, Is.False);
			Assert.That(legacy.WithCoat(BiomeStructureCoat.Lichen, 0.5f, 0f).Coats, Is.False);
		}

		[Test]
		public void ACoat_MarksItselfAfterTheOldMark_AndFillsItsRow()
		{
			BiomeWeathering w = new BiomeWeathering(BiomeStructureFinish.None, 0f, 0f).WithCoat(BiomeStructureCoat.Lichen, 0.3f, 0.8f);
			Assert.That(w.Coats, Is.True);
			Assert.That(w.Signature(), Does.StartWith("None|0|0|coat Lichen|"));
			Assert.That(w.Signature(), Is.Not.EqualTo(w.WithCoat(BiomeStructureCoat.Lichen, 0.3f, 0.8f, new Color(1f, 0f, 0f, 0.5f)).Signature()), "the tint is marked");
			Vector4[] row = BiomeStructureCoats.Texels(w, AtmosphereKind.Standard);
			Assert.That(row[0].w, Is.EqualTo(0.8f).Within(1e-6f), "strength");
			Assert.That(row[1].w, Is.EqualTo(0.3f).Within(1e-6f), "cover");
			Assert.That(row[7].z, Is.GreaterThan(0f), "a pattern scale");
			Assert.That(BiomeStructureCoats.AnyCoat(new[] { default(BiomeWeathering), w }), Is.True);
			// Linear light: the recipe's sRGB grey crust darker in linear.
			BiomeCoatRecipe lichen = BiomeStructureCoats.Recipe(BiomeStructureCoat.Lichen);
			Assert.That(row[0].x, Is.LessThan(lichen.A.r));
			// A tint alpha 1 replaces the main colour.
			Vector4[] tinted = BiomeStructureCoats.Texels(w.WithCoat(BiomeStructureCoat.Lichen, 0.3f, 0.8f, new Color(1f, 0f, 0f, 1f)), AtmosphereKind.Standard);
			Assert.That(tinted[0].x, Is.EqualTo(1f).Within(1e-5f));
			Assert.That(tinted[0].y, Is.EqualTo(0f).Within(1e-5f));
		}

		[Test]
		public void Icejacked_IsAirAware_AndTheAirBorneCoatsVanishWithoutAir()
		{
			BiomeWeathering ice = default(BiomeWeathering).WithCoat(BiomeStructureCoat.Icejacked, 0.9f);
			Vector4[] withAir = BiomeStructureCoats.Texels(ice, AtmosphereKind.Standard);
			Vector4[] airless = BiomeStructureCoats.Texels(ice, AtmosphereKind.None);
			Assert.That(withAir[4].x, Is.GreaterThan(0f), "drift caps with air");
			Assert.That(withAir[5].z, Is.GreaterThan(0f), "icicles with air");
			Assert.That(airless[4].x, Is.Zero, "no drift caps without air");
			Assert.That(airless[5].z, Is.Zero, "no icicles without air");
			Assert.That(airless[2].w, Is.Zero, "no rime speckle without air");
			Assert.That(airless[5].w, Is.EqualTo(withAir[5].w), "the ice lenses in the joints stay");
			Assert.That(airless[6].w, Is.GreaterThanOrEqualTo(0.5f), "crisp: the years' moss and grime undone");
			Assert.That(BiomeStructureCoats.Texels(ice, AtmosphereKind.Thin)[4].x, Is.Zero, "thin air is not enough");
			Assert.That(BiomeStructureCoats.Texels(ice, AtmosphereKind.Thick)[4].x, Is.EqualTo(withAir[4].x));
			// Lichen and spray glaze need air altogether.
			foreach (BiomeStructureCoat coat in new[] { BiomeStructureCoat.Lichen, BiomeStructureCoat.Sprayglazed, BiomeStructureCoat.Rimed })
			{
				foreach (Vector4 texel in BiomeStructureCoats.Texels(default(BiomeWeathering).WithCoat(coat, 1f), AtmosphereKind.None))
				{
					Assert.That(texel, Is.EqualTo(Vector4.zero), $"{coat} on an airless world");
				}
			}
			// A mineral coat does not care.
			Assert.That(BiomeStructureCoats.Texels(default(BiomeWeathering).WithCoat(BiomeStructureCoat.Starworn, 1f), AtmosphereKind.None)[0].w, Is.EqualTo(1f));
		}

		[Test]
		public void Tideline_IsMeasuredFromTheSea_AndPristineKeepsTheBuildingUnaged()
		{
			BiomeCoatRecipe tide = BiomeStructureCoats.Recipe(BiomeStructureCoat.Tideline);
			Assert.That(tide.FromSea, Is.True);
			Assert.That(tide.FootHeight, Is.GreaterThan(0f));
			Vector4[] row = BiomeStructureCoats.Texels(default(BiomeWeathering).WithCoat(BiomeStructureCoat.Tideline, 0.5f), AtmosphereKind.Standard);
			Assert.That(row[7].y, Is.EqualTo(1f), "from the sea's low water");
			Assert.That(row[3].w, Is.EqualTo(tide.FootHeight));
			Assert.That(BiomeStructureCoats.Recipe(BiomeStructureCoat.Pristine).Keep, Is.EqualTo(1f));
			foreach (BiomeStructureCoat coat in (BiomeStructureCoat[])Enum.GetValues(typeof(BiomeStructureCoat)))
			{
				BiomeCoatRecipe r = BiomeStructureCoats.Recipe(coat);
				Assert.That(r.Keep, Is.InRange(0f, 1f), coat.ToString());
				Assert.That(r.Patchiness, Is.InRange(0f, 1f), coat.ToString());
				Assert.That(r.Smoothness, Is.LessThanOrEqualTo(1f), coat.ToString());
				Assert.That(r.FootHeight, Is.InRange(0f, 2f), coat.ToString());
				if (coat != BiomeStructureCoat.Tideline && coat != BiomeStructureCoat.Gooseneck)
				{
					Assert.That(r.FromSea, Is.False, $"{coat}: only the waterline coats read the sea");
				}
			}
		}

		/// <summary>The coats that read a term of texels 8.zw / 9 or the local water (I2g-e); every other row leaves them zero.</summary>
		private static readonly BiomeStructureCoat[] LaterTerms =
		{
			BiomeStructureCoat.Waxed, BiomeStructureCoat.Riftstepped, BiomeStructureCoat.Currentgrown, BiomeStructureCoat.Burandrift,
			BiomeStructureCoat.Teastain, BiomeStructureCoat.Sinkline, BiomeStructureCoat.Mistmould, BiomeStructureCoat.Hazefall,
			BiomeStructureCoat.Sheared, BiomeStructureCoat.Flowscoured, BiomeStructureCoat.Rewashed,
		};

		[Test]
		public void TheLaterTerms_FillOnlyTheirCoatsRows_AndEveryOtherRowIsAsBefore()
		{
			var later = new HashSet<BiomeStructureCoat>(LaterTerms);
			foreach (BiomeStructureCoat coat in (BiomeStructureCoat[])Enum.GetValues(typeof(BiomeStructureCoat)))
			{
				if (coat == BiomeStructureCoat.None)
				{
					continue;
				}
				Vector4[] row = BiomeStructureCoats.Texels(default(BiomeWeathering).WithCoat(coat, 1f), AtmosphereKind.Standard);
				Assert.That(row.Length, Is.EqualTo(10));
				bool uses = row[8].z != 0f || row[8].w != 0f || row[9] != Vector4.zero || row[7].y > 1.5f;
				Assert.That(uses, Is.EqualTo(later.Contains(coat)), $"{coat}: the later terms");
				Assert.That(row[7].y == 0f || row[7].y == 1f || row[7].y == 2f, Is.True, $"{coat}: foot from");
			}
			// The local water: Sinkline's band, Waxed's bathtub ring, Mistmould's flood line.
			foreach (BiomeStructureCoat coat in new[] { BiomeStructureCoat.Sinkline, BiomeStructureCoat.Waxed, BiomeStructureCoat.Mistmould })
			{
				Assert.That(BiomeStructureCoats.Texels(default(BiomeWeathering).WithCoat(coat, 1f), AtmosphereKind.Standard)[7].y, Is.EqualTo(2f), coat.ToString());
				Assert.That(BiomeStructureCoats.Recipe(coat).FootHeight, Is.GreaterThan(0f), coat.ToString());
			}
			Vector4[] tea = BiomeStructureCoats.Texels(default(BiomeWeathering).WithCoat(BiomeStructureCoat.Teastain, 0.9f), AtmosphereKind.Standard);
			Assert.That(tea[3].w * (1f - tea[8].z), Is.EqualTo(0.4f).Within(1e-5f), "the lowest flood height");
			Assert.That(tea[3].w * (1f + tea[8].z), Is.EqualTo(1.6f).Within(1e-5f), "the highest flood height");
			Assert.That(BiomeStructureCoats.Texels(default(BiomeWeathering).WithCoat(BiomeStructureCoat.Rewashed, 1f), AtmosphereKind.Standard)[8].w, Is.GreaterThan(0f), "the year's fade");
			Assert.That(BiomeStructureCoats.Recipe(BiomeStructureCoat.Currentgrown).GrowthBelow, Is.EqualTo(15f), "coralline only above 15 m");
			Assert.That(BiomeStructureCoats.Recipe(BiomeStructureCoat.Sheared).Faults, Is.GreaterThan(0f), "strike-slip: vertical planes");
			Assert.That(BiomeStructureCoats.Recipe(BiomeStructureCoat.Riftstepped).Faults, Is.LessThan(0f), "normal faults along the contour");
		}

		[Test]
		public void Hazefall_LosesItsPolishAndDriftWithoutAir_ButKeepsItsHaze()
		{
			BiomeWeathering haze = default(BiomeWeathering).WithCoat(BiomeStructureCoat.Hazefall, 0.95f);
			Vector4[] titan = BiomeStructureCoats.Texels(haze, AtmosphereKind.Standard);
			Vector4[] thin = BiomeStructureCoats.Texels(haze, AtmosphereKind.Thin);
			Assert.That(titan[9].x, Is.GreaterThan(0f), "the windward polish");
			Assert.That(titan[9].y, Is.EqualTo(1.5f), "to 1.5 m");
			Assert.That(titan[3].w, Is.GreaterThan(0f), "the dark sand at the windward foot");
			Assert.That(titan[7].w, Is.GreaterThan(0f), "drifted on the windward side");
			Assert.That(thin[9].x, Is.Zero, "no saltation without air");
			Assert.That(thin[3].w, Is.Zero, "no drift without air");
			Assert.That(thin[4].x, Is.EqualTo(titan[4].x), "the settled haze stays");
			Assert.That(thin[4].z, Is.EqualTo(titan[4].z), "on the lee too");
			// Fumed without air: the bloom and the frost collar only.
			Vector4[] fumed = BiomeStructureCoats.Texels(default(BiomeWeathering).WithCoat(BiomeStructureCoat.Fumed, 0.6f), AtmosphereKind.None);
			Assert.That(fumed[4].x, Is.GreaterThan(0f), "the bloom");
			Assert.That(fumed[3].w, Is.GreaterThan(0f), "the collar");
			Assert.That(fumed[5].w, Is.Zero, "no sal-ammoniac fur");
			Assert.That(fumed[2].w, Is.Zero, "no acid pits");
			Assert.That(BiomeCoatParts.All.HasFlag(BiomeCoatParts.Scour), Is.True);
		}

		[Test]
		public void TheSpecColumn_KeepsTheFinish_AndWeathersKeepsTheCoat()
		{
			// No spec names a coat until its round authors one (an authored round's own coat: BiomeAuthoredRoundTests).
			foreach (BiomeArtSpec.Entry unauthored in BiomeArtSpec.Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)))
			{
				Assert.That(unauthored.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.None), $"{unauthored.Biome}: no spec names a coat until its round authors one");
			}
			var entry = new BiomeArtSpec.Entry { Biome = "Probe Biome" };
			entry.Coated(BiomeStructureCoat.Lichen, 0.3f).Weathers(BiomeStructureFinish.None, 0f, 0f);
			Assert.That(entry.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Lichen));
			Assert.That(entry.Weathering().Finish, Is.EqualTo(BiomeStructureFinish.None));
			var other = new BiomeArtSpec.Entry { Biome = "Probe Biome" };
			other.Weathers(BiomeStructureFinish.Algae, 0.3f, 0.6f).Coated(BiomeStructureCoat.Tideline, 0.5f, 0.9f);
			Assert.That(other.Weathering().Finish, Is.EqualTo(BiomeStructureFinish.Algae));
			Assert.That(other.Weathering().CoatStrength, Is.EqualTo(0.9f));
		}

		[Test]
		public void TheShader_ReadsTheRowTheTableWrites_InTheIndirectPassOnly()
		{
			string folder = Path.Combine("Assets", "Prefabs", "Client", "Weather", "Shaders");
			string coat = File.ReadAllText(Path.Combine(folder, "FishStructureCoat.hlsl"));
			Assert.That(coat, Does.Contain("#define FISH_COAT_TEXELS " + BiomeStructureCoats.TexelsPerBiome));
			for (int t = 0; t < BiomeStructureCoats.TexelsPerBiome; t++)
			{
				Assert.That(coat, Does.Contain($"FishCoatTexel(row, {t})"), $"texel {t} is read");
			}
			string pass = File.ReadAllText(Path.Combine(folder, "FishWeatherLitForwardPass.hlsl"));
			int include = pass.IndexOf("#include \"FishStructureCoat.hlsl\"", StringComparison.Ordinal);
			Assert.That(include, Is.GreaterThan(0));
			Assert.That(pass.LastIndexOf("#if defined(FISH_INDIRECT_INSTANCED)", include, StringComparison.Ordinal),
				Is.GreaterThan(pass.LastIndexOf("#endif", include, StringComparison.Ordinal)), "the include stands under the indirect twin's #if");
			Assert.That(pass.IndexOf("FishStructureCoat(", StringComparison.Ordinal), Is.GreaterThan(pass.IndexOf("FishStructureWeather(inputData", StringComparison.Ordinal)),
				"the coat lies over the weathering");
			// The later terms (I2g-e) are read, each behind its own zero test, so an old row draws as before.
			foreach (string term in new[] { "t7.y > 1.5", "t8.z", "t8.w > 0.0", "t9.x > 0.0", "t9.z != 0.0", "t9.w > 0.0", "_FishSeason.x" })
			{
				Assert.That(coat, Does.Contain(term), term);
			}
		}
	}
}
