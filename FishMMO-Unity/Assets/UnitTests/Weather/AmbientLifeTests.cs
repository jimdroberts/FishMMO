using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The land's ambient life (AmbientGating, AmbientLifeMotion, AmbientCreatureMeshes): which creatures belong to a
	/// biome, a climate, an hour and a weather; when one takes fright and which way it runs; the same placement for
	/// every player; nothing walking on water.
	/// </summary>
	[TestFixture]
	public class AmbientLifeTests
	{
		private const uint Seed = 0xA11CEu;
		private static readonly AmbientCreatureKind[] Kinds = AmbientLifeCatalogue.Defaults();

		private static AmbientCreatureKind Kind(string name)
		{
			foreach (AmbientCreatureKind kind in Kinds)
			{
				if (kind.Name == name)
				{
					return kind;
				}
			}
			Assert.Fail($"no kind '{name}' in the catalogue");
			return null;
		}

		private static float C(float celsius) => AmbientLifeCatalogue.Celsius(celsius);

		private static AmbientConditions At(double localTime01, bool daylight, float celsius, float rain = 0f, float storm = 0f, float cloud = 0f, float summer = 0.5f)
		{
			AmbientConditions.Daylight(localTime01, daylight, out float light, out float twilight, out bool evening);
			return new AmbientConditions { Light = light, Twilight = twilight, Evening = evening, Temperature = C(celsius), Rain = rain, Storm = storm, Cloud = cloud, Summer = summer };
		}

		/// <summary>
		/// A world: a lake west of x = -100 (ground 2 m under its surface at 0), a dry rising plain in the middle, a
		/// beach from x = 250 to 300 dropping into the sea (mean level 0) beyond; nothing past ±1000 m.
		/// </summary>
		private sealed class FakeWorld : IAmbientWorld
		{
			public AmbientHabitat Land = AmbientHabitat.Grassland | AmbientHabitat.Woodland | AmbientHabitat.Farmland;
			public float Temperature = C(16f);
			public bool HasSea = true;
			public AmbientWorld LifeWorld = AmbientWorld.Earthly;
			/// <summary>The biome looks everywhere on it, or none (the kinds' own colours).</summary>
			public BiomeLooks.Look Looks;

			public AmbientWorld World => LifeWorld;

			public bool LookAt(Vector3 position, out BiomeLooks.Look look)
			{
				look = Looks;
				return look != null;
			}

			public bool TryGround(float x, float z, out float y)
			{
				if (Mathf.Abs(x) > 1000f || Mathf.Abs(z) > 1000f)
				{
					y = float.PositiveInfinity;
					return true;
				}
				if (x < -100f)
				{
					y = -2f;
				}
				else if (x > 300f)
				{
					y = -10f;
				}
				else if (x > 250f)
				{
					y = Mathf.Lerp(3f, -0.5f, (x - 250f) / 50f);
				}
				else
				{
					y = 5f + 0.01f * x + Mathf.Sin(z * 0.05f);
				}
				return true;
			}

			public float WaterAt(float x, float z, out bool sea)
			{
				if (x < -100f)
				{
					sea = false;
					return 0f;
				}
				sea = HasSea;
				return HasSea ? 0f : float.NegativeInfinity;
			}

			public float LiveWaterAt(float x, float z) => WaterAt(x, z, out _);

			public void Habitat(Vector3 position, out AmbientHabitat habitat, out float temperature)
			{
				temperature = Temperature;
				habitat = position.x < -100f ? AmbientHabitat.FreshWater | AmbientHabitat.Wetland : position.x > 300f ? AmbientHabitat.Sea : position.x > 250f ? AmbientHabitat.Coast : Land;
			}
		}

		// ── Habitat ───────────────────────────────────────────────────

		[Test]
		public void HabitatOf_ReadsTheBiomesByName()
		{
			Assert.That(AmbientGating.HabitatOf("Forest") & AmbientHabitat.Forest, Is.Not.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOf("Taiga") & AmbientHabitat.Forest, Is.Not.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOf("Ice Sheet"), Is.EqualTo(AmbientHabitat.Ice));
			Assert.That(AmbientGating.HabitatOf("Glacier"), Is.EqualTo(AmbientHabitat.Ice));
			Assert.That(AmbientGating.HabitatOf("Coastal Water"), Is.EqualTo(AmbientHabitat.Sea), "coastal water is the sea, not the shore");
			Assert.That(AmbientGating.HabitatOf("Coral Reef"), Is.EqualTo(AmbientHabitat.Sea));
			Assert.That(AmbientGating.HabitatOf("Rocky Coast") & AmbientHabitat.Coast, Is.Not.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOf("High Desert") & AmbientHabitat.Desert, Is.Not.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOf("Peat Bog") & AmbientHabitat.Wetland, Is.Not.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOf("Methane Lake"), Is.EqualTo(AmbientHabitat.Alien), "an alien lake is alien ground, not fresh water");
			Assert.That(AmbientGating.HabitatOf("Regolith Plain"), Is.EqualTo(AmbientHabitat.Alien));
			Assert.That(AmbientGating.HabitatOf("Cryovolcanic Plain"), Is.EqualTo(AmbientHabitat.Alien));
			Assert.That(AmbientGating.HabitatOf("Tholin Plain"), Is.EqualTo(AmbientHabitat.Alien));
			Assert.That(AmbientGating.HabitatOf("Wasteland"), Is.EqualTo(AmbientHabitat.Barren));
			Assert.That(AmbientGating.HabitatOf("Molten Surface"), Is.EqualTo(AmbientHabitat.Barren));
			Assert.That(AmbientGating.HabitatOf("Something Unheard Of"), Is.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOf(null), Is.EqualTo(AmbientHabitat.None));
		}

		[Test]
		public void HabitatOfBiome_OtherWorldsGroundIsAlienNotBarren()
		{
			const BiomeAtmosphereRequirement otherAir = BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin;
			// The biomes that only form under thin or no air (their assets' Atmosphere is 3): the thin and frozen airs' country.
			foreach (string name in new[] { "Dust Sea", "Regolith Plain", "Cryovolcanic Plain", "Nitrogen Ice Field", "Impact Basin", "Rille" })
			{
				AmbientHabitat habitat = AmbientGating.HabitatOfBiome(name, name, otherAir);
				Assert.That(habitat & AmbientHabitat.Alien, Is.Not.EqualTo(AmbientHabitat.None), $"{name} is alien ground");
				Assert.That(habitat & AmbientHabitat.Barren, Is.EqualTo(AmbientHabitat.None), $"{name} is not lifeless");
			}
			Assert.That(AmbientGating.HabitatOfBiome("Ice Geyser Field", "Ice Geyser Field", otherAir), Is.EqualTo(AmbientHabitat.Ice | AmbientHabitat.Alien), "ice, and alien ground");
			Assert.That(AmbientGating.HabitatOfBiome("Unnamed Thin Plain", null, BiomeAtmosphereRequirement.Thin), Is.EqualTo(AmbientHabitat.Alien), "a nameless thin-air plain");
			// Lifeless names stay lifeless on any world, whatever air they need.
			foreach (string name in new[] { "Radiation Plain", "Lava Tube" })
			{
				Assert.That(AmbientGating.HabitatOfBiome(name, name, otherAir), Is.EqualTo(AmbientHabitat.Barren), name);
			}
			Assert.That(AmbientGating.HabitatOfBiome("Sulphuric Cloud Deck", "Sulphuric Cloud Deck", BiomeAtmosphereRequirement.Thick), Is.EqualTo(AmbientHabitat.Barren));
			// Home-world biomes are read as before; one no keyword matches is left to the climate.
			Assert.That(AmbientGating.HabitatOfBiome("Forest", "Forest", BiomeAtmosphereRequirement.Standard | BiomeAtmosphereRequirement.Thick) & AmbientHabitat.Forest, Is.Not.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOfBiome("Forest", "Forest", BiomeAtmosphereRequirement.Any) & AmbientHabitat.Alien, Is.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOfBiome("Something Unheard Of", "Also Unheard Of", BiomeAtmosphereRequirement.Any), Is.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOfBiome("Rocky", null, BiomeAtmosphereRequirement.Any) & AmbientHabitat.Alien, Is.EqualTo(AmbientHabitat.None));

			// And the creatures of thin and frozen air live there now.
			AmbientHabitat dustSea = AmbientGating.HabitatOfBiome("Dust Sea", "Dust Sea", otherAir);
			Assert.That(AmbientGating.HabitatWeight(Kind("Dust skitters"), dustSea, -1f, false, false, false), Is.GreaterThan(0f), "dust skitters on a dust sea");
			Assert.That(AmbientGating.HabitatWeight(Kind("Sand drakelings"), AmbientGating.HabitatOfBiome("Regolith Plain", "Regolith Plain", otherAir), -1f, false, false, false), Is.GreaterThan(0f));
			Assert.That(AmbientGating.HabitatWeight(Kind("Rime beetles"), AmbientGating.HabitatOfBiome("Ice Geyser Field", "Ice Geyser Field", otherAir), -1f, false, false, false), Is.GreaterThan(0f), "rime beetles on an ice geyser field");
			Assert.That(AmbientGating.HabitatWeight(Kind("Rime beetles"), AmbientGating.HabitatOfBiome("Nitrogen Ice Field", "Nitrogen Ice Field", otherAir), -1f, false, false, false), Is.GreaterThan(0f));
			// Never the home world's.
			Assert.That(AmbientGating.HabitatWeight(Kind("Rabbit"), dustSea, C(15f), false, false, false), Is.Zero, "no rabbits on a dust sea");
		}

		[Test]
		public void Habitat_IceAndBarrenHoldAlmostNothing()
		{
			foreach (AmbientCreatureKind kind in Kinds)
			{
				if (kind.Worlds != AmbientWorld.Earthly)
				{
					// Other worlds' creatures: never on barren ground, but the frozen worlds' are at home on the ice.
					Assert.That(AmbientGating.HabitatWeight(kind, AmbientHabitat.Barren, C(15f), true, true, true), Is.Zero, $"{kind.Name} on barren ground");
					continue;
				}
				Assert.That(AmbientGating.HabitatWeight(kind, AmbientHabitat.Barren, C(15f), true, true, true), Is.Zero, $"{kind.Name} on barren ground");
				float ice = AmbientGating.HabitatWeight(kind, AmbientHabitat.Ice, C(-15f), true, false, false);
				if (kind.Name == "Gulls")
				{
					Assert.That(ice, Is.GreaterThan(0f).And.LessThan(0.5f), "a sprinkling of gulls along an icy coast");
				}
				else
				{
					Assert.That(ice, Is.Zero, $"{kind.Name} on the ice");
				}
			}
			// Not even gulls on an ice sheet far from the sea.
			Assert.That(AmbientGating.HabitatWeight(Kind("Gulls"), AmbientHabitat.Ice, C(-15f), false, false, false), Is.Zero);
		}

		[Test]
		public void Habitat_EachKindInItsOwnCountry()
		{
			Assert.That(AmbientGating.HabitatWeight(Kind("Lizard"), AmbientHabitat.Desert, C(30f), false, false, false), Is.GreaterThan(1f), "lizards love the desert");
			Assert.That(AmbientGating.HabitatWeight(Kind("Lizard"), AmbientHabitat.Desert, C(5f), false, false, false), Is.Zero, "but not a cold one");
			Assert.That(AmbientGating.HabitatWeight(Kind("Frog"), AmbientHabitat.Desert, C(30f), false, false, false), Is.Zero, "no frogs in the desert");
			Assert.That(AmbientGating.HabitatWeight(Kind("Frog"), AmbientHabitat.Wetland, C(18f), false, false, false), Is.Zero, "frogs need water near");
			Assert.That(AmbientGating.HabitatWeight(Kind("Frog"), AmbientHabitat.Wetland, C(18f), false, true, false), Is.GreaterThan(1f));
			Assert.That(AmbientGating.HabitatWeight(Kind("Squirrel"), AmbientHabitat.Forest, C(12f), false, false, false), Is.Zero, "squirrels need trees");
			Assert.That(AmbientGating.HabitatWeight(Kind("Squirrel"), AmbientHabitat.Forest, C(12f), false, false, true), Is.GreaterThan(0f));
			Assert.That(AmbientGating.HabitatWeight(Kind("Shore crab"), AmbientHabitat.Coast, C(20f), true, false, false), Is.GreaterThan(0f));
			Assert.That(AmbientGating.HabitatWeight(Kind("Shore crab"), AmbientHabitat.Grassland, C(20f), true, false, false), Is.Zero);
			Assert.That(AmbientGating.HabitatWeight(Kind("Vulture"), AmbientHabitat.Desert, C(32f), false, false, false), Is.GreaterThan(0f));
			Assert.That(AmbientGating.HabitatWeight(Kind("Vulture"), AmbientHabitat.Grassland, C(2f), false, false, false), Is.Zero, "no vultures in the cold");
			// Over open sea only seabirds.
			foreach (AmbientCreatureKind kind in Kinds)
			{
				float sea = AmbientGating.HabitatWeight(kind, AmbientHabitat.Sea, C(15f), true, false, false);
				Assert.That(sea > 0f, Is.EqualTo(kind.Shape == AmbientShape.Gull), $"{kind.Name} over open sea");
			}
		}

		// ── Worlds ────────────────────────────────────────────────────

		private static BiomeWorldConditions Air(AtmosphereKind air, Condensate condensate = Condensate.Water)
		{
			BiomeWorldConditions world = BiomeWorldConditions.Earthlike;
			world.Atmosphere = air;
			world.Condensate = condensate;
			return world;
		}

		[Test]
		public void WorldOf_FollowsTheAir()
		{
			Assert.That(AmbientGating.WorldOf(BiomeWorldConditions.Earthlike), Is.EqualTo(AmbientWorld.Earthly));
			Assert.That(AmbientGating.WorldOf(Air(AtmosphereKind.Thick)), Is.EqualTo(AmbientWorld.Earthly));
			Assert.That(AmbientGating.WorldOf(Air(AtmosphereKind.None)), Is.EqualTo(AmbientWorld.None), "no air, no life");
			Assert.That(AmbientGating.WorldOf(Air(AtmosphereKind.Thin)), Is.EqualTo(AmbientWorld.ThinAir));
			Assert.That(AmbientGating.WorldOf(Air(AtmosphereKind.Thick, Condensate.Methane)), Is.EqualTo(AmbientWorld.Cryogenic));
			Assert.That(AmbientGating.WorldOf(Air(AtmosphereKind.Standard, Condensate.Nitrogen)), Is.EqualTo(AmbientWorld.Cryogenic));
			Assert.That(AmbientGating.WorldOf(Air(AtmosphereKind.Standard, Condensate.Ammonia)), Is.EqualTo(AmbientWorld.Cryogenic));
			Assert.That(AmbientGating.WorldOf(Air(AtmosphereKind.Thin, Condensate.Methane)), Is.EqualTo(AmbientWorld.ThinAir), "thin air wins: no wing beats in it");
			Assert.That(AmbientGating.WorldOf(Air(AtmosphereKind.Thick, Condensate.SulphuricAcid)), Is.EqualTo(AmbientWorld.None), "nothing under a runaway greenhouse");
			BiomeWorldConditions frozen = BiomeWorldConditions.Earthlike;
			frozen.MeanTemperature = -2.5f;
			Assert.That(AmbientGating.WorldOf(frozen), Is.EqualTo(AmbientWorld.Earthly | AmbientWorld.Cryogenic), "home air on a world that never thaws: the frozen air's creatures too");
		}

		[Test]
		public void Kinds_EachWorldHasItsOwn()
		{
			int earthly = 0, thin = 0, frozen = 0;
			foreach (AmbientCreatureKind kind in Kinds)
			{
				Assert.That(kind.Worlds, Is.Not.EqualTo(AmbientWorld.None), $"{kind.Name} lives nowhere");
				Assert.That(AmbientGating.LivesOn(kind, AmbientWorld.None), Is.False, $"{kind.Name} on an airless world");
				bool home = AmbientGating.LivesOn(kind, AmbientWorld.Earthly);
				earthly += home ? 1 : 0;
				thin += AmbientGating.LivesOn(kind, AmbientWorld.ThinAir) ? 1 : 0;
				frozen += AmbientGating.LivesOn(kind, AmbientWorld.Cryogenic) ? 1 : 0;
				float alien = AmbientGating.HabitatWeight(kind, AmbientHabitat.Alien, -1f, false, false, false);
				Assert.That(alien > 0f, Is.EqualTo(!home), $"{kind.Name} on alien ground");
				if (AmbientGating.LivesOn(kind, AmbientWorld.ThinAir))
				{
					Assert.That(kind.Flies, Is.False, $"{kind.Name} flies in air too thin to beat a wing in");
				}
			}
			Assert.That(earthly, Is.EqualTo(62), "forty-four of the land and air, eighteen of the caves (ten of them the light-makers, 2026-10-20)");
			Assert.That(thin, Is.GreaterThanOrEqualTo(3));
			Assert.That(frozen, Is.GreaterThanOrEqualTo(3));
		}

		[Test]
		public void Placement_EachWorldGetsOnlyItsOwnLife()
		{
			List<AmbientGroup> airless = PlaceArea(new FakeWorld { LifeWorld = AmbientWorld.None, Land = AmbientHabitat.Mountain | AmbientHabitat.Volcanic, HasSea = false }, false);
			Assert.That(airless, Is.Empty, "nothing on a world without air, even on ground named for mountains");
			foreach (AmbientWorld world in new[] { AmbientWorld.ThinAir, AmbientWorld.Cryogenic, AmbientWorld.Earthly })
			{
				var fake = new FakeWorld { LifeWorld = world, HasSea = false };
				if (world != AmbientWorld.Earthly)
				{
					fake.Land = AmbientHabitat.Alien | AmbientHabitat.Mountain | AmbientHabitat.Ice;
					fake.Temperature = -1f;
				}
				List<AmbientGroup> groups = PlaceArea(fake, false);
				Assert.That(groups.Count, Is.GreaterThan(5), $"{world} should hold some life");
				foreach (AmbientGroup g in groups)
				{
					Assert.That(AmbientGating.LivesOn(Kinds[g.Kind], world), Is.True, $"{Kinds[g.Kind].Name} on a {world} world");
				}
			}
		}

		// ── The moment ────────────────────────────────────────────────

		[Test]
		public void Daylight_FollowsTheLocalSolarTime()
		{
			AmbientConditions.Daylight(0.5, true, out float light, out float twilight, out _);
			Assert.That(light, Is.EqualTo(1f).Within(1e-3f));
			Assert.That(twilight, Is.LessThan(0.01f));
			AmbientConditions.Daylight(0.0, false, out light, out twilight, out _);
			Assert.That(light, Is.LessThan(0.01f));
			Assert.That(twilight, Is.LessThan(0.01f));
			AmbientConditions.Daylight(0.75, true, out light, out twilight, out bool evening);
			Assert.That(twilight, Is.GreaterThan(0.9f), "sunset");
			Assert.That(evening, Is.True);
			AmbientConditions.Daylight(0.25, true, out _, out _, out evening);
			Assert.That(evening, Is.False, "sunrise is not the evening");
		}

		[Test]
		public void Bats_ComeOutAtDuskOnWarmDryNights()
		{
			AmbientCreatureKind bats = Kind("Bats");
			Assert.That(AmbientGating.Activity(bats, At(0.5, true, 20f)), Is.Zero, "none at noon");
			float dusk = AmbientGating.Activity(bats, At(0.79, false, 18f));
			float night = AmbientGating.Activity(bats, At(0.95, false, 15f));
			Assert.That(dusk, Is.GreaterThan(0.5f), "out at dusk");
			Assert.That(night, Is.GreaterThan(0.3f), "and through the night");
			Assert.That(dusk, Is.GreaterThanOrEqualTo(night), "busiest at dusk");
			Assert.That(AmbientGating.Activity(bats, At(0.85, false, 3f)), Is.Zero, "in the roost below 5 °C");
			Assert.That(AmbientGating.Activity(bats, At(0.85, false, 18f, rain: 0.3f)), Is.Zero, "and in the rain");
			Assert.That(AmbientGating.Activity(bats, At(0.85, false, 18f, summer: -0.9f)), Is.Zero, "hibernating in deep winter");
		}

		[Test]
		public void NightCritters_KeepToTheNight()
		{
			// 2026-10-14: the night's animals are the night's. A third of the rats and mice used to be out by day.
			AmbientConditions mildNoon = At(0.5, true, 18f);
			AmbientConditions warmNight = At(0.0, false, 20f);
			float day = AmbientGating.Activity(Kind("Rat"), mildNoon);
			Assert.That(day, Is.EqualTo(AmbientGating.DaytimeNocturnalShare).Within(0.02f), "the odd rat by day");
			Assert.That(AmbientGating.Activity(Kind("Rat"), warmNight), Is.GreaterThan(0.8f), "the night is theirs");
			foreach (string name in new[] { "Mouse", "Toad", "Gecko", "Ground beetles" })
			{
				Assert.That(AmbientGating.Activity(Kind(name), mildNoon), Is.LessThan(0.08f), $"{name} by day");
				Assert.That(AmbientGating.Activity(Kind(name), warmNight), Is.GreaterThan(0.5f), $"{name} at night");
			}
			// Nothing of the night's that flies is about by day at all.
			foreach (AmbientCreatureKind kind in Kinds)
			{
				if (kind.Activity == AmbientActivity.Nocturnal && kind.Flies)
				{
					Assert.That(AmbientGating.Activity(kind, mildNoon), Is.Zero, kind.Name);
				}
			}
		}

		[Test]
		public void HalfLightAnimals_LieUpThroughTheDay()
		{
			AmbientCreatureKind rabbit = Kind("Rabbit");
			float noon = AmbientGating.Activity(rabbit, At(0.5, true, 16f));
			float dusk = AmbientGating.Activity(rabbit, At(0.76, true, 14f));
			float night = AmbientGating.Activity(rabbit, At(0.0, false, 12f));
			Assert.That(noon, Is.LessThan(0.1f), "few out at noon");
			Assert.That(dusk, Is.GreaterThan(0.6f), "out at dusk");
			Assert.That(night, Is.InRange(0.25f, 0.6f), "some through the night");
		}

		// ── Swarms ────────────────────────────────────────────────────

		[Test]
		public void Swarms_KeepToTheirOwnHours()
		{
			AmbientConditions sunnyNoon = At(0.5, true, 24f);
			AmbientConditions warmDusk = At(0.79, false, 21f);
			AmbientConditions warmNight = At(0.0, false, 20f);
			Assert.That(AmbientGating.Activity(Kind("Firefly swarm"), warmNight), Is.GreaterThan(0.8f), "fireflies on a warm summer night");
			Assert.That(AmbientGating.Activity(Kind("Firefly swarm"), sunnyNoon), Is.Zero, "and never by day");
			Assert.That(AmbientGating.Activity(Kind("Firefly swarm"), At(0.0, false, 20f, summer: -0.9f)), Is.Zero, "nor in winter");
			Assert.That(AmbientGating.Activity(Kind("Mosquito cloud"), warmDusk), Is.GreaterThan(0.6f), "mosquitoes at dusk");
			Assert.That(AmbientGating.Activity(Kind("Mosquito cloud"), sunnyNoon), Is.Zero);
			Assert.That(AmbientGating.Activity(Kind("Midge column"), At(0.76, true, 16f)), Is.GreaterThan(AmbientGating.Activity(Kind("Midge column"), At(0.5, true, 16f))),
				"midges thickest in the half-light");
			Assert.That(AmbientGating.Activity(Kind("Fly swarm"), sunnyNoon), Is.GreaterThan(0.6f), "flies in the heat of the day");
			Assert.That(AmbientGating.Activity(Kind("Fly swarm"), warmNight), Is.Zero);
			Assert.That(AmbientGating.Activity(Kind("Bees"), sunnyNoon), Is.GreaterThan(0.6f), "bees on a sunny day");
			Assert.That(AmbientGating.Activity(Kind("Bees"), At(0.5, true, 24f, cloud: 1f)), Is.GreaterThan(0.3f), "under cloud too when it is warm");
			Assert.That(AmbientGating.Activity(Kind("Bees"), At(0.5, true, 24f, rain: 0.2f)), Is.Zero, "but not in the rain");
			Assert.That(AmbientGating.Activity(Kind("Locust swarm"), At(0.5, true, 15f)), Is.Zero, "locusts only in the heat");
		}

		[Test]
		public void Swarms_AnOrdinaryDayHasItsBugs()
		{
			// 2026-10-14: a mild drizzly noon (12 °C, rain 0.11) had no insects about at all.
			AmbientConditions mildDrizzle = At(0.5, true, 12f, rain: 0.11f);
			Assert.That(AmbientGating.Activity(Kind("Gnat cloud"), mildDrizzle), Is.GreaterThan(0.5f), "gnats in the drizzle");
			Assert.That(AmbientGating.Activity(Kind("Fly swarm"), mildDrizzle), Is.GreaterThan(0.2f), "and flies");
			Assert.That(AmbientGating.Activity(Kind("Midge column"), At(0.77, true, 12f, rain: 0.11f)), Is.GreaterThan(0.5f), "midges at a damp dusk");
			Assert.That(AmbientGating.Activity(Kind("Gnat cloud"), At(0.5, true, 12f, rain: 0.5f)), Is.Zero, "a downpour clears them");
		}

		[Test]
		public void Swarms_BreakUpInABreeze()
		{
			AmbientCreatureKind midges = Kind("Midge column");
			AmbientConditions still = At(0.76, true, 16f);
			AmbientConditions breeze = still;
			breeze.Wind = 0.3f; // 9 m/s
			Assert.That(AmbientGating.Activity(midges, still), Is.GreaterThan(0.5f));
			Assert.That(AmbientGating.Activity(midges, breeze), Is.Zero, "a midge column does not hold in a breeze");
			Assert.That(AmbientGating.Activity(Kind("Rooks"), breeze), Is.GreaterThan(0f), "that a crow flies through");
		}

		[Test]
		public void Swarms_AreOneCloudOverTheirGroundOrWater()
		{
			var world = new FakeWorld();
			foreach (string name in new[] { "Midge column", "Mosquito cloud", "Fly swarm", "Firefly swarm", "Bees", "Locust swarm" })
			{
				AmbientCreatureKind kind = Kind(name);
				int index = System.Array.IndexOf(Kinds, kind);
				foreach (float x in new[] { 40f, -300f })
				{
					var group = new AmbientGroup { Kind = index, Seed = 777u, Home = new Vector3(x, 0f, 20f), Radius = kind.Roam.x, Altitude = kind.Altitude.x,
						Episode = 40.0, Count = kind.GroupSize.x, Length = kind.Length.x };
					var into = new List<AmbientInstance>();
					AmbientLifeMotion.Frame frame = FrameAt(123456.7, world, new AmbientMemberState[0], new AmbientThreats());
					AmbientLifeMotion.Evaluate(group, kind, ref frame, into);
					Assert.That(into.Count, Is.EqualTo(1), $"{name}: one instance is the whole cloud");
					AmbientInstance cloud = into[0];
					Vector3 at = cloud.Matrix.GetColumn(3);
					world.TryGround(at.x, at.z, out float ground);
					float surface = Mathf.Max(ground, world.WaterAt(at.x, at.z, out _));
					Assert.That(at.y - surface, Is.InRange(kind.Altitude.x - 0.2f, kind.Altitude.y + 0.2f), $"{name} hangs at its height over {(x < 0f ? "the lake" : "the plain")}");
					Assert.That(cloud.Matrix.lossyScale.x, Is.InRange(kind.Spread * 0.79f, kind.Spread * 1.21f), $"{name} scaled to its cloud");
					Assert.That(cloud.Anim.x, Is.InRange(0f, (float)AmbientLifeMotion.SwarmClockWrap), $"{name}: the shader's clock is wound back");
					Assert.That(cloud.Anim.y, Is.InRange(0.01f, 1f), $"{name}: a share of its insects drawn");
				}
			}
		}

		[Test]
		public void HotNoon_DrivesTheMammalsUnderCoverAndBringsOutTheLizards()
		{
			AmbientConditions noon = At(0.5, true, 33f);
			float lizards = AmbientGating.Activity(Kind("Lizard"), noon);
			Assert.That(lizards, Is.GreaterThan(0.8f), "lizards bask at a hot noon");
			Assert.That(AmbientGating.Activity(Kind("Rabbit"), noon), Is.LessThan(lizards * 0.5f));
			Assert.That(AmbientGating.Activity(Kind("Rat"), noon), Is.LessThan(0.1f));
			Assert.That(AmbientGating.Activity(Kind("Lizard"), At(0.5, true, 10f)), Is.Zero, "too cold to run below 15 °C");
			Assert.That(AmbientGating.Activity(Kind("Lizard"), At(0.0, false, 25f)), Is.Zero, "and not at night");
		}

		[Test]
		public void Storms_SendEverythingToCover()
		{
			AmbientConditions storm = At(0.5, true, 20f, rain: 0.9f, storm: 0.8f);
			foreach (AmbientCreatureKind kind in Kinds)
			{
				Assert.That(AmbientGating.Activity(kind, storm), Is.Zero, kind.Name);
			}
		}

		[Test]
		public void Rain_ShelterTheBirdsAndBringOutTheFrogs()
		{
			// By day (when few frogs are about), rain brings more out.
			AmbientConditions dry = At(0.45, true, 16f);
			AmbientConditions wet = At(0.45, true, 16f, rain: 0.4f);
			Assert.That(AmbientGating.Activity(Kind("Frog"), wet), Is.GreaterThan(AmbientGating.Activity(Kind("Frog"), dry)));
			Assert.That(AmbientGating.Activity(Kind("Frog"), At(0.95, false, 4f)), Is.Zero, "frogs stay in the mud when it is cold");
			AmbientConditions day = At(0.45, true, 16f);
			AmbientConditions rainyDay = At(0.45, true, 16f, rain: 0.5f);
			Assert.That(AmbientGating.Activity(Kind("Finch flock"), rainyDay), Is.Zero);
			Assert.That(AmbientGating.Activity(Kind("Songbird"), rainyDay), Is.Zero);
			Assert.That(AmbientGating.Activity(Kind("Songbird"), day), Is.GreaterThan(0.8f));
			Assert.That(AmbientGating.Shelter(rainyDay), Is.EqualTo(1f));
			Assert.That(AmbientGating.Shelter(day), Is.Zero);
		}

		[Test]
		public void Raptors_SoarOnlyOnSunnyDays()
		{
			AmbientCreatureKind buzzard = Kind("Buzzard");
			Assert.That(AmbientGating.Activity(buzzard, At(0.55, true, 20f)), Is.GreaterThan(0.8f), "clear afternoon");
			Assert.That(AmbientGating.Activity(buzzard, At(0.55, true, 20f, cloud: 1f)), Is.Zero, "no thermals under overcast");
			Assert.That(AmbientGating.Activity(buzzard, At(0.0, false, 20f)), Is.Zero, "none at night");
		}

		[Test]
		public void Diurnal_BirdsRoostAtNight()
		{
			AmbientConditions night = At(0.0, false, 15f);
			foreach (AmbientCreatureKind kind in Kinds)
			{
				if (kind.Activity == AmbientActivity.Diurnal)
				{
					Assert.That(AmbientGating.Activity(kind, night), Is.Zero, kind.Name);
				}
			}
		}

		// ── Fear ──────────────────────────────────────────────────────

		[Test]
		public void Flush_WithinTheFlightDistance()
		{
			AmbientCreatureKind songbird = Kind("Songbird");
			var bird = new Vector3(10f, 0f, 10f);
			Assert.That(AmbientGating.ShouldFlush(songbird, bird, bird + new Vector3(3f, 0f, 0f), 1f, false), Is.True);
			Assert.That(AmbientGating.ShouldFlush(songbird, bird, bird + new Vector3(9f, 0f, 0f), 1f, false), Is.False);
			// Running at a bird flushes it from further off.
			float walking = AmbientGating.FlightDistance(songbird, 1.4f), running = AmbientGating.FlightDistance(songbird, 6f);
			Assert.That(running, Is.GreaterThan(walking * 1.3f));
			Assert.That(AmbientGating.ShouldFlush(songbird, bird, bird + new Vector3(6.5f, 0f, 0f), 6f, false), Is.True);
			Assert.That(AmbientGating.ShouldFlush(songbird, bird, bird + new Vector3(6.5f, 0f, 0f), 0.5f, false), Is.False);
			// A bird aloft never flushes.
			Assert.That(AmbientGating.ShouldFlush(songbird, bird, bird, 6f, true), Is.False);
			// A bird high in a tree lets people come closer underneath than across the ground.
			Assert.That(AmbientGating.ShouldFlush(songbird, bird, bird + new Vector3(4f, 0f, 0f), 1f, false), Is.True);
			Assert.That(AmbientGating.ShouldFlush(songbird, bird + Vector3.up * 4f, bird, 1f, false), Is.False);
			// Raptors and bats never flush.
			Assert.That(AmbientGating.ShouldFlush(Kind("Buzzard"), bird, bird, 6f, false), Is.False);
			Assert.That(AmbientGating.ShouldFlush(Kind("Bats"), bird, bird, 6f, false), Is.False);
			// Crows are warier than songbirds.
			Assert.That(AmbientGating.FlightDistance(Kind("Crow"), 1f), Is.GreaterThan(AmbientGating.FlightDistance(songbird, 1f) * 2f));
		}

		[Test]
		public void Flee_AwayFromTheThreat()
		{
			var animal = new Vector3(5f, 0f, 5f);
			var threat = new Vector3(0f, 0f, 0f);
			Vector3 away = new Vector3(1f, 0f, 1f).normalized;
			for (uint s = 0; s < 64; s++)
			{
				Vector3 flee = AmbientGating.FleeDirection(animal, threat, s * 7919u);
				Assert.That(flee.magnitude, Is.EqualTo(1f).Within(1e-4f));
				Assert.That(flee.y, Is.Zero);
				Assert.That(Vector3.Dot(flee, away), Is.GreaterThan(Mathf.Cos(41f * Mathf.Deg2Rad)), "within 40° of straight away");
			}
		}

		// ── Placement and routines ────────────────────────────────────

		private static List<AmbientGroup> PlaceArea(IAmbientWorld world, bool reverse, int originX = -2)
		{
			var groups = new List<AmbientGroup>();
			var perches = new List<Vector4> { new Vector4(20f, 15f, 20f, 12f), new Vector4(40f, 18f, 30f, 14f), new Vector4(70f, 12f, 50f, 9f) };
			for (int i = 0; i < 64; i++)
			{
				int n = reverse ? 63 - i : i;
				int cx = originX + n % 8, cz = n / 8 - 4;
				for (int k = 0; k < Kinds.Length; k++)
				{
					Assert.That(AmbientLifeMotion.TryPlace(Kinds[k], k, 1f, Seed, cx, cz, 64f, world, perches, groups), Is.True);
				}
			}
			groups.Sort((a, b) => a.Seed.CompareTo(b.Seed));
			return groups;
		}

		[Test]
		public void Placement_IsTheSameWhicheverOrderTheCellsAreSeenIn()
		{
			List<AmbientGroup> a = PlaceArea(new FakeWorld(), false);
			List<AmbientGroup> b = PlaceArea(new FakeWorld(), true);
			Assert.That(a.Count, Is.GreaterThan(10), "the area should hold some life");
			Assert.That(b.Count, Is.EqualTo(a.Count));
			for (int i = 0; i < a.Count; i++)
			{
				Assert.That(b[i].Seed, Is.EqualTo(a[i].Seed));
				Assert.That(b[i].Home, Is.EqualTo(a[i].Home));
				Assert.That(b[i].Count, Is.EqualTo(a[i].Count));
				Assert.That(b[i].Rank, Is.EqualTo(a[i].Rank));
			}
		}

		[Test]
		public void Placement_KeepsWalkersOffTheWaterAndCrabsOnTheBeach()
		{
			// Cells from the lake (x < -100) across the plain and the beach to the sea (x > 300).
			List<AmbientGroup> groups = PlaceArea(new FakeWorld(), false, -4);
			groups.AddRange(PlaceArea(new FakeWorld(), false, 3));
			int crabs = 0, ducks = 0;
			foreach (AmbientGroup g in groups)
			{
				AmbientCreatureKind kind = Kinds[g.Kind];
				if (kind.Behaviour == AmbientBehaviour.Scurry || kind.Behaviour == AmbientBehaviour.Perch)
				{
					Assert.That(g.Home.x, Is.InRange(-100f, 300f), $"{kind.Name} placed on the water at {g.Home}");
				}
				if (kind.Name == "Shore crab")
				{
					crabs++;
					Assert.That(g.Home.x, Is.InRange(250f, 300f), "shore crabs keep to the beach");
				}
				if (kind.Behaviour == AmbientBehaviour.Paddle)
				{
					ducks++;
					Assert.That(g.Home.x, Is.LessThan(-100f), "ducks on the lake, not the sea");
					Assert.That(g.Home.y, Is.EqualTo(0f), "on its surface");
				}
			}
			Assert.That(crabs, Is.GreaterThan(0), "the beach should have crabs");
			Assert.That(ducks, Is.GreaterThan(0), "the lake should have ducks");
		}

		[Test]
		public void Placement_NothingWhereThereIsNoLife()
		{
			var world = new FakeWorld { Land = AmbientHabitat.Barren, HasSea = false };
			var groups = new List<AmbientGroup>();
			for (int cx = -1; cx < 3; cx++)
			{
				for (int cz = 0; cz < 4; cz++)
				{
					for (int k = 0; k < Kinds.Length; k++)
					{
						AmbientLifeMotion.TryPlace(Kinds[k], k, 1f, Seed, cx, cz, 64f, world, new List<Vector4>(), groups);
					}
				}
			}
			Assert.That(groups, Is.Empty);
		}

		private static AmbientLifeMotion.Frame FrameAt(double seconds, IAmbientWorld world, AmbientMemberState[] states, AmbientThreats threats, List<Vector4> perches = null)
		{
			return new AmbientLifeMotion.Frame
			{
				Seconds = seconds,
				Conditions = AmbientConditions.MildDay,
				World = world,
				Perches = perches ?? new List<Vector4>(),
				States = states,
				Threats = threats,
				Visible = 1f,
			};
		}

		[Test]
		public void Critters_StayOnTheGroundAndBoltFromAThreat()
		{
			var world = new FakeWorld();
			AmbientCreatureKind rabbit = Kind("Rabbit");
			int index = System.Array.IndexOf(Kinds, rabbit);
			var group = new AmbientGroup { Kind = index, Seed = 12345u, Home = new Vector3(50f, 0f, 50f), Radius = 4f, Episode = 12.0, Count = 3, Length = 0.4f, StateFirst = 0 };
			world.TryGround(50f, 50f, out group.Home.y);
			var states = new AmbientMemberState[3];
			var into = new List<AmbientInstance>();
			var none = new AmbientThreats();
			for (double t = 1000.0; t < 1060.0; t += 0.7)
			{
				into.Clear();
				AmbientLifeMotion.Frame frame = FrameAt(t, world, states, none);
				AmbientLifeMotion.Evaluate(group, rabbit, ref frame, into);
				Assert.That(into.Count, Is.EqualTo(3), "all three about with nobody near");
				foreach (AmbientInstance a in into)
				{
					Vector3 at = a.Matrix.GetColumn(3);
					world.TryGround(at.x, at.z, out float y);
					Assert.That(at.y, Is.EqualTo(y).Within(0.01f), "on the ground");
				}
			}
			Assert.That(states[0].Refuge, Is.EqualTo(AmbientMemberState.None));

			// Someone walks up to a lone rabbit: it bolts away, goes to ground while they stay, and comes back long after.
			var lone = group;
			lone.Count = 1;
			lone.Seed = 54321u;
			var one = new AmbientMemberState[1];
			into.Clear();
			AmbientLifeMotion.Frame scared = FrameAt(2000.0, world, one, none);
			AmbientLifeMotion.Evaluate(lone, rabbit, ref scared, into);
			Assert.That(into.Count, Is.EqualTo(1));
			var threats = new AmbientThreats();
			threats.Add(into[0].Matrix.GetColumn(3), 1.4f);
			into.Clear();
			scared.Threats = threats;
			AmbientLifeMotion.Evaluate(lone, rabbit, ref scared, into);
			Assert.That(one[0].Refuge, Is.Not.EqualTo(AmbientMemberState.None), "it took fright");
			Vector3 from = one[0].From;
			into.Clear();
			AmbientLifeMotion.Frame later = FrameAt(2000.5, world, one, threats);
			AmbientLifeMotion.Evaluate(lone, rabbit, ref later, into);
			Assert.That(into.Count, Is.EqualTo(1), "still in sight as it runs");
			Vector3 fled = into[0].Matrix.GetColumn(3);
			Assert.That(Vector3.Distance(fled, threats.Positions[0]), Is.GreaterThan(Vector3.Distance(from, threats.Positions[0]) + 2f), "running away");
			world.TryGround(fled.x, fled.z, out float fledGround);
			Assert.That(fled.y, Is.EqualTo(fledGround).Within(0.01f), "on the ground as it runs");
			into.Clear();
			AmbientLifeMotion.Frame gone = FrameAt(2006.0, world, one, threats);
			AmbientLifeMotion.Evaluate(lone, rabbit, ref gone, into);
			Assert.That(into, Is.Empty, "gone to ground while the threat stays");
			into.Clear();
			AmbientLifeMotion.Frame stillThere = FrameAt(2060.0, world, one, threats);
			AmbientLifeMotion.Evaluate(lone, rabbit, ref stillThere, into);
			Assert.That(into, Is.Empty, "and stays hidden as long as they stand there");
			// Long after the threat has left, it is back.
			into.Clear();
			AmbientLifeMotion.Frame clear = FrameAt(2070.0, world, one, none);
			AmbientLifeMotion.Evaluate(lone, rabbit, ref clear, into);
			into.Clear();
			AmbientLifeMotion.Frame clearLater = FrameAt(2071.0, world, one, none);
			AmbientLifeMotion.Evaluate(lone, rabbit, ref clearLater, into);
			Assert.That(one[0].Refuge, Is.EqualTo(AmbientMemberState.None));
			Assert.That(into.Count, Is.EqualTo(1));
		}

		[Test]
		public void SoloBirds_SitOnTheGroundOrACrownAndAreTheSameForEveryone()
		{
			var world = new FakeWorld();
			AmbientCreatureKind songbird = Kind("Songbird");
			int index = System.Array.IndexOf(Kinds, songbird);
			var perches = new List<Vector4> { new Vector4(20f, 15f, 20f, 12f), new Vector4(30f, 18f, 25f, 14f) };
			var group = new AmbientGroup { Kind = index, Seed = 777u, Home = new Vector3(25f, 0f, 22f), Radius = 15f, Episode = 25.0, Count = 2, Length = 0.15f };
			world.TryGround(25f, 22f, out group.Home.y);
			var a = new List<AmbientInstance>();
			var b = new List<AmbientInstance>();
			var none = new AmbientThreats();
			int seated = 0;
			for (double t = 5000.0; t < 5300.0; t += 1.3)
			{
				a.Clear();
				b.Clear();
				AmbientLifeMotion.Frame fa = FrameAt(t, world, new AmbientMemberState[2], none, perches);
				AmbientLifeMotion.Frame fb = FrameAt(t, world, new AmbientMemberState[2], none, perches);
				AmbientLifeMotion.Evaluate(group, songbird, ref fa, a);
				AmbientLifeMotion.Evaluate(group, songbird, ref fb, b);
				Assert.That(b.Count, Is.EqualTo(a.Count));
				for (int i = 0; i < a.Count; i++)
				{
					Assert.That(b[i].Matrix, Is.EqualTo(a[i].Matrix), "two players, one bird");
					if (a[i].Anim.z >= 0.999f)
					{
						// Folded: sitting. On the ground (give or take a hop) or on a crown.
						seated++;
						Vector3 at = a[i].Matrix.GetColumn(3);
						world.TryGround(at.x, at.z, out float y);
						bool onGround = at.y - y > -0.02f && at.y - y < 0.1f;
						bool onCrown = at.y > 10f;
						Assert.That(onGround || onCrown, Is.True, $"sitting in mid-air at {at} (ground {y})");
					}
				}
			}
			Assert.That(seated, Is.GreaterThan(50), "birds spend most of their time sitting");
		}

		[Test]
		public void RememberedSpots_AreTheSpotsWorkedOutAfresh()
		{
			// The episode spots kept a frame to the next (AmbientLifeMotion.SpotMemo) give every animal exactly the place working
			// them out again every frame gives: songbirds on crowns and the ground, ducks on the lake, rabbits, through many episodes.
			var world = new FakeWorld();
			var perches = new List<Vector4> { new Vector4(20f, 15f, 20f, 12f), new Vector4(30f, 18f, 25f, 14f) };
			AmbientCreatureKind songbird = Kind("Songbird");
			AmbientCreatureKind duck = Kind("Duck");
			var birds = new AmbientGroup { Kind = System.Array.IndexOf(Kinds, songbird), Seed = 777u, Home = new Vector3(25f, 0f, 22f), Radius = 15f, Episode = 25.0, Count = 2, Length = 0.15f };
			world.TryGround(25f, 22f, out birds.Home.y);
			var ducks = new AmbientGroup { Kind = System.Array.IndexOf(Kinds, duck), Seed = 991u, Home = new Vector3(-160f, 0f, 10f), Radius = 20f, Episode = 18.0, Count = 3, Length = 0.4f, StateFirst = 2 };
			AmbientCreatureKind rabbit = Kind("Rabbit");
			var rabbits = new AmbientGroup { Kind = System.Array.IndexOf(Kinds, rabbit), Seed = 12345u, Home = new Vector3(50f, 0f, 50f), Radius = 4f, Episode = 12.0, Count = 3, Length = 0.4f, StateFirst = 5 };
			world.TryGround(50f, 50f, out rabbits.Home.y);
			var memo = new AmbientLifeMotion.SpotMemo();
			var remembered = new AmbientMemberState[8];
			var fresh = new AmbientMemberState[8];
			var a = new List<AmbientInstance>();
			var b = new List<AmbientInstance>();
			var none = new AmbientThreats();
			int compared = 0;
			for (double t = 5000.0; t < 5600.0; t += 0.37)
			{
				memo.BeginFrame(((IAmbientWorld)world).Stamp);
				foreach ((AmbientGroup group, AmbientCreatureKind kind) in new[] { (birds, songbird), (ducks, duck), (rabbits, rabbit) })
				{
					a.Clear();
					b.Clear();
					AmbientLifeMotion.Frame fa = FrameAt(t, world, remembered, none, perches);
					fa.Spots = memo;
					AmbientLifeMotion.Frame fb = FrameAt(t, world, fresh, none, perches);
					AmbientLifeMotion.Evaluate(group, kind, ref fa, a);
					AmbientLifeMotion.Evaluate(group, kind, ref fb, b);
					Assert.That(a.Count, Is.EqualTo(b.Count), $"{kind.Name} at {t}");
					for (int i = 0; i < a.Count; i++)
					{
						Assert.That(a[i].Matrix, Is.EqualTo(b[i].Matrix), $"{kind.Name} member {i} at {t}");
						compared++;
					}
				}
			}
			Assert.That(compared, Is.GreaterThan(1000));
			Assert.That(memo.Count, Is.EqualTo(8), "one entry a member");
			memo.BeginFrame(1);
			Assert.That(memo.Count, Is.EqualTo(0), "a changed world forgets every spot");
		}

		[Test]
		public void Owls_HuntAtNightAndRoostByDay()
		{
			AmbientCreatureKind owl = Kind("Owl");
			AmbientConditions night = At(0.0, false, 12f), noon = At(0.5, true, 18f);
			Assert.That(AmbientGating.Activity(owl, night), Is.GreaterThan(0.8f), "out at night");
			Assert.That(AmbientGating.Activity(owl, noon), Is.LessThan(0.2f), "few about at noon");
			Assert.That(AmbientGating.Shelter(owl, night), Is.LessThan(0.5f), "hunting, not roosting, in the dark");
			Assert.That(AmbientGating.Shelter(owl, noon), Is.EqualTo(1f), "roosting by day");
			Assert.That(AmbientGating.Shelter(Kind("Songbird"), night), Is.EqualTo(1f), "the day birds roost at night");
		}

		[Test]
		public void Transit_CrossesHighInAStraightLineAndKeepsItsV()
		{
			var world = new FakeWorld();
			AmbientCreatureKind geese = Kind("Geese skein");
			int index = System.Array.IndexOf(Kinds, geese);
			var group = new AmbientGroup { Kind = index, Seed = 4242u, Home = new Vector3(60f, 0f, 40f), Radius = 320f, Altitude = 120f, Episode = 150.0, Count = 11, Length = 0.85f };
			world.TryGround(60f, 40f, out group.Home.y);
			var a = new List<AmbientInstance>();
			var b = new List<AmbientInstance>();
			var none = new AmbientThreats();
			int passing = 0, empty = 0;
			Vector3 lastLead = default;
			bool haveLast = false;
			for (double t = 10000.0; t < 13000.0; t += 2.5)
			{
				a.Clear();
				b.Clear();
				AmbientLifeMotion.Frame fa = FrameAt(t, world, new AmbientMemberState[11], none);
				AmbientLifeMotion.Frame fb = FrameAt(t, world, new AmbientMemberState[11], none);
				AmbientLifeMotion.Evaluate(group, geese, ref fa, a);
				AmbientLifeMotion.Evaluate(group, geese, ref fb, b);
				Assert.That(b.Count, Is.EqualTo(a.Count));
				if (a.Count == 0)
				{
					empty++;
					haveLast = false;
					continue;
				}
				passing++;
				Assert.That(a.Count, Is.EqualTo(11), "the whole skein passes together");
				Vector3 lead = a[0].Matrix.GetColumn(3);
				Vector3 forward = a[0].Matrix.rotation * Vector3.forward;
				for (int i = 0; i < a.Count; i++)
				{
					Assert.That(b[i].Matrix, Is.EqualTo(a[i].Matrix), "two players, one skein");
					Vector3 at = a[i].Matrix.GetColumn(3);
					world.TryGround(at.x, at.z, out float y);
					Assert.That(at.y - y, Is.GreaterThan(60f), "high over the ground");
					float flat = new Vector2(at.x - group.Home.x, at.z - group.Home.z).magnitude;
					Assert.That(flat, Is.LessThan(group.Radius * 1.05f + geese.Spread * group.Count), "within its reach");
					if (i > 0)
					{
						Assert.That(Vector3.Dot(at - lead, forward), Is.LessThan(0f), "the leader at the point of the V");
					}
				}
				if (haveLast)
				{
					// A straight crossing: the leader moves along its own heading.
					Vector3 moved = lead - lastLead;
					moved.y = 0f;
					if (moved.magnitude > 1f)
					{
						Vector3 flatForward = new Vector3(forward.x, 0f, forward.z).normalized;
						Assert.That(Vector3.Dot(moved.normalized, flatForward), Is.GreaterThan(0.98f), "flying where it faces");
					}
				}
				lastLead = lead;
				haveLast = true;
			}
			Assert.That(passing, Is.GreaterThan(0), "it passes over now and then");
			Assert.That(empty, Is.GreaterThan(passing), "and the sky is mostly empty of it");
		}

		// ── Biome looks ───────────────────────────────────────────────

		[Test]
		public void Looks_PackExactly()
		{
			var coat = new Color(12f / 255f, 200f / 255f, 1f);
			var belly = new Color(1f, 0f, 128f / 255f);
			var mark = new Color(0f, 0f, 0f);
			Vector4 packed = AmbientLooks.Pack(coat, belly, mark, BiomeLooks.CoatPattern.Mottled, 0.999f);
			Assert.That(AmbientLooks.Has(packed), Is.True);
			Assert.That(AmbientLooks.PatternOf(packed), Is.EqualTo(BiomeLooks.CoatPattern.Mottled));
			Assert.That(Mathf.FloorToInt(packed.w), Is.EqualTo(5), "the phase never carries into the pattern");
			AssertSame(AmbientLooks.UnpackSrgb(packed.x), coat);
			AssertSame(AmbientLooks.UnpackSrgb(packed.y), belly);
			AssertSame(AmbientLooks.UnpackSrgb(packed.z), mark);
			foreach (BiomeLooks.CoatPattern pattern in System.Enum.GetValues(typeof(BiomeLooks.CoatPattern)))
			{
				Assert.That(AmbientLooks.PatternOf(AmbientLooks.Pack(coat, belly, mark, pattern, 0f)), Is.EqualTo(pattern));
			}
			// White packs to the largest number a float still holds exactly.
			Assert.That(AmbientLooks.PackSrgb(Color.white), Is.EqualTo(16777215f));
			Assert.That((uint)AmbientLooks.PackSrgb(Color.white), Is.EqualTo(16777215u));
			Assert.That(AmbientLooks.Has(Vector4.zero), Is.False, "zero is the kind's own colours");

			void AssertSame(Color a, Color b)
			{
				Assert.That(a.r, Is.EqualTo(b.r).Within(0.5f / 255f));
				Assert.That(a.g, Is.EqualTo(b.g).Within(0.5f / 255f));
				Assert.That(a.b, Is.EqualTo(b.b).Within(0.5f / 255f));
			}
		}

		[Test]
		public void Looks_TheGroundTakesThemAndTheAirKeepsItsOwn()
		{
			foreach (AmbientCreatureKind kind in Kinds)
			{
				AmbientLookMix mix = AmbientLooks.MixOf(kind);
				foreach (float v in new[] { mix.Coat, mix.Accent, mix.Markings, mix.KeepBrightness, mix.Size })
				{
					Assert.That(v, Is.InRange(0f, 1f), kind.Name);
				}
				if (kind.Family == AmbientFamily.Critters)
				{
					Assert.That(mix.Coat, Is.InRange(0.65f, 0.8f), $"{kind.Name} is camouflaged for its ground");
				}
				if (kind.Family == AmbientFamily.Flocks || kind.Family == AmbientFamily.SoloBirds || kind.Shape == AmbientShape.Bat)
				{
					Assert.That(mix.Coat, Is.InRange(0.2f, 0.4f), $"{kind.Name} keeps its species' looks");
				}
				if (kind.Shape == AmbientShape.Swarm)
				{
					Assert.That(mix.Markings, Is.Zero, $"{kind.Name}: no pattern on a speck");
					Assert.That(mix.Size, Is.Zero, $"{kind.Name}: the cloud's size is the kind's");
				}
			}
			Assert.That(AmbientLooks.MixOf(Kind("Crow")).KeepBrightness, Is.EqualTo(1f), "a crow stays dark");
			Assert.That(AmbientLooks.MixOf(Kind("Gulls")).KeepBrightness, Is.GreaterThan(0.8f), "a gull stays pale");
			Assert.That(AmbientLooks.MixOf(Kind("Firefly swarm")).Coat, Is.LessThan(AmbientLooks.MixOf(Kind("Gnat cloud")).Coat), "a firefly is its light");
		}

		[Test]
		public void Looks_EachGroupWearsOneOfItsBiomesLooks()
		{
			BiomeLooks.Look meadow = BiomeLooks.For(BiomeLookClass.Temperate, 1234u, BiomeLooks.WorldTraits.Earthlike);
			List<AmbientGroup> a = PlaceArea(new FakeWorld { Looks = meadow }, false);
			List<AmbientGroup> b = PlaceArea(new FakeWorld { Looks = meadow }, true);
			List<AmbientGroup> plain = PlaceArea(new FakeWorld(), false);
			Assert.That(a.Count, Is.GreaterThan(30));
			Assert.That(b.Count, Is.EqualTo(a.Count));
			Assert.That(plain.Count, Is.EqualTo(a.Count), "a look changes the coat, never who lives there");
			var worn = new HashSet<int>();
			for (int i = 0; i < a.Count; i++)
			{
				AmbientGroup g = a[i];
				AmbientCreatureKind kind = Kinds[g.Kind];
				Assert.That(AmbientLooks.Has(g.Look), Is.True, $"{kind.Name} wears a look");
				Assert.That(b[i].Look, Is.EqualTo(g.Look), "the same look whichever order the cells are seen in");
				Assert.That(b[i].Length, Is.EqualTo(g.Length));
				Assert.That(g.LookVariant, Is.LessThan(BiomeLooks.Variants));
				BiomeLooks.Variant v = meadow.Variants[g.LookVariant];
				Assert.That(g.Look, Is.EqualTo(AmbientLooks.Pack(v.Coat, v.Belly, v.Marking, v.Pattern, 0f) + new Vector4(0f, 0f, 0f, g.Look.w - Mathf.Floor(g.Look.w))),
					$"{kind.Name} wears its pick's colours and pattern");
				worn.Add(g.LookVariant);
				// Sized by the look, but still plainly its kind.
				Assert.That(g.Length, Is.InRange(kind.Length.x * AmbientLooks.MinSize - 1e-5f, kind.Length.y * AmbientLooks.MaxSize + 1e-5f), kind.Name);
				Assert.That(AmbientLooks.Has(plain[i].Look), Is.False, "no looks built: the kind's own colours");
				Assert.That(plain[i].Length, Is.InRange(kind.Length.x, kind.Length.y), "and its own size");
				if (AmbientLooks.MixOf(kind).Size == 0f)
				{
					Assert.That(g.Length, Is.EqualTo(plain[i].Length), $"{kind.Name} is not resized by a look");
				}
			}
			Assert.That(worn.Count, Is.GreaterThanOrEqualTo(8), "a place's groups wear many of its looks, not one");
		}

		[Test]
		public void Looks_OtherWorldsCreaturesWearTheirWorldsLooks()
		{
			BiomeWorldConditions frozen = Air(AtmosphereKind.Thick, Condensate.Methane);
			frozen.MeanTemperature = -4f;
			BiomeLooks.Look alien = BiomeLooks.For(BiomeLookClass.Alien, 77u, BiomeLooks.WorldTraits.Of(frozen, 3800f, 9u));
			BiomeLooks.Look home = BiomeLooks.For(BiomeLookClass.Alien, 77u, BiomeLooks.WorldTraits.Earthlike);
			var fake = new FakeWorld { LifeWorld = AmbientWorld.Cryogenic, Land = AmbientHabitat.Alien | AmbientHabitat.Mountain | AmbientHabitat.Ice, Temperature = -1f, HasSea = false, Looks = alien };
			List<AmbientGroup> groups = PlaceArea(fake, false);
			Assert.That(groups.Count, Is.GreaterThan(5));
			bool differs = false;
			foreach (AmbientGroup g in groups)
			{
				Assert.That(AmbientLooks.Has(g.Look), Is.True, $"{Kinds[g.Kind].Name} wears its world's look");
				BiomeLooks.Variant v = alien.Variants[g.LookVariant];
				Assert.That(AmbientLooks.UnpackSrgb(g.Look.x).r, Is.EqualTo(Mathf.Clamp01(v.Coat.r)).Within(0.5f / 255f));
				differs |= AmbientLooks.PackSrgb(home.Variants[g.LookVariant].Coat) != g.Look.x;
			}
			Assert.That(differs, Is.True, "a frozen world under an orange star dresses its creatures otherwise than the home world would");
		}

		// ── Meshes ────────────────────────────────────────────────────

		[Test]
		public void Meshes_AreSmallAndCarryTheirParts()
		{
			foreach (AmbientCreatureKind kind in Kinds)
			{
				Mesh mesh = AmbientCreatureMeshes.Build(kind, true);
				try
				{
					Assert.That(mesh.vertexCount, Is.InRange(30, 700), $"{kind.Name} vertices");
					Assert.That(mesh.triangles.Length / 3, Is.InRange(20, 900), $"{kind.Name} triangles");
					var parts = new List<Vector2>();
					mesh.GetUVs(1, parts);
					// Every vertex's colour roles (back, belly, accent shares) sum to at most about one, and the body has a coat.
					var roles = new List<Vector3>();
					mesh.GetUVs(2, roles);
					Assert.That(roles.Count, Is.EqualTo(mesh.vertexCount), $"{kind.Name} colour roles");
					float coat = 0f;
					foreach (Vector3 r in roles)
					{
						Assert.That(r.x + r.y + r.z, Is.LessThanOrEqualTo(1.05f), $"{kind.Name} roles");
						Assert.That(Mathf.Min(r.x, Mathf.Min(r.y, r.z)), Is.GreaterThanOrEqualTo(-1e-4f), $"{kind.Name} roles");
						coat = Mathf.Max(coat, r.x);
					}
					// (A swarm's specks are each a shade between its back and belly, so the darkest is only mostly back.)
					Assert.That(coat, Is.GreaterThan(kind.Shape == AmbientShape.Swarm ? 0.5f : 0.9f), $"{kind.Name} has a back to wear a coat on");
					bool wings = false, legs = false;
					foreach (Vector2 p in parts)
					{
						int id = Mathf.RoundToInt(p.x);
						wings |= id == 1 || id == 2;
						legs |= id >= 1 && id <= 8;
					}
					if (kind.Shape == AmbientShape.Swarm)
					{
						// A swarm is a quad an insect, every one of the kind's largest swarm.
						Assert.That(mesh.vertexCount, Is.EqualTo(4 * kind.GroupSize.y), $"{kind.Name}: four corners an insect");
						Assert.That(mesh.triangles.Length / 3, Is.EqualTo(2 * kind.GroupSize.y), $"{kind.Name}: two triangles an insect");
					}
					else if (AmbientCreatureMeshes.ModeOf(kind.Shape) == 0f)
					{
						Assert.That(wings, Is.True, $"{kind.Name} has wings");
					}
					else
					{
						Assert.That(legs, Is.True, $"{kind.Name} has legs");
					}
				}
				finally
				{
					Object.DestroyImmediate(mesh);
				}
			}
		}
	}
}
