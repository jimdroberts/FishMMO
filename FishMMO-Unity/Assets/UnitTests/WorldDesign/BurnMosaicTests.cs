using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The burn mosaic (2026-10-25, phase I2t; decisions 75, 82, 84): every fire a pure function of the shared clock and the scene's
	/// thermal year, continuous across the year's turn; deterministic cells; the stages; the GPU's year skipping drawing exactly the
	/// full answer; the blue fire only at night and only on breathable worlds; FishBurn.hlsl constant for constant; the template,
	/// spec, authoring, grass and terrain plumbing; and every template burning nothing until a round says so.
	/// </summary>
	[TestFixture]
	public class BurnMosaicTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders";
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		/// <summary>A short year (a 365th of it ten minutes), so the floats stay fine and a test spans many years.</summary>
		private const double Year = 365.0 * 600.0;
		private const uint Seed = 0x00A1B2C3u;

		private static string Read(string path) => File.ReadAllText(path);

		private static BurnMosaic.BurnClock At(double seconds, double coldest = 0.3) => BurnMosaic.ClockAt(seconds, Year, coldest);

		// ── The regime ─────────────────────────────────────────────

		[Test]
		public void Regimes_AreAppendOnly_AndParse()
		{
			Assert.That((int)BiomeBurnRegime.None, Is.EqualTo(0));
			Assert.That((int)BiomeBurnRegime.Occasional, Is.EqualTo(1));
			Assert.That((int)BiomeBurnRegime.Annual, Is.EqualTo(2));
			Assert.That((int)BiomeBurnRegime.Brimstone, Is.EqualTo(3));
			Assert.That(BiomeBurnRegimes.Last, Is.EqualTo(BiomeBurnRegime.Brimstone));
			foreach (BiomeBurnRegime r in Enum.GetValues(typeof(BiomeBurnRegime)))
			{
				Assert.That(BiomeBurnRegimes.TryParse(BiomeBurnRegimes.Signature(r).ToLowerInvariant(), out BiomeBurnRegime back), Is.True);
				Assert.That(back, Is.EqualTo(r));
			}
			Assert.That(BiomeBurnRegimes.TryParse("Wildfire", out _), Is.False);
			Assert.That(BiomeBurnRegimes.IsVeld(BiomeBurnRegime.Annual) && BiomeBurnRegimes.IsVeld(BiomeBurnRegime.Occasional), Is.True);
			Assert.That(BiomeBurnRegimes.IsVeld(BiomeBurnRegime.Brimstone) || BiomeBurnRegimes.IsVeld(BiomeBurnRegime.None), Is.False);
		}

		[Test]
		public void Brimstone_IsGatedByTheAir()
		{
			BiomeWorldConditions earth = BiomeWorldConditions.Earthlike;
			BiomeWorldConditions airless = earth;
			airless.Atmosphere = AtmosphereKind.None;
			BiomeWorldConditions thin = earth;
			thin.Atmosphere = AtmosphereKind.Thin;
			Assert.That(BiomeAir.IsBreathable(earth), Is.True);
			Assert.That(BiomeBurnRegimes.Drawn(BiomeBurnRegime.Brimstone, BiomeAir.IsBreathable(earth)), Is.EqualTo(BiomeBurnRegime.Brimstone));
			Assert.That(BiomeBurnRegimes.Drawn(BiomeBurnRegime.Brimstone, BiomeAir.IsBreathable(airless)), Is.EqualTo(BiomeBurnRegime.None));
			Assert.That(BiomeBurnRegimes.Drawn(BiomeBurnRegime.Brimstone, BiomeAir.IsBreathable(thin)), Is.EqualTo(BiomeBurnRegime.None));
			// Veld fires and none are untouched by the air (the grass's own biome selection asks for it).
			foreach (bool breathable in new[] { true, false })
			{
				Assert.That(BiomeBurnRegimes.Drawn(BiomeBurnRegime.Annual, breathable), Is.EqualTo(BiomeBurnRegime.Annual));
				Assert.That(BiomeBurnRegimes.Drawn(BiomeBurnRegime.Occasional, breathable), Is.EqualTo(BiomeBurnRegime.Occasional));
				Assert.That(BiomeBurnRegimes.Drawn(BiomeBurnRegime.None, breathable), Is.EqualTo(BiomeBurnRegime.None));
			}
			Assert.That(BiomeBurnRegimes.Drawn((BiomeBurnRegime)9, true), Is.EqualTo(BiomeBurnRegime.None), "an unknown value draws nothing");
			// The client table builds its rows through Drawn with the scene's IsBreathable.
			string table = Read("Assets/Scripts/Client/World/Terrain/BurnMosaicTable.cs");
			Assert.That(table, Does.Contain("bool breathable = scene == null || BiomeAir.IsBreathable(scene.WorldConditions);"));
			Assert.That(table, Does.Contain("BiomeBurnRegimes.Drawn(biome != null ? biome.BurnRegime : BiomeBurnRegime.None, breathable)"));
		}

		// ── The clock ──────────────────────────────────────────────

		[Test]
		public void TheClock_IsPure_AndTurnsAtTheColdestPoint()
		{
			for (double t = 0.0; t < 6.0 * Year; t += Year / 37.0)
			{
				BurnMosaic.BurnClock a = At(t), b = At(t);
				Assert.That(a.Year, Is.EqualTo(b.Year));
				Assert.That(a.Seconds, Is.EqualTo(b.Seconds));
				Assert.That(a.Seconds, Is.InRange(0f, (float)Year));
			}
			// The year turns exactly at the coldest point (0.3 of the calendar year here).
			BurnMosaic.BurnClock before = At(2.3 * Year - 1.0), after = At(2.3 * Year + 1.0);
			Assert.That(after.Year, Is.EqualTo(before.Year + 1));
			Assert.That(before.Seconds, Is.EqualTo((float)Year - 1f).Within(0.1f));
			Assert.That(after.Seconds, Is.EqualTo(1f).Within(0.1f));
			Assert.That(BurnMosaic.ClockAt(1000.0, 0.0, 0.0).YearSeconds, Is.EqualTo(0f), "no year: nothing burns");
		}

		// ── The cells ──────────────────────────────────────────────

		[Test]
		public void TheCells_AreDeterministic_AndHoldTheirShares()
		{
			foreach (BiomeBurnRegime regime in new[] { BiomeBurnRegime.Annual, BiomeBurnRegime.Occasional })
			{
				BurnMosaic.WindowOf(regime, out float from, out float to);
				int burns = 0, n = 0;
				for (int year = 0; year < 8; year++)
				{
					for (int cz = -10; cz < 10; cz++)
					{
						for (int cx = -10; cx < 10; cx++)
						{
							BurnMosaic.Fire a = BurnMosaic.FireOf(cx, cz, year, Seed, regime, (float)Year);
							BurnMosaic.Fire b = BurnMosaic.FireOf(cx, cz, year, Seed, regime, (float)Year);
							Assert.That(a.Burns == b.Burns && a.X == b.X && a.Z == b.Z && a.Lit == b.Lit && a.Speed == b.Speed, Is.True);
							n++;
							burns += a.Burns ? 1 : 0;
							Assert.That(a.Lit, Is.InRange(from * (float)Year, to * (float)Year));
							Assert.That(a.Speed, Is.InRange(BurnMosaic.MinSpeed, BurnMosaic.MaxSpeed));
							Assert.That(a.X / BurnMosaic.CellMetres - cx, Is.InRange(0.15f - 1e-4f, 0.85f + 1e-4f));
							Assert.That(a.Z / BurnMosaic.CellMetres - cz, Is.InRange(0.15f - 1e-4f, 0.85f + 1e-4f));
						}
					}
				}
				float share = burns / (float)n;
				Assert.That(share, Is.EqualTo(BurnMosaic.ChanceOf(regime)).Within(0.04f), $"{regime} burns its share of cells");
			}
			// Each year re-hashes the mosaic; another scene's seed another mosaic.
			int differ = 0, seedDiffer = 0;
			for (int cx = 0; cx < 50; cx++)
			{
				differ += BurnMosaic.FireOf(cx, 3, 4, Seed, BiomeBurnRegime.Annual, (float)Year).Lit != BurnMosaic.FireOf(cx, 3, 5, Seed, BiomeBurnRegime.Annual, (float)Year).Lit ? 1 : 0;
				seedDiffer += BurnMosaic.FireOf(cx, 3, 4, Seed, BiomeBurnRegime.Annual, (float)Year).X != BurnMosaic.FireOf(cx, 3, 4, Seed ^ 1u, BiomeBurnRegime.Annual, (float)Year).X ? 1 : 0;
			}
			Assert.That(differ, Is.GreaterThan(45));
			Assert.That(seedDiffer, Is.GreaterThan(45));
			Assert.That(BurnMosaic.SeedOf("Moron 1"), Is.EqualTo(BurnMosaic.SeedOf("Moron 1")));
			Assert.That(BurnMosaic.SeedOf("Moron 1"), Is.LessThan(1u << 24), "exact in a float on the GPU");
			Assert.That(BurnMosaic.ChanceOf(BiomeBurnRegime.Brimstone) + BurnMosaic.ChanceOf(BiomeBurnRegime.None), Is.EqualTo(0f));
		}

		[Test]
		public void TheFront_RunsFasterDownwind()
		{
			// One burning cell: the head (downwind) arrives before the back (upwind) at the same distance from the ignition.
			BurnMosaic.Fire fire = default;
			int cx = 0;
			for (; cx < 200; cx++)
			{
				fire = BurnMosaic.FireOf(cx, 0, 7, Seed, BiomeBurnRegime.Annual, (float)Year);
				if (fire.Burns)
				{
					break;
				}
			}
			Assert.That(fire.Burns, Is.True);
			float now = fire.Lit + 20000f;
			float head = BurnMosaic.ArrivalAge(fire.X + 25f, fire.Z, 7, now, 1f, 0f, Seed, BiomeBurnRegime.Annual, (float)Year);
			float back = BurnMosaic.ArrivalAge(fire.X - 25f, fire.Z, 7, now, 1f, 0f, Seed, BiomeBurnRegime.Annual, (float)Year);
			Assert.That(head, Is.GreaterThan(back), "the head fire reached its point first");
			// Before it is lit nothing near the ignition has burnt.
			Assert.That(BurnMosaic.ArrivalAge(fire.X, fire.Z, 7, fire.Lit - 1f, 1f, 0f, Seed, BiomeBurnRegime.Annual, (float)Year), Is.LessThan(0f));
		}

		// ── Pure in the clock, continuous across the wrap ─────────────

		[Test]
		public void TheAge_IsPureInTheClock_AndContinuousAcrossTheYearsTurn()
		{
			var rng = new System.Random(5);
			int burnt = 0;
			for (int k = 0; k < 400; k++)
			{
				float x = (float)(rng.NextDouble() * 6000.0 - 3000.0), z = (float)(rng.NextDouble() * 6000.0 - 3000.0);
				double t = (2.3 + rng.NextDouble() * 3.0) * Year;
				BurnMosaic.BurnClock c = At(t);
				float a = BurnMosaic.AgeAt(x, z, in c, 0.6f, 0.8f, Seed, BiomeBurnRegime.Annual);
				float b = BurnMosaic.AgeAt(x, z, in c, 0.6f, 0.8f, Seed, BiomeBurnRegime.Annual);
				Assert.That(a, Is.EqualTo(b), "the same moment, the same age, whatever was asked before");
				if (a >= 0f)
				{
					burnt++;
					// Time runs on: 60 s later the same fire is 60 s older (unless this year's front arrived in between).
					BurnMosaic.BurnClock later = At(t + 60.0);
					float l = BurnMosaic.AgeAt(x, z, in later, 0.6f, 0.8f, Seed, BiomeBurnRegime.Annual);
					Assert.That(l - a, Is.EqualTo(60f).Within(1f).Or.LessThan(60f));
				}
			}
			Assert.That(burnt, Is.GreaterThan(100), "most of the ground burnt in the year or the last");
			// Across the turn of the year (the coldest point): last year's fire keeps ageing, nothing jumps.
			for (int k = 0; k < 200; k++)
			{
				float x = (float)(rng.NextDouble() * 4000.0), z = (float)(rng.NextDouble() * 4000.0);
				double turn = 4.3 * Year;
				BurnMosaic.BurnClock a = At(turn - 5.0), b = At(turn + 5.0);
				Assert.That(b.Year, Is.EqualTo(a.Year + 1));
				float before = BurnMosaic.AgeAt(x, z, in a, 0.6f, 0.8f, Seed, BiomeBurnRegime.Annual);
				float after = BurnMosaic.AgeAt(x, z, in b, 0.6f, 0.8f, Seed, BiomeBurnRegime.Annual);
				if (before < 0f)
				{
					Assert.That(after, Is.LessThan(0f), "unburnt stays unburnt over the turn (the window opens later)");
				}
				else if (before < (float)Year)
				{
					Assert.That(after - before, Is.EqualTo(10f).Within(1f), "the year's fire, 10 s older");
				}
				else
				{
					// A fire of the year before last drops out at the turn: long past anything a stage shows.
					Assert.That(BurnMosaic.StageOf(before, (float)Year).Regrow, Is.EqualTo(1f));
					Assert.That(BurnMosaic.StageOf(before, (float)Year).Tone, Is.EqualTo(0f));
				}
				BurnMosaic.BurnStage sa = BurnMosaic.StageAt(x, z, in a, 0.6f, 0.8f, Seed, BiomeBurnRegime.Annual);
				BurnMosaic.BurnStage sb = BurnMosaic.StageAt(x, z, in b, 0.6f, 0.8f, Seed, BiomeBurnRegime.Annual);
				Assert.That(Math.Abs(sa.Scar - sb.Scar) + Math.Abs(sa.Tone - sb.Tone) + Math.Abs(sa.Regrow - sb.Regrow), Is.LessThan(1e-3f));
			}
			Assert.That(BurnMosaic.AgeAt(0f, 0f, At(3.0 * Year), 1f, 0f, Seed, BiomeBurnRegime.None), Is.EqualTo(BurnMosaic.Never));
			Assert.That(BurnMosaic.AgeAt(0f, 0f, At(3.0 * Year), 1f, 0f, Seed, BiomeBurnRegime.Brimstone), Is.EqualTo(BurnMosaic.Never));
		}

		[Test]
		public void TheGpusYearSkipping_DrawsExactlyTheFullAnswer()
		{
			var rng = new System.Random(11);
			foreach (BiomeBurnRegime regime in new[] { BiomeBurnRegime.Annual, BiomeBurnRegime.Occasional })
			{
				for (int k = 0; k < 3000; k++)
				{
					float x = (float)(rng.NextDouble() * 5000.0), z = (float)(rng.NextDouble() * 5000.0);
					BurnMosaic.BurnClock c = At((3.0 + rng.NextDouble() * 2.0) * Year);
					BurnMosaic.BurnStage full = BurnMosaic.StageOf(BurnMosaic.AgeAt(x, z, in c, -0.3f, 0.9f, Seed, regime), c.YearSeconds);
					BurnMosaic.BurnStage drawn = BurnMosaic.StageAt(x, z, in c, -0.3f, 0.9f, Seed, regime);
					Assert.That(drawn.Flame, Is.EqualTo(full.Flame).Within(1e-6f));
					Assert.That(drawn.Ember, Is.EqualTo(full.Ember).Within(1e-6f));
					Assert.That(drawn.Scar, Is.EqualTo(full.Scar).Within(1e-6f));
					Assert.That(drawn.Regrow, Is.EqualTo(full.Regrow).Within(1e-6f));
					Assert.That(drawn.Tone, Is.EqualTo(full.Tone).Within(1e-6f));
					Assert.That(drawn.Lily, Is.EqualTo(full.Lily).Within(1e-6f));
				}
				// Out of the season (a real-length year: 365 six-hour days) nothing is read at all.
				const double real = 365.0 * 6.0 * 3600.0;
				BurnMosaic.WindowOf(regime, out float from, out _);
				Assert.That(BurnMosaic.LiveYears(BurnMosaic.ClockAt(3.3 * real + 0.5 * from * real, real, 0.3), regime), Is.EqualTo(0));
				Assert.That(BurnMosaic.LiveYears(BurnMosaic.ClockAt(3.3 * real + 0.9 * real, real, 0.3), regime), Is.EqualTo(0));
				Assert.That(BurnMosaic.LiveYears(BurnMosaic.ClockAt(3.3 * real + 0.2 * real, real, 0.3), regime), Is.EqualTo(1));
			}
		}

		// ── The stages ─────────────────────────────────────────────

		[Test]
		public void TheStages_RunFromFlameToFlushToGone()
		{
			float day = (float)Year / BurnMosaic.YearDays;
			BurnMosaic.BurnStage none = BurnMosaic.StageOf(BurnMosaic.Never, (float)Year);
			Assert.That(none.Flame + none.Ember + none.Scar + none.Tone + none.Lily, Is.EqualTo(0f));
			Assert.That(none.Regrow, Is.EqualTo(1f));
			BurnMosaic.BurnStage front = BurnMosaic.StageOf(0f, (float)Year);
			Assert.That(front.Flame, Is.EqualTo(1f));
			Assert.That(front.Regrow, Is.EqualTo(BurnMosaic.Stubble));
			Assert.That(front.Tone, Is.EqualTo(1f), "charcoal");
			Assert.That(front.Scar, Is.EqualTo(1f));
			Assert.That(BurnMosaic.StageOf(BurnMosaic.FlameSeconds + 1f, (float)Year).Flame, Is.EqualTo(0f), "a thin line");
			BurnMosaic.BurnStage flushed = BurnMosaic.StageOf((BurnMosaic.CharDays + BurnMosaic.FlushDays) * day, (float)Year);
			Assert.That(flushed.Regrow, Is.EqualTo(1f).Within(1e-5f));
			Assert.That(flushed.Tone, Is.EqualTo(BurnMosaic.EmeraldTone).Within(1e-5f), "full emerald");
			BurnMosaic.BurnStage lilies = BurnMosaic.StageOf(14f * day, (float)Year);
			Assert.That(lilies.Lily, Is.EqualTo(1f).Within(1e-5f), "fire lilies two weeks after");
			Assert.That(BurnMosaic.StageOf(3f * day, (float)Year).Lily, Is.EqualTo(0f));
			Assert.That(BurnMosaic.StageOf(30f * day, (float)Year).Lily, Is.EqualTo(0f));
			BurnMosaic.BurnStage gone = BurnMosaic.StageOf(BurnMosaic.GreenDays * day, (float)Year);
			Assert.That(gone.Tone + gone.Scar + gone.Lily, Is.EqualTo(0f).Within(1e-6f));
			Assert.That(gone.Regrow, Is.EqualTo(1f));
			// Continuous through every stage (1-minute steps over 80 days of a real-length year).
			float realYear = 365f * 6f * 3600f, realDay = realYear / BurnMosaic.YearDays;
			BurnMosaic.BurnStage last = BurnMosaic.StageOf(BurnMosaic.EmberSeconds, realYear);
			for (float age = BurnMosaic.EmberSeconds; age < 80f * realDay; age += 60f)
			{
				BurnMosaic.BurnStage s = BurnMosaic.StageOf(age, realYear);
				Assert.That(Math.Abs(s.Tone - last.Tone) + Math.Abs(s.Regrow - last.Regrow) + Math.Abs(s.Scar - last.Scar) + Math.Abs(s.Lily - last.Lily), Is.LessThan(0.01f), $"age {age}");
				last = s;
			}
		}

		[Test]
		public void TheBlueFire_BurnsOnlyAtNight_AndFlickersAcrossTheWrap()
		{
			Assert.That(BurnMosaic.NightOf(0.5f), Is.EqualTo(0f));
			Assert.That(BurnMosaic.NightOf(-0.5f), Is.EqualTo(1f));
			Assert.That(BurnMosaic.Flicker(0f, 0.3f), Is.EqualTo(BurnMosaic.Flicker(BurnMosaic.FlickerWrapSeconds, 0.3f)).Within(2e-3f));
			int lit = 0;
			for (int k = 0; k < 4000; k++)
			{
				float x = k * 1.37f, z = k * 0.91f;
				Assert.That(BurnMosaic.SeepAt(x, z, 123f, 0f, Seed), Is.EqualTo(0f), "never by day");
				float f = BurnMosaic.SeepAt(x, z, 123f, 1f, Seed);
				Assert.That(f, Is.InRange(0f, 1f));
				Assert.That(BurnMosaic.SeepAt(x, z, 123f, 1f, Seed), Is.EqualTo(f));
				lit += f > 0f ? 1 : 0;
			}
			Assert.That(lit, Is.InRange(10, 1200), "seeps are scattered, not everywhere");
		}

		// ── The shader mirror ─────────────────────────────────────

		[Test]
		public void TheShader_MirrorsTheConstants()
		{
			string hlsl = Read(Path.Combine(Shaders, "FishBurn.hlsl"));
			float F(string name)
			{
				Match m = Regex.Match(hlsl, @"#define " + name + @" \(?([0-9.]+)(?: / ([0-9.]+))?\)?\s*\n");
				Assert.That(m.Success, Is.True, name);
				float v = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
				return m.Groups[2].Success ? v / float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : v;
			}
			Assert.That(F("FISH_BURN_CELL"), Is.EqualTo(BurnMosaic.CellMetres));
			Assert.That(F("FISH_BURN_JITTER"), Is.EqualTo(BurnMosaic.Jitter));
			Assert.That(F("FISH_BURN_ECCENTRICITY"), Is.EqualTo(BurnMosaic.Eccentricity));
			Assert.That(F("FISH_BURN_MIN_SPEED"), Is.EqualTo(BurnMosaic.MinSpeed).Within(1e-7f));
			Assert.That(F("FISH_BURN_MAX_SPEED"), Is.EqualTo(BurnMosaic.MaxSpeed).Within(1e-7f));
			Assert.That(F("FISH_BURN_RAGGED"), Is.EqualTo(BurnMosaic.Ragged));
			Assert.That(F("FISH_BURN_RAGGED_METRES"), Is.EqualTo(BurnMosaic.RaggedMetres));
			Assert.That(F("FISH_BURN_FLAME_SECONDS"), Is.EqualTo(BurnMosaic.FlameSeconds));
			Assert.That(F("FISH_BURN_EMBER_SECONDS"), Is.EqualTo(BurnMosaic.EmberSeconds));
			Assert.That(F("FISH_BURN_CHAR_DAYS"), Is.EqualTo(BurnMosaic.CharDays));
			Assert.That(F("FISH_BURN_FLUSH_DAYS"), Is.EqualTo(BurnMosaic.FlushDays));
			Assert.That(F("FISH_BURN_SCAR_DAYS"), Is.EqualTo(BurnMosaic.ScarDays));
			Assert.That(F("FISH_BURN_GREEN_DAYS"), Is.EqualTo(BurnMosaic.GreenDays));
			Assert.That(F("FISH_BURN_LILY_FROM_DAYS"), Is.EqualTo(BurnMosaic.LilyFromDays));
			Assert.That(F("FISH_BURN_LILY_TO_DAYS"), Is.EqualTo(BurnMosaic.LilyToDays));
			Assert.That(F("FISH_BURN_STUBBLE"), Is.EqualTo(BurnMosaic.Stubble));
			Assert.That(F("FISH_BURN_YEAR_DAYS"), Is.EqualTo(BurnMosaic.YearDays));
			Assert.That(F("FISH_BURN_EMERALD_TONE"), Is.EqualTo(BurnMosaic.EmeraldTone).Within(1e-7f));
			Assert.That(F("FISH_BURN_ANNUAL_CHANCE"), Is.EqualTo(BurnMosaic.AnnualChance));
			Assert.That(F("FISH_BURN_ANNUAL_FROM"), Is.EqualTo(BurnMosaic.AnnualFrom));
			Assert.That(F("FISH_BURN_ANNUAL_TO"), Is.EqualTo(BurnMosaic.AnnualTo));
			Assert.That(F("FISH_BURN_OCCASIONAL_CHANCE"), Is.EqualTo(BurnMosaic.OccasionalChance));
			Assert.That(F("FISH_BURN_OCCASIONAL_FROM"), Is.EqualTo(BurnMosaic.OccasionalFrom));
			Assert.That(F("FISH_BURN_OCCASIONAL_TO"), Is.EqualTo(BurnMosaic.OccasionalTo));
			Assert.That(F("FISH_BURN_SEEP_METRES"), Is.EqualTo(BurnMosaic.SeepMetres));
			Assert.That(F("FISH_BURN_SEEP_CHANCE"), Is.EqualTo(BurnMosaic.SeepChance));
			Assert.That(F("FISH_BURN_SEEP_MIN_RADIUS"), Is.EqualTo(BurnMosaic.SeepMinRadius));
			Assert.That(F("FISH_BURN_SEEP_MAX_RADIUS"), Is.EqualTo(BurnMosaic.SeepMaxRadius));
			Assert.That(F("FISH_BURN_FLICKER_WRAP"), Is.EqualTo(BurnMosaic.FlickerWrapSeconds));
			Assert.That(F("FISH_BURN_FLICKER_CYCLES_A"), Is.EqualTo(BurnMosaic.FlickerCyclesA));
			Assert.That(F("FISH_BURN_FLICKER_CYCLES_B"), Is.EqualTo(BurnMosaic.FlickerCyclesB));
			Assert.That(hlsl, Does.Contain($"#define FISH_BURN_SALT_CELL 0x{BurnMosaic.SaltCell:X8}u"));
			Assert.That(hlsl, Does.Contain($"#define FISH_BURN_SALT_RAGGED 0x{BurnMosaic.SaltRagged:X8}u"));
			Assert.That(hlsl, Does.Contain($"#define FISH_BURN_SALT_SEEP 0x{BurnMosaic.SaltSeep:X8}u"));
			Assert.That(hlsl, Does.Contain("return (year & 0xFFFFu) * 0x9E3779B9u ^ seed * 0x85EBCA6Bu ^ FISH_BURN_SALT_CELL;"));
			Assert.That(hlsl, Does.Contain("#define FISH_BURN_BRIMSTONE 3u"));
			Assert.That(hlsl, Does.Contain("#define FISH_BURN_ANNUAL 2u"));
			Assert.That(hlsl, Does.Contain("#define FISH_BURN_OCCASIONAL 1u"));
			Assert.That(BurnMosaicTable.Vents, Is.EqualTo(int.Parse(Regex.Match(hlsl, @"#define FISH_BURN_VENTS (\d+)").Groups[1].Value)));
			Assert.That(BurnMosaicTable.Rows, Is.EqualTo(64), "_FishBurnRows[16] holds four rows a vector");
			// The hashes are FishFlora.hlsl's.
			string flora = Read(Path.Combine(Shaders, "FishFlora.hlsl"));
			Assert.That(flora, Does.Contain("x ^= x >> 16; x *= 0x7feb352du;"));
			Assert.That(flora, Does.Contain("return FishFloraHash((uint)cell.x * 0x8da6b343u ^ (uint)cell.y * 0xd8163841u ^ salt * 0xcb1ab31fu);"));
		}

		// ── Grass colour bits ──────────────────────────────────────

		[Test]
		public void TheBurnTone_RidesTheColoursFreeBits()
		{
			uint colour = GrassMath.WithHeadPart(GrassMath.PackColour(0.3f, 0.4f, 0.1f, true), 2) | GrassMath.RecordBits(20, 3f);
			for (int q = 0; q <= 7; q++)
			{
				uint toned = GrassMath.WithBurnTone(colour, q / 7f, 0.25f);
				Assert.That(GrassMath.BurnToneOf(toned), Is.EqualTo(q / 7f).Within(1e-6f));
				Assert.That(toned & ~(7u << GrassMath.BurnToneShift), Is.EqualTo(colour), "every other bit kept");
				Assert.That(GrassMath.HeadPartOf(toned), Is.EqualTo(2));
			}
			Assert.That(GrassMath.WithBurnTone(colour, 0f, 0.999f), Is.EqualTo(colour), "untouched blades are exactly as before");
			string compute = Read(Path.Combine(Shaders, "FishGrassBlades.compute"));
			Assert.That(compute, Does.Contain("uint q = (uint)clamp((int)floor(saturate(tone) * 7.0 + dither), 0, 7);"));
			Assert.That(compute, Does.Contain("return (colour & ~(7u << 24)) | (q << 24);"));
		}

		// ── Plumbing ───────────────────────────────────────────────

		[Test]
		public void ThePlumbing_IsWired()
		{
			string flora = Read("Assets/Scripts/Client/World/Terrain/FloraPaletteMap.cs");
			Assert.That(flora, Does.Contain("BurnMosaicTable.Build(rowBiomes, settings);"));
			Assert.That(flora, Does.Contain("BurnMosaicTable.Publish(on);"));
			Assert.That(flora, Does.Contain("BurnMosaicTable.Release();"));
			Assert.That(Read("Assets/Scripts/Client/World/Weather/Sky/GlowSky.cs"), Does.Contain("BurnMosaicTable.Frame(in sky,"));
			string renderer = Read("Assets/Scripts/Client/World/Terrain/Grass/GrassBladeRenderer.cs");
			Assert.That(renderer, Does.Contain("BurnMosaicTable.BindCompute(cmd, compute);"));
			Assert.That(renderer, Does.Contain("look.AfterBurn ? 1f : 0f"));
			string compute = Read(Path.Combine(Shaders, "FishGrassBlades.compute"));
			Assert.That(compute, Does.Contain("#include \"FishBurn.hlsl\""));
			Assert.That(compute, Does.Contain("height *= burnRegrow;"));
			Assert.That(compute, Does.Contain("bool afterBurn = GrassTypeRow(type, GRASS_ROW_MORE).w > 0.5;"));
			Assert.That(Read(Path.Combine(Shaders, "FishGrassBlades.hlsl")), Does.Contain("uint burnBits = (blade.colour >> 24) & 7u;"));
			string terrain = Read(Path.Combine(Shaders, "FishWeatherTerrainArrayPasses.hlsl"));
			Assert.That(terrain, Does.Contain("#include \"FishBurn.hlsl\""));
			Assert.That(terrain, Does.Contain("burnEmission = FishBurnGround(inputData.positionWS, inputData.normalWS, burnTrodden, albedo, smoothness);"));
			Assert.That(Regex.Matches(terrain, @"color\.rgb \+= \(?(half3\))?burnEmission;").Count, Is.EqualTo(2), "forward and GBuffer");
			// Template, spec, authoring.
			Assert.That(BiomeTemplate.Authored.Burn, Is.EqualTo("burnRegime"));
			var entry = new BiomeArtSpec.Entry { Biome = "Probe" };
			Assert.That(entry.BurnRegime.HasValue, Is.False);
			Assert.That(entry.Burns(BiomeBurnRegime.Annual).BurnRegime, Is.EqualTo(BiomeBurnRegime.Annual));
			string authoring = Read(Path.Combine(World, "ProceduralArt/BiomeArtAuthoring.cs"));
			Assert.That(authoring, Does.Contain("AuthorBurn(biome, entry, report, apply);"));
			Assert.That(authoring, Does.Contain("AuthorText(biome, BiomeTemplate.Authored.Burn, BiomeBurnRegimes.Signature(biome.BurnRegime), BiomeBurnRegimes.Signature(target),"));
			// A look says it grows after a burn only when it does, so every older signature and mark reads the same.
			var look = new BiomeGrassType();
			string plain = look.Signature();
			Assert.That(plain, Does.Not.Contain("|F"));
			Assert.That(look.AfterBurning().Signature(), Is.EqualTo(plain + "|F"));
		}

		// ── Defaults: nothing changes ─────────────────────────────────

		[Test]
		public void Defaults_BurnNothing_OnEveryTemplate()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.BurnRegime.HasValue, Is.False, $"{entry.Biome} names no burn regime yet (its round authors it)");
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
				Match m = Regex.Match(text, @"^  burnRegime: (\d+)", RegexOptions.Multiline);
				Assert.That(!m.Success || m.Groups[1].Value == "0", Is.True, $"{Path.GetFileName(path)} burns");
				Assert.That(Regex.IsMatch(text, @"^\s+AfterBurn: 1", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} has an after-burn grass");
			}
			Assert.That(templates, Is.EqualTo(83));
			// With no burning row the table is off and every shader takes its old path (_FishBurnInfo.x = 0).
			string table = Read("Assets/Scripts/Client/World/Terrain/BurnMosaicTable.cs");
			Assert.That(table, Does.Contain("info = On ? new Vector4(1f, 0f, 0f, 0f) : Vector4.zero;"));
			Assert.That(table, Does.Contain("Shader.SetGlobalVector(InfoId, published ? info : Vector4.zero);"));
			Assert.That(Read(Path.Combine(Shaders, "FishBurn.hlsl")), Does.Contain("if (_FishBurnInfo.x < 0.5 || _FishFloraInfo.x < 0.5)"));
			Assert.That(new BiomeGrassType().AfterBurn, Is.False);
			// No stage outlives a year, so remembering this year and the last is enough.
			Assert.That(BurnMosaic.AnnualTo * BurnMosaic.YearDays + BurnMosaic.GreenDays, Is.LessThan(BurnMosaic.YearDays));
		}
	}
}
