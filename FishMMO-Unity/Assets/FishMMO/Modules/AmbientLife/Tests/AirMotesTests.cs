using NUnit.Framework;
using FishMMO.Client;
using FishMMO.Shared.Biomes;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// What floats in the air above water (AirMotesField.Choose): dust always, pollen on a spring day over
	/// growing land, diamond dust in clear calm cold, fireflies on a warm humid summer night, wisps over a
	/// bog in the dark — and none of the rest where they would not be.
	/// </summary>
	/// <remarks>
	/// Pinned as clear-cut cases and comparisons a naturalist would name, not to whatever the curves
	/// happen to produce.
	/// </remarks>
	[TestFixture]
	public class AirMotesTests
	{
		private const float Spring = 0.3f, Midsummer = 0.5f, Midwinter = 0f, Autumn = 0.8f;
		private const float Noon = 50f, Night = -25f;

		private static readonly TerrainProcess WetlandGround = new TerrainProcess { VegetationCohesion = 0.8f, LakeRetention = 0.95f };
		private static readonly TerrainProcess PeriglacialGround = new TerrainProcess { VegetationCohesion = 0.35f, LakeRetention = 0.9f };
		private static readonly TerrainProcess TemperateGround = new TerrainProcess { VegetationCohesion = 0.5f, LakeRetention = 0.6f };

		/// <summary>A temperate meadow: mild, fairly humid, a light breeze, clear sky.</summary>
		private static AirMotesClimate Meadow(float celsius, float sun, float phase, float humidity = 0.6f, float wind = 2f) => new AirMotesClimate
		{
			HasAir = true,
			Celsius = celsius,
			FreezingCelsius = 0.01f,
			RelativeHumidity = humidity,
			WindMetres = wind,
			SunAltitude = sun,
			CloudCover = 0.2f,
			YearPhase = phase,
			SeasonStrength = 1f,
			Vegetation = 0.5f,
			Wetland = AirMotesField.WetlandOf(TemperateGround),
		};

		[Test]
		public void DustIsInAllAir_AndTheRainWashesItOut()
		{
			AirMotesClimate dry = Meadow(20f, Noon, Midsummer);
			Assert.That(AirMotesField.Choose(dry).Dust, Is.GreaterThan(0.5f));
			AirMotesClimate night = Meadow(5f, Night, Midwinter);
			Assert.That(AirMotesField.Choose(night).Dust, Is.GreaterThan(0.5f), "the light decides whether it is seen, not the hour");
			AirMotesClimate raining = dry;
			raining.Precipitation = 0.4f;
			Assert.That(AirMotesField.Choose(raining).Dust, Is.EqualTo(0f).Within(1e-4f));
		}

		[Test]
		public void AnAirlessWorld_HoldsNothingUp()
		{
			AirMotesClimate vacuum = Meadow(20f, Noon, Midsummer);
			vacuum.HasAir = false;
			AirMotesMix mix = AirMotesField.Choose(vacuum);
			Assert.That(mix.Dust + mix.Pollen + mix.Ice + mix.Fireflies + mix.Wisps, Is.EqualTo(0f));
		}

		[Test]
		public void Pollen_FloatsOnASpringDay_NotInWinterNorAtNight()
		{
			float spring = AirMotesField.Choose(Meadow(16f, Noon, Spring)).Pollen;
			Assert.That(spring, Is.GreaterThan(0.5f));
			Assert.That(AirMotesField.Choose(Meadow(16f, Night, Spring)).Pollen, Is.EqualTo(0f), "anthers shed by day");
			Assert.That(AirMotesField.Choose(Meadow(16f, Noon, Midwinter)).Pollen, Is.EqualTo(0f), "nothing flowers in midwinter");
			Assert.That(AirMotesField.Choose(Meadow(16f, Noon, Autumn)).Pollen, Is.EqualTo(0f), "over by autumn");
			Assert.That(AirMotesField.Choose(Meadow(0f, Noon, Spring)).Pollen, Is.EqualTo(0f), "too cold to shed");
		}

		[Test]
		public void Pollen_NeedsPlants_AndDryAir_AndLessThanAGale()
		{
			AirMotesClimate desert = Meadow(25f, Noon, Spring);
			desert.Vegetation = 0.05f;
			Assert.That(AirMotesField.Choose(desert).Pollen, Is.EqualTo(0f));
			float breeze = AirMotesField.Choose(Meadow(18f, Noon, Spring)).Pollen;
			Assert.That(AirMotesField.Choose(Meadow(18f, Noon, Spring, humidity: 0.98f)).Pollen, Is.LessThan(0.05f), "anthers close in saturated air");
			Assert.That(AirMotesField.Choose(Meadow(18f, Noon, Spring, wind: 16f)).Pollen, Is.EqualTo(0f), "a gale scatters it too thin to see");
			Assert.That(AirMotesField.Choose(Meadow(18f, Noon, Spring, wind: 0f)).Pollen, Is.LessThan(breeze), "a breeze shakes more loose than a dead calm");
		}

		[Test]
		public void DiamondDust_InClearCalmColdSaturatedAir()
		{
			AirMotesClimate arctic = Meadow(-25f, 5f, Midwinter, humidity: 0.85f, wind: 0.5f);
			arctic.CloudCover = 0f;
			Assert.That(AirMotesField.Choose(arctic).Ice, Is.GreaterThan(0.8f));

			AirMotesClimate mild = arctic;
			mild.Celsius = -4f;
			Assert.That(AirMotesField.Choose(mild).Ice, Is.EqualTo(0f), "a few degrees of frost makes none");
			AirMotesClimate cloudy = arctic;
			cloudy.CloudCover = 1f;
			Assert.That(AirMotesField.Choose(cloudy).Ice, Is.EqualTo(0f), "it forms under a clear sky");
			AirMotesClimate windy = arctic;
			windy.WindMetres = 10f;
			Assert.That(AirMotesField.Choose(windy).Ice, Is.EqualTo(0f), "a wind mixes the cold air away");
			AirMotesClimate dry = arctic;
			dry.RelativeHumidity = 0.4f;
			Assert.That(AirMotesField.Choose(dry).Ice, Is.EqualTo(0f), "air short of ice saturation grows no crystals");
		}

		[Test]
		public void DiamondDust_ReadsTheWorldsOwnFreezingPoint()
		{
			// Methane condenses at 90.7 K: −183 °C. Air at −150 °C there is far above its freezing point.
			AirMotesClimate titan = Meadow(-150f, 5f, Midwinter, humidity: 0.95f, wind: 0.5f);
			titan.CloudCover = 0f;
			titan.FreezingCelsius = -182.5f;
			Assert.That(AirMotesField.Choose(titan).Ice, Is.EqualTo(0f));
			titan.Celsius = -200f;
			Assert.That(AirMotesField.Choose(titan).Ice, Is.GreaterThan(0.5f));
		}

		[Test]
		public void IceSaturation_IsBelowWaterSaturationInTheCold()
		{
			Assert.That(AirMotesField.IceSaturationHumidity(0f), Is.EqualTo(1f).Within(1e-4f));
			// e_i/e_w: 0.82 at −20 °C, 0.71 at −30 °C (Magnus over ice and water).
			Assert.That(AirMotesField.IceSaturationHumidity(-20f), Is.EqualTo(0.82f).Within(0.02f));
			Assert.That(AirMotesField.IceSaturationHumidity(-30f), Is.EqualTo(0.74f).Within(0.04f));
		}

		[Test]
		public void Fireflies_OnAWarmHumidSummerNight()
		{
			float summerNight = AirMotesField.Choose(Meadow(21f, Night, Midsummer, humidity: 0.85f, wind: 1f)).Fireflies;
			Assert.That(summerNight, Is.GreaterThan(0.7f));
			Assert.That(AirMotesField.Choose(Meadow(21f, Noon, Midsummer, humidity: 0.85f, wind: 1f)).Fireflies, Is.EqualTo(0f), "not by day");
			Assert.That(AirMotesField.Choose(Meadow(10f, Night, Midsummer, humidity: 0.85f, wind: 1f)).Fireflies, Is.EqualTo(0f), "a cool night keeps them down");
			Assert.That(AirMotesField.Choose(Meadow(21f, Night, Midsummer, humidity: 0.3f, wind: 1f)).Fireflies, Is.EqualTo(0f), "dry air");
			Assert.That(AirMotesField.Choose(Meadow(21f, Night, Spring, humidity: 0.85f, wind: 1f)).Fireflies, Is.EqualTo(0f), "too early in the year");
			Assert.That(AirMotesField.Choose(Meadow(21f, Night, Midsummer, humidity: 0.85f, wind: 9f)).Fireflies, Is.EqualTo(0f), "they do not fly in a wind");
			AirMotesClimate rain = Meadow(21f, Night, Midsummer, humidity: 0.95f, wind: 1f);
			rain.Precipitation = 0.2f;
			Assert.That(AirMotesField.Choose(rain).Fireflies, Is.EqualTo(0f), "nor in the rain");
		}

		[Test]
		public void Fireflies_BeginAtSunset()
		{
			float setting = AirMotesField.Choose(Meadow(21f, -2f, Midsummer, humidity: 0.85f, wind: 1f)).Fireflies;
			float dusk = AirMotesField.Choose(Meadow(21f, -8f, Midsummer, humidity: 0.85f, wind: 1f)).Fireflies;
			Assert.That(setting, Is.GreaterThan(0f).And.LessThan(dusk));
		}

		[Test]
		public void TheTropics_FlowerAndGlowAllYear()
		{
			AirMotesClimate equator = Meadow(26f, Night, Midwinter, humidity: 0.85f, wind: 1f);
			equator.SeasonStrength = 0f;
			Assert.That(AirMotesField.Choose(equator).Fireflies, Is.GreaterThan(0.5f));
			Assert.That(AirMotesField.PollenSeason(Midwinter, 0f), Is.GreaterThan(0.5f));
		}

		[Test]
		public void Wisps_OverWetlandInTheDark_Only()
		{
			AirMotesClimate bog = Meadow(12f, Night, Autumn, humidity: 0.9f, wind: 1f);
			bog.Wetland = AirMotesField.WetlandOf(WetlandGround);
			bog.Vegetation = WetlandGround.VegetationCohesion;
			Assert.That(AirMotesField.Choose(bog).Wisps, Is.GreaterThan(0.8f));

			Assert.That(AirMotesField.Choose(Meadow(12f, Night, Autumn, humidity: 0.9f, wind: 1f)).Wisps, Is.EqualTo(0f), "not over a meadow");
			AirMotesClimate dusk = bog;
			dusk.SunAltitude = -3f;
			Assert.That(AirMotesField.Choose(dusk).Wisps, Is.EqualTo(0f), "only once it is properly dark");
			AirMotesClimate frozen = bog;
			frozen.Celsius = -5f;
			Assert.That(AirMotesField.Choose(frozen).Wisps, Is.EqualTo(0f), "a frozen bog makes no gas");
			AirMotesClimate windy = bog;
			windy.WindMetres = 8f;
			Assert.That(AirMotesField.Choose(windy).Wisps, Is.EqualTo(0f));
		}

		[Test]
		public void Wetland_IsStandingWaterThroughLivingCover()
		{
			Assert.That(AirMotesField.WetlandOf(WetlandGround), Is.EqualTo(1f).Within(1e-4f), "peat bog, swamp, wetlands, mangrove, estuary");
			Assert.That(AirMotesField.WetlandOf(PeriglacialGround), Is.EqualTo(0f), "tundra ponds over bare churned soil");
			Assert.That(AirMotesField.WetlandOf(TemperateGround), Is.EqualTo(0f));
		}

		[Test]
		public void UnderCover_TheOpenAirKindsThin_TheDustDoesNot()
		{
			AirMotesClimate open = Meadow(18f, Noon, Spring);
			AirMotesClimate cave = open;
			cave.Shelter = 1f;
			AirMotesMix outside = AirMotesField.Choose(open), inside = AirMotesField.Choose(cave);
			Assert.That(inside.Dust, Is.GreaterThanOrEqualTo(outside.Dust), "no wind clears a room");
			Assert.That(inside.Pollen, Is.LessThan(outside.Pollen));
		}

		[Test]
		public void ShownShare_NeverHoldsMoreThanTheAirDoes()
		{
			AirMotesField.Traits wisps = AirMotesField.TraitsOf(AirMoteKind.Wisp);
			float share = AirMotesField.ShownShare(1f, wisps, AirMotesField.ParticlesOf(AirMoteKind.Wisp, 2000));
			float shownWisps = share * AirMotesField.ParticlesOf(AirMoteKind.Wisp, 2000);
			Assert.That(shownWisps, Is.LessThan(8f), "a few in a whole marsh");
			Assert.That(shownWisps, Is.GreaterThan(1f));
			Assert.That(AirMotesField.ShownShare(0.5f, AirMotesField.TraitsOf(AirMoteKind.Dust), 1), Is.EqualTo(0.5f).Within(1e-5f), "a small budget shows its amount");
		}

		[Test]
		public void TheBudget_IsAFewThousandQuadsAtMost()
		{
			int total = 0;
			int budget = AirMotesField.Budget(20000);
			foreach (AirMoteKind kind in AirMotesField.Kinds)
			{
				total += AirMotesField.ParticlesOf(kind, budget);
			}
			Assert.That(budget, Is.LessThanOrEqualTo(AirMotesField.MaxBudget));
			Assert.That(total, Is.LessThanOrEqualTo(5000));
			Assert.That(AirMotesField.Budget(3000), Is.EqualTo(750));
		}

		[Test]
		public void AGlint_IsTensOfTimesAWhiteCard()
		{
			// The sun's disc at 5 % over a cone of 1.5°: bright enough to sparkle, never the sun itself.
			Assert.That(AirMotesField.GlintPeak(), Is.InRange(30f, 200f));
		}
	}
}
