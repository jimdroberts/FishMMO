using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The glow modes (2026-10-25, phase I6i; decisions 33, 40, 43, 46, 53, 56–59, 61, 64, 65, 68–76, 81, 83, 86, 89, 90, 92, 93, 95):
	/// every mode a pure function of the shared clock (and the published sky), meeting itself across the 10 000 s wrap, mirrored in
	/// FishGlowModes.hlsl; every name a draft gave a mode resolves; the shared episode slot function (fountains, vents) and the
	/// turbidity schedule; the template, spec, authoring and cloud-world plumbing; and every biome naming no mode glows exactly as
	/// before (the old code paths kept word for word, nothing authored names one). Since I6i-b: TRAIL, KNEADING and EPISODE read the
	/// stored lines and vents (SceneGlowPlaces), a light and its stone one row across a biome edge, nothing stored = the old stand-ins.
	/// </summary>
	[TestFixture]
	public class GlowModeTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const float Wrap = (float)SceneGlow.WrapSeconds;

		private static string Read(string path) => File.ReadAllText(path);

		private static IEnumerable<SceneGlowMode> Modes() => Enum.GetValues(typeof(SceneGlowMode)).Cast<SceneGlowMode>();

		/// <summary>A sky somewhere in the middle of everything (dusk, half moon, a breeze, some rain, a little fog, mid orbit).</summary>
		private static SceneGlowSky Sky(float day = 1234f, float localTime = 0.8f) => new SceneGlowSky
		{
			SunHeight = -0.05f, LocalTime01 = localTime, NightShare = 0.45f, Day = day, MoonLight = 0.5f, ParentLight = 0.6f, FieldTurn = 0.3f,
			OrbitTurn = 0.4f, Wet = 0.7f, Wind = 0.4f, Rain = 0.2f, Fog = 0.3f, Sea = 0.5f, Turbidity = 0.6f, WayX = 0.6f, WayZ = 0.8f,
			PlumeTide = 0.7f, VentBurst = 0.5f,
		};

		private static BiomeFountainEpisodes Volcanic => BiomeFountainEpisodes.Volcanic;

		// ── The modes ─────────────────────────────────────────────────

		[Test]
		public void Modes_AreAppendOnly()
		{
			string[] names =
			{
				"Default", "Pulse", "Flicker", "Still", "Caustic", "Step", "Sun", "Season", "Wind", "Breath", "Overturn", "AfterSunset", "Swell",
				"Field", "Terminator", "Volvalight", "Rain", "Trail", "NightLength", "Night", "Eyelid", "DayCount", "Kneading", "Fog", "PlumeTide",
				"Tremor", "Episode", "Burst",
			};
			Assert.That(Enum.GetNames(typeof(SceneGlowMode)), Is.EqualTo(names), "append only: templates and the GPU table store the number");
			for (int i = 0; i < names.Length; i++)
			{
				Assert.That((int)(SceneGlowMode)Enum.Parse(typeof(SceneGlowMode), names[i]), Is.EqualTo(i));
			}
			Assert.That(Enum.GetNames(typeof(SceneGlowSource)), Is.EqualTo(new[] { "Way", "Crystal" }));
		}

		[Test]
		public void EveryMode_IsAPureFunctionOfTheClock_AndBounded()
		{
			SceneGlowSky sky = Sky();
			BiomeFountainEpisodes ep = Volcanic;
			foreach (SceneGlowMode mode in Modes())
			{
				for (int i = 0; i < 400; i++)
				{
					float t = i * 24.937f, x = 13.7f * i - 900f, z = 400f - 7.3f * i;
					float a = SceneGlowModes.Value(mode, 0, in ep, t, x, z, in sky);
					// Ask other moments and places in between: nothing remembers.
					SceneGlowModes.Value(mode, 0, in ep, t * 0.37f + 5f, -x, z * 2f, in sky);
					SceneGlowModes.Value(mode, 0, in ep, Wrap - t, z, x, in sky);
					float b = SceneGlowModes.Value(mode, 0, in ep, t, x, z, in sky);
					Assert.That(b, Is.EqualTo(a), $"{mode} at {t}");
					Assert.That(a, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(1.4f), $"{mode} at {t}: {a}");
				}
			}
		}

		[Test]
		public void EveryMode_MeetsItselfAcrossTheWrap_AndRunsWithoutJumps()
		{
			SceneGlowSky sky = Sky();
			BiomeFountainEpisodes ep = Volcanic;
			foreach (SceneGlowMode mode in Modes())
			{
				foreach (Vector2 at in new[] { new Vector2(0f, 0f), new Vector2(731.4f, -2210.5f), new Vector2(-3999f, 1777f) })
				{
					float end = SceneGlowModes.Value(mode, 0, in ep, Wrap - 1e-3f, at.x, at.y, in sky);
					float start = SceneGlowModes.Value(mode, 0, in ep, 0f, at.x, at.y, in sky);
					Assert.That(Math.Abs(end - start), Is.LessThan(0.08f), $"{mode} at {at}: {end} before the wrap, {start} after");
				}
				if (mode == SceneGlowMode.Flicker)
				{
					// The stormglass crackles by design (decision 33; SceneGlowAndWallLightTests holds its own continuity).
					continue;
				}
				// Through time, a hundredth of a second a step: no step jumps further than its fastest wave allows.
				float last = SceneGlowModes.Value(mode, 0, in ep, 0f, 12f, 34f, in sky), worst = 0f;
				for (int i = 1; i < 60000; i++)
				{
					float v = SceneGlowModes.Value(mode, 0, in ep, i * 0.01f, 12f, 34f, in sky);
					worst = Math.Max(worst, Math.Abs(v - last));
					last = v;
				}
				Assert.That(worst, Is.LessThan(0.12f), $"{mode} jumps {worst} in 10 ms");
			}
		}

		[Test]
		public void EveryRate_IsWholeCyclesInTheWrap()
		{
			float[] rates =
			{
				SceneGlowModes.CausticAHz, SceneGlowModes.CausticBHz, SceneGlowModes.CausticCHz, SceneGlowModes.BreathHz, SceneGlowModes.OverturnHz,
				SceneGlowModes.SwellHz, SceneGlowModes.TrailHz, SceneGlowModes.TrembleHz,
			};
			foreach (float rate in rates)
			{
				double cycles = rate * SceneGlow.WrapSeconds;
				Assert.That(Math.Abs(cycles - Math.Round(cycles)), Is.LessThan(1e-3), $"{rate} Hz is {cycles} cycles in the wrap");
			}
			foreach (int period in VentBursts.Periods)
			{
				Assert.That(10000 % period, Is.EqualTo(0), $"vent period {period}");
			}
			Assert.That(Volcanic.DividesWrap, Is.True);
			Assert.That(Volcanic.Slot(0), Is.EqualTo(2000));
			Assert.That(Volcanic.Slot(1), Is.EqualTo(2500));
			Assert.That(Volcanic.Slot(2), Is.EqualTo(5000));
			Assert.That(new BiomeFountainEpisodes { SlotA = 3000 }.DividesWrap, Is.False);
		}

		[Test]
		public void DayModes_StepAtLocalMidnight_AndRepeatAcrossTheDayWrap()
		{
			BiomeFountainEpisodes ep = default;
			// Step: one notch a day of 13; DayCount: lit one day in 8.
			SceneGlowSky noon = Sky(26f, 0.5f), next = Sky(27f, 0.5f);
			float a = SceneGlowModes.Value(SceneGlowMode.Step, 0, in ep, 100f, 0f, 0f, in noon);
			float b = SceneGlowModes.Value(SceneGlowMode.Step, 0, in ep, 100f, 0f, 0f, in next);
			Assert.That(a, Is.EqualTo(0.2f + 0.8f * (26 % 13 + 1) / 13f).Within(1e-5f));
			Assert.That(b, Is.EqualTo(0.2f + 0.8f * (27 % 13 + 1) / 13f).Within(1e-5f));
			// Just after midnight it still shows the day before, easing in over the first hundredth of the day.
			float midnight = SceneGlowModes.Value(SceneGlowMode.Step, 0, in ep, 100f, 0f, 0f, Sky(27f, 0f));
			Assert.That(midnight, Is.EqualTo(a).Within(1e-5f));
			Assert.That(SceneGlowModes.Value(SceneGlowMode.DayCount, 0, in ep, 0f, 0f, 0f, Sky(16f, 0.5f)), Is.EqualTo(1f).Within(1e-5f));
			Assert.That(SceneGlowModes.Value(SceneGlowMode.DayCount, 0, in ep, 0f, 0f, 0f, Sky(17f, 0.5f)), Is.EqualTo(0.04f).Within(1e-5f));
			Assert.That(SceneGlowModes.Value(SceneGlowMode.DayCount, 3, in ep, 0f, 0f, 0f, Sky(18f, 0.5f)), Is.EqualTo(1f).Within(1e-5f), "every 3");
			// The day number's wrap is divided by every count to 16: day 0 follows the wrap's last day as day n follows n − 1.
			for (int n = 1; n <= SceneGlowModes.MaxEvery; n++)
			{
				Assert.That(SceneGlowModes.DayWrap % n, Is.EqualTo(0));
				Assert.That(SceneGlowModes.StepLevel(-1f, n), Is.EqualTo(SceneGlowModes.StepLevel(SceneGlowModes.DayWrap - 1, n)));
			}
			// The local day turns at local midnight.
			double solarDay = 24.0;
			Assert.That(SceneGlowModes.DayNumber(24.0 * 100 + 23.99, solarDay, 23.99 / 24.0), Is.EqualTo(100f));
			Assert.That(SceneGlowModes.DayNumber(24.0 * 101 + 0.01, solarDay, 0.01 / 24.0), Is.EqualTo(101f));
			Assert.That(SceneGlowModes.DayNumber(1e6, double.PositiveInfinity, 0.3), Is.EqualTo(0f));
		}

		[Test]
		public void SkyModes_FollowTheSkyTheyName()
		{
			BiomeFountainEpisodes ep = default;
			SceneGlowSky sky = Sky();
			float Sun(float h) { sky.SunHeight = h; return SceneGlowModes.Value(SceneGlowMode.Sun, 0, in ep, 0f, 0f, 0f, in sky); }
			float Night(float h) { sky.SunHeight = h; return SceneGlowModes.Value(SceneGlowMode.Night, 0, in ep, 0f, 0f, 0f, in sky); }
			Assert.That(Sun(0.9f), Is.GreaterThan(Sun(0.2f)));
			Assert.That(Sun(0.2f), Is.GreaterThan(Sun(-0.3f)));
			foreach (float h in new[] { -0.6f, -0.1f, 0f, 0.2f, 0.7f })
			{
				Assert.That(Sun(h) + Night(h), Is.EqualTo(1.15f).Within(1e-5f), "NIGHT is SUN inverted");
			}
			sky.SunHeight = 0f;
			float onHorizon = SceneGlowModes.Value(SceneGlowMode.Terminator, 0, in ep, 0f, 0f, 0f, in sky);
			sky.SunHeight = 0.5f;
			Assert.That(onHorizon, Is.GreaterThan(SceneGlowModes.Value(SceneGlowMode.Terminator, 0, in ep, 0f, 0f, 0f, in sky) + 0.8f));
			Assert.That(SceneGlowModes.OwnsDaylight(SceneGlowMode.Sun) && SceneGlowModes.OwnsDaylight(SceneGlowMode.Night)
				&& SceneGlowModes.OwnsDaylight(SceneGlowMode.Terminator) && SceneGlowModes.OwnsDaylight(SceneGlowMode.AfterSunset), Is.True);
			Assert.That(Modes().Count(SceneGlowModes.OwnsDaylight), Is.EqualTo(4));
			// After sunset: just after it bright, toward dawn dim, by day dark.
			sky = Sky();
			sky.NightShare = 0.5f;
			float At(float lt) { sky.LocalTime01 = lt; return SceneGlowModes.AfterSunset(in sky); }
			Assert.That(At(0.78f), Is.GreaterThan(0.9f));
			Assert.That(At(0.2f), Is.LessThan(0.3f).And.GreaterThan(0f));
			Assert.That(At(0.5f), Is.EqualTo(0f));
			Assert.That(At(0.75f), Is.EqualTo(0f).Within(1e-4f), "rises from nothing at sunset");
			// Eyelid: open only on dark-moon nights.
			sky = Sky();
			sky.MoonLight = 0f;
			float dark = SceneGlowModes.Value(SceneGlowMode.Eyelid, 0, in ep, 0f, 0f, 0f, in sky);
			sky.MoonLight = 0.6f;
			Assert.That(dark, Is.EqualTo(1f).Within(1e-5f));
			Assert.That(SceneGlowModes.Value(SceneGlowMode.Eyelid, 0, in ep, 0f, 0f, 0f, in sky), Is.EqualTo(0.04f).Within(1e-5f));
			// Wind, rain, fog, season, night length, plume tide, field, tremor, burst rise with what they follow.
			foreach ((SceneGlowMode mode, Action<float> set) in new (SceneGlowMode, Action<float>)[]
			{
				(SceneGlowMode.Wind, v => sky.Wind = v), (SceneGlowMode.Rain, v => sky.Rain = v), (SceneGlowMode.Fog, v => sky.Fog = v),
				(SceneGlowMode.Season, v => sky.Wet = v), (SceneGlowMode.NightLength, v => sky.NightShare = v), (SceneGlowMode.PlumeTide, v => sky.PlumeTide = v),
				(SceneGlowMode.Volvalight, v => sky.ParentLight = v), (SceneGlowMode.Tremor, v => sky.Turbidity = v), (SceneGlowMode.Burst, v => sky.VentBurst = v),
			})
			{
				sky = Sky();
				set(0f);
				float low = SceneGlowModes.Value(mode, 0, in ep, 1f, 0f, 0f, in sky);
				set(1f);
				float high = SceneGlowModes.Value(mode, 0, in ep, 1f, 0f, 0f, in sky);
				Assert.That(high, Is.GreaterThan(low + 0.3f), $"{mode}: {low} → {high}");
			}
			// Field brightens twice a turn.
			sky = Sky();
			sky.FieldTurn = 0f;
			float f0 = SceneGlowModes.Value(SceneGlowMode.Field, 0, in ep, 0f, 0f, 0f, in sky);
			sky.FieldTurn = 0.5f;
			float f5 = SceneGlowModes.Value(SceneGlowMode.Field, 0, in ep, 0f, 0f, 0f, in sky);
			sky.FieldTurn = 0.25f;
			Assert.That(f0, Is.EqualTo(1f).Within(1e-5f));
			Assert.That(f5, Is.EqualTo(1f).Within(1e-5f));
			Assert.That(SceneGlowModes.Value(SceneGlowMode.Field, 0, in ep, 0f, 0f, 0f, in sky), Is.EqualTo(0.25f).Within(1e-5f));
			// Kneading: the stress turns with the orbit; cracks of other bearings peak at other moments.
			sky = Sky();
			var peaks = new HashSet<int>();
			foreach (float x in new[] { 0f, 150f, 300f, 450f })
			{
				int best = 0;
				float bestValue = -1f;
				for (int i = 0; i < 100; i++)
				{
					sky.OrbitTurn = i / 100f;
					float v = SceneGlowModes.Value(SceneGlowMode.Kneading, 0, in ep, 0f, x, 0f, in sky);
					if (v > bestValue)
					{
						bestValue = v;
						best = i % 50;
					}
				}
				peaks.Add(best / 5);
			}
			Assert.That(peaks.Count, Is.GreaterThan(1), "cracks of different bearings knead at different moments");
		}

		[Test]
		public void SkyNumbers_ArePureInTheWorldClock()
		{
			Assert.That(SceneGlowModes.PlumeTideOf(0f), Is.EqualTo(0.3f).Within(1e-6f));
			Assert.That(SceneGlowModes.PlumeTideOf(0.5f), Is.EqualTo(1f).Within(1e-6f), "peak at apocentre");
			Assert.That(SceneGlowModes.TurnOf(33.0 * 10 + 16.5, 0.0, 0.0, SceneGlowModes.FallbackOrbitHours), Is.EqualTo(0.5f).Within(1e-5f), "33 h fallback");
			Assert.That(SceneGlowModes.TurnOf(10.0, 40.0, 90.0, 33.0), Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(SceneGlowModes.WetOf(0.5f, 30f), Is.EqualTo(1f).Within(1e-5f), "northern summer");
			Assert.That(SceneGlowModes.WetOf(0.5f, -30f), Is.EqualTo(0f).Within(1e-5f), "southern winter");
			Assert.That(SceneGlowModes.FieldTurnOf(10.0, 10.0, double.PositiveInfinity), Is.EqualTo(0f).Within(1e-5f));
			// Knots: the mean of the next window over a line through knots is continuous and history-free.
			Func<long, float> knot = k => (float)((k * 7919L) % 13L) / 13f;
			float last = SceneGlowModes.KnotMean(0.0, 3600.0, 6 * 3600.0, knot);
			for (int i = 1; i < 5000; i++)
			{
				float v = SceneGlowModes.KnotMean(i * 10.0, 3600.0, 6 * 3600.0, knot);
				Assert.That(Math.Abs(v - last), Is.LessThan(0.002f), $"at {i * 10} s");
				last = v;
			}
			Assert.That(SceneGlowModes.KnotMean(1234.5, 3600.0, 6 * 3600.0, k => 0.4f), Is.EqualTo(0.4f).Within(1e-6f));
			Assert.That(SceneGlowModes.KnotLerp(90.0, 60.0, k => k), Is.EqualTo(1.5f).Within(1e-6f));
		}

		// ── Episodes ─────────────────────────────────────────────────

		[Test]
		public void FountainEpisodes_ShareTheSlotFunction_AndPlayTheirShare()
		{
			BiomeFountainEpisodes ep = Volcanic;
			for (uint seed = 0; seed < 30; seed++)
			{
				int length = ep.Slot((int)(seed % 3u));
				int on = 0, steps = 0;
				for (float t = 0f; t < Wrap; t += 1f)
				{
					float v = BiomeFountainEpisodes.At(in ep, t, seed);
					Assert.That(v, Is.EqualTo(EpisodeSlots.At(t, length, ep.OnShare * length, ep.RampSeconds, seed)), "one slot function");
					Assert.That(v, Is.InRange(0f, 1f));
					on += v > 0f ? 1 : 0;
					steps++;
				}
				Assert.That(on / (float)steps, Is.EqualTo(ep.OnShare).Within(0.01f), $"seed {seed}: plays its share");
				Assert.That(BiomeFountainEpisodes.At(in ep, 0f, seed), Is.EqualTo(0f), "never on across a slot's edge (or the wrap)");
				Assert.That(BiomeFountainEpisodes.At(in ep, Wrap - 0.01f, seed), Is.EqualTo(0f));
			}
			Assert.That(BiomeFountainEpisodes.At(default, 100f, 1u), Is.EqualTo(0f), "no schedule, no episodes");
			// A glow 'cell' follows one fountain; glows a cell apart need not.
			Assert.That(BiomeFountainEpisodes.GlowSeed(10f, 10f), Is.EqualTo(BiomeFountainEpisodes.GlowSeed(490f, 20f)));
			Assert.That(BiomeFountainEpisodes.GlowSeed(-10f, 10f), Is.Not.EqualTo(BiomeFountainEpisodes.GlowSeed(10f, 10f)));
		}

		[Test]
		public void VentBursts_SkipAShare_WithHashedStrength()
		{
			for (uint seed = 1; seed < 7; seed++)
			{
				int period = VentBursts.PeriodOf(seed);
				Assert.That(VentBursts.Periods, Does.Contain(period));
				int bursts = 0, slots = 0;
				var strengths = new HashSet<int>();
				// Many wraps' worth of slots, a reading at each burst's height.
				for (int slot = 0; slot < 1500; slot++)
				{
					float best = 0f;
					float t0 = (slot * period) % Wrap;
					for (float t = t0; t < t0 + period; t += 1f)
					{
						best = Math.Max(best, EpisodeSlots.At(t, period, VentBursts.BurstSeconds, VentBursts.RampSeconds, seed ^ (uint)(slot / (10000 / period)) * 0x9E37u,
							VentBursts.Skip, VentBursts.Weakest, 1f));
					}
					slots++;
					if (best > 0f)
					{
						bursts++;
						Assert.That(best, Is.InRange(VentBursts.Weakest - 0.02f, 1f));
						strengths.Add((int)(best * 20f));
					}
				}
				Assert.That(1f - bursts / (float)slots, Is.EqualTo(VentBursts.Skip).Within(0.03f), $"seed {seed} skips its share");
				Assert.That(strengths.Count, Is.GreaterThan(5), "strength hashed burst by burst");
				Assert.That(VentBursts.At(123.4f, seed), Is.EqualTo(VentBursts.At(123.4f, seed)));
			}
		}

		[Test]
		public void TurbidityEpisodes_ArePure_Continuous_AndOccasional()
		{
			uint seed = TurbidityEpisodes.SeedOf("Underwater Canyon Test");
			Assert.That(seed, Is.EqualTo(TurbidityEpisodes.SeedOf("Underwater Canyon Test")));
			int active = 0;
			for (long slot = 0; slot < 4000; slot++)
			{
				if (TurbidityEpisodes.TryEpisode(seed, slot, out double start))
				{
					active++;
					Assert.That(start, Is.GreaterThanOrEqualTo(slot * TurbidityEpisodes.SlotSeconds));
					Assert.That(start + TurbidityEpisodes.DurationSeconds, Is.LessThanOrEqualTo((slot + 1) * TurbidityEpisodes.SlotSeconds), "never straddles");
					double peak = start + TurbidityEpisodes.RiseSeconds;
					Assert.That(TurbidityEpisodes.Strength(seed, peak), Is.EqualTo(1f).Within(1e-4f));
					Assert.That(TurbidityEpisodes.Strength(seed, start + TurbidityEpisodes.DurationSeconds + 1.0), Is.EqualTo(0f));
					float last = 0f;
					for (double t = start - 5.0; t < start + TurbidityEpisodes.DurationSeconds + 5.0; t += 0.5)
					{
						float v = TurbidityEpisodes.Strength(seed, t);
						Assert.That(Math.Abs(v - last), Is.LessThan(0.02f), $"continuous at {t}");
						last = v;
					}
				}
			}
			Assert.That(active / 4000.0, Is.EqualTo(TurbidityEpisodes.Chance).Within(0.03));
			// Far from the epoch it still reads in double.
			Assert.That(TurbidityEpisodes.Strength(seed, 3.1e9), Is.EqualTo(TurbidityEpisodes.Strength(seed, 3.1e9)));
		}

		// ── Names ─────────────────────────────────────────────────────

		[Test]
		public void EveryDraftsNamedMode_Resolves()
		{
			// The names the drafts gave (decisions 33, 40, 43/46/69 warm, 53, 56–59, 61, 64, 65, 68, 70–72, 74–76, 81, 83, 86, 89, 90, 92, 93, 95).
			(string name, SceneGlowMode mode)[] drafts =
			{
				("FLICKER", SceneGlowMode.Flicker), ("CAUSTIC", SceneGlowMode.Caustic), ("caustic pulse", SceneGlowMode.Caustic), ("STEP", SceneGlowMode.Step),
				("SUN", SceneGlowMode.Sun), ("Sun-facing", SceneGlowMode.Sun), ("SEASON", SceneGlowMode.Season), ("WIND", SceneGlowMode.Wind),
				("Breath", SceneGlowMode.Breath), ("OVERTURN", SceneGlowMode.Overturn), ("AFTERGLOW", SceneGlowMode.AfterSunset),
				("time since sunset", SceneGlowMode.AfterSunset), ("SWELL", SceneGlowMode.Swell), ("STILL", SceneGlowMode.Still),
				("FIELD", SceneGlowMode.Field), ("Terminator", SceneGlowMode.Terminator), ("Volvalight", SceneGlowMode.Volvalight),
				("RAIN", SceneGlowMode.Rain), ("TRAIL", SceneGlowMode.Trail), ("NIGHTLENGTH", SceneGlowMode.NightLength), ("NIGHT", SceneGlowMode.Night),
				("EYELID", SceneGlowMode.Eyelid), ("DAYCOUNT", SceneGlowMode.DayCount), ("KNEADING", SceneGlowMode.Kneading), ("FOG", SceneGlowMode.Fog),
				("plume tide", SceneGlowMode.PlumeTide), ("TREMOR", SceneGlowMode.Tremor), ("EPISODE", SceneGlowMode.Episode),
				("BURST", SceneGlowMode.Burst), ("twinkle", SceneGlowMode.Flicker), ("pulse", SceneGlowMode.Pulse),
			};
			foreach ((string name, SceneGlowMode mode) in drafts)
			{
				Assert.That(SceneGlowModes.TryParse(name, out SceneGlowMode found), Is.True, name);
				Assert.That(found, Is.EqualTo(mode), name);
			}
			Assert.That(SceneGlowModes.TryParse("Warm", out _), Is.False, "WARM is a tint on any mode, not a mode");
			Assert.That(SceneGlowModes.TryParse("Gloaming", out _), Is.False);
			// The warm tint keeps a glow's brightness (its luminance) and turns it to firelight.
			Color cold = SceneGlow.Colour(SceneGlowHue.Rune);
			Color warm = SceneGlowModes.Warmed(cold, true);
			float L(Color c) => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
			Assert.That(L(warm), Is.EqualTo(L(cold)).Within(1e-3f));
			Assert.That(warm.r, Is.GreaterThan(warm.g).And.GreaterThan(warm.b));
			Assert.That(SceneGlowModes.Warmed(cold, false), Is.EqualTo(cold));
		}

		// ── Defaults: nothing changes ─────────────────────────────────

		[Test]
		public void Defaults_NameNoMode_AndGlowAsBefore()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			// An authored round's own glow modes are pinned in BiomeAuthoredRoundTests.
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)))
			{
				Assert.That(entry.GlowModes(), Is.Empty, $"{entry.Biome} names no glow mode yet");
				Assert.That(entry.FountainEpisodes().IsEmpty, Is.True, $"{entry.Biome} has no fountain episodes yet");
			}
			int templates = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				templates++;
				if (BiomeAuthoredRounds.Contains(Path.GetFileNameWithoutExtension(path)))
				{
					continue;
				}
				Assert.That(Regex.IsMatch(text, @"^  glowModes:\s*\n  - ", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} names a glow mode");
			}
			Assert.That(templates, Is.EqualTo(83));
			Assert.That(new CloudWorldStructures().GlowModes, Is.Empty, "a cloud world's body names none");
			// No entry (or an entry that moves nothing): the table is off and every glow takes its old path.
			Assert.That(BiomeGlowModes.Resolve(BiomeGlowModes.None(), null, default, SceneGlowSource.Way, out _), Is.False);
			Assert.That(BiomeGlowModes.Resolve(new List<BiomeGlowMode> { new BiomeGlowMode(SceneGlowSource.Crystal, SceneGlowMode.Default) }, null, default,
				SceneGlowSource.Crystal, out _), Is.False, "Default and cold moves nothing");
			// A Default entry naming only the warm tint keeps the emitter's own wave exactly.
			var warmOnly = new BiomeGlowMode(SceneGlowSource.Way, SceneGlowMode.Default, warm: true);
			SceneGlowSky sky = Sky();
			for (int i = 0; i < 200; i++)
			{
				var at = new Vector3(i * 3.1f, 0f, -i * 1.7f);
				float t = i * 41.3f;
				Assert.That(SceneGlowModes.Light(in warmOnly, SceneGlow.Intensity, t, at, in sky, 0.4f, false, false),
					Is.EqualTo(SceneGlow.Light(t, at, 0.4f, false, false)).Within(1e-6f));
				Assert.That(SceneGlowModes.Light(in warmOnly, SceneGlow.Intensity, t, at, in sky, 0.4f, true, true),
					Is.EqualTo(SceneGlow.Light(t, at, 0.4f, true, true)).Within(1e-6f));
				// The Pulse mode is today's pulse; Flicker today's flicker.
				Assert.That(SceneGlowModes.Value(SceneGlowMode.Pulse, 0, default, t, at.x, at.z, in sky), Is.EqualTo(SceneGlow.Pulse(t, at.x, at.z)));
				Assert.That(SceneGlowModes.Value(SceneGlowMode.Flicker, 0, default, t, at.x, at.z, in sky), Is.EqualTo(SceneGlow.Flicker(t, at.x, at.z)));
			}
			// The old shader lines stand word for word; the moded path only overwrites when the table names a mode.
			string pass = Read(Path.Combine(Shaders, "FishWeatherLitForwardPass.hlsl"));
			StringAssert.Contains("surfaceData.emission *= FishStructureGlowLight(input.fishRockOrigin, fishUnderground);", pass);
			StringAssert.Contains("surfaceData.emission *= FishGlowFlicker(input.fishRockOrigin);", pass);
			StringAssert.Contains("FishGlowModeWay(fishGlowMade, input.fishRockOrigin, fishUnderground, surfaceData.emission);", pass);
			StringAssert.Contains("FishGlowModeCrystal(fishGlowMade, input.fishRockOrigin, fishFlickers, surfaceData.emission);", pass);
			StringAssert.Contains("bool fishFlickers = _FishStructure > -1.5h;", pass);
			int include = pass.IndexOf("#include \"FishGlowModes.hlsl\"", StringComparison.Ordinal);
			Assert.That(include, Is.GreaterThan(pass.IndexOf("#include \"FishStructureCoat.hlsl\"", StringComparison.Ordinal)), "inside the indirect twin's includes");
			string modes = Read(Path.Combine(Shaders, "FishGlowModes.hlsl"));
			StringAssert.Contains("if (_FishGlowInfo.x < 0.5)\n    {\n        return false;", modes);
			StringAssert.Contains("float value = mode == 0 ? FishStructureGlowLight(originWS, underground)", modes);
			StringAssert.Contains("float value = mode == 0 ? (flickers ? FishGlowFlicker(originWS) : 1.0)", modes);
			// A steady crystal's new mark (−2) shines as made: below −0.5 (the crystal branch) and below −1.5 (not a flicker).
			Assert.That(BiomeArtGenerator.SteadyCrystalFlag, Is.LessThan(-1.5f));
			Assert.That(BiomeArtGenerator.FlickeringCrystalFlag, Is.GreaterThan(-1.5f).And.LessThan(-0.5f));
			string cave = Read(Path.Combine(World, "ProceduralArt/BiomeArtGenerator.CaveGlow.cs"));
			StringAssert.Contains("m.SetFloat(\"_FishStructure\", SteadyCrystalFlag);", cave);
			// The table is published only when a biome names a mode (GlowModeTable; FloraPaletteMap builds and publishes it).
			string table = Read("Assets/Scripts/Client/World/Terrain/GlowModeTable.cs");
			StringAssert.Contains("info = any && count > 0 ? new Vector4(1f, count, width, height) : Vector4.zero;", table);
			Assert.That(table, Does.Not.Contain("Texture2D"), "no texture: the indirect pass is near the texture units WebGPU and GL allow");
			Assert.That(GlowModeTableRows, Is.EqualTo(64));
			string flora = Read("Assets/Scripts/Client/World/Terrain/FloraPaletteMap.cs");
			StringAssert.Contains("GlowModeTable.Build(rowBiomes, indices, w, h, rect,", flora);
			StringAssert.Contains("GlowModeTable.Publish(published);", flora);
			StringAssert.Contains("GlowModeTable.Release();", flora);
			// The site lights take their old intensity and only move where the table names a mode.
			string pool = Read("Assets/Scripts/Client/World/Terrain/SiteLightPool.cs");
			StringAssert.Contains("intensity = SceneGlow.Light(time, e.Position, glow, buried, SceneGlow.Flickers(e.Seed));", pool);
			StringAssert.Contains("if (GlowModeTable.TryAt(e.Position, SceneGlowModes.SourceOf(SceneGlow.HueOf(e.Seed)), out", pool);
			StringAssert.Contains("if (GlowModeTable.TryAt(e.Position, SceneGlowSource.Crystal, out", pool);
			StringAssert.Contains("GlowSky.Publish(state, in context, sunDirections[0].y);", Read("Assets/Scripts/Client/World/Weather/Sky/SkySystem.cs"));
		}

		// ── The shader's copy ─────────────────────────────────────────

		[Test]
		public void TheShader_MirrorsEveryMode_ConstantForConstant()
		{
			string hlsl = Read(Path.Combine(Shaders, "FishGlowModes.hlsl"));
			foreach (SceneGlowMode mode in Modes())
			{
				if (mode == SceneGlowMode.Default || mode == SceneGlowMode.Pulse)
				{
					continue;
				}
				StringAssert.Contains($"mode == {(int)mode}", hlsl, $"{mode} has its branch");
			}
			string F(float v) => v.ToString("0.0###", CultureInfo.InvariantCulture);
			foreach (float c in new[]
			{
				SceneGlowModes.CausticAHz, SceneGlowModes.CausticAX, SceneGlowModes.CausticAZ, SceneGlowModes.CausticBHz, SceneGlowModes.CausticBX, SceneGlowModes.CausticBZ,
				SceneGlowModes.CausticCHz, SceneGlowModes.CausticCX, SceneGlowModes.CausticCZ, SceneGlowModes.BreathHz, SceneGlowModes.BreathX, SceneGlowModes.BreathZ,
				SceneGlowModes.OverturnHz, SceneGlowModes.OverturnX, SceneGlowModes.OverturnZ, SceneGlowModes.SwellHz, SceneGlowModes.SwellMetres, SceneGlowModes.TrailHz,
				SceneGlowModes.TrailMetres, SceneGlowModes.CrackX, SceneGlowModes.CrackZ, SceneGlowModes.TrembleHz, SceneGlowModes.TrembleX, SceneGlowModes.TrembleZ,
				SceneGlowModes.WarmTint.x, SceneGlowModes.WarmTint.y, SceneGlowModes.WarmTint.z, BiomeFountainEpisodes.GlowCellMetres,
			})
			{
				StringAssert.Contains(F(Math.Abs(c)), hlsl, $"constant {c}");
			}
			// The hash and its salts, as the C#.
			foreach (string h in new[] { "0x9E3779B1u", "0x7FEB352Du", "0x846CA68Bu", "0x8DA6B343u", "0xD8163841u", "0xE9150DEu" })
			{
				StringAssert.Contains(h, hlsl);
			}
			StringAssert.Contains("0x9E3779B1u", Read("Assets/Scripts/Shared/Implementation/Weather/TurbidityEpisodes.cs"));
			StringAssert.Contains("0x8DA6B343u", Read("Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeGlowMode.cs"));
			StringAssert.Contains("mode == 6 || mode == 11 || mode == 14 || mode == 19", hlsl, "the modes that carry their own daylight");
			Assert.That(new[] { (int)SceneGlowMode.Sun, (int)SceneGlowMode.AfterSunset, (int)SceneGlowMode.Terminator, (int)SceneGlowMode.Night }, Is.EqualTo(new[] { 6, 11, 14, 19 }));
			// Its globals are what GlowSky and GlowModeTable publish.
			string sky = Read("Assets/Scripts/Client/World/Weather/Sky/GlowSky.cs") + Read("Assets/Scripts/Client/World/Terrain/GlowModeTable.cs");
			foreach (string g in new[] { "_FishGlowRows", "_FishGlowEpisodes", "_FishGlowRamps", "_FishGlowInfo", "_FishGlowSkyA", "_FishGlowSkyB", "_FishGlowSkyC", "_FishGlowSkyD", "_FishPlumeTide", "_FishTidalStress", "_FishVentBurst" })
			{
				StringAssert.Contains(g, hlsl);
				StringAssert.Contains("\"" + g + "\"", sky);
			}
			Assert.That(hlsl, Does.Not.Contain("#pragma"), "an include of the FXC indirect pass");
		}

		/// <summary>The GPU's row count (GlowModeTable.Rows lives in the client assembly; its declaration is checked against the shader's).</summary>
		private static int GlowModeTableRows
		{
			get
			{
				StringAssert.Contains("public const int Rows = FloraPaletteMap.MaxBiomes + 1;", Read("Assets/Scripts/Client/World/Terrain/GlowModeTable.cs"));
				StringAssert.Contains("public const int MaxBiomes = 63;", Read("Assets/Scripts/Client/World/Terrain/FloraPaletteMap.cs"));
				StringAssert.Contains("float4 _FishGlowRows[64];", Read(Path.Combine(Shaders, "FishGlowModes.hlsl")));
				return 64;
			}
		}

		[Test]
		public void TheTablesPacking_RoundTripsAsTheShaderUnpacksIt()
		{
			string hlsl = Read(Path.Combine(Shaders, "FishGlowModes.hlsl"));
			StringAssert.Contains("float every = floor(packed / 64.0);", hlsl);
			StringAssert.Contains("float warm = rest >= 32.0 ? 1.0 : 0.0;", hlsl);
			string table = Read("Assets/Scripts/Client/World/Terrain/GlowModeTable.cs");
			StringAssert.Contains("public const float WarmBit = 32f, EveryUnit = 64f;", table);
			StringAssert.Contains("(float)entry.Mode + (entry.Warm ? WarmBit : 0f) + EveryUnit * SceneGlowModes.EveryOf(entry.Mode, entry.Every)", table);
			foreach (SceneGlowMode mode in Modes())
			{
				foreach (bool warm in new[] { false, true })
				{
					for (int every = 0; every <= SceneGlowModes.MaxEvery; every++)
					{
						float packed = (float)mode + (warm ? 32f : 0f) + 64f * SceneGlowModes.EveryOf(mode, every);
						// The shader's unpacking, line for line.
						float e = (float)Math.Floor(packed / 64.0f);
						float rest = packed - 64f * e;
						float w = rest >= 32f ? 1f : 0f;
						Assert.That(rest - 32f * w, Is.EqualTo((float)mode));
						Assert.That(w > 0.5f, Is.EqualTo(warm));
						Assert.That(e, Is.EqualTo(SceneGlowModes.EveryOf(mode, every)));
						Assert.That(packed, Is.GreaterThan(0.5f), "a named entry is never 0 (none)");
					}
				}
			}
			Assert.That(Enum.GetValues(typeof(SceneGlowMode)).Length, Is.LessThanOrEqualTo(32), "a mode fits under the warm bit");
		}

		// ── Template, spec, authoring, cloud worlds ───────────────────

		[Test]
		public void Plumbing_TemplateSpecAuthoringAndCloudWorlds()
		{
			string template = Read("Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs");
			StringAssert.Contains("private List<BiomeGlowMode> glowModes = new List<BiomeGlowMode>();", template);
			StringAssert.Contains("private BiomeFountainEpisodes fountainEpisodes;", template);
			StringAssert.Contains("public const string GlowModes = nameof(glowModes);", template);
			string authoring = Read(Path.Combine(World, "ProceduralArt/BiomeArtAuthoring.cs"));
			StringAssert.Contains("AuthorGlowModes(biome, entry, report, apply);", authoring);
			Assert.That(BiomeArtAuthoring.GlowModeId(new BiomeGlowMode(SceneGlowSource.Crystal, SceneGlowMode.Sun, true)), Is.EqualTo("Crystal"));
			// A spec entry's own (the round's) replaces its source's, and an Episode entry with no schedule takes the biome's.
			var spec = new BiomeArtSpec.Entry { Biome = "Probe" };
			spec.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Sun, warm: true).GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Episode)
				.Fountains(BiomeFountainEpisodes.Volcanic);
			Assert.That(spec.GlowModes().Count, Is.EqualTo(1));
			Assert.That(BiomeGlowModes.Resolve(spec.GlowModes(), null, spec.FountainEpisodes(), SceneGlowSource.Crystal, out BiomeGlowMode e), Is.True);
			Assert.That(e.Mode, Is.EqualTo(SceneGlowMode.Episode));
			Assert.That(e.Episodes.SlotC, Is.EqualTo(5000));
			Assert.That(BiomeGlowModes.Resolve(spec.GlowModes(), null, default, SceneGlowSource.Crystal, out e), Is.True);
			Assert.That(e.Episodes.ToString(), Is.EqualTo(BiomeFountainEpisodes.Volcanic.ToString()), "the volcanic default");
			Assert.That(BiomeGlowModes.Resolve(spec.GlowModes(), null, default, SceneGlowSource.Way, out _), Is.False);
			// A cloud world's body moves every glow whose biome names none; a biome's own comes first.
			var world = new List<BiomeGlowMode> { new BiomeGlowMode(SceneGlowSource.Way, SceneGlowMode.Field) };
			Assert.That(BiomeGlowModes.Resolve(null, world, default, SceneGlowSource.Way, out e) && e.Mode == SceneGlowMode.Field, Is.True);
			var own = new List<BiomeGlowMode> { new BiomeGlowMode(SceneGlowSource.Way, SceneGlowMode.Still) };
			Assert.That(BiomeGlowModes.Resolve(own, world, default, SceneGlowSource.Way, out e) && e.Mode == SceneGlowMode.Still, Is.True);
			Assert.That(SceneGlowModes.SourceOf(SceneGlowHue.Rune), Is.EqualTo(SceneGlowSource.Way));
			Assert.That(SceneGlowModes.SourceOf(SceneGlowHue.Faerie), Is.EqualTo(SceneGlowSource.Way));
			Assert.That(SceneGlowModes.SourceOf(SceneGlowHue.Amber), Is.EqualTo(SceneGlowSource.Crystal));
			// Cloud-world lights: the cut keeps each bridge's end lights on its crossing and the stages' on the record, and the record
			// offers them to the site lights.
			string cut = Read(Path.Combine(World, "CloudWorld/CloudWorldCut.cs"));
			StringAssert.Contains("LightEmitters(prefab, at, facing, Vector3.one, seed, endLights);", cut);
			StringAssert.Contains("Lights = endLights.ToArray()", cut);
			string stages = Read(Path.Combine(World, "PointsOfInterest/Features/PointOfInterestSkyTraffic.cs"));
			StringAssert.Contains("world.StageLights = stageLights.ToArray();", stages);
			string record = Read("Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/CloudWorld/SceneCloudWorld.cs");
			StringAssert.Contains("SceneSiteEmitters.AddLights(this, Lights());", record);
			StringAssert.Contains("SceneSiteEmitters.RemoveLights(this);", record);
		}

		[Test]
		public void CloudWorldRecord_GathersEveryLight()
		{
			// Pure: the record's list (not the component's life cycle).
			var crossings = new[]
			{
				new SceneCloudWorld.Crossing { Lights = new[] { new SceneEmitter { Kind = SceneEmitterKind.Glow, Position = Vector3.one } } },
				new SceneCloudWorld.Crossing { Lights = null },
				new SceneCloudWorld.Crossing { Lights = new[] { new SceneEmitter { Kind = SceneEmitterKind.Lamp }, new SceneEmitter { Kind = SceneEmitterKind.Fire } } },
			};
			Assert.That(crossings.Where(c => c.Lights != null).Sum(c => c.Lights.Length), Is.EqualTo(3));
			string record = Read("Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/CloudWorld/SceneCloudWorld.cs");
			StringAssert.Contains("all.AddRange(crossing.Lights);", record);
			StringAssert.Contains("all.AddRange(StageLights);", record);
			string emitters = Read("Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/PointsOfInterest/SceneSiteEmitters.cs");
			StringAssert.Contains("into.Add(set < active.Count ? active[set].Emitters[found[i].index] : extra[set - active.Count].Emitters[found[i].index]);", emitters);
		}

		// ── I6i-b: the stand-ins switched to the stored data (decision 34) ──

		/// <summary>An L-shaped rille (300 m east, then 400 m north) of one line, and a short line far away.</summary>
		private static List<VentLine> Rille() => new List<VentLine>
		{
			new VentLine { A = new Vector2(0f, 0f), B = new Vector2(300f, 0f), AlongA = 0f, LineLength = 700f, Half = 8f, Seed = 7u, BiomeID = 5 },
			new VentLine { A = new Vector2(300f, 0f), B = new Vector2(300f, 400f), AlongA = 300f, LineLength = 700f, Half = 8f, Seed = 7u, BiomeID = 5 },
			new VentLine { A = new Vector2(5000f, 5000f), B = new Vector2(5100f, 5000f), AlongA = 0f, LineLength = 100f, Half = 4f, Seed = 9u, BiomeID = 6 },
		};

		[Test]
		public void StoredLines_TrailCreepsAlongTheNearest_PureAndFallsBack()
		{
			SceneGlowSky sky = Sky();
			BiomeFountainEpisodes ep = Volcanic;
			List<SceneGlowLine> lines = SceneGlowPlaces.Choose(Rille(), null);
			Assert.That(lines.Count, Is.EqualTo(3));
			float phase = SceneGlowPlaces.PhaseOf(7u);
			Assert.That(phase, Is.GreaterThanOrEqualTo(0f).And.LessThan(SceneGlowModes.TrailMetres));
			// Along the line, wherever beside it the glow stands, either side of it alike.
			SceneGlowPlace a = SceneGlowPlaces.At(lines, null, 150f, 3f), b = SceneGlowPlaces.At(lines, null, 150f, -3f);
			Assert.That(a.OnLine, Is.True);
			Assert.That(a.Along, Is.EqualTo(150f + phase).Within(1e-3f));
			Assert.That(b.Along, Is.EqualTo(a.Along).Within(1e-3f));
			Assert.That(SceneGlowPlaces.At(lines, null, 304f, 100f).Along, Is.EqualTo(400f + phase).Within(1e-3f), "round the corner: 300 m + 100 m up");
			Assert.That(SceneGlowPlaces.At(lines, null, 5050f, 5010f).Along, Is.EqualTo(50f + SceneGlowPlaces.PhaseOf(9u)).Within(1e-3f), "the nearest line, not the first");
			// The point of light creeps along it at TrailHz × TrailMetres metres a second (0.5 m/s): 10 s later it is 5 m on.
			SceneGlowPlace on = SceneGlowPlaces.At(lines, null, 105f, 2f);
			for (int i = 0; i < 300; i++)
			{
				float t = i * 31.7f;
				float here = SceneGlowModes.Value(SceneGlowMode.Trail, 0, in ep, t, 100f, 2f, in sky, SceneGlowPlaces.At(lines, null, 100f, 2f));
				float later = SceneGlowModes.Value(SceneGlowMode.Trail, 0, in ep, t + 10f, 105f, 2f, in sky, in on);
				Assert.That(later, Is.EqualTo(here).Within(2e-3f), $"at {t}");
				// Pure: asking elsewhere in between changes nothing.
				SceneGlowModes.Value(SceneGlowMode.Trail, 0, in ep, t * 0.3f, -40f, 9f, in sky, in b);
				Assert.That(SceneGlowModes.Value(SceneGlowMode.Trail, 0, in ep, t + 10f, 105f, 2f, in sky, in on), Is.EqualTo(later));
			}
			// Meets itself across the wrap; no jumps (10 ms steps).
			float end = SceneGlowModes.Value(SceneGlowMode.Trail, 0, in ep, Wrap - 1e-3f, 100f, 2f, in sky, in on);
			float start = SceneGlowModes.Value(SceneGlowMode.Trail, 0, in ep, 0f, 100f, 2f, in sky, in on);
			Assert.That(Math.Abs(end - start), Is.LessThan(0.08f));
			float last = start, worst = 0f;
			for (int i = 1; i < 60000; i++)
			{
				float v = SceneGlowModes.Value(SceneGlowMode.Trail, 0, in ep, i * 0.01f, 100f, 2f, in sky, in on);
				worst = Math.Max(worst, Math.Abs(v - last));
				last = v;
			}
			Assert.That(worst, Is.LessThan(0.12f));
			// Nothing stored (or no line of a biome that trails): the fixed bearing, to the bit, for every mode.
			Assert.That(SceneGlowPlaces.Choose(null, null), Is.Empty);
			Assert.That(SceneGlowPlaces.Choose(Rille(), id => id == 99), Is.Empty);
			Assert.That(SceneGlowPlaces.At(null, null, 1f, 2f).OnLine, Is.False);
			foreach (SceneGlowMode mode in Modes())
			{
				for (int i = 0; i < 50; i++)
				{
					float t = i * 77.1f, x = 31f * i - 700f, z = 900f - 13f * i;
					Assert.That(SceneGlowModes.Value(mode, 0, in ep, t, x, z, in sky, SceneGlowPlace.None),
						Is.EqualTo(SceneGlowModes.Value(mode, 0, in ep, t, x, z, in sky)), $"{mode}: no place is the old answer");
				}
			}
			// A biome whose lines are kept; pads are not lines.
			var withPad = Rille();
			withPad.Add(new VentLine { A = new Vector2(10f, 10f), B = new Vector2(10f, 10f), LineLength = 0f, Half = 20f, BiomeID = 5 });
			Assert.That(SceneGlowPlaces.Choose(withPad, id => id == 5).Count, Is.EqualTo(2));
		}

		[Test]
		public void StoredLines_MergeToTheCap_KeepingAlongAndLength()
		{
			// One sinuous line of 200 stretches, 10 m each.
			var line = new List<VentLine>();
			Vector2 p = Vector2.zero;
			for (int k = 0; k < 200; k++)
			{
				Vector2 q = p + new Vector2(10f * Mathf.Cos(0.3f * Mathf.Sin(k * 0.2f)), 10f * Mathf.Sin(0.3f * Mathf.Sin(k * 0.2f)));
				line.Add(new VentLine { A = p, B = q, AlongA = 10f * k, LineLength = 2000f, Half = 3f, Seed = 11u, BiomeID = 1 });
				p = q;
			}
			List<SceneGlowLine> chosen = SceneGlowPlaces.Choose(line, null);
			Assert.That(chosen.Count, Is.LessThanOrEqualTo(SceneGlowPlaces.MaxLines));
			Assert.That(chosen[0].Along, Is.EqualTo(SceneGlowPlaces.PhaseOf(11u)).Within(1e-3f));
			Assert.That(chosen.Sum(c => c.Span), Is.EqualTo(2000f).Within(0.05f), "the line's length kept");
			for (int i = 1; i < chosen.Count; i++)
			{
				Assert.That(chosen[i].Along, Is.EqualTo(chosen[i - 1].Along + chosen[i - 1].Span).Within(0.01f), "along runs on");
				Assert.That((chosen[i].A - chosen[i - 1].B).sqrMagnitude, Is.LessThan(1e-4f), "joined end to start");
			}
			// Deterministic: the same lines give the same stretches.
			List<SceneGlowLine> again = SceneGlowPlaces.Choose(line, null);
			Assert.That(again.Select(c => c.Along), Is.EqualTo(chosen.Select(c => c.Along)));
			// The cut's store: every landform line of every biome, one seed a run, as its vent lines'.
			var site = new BiomeLandformSite
			{
				BiomeID = 3,
				Seed = 0xC0FFEEu,
				Segments = new List<BiomeLandformSegment>
				{
					new BiomeLandformSegment(new Vector2(0f, 0f), new Vector2(50f, 0f), 6f, 4f, 0f, 100f),
					new BiomeLandformSegment(new Vector2(50f, 0f), new Vector2(100f, 0f), 6f, 4f, 50f, 100f),
					new BiomeLandformSegment(new Vector2(0f, 30f), new Vector2(80f, 30f), 6f, 4f, 0f, 80f),
				},
			};
			var plain = new BiomeLandformSite { BiomeID = 3, Seed = 1u };
			List<VentLine> stored = SceneGlowPlaces.FromLandforms(new[] { plain, site });
			Assert.That(stored.Count, Is.EqualTo(3));
			Assert.That(stored.All(l => l.Layout == BiomeVentLayout.Grid && l.BiomeID == 3), Is.True);
			Assert.That(stored[0].Seed, Is.EqualTo(stored[1].Seed));
			Assert.That(stored[2].Seed, Is.Not.EqualTo(stored[0].Seed), "a second run is a second line");
			List<VentLine> vents = IceStripes.FromLandforms(new[] { plain, site }, null, _ => BiomeVentLayout.Fissures);
			Assert.That(stored.Select(l => l.Seed), Is.EqualTo(vents.Select(l => l.Seed)), "a line's seed is its vent line's");
			Assert.That(SceneGlowPlaces.FromLandforms(null), Is.Empty);
		}

		[Test]
		public void StoredLines_KneadingReadsTheCracksBearing_AsItsVentsDo()
		{
			BiomeFountainEpisodes ep = Volcanic;
			foreach (float bearing in new[] { 0.1f, 0.7f, 1.5f, 2.6f })
			{
				var dir = new Vector2(Mathf.Cos(bearing), Mathf.Sin(bearing));
				var crack = new VentLine { A = new Vector2(40f, -20f), B = new Vector2(40f, -20f) + 300f * dir, AlongA = 0f, LineLength = 300f, Half = 5f, Seed = 3u, BiomeID = 2 };
				var reversed = new VentLine { A = crack.B, B = crack.A, AlongA = 0f, LineLength = 300f, Half = 5f, Seed = 3u, BiomeID = 2 };
				Vector2 at = crack.A + 120f * dir + 4f * new Vector2(-dir.y, dir.x);
				SceneGlowPlace place = SceneGlowPlaces.At(SceneGlowPlaces.Choose(new List<VentLine> { crack }, null), null, at.x, at.y);
				SceneGlowPlace back = SceneGlowPlaces.At(SceneGlowPlaces.Choose(new List<VentLine> { reversed }, null), null, at.x, at.y);
				for (int i = 0; i < 40; i++)
				{
					SceneGlowSky sky = Sky();
					sky.OrbitTurn = i / 40f;
					float glow = SceneGlowModes.Value(SceneGlowMode.Kneading, 0, in ep, 100f, at.x, at.y, in sky, in place);
					Vector4 stress = sky.TidalStress;
					float vent = VentTiming.Kneading(new Vector2(stress.x, stress.y), crack.Bearing);
					Assert.That(glow, Is.EqualTo(vent).Within(1e-4f), $"bearing {bearing}, orbit {sky.OrbitTurn}: the glow and the vents of one crack");
					Assert.That(SceneGlowModes.Value(SceneGlowMode.Kneading, 0, in ep, 100f, at.x, at.y, in sky, in back), Is.EqualTo(glow).Within(1e-5f), "a crack has no sense");
				}
			}
		}

		[Test]
		public void Episode_FollowsTheNearestStoredVent()
		{
			var schedule = new BiomeFountainEpisodes { SlotA = 1250, OnShare = 0.3f, RampSeconds = 20f };
			var sites = new List<VentSite>
			{
				new VentSite { Position = new Vector2(0f, 0f), Timing = BiomeVentTiming.Episode, TimingSeed = 111u, Seed = 1u },
				new VentSite { Position = new Vector2(30f, 0f), Timing = BiomeVentTiming.Steady, TimingSeed = 333u, Seed = 3u },
				new VentSite { Position = new Vector2(1000f, 0f), Timing = BiomeVentTiming.Episode, TimingSeed = 222u, Seed = 2u, Episodes = schedule },
			};
			List<SceneGlowVent> vents = SceneGlowPlaces.VentsOf(sites);
			Assert.That(vents.Count, Is.EqualTo(2), "only the vents that play the fountain episodes");
			Assert.That(vents[0].Episodes.ToString(), Is.EqualTo(Volcanic.ToString()), "an empty schedule is the volcanic default, as VentTiming's");
			SceneGlowPlace near = SceneGlowPlaces.At(null, vents, 40f, 10f), far = SceneGlowPlaces.At(null, vents, 900f, 0f);
			Assert.That(near.AtVent && near.VentSeed == 111u, Is.True);
			Assert.That(far.AtVent && far.VentSeed == 222u && far.VentEpisodes.SlotA == 1250, Is.True);
			BiomeFountainEpisodes own = Volcanic;
			SceneGlowSky sky = Sky();
			int playing = 0;
			for (int i = 0; i < 4000; i++)
			{
				float t = i * 2.5f;
				// The glow and its vent rise and fall as one (VentTiming's Episode: 0.1 + 0.9·slot; the glow's 0.12 + 0.88·slot).
				foreach ((SceneGlowPlace place, VentSite site, BiomeFountainEpisodes ventSchedule) in new[] { (near, sites[0], Volcanic), (far, sites[2], schedule) })
				{
					float glow = (SceneGlowModes.Value(SceneGlowMode.Episode, 0, in own, t, 50f, 0f, in sky, in place) - 0.12f) / 0.88f;
					float vent = (VentTiming.Strength(BiomeVentTiming.Episode, VentSky.Of(t, in sky), site.TimingSeed, 0f, in ventSchedule) - 0.1f) / 0.9f;
					Assert.That(glow, Is.EqualTo(vent).Within(1e-4f), $"at {t}");
					playing += glow > 0.5f ? 1 : 0;
				}
				Assert.That(SceneGlowModes.Value(SceneGlowMode.Episode, 0, in own, t, 50f, 0f, in sky, in near),
					Is.EqualTo(SceneGlowModes.Value(SceneGlowMode.Episode, 0, in own, t, 50f, 0f, in sky, in near)), "pure");
			}
			Assert.That(playing, Is.GreaterThan(0), "the episodes play");
			// No vent stored: the 500 m cell's seed, as before.
			SceneGlowPlace none = SceneGlowPlaces.At(null, SceneGlowPlaces.VentsOf(null), 50f, 0f);
			Assert.That(none.AtVent, Is.False);
			Assert.That(SceneGlowModes.Value(SceneGlowMode.Episode, 0, in own, 1234f, 50f, 0f, in sky, in none),
				Is.EqualTo(0.12f + 0.88f * BiomeFountainEpisodes.GlowAt(in own, 1234f, 50f, 0f)));
		}

		[Test]
		public void Anchors_KeepALightAndItsStoneOnOneRow_AcrossABiomeEdge()
		{
			// Four 100 m cells along x: rows 1 1 2 2 (an edge at x = 200); row 1 trails, row 2 shines still.
			var grid = new SceneGlowGrid { Cells = new byte[] { 1, 1, 2, 2 }, Width = 4, Height = 1, Rect = new Vector4(0f, 0f, 1f / 400f, 1f / 100f), Rows = 3 };
			string[] moves = { "none", "Trail", "Still" };
			Func<int, int, bool> same = (a, b) => moves[a] == moves[b];
			Assert.That(grid.RowAt(150f, 50f), Is.EqualTo(1));
			Assert.That(grid.RowAt(250f, 50f), Is.EqualTo(2));
			Assert.That(grid.RowAt(-5f, 50f), Is.EqualTo(0), "off the grid row 0, as the GPU");
			// A site straddling the edge with a light in it: the whole site reads its centre's row, so the stone across the edge and
			// its light agree.
			var sites = new List<Vector3> { new Vector3(190f, 50f, 30f), new Vector3(60f, 50f, 20f) };
			var lights = new List<Vector2> { new Vector2(195f, 50f), new Vector2(60f, 55f), new Vector2(199f, 90f), new Vector2(120f, 90f) };
			List<SceneGlowAnchor> anchors = SceneGlowPlaces.Anchors(in grid, sites, lights, same);
			Assert.That(anchors.Count, Is.EqualTo(2), "the straddling site and the lone light by the edge; not the site or light far from it");
			Assert.That(anchors[0].Centre, Is.EqualTo(new Vector2(190f, 50f)));
			Assert.That(anchors[0].Row, Is.EqualTo(1));
			Assert.That(SceneGlowPlaces.RowAt(anchors, 205f, 50f, grid.RowAt(205f, 50f)), Is.EqualTo(SceneGlowPlaces.RowAt(anchors, 195f, 50f, grid.RowAt(195f, 50f))),
				"the stone across the edge reads its light's row");
			Assert.That(anchors[1].Radius, Is.EqualTo(SceneGlowPlaces.LightReach));
			Assert.That(SceneGlowPlaces.RowAt(anchors, 203f, 90f, grid.RowAt(203f, 90f)), Is.EqualTo(1), "a lone light's stone a few metres over reads the light's row");
			Assert.That(SceneGlowPlaces.RowAt(anchors, 300f, 50f, grid.RowAt(300f, 50f)), Is.EqualTo(2), "outside every disc, the ground's own row");
			// Biomes whose glows move alike need no disc: every glow reads its own ground, as before.
			Assert.That(SceneGlowPlaces.Anchors(in grid, sites, lights, (a, b) => true), Is.Empty);
			Assert.That(SceneGlowPlaces.Anchors(default, sites, lights, same), Is.Empty);
			// A site without a light needs none; the cap holds.
			Assert.That(SceneGlowPlaces.Anchors(in grid, new List<Vector3> { new Vector3(200f, 50f, 30f) }, new List<Vector2>(), same), Is.Empty);
			var edge = new List<Vector2> { new Vector2(199f, 10f), new Vector2(201f, 40f), new Vector2(198f, 70f) };
			Assert.That(SceneGlowPlaces.Anchors(in grid, null, edge, same).Count, Is.EqualTo(3));
			Assert.That(SceneGlowPlaces.Anchors(in grid, null, edge, same, 2).Count, Is.EqualTo(2));
			Assert.That(SceneGlowPlaces.MaxAnchors, Is.EqualTo(32));
		}

		[Test]
		public void StoredPlaces_TheShaderAndThePlumbing()
		{
			string hlsl = Read(Path.Combine(Shaders, "FishGlowModes.hlsl"));
			StringAssert.Contains($"float4 _FishGlowLines[{SceneGlowPlaces.MaxLines}];", hlsl);
			StringAssert.Contains($"float4 _FishGlowLineData[{SceneGlowPlaces.MaxLines}];", hlsl);
			StringAssert.Contains($"float4 _FishGlowVents[{SceneGlowPlaces.MaxVents}];", hlsl);
			StringAssert.Contains($"float4 _FishGlowAnchors[{SceneGlowPlaces.MaxAnchors}];", hlsl);
			string table = Read("Assets/Scripts/Client/World/Terrain/GlowModeTable.cs");
			StringAssert.Contains("public const int Schedules = 16;", table);
			StringAssert.Contains("float4 _FishGlowEpisodes[16];", hlsl);
			StringAssert.Contains("float4 _FishGlowRamps[4];", hlsl);
			StringAssert.Contains("uint s = min(index, 15u);", hlsl);
			foreach (string g in new[] { "_FishGlowLines", "_FishGlowLineData", "_FishGlowVents", "_FishGlowAnchors", "_FishGlowPlaceInfo" })
			{
				StringAssert.Contains("\"" + g + "\"", table);
			}
			// The seed in two halves (a 32-bit seed in a float rounds), the schedule above the high half.
			StringAssert.Contains("(v.Seed >> 16) + 65536f * schedule, v.Seed & 0xFFFFu", table);
			StringAssert.Contains("seed = (high << 16) | (uint)round(v.w);", hlsl);
			// The same nearest search as the C#, and the old stand-ins kept for a scene that stores nothing.
			StringAssert.Contains("float t = length2 > 0.0 ? saturate(dot(xz - s.xy, d) / length2) : 0.0;", hlsl);
			StringAssert.Contains("along = data.x + bestT * data.y;", hlsl);
			StringAssert.Contains("along = x * 0.6 + z * 0.8;", hlsl);
			StringAssert.Contains("float bearing = 3.14159265359 * (0.5 + 0.5 * sin(FISH_GLOW_TURN * frac(x * 0.0011 + z * 0.0007)));", hlsl);
			StringAssert.Contains("uint seed = FishGlowMix(((uint)cell.x * 0x8DA6B343u) ^ ((uint)cell.y * 0xD8163841u), 0xE9150DEu);", hlsl);
			StringAssert.Contains("uint row = min(FishGlowAnchoredRow(originWS.xz, FishGlowRowAt(originWS.xz)), 63u);", hlsl);
			Assert.That(SceneGlowModes.TrailX, Is.EqualTo(0.6f));
			Assert.That(SceneGlowModes.TrailZ, Is.EqualTo(0.8f));
			// Plumbing: the cut stores every landform line, the table reads them (and the vents), the lights read the same places.
			StringAssert.Contains("map.SetGlowLines(SceneGlowPlaces.FromLandforms(SceneLandforms.Sites(scene)));", Read(Path.Combine(World, "SceneGenerator.cs")));
			string map = Read("Assets/Scripts/Shared/Implementation/Tools/Biomes/SceneBiomeMap.cs");
			StringAssert.Contains("[HideInInspector] public List<VentLine> GlowLines = new List<VentLine>();", map);
			StringAssert.Contains("SetGlowLines(null);", map);
			StringAssert.Contains("map.GlowLines != null && map.GlowLines.Count > 0 ? map.GlowLines : IceStripes.Of(map)", table);
			StringAssert.Contains("SceneGlowPlaces.VentsOf(VolcanicVents.Of(settings))", table);
			StringAssert.Contains("GlowModeTable.Place(settings);", Read("Assets/Scripts/Client/World/Terrain/FloraPaletteMap.cs"));
			StringAssert.Contains("GlowModeTable.Refresh();", Read("Assets/Scripts/Client/World/Weather/Sky/GlowSky.cs"));
			string pool = Read("Assets/Scripts/Client/World/Terrain/SiteLightPool.cs");
			Assert.That(Regex.Matches(pool, Regex.Escape("GlowModeTable.PlaceAt(e.Position)")).Count, Is.EqualTo(2), "the crystal's and the glow's light");
			// Cloud worlds: a crystal post carries its crystal (where the art exists) and its light takes that crystal's hue.
			string cut = Read(Path.Combine(World, "CloudWorld/CloudWorldCut.cs"));
			StringAssert.Contains("if (PostCrystal(prefab, at, facing, Vector3.one, seed, out GameObject crystal, out Vector3 crystalAt, out Vector3 crystalSize))", cut);
			StringAssert.Contains("SceneGlow.WithCrystalHue(seed, cradled ? postMineral.LightHue : -1)", cut);
			StringAssert.Contains("CloudWorldCut.PostCrystal(light, at,", Read(Path.Combine(World, "PointsOfInterest/Features/PointOfInterestSkyTraffic.cs")));
			Assert.That(CloudWorldCut.PostCrystalKind, Is.EqualTo(BiomeCaveCrystals.PostCrystal(BiomeCaveCrystals.Default())), "a post with no biome's crystal");
		}
	}
}
