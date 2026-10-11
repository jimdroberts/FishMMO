using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;

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
			Assert.That(AmbientGating.HabitatOf("Methane Lake"), Is.EqualTo(AmbientHabitat.Barren), "an alien lake is lifeless, not fresh water");
			Assert.That(AmbientGating.HabitatOf("Regolith Plain"), Is.EqualTo(AmbientHabitat.Barren));
			Assert.That(AmbientGating.HabitatOf("Cryovolcanic Plain"), Is.EqualTo(AmbientHabitat.Barren));
			Assert.That(AmbientGating.HabitatOf("Wasteland"), Is.EqualTo(AmbientHabitat.Barren));
			Assert.That(AmbientGating.HabitatOf("Something Unheard Of"), Is.EqualTo(AmbientHabitat.None));
			Assert.That(AmbientGating.HabitatOf(null), Is.EqualTo(AmbientHabitat.None));
		}

		[Test]
		public void Habitat_IceAndBarrenHoldAlmostNothing()
		{
			foreach (AmbientCreatureKind kind in Kinds)
			{
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
				Assert.That(sea > 0f, Is.EqualTo(kind.Name == "Gulls"), $"{kind.Name} over open sea");
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
		public void NightCritters_AreAboutByDayTooButFewer()
		{
			// Jim, 2026-10-10: a third of the night's rats and mice are out by day, so a noon walk is not empty of them.
			AmbientConditions mildNoon = At(0.5, true, 18f);
			AmbientConditions night = At(0.0, false, 15f);
			float day = AmbientGating.Activity(Kind("Rat"), mildNoon);
			Assert.That(day, Is.EqualTo(AmbientGating.DaytimeNocturnalShare).Within(0.05f), "about a third by day");
			Assert.That(AmbientGating.Activity(Kind("Rat"), night), Is.GreaterThan(day * 2f), "the night is still theirs");
			Assert.That(AmbientGating.Activity(Kind("Mouse"), mildNoon), Is.GreaterThan(0.25f));
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
					bool wings = false, legs = false;
					foreach (Vector2 p in parts)
					{
						int id = Mathf.RoundToInt(p.x);
						wings |= id == 1 || id == 2;
						legs |= id >= 1 && id <= 8;
					}
					if (AmbientCreatureMeshes.ModeOf(kind.Shape) == 0f)
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
