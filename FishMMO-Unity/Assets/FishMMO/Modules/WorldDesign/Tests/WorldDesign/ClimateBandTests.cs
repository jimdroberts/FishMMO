using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Climate bands on spawn rules (<see cref="PrefabSpawnRule.useClimateBand"/>): the band's arithmetic,
	/// that a rule without one is untouched — the same factor of 1 in the scatter and the same
	/// fingerprint in the authoring tool as before bands existed — and that the spec's <c>Band</c> helper
	/// reaches the rule and its earlier versions.
	/// </summary>
	/// <remarks>
	/// Pure: no terrain, no assets, no native Unity calls, so it runs outside the editor. The pinned
	/// fingerprints were taken from the code before bands were added; a change to them means every
	/// rule the authoring tool ever marked would read as hand-tuned and stop receiving spec updates.
	/// </remarks>
	[TestFixture]
	public class ClimateBandTests
	{
		private const float Falloff = 0.1f;

		[Test]
		public void ARuleWithoutABand_GrowsInEveryClimate()
		{
			var rule = new PrefabSpawnRule();
			Assert.That(rule.useClimateBand, Is.False, "the off state must be the field's default, so old assets load with it off");
			for (float t = -1f; t <= 1f; t += 0.125f)
			{
				for (float h = -1f; h <= 1f; h += 0.25f)
				{
					Assert.That(TerrainScatter.ClimateBand(rule, t, h), Is.EqualTo(1f), $"no band at T {t}, H {h}");
				}
			}
			Assert.That(TerrainScatter.ClimateBand(null, 0f, 0f), Is.EqualTo(1f), "no rule");

			// Switched on with the default ranges: both axes open at ±1, so still everywhere.
			rule.useClimateBand = true;
			Assert.That(TerrainScatter.ClimateBand(rule, 1f, 1f), Is.EqualTo(1f), "the hottest, wettest place a world has");
			Assert.That(TerrainScatter.ClimateBand(rule, -1f, -1f), Is.EqualTo(1f), "the coldest, driest");
			Assert.That(TerrainScatter.ClimateBand(rule, 0.3f, -0.2f), Is.EqualTo(1f));
		}

		[Test]
		public void TheBand_IsFullInside_EmptyOutside_AndHalfAtItsEnds()
		{
			var band = new Vector2(0.2f, 0.6f);
			Assert.That(TerrainScatter.ClimateBand(band, Falloff, 0.4f), Is.EqualTo(1f), "inside");
			Assert.That(TerrainScatter.ClimateBand(band, Falloff, 0.26f), Is.EqualTo(1f), "inside by more than half the falloff");
			Assert.That(TerrainScatter.ClimateBand(band, Falloff, 0.1f), Is.EqualTo(0f), "colder than the band");
			Assert.That(TerrainScatter.ClimateBand(band, Falloff, 0.9f), Is.EqualTo(0f), "warmer than the band");
			Assert.That(TerrainScatter.ClimateBand(band, Falloff, 0.2f), Is.EqualTo(0.5f).Within(1e-5f), "the soft edge is centred on the end");
			Assert.That(TerrainScatter.ClimateBand(band, Falloff, 0.6f), Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(TerrainScatter.ClimateBand(band, Falloff, 0.15f), Is.EqualTo(0f).Within(1e-6f), "none a half-width outside");
			Assert.That(TerrainScatter.ClimateBand(band, Falloff, 0.25f), Is.EqualTo(1f).Within(1e-6f), "full a half-width inside");
		}

		[Test]
		public void TheSoftEdge_RisesSmoothlyAndNeverFalls()
		{
			var band = new Vector2(0f, 1f);
			float previous = -1f;
			for (int i = 0; i <= 200; i++)
			{
				float t = -0.2f + i * 0.002f;
				float grow = TerrainScatter.ClimateBand(band, Falloff, t);
				Assert.That(grow, Is.InRange(0f, 1f));
				Assert.That(grow, Is.GreaterThanOrEqualTo(previous), $"warmer is never sparser on a cold edge (T {t})");
				// A smoothstep has no jump: two readings 0.002 apart differ by at most 1.5 × 0.002 / falloff.
				if (previous >= 0f)
				{
					Assert.That(grow - previous, Is.LessThanOrEqualTo(1.5f * 0.002f / Falloff + 1e-4f), $"no step at T {t}");
				}
				previous = grow;
			}
		}

		[Test]
		public void EndsAtOrPastPlusMinusOne_AreOpen()
		{
			// "0.55 and warmer": the hottest place a world has is full, not halved by a soft edge at 1.
			Assert.That(TerrainScatter.ClimateBand(new Vector2(0.55f, 1f), Falloff, 1f), Is.EqualTo(1f));
			Assert.That(TerrainScatter.ClimateBand(new Vector2(0.55f, 1f), Falloff, 0.4f), Is.EqualTo(0f));
			// "0.6 and colder".
			Assert.That(TerrainScatter.ClimateBand(new Vector2(-1f, 0.6f), Falloff, -1f), Is.EqualTo(1f));
			Assert.That(TerrainScatter.ClimateBand(new Vector2(-1f, 0.6f), Falloff, 0.8f), Is.EqualTo(0f));
			// Infinities and values past the scale are open by the same test.
			Assert.That(TerrainScatter.ClimateBand(new Vector2(float.NegativeInfinity, float.PositiveInfinity), Falloff, 0.3f), Is.EqualTo(1f));
			Assert.That(TerrainScatter.ClimateBand(new Vector2(0f, float.PositiveInfinity), Falloff, 1f), Is.EqualTo(1f));
			Assert.That(TerrainScatter.ClimateBand(new Vector2(-5f, 0f), Falloff, -1f), Is.EqualTo(1f));
		}

		[Test]
		public void AHardBand_IncludesItsEnds_AndEitherOrderMeansTheSame()
		{
			var band = new Vector2(-0.3f, 0.2f);
			Assert.That(TerrainScatter.ClimateBand(band, 0f, -0.3f), Is.EqualTo(1f), "an end is inside");
			Assert.That(TerrainScatter.ClimateBand(band, 0f, 0.2f), Is.EqualTo(1f));
			Assert.That(TerrainScatter.ClimateBand(band, 0f, 0.2001f), Is.EqualTo(0f));
			Assert.That(TerrainScatter.ClimateBand(band, 0f, -0.3001f), Is.EqualTo(0f));
			for (float t = -1f; t <= 1f; t += 0.05f)
			{
				Assert.That(TerrainScatter.ClimateBand(new Vector2(0.2f, -0.3f), Falloff, t), Is.EqualTo(TerrainScatter.ClimateBand(band, Falloff, t)), $"swapped ends at T {t}");
			}
			Assert.That(TerrainScatter.ClimateBand(band, Falloff, float.NaN), Is.EqualTo(1f), "no reading passes");
		}

		[Test]
		public void TemperatureAndHumidity_Multiply()
		{
			var rule = new PrefabSpawnRule
			{
				useClimateBand = true,
				temperatureRange = new Vector2(0.3f, 1f),
				humidityRange = new Vector2(-1f, 0f),
			};
			Assert.That(TerrainScatter.ClimateBand(rule, 0.6f, -0.5f), Is.EqualTo(1f), "warm and dry");
			Assert.That(TerrainScatter.ClimateBand(rule, 0.6f, 0.5f), Is.EqualTo(0f), "warm but wet");
			Assert.That(TerrainScatter.ClimateBand(rule, 0f, -0.5f), Is.EqualTo(0f), "dry but cold");
			Assert.That(TerrainScatter.ClimateBand(rule, 0.3f, 0f), Is.EqualTo(0.25f).Within(1e-5f), "on both edges: half of a half");
		}

		// ── The authoring tool's fingerprints ─────────────────────────

		private static BiomeArtSpec.Scatter Palm() => new BiomeArtSpec.Scatter
		{
			Name = "Trees: Palm",
			Channel = PrefabSpawnChannel.TreeInstance,
			Density = 0.4f,
			Spacing = 6f,
			Slope = new Vector2(0f, 30f),
			ClusterMetres = 12f,
			ClusterSize = 5f,
		};

		private static BiomeArtSpec.Scatter Kelp() => new BiomeArtSpec.Scatter
		{
			Name = "Kelp",
			Channel = PrefabSpawnChannel.DetailLayer,
			Density = 3f,
			Depth = new Vector2(5f, 30f),
			Placement = DetailPlacement.Carpet,
		};

		[Test]
		public void WithoutABand_EveryFingerprintIsWhatItWasBeforeBandsExisted()
		{
			// Pinned from the code before climate bands were added.
			var plain = new PrefabSpawnRule { prefabs = new GameObject[0] };
			Assert.That(BiomeArtAuthoring.Fingerprint(plain), Is.EqualTo("1c8a98b57d35dd8c"), "a default rule");
			Assert.That(BiomeArtAuthoring.LegacyFingerprint(plain), Is.EqualTo("7f90fc27f85c1696"));

			PrefabSpawnRule palm = BiomeArtAuthoring.ToRule("Beach", Palm(), new GameObject[0]);
			Assert.That(palm.SpecFingerprint, Is.EqualTo("ecb19655fd8f2137"), "a grouped tree rule");
			Assert.That(BiomeArtAuthoring.LegacyFingerprint(palm), Is.EqualTo("2798cfb254f880c5"));

			PrefabSpawnRule kelp = BiomeArtAuthoring.ToRule("Ocean", Kelp(), new GameObject[0]);
			Assert.That(kelp.SpecFingerprint, Is.EqualTo("2139f1ba67a16e92"), "a depth-banded carpet");
			Assert.That(BiomeArtAuthoring.LegacyFingerprint(kelp), Is.EqualTo("7e14b6222288b58d"));

			// A spec rule without a band writes every band field at its default.
			var fresh = new PrefabSpawnRule();
			Assert.That(palm.useClimateBand, Is.False);
			Assert.That(palm.temperatureRange, Is.EqualTo(fresh.temperatureRange));
			Assert.That(palm.humidityRange, Is.EqualTo(fresh.humidityRange));
			Assert.That(palm.temperatureFalloff, Is.EqualTo(fresh.temperatureFalloff));
			Assert.That(palm.humidityFalloff, Is.EqualTo(fresh.humidityFalloff));

			// Ranges moved while the band is off are inert, so not a tuning.
			palm.temperatureRange = new Vector2(0.4f, 0.8f);
			palm.humidityFalloff = 0.5f;
			Assert.That(BiomeArtAuthoring.Fingerprint(palm), Is.EqualTo("ecb19655fd8f2137"), "band settings with the band off must not change the hash");
		}

		[Test]
		public void ABand_IsWritten_Hashed_AndATunedOneIsSomebodysWork()
		{
			BiomeArtSpec.Scatter spec = Palm();
			spec.Temperature = new Vector2(0.55f, 1f);
			PrefabSpawnRule banded = BiomeArtAuthoring.ToRule("Beach", spec, new GameObject[0]);
			Assert.That(banded.useClimateBand, Is.True);
			Assert.That(banded.temperatureRange, Is.EqualTo(new Vector2(0.55f, 1f)));
			Assert.That(banded.humidityRange, Is.EqualTo(new Vector2(-1f, 1f)), "no humidity band: open");
			Assert.That(banded.temperatureFalloff, Is.EqualTo(0.1f).Within(1e-6f));
			Assert.That(banded.SpecFingerprint, Is.Not.EqualTo("ecb19655fd8f2137"), "a band is a value the tool wrote");
			Assert.That(banded.SpecFingerprint, Is.EqualTo(BiomeArtAuthoring.Fingerprint(banded)));
			Assert.That(BiomeArtAuthoring.LegacyFingerprint(banded), Is.EqualTo("2798cfb254f880c5"),
				"the legacy hash never carries the band, so an untouched pre-fingerprint rule is still recognised against a banded spec");

			string marked = banded.SpecFingerprint;
			banded.temperatureRange = new Vector2(0.5f, 1f);
			Assert.That(BiomeArtAuthoring.Fingerprint(banded), Is.Not.EqualTo(marked), "moving a band is a tuning");
			banded.temperatureRange = new Vector2(0.55f, 1f);
			banded.humidityFalloff = 0.3f;
			Assert.That(BiomeArtAuthoring.Fingerprint(banded), Is.Not.EqualTo(marked), "so is softening one");
			banded.humidityFalloff = 0.1f;
			Assert.That(BiomeArtAuthoring.Fingerprint(banded), Is.EqualTo(marked), "and putting it back is not");
			banded.useClimateBand = false;
			Assert.That(BiomeArtAuthoring.Fingerprint(banded), Is.Not.EqualTo(marked), "switching it off is a tuning");

			// Bringing a rule up to date copies the band in place.
			PrefabSpawnRule old = BiomeArtAuthoring.ToRule("Beach", Palm(), new GameObject[0]);
			BiomeArtAuthoring.ApplySpec(old, "Beach", spec, new GameObject[0]);
			Assert.That(old.useClimateBand, Is.True);
			Assert.That(old.temperatureRange, Is.EqualTo(new Vector2(0.55f, 1f)));
			Assert.That(old.SpecFingerprint, Is.EqualTo(marked));

			// An open end that is infinite hashes the same every time.
			old.temperatureRange = new Vector2(float.NegativeInfinity, float.PositiveInfinity);
			Assert.That(BiomeArtAuthoring.Fingerprint(old), Is.EqualTo(BiomeArtAuthoring.Fingerprint(old)));
		}

		[Test]
		public void TheSpecsBandHelper_BandsTheRuleAndEveryEarlierVersion()
		{
			MethodInfo band = typeof(BiomeArtSpec).GetMethod("Band", BindingFlags.NonPublic | BindingFlags.Static);
			Assert.That(band, Is.Not.Null, "BiomeArtSpec.Band(Scatter, float, float, float, float)");

			BiomeArtSpec.Scatter rule = Palm();
			BiomeArtSpec.Scatter before = rule.Clone();
			rule.Earlier.Add(before);
			object returned = band.Invoke(null, new object[] { rule, -1f, 0.6f, -0.2f, 1f });
			Assert.That(returned, Is.SameAs(rule), "returns the rule, for chaining in a layer's list");
			Assert.That(rule.Temperature, Is.EqualTo((Vector2?)new Vector2(-1f, 0.6f)));
			Assert.That(rule.Humidity, Is.EqualTo((Vector2?)new Vector2(-0.2f, 1f)));
			Assert.That(before.Temperature, Is.EqualTo(rule.Temperature), "earlier versions are banded too, as Sea sets their depth");
			Assert.That(before.Humidity, Is.EqualTo(rule.Humidity));

			// Both ends open on an axis is no band on it; both axes open is no band at all.
			BiomeArtSpec.Scatter open = Palm();
			band.Invoke(null, new object[] { open, 0.2f, 5f, -1f, 1f });
			Assert.That(open.Temperature, Is.EqualTo((Vector2?)new Vector2(0.2f, 1f)), "an end past the scale is clamped to it (still open)");
			Assert.That(open.Humidity, Is.Null);
			BiomeArtSpec.Scatter none = Palm();
			band.Invoke(null, new object[] { none, -1f, 1f, -1f, 1f });
			Assert.That(none.Temperature, Is.Null);
			Assert.That(none.Humidity, Is.Null);
			Assert.That(BiomeArtAuthoring.ToRule("Beach", none, new GameObject[0]).SpecFingerprint, Is.EqualTo("ecb19655fd8f2137"), "written exactly as without the call");
		}
	}
}
