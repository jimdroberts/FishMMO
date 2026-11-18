using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The caves' animals (<see cref="AmbientCaveLife"/>): who lives at which cave-life spot, that underground neither the day,
	/// the weather nor the season's heat moves them (only the bats read the night outside, to go out and come back), the same
	/// placement for everyone, and every member kept to its spot: on its patch's plane, in its box of air, under its pool.
	/// </summary>
	[TestFixture]
	public class AmbientCaveLifeTests
	{
		private static readonly AmbientCreatureKind[] Kinds = AmbientLifeCatalogue.Defaults();

		private static readonly string[] CaveKinds =
		{
			"Cave bats", "Cave crickets", "Cave spiders", "Cave salamander", "Olm", "Cave moths", "Fungus gnats", "Cave geckos",
			// 2026-10-20: the light-makers (AmbientGlowLifeTests).
			"Lantern beetles", "Glow millipedes", "Glowing snails", "Spore motes", "Cave lanternfish", "Lantern jellies", "Wisp moths",
			"Lantern newts", "Ember mites", "Ice worms",
		};

		private static int IndexOf(string name)
		{
			for (int i = 0; i < Kinds.Length; i++)
			{
				if (Kinds[i].Name == name)
				{
					return i;
				}
			}
			Assert.Fail($"no kind '{name}'");
			return -1;
		}

		private static AmbientCreatureKind Kind(string name) => Kinds[IndexOf(name)];

		private static float C(float celsius) => AmbientLifeCatalogue.Celsius(celsius);

		private static AmbientConditions At(double localTime01, bool daylight, float celsius, float rain = 0f, float storm = 0f, float wind = 0f, float summer = 0.5f)
		{
			AmbientConditions.Daylight(localTime01, daylight, out float light, out float twilight, out bool evening);
			return new AmbientConditions { Light = light, Twilight = twilight, Evening = evening, Temperature = C(celsius), Rain = rain, Storm = storm, Wind = wind, Summer = summer };
		}

		private static SceneCaveLifeSpot Spot(SceneCaveLifeKind kind, SceneCaveForm form = SceneCaveForm.Cave, float dark = 1f, SceneCaveLifeFlags flags = SceneCaveLifeFlags.None)
		{
			var spot = new SceneCaveLifeSpot { Kind = kind, Form = form, Dark = dark, Flags = flags, Position = new Vector3(10f, 50f, 20f), Normal = Vector3.up, Extent = new Vector3(1.5f, 4f, 0f) };
			switch (kind)
			{
				case SceneCaveLifeKind.Wall:
					spot.Normal = new Vector3(0.6f, 0.1f, 0.8f).normalized;
					break;
				case SceneCaveLifeKind.Roost:
					spot.Normal = Vector3.down;
					break;
				case SceneCaveLifeKind.Flight:
				case SceneCaveLifeKind.Glow:
					spot.Extent = new Vector3(5f, 2f, 3f);
					spot.Yaw = 30f;
					break;
				case SceneCaveLifeKind.Pool:
					spot.Extent = new Vector3(2f, 0.6f, 0f);
					break;
				case SceneCaveLifeKind.Mouth:
					spot.Normal = Vector3.right;
					spot.Extent = new Vector3(2.5f, 0f, 12f);
					spot.Dark = 0f;
					break;
			}
			return spot;
		}

		/// <summary>A world of mild temperate country (12 °C, the cave's air the same) with the home world's air.</summary>
		private sealed class CaveWorld : IAmbientWorld
		{
			public AmbientHabitat Land = AmbientHabitat.Mountain | AmbientHabitat.Woodland;
			public float Temperature = AmbientLifeCatalogue.Celsius(12f);
			public AmbientWorld LifeWorld = AmbientWorld.Earthly;
			public AmbientWorld World => LifeWorld;

			public bool TryGround(float x, float z, out float y)
			{
				y = 120f; // the hill over the cave: nothing underground may be put on it
				return true;
			}

			public float WaterAt(float x, float z, out bool sea)
			{
				sea = false;
				return float.NegativeInfinity;
			}

			public float LiveWaterAt(float x, float z) => float.NegativeInfinity;

			public void Habitat(Vector3 position, out AmbientHabitat habitat, out float temperature)
			{
				habitat = Land;
				temperature = Temperature;
			}
		}

		// ── The catalogue ─────────────────────────────────────────────

		[Test]
		public void TheCavesKindsAreAppendedAndNeverOnTheOpenLand()
		{
			for (int i = 0; i < CaveKinds.Length; i++)
			{
				AmbientCreatureKind kind = Kinds[Kinds.Length - CaveKinds.Length + i];
				Assert.That(kind.Name, Is.EqualTo(CaveKinds[i]), "appended at the catalogue's end, in order: the land's placements keep their hashes");
				Assert.That(kind.GroupsPerKm2, Is.Zero, $"{kind.Name} only underground");
				Assert.That(AmbientCaveLife.LivesInCaves(kind), Is.True);
				Assert.That(kind.Worlds, Is.EqualTo(AmbientWorld.Earthly));
			}
			Assert.That(AmbientCaveLife.LivesInCaves(Kind("Rat")), Is.True, "rats come in from the mouth");
			Assert.That(AmbientCaveLife.LivesInCaves(Kind("Songbird")), Is.False);
			// The open land never places a cave's kind.
			var groups = new List<AmbientGroup>();
			foreach (string name in CaveKinds)
			{
				Assert.That(AmbientLifeMotion.TryPlace(Kind(name), IndexOf(name), 1f, 7u, 0, 0, 64f, new CaveWorld(), null, groups), Is.True);
			}
			Assert.That(groups, Is.Empty);
		}

		[Test]
		public void TheNicheAndFormBitsFollowTheSpotsOrdinals()
		{
			Assert.That(AmbientCaveLife.NicheOf(SceneCaveLifeKind.Floor), Is.EqualTo(AmbientCaveNiche.Floor));
			Assert.That(AmbientCaveLife.NicheOf(SceneCaveLifeKind.Wall), Is.EqualTo(AmbientCaveNiche.Wall));
			Assert.That(AmbientCaveLife.NicheOf(SceneCaveLifeKind.Roost), Is.EqualTo(AmbientCaveNiche.Roost));
			Assert.That(AmbientCaveLife.NicheOf(SceneCaveLifeKind.Flight), Is.EqualTo(AmbientCaveNiche.Flight));
			Assert.That(AmbientCaveLife.NicheOf(SceneCaveLifeKind.Pool), Is.EqualTo(AmbientCaveNiche.Pool));
			Assert.That(AmbientCaveLife.NicheOf(SceneCaveLifeKind.Mouth), Is.EqualTo(AmbientCaveNiche.Mouth));
			Assert.That(AmbientCaveLife.NicheOf(SceneCaveLifeKind.Glow), Is.EqualTo(AmbientCaveNiche.Glow));
			foreach (SceneCaveForm form in System.Enum.GetValues(typeof(SceneCaveForm)))
			{
				Assert.That(AmbientCaveLife.FormOf(form).ToString(), Is.EqualTo(form.ToString()), $"{form}");
			}
		}

		// ── Who lives where ───────────────────────────────────────────

		[Test]
		public void EachKindAtItsOwnSpotsInItsOwnCaves()
		{
			float mild = C(12f);
			AmbientHabitat land = AmbientHabitat.Mountain;
			Assert.That(AmbientCaveLife.Weight(Kind("Cave bats"), Spot(SceneCaveLifeKind.Roost), land, mild), Is.GreaterThan(0.9f));
			Assert.That(AmbientCaveLife.Weight(Kind("Cave bats"), Spot(SceneCaveLifeKind.Floor), land, mild), Is.Zero, "bats do not walk the floor");
			Assert.That(AmbientCaveLife.Weight(Kind("Cave crickets"), Spot(SceneCaveLifeKind.Wall), land, mild), Is.GreaterThan(0.9f));
			Assert.That(AmbientCaveLife.Weight(Kind("Cave crickets"), Spot(SceneCaveLifeKind.Wall, SceneCaveForm.IceCave), land, mild), Is.Zero, "nothing crawls on ice");
			Assert.That(AmbientCaveLife.Weight(Kind("Cave crickets"), Spot(SceneCaveLifeKind.Floor, SceneCaveForm.LavaTube), land, mild), Is.GreaterThan(0f).And.LessThan(0.5f), "a lava tube holds a few");
			Assert.That(AmbientCaveLife.Weight(Kind("Cave bats"), Spot(SceneCaveLifeKind.Roost, SceneCaveForm.IceCave), land, mild), Is.GreaterThan(0f).And.LessThan(0.2f), "an ice cave next to nothing");
			// The wet's own: salamanders and olms only where it is damp, and never by the sea's water.
			Assert.That(AmbientCaveLife.Weight(Kind("Cave salamander"), Spot(SceneCaveLifeKind.Floor), land, mild), Is.Zero, "a dry floor");
			Assert.That(AmbientCaveLife.Weight(Kind("Cave salamander"), Spot(SceneCaveLifeKind.Floor, flags: SceneCaveLifeFlags.Damp), land, mild), Is.GreaterThan(0.9f));
			Assert.That(AmbientCaveLife.Weight(Kind("Olm"), Spot(SceneCaveLifeKind.Pool, flags: SceneCaveLifeFlags.Damp), land, mild), Is.GreaterThan(0.9f));
			Assert.That(AmbientCaveLife.Weight(Kind("Olm"), Spot(SceneCaveLifeKind.Pool, SceneCaveForm.Grotto, flags: SceneCaveLifeFlags.Damp | SceneCaveLifeFlags.Salt), land, mild), Is.Zero, "no olm in the sea");
			Assert.That(AmbientCaveLife.Weight(Kind("Shore crab"), Spot(SceneCaveLifeKind.Floor, SceneCaveForm.SeaCave, flags: SceneCaveLifeFlags.Damp | SceneCaveLifeFlags.Salt), land, mild), Is.GreaterThan(0f),
				"crabs on a sea cave's wet rock");
			Assert.That(AmbientCaveLife.Weight(Kind("Shore crab"), Spot(SceneCaveLifeKind.Floor, SceneCaveForm.SeaCave), land, mild), Is.Zero, "but not up its dry back");
			// The twilight's and the dark's.
			Assert.That(AmbientCaveLife.Weight(Kind("Cave moths"), Spot(SceneCaveLifeKind.Flight, dark: 0.2f), land, mild), Is.GreaterThan(0.9f));
			Assert.That(AmbientCaveLife.Weight(Kind("Cave moths"), Spot(SceneCaveLifeKind.Flight, dark: 1f), land, mild), Is.Zero, "moths keep to the twilight");
			Assert.That(AmbientCaveLife.Weight(Kind("Rat"), Spot(SceneCaveLifeKind.Floor, dark: 1f), land, mild), Is.Zero, "rats go no deeper than there is food");
			// Warm caves' geckos; nothing under barren ground.
			Assert.That(AmbientCaveLife.Weight(Kind("Cave geckos"), Spot(SceneCaveLifeKind.Wall, dark: 0.6f), land, mild), Is.Zero, "a cool cave has no geckos");
			Assert.That(AmbientCaveLife.Weight(Kind("Cave geckos"), Spot(SceneCaveLifeKind.Wall, dark: 0.6f), land, C(26f)), Is.GreaterThan(0.9f));
			foreach (AmbientCreatureKind kind in Kinds)
			{
				Assert.That(AmbientCaveLife.Weight(kind, Spot(SceneCaveLifeKind.Floor, flags: SceneCaveLifeFlags.Damp), AmbientHabitat.Barren, mild), Is.Zero, $"{kind.Name} under barren ground");
			}
		}

		[Test]
		public void PlacementIsTheSameForEveryoneAndNothingOnAnAirlessWorld()
		{
			var world = new CaveWorld();
			SceneCaveLifeSpot roost = Spot(SceneCaveLifeKind.Roost);
			var a = new List<AmbientGroup>();
			var b = new List<AmbientGroup>();
			int found = 0;
			for (int spot = 0; spot < 40; spot++)
			{
				for (int k = 0; k < Kinds.Length; k++)
				{
					AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0xCAFEu, spot, roost, spot + 1, world, a);
				}
			}
			for (int spot = 39; spot >= 0; spot--)
			{
				for (int k = Kinds.Length - 1; k >= 0; k--)
				{
					found += AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0xCAFEu, spot, roost, spot + 1, world, b);
				}
			}
			Assert.That(found, Is.EqualTo(a.Count));
			Assert.That(a.Count, Is.GreaterThan(15), "most roosts have a colony");
			var seeds = new HashSet<uint>();
			foreach (AmbientGroup g in b)
			{
				seeds.Add(g.Seed);
			}
			foreach (AmbientGroup g in a)
			{
				Assert.That(seeds.Contains(g.Seed), Is.True, "the same groups whatever order the spots are placed in");
				Assert.That(Kinds[g.Kind].Name, Is.EqualTo("Cave bats"));
				Assert.That(g.Cave, Is.GreaterThan(0));
				Assert.That(g.Count, Is.InRange(Kinds[g.Kind].GroupSize.x, Kinds[g.Kind].GroupSize.y));
				Assert.That(g.Steady, Is.EqualTo(world.Temperature));
			}
			var airless = new List<AmbientGroup>();
			var none = new CaveWorld { LifeWorld = AmbientWorld.None };
			for (int k = 0; k < Kinds.Length; k++)
			{
				AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0xCAFEu, 0, roost, 1, none, airless);
				AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0xCAFEu, 1, Spot(SceneCaveLifeKind.Floor, flags: SceneCaveLifeFlags.Damp), 2, none, airless);
			}
			Assert.That(airless, Is.Empty, "no air, no cave life");
		}

		[Test]
		public void TheDeepDarkIsNotBare()
		{
			// 2026-10-19: almost every baked spot is past the twilight (Dark 1), and caves read empty when only the crickets
			// lived there at under a group a spot. A dry deep floor in a mild cave holds a few animals of more than one kind.
			var world = new CaveWorld { Temperature = C(12f) };
			var groups = new List<AmbientGroup>();
			var kinds = new HashSet<string>();
			const int spots = 100;
			for (int spot = 0; spot < spots; spot++)
			{
				SceneCaveLifeSpot floor = Spot(SceneCaveLifeKind.Floor, dark: 1f);
				for (int k = 0; k < Kinds.Length; k++)
				{
					AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0xD0Cu, spot, floor, spot + 1, world, groups);
				}
			}
			int animals = 0;
			foreach (AmbientGroup g in groups)
			{
				animals += g.Count;
				kinds.Add(Kinds[g.Kind].Name);
			}
			Assert.That(animals, Is.GreaterThan(6 * spots), "a deep floor patch holds several animals on average");
			Assert.That(kinds, Does.Contain("Cave crickets").And.Contain("Cave spiders"), "the crickets and what hunts them");
		}

		[Test]
		public void ACaveUnderColdMountainsIsTheGroundsWarmthNotTheAirs()
		{
			// 2026-10-20: every cave round a cold scene's spawn read −2..−4 °C (the open air's year mean at the cave), under every
			// cave kind's band, and stood empty. The rock is the ground's year mean, and snow keeps the winter's frost off it.
			Assert.That(AmbientCaveLife.GroundYearMean(C(12f), C(20f), 0.35f), Is.EqualTo(C(12f)).Within(1e-6f), "no frost, no change");
			Assert.That(AmbientCaveLife.GroundYearMean(C(-3f), C(10f), 1f), Is.EqualTo(C(-3f)).Within(1e-6f), "bare ground feels all the frost");
			float wet = AmbientCaveLife.CaveAir(C(-3f), C(10f), true, 0.5f);
			float dry = AmbientCaveLife.CaveAir(C(-3f), C(10f), true, -0.8f);
			Assert.That(wet, Is.InRange(C(-0.5f), C(1.5f)), "deep snow keeps a −3 °C year's ground at about freezing, the edge of permafrost");
			Assert.That(dry, Is.LessThan(wet).And.GreaterThan(C(-3f)), "thin snow keeps less of the frost off");
			Assert.That(AmbientCaveLife.CaveAir(C(-3f), C(10f), false, 0.5f), Is.EqualTo(C(-3f)), "no seasons known: the air's own mean");
			// Always frozen: snow that never melts keeps less cold out, but the ground stays frozen.
			Assert.That(AmbientCaveLife.GroundYearMean(C(-20f), C(-10f), 0.35f), Is.LessThan(C(-5f)));
			// The sine's frost is continuous where the year starts to freeze and where it stops thawing.
			Assert.That(AmbientCaveLife.GroundYearMean(C(5f), C(10.0001f), 0.35f), Is.EqualTo(C(5f)).Within(1e-3f));
			Assert.That(AmbientCaveLife.GroundYearMean(C(-5f), C(-0.0001f), 0.35f), Is.EqualTo(0.35f * C(-5f)).Within(1e-3f));

			// And such a cave has its crickets, the spiders hunting them and its bats.
			var world = new CaveWorld { Temperature = wet };
			var kinds = new HashSet<string>();
			var groups = new List<AmbientGroup>();
			for (int spot = 0; spot < 60; spot++)
			{
				for (int k = 0; k < Kinds.Length; k++)
				{
					AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0xC01Du, spot, Spot(SceneCaveLifeKind.Floor, SceneCaveForm.Cave, dark: 1f), spot + 1, world, groups);
					AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0xC01Du, 100 + spot, Spot(SceneCaveLifeKind.Roost), spot + 1, world, groups);
				}
			}
			foreach (AmbientGroup g in groups)
			{
				kinds.Add(Kinds[g.Kind].Name);
				Assert.That(AmbientCaveLife.Activity(Kinds[g.Kind], At(0.5, true, -8f, summer: -0.2f), g.Steady, 1f), Is.GreaterThan(0.3f), $"{Kinds[g.Kind].Name} about in its cave");
			}
			Assert.That(kinds, Does.Contain("Cave crickets").And.Contain("Cave spiders").And.Contain("Cave bats"));
			Assert.That(groups.Count, Is.GreaterThan(60), "more than a group a spot");
		}

		// ── When ──────────────────────────────────────────────────────

		[Test]
		public void UndergroundNeitherTheDayNorTheWeatherReaches()
		{
			AmbientConditions noonStorm = At(0.5, true, 30f, rain: 0.9f, storm: 0.9f, wind: 0.9f);
			AmbientConditions midnight = At(0.0, false, 12f);
			float steady = C(12f);
			foreach (string name in new[] { "Cave crickets", "Cave spiders", "Cave salamander", "Olm", "Fungus gnats" })
			{
				AmbientCreatureKind kind = Kind(name);
				Assert.That(AmbientGating.Activity(kind, noonStorm), Is.Zero, $"{name}: a storm sends everything on the land to cover");
				Assert.That(AmbientCaveLife.Activity(kind, noonStorm, steady, 1f), Is.EqualTo(AmbientCaveLife.Activity(kind, midnight, steady, 1f)).Within(1e-4f),
					$"{name}: the deep cave is the same at a stormy noon as on a still midnight");
				Assert.That(AmbientCaveLife.Activity(kind, noonStorm, steady, 1f), Is.GreaterThan(0.9f), $"{name} is about in the dark");
			}
			// A rat in from the mouth: the daylight reaches it in the twilight, none past it.
			Assert.That(AmbientCaveLife.Activity(Kind("Rat"), At(0.5, true, 15f), steady, 0f), Is.LessThan(0.1f), "a rat by day at the mouth lies up");
			Assert.That(AmbientCaveLife.Activity(Kind("Rat"), At(0.5, true, 15f), steady, 0.7f), Is.GreaterThan(0.9f), "past the twilight it is night all day");
			// The cave's own air: a frost outside does not reach it, a cold cave keeps its geckos out all the same.
			Assert.That(AmbientCaveLife.Activity(Kind("Cave crickets"), At(0.0, false, -15f), steady, 1f), Is.GreaterThan(0.9f));
			Assert.That(AmbientCaveLife.Activity(Kind("Cave geckos"), At(0.0, false, 30f), steady, 1f), Is.Zero);
		}

		[Test]
		public void BatsHangByDayStreamOutAtDuskAndSleepThroughTheWinter()
		{
			AmbientCaveLife.CaveBatHours noon = AmbientCaveLife.BatHours(At(0.5, true, 18f));
			Assert.That(noon.Out, Is.LessThan(0.05f), "hanging in the roost by day");
			Assert.That(noon.Leaving + noon.Returning, Is.LessThan(0.05f));
			AmbientCaveLife.CaveBatHours dusk = AmbientCaveLife.BatHours(At(0.77, false, 18f));
			Assert.That(dusk.Leaving, Is.GreaterThan(0.2f), "streaming out at dusk");
			Assert.That(dusk.Returning, Is.Zero);
			Assert.That(dusk.Stirring, Is.GreaterThan(noon.Stirring), "circling the cave first");
			AmbientCaveLife.CaveBatHours night = AmbientCaveLife.BatHours(At(0.95, false, 18f));
			Assert.That(night.Out, Is.GreaterThan(0.5f), "most out feeding in the first hours of dark");
			AmbientCaveLife.CaveBatHours dawn = AmbientCaveLife.BatHours(At(0.23, false, 18f));
			Assert.That(dawn.Returning, Is.GreaterThan(0.1f), "back in before dawn");
			Assert.That(dawn.Leaving, Is.Zero);
			Assert.That(AmbientCaveLife.BatHours(At(0.95, false, 18f, rain: 0.5f)).Out, Is.Zero, "none out in the rain");
			Assert.That(AmbientCaveLife.BatHours(At(0.95, false, 1f)).Out, Is.LessThan(0.05f), "none out in the cold");
			AmbientCaveLife.CaveBatHours winter = AmbientCaveLife.BatHours(At(0.95, false, 8f, summer: -1f));
			Assert.That(winter.Out + winter.Stirring, Is.LessThan(0.01f), "asleep through the winter");
			// And the colony is there in its roost all the same, in winter and in a storm.
			Assert.That(AmbientCaveLife.Activity(Kind("Cave bats"), At(0.5, true, -10f, storm: 1f, summer: -1f), C(8f), 1f), Is.GreaterThan(0.9f));
		}

		// ── Kept to the spot (these build instance matrices: native maths) ──

		private static List<AmbientInstance> Evaluate(string name, SceneCaveLifeSpot spot, double seconds, in AmbientConditions conditions, out AmbientGroup group)
		{
			int k = IndexOf(name);
			var groups = new List<AmbientGroup>();
			for (int i = 0; groups.Count == 0 && i < 200; i++)
			{
				AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0xBEEFu, i, spot, 1, new CaveWorld { Temperature = C(14f) }, groups);
			}
			Assert.That(groups, Is.Not.Empty, $"{name} placed at a {spot.Kind}");
			group = groups[0];
			group.StateFirst = 0;
			var frame = new AmbientLifeMotion.Frame
			{
				Seconds = seconds,
				Conditions = conditions,
				World = new CaveWorld(),
				States = new AmbientMemberState[group.Count],
				Visible = 1f,
				CaveSpots = new List<SceneCaveLifeSpot> { spot },
			};
			var into = new List<AmbientInstance>();
			AmbientLifeMotion.Evaluate(group, Kinds[k], ref frame, into);
			return into;
		}

		[Test]
		public void Crawlers_StayOnTheirPatchsPlane()
		{
			foreach ((string name, SceneCaveLifeKind kind, SceneCaveLifeFlags flags) in new[]
			{
				("Cave crickets", SceneCaveLifeKind.Wall, SceneCaveLifeFlags.None), ("Cave spiders", SceneCaveLifeKind.Wall, SceneCaveLifeFlags.None),
				("Cave salamander", SceneCaveLifeKind.Floor, SceneCaveLifeFlags.Damp), ("Rat", SceneCaveLifeKind.Floor, SceneCaveLifeFlags.None),
			})
			{
				SceneCaveLifeSpot spot = Spot(kind, dark: name == "Rat" || name == "Cave spiders" ? 0.5f : 1f, flags: flags);
				int seen = 0;
				for (double t = 100000.0; t < 100600.0; t += 3.7)
				{
					foreach (AmbientInstance a in Evaluate(name, spot, t, At(0.0, false, 14f), out _))
					{
						Vector3 p = a.Matrix.GetColumn(3);
						Vector3 d = p - spot.Position;
						Assert.That(Mathf.Abs(Vector3.Dot(d, spot.Normal)), Is.LessThan(1e-3f), $"{name} off its {kind}'s plane");
						Assert.That((d - spot.Normal * Vector3.Dot(d, spot.Normal)).magnitude, Is.LessThanOrEqualTo(spot.Extent.x + 1e-3f), $"{name} off its patch");
						Assert.That(Vector3.Dot(a.Matrix.GetColumn(1).normalized, spot.Normal), Is.GreaterThan(0.85f), $"{name}'s back to the air (a rat rears to sniff, 30°)");
						seen++;
					}
				}
				Assert.That(seen, Is.GreaterThan(50), name);
			}
		}

		[Test]
		public void Fliers_StayInTheirBoxAndColoniesHangFromTheRoof()
		{
			SceneCaveLifeSpot box = Spot(SceneCaveLifeKind.Flight, dark: 0.3f);
			float r = box.Yaw * Mathf.Deg2Rad;
			var along = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
			var across = new Vector3(along.z, 0f, -along.x);
			int flying = 0;
			for (double t = 50000.0; t < 50300.0; t += 1.3)
			{
				foreach (AmbientInstance a in Evaluate("Cave moths", box, t, At(0.0, false, 14f), out _))
				{
					Vector3 d = (Vector3)a.Matrix.GetColumn(3) - box.Position;
					Assert.That(Mathf.Abs(Vector3.Dot(d, along)), Is.LessThanOrEqualTo(box.Extent.x));
					Assert.That(Mathf.Abs(d.y), Is.LessThanOrEqualTo(box.Extent.y));
					Assert.That(Mathf.Abs(Vector3.Dot(d, across)), Is.LessThanOrEqualTo(box.Extent.z));
					flying++;
				}
			}
			Assert.That(flying, Is.GreaterThan(50), "moths wheel in the twilight's air");

			SceneCaveLifeSpot roost = Spot(SceneCaveLifeKind.Roost);
			List<AmbientInstance> hanging = Evaluate("Cave bats", roost, 70000.0, At(0.5, true, 18f), out AmbientGroup colony);
			Assert.That(hanging.Count, Is.GreaterThan(colony.Count / 2), "by day most of the colony hangs");
			foreach (AmbientInstance a in hanging)
			{
				Vector3 p = a.Matrix.GetColumn(3);
				Assert.That(p.y, Is.LessThan(roost.Position.y), "under the roof");
				Assert.That(roost.Position.y - p.y, Is.LessThan(0.2f), "hung by its feet, a body's length at most");
				Assert.That(Vector3.Dot(a.Matrix.GetColumn(2).normalized, Vector3.down), Is.GreaterThan(0.99f), "head down");
				Assert.That(a.Anim.z, Is.GreaterThan(0.3f), "wings wrapped");
			}
			Assert.That(Evaluate("Cave bats", roost, 70000.0, At(0.95, false, 18f), out _).Count, Is.LessThan(hanging.Count), "fewer hang at night: out feeding");
		}
	}
}
