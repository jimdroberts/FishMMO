using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The caves' light-makers (2026-10-20: "Bioluminescent creatures should also exist in caves"; <see cref="AmbientGlow"/>): that
	/// they live in an ordinary cave (its rock at the ground's year mean, about 8–12 °C) and in the forms they are of, that each
	/// one's light is a pure function of the shared clock and its own seed, that nothing else glows, and that the cut gives them
	/// homes low enough to be seen.
	/// </summary>
	[TestFixture]
	public class AmbientGlowLifeTests
	{
		private static readonly AmbientCreatureKind[] Kinds = AmbientLifeCatalogue.Defaults();

		/// <summary>The light-makers with an emission of their own (a swarm's light is its swarm mode's: the spore motes).</summary>
		private static readonly string[] Glowing =
		{
			"Lantern beetles", "Glow millipedes", "Glowing snails", "Cave lanternfish", "Lantern jellies", "Wisp moths", "Lantern newts", "Ember mites", "Ice worms",
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

		/// <summary>The first spot kind of a niche, and the first form of a kind's forms.</summary>
		private static SceneCaveLifeKind FirstNiche(AmbientCaveNiche niche)
		{
			for (int i = 0; i < 8; i++)
			{
				if (((int)niche & (1 << i)) != 0)
				{
					return (SceneCaveLifeKind)i;
				}
			}
			return SceneCaveLifeKind.Floor;
		}

		private static SceneCaveForm FirstForm(AmbientCaveForms forms)
		{
			for (int i = 0; i < 8; i++)
			{
				if (((int)forms & (1 << i)) != 0)
				{
					return (SceneCaveForm)i;
				}
			}
			return SceneCaveForm.Cave;
		}

		private static SceneCaveLifeSpot SpotFor(AmbientCreatureKind kind)
		{
			SceneCaveLifeKind niche = FirstNiche(kind.CaveNiche);
			var spot = new SceneCaveLifeSpot
			{
				Kind = niche,
				Form = FirstForm(kind.CaveForms),
				Flags = SceneCaveLifeFlags.Damp,
				Dark = Mathf.Clamp01(0.5f * (kind.CaveDark.x + kind.CaveDark.y)),
				Position = new Vector3(10f, 50f, 20f),
				Normal = Vector3.up,
				Extent = new Vector3(1.5f, 4f, 0f),
			};
			if (niche == SceneCaveLifeKind.Flight || niche == SceneCaveLifeKind.Glow)
			{
				spot.Extent = new Vector3(2f, 0.7f, 2f);
			}
			else if (niche == SceneCaveLifeKind.Pool)
			{
				spot.Extent = new Vector3(2f, 0.8f, 0f);
			}
			return spot;
		}

		// ── Who ───────────────────────────────────────────────────────

		[Test]
		public void TheLightMakersLiveInATypicalCave()
		{
			// A cave's rock is the ground's year mean: 8-12 °C under most temperate country.
			foreach (float celsius in new[] { 8f, 10f, 12f })
			{
				foreach (string name in new[] { "Lantern beetles", "Glow millipedes", "Glowing snails", "Spore motes", "Cave lanternfish", "Lantern jellies", "Wisp moths", "Lantern newts" })
				{
					AmbientCreatureKind kind = Kind(name);
					SceneCaveLifeSpot spot = SpotFor(kind);
					Assert.That(spot.Form, Is.EqualTo(SceneCaveForm.Cave), $"{name} lives in an ordinary cave");
					Assert.That(AmbientCaveLife.Weight(kind, spot, AmbientHabitat.Mountain, C(celsius)), Is.GreaterThan(0.9f), $"{name} at home in a {celsius} °C cave");
					Assert.That(AmbientCaveLife.Activity(kind, default, C(celsius), 1f), Is.GreaterThan(0.9f), $"{name} about in the dark at {celsius} °C");
				}
			}
			// The forms' own: the lava tubes' ember mites, the ice caves' worms (cold-trap ice caves keep their ice under a ground
			// year mean of a few degrees; the worms die past about 5 °C, and no ice cave is 12 °C).
			SceneCaveLifeSpot tube = SpotFor(Kind("Ember mites"));
			Assert.That(tube.Form, Is.EqualTo(SceneCaveForm.LavaTube));
			Assert.That(AmbientCaveLife.Weight(Kind("Ember mites"), tube, AmbientHabitat.Volcanic, C(10f)), Is.GreaterThan(0.4f), "a lava tube's few: its share");
			tube.Form = SceneCaveForm.Cave;
			Assert.That(AmbientCaveLife.Weight(Kind("Ember mites"), tube, AmbientHabitat.Mountain, C(10f)), Is.Zero, "only in the lava tubes");
			SceneCaveLifeSpot ice = SpotFor(Kind("Ice worms"));
			Assert.That(ice.Form, Is.EqualTo(SceneCaveForm.IceCave));
			Assert.That(AmbientCaveLife.Weight(Kind("Ice worms"), ice, AmbientHabitat.Ice, C(0f)) * Kind("Ice worms").CavePerSpot, Is.GreaterThan(0.5f),
				"an ice cave's worms: more than half a group a spot for all the ice cave's thin share");
			Assert.That(AmbientCaveLife.Weight(Kind("Ice worms"), ice, AmbientHabitat.Ice, C(14f)), Is.Zero, "a warm cave melts them");
			Assert.That(AmbientCaveLife.Activity(Kind("Ice worms"), default, C(-6f), 1f), Is.GreaterThan(0.9f), "and they are about well below freezing");
			// The wet's: snails and newts only on damp rock; the fresh water's fish never in the sea's.
			SceneCaveLifeSpot dry = SpotFor(Kind("Glowing snails"));
			dry.Flags = SceneCaveLifeFlags.None;
			Assert.That(AmbientCaveLife.Weight(Kind("Glowing snails"), dry, AmbientHabitat.Mountain, C(10f)), Is.Zero, "a snail needs the wet");
			SceneCaveLifeSpot sea = SpotFor(Kind("Cave lanternfish"));
			sea.Form = SceneCaveForm.Grotto;
			sea.Flags = SceneCaveLifeFlags.Damp | SceneCaveLifeFlags.Salt;
			Assert.That(AmbientCaveLife.Weight(Kind("Cave lanternfish"), sea, AmbientHabitat.Coast, C(10f)), Is.Zero, "no lanternfish in the sea");
			Assert.That(AmbientCaveLife.Weight(Kind("Lantern jellies"), sea, AmbientHabitat.Coast, C(10f)), Is.GreaterThan(0.9f), "the jellies' kin in the sea cave's pools");
		}

		[Test]
		public void ACaveHoldsItsLightMakers()
		{
			// Floors, walls, glow air and pools of a mild limestone cave, as the cut gives them: every light-maker of the cave form placed.
			var world = new GlowWorld();
			var groups = new List<AmbientGroup>();
			var kinds = new HashSet<string>();
			var niches = new[] { SceneCaveLifeKind.Floor, SceneCaveLifeKind.Wall, SceneCaveLifeKind.Glow, SceneCaveLifeKind.Pool, SceneCaveLifeKind.Flight };
			int index = 0;
			foreach (SceneCaveLifeKind niche in niches)
			{
				for (int i = 0; i < 40; i++, index++)
				{
					var spot = new SceneCaveLifeSpot
					{
						Kind = niche, Form = SceneCaveForm.Cave, Flags = i % 2 == 0 ? SceneCaveLifeFlags.Damp : SceneCaveLifeFlags.None, Dark = 1f,
						Position = new Vector3(i, 40f, 0f), Normal = Vector3.up, Extent = niche == SceneCaveLifeKind.Pool ? new Vector3(2f, 0.8f, 0f) : new Vector3(2f, 1f, 2f),
					};
					if (niche == SceneCaveLifeKind.Pool)
					{
						spot.Flags = SceneCaveLifeFlags.Damp;
					}
					for (int k = 0; k < Kinds.Length; k++)
					{
						AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0x6100u, index, spot, i + 1, world, groups);
					}
				}
			}
			foreach (AmbientGroup g in groups)
			{
				kinds.Add(Kinds[g.Kind].Name);
			}
			foreach (string name in new[] { "Lantern beetles", "Glow millipedes", "Glowing snails", "Spore motes", "Cave lanternfish", "Lantern jellies", "Wisp moths", "Lantern newts" })
			{
				Assert.That(kinds, Does.Contain(name), $"{name} placed in a mild cave");
			}
			Assert.That(kinds, Does.Not.Contain("Ember mites").And.Not.Contain("Ice worms"), "the lava tubes' and the ice caves' own");
		}

		// ── The light ─────────────────────────────────────────────────

		[Test]
		public void EmissionIsOnlyTheLightMakers()
		{
			var glowing = new HashSet<string>(Glowing);
			foreach (AmbientCreatureKind kind in Kinds)
			{
				bool glows = glowing.Contains(kind.Name);
				Assert.That(AmbientGlow.Glows(kind), Is.EqualTo(glows), $"{kind.Name} emission");
				Vector4 colour = AmbientGlow.EmitColour(kind), pattern = AmbientGlow.Pattern(kind);
				if (!glows)
				{
					Assert.That(colour, Is.EqualTo(Vector4.zero), $"{kind.Name} gives off no light");
					Assert.That(pattern, Is.EqualTo(Vector4.zero), kind.Name);
					Assert.That(AmbientGlow.At(kind, 12345u, 98765.4), Is.EqualTo(Vector4.zero), kind.Name);
					continue;
				}
				Assert.That(colour.w, Is.EqualTo(1f), $"{kind.Name} has its light on");
				Assert.That(Mathf.Max(colour.x, Mathf.Max(colour.y, colour.z)), Is.GreaterThan(1f), $"{kind.Name}: past one, so the bloom catches it in the dark");
				Assert.That(kind.GroupsPerKm2, Is.Zero, $"{kind.Name} only underground");
				Assert.That(kind.CastShadows, Is.False, kind.Name);
			}
			Assert.That(AmbientGlow.Luminous(Kind("Spore motes")), Is.True, "the motes' light is their swarm's");
			Assert.That(AmbientGlow.Glows(Kind("Spore motes")), Is.False);
			Assert.That(AmbientGlow.Luminous(Kind("Fungus gnats")), Is.True);
			Assert.That(AmbientGlow.Luminous(Kind("Cave crickets")), Is.False);
		}

		[Test]
		public void TheLightIsThePureFunctionOfTheClockAndTheAnimal()
		{
			foreach (string name in Glowing)
			{
				AmbientCreatureKind kind = Kind(name);
				for (double t = 1.0e6; t < 1.0e6 + 120.0; t += 0.37)
				{
					Vector4 a = AmbientGlow.At(kind, 0xABCDu, t), b = AmbientGlow.At(kind, 0xABCDu, t);
					Assert.That(a, Is.EqualTo(b), $"{name}: the same moment, the same light");
					Assert.That(a.x, Is.InRange(0f, 1f), $"{name} phase");
					Assert.That(a.y, Is.InRange(Mathf.Min(kind.GlowRest, 0.6f) - 1e-4f, 1f + 1e-4f), $"{name} brightness between its rest and its full");
				}
				// Far into the world's life (a year of seconds and more) it is still the same function, at full precision.
				double late = 4.0e8 + 0.25;
				Assert.That(AmbientGlow.At(kind, 7u, late), Is.EqualTo(AmbientGlow.At(kind, 7u, late)));
				// Each animal its own: two seeds out of step most of the time.
				int differ = 0;
				for (double t = 5000.0; t < 5100.0; t += 1.0)
				{
					differ += Mathf.Abs(AmbientGlow.At(kind, 1u, t).x - AmbientGlow.At(kind, 2u, t).x) > 1e-3f ? 1 : 0;
				}
				Assert.That(differ, Is.GreaterThan(90), $"{name}: every animal on its own rhythm");
			}
		}

		[Test]
		public void FlashTrainsFlashAndPulsesBreathe()
		{
			AmbientCreatureKind snail = Kind("Glowing snails");
			Assert.That(snail.GlowPattern, Is.EqualTo(AmbientGlowPattern.FlashTrain));
			int bright = 0, dim = 0, samples = 0, rises = 0;
			bool was = false;
			for (double t = 20000.0; t < 20600.0; t += 0.02)
			{
				float b = AmbientGlow.BrightnessAt(snail, 99u, t);
				bright += b > 0.9f ? 1 : 0;
				dim += b < snail.GlowRest + 0.05f ? 1 : 0;
				bool on = b > 0.5f;
				rises += on && !was ? 1 : 0;
				was = on;
				samples++;
			}
			Assert.That(rises, Is.InRange(150, 450), "two to four flashes every six seconds or so");
			Assert.That(dim, Is.GreaterThan(samples / 2), "dark between the flashes most of the time");
			Assert.That(bright, Is.GreaterThan(0).And.LessThan(samples / 5), "short flashes");
			// A pulse is smooth: never a jump between frames.
			AmbientCreatureKind beetle = Kind("Lantern beetles");
			for (double t = 3000.0; t < 3100.0; t += 0.016)
			{
				Assert.That(Mathf.Abs(AmbientGlow.BrightnessAt(beetle, 5u, t + 0.016) - AmbientGlow.BrightnessAt(beetle, 5u, t)), Is.LessThan(0.03f), "a slow breath");
			}
			// An ember's flicker never settles.
			AmbientCreatureKind mite = Kind("Ember mites");
			float lo = 1f, hi = 0f;
			for (double t = 100.0; t < 130.0; t += 0.05)
			{
				float b = AmbientGlow.BrightnessAt(mite, 3u, t);
				lo = Mathf.Min(lo, b);
				hi = Mathf.Max(hi, b);
			}
			Assert.That(hi - lo, Is.GreaterThan(0.35f), "a flicker");
		}

		[Test]
		public void AJellysLightRidesItsBeat()
		{
			int k = IndexOf("Lantern jellies");
			var spot = new SceneCaveLifeSpot
			{
				Kind = SceneCaveLifeKind.Pool, Form = SceneCaveForm.Cave, Flags = SceneCaveLifeFlags.Damp, Dark = 1f, Position = new Vector3(5f, 30f, 5f),
				Normal = Vector3.up, Extent = new Vector3(2f, 0.9f, 0f),
			};
			var groups = new List<AmbientGroup>();
			for (int i = 0; groups.Count == 0 && i < 200; i++)
			{
				AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0x7E11u, i, spot, 1, new GlowWorld(), groups);
			}
			Assert.That(groups, Is.Not.Empty);
			AmbientGroup g = groups[0];
			g.StateFirst = 0;
			for (double t = 40000.0; t < 40060.0; t += 0.7)
			{
				var frame = new AmbientLifeMotion.Frame
				{
					Seconds = t, World = new GlowWorld(), States = new AmbientMemberState[g.Count], Visible = 1f,
					CaveSpots = new List<SceneCaveLifeSpot> { spot },
				};
				var into = new List<AmbientInstance>();
				AmbientLifeMotion.Evaluate(g, Kinds[k], ref frame, into);
				Assert.That(into.Count, Is.EqualTo(g.Count));
				foreach (AmbientInstance a in into)
				{
					Assert.That(a.Glow.x, Is.EqualTo(a.Anim.x).Within(1e-5f), "the light runs out as the bell squeezes");
					Assert.That(a.Glow.y, Is.GreaterThan(0.5f));
					Vector3 p = a.Matrix.GetColumn(3);
					Assert.That(p.y, Is.LessThan(spot.Position.y), "under the pool's surface");
					Assert.That(p.y, Is.GreaterThan(spot.Position.y - spot.Extent.y), "over its bed");
					Assert.That(Vector3.Dot(a.Matrix.GetColumn(1).normalized, Vector3.up), Is.GreaterThan(0.99f), "bell up");
				}
			}
		}

		[Test]
		public void AGlowingCrawlersInstancesCarryItsLightAndOthersNone()
		{
			var spot = new SceneCaveLifeSpot
			{
				Kind = SceneCaveLifeKind.Wall, Form = SceneCaveForm.Cave, Flags = SceneCaveLifeFlags.Damp, Dark = 1f, Position = new Vector3(0f, 20f, 0f),
				Normal = new Vector3(0.6f, 0.1f, 0.8f).normalized, Extent = new Vector3(1.5f, 3f, 0f),
			};
			foreach ((string name, bool lit) in new[] { ("Glow millipedes", true), ("Lantern newts", true), ("Cave crickets", false) })
			{
				int k = IndexOf(name);
				var groups = new List<AmbientGroup>();
				for (int i = 0; groups.Count == 0 && i < 200; i++)
				{
					AmbientCaveLife.TryPlace(Kinds[k], k, 1f, 0x51DEu, i, spot, 1, new GlowWorld(), groups);
				}
				Assert.That(groups, Is.Not.Empty, name);
				AmbientGroup g = groups[0];
				g.StateFirst = 0;
				var frame = new AmbientLifeMotion.Frame
				{
					Seconds = 12345.6, World = new GlowWorld(), States = new AmbientMemberState[g.Count], Visible = 1f,
					CaveSpots = new List<SceneCaveLifeSpot> { spot },
				};
				var into = new List<AmbientInstance>();
				AmbientLifeMotion.Evaluate(g, Kinds[k], ref frame, into);
				Assert.That(into, Is.Not.Empty, name);
				foreach (AmbientInstance a in into)
				{
					Assert.That(a.Glow.y > 0f, Is.EqualTo(lit), $"{name}'s light");
					Assert.That(a.Glow, Is.EqualTo(AmbientGlow.At(Kinds[k], a.Seed, 12345.6)), $"{name}: each member's own, from its seed");
				}
			}
		}

		[Test]
		public void OnlyTheLightMakersMeshesCarryAMask()
		{
			var glowing = new HashSet<string>(Glowing);
			foreach (AmbientCreatureKind kind in Kinds)
			{
				float[] masks = AmbientCreatureMeshes.MaskOf(kind, out int triangles);
				float most = 0f, lit = 0f;
				foreach (float m in masks)
				{
					Assert.That(m, Is.InRange(0f, 1f), kind.Name);
					most = Mathf.Max(most, m);
					lit += m > 0.5f ? 1f : 0f;
				}
				if (glowing.Contains(kind.Name))
				{
					Assert.That(most, Is.GreaterThan(0.9f), $"{kind.Name}'s lamps");
					// Lamps and lines are a part of the body, not all of it (a whole-body glow is the whole of it).
					if (kind.GlowParts != AmbientGlowParts.Body)
					{
						Assert.That(lit, Is.LessThan(masks.Length), $"{kind.Name}: the light is where its lamps are");
					}
				}
				else
				{
					Assert.That(most, Is.Zero, $"{kind.Name} has no light of its own");
				}
				// The meshes' budget (AmbientLifeTests.Meshes_AreSmallAndCarryTheirParts, which needs the editor).
				Assert.That(masks.Length, Is.InRange(30, 700), $"{kind.Name} vertices");
				Assert.That(triangles, Is.InRange(20, 900), $"{kind.Name} triangles");
			}
		}

		/// <summary>A world of mild temperate country (10 °C) with the home world's air.</summary>
		private sealed class GlowWorld : IAmbientWorld
		{
			public AmbientWorld World => AmbientWorld.Earthly;

			public bool TryGround(float x, float z, out float y)
			{
				y = 120f;
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
				habitat = AmbientHabitat.Mountain | AmbientHabitat.Woodland;
				temperature = AmbientLifeCatalogue.Celsius(10f);
			}
		}
	}

	/// <summary>
	/// The light-makers' homes the cut bakes (<see cref="CaveLifeBake"/>, 2026-10-20): low glow air in a natural cave a person walks
	/// under (round-2's lesson: a roost 19 m up is never seen), the same for the same system, within the spot budget.
	/// </summary>
	[TestFixture]
	public class CaveLightHomesTests
	{
		/// <summary>CaveLifeBakeTests' escarpment: a foot at 70 m, a face rising 60 m over 30 m, a rolling plateau behind.</summary>
		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return 60f + face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		private static CaveSystem Plan(CaveForm form, int size, int seed)
		{
			float tilt = seed * 13f % 40f - 20f;
			CaveGround ground = CaveGround.Sample(Escarpment(tilt, seed), -560f, -560f, 560f, 560f, 2f);
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed, ground, 0f, out string problem, float.NaN);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			return CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed);
		}

		[Test]
		public void ACaveHasLowGlowAirAndThePatchesByIt()
		{
			int low = 0, boxes = 0;
			foreach ((CaveForm form, int size, int seed) in new[] { (CaveForm.Cave, 1, 3), (CaveForm.Cave, 2, 6), (CaveForm.Mine, 1, 5) })
			{
				CaveSystem system = Plan(form, size, seed);
				List<SceneCaveLifeSpot> spots = CaveLifeBake.Bake(system, seed);
				List<SceneCaveLifeSpot> again = CaveLifeBake.Bake(CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed), seed);
				Assert.That(again.Count, Is.EqualTo(spots.Count), $"{form}: the same homes every bake");
				Assert.That(spots.Count, Is.LessThanOrEqualTo(CaveLifeBake.MaxSpots));
				for (int i = 0; i < spots.Count; i++)
				{
					Assert.That(again[i].Position, Is.EqualTo(spots[i].Position), $"{form} spot {i}");
					if (spots[i].Kind != SceneCaveLifeKind.Glow)
					{
						continue;
					}
					boxes++;
					// How high over the floor under it the box's middle hangs.
					Vector3 local = spots[i].Position - system.Origin;
					float floor = local.y;
					while (floor > local.y - 40f && system.Void(new Vector3(local.x, floor - 0.1f, local.z)) < 0f)
					{
						floor -= 0.1f;
					}
					float over = local.y - floor;
					low += over <= 4f ? 1 : 0;
					Assert.That(over, Is.GreaterThan(spots[i].Extent.y), $"{form}: glow air clear of the floor");
				}
			}
			Assert.That(boxes, Is.GreaterThan(0), "some glow air");
			Assert.That(low, Is.GreaterThan(2), "glow air low over the floor, where it is seen");
		}
	}
}
