using System.Collections.Generic;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The open sea's life (2026-10-25, phase I2p): gliding flying fish, men-o'-war afloat on the swell, mudskippers on the bare mud,
	/// upside-down jellies on the sand, lanternfish rising at night, a milling trevally column, boiling tuna, creature light and the
	/// seabirds that live only far out at sea. All pure functions of the shared clock; the defaults hold none of it.
	/// </summary>
	[TestFixture]
	public class SeaLifeOpenSeaTests
	{
		/// <summary>A flat sea floor at <see cref="Floor"/>, everywhere.</summary>
		private sealed class FlatGround : ISeaGround
		{
			public float Floor = -30f;

			public bool TryGround(float x, float z, out float y)
			{
				y = Floor;
				return true;
			}

			public void Habitat(Vector3 position, SeaCreatureKind kind, out bool home, out float homeWeight, out float temperature)
			{
				home = true;
				homeWeight = 1f;
				temperature = 0.5f;
			}

			public float HeaviestHome(SeaCreatureKind kind) => 1f;
		}

		private static SeaGroup Group(SeaCreatureKind kind, Vector3 home, int count = 12)
		{
			var group = new SeaGroup { Kind = 0, Seed = 0xA11CEu, Home = home, Radius = 20f, Aspect = 0.7f, Yaw = 0.3f, Sense = 1f, Count = count, Length = Mathf.Lerp(kind.Length.x, kind.Length.y, 0.5f) };
			group.Period = SeaLifePlacement.LapSeconds(group, kind);
			return group;
		}

		private static SeaCreatureKind Kind(SeaCreatureBehaviour behaviour, SeaCreatureShape shape = SeaCreatureShape.Fish)
		{
			return new SeaCreatureKind
			{
				Name = behaviour.ToString(), Shape = shape, Behaviour = behaviour, Length = new Vector2(0.3f, 0.4f), GroupSize = new Vector2Int(12, 12),
				Spread = 3f, Speed = 1f, Roam = new Vector2(10f, 20f), Depth = new Vector2(1f, 20f), MinWater = 1f, FloorClearance = new Vector2(0.5f, 2f),
			};
		}

		private static List<SeaInstance> Evaluate(SeaCreatureKind kind, SeaGroup group, double seconds, ISeaGround ground, float level = 0f, float night = 0f)
		{
			var list = new List<SeaInstance>();
			SeaLifePlacement.Evaluate(group, kind, seconds, level - 0.5f, ground, 1f, list, level, night);
			return list;
		}

		private static void AssertSame(List<SeaInstance> a, List<SeaInstance> b, string what)
		{
			Assert.That(b.Count, Is.EqualTo(a.Count), what);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.That(b[i].Matrix, Is.EqualTo(a[i].Matrix), what);
				Assert.That(b[i].Anim, Is.EqualTo(a[i].Anim), what);
				Assert.That(b[i].Surface, Is.EqualTo(a[i].Surface), what);
			}
		}

		// ── Defaults and values ───────────────────────────────────────

		[Test]
		public void Enums_AreAppended_AndTheDefaultsHoldNothingNew()
		{
			Assert.That((int)SeaCreatureShape.Eel, Is.EqualTo(8));
			Assert.That((int)SeaCreatureShape.Mola, Is.EqualTo(9));
			Assert.That((int)SeaCreatureShape.Billfish, Is.EqualTo(10));
			Assert.That((int)SeaCreatureShape.Nautilus, Is.EqualTo(11));
			Assert.That((int)SeaCreatureShape.Dugong, Is.EqualTo(12));
			Assert.That((int)SeaCreatureShape.Sawfish, Is.EqualTo(13));
			Assert.That((int)SeaCreatureShape.Mudskipper, Is.EqualTo(14));
			Assert.That((int)SeaCreatureBehaviour.Lurk, Is.EqualTo(5));
			Assert.That((int)SeaCreatureBehaviour.Glide, Is.EqualTo(6));
			Assert.That((int)SeaCreatureBehaviour.SurfaceFloat, Is.EqualTo(7));
			Assert.That((int)SeaCreatureBehaviour.Skip, Is.EqualTo(8));
			Assert.That((int)SeaCreatureBehaviour.Settle, Is.EqualTo(9));
			Assert.That((int)SeaCreatureBehaviour.DielMigrate, Is.EqualTo(10));
			Assert.That((int)SeaCreatureBehaviour.Mill, Is.EqualTo(11));
			foreach (SeaCreatureKind kind in SeaLifeSettings.Defaults())
			{
				Assert.That((int)kind.Shape, Is.LessThan((int)SeaCreatureShape.Mola), kind.Name);
				Assert.That((int)kind.Behaviour, Is.LessThan((int)SeaCreatureBehaviour.Glide), kind.Name);
				Assert.That(kind.GlowStrength, Is.EqualTo(0f), kind.Name + " has no light of its own");
				Assert.That(kind.Boils || kind.NightSpread || kind.HammerHead, Is.False, kind.Name);
				Assert.That(SeaLifeSystem.EmitColour(kind), Is.EqualTo(Vector4.zero), kind.Name);
			}
		}

		/// <summary>A default kind draws exactly as before: no swell ride and no light on any instance.</summary>
		[Test]
		public void DefaultKinds_RideNoSwell_AndShineNot()
		{
			var ground = new FlatGround();
			SeaCreatureKind[] kinds = SeaLifeSettings.Defaults();
			foreach (SeaCreatureKind kind in kinds)
			{
				SeaGroup group = Group(kind, new Vector3(0f, SeaLifePlacement.OnFloor(kind) ? ground.Floor : -10f, 0f), 6);
				foreach (SeaInstance s in Evaluate(kind, group, 1234.5, ground))
				{
					Assert.That(s.Surface, Is.EqualTo(Vector4.zero), kind.Name);
				}
			}
		}

		// ── Determinism ───────────────────────────────────────────────

		[TestCase(SeaCreatureBehaviour.School)]
		[TestCase(SeaCreatureBehaviour.Cruise)]
		[TestCase(SeaCreatureBehaviour.Hover)]
		[TestCase(SeaCreatureBehaviour.Drift)]
		[TestCase(SeaCreatureBehaviour.Crawl)]
		[TestCase(SeaCreatureBehaviour.Lurk)]
		[TestCase(SeaCreatureBehaviour.Glide)]
		[TestCase(SeaCreatureBehaviour.SurfaceFloat)]
		[TestCase(SeaCreatureBehaviour.Skip)]
		[TestCase(SeaCreatureBehaviour.Settle)]
		[TestCase(SeaCreatureBehaviour.DielMigrate)]
		[TestCase(SeaCreatureBehaviour.Mill)]
		public void NewBehaviours_AreAPureFunctionOfTheClock(SeaCreatureBehaviour behaviour)
		{
			var ground = new FlatGround();
			if (behaviour == SeaCreatureBehaviour.Skip)
			{
				ground.Floor = 0.5f;
			}
			SeaCreatureKind kind = Kind(behaviour, behaviour == SeaCreatureBehaviour.SurfaceFloat || behaviour == SeaCreatureBehaviour.Settle ? SeaCreatureShape.Jelly : SeaCreatureShape.Fish);
			kind.Boils = true;
			kind.GlowStrength = 2f;
			kind.GlowPattern = AmbientGlowPattern.FlashTrain;
			SeaGroup group = Group(kind, new Vector3(5f, SeaLifePlacement.OnFloor(kind) ? ground.Floor : -10f, -3f));
			foreach (double seconds in new[] { 0.0, 61.25, 86400.0 * 400 + 3.5 })
			{
				List<SeaInstance> a = Evaluate(kind, group, seconds, ground, 0f, 0.7f);
				Evaluate(kind, group, seconds + 977.0, ground, 0f, 0.2f);
				List<SeaInstance> b = Evaluate(kind, group, seconds, ground, 0f, 0.7f);
				AssertSame(a, b, $"{behaviour} at {seconds}");
			}
		}

		// ── Glide ─────────────────────────────────────────────────────

		[Test]
		public void Glide_IsContinuous_AndFliesOverTheSurface()
		{
			const double period = 40.0;
			const float air = 0.08f;
			float previous = -1f;
			SeaLifePlacement.GlideStage last = SeaLifePlacement.GlideStage.Schooling;
			int stages = 0;
			for (int i = 0; i <= 4000; i++)
			{
				double t = i * period / 4000.0;
				SeaLifePlacement.GlideStage stage = SeaLifePlacement.GlideAt(t, period, 0.1f, air, out float g);
				Assert.That(g, Is.InRange(0f, 1f));
				if (stage != last)
				{
					stages++;
					last = stage;
				}
				previous = g;
			}
			Assert.That(stages, Is.GreaterThanOrEqualTo(4), "every stage is passed through in a round");
			Assert.That(previous, Is.GreaterThanOrEqualTo(0f));

			var ground = new FlatGround();
			SeaCreatureKind kind = Kind(SeaCreatureBehaviour.Glide);
			kind.Depth = new Vector2(0.5f, 3f);
			SeaGroup group = Group(kind, new Vector3(0f, -1.5f, 0f), 40);
			bool flew = false;
			for (int i = 0; i < 600 && !flew; i++)
			{
				foreach (SeaInstance s in Evaluate(kind, group, i * 0.5, ground))
				{
					if (s.Matrix.GetColumn(3).y > 0.3f)
					{
						flew = true;
					}
				}
			}
			Assert.That(flew, Is.True, "some fish of forty is in the air within five minutes");
		}

		// ── Surface float ─────────────────────────────────────────────

		[Test]
		public void SurfaceFloaters_LieAtTheWaterline_AndRideTheSwell()
		{
			var ground = new FlatGround();
			SeaCreatureKind kind = Kind(SeaCreatureBehaviour.SurfaceFloat, SeaCreatureShape.Jelly);
			SeaGroup group = Group(kind, new Vector3(0f, -2f, 0f));
			List<SeaInstance> list = Evaluate(kind, group, 500.0, ground, 1.25f);
			Assert.That(list, Is.Not.Empty);
			foreach (SeaInstance s in list)
			{
				Assert.That(s.Surface.x, Is.EqualTo(1f));
				Assert.That(s.Matrix.GetColumn(3).y, Is.EqualTo(1.25f).Within(0.1f), "at the tide of the moment");
			}
		}

		// ── Skip ──────────────────────────────────────────────────────

		[Test]
		public void Skippers_AreDrawnOnlyOnTheBareMud()
		{
			SeaCreatureKind kind = Kind(SeaCreatureBehaviour.Skip, SeaCreatureShape.Mudskipper);
			var mud = new FlatGround { Floor = 0.6f };
			SeaGroup group = Group(kind, new Vector3(0f, mud.Floor, 0f));
			Assert.That(Evaluate(kind, group, 100.0, mud, 0f), Is.Not.Empty, "the tide is out: on the mud");
			Assert.That(Evaluate(kind, group, 100.0, mud, 1f), Is.Empty, "the tide is in over the mud");
			var high = new FlatGround { Floor = 3f };
			Assert.That(Evaluate(kind, Group(kind, new Vector3(0f, 3f, 0f)), 100.0, high, 0f), Is.Empty, "more than two metres above the water");
		}

		// ── Settle ────────────────────────────────────────────────────

		[Test]
		public void SettledJellies_LieBellDown_OnTheBed()
		{
			var ground = new FlatGround { Floor = -3f };
			SeaCreatureKind kind = Kind(SeaCreatureBehaviour.Settle, SeaCreatureShape.Jelly);
			List<SeaInstance> list = Evaluate(kind, Group(kind, new Vector3(0f, -3f, 0f)), 10.0, ground);
			Assert.That(list, Is.Not.Empty);
			foreach (SeaInstance s in list)
			{
				Assert.That(s.Matrix.GetColumn(3).y, Is.LessThan(-2.8f), "on the bed");
				Assert.That(((Vector3)s.Matrix.GetColumn(1)).normalized.y, Is.LessThan(-0.99f), "bell down");
			}
		}

		// ── Diel migration, boils, mill, night spread ────────────────

		[Test]
		public void Migrators_RiseAtNight()
		{
			Assert.That(SeaLifePlacement.NightShare(12.0), Is.EqualTo(0f));
			Assert.That(SeaLifePlacement.NightShare(0.0), Is.EqualTo(1f));
			Assert.That(SeaLifePlacement.NightShare(23.0), Is.EqualTo(1f));
			Assert.That(SeaLifePlacement.NightShare(19.5), Is.InRange(0.01f, 0.99f));
			var ground = new FlatGround { Floor = -800f };
			SeaCreatureKind kind = Kind(SeaCreatureBehaviour.DielMigrate);
			kind.Depth = new Vector2(20f, 400f);
			SeaGroup group = Group(kind, new Vector3(0f, -200f, 0f));
			float Mean(float night)
			{
				float sum = 0f;
				List<SeaInstance> list = Evaluate(kind, group, 300.0, ground, 0f, night);
				foreach (SeaInstance s in list)
				{
					sum += s.Matrix.GetColumn(3).y;
				}
				return sum / Mathf.Max(1, list.Count);
			}
			Assert.That(Mean(0f), Is.LessThan(-300f), "deep by day");
			Assert.That(Mean(1f), Is.GreaterThan(-40f), "near the surface by night");
		}

		[Test]
		public void Boils_ComeAndGo_OnTheClock()
		{
			int boiling = 0;
			for (int i = 0; i < 4000; i++)
			{
				float b = SeaLifePlacement.BoilAt(i * 1.0, 0xB0B0u);
				Assert.That(b, Is.InRange(0f, 1f));
				if (b > 0f)
				{
					boiling++;
				}
			}
			Assert.That(boiling / 4000f, Is.EqualTo(SeaLifePlacement.BoilShare).Within(0.05f));
		}

		[Test]
		public void Mill_TurnsRoundOneAxis()
		{
			var ground = new FlatGround();
			SeaCreatureKind kind = Kind(SeaCreatureBehaviour.Mill);
			SeaGroup group = Group(kind, new Vector3(0f, -10f, 0f), 30);
			List<SeaInstance> list = Evaluate(kind, group, 42.0, ground);
			Assert.That(list.Count, Is.EqualTo(30));
			Vector3 centre = Vector3.zero;
			foreach (SeaInstance s in list)
			{
				centre += (Vector3)s.Matrix.GetColumn(3);
			}
			centre /= list.Count;
			foreach (SeaInstance s in list)
			{
				Vector3 at = (Vector3)s.Matrix.GetColumn(3) - centre;
				Vector3 forward = ((Vector3)s.Matrix.GetColumn(2)).normalized;
				at.y = 0f;
				Assert.That(Mathf.Abs(Vector3.Dot(at.normalized, forward)), Is.LessThan(0.6f), "swimming round, not out");
			}
		}

		[Test]
		public void NightSpreaders_SpreadOutAfterDark()
		{
			var ground = new FlatGround();
			SeaCreatureKind kind = Kind(SeaCreatureBehaviour.School, SeaCreatureShape.Shark);
			kind.NightSpread = true;
			SeaGroup group = Group(kind, new Vector3(0f, -10f, 0f), 20);
			float Spread(float night)
			{
				List<SeaInstance> list = Evaluate(kind, group, 77.0, ground, 0f, night);
				Vector3 c = Vector3.zero;
				foreach (SeaInstance s in list)
				{
					c += (Vector3)s.Matrix.GetColumn(3);
				}
				c /= list.Count;
				float r = 0f;
				foreach (SeaInstance s in list)
				{
					Vector3 d = (Vector3)s.Matrix.GetColumn(3) - c;
					d.y = 0f;
					r += d.magnitude;
				}
				return r / list.Count;
			}
			Assert.That(Spread(1f), Is.GreaterThan(Spread(0f) * 2f));
		}

		// ── Creature light ────────────────────────────────────────────

		[Test]
		public void CreatureLight_FollowsTheAmbientRhythms()
		{
			var ground = new FlatGround();
			SeaCreatureKind kind = Kind(SeaCreatureBehaviour.School);
			kind.GlowStrength = 3f;
			kind.GlowPattern = AmbientGlowPattern.Pulse;
			kind.GlowRest = 0.2f;
			Assert.That(SeaLifeSystem.EmitColour(kind).w, Is.EqualTo(1f));
			foreach (SeaInstance s in Evaluate(kind, Group(kind, new Vector3(0f, -10f, 0f)), 321.0, ground))
			{
				Assert.That(s.Surface.y, Is.InRange(0.2f, 1f), "between its rest and full");
				Assert.That(s.Surface.z, Is.InRange(0f, 1f));
			}
		}

		// ── Seabirds offshore ─────────────────────────────────────────

		/// <summary>Sea everywhere but an island of land within <see cref="Island"/> m of the origin.</summary>
		private sealed class IslandWorld : IAmbientWorld
		{
			public float Island = 300f;

			public bool TryGround(float x, float z, out float y)
			{
				y = x * x + z * z < Island * Island ? 5f : -40f;
				return true;
			}

			public float WaterAt(float x, float z, out bool sea)
			{
				sea = true;
				return 0f;
			}

			public float LiveWaterAt(float x, float z) => 0f;

			public void Habitat(Vector3 position, out AmbientHabitat habitat, out float temperature)
			{
				habitat = AmbientHabitat.Sea;
				temperature = 0.4f;
			}
		}

		[Test]
		public void Offshore_KeepsSeabirdsFarFromLand()
		{
			var world = new IslandWorld();
			Assert.That(AmbientLifeMotion.IsOffshore(world, new Vector3(3000f, 0f, 0f), 2000f), Is.True, "2 km and 1 km out: open sea only");
			Assert.That(AmbientLifeMotion.IsOffshore(world, new Vector3(1800f, 0f, 0f), 2000f), Is.False, "the island lies 2 km away on one bearing");
			Assert.That(AmbientLifeMotion.IsOffshore(world, new Vector3(1100f, 0f, 0f), 2000f), Is.False, "half the distance reaches the island");
			foreach (AmbientCreatureKind kind in AmbientLifeCatalogue.Defaults())
			{
				Assert.That(kind.Offshore, Is.EqualTo(0f), kind.Name + ": no default kind is offshore-only (kinds are the rounds')");
			}
		}
	}
}
