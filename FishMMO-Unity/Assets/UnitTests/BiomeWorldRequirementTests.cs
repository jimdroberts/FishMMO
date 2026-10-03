using System;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Alien biomes are kept off a world by what the world IS — frozen through, heated from below,
	/// what its sky condenses — not by its climate; and the biome spec table that writes those
	/// requirements is explicit, complete and idempotent.
	/// </summary>
	[TestFixture]
	public class BiomeWorldRequirementTests
	{
		/// <summary>The biomes that cannot exist on an Earth-like world, named rather than derived.</summary>
		private static readonly string[] Aliens =
		{
			"Subsurface Ocean Vent", "Tidal Fracture", "Cryovolcanic Plain", "Ice Geyser Field", "Nitrogen Ice Field",
			"Lava Tube", "Tholin Plain", "Methane Lake", "Sulphur Flats", "Wasteland", "Molten Surface", "Radiation Plain",
			"Regolith Plain", "Impact Basin", "Rille", "Dust Sea", "Runaway Greenhouse Plain", "Sulphuric Cloud Deck",
			"Ice Shelf",
		};

		private static BiomeWorldConditions Europa => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.None, MeanTemperature = -1f, Water = 0.35f, InternalHeat = 0.44f,
			TidalHeat = 0.44f, Condensate = Condensate.Methane, GiantMagneticField = 2f,
		};

		private static BiomeWorldConditions Io => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.None, MeanTemperature = -1f, Water = 0f, InternalHeat = 0.8f,
			TidalHeat = 0.8f, Condensate = Condensate.Methane, GiantMagneticField = 2f,
		};

		private static BiomeWorldConditions Titan => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.Standard, MeanTemperature = -1f, Water = 0.3f, InternalHeat = 0.28f,
			TidalHeat = 0.13f, Condensate = Condensate.Methane, GiantMagneticField = 0.6f,
		};

		// Temperatures are the climate field's absolute scale, unclamped: 0 at freezing, 33.1 K a unit.
		private static float Scale(double kelvin) => (float)ClimateModel.ToScaleUnclamped(kelvin);

		private static BiomeWorldConditions Venus => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.Thick, MeanTemperature = Scale(434.0), Water = 0f, InternalHeat = 0.93f,
			Condensate = Condensate.SulphuricAcid,
		};

		/// <summary>A world 17 K colder than the home world (263 K) with air and open seas between its ice caps: a cold Earth, not an ice moon.</summary>
		private static BiomeWorldConditions ColdEarth => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.Standard, MeanTemperature = Scale(263.0), Water = 0.13f, InternalHeat = 0.61f,
			Condensate = Condensate.Water,
		};

		/// <summary>Galris as the project has it: 210 K under air like ours, 13% water, warm inside from its size.</summary>
		private static BiomeWorldConditions Galris => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.Standard, MeanTemperature = Scale(210.0), Water = 0.13f, InternalHeat = 0.61f,
			Condensate = Condensate.Water,
		};

		[Test]
		public void NoRequirement_IsMetEverywhere()
		{
			Assert.IsTrue(BiomeWorldConditions.Earthlike.Meets(BiomeWorldRequirement.None));
			Assert.IsTrue(Europa.Meets(BiomeWorldRequirement.None));
			Assert.IsTrue(default(BiomeWorldConditions).Meets(BiomeWorldRequirement.None));
		}

		[Test]
		public void TheEarthlikeWorld_MeetsOnlyTheFlagsAnOrdinaryWorldHas()
		{
			BiomeWorldConditions earth = BiomeWorldConditions.Earthlike;
			foreach (BiomeWorldRequirement flag in Enum.GetValues(typeof(BiomeWorldRequirement)))
			{
				if (flag == BiomeWorldRequirement.None)
				{
					continue;
				}
				bool ordinary = flag == BiomeWorldRequirement.Volcanic
					|| flag == BiomeWorldRequirement.RockSurface
					|| flag == BiomeWorldRequirement.SurfaceWater;
				Assert.AreEqual(ordinary, earth.Meets(flag), $"{flag} on an Earth-like world");
			}
		}

		[Test]
		public void EachAlienWorld_MeetsWhatMakesItAlien()
		{
			Assert.IsTrue(Europa.Meets(BiomeWorldRequirement.IceWorld | BiomeWorldRequirement.Cryovolcanic | BiomeWorldRequirement.TidallyHeated | BiomeWorldRequirement.GiantMagnetosphere));
			Assert.IsFalse(Europa.Meets(BiomeWorldRequirement.RockSurface), "Europa's crust is ice: no regolith, rilles or lava tubes");
			Assert.IsTrue(Io.Meets(BiomeWorldRequirement.Volcanic | BiomeWorldRequirement.NoLiquidWater | BiomeWorldRequirement.TidallyHeated | BiomeWorldRequirement.RockSurface));
			Assert.IsFalse(Io.Meets(BiomeWorldRequirement.SurfaceWater), "Io has no water to make ice of");
			Assert.IsTrue(Titan.Meets(BiomeWorldRequirement.MethaneCycle | BiomeWorldRequirement.CryogenicAir));
			Assert.IsFalse(Titan.Meets(BiomeWorldRequirement.FrozenNitrogen), "94 K is methane weather, not nitrogen ice");
			Assert.IsTrue(Venus.Meets(BiomeWorldRequirement.RunawayGreenhouse | BiomeWorldRequirement.Volcanic | BiomeWorldRequirement.NoLiquidWater));
			Assert.IsTrue(new BiomeWorldConditions { Atmosphere = AtmosphereKind.Thin, Water = 0.15f, MeanTemperature = -1f, Condensate = Condensate.Nitrogen }
				.Meets(BiomeWorldRequirement.FrozenNitrogen | BiomeWorldRequirement.CryogenicAir), "Pluto");
		}

		[Test]
		public void ACold_WetWorldWithOpenSeas_IsNotAnIceMoon()
		{
			BiomeWorldConditions cold = ColdEarth;
			Assert.IsTrue(cold.HasLiquidWater, "its warm belt is above freezing: open water");
			Assert.IsFalse(cold.IsIceWorld, "so it is not frozen across the whole world");
			Assert.IsFalse(cold.Meets(BiomeWorldRequirement.IceWorld), "open water means not frozen through");
			Assert.IsFalse(cold.Meets(BiomeWorldRequirement.Cryovolcanic), "and so no cryovolcanism");
			Assert.IsTrue(cold.Meets(BiomeWorldRequirement.RockSurface));
		}

		// ── Liquid water and ice, from the field's own absolute scale ────────

		[Test]
		public void Galris_IsFrozenThrough_AsItsFieldReads()
		{
			// The bug: the conditions read Galris's temperature relative to home (−0.53) and called it
			// a world of open seas, while every point of its field read −1.
			BiomeWorldConditions galris = Galris;
			Assert.LessOrEqual(galris.WarmestTemperature, BiomeWorldConditions.FreezingTemperature, "nothing on it is above freezing");
			Assert.IsFalse(galris.HasLiquidWater);
			Assert.IsTrue(galris.IsIceWorld);
			Assert.IsTrue(galris.IsFrozenThrough);
			Assert.IsFalse(galris.Meets(BiomeWorldRequirement.RockSurface));
		}

		[Test]
		public void LiquidWaterAndIce_AreComplements_OfTheWarmestGround()
		{
			for (float t = -6f; t <= 6f; t += 0.01f)
			{
				var world = new BiomeWorldConditions { Atmosphere = AtmosphereKind.Standard, MeanTemperature = t, Water = 0.5f };
				bool belowBoiling = ClimateModel.ToKelvin(t) < BiomeWorldConditions.LiquidCeilingKelvin(AtmosphereKind.Standard);
				if (belowBoiling)
				{
					Assert.AreNotEqual(world.HasLiquidWater, world.IsIceWorld, $"mean {t:0.00}: liquid somewhere or frozen everywhere, never both nor neither");
				}
				Assert.AreEqual(world.IsIceWorld, world.IsFrozenThrough, $"mean {t:0.00}");
				Assert.AreEqual(world.IsIceWorld, BiomeWorldConditions.IsIceAt(world.Water, world.WarmestTemperature),
					"the whole-world test is the per-point test at the warmest point");
			}
		}

		[Test]
		public void TheWarmestGround_BoundsEveryPointTheFieldPaints()
		{
			// When the world is an ice world, every point the globe bake shades must be ice too.
			PlanetClimateField field = PlanetClimateField.For(null, null);
			float warmest = field.Conditions.WarmestTemperature;
			float excess = field.SubSolarTemperature + PlanetClimateField.RegionalVariation - field.MeanTemperature;
			Assert.AreEqual(BiomeWorldConditions.WarmestExcess, excess, 1e-4f, "the conditions' bound is the field's own construction");
			for (int i = 0; i < 4000; i++)
			{
				UnityEngine.Vector3 d = PlanetSurface.FibonacciDirection(i, 4000);
				double latitude = PlanetClimateField.LatitudeOf(d);
				Assert.LessOrEqual(field.TemperatureAt(latitude, d, 0f), field.MeanTemperature + BiomeWorldConditions.WarmestExcess + 1e-4f);
			}
			Assert.Greater(warmest, 0f, "an Earth-like world has open water");
		}

		[Test]
		public void WaterBoilsOff_AtItsOwnAirsBoilingPoint()
		{
			float standard = BiomeWorldConditions.LiquidCeilingKelvin(AtmosphereKind.Standard);
			float thin = BiomeWorldConditions.LiquidCeilingKelvin(AtmosphereKind.Thin);
			float thick = BiomeWorldConditions.LiquidCeilingKelvin(AtmosphereKind.Thick);
			Assert.That(standard, Is.InRange(360f, 380f), "about 100 °C under a bar");
			Assert.Less(thin, standard, "thinner air boils it cooler");
			Assert.LessOrEqual(thick, (float)AirPhysics.RunawayGreenhouseKelvin, "and never past the runaway greenhouse");
			Assert.IsFalse(new BiomeWorldConditions { Atmosphere = AtmosphereKind.Standard, MeanTemperature = Scale(380.0), Water = 0.7f }.HasLiquidWater,
				"a world above boiling has no seas");
			Assert.IsTrue(new BiomeWorldConditions { Atmosphere = AtmosphereKind.Standard, MeanTemperature = Scale(330.0), Water = 0.7f }.HasLiquidWater);
		}

		[Test]
		public void TheEarthlikeDefault_StandsAtEarthsMean()
		{
			Assert.AreEqual(BiomeWorldConditions.EarthlikeKelvin, ClimateModel.ToKelvin(BiomeWorldConditions.Earthlike.MeanTemperature), 0.01);
			Assert.IsTrue(BiomeWorldConditions.Earthlike.HasLiquidWater);
			Assert.IsFalse(BiomeWorldConditions.Earthlike.IsIceWorld);
		}

		[Test]
		public void Allows_TestsAirWaterAndWorldTogether()
		{
			var vent = ScriptableObject.CreateInstance<BiomeTemplate>();
			try
			{
				vent.Atmosphere = BiomeAtmosphereRequirement.Any;
				vent.RequiresLiquidWater = false;
				vent.Requires = BiomeWorldRequirement.Cryovolcanic;
				Assert.IsTrue(Europa.Allows(vent));
				Assert.IsFalse(BiomeWorldConditions.Earthlike.Allows(vent), "a polar sea reads the same climate and is still not Europa");
				vent.Requires = BiomeWorldRequirement.None;
				Assert.IsTrue(BiomeWorldConditions.Earthlike.Allows(vent), "with no requirement, only air and water decide, as before");
			}
			finally
			{
				Object.DestroyImmediate(vent);
			}
		}

		// ── Molten rock: a magma ocean OR lava lakes (2026-10-02) ──────

		/// <summary>Helis as the project has it: airless, 243 K, dry, kneaded to the full by its parent.</summary>
		private static BiomeWorldConditions Helis => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.None, MeanTemperature = Scale(243.0), Water = 0f, InternalHeat = 1f, TidalHeat = 1f,
		};

		/// <summary>Rheis: an airless planet at 324 K, warm inside from its size (volcanic by that test) but not molten.</summary>
		private static BiomeWorldConditions Rheis => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.None, MeanTemperature = Scale(324.0), Water = 0f, InternalHeat = 0.75f,
		};

		[Test]
		public void MoltenRock_IsWhereTheLavaCodePutsLava()
		{
			Assert.IsTrue(Helis.Meets(BiomeWorldRequirement.MoltenRock), "lava lakes: thin air, unfrozen, heated past the lake threshold");
			Assert.IsTrue(new BiomeWorldConditions { Atmosphere = AtmosphereKind.Thick, MeanTemperature = Scale(SurfaceLiquids.SolidusKelvin + 100.0), Water = 0f, InternalHeat = 0.2f }
				.Meets(BiomeWorldRequirement.MoltenRock), "a magma ocean: past the solidus from starlight alone, whatever its air or interior");

			Assert.IsTrue(Rheis.IsVolcanic, "volcanic by its size");
			Assert.IsFalse(Rheis.Meets(BiomeWorldRequirement.MoltenRock), "and still not molten: warm sunlight does not melt rock");
			Assert.IsTrue(Venus.IsVolcanic);
			Assert.IsFalse(Venus.Meets(BiomeWorldRequirement.MoltenRock), "434 K of long-set basalt under thick air");
			Assert.IsFalse(BiomeWorldConditions.Earthlike.Meets(BiomeWorldRequirement.MoltenRock), "open seas quench it");
			Assert.IsFalse(Europa.Meets(BiomeWorldRequirement.MoltenRock), "an ice shell over a warm interior is cryovolcanism, not lava");
		}

		[Test]
		public void MoltenRock_AgreesWithSurfaceLiquids_AcrossWorlds()
		{
			AtmosphereKind[] airs = { AtmosphereKind.None, AtmosphereKind.Thin, AtmosphereKind.Standard, AtmosphereKind.Thick };
			foreach (AtmosphereKind air in airs)
			{
				foreach (double kelvin in new[] { 60.0, 150.0, 250.0, 300.0, 450.0, 900.0, 1299.0, 1301.0, 2000.0 })
				{
					foreach (float water in new[] { 0f, 0.3f })
					{
						foreach (float heat in new[] { 0.1f, 0.6f, 0.85f, 1f })
						{
							var world = new BiomeWorldConditions { Atmosphere = air, MeanTemperature = Scale(kelvin), Water = water, InternalHeat = heat };
							bool lava = SurfaceLiquids.Decide(world, kelvin) == SurfaceLiquid.Lava;
							Assert.AreEqual(lava, world.HasMoltenRock, $"{air} {kelvin} K water {water} heat {heat}");
							Assert.AreEqual(lava, world.Meets(BiomeWorldRequirement.MoltenRock));
						}
					}
				}
			}
		}

		[Test]
		public void MoltenSurface_IsTabledOnMoltenRock_AndOnlyThere()
		{
			BiomeSpecTable.Entry molten = Array.Find(BiomeSpecTable.Entries, e => e.Name == "Molten Surface");
			Assert.AreEqual(BiomeWorldRequirement.MoltenRock, molten.Requires);
			Assert.IsTrue(Helis.Allows(molten.Atmosphere, molten.RequiresLiquidWater, molten.Requires));
			Assert.IsFalse(Rheis.Allows(molten.Atmosphere, molten.RequiresLiquidWater, molten.Requires), "Rheis keeps its regolith");
			Assert.IsFalse(Venus.Allows(molten.Atmosphere, molten.RequiresLiquidWater, molten.Requires), "Venus keeps its greenhouse plains");
			Assert.AreEqual(-1f, molten.MinTemperature, "the heat is the rock's, not the sunlight's: an Io reads −1");
		}

		// ── Ice Shelf ────────────────────────────────────────────────

		[Test]
		public void IceShelf_IsAFrozenThroughWorldsShore_AndNoOneElses()
		{
			BiomeSpecTable.Entry shelf = Array.Find(BiomeSpecTable.Entries, e => e.Name == "Ice Shelf");
			Assert.AreEqual("Ice Shelf", shelf.Name, "tabled");
			Assert.AreEqual(3, shelf.ElevationTier, "the raised frozen sea stands a few metres above the datum: the shore tier");
			Assert.IsTrue(shelf.Requires.HasFlag(BiomeWorldRequirement.IceWorld));
			Assert.IsTrue(Galris.Allows(shelf.Atmosphere, shelf.RequiresLiquidWater, shelf.Requires));
			Assert.IsTrue(Europa.Allows(shelf.Atmosphere, shelf.RequiresLiquidWater, shelf.Requires));
			Assert.IsFalse(BiomeWorldConditions.Earthlike.Allows(shelf.Atmosphere, shelf.RequiresLiquidWater, shelf.Requires));
			Assert.IsFalse(ColdEarth.Allows(shelf.Atmosphere, shelf.RequiresLiquidWater, shelf.Requires), "a cold world with open seas keeps Rocky Coast");

			// Galris's shelf reads −1, −1; Rocky Coast is the only other shore biome its world allows.
			BiomeSpecTable.Entry coast = Array.Find(BiomeSpecTable.Entries, e => e.Name == "Rocky Coast");
			Assert.IsTrue(Galris.Allows(coast.Atmosphere, coast.RequiresLiquidWater, coast.Requires));
			Assert.Greater(shelf.SelectionWeight, coast.SelectionWeight, "at the corner reading neither is central, so the weight decides: the shelf");
			Assert.That(-1f, Is.InRange(shelf.MinTemperature, shelf.MaxTemperature));
		}

		[Test]
		public void IceShelf_CanBeCreated_WithAnIdentityOfItsOwn()
		{
			Assert.IsTrue(BiomeSpecTable.TryGetIdentity("Ice Shelf", out BiomeSpecTable.Identity identity));
			Assert.AreEqual("Ice Shelf", identity.DisplayName);
			Assert.IsFalse(string.IsNullOrWhiteSpace(identity.Description));
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate), new[] { BiomeSpecTable.Folder }))
			{
				var biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (biome == null || biome.name == identity.Name)
				{
					continue;
				}
				Assert.AreNotEqual(identity.BiomeColorId, biome.BiomeColorId, $"{biome.name} already has Ice Shelf's map colour");
			}
			foreach (BiomeSpecTable.Identity created in BiomeSpecTable.NewBiomes)
			{
				Assert.IsTrue(Array.Exists(BiomeSpecTable.Entries, e => e.Name == created.Name), $"{created.Name} would be created with no envelope to write");
			}
		}

		[Test]
		public void CreatingABiome_WritesItsIdentity_ThenTheEntry_AndASecondPassWritesNothing()
		{
			const string folder = "Assets/__BiomeSpecCreateTest";
			AssetDatabase.CreateFolder("Assets", "__BiomeSpecCreateTest");
			try
			{
				// A name of its own, so the throwaway asset never stands in the registry for the real Ice Shelf.
				Assert.IsTrue(BiomeSpecTable.TryGetIdentity("Ice Shelf", out BiomeSpecTable.Identity shelf));
				var identity = new BiomeSpecTable.Identity("Biome Spec Create Test", shelf.DisplayName, shelf.Description, shelf.BiomeColorId, shelf.GizmoColor);
				BiomeSpecTable.Entry entry = Array.Find(BiomeSpecTable.Entries, e => e.Name == "Ice Shelf");
				string path = $"{folder}/{identity.Name}.asset";

				BiomeTemplate biome = BiomeSpecTable.Create(identity, path);
				Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<BiomeTemplate>(path), "the asset exists on disk");
				Assert.AreEqual(identity.DisplayName, biome.DisplayName);
				Assert.AreEqual(identity.Description, biome.Description);
				Assert.AreEqual(identity.BiomeColorId, biome.BiomeColorId);
				Assert.AreEqual(identity.GizmoColor, biome.GizmoColor);

				var log = new StringBuilder();
				Assert.Greater(BiomeSpecTable.ApplyTo(biome, entry, log), 0);
				Assert.AreEqual(0, BiomeSpecTable.Differences(biome, entry), "applied");
				Assert.AreEqual(BiomeWorldRequirement.IceWorld, biome.Requires);
			}
			finally
			{
				AssetDatabase.DeleteAsset(folder);
			}
		}

		// ── The spec table ───────────────────────────────────────────

		[Test]
		public void EveryTabledBiome_HasItsAsset_OrAnIdentityToCreateItFrom()
		{
			// A biome new to the table (Ice Shelf) has no asset until "Apply biome envelopes and world
			// requirements" creates it; anything else missing is a misspelt or deleted asset.
			foreach (BiomeSpecTable.Entry entry in BiomeSpecTable.Entries)
			{
				bool exists = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(entry.AssetPath) != null;
				Assert.IsTrue(exists || BiomeSpecTable.TryGetIdentity(entry.Name, out _), $"{entry.Name}: no asset at {entry.AssetPath} and no identity to create one");
			}
		}

		[Test]
		public void TheTable_NamesEachBiomeOnce_AndKeepsItsValuesInRange()
		{
			var seen = new System.Collections.Generic.HashSet<string>();
			foreach (BiomeSpecTable.Entry e in BiomeSpecTable.Entries)
			{
				Assert.IsTrue(seen.Add(e.Name), $"{e.Name} is tabled twice");
				Assert.That(e.ElevationTier, Is.InRange(0, 9), e.Name);
				Assert.LessOrEqual(e.MinHeight, e.MaxHeight, e.Name);
				Assert.LessOrEqual(e.MinTemperature, e.MaxTemperature, e.Name);
				Assert.LessOrEqual(e.MinHumidity, e.MaxHumidity, e.Name);
				Assert.That(e.MinTemperature, Is.InRange(-1f, 1f), e.Name);
				Assert.That(e.MaxTemperature, Is.InRange(-1f, 1f), e.Name);
				Assert.That(e.MinHumidity, Is.InRange(-1f, 1f), e.Name);
				Assert.That(e.MaxHumidity, Is.InRange(-1f, 1f), e.Name);
				Assert.GreaterOrEqual(e.SelectionWeight, 0f, e.Name);
				Assert.AreNotEqual(BiomeAtmosphereRequirement.None, e.Atmosphere, $"{e.Name} could exist under no air at all");
			}
		}

		[Test]
		public void EveryTabledAlien_IsKeptOffTheEarthlikeWorld()
		{
			int found = 0;
			foreach (BiomeSpecTable.Entry e in BiomeSpecTable.Entries)
			{
				if (Array.IndexOf(Aliens, e.Name) < 0)
				{
					continue;
				}
				found++;
				Assert.IsFalse(BiomeWorldConditions.Earthlike.Allows(e.Atmosphere, e.RequiresLiquidWater, e.Requires), $"{e.Name} is allowed on an Earth-like world");
			}
			Assert.AreEqual(Aliens.Length, found, "every alien biome is tabled");
		}

		[Test]
		public void EveryTabledEarthBiome_IsAllowedOnTheEarthlikeWorld()
		{
			foreach (BiomeSpecTable.Entry e in BiomeSpecTable.Entries)
			{
				if (Array.IndexOf(Aliens, e.Name) >= 0 || e.SelectionWeight <= 0f)
				{
					continue;
				}
				Assert.IsTrue(BiomeWorldConditions.Earthlike.Allows(e.Atmosphere, e.RequiresLiquidWater, e.Requires), $"{e.Name} is kept off the world it belongs to");
			}
		}

		[Test]
		public void ApplyingAnEntry_WritesEveryField_AndASecondRunWritesNothing()
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			try
			{
				BiomeSpecTable.Entry entry = Array.Find(BiomeSpecTable.Entries, e => e.Name == "Cryovolcanic Plain");
				Assert.IsNotNull(entry.Name);
				var log = new StringBuilder();
				int first = BiomeSpecTable.ApplyTo(biome, entry, log);
				Assert.Greater(first, 0, "a fresh template differs from the table");
				StringAssert.Contains("Cryovolcanic Plain.Requires: None → Cryovolcanic", log.ToString(), "each change is logged before → after");

				Assert.AreEqual(entry.ElevationTier, biome.ElevationTier);
				Assert.AreEqual(entry.MinHeight, biome.MinHeight);
				Assert.AreEqual(entry.MaxHeight, biome.MaxHeight);
				Assert.AreEqual(entry.MinTemperature, biome.MinTemperature);
				Assert.AreEqual(entry.MaxTemperature, biome.MaxTemperature);
				Assert.AreEqual(entry.MinHumidity, biome.MinHumidity);
				Assert.AreEqual(entry.MaxHumidity, biome.MaxHumidity);
				Assert.AreEqual(entry.SelectionWeight, biome.SelectionWeight);
				Assert.AreEqual(entry.Atmosphere, biome.Atmosphere);
				Assert.AreEqual(entry.RequiresLiquidWater, biome.RequiresLiquidWater);
				Assert.AreEqual(entry.Requires, biome.Requires);

				var second = new StringBuilder();
				Assert.AreEqual(0, BiomeSpecTable.ApplyTo(biome, entry, second), "already applied: nothing to write");
				Assert.AreEqual(0, second.Length, "and nothing logged");
				Assert.AreEqual(0, BiomeSpecTable.Differences(biome, entry));
			}
			finally
			{
				Object.DestroyImmediate(biome);
			}
		}
	}
}
