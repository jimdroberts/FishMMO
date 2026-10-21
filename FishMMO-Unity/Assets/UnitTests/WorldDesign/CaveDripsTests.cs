using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A cave's drips (2026-10-20, caves livened): their emitter kind appended and never taken for a light; the seed's packing; the
	/// drops' fall and beat as pure functions of the shared clock (<see cref="SceneDrip"/>) with never more than three in the air; the
	/// dressing's drips (<see cref="CaveDrips"/>) the same every time, within budget, spaced, landing in the void on floors that hold
	/// their puddles, refused where the caller says, few in a dry lava tube; the client's numbers for the shader
	/// (<see cref="CaveDripField.Fill"/>) agreeing with the beat; and the drip sounds (<see cref="DripSounds"/>).
	/// </summary>
	[TestFixture]
	public class CaveDripsTests
	{
		/// <summary>A foot at 70 m, a face rising 60 m over 30 m, a rolling plateau behind (CaveHabitatTests' escarpment).</summary>
		private static float Escarpment(float x, float z)
		{
			float t = Mathf.Clamp01(z / 30f);
			float face = 70f + 60f * (t * t * (3f - 2f * t));
			float roll = 3f * Mathf.Sin(x * 0.05f + 1f) + 2f * Mathf.Sin(z * 0.031f + 0.7f) * Mathf.Clamp01(z / 40f);
			return face + roll * Mathf.Clamp01((z + 10f) / 30f) - 0.04f * Mathf.Max(0f, -z);
		}

		private static CaveSystem Plan(CaveForm form, int size, int seed)
		{
			CaveGround ground = CaveGround.Sample(Escarpment, -560f, -560f, 560f, 560f, 2f);
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), 180f, seed * 7919, seed, ground, 0f, out string problem);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			return CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed);
		}

		/// <summary>A stalactite half a metre long over the middle of each chamber, hung from the roof found there.</summary>
		private static List<CaveDripHang> Stalactites(CaveSystem system)
		{
			var hangs = new List<CaveDripHang>();
			foreach (CaveChamber chamber in system.Chambers)
			{
				var p = new Vector3(chamber.Centre.x, chamber.Floor + 0.8f, chamber.Centre.z);
				if (CaveDrips.RoofAbove(system, p, 40f, out float roof))
				{
					hangs.Add(new CaveDripHang { Tip = new Vector3(p.x, roof - 0.5f, p.z), Roof = roof, LandOn = float.NaN });
				}
			}
			return hangs;
		}

		private static readonly int[] Seeds = { 3, 8, 13 };

		// ── The kind ──────────────────────────────────────────────────

		[Test]
		public void TheDripKind_IsAppended_AndNoLightPoolTakesItForALamp()
		{
			string[] names = Enum.GetNames(typeof(SceneEmitterKind));
			Assert.That(Array.IndexOf(names, "Drip"), Is.EqualTo(12), "appended after Fumes (11)");
			Assert.That((int)SceneEmitterKind.Fumes, Is.EqualTo(11));
			string pool = File.ReadAllText("Assets/Scripts/Client/World/Terrain/SiteLightPool.cs");
			Assert.That(pool, Does.Contain("kind != SceneEmitterKind.Drip"), "the client's site lights skip drips");
			string walk = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Shapers/CaveWalkRender.cs");
			Assert.That(walk, Does.Contain("x.Kind != SceneEmitterKind.Drip"), "the cave walk render's lights skip drips");
			Assert.That(Enum.GetNames(typeof(SceneDripLanding)), Is.EqualTo(new[] { "Puddle", "Rock", "Tip" }), "append only");
		}

		[Test]
		public void ASeed_CarriesItsBeat_ItsPuddle_AndItsLanding()
		{
			uint seed = SceneDrip.Seed(0xABCDEu, 2.35f, 0.62f, SceneDripLanding.Puddle);
			Assert.That(SceneDrip.IntervalOf(seed), Is.EqualTo(2.35f).Within(1e-4f));
			Assert.That(SceneDrip.PuddleOf(seed), Is.EqualTo(0.62f).Within(1e-4f));
			Assert.That(SceneDrip.LandingOf(seed), Is.EqualTo(SceneDripLanding.Puddle));
			Assert.That(seed & 0xFFFu, Is.EqualTo(0xCDEu), "its own hash in the low bits");
			uint rock = SceneDrip.Seed(7u, 99f, 0.5f, SceneDripLanding.Rock);
			Assert.That(SceneDrip.PuddleOf(rock), Is.EqualTo(0f), "no puddle on rock");
			Assert.That(SceneDrip.IntervalOf(rock), Is.EqualTo(SceneDrip.LeastInterval + 255f * SceneDrip.IntervalStep).Within(1e-4f), "clamped to the slowest stored");
			Assert.That(SceneDrip.LandingOf(SceneDrip.Seed(7u, 1f, 0f, SceneDripLanding.Tip)), Is.EqualTo(SceneDripLanding.Tip));
			Assert.That(SceneDrip.PuddleOf(SceneDrip.Seed(7u, 1f, 5f, SceneDripLanding.Puddle)), Is.EqualTo(SceneDrip.MostPuddle).Within(1e-4f));
		}

		// ── The fall and the beat ─────────────────────────────────────

		[Test]
		public void TheFall_IsFreeFallAtFirst_AndNeverPastItsTerminalSpeed()
		{
			Assert.That(SceneDrip.FallenMetres(0.2f), Is.EqualTo(0.5f * 9.81f * 0.04f).Within(0.01f), "a fifth of a second: free fall");
			foreach (float h in new[] { 0.6f, 2f, 7.5f, 20f, 45f, 60f })
			{
				float t = SceneDrip.FallSeconds(h);
				Assert.That(SceneDrip.FallenMetres(t), Is.EqualTo(h).Within(1e-3f * h + 1e-3f), $"{h} m: the fall's inverse");
				Assert.That(t, Is.GreaterThanOrEqualTo(Mathf.Sqrt(2f * h / 9.81f) - 1e-4f), "drag only slows it");
				Assert.That(SceneDrip.SpeedAt(t), Is.LessThanOrEqualTo(SceneDrip.TerminalSpeed));
			}
			Assert.That(SceneDrip.FallSeconds(30f), Is.InRange(3.5f, 4.5f), "thirty metres at a drop's terminal speed");
		}

		[Test]
		public void TheBeat_IsAPureFunctionOfTheClock_EvenFarIntoTheWorld()
		{
			foreach (double start in new[] { 12.5, 1.9e9 + 0.123, 3.1e10 })
			{
				uint seed = SceneDrip.Seed(0x5A5u, 1.7f, 0.4f, SceneDripLanding.Puddle);
				// Every landing seen from one moment is seen at the same instant from any other moment.
				var seen = new List<double>();
				for (double t = start; t < start + 20.0; t += 0.37)
				{
					SceneDrip.ClockAt(t, seed, out long cycle, out float into);
					Assert.That(into, Is.InRange(0f, SceneDrip.IntervalOf(seed)));
					for (int m = -1; m <= 2; m++)
					{
						seen.Add(t + SceneDrip.SecondsToLanding(seed, cycle, into, m));
					}
				}
				seen.Sort();
				// The same landing seen from many moments agrees to the millisecond: merged, then no two closer than the jitter allows.
				var landings = new List<double>();
				foreach (double at in seen)
				{
					if (landings.Count == 0 || at - landings[landings.Count - 1] > 0.002)
					{
						landings.Add(at);
					}
					else
					{
						Assert.That(at - landings[landings.Count - 1], Is.LessThan(0.002), $"start {start}");
					}
				}
				double[] gaps = landings.Zip(landings.Skip(1), (a, b) => b - a).ToArray();
				Assert.That(gaps.Min(), Is.GreaterThanOrEqualTo((1.0 - 2.0 * SceneDrip.Jitter) * SceneDrip.IntervalOf(seed) - 0.003), $"start {start}");
				Assert.That(gaps.Max(), Is.LessThanOrEqualTo((1.0 + 2.0 * SceneDrip.Jitter) * SceneDrip.IntervalOf(seed) + 0.003), $"start {start}");
				Assert.That(landings.Count, Is.GreaterThanOrEqualTo(12), $"start {start}: about one each 1.7 s over 20 s");
			}
		}

		[Test]
		public void NoMoreThanThreeDrops_AreEverInTheAir()
		{
			var random = new System.Random(5);
			for (int n = 0; n < 200; n++)
			{
				float height = (float)(SceneDrip.LeastHeight + random.NextDouble() * (SceneDrip.MostHeight - SceneDrip.LeastHeight));
				float interval = SceneDrip.LeastIntervalFor(height);
				interval = SceneDrip.LeastInterval + Mathf.Ceil((interval - SceneDrip.LeastInterval) / SceneDrip.IntervalStep - 1e-3f) * SceneDrip.IntervalStep;
				uint seed = SceneDrip.Seed((uint)random.Next(), interval, 0.3f, SceneDripLanding.Puddle);
				float fall = SceneDrip.FallSeconds(height);
				Assert.That(SceneDrip.IntervalOf(seed) * SceneDrip.InFlight, Is.GreaterThanOrEqualTo(fall - 1e-3f), $"{height} m");
				for (double t = 100.0; t < 160.0; t += 0.0931)
				{
					SceneDrip.ClockAt(t, seed, out long cycle, out float into);
					int flying = 0;
					for (int m = -2; m <= 6; m++)
					{
						float due = SceneDrip.SecondsToLanding(seed, cycle, into, m);
						if (due > 0f && due <= fall)
						{
							flying++;
							Assert.That(m, Is.InRange(0, 2), "the client draws drops 0, +1 and +2 only");
						}
					}
					Assert.That(flying, Is.LessThanOrEqualTo(3), $"{height} m at {t}");
				}
			}
		}

		[Test]
		public void TheShadersNumbers_AreTheBeatsOwn()
		{
			uint seed = SceneDrip.Seed(0x321u, 3.1f, 0.55f, SceneDripLanding.Puddle);
			var e = new SceneEmitter { Position = new Vector3(4f, -20f, 9f), Kind = SceneEmitterKind.Drip, Seed = seed, Reach = 7.5f };
			double t = 1.9e9 + 41.25;
			CaveDripField.Fill(e, t, 0.8f, out Vector4 source, out Vector4 shape, out Vector4 time, out Vector4 extra);
			Assert.That((Vector3)source, Is.EqualTo(e.Position));
			Assert.That(source.w, Is.EqualTo(7.5f));
			Assert.That(shape.x, Is.EqualTo(0.55f).Within(1e-4f));
			Assert.That(shape.y, Is.EqualTo(SceneDrip.FallSeconds(7.5f)).Within(1e-5f));
			Assert.That(shape.z, Is.EqualTo(0.8f));
			Assert.That(shape.w, Is.EqualTo(SceneDrip.RingLifeOf(seed)).Within(1e-5f));
			SceneDrip.ClockAt(t, seed, out long cycle, out float into);
			Assert.That(time.x, Is.LessThanOrEqualTo(0f), "drop −1 has landed");
			Assert.That(time.x, Is.LessThan(time.y));
			Assert.That(time.y, Is.LessThan(time.z));
			Assert.That(time.z, Is.LessThan(time.w));
			Assert.That(time.z, Is.EqualTo(SceneDrip.SecondsToLanding(seed, cycle, into, 1)).Within(1e-6f));
			Assert.That(extra.x, Is.EqualTo(cycle % 4096));
			Assert.That(extra.y, Is.EqualTo((float)SceneDripLanding.Puddle));
		}

		// ── The dressing's drips ──────────────────────────────────────

		[Test]
		public void ACavesDrips_AreTheSameEveryTime_WithinBudget_AndWhereWaterCanFall()
		{
			int total = 0, puddles = 0, stalactites = 0;
			foreach (int seed in Seeds)
			{
				CaveSystem system = Plan(CaveForm.Cave, 1, seed);
				List<CaveDripHang> hangs = Stalactites(system);
				CaveDripLayout first = CaveDrips.Lay(system, CaveHabitat.For(system), hangs);
				CaveDripLayout again = CaveDrips.Lay(system, CaveHabitat.For(system), hangs);
				Assert.That(again.Drips.Select(d => (d.Landing, d.Seed, d.Height)), Is.EqualTo(first.Drips.Select(d => (d.Landing, d.Seed, d.Height))), $"seed {seed}: deterministic");
				Assert.That(first.Drips.Count, Is.LessThanOrEqualTo(CaveDrips.MostDrips));
				Assert.That(first.FromRoof, Is.LessThanOrEqualTo(CaveDrips.MostFromRoof));
				Assert.That(first.FromRoof + first.FromStalactites + first.FromFlowstone, Is.EqualTo(first.Drips.Count));
				Assert.That(first.Puddles, Is.EqualTo(first.Drips.Count(d => d.Lands == SceneDripLanding.Puddle)));
				for (int i = 0; i < first.Drips.Count; i++)
				{
					CaveDrip d = first.Drips[i];
					string where = $"seed {seed} drip {i} at {d.Landing}";
					Assert.That(d.Height, Is.InRange(SceneDrip.LeastHeight, SceneDrip.MostHeight), where);
					Assert.That(d.Interval, Is.GreaterThanOrEqualTo(SceneDrip.LeastIntervalFor(d.Height) - 1e-4f), where + ": its drops never crowd the air");
					Assert.That(SceneDrip.IntervalOf(d.Seed), Is.EqualTo(d.Interval).Within(1e-5f), where);
					// It falls through the void: open air under where it lets go, down to where it lands.
					Vector3 from = d.Landing + Vector3.up * d.Height;
					for (float f = 0.1f; f < d.Height - 0.1f; f += 0.5f)
					{
						Assert.That(system.Void(from - Vector3.up * f), Is.LessThan(0f), where + $": open {f:0.0} m under where it lets go");
					}
					if (d.Lands == SceneDripLanding.Puddle)
					{
						Assert.That(d.Puddle, Is.InRange(CaveDrips.LeastPuddle - 1e-3f, SceneDrip.MostPuddle), where);
						Assert.That(CaveDrips.FloorBelow(system, d.Landing + Vector3.up * 0.01f, 1f, out float floor), Is.True, where);
						Assert.That(d.Landing.y - floor, Is.EqualTo(CaveDrips.PuddleDepth).Within(0.02f), where + ": its water a few centimetres over the floor");
					}
					else
					{
						Assert.That(d.Puddle, Is.EqualTo(0f), where);
					}
					for (int j = 0; j < i; j++)
					{
						CaveDrip o = first.Drips[j];
						if (Mathf.Abs(o.Landing.y - d.Landing.y) < 2.5f)
						{
							float apart = new Vector2(o.Landing.x - d.Landing.x, o.Landing.z - d.Landing.z).magnitude;
							Assert.That(apart, Is.GreaterThanOrEqualTo(CaveDrips.Spacing + o.Puddle + d.Puddle - 1e-3f), where + $": clear of drip {j}");
						}
					}
				}
				total += first.Drips.Count;
				puddles += first.Puddles;
				stalactites += first.FromStalactites;
				Console.WriteLine($"cave seed {seed}: {first.Drips.Count} drips ({first.FromStalactites} stalactites, {first.FromRoof} roof), {first.Puddles} puddles");
			}
			Assert.That(total, Is.GreaterThanOrEqualTo(3 * 8), "a limestone cave drips");
			Assert.That(puddles, Is.GreaterThan(0), "and some of its drips stand in puddles");
			Assert.That(stalactites, Is.GreaterThan(0), "its stalactites drip");
		}

		[Test]
		public void ADripOntoItsStalagmite_LandsOnItsTip()
		{
			CaveSystem system = Plan(CaveForm.Cave, 1, 3);
			List<CaveDripHang> hangs = Stalactites(system).Select(h =>
			{
				CaveDrips.FloorBelow(system, h.Tip, 60f, out float floor);
				h.LandOn = floor + 0.7f;
				return h;
			}).ToList();
			CaveDripLayout layout = CaveDrips.Lay(system, CaveHabitat.For(system), hangs);
			List<CaveDrip> tips = layout.Drips.Where(d => d.Source == CaveDripSource.Stalactite).ToList();
			Assert.That(tips, Is.Not.Empty);
			foreach (CaveDrip d in tips)
			{
				Assert.That(d.Lands, Is.EqualTo(SceneDripLanding.Tip));
				Assert.That(hangs.Any(h => Mathf.Abs(h.LandOn - d.Landing.y) < 1e-4f && Mathf.Abs(h.Tip.y - 0.03f - d.Landing.y - d.Height) < 1e-3f), Is.True, $"{d.Landing}: on its stalagmite's top, falling from its tip");
			}
		}

		[Test]
		public void NothingLands_WhereTheCallerRefuses()
		{
			CaveSystem system = Plan(CaveForm.Cave, 1, 8);
			CaveDripLayout none = CaveDrips.Lay(system, CaveHabitat.For(system), Stalactites(system), (p, margin) => true);
			Assert.That(none.Drips, Is.Empty);
			// Refusing one half of the cave keeps every drip in the other.
			CaveDripLayout half = CaveDrips.Lay(system, CaveHabitat.For(system), Stalactites(system), (p, margin) => p.x - margin < 0f);
			Assert.That(half.Drips.All(d => d.Landing.x - d.Puddle >= 0f), Is.True);
		}

		[Test]
		public void ADryLavaTube_DripsFarLessThanALimestoneCave()
		{
			int cave = 0, tube = 0;
			foreach (int seed in Seeds)
			{
				CaveSystem limestone = Plan(CaveForm.Cave, 1, seed);
				CaveSystem lava = Plan(CaveForm.LavaTube, 1, seed);
				cave += CaveDrips.Lay(limestone, CaveHabitat.For(limestone), null).FromRoof;
				tube += CaveDrips.Lay(lava, CaveHabitat.For(lava), null).FromRoof;
			}
			Console.WriteLine($"roof drips: caves {cave}, lava tubes {tube}");
			Assert.That(tube, Is.LessThan(Mathf.Max(1, cave / 3)), "a lava tube's rock is dry but where rain blows in");
		}

		// ── The sound ─────────────────────────────────────────────────

		[Test]
		public void TheDripSounds_AreMade_TheSameEveryTime_AndShort()
		{
			foreach (SceneDripLanding landing in new[] { SceneDripLanding.Puddle, SceneDripLanding.Rock })
			{
				for (int v = 0; v < DripSounds.Variants; v++)
				{
					float[] a = DripSounds.Drip(landing, v);
					float[] b = DripSounds.Drip(landing, v);
					Assert.That(a, Is.EqualTo(b), $"{landing} {v}: the same on every client");
					Assert.That(a.Length, Is.InRange(SoundSynth.Rate / 20, SoundSynth.Rate / 4), $"{landing} {v}: a quarter of a second at most");
					Assert.That(a.All(x => !float.IsNaN(x) && Mathf.Abs(x) <= 0.9001f), Is.True, $"{landing} {v}: finite, under full scale");
					Assert.That(a.Max(Mathf.Abs), Is.GreaterThan(0.5f), $"{landing} {v}: not silent");
				}
			}
			Assert.That(DripSounds.Key(SceneDripLanding.Tip, 2), Is.EqualTo(DripSounds.Key(SceneDripLanding.Rock, 2)), "a stalagmite's tip is wet rock");
			CaveDripPresenter.Pick(0xBEEFu, 41, out int va, out float pa);
			CaveDripPresenter.Pick(0xBEEFu, 41, out int vb, out float pb);
			Assert.That((va, pa), Is.EqualTo((vb, pb)), "a drop's variant and pitch are the drip's and the drop's, not the client's");
			Assert.That(CaveDripPresenter.Level(SceneDripLanding.Puddle, 10f), Is.GreaterThan(CaveDripPresenter.Level(SceneDripLanding.Puddle, 1f)), "harder from higher");
		}
	}
}
