using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A world's colours come from what it is made of and how warm it is (2026-10-14: "The generator needs to provide
	/// accurate color based on the atmospheric makeup, temperature and other factors"): BodyColours, checked against the
	/// solar system's own worlds.
	/// </summary>
	public class BodyColourTests
	{
		private static BodyColours.Look Giant(float kelvin, uint seed = 7u)
			=> BodyColours.For(new BodyColours.Physics { Kind = WorldBodyKind.GasGiant, Atmosphere = AtmosphereKind.Thick, Kelvin = kelvin, Seed = seed });

		private static BodyColours.Look Solid(AtmosphereKind air, float water, float kelvin, Condensate condensate = Condensate.Water,
			float tidal = 0f, WorldBodyKind kind = WorldBodyKind.Planet)
			=> BodyColours.For(new BodyColours.Physics
			{
				Kind = kind, Atmosphere = air, Water = water, Kelvin = kelvin, Condensate = condensate, TidalHeat = tidal, Seed = 11u,
			});

		[Test]
		public void AColdGiantIsMethaneBlue()
		{
			Color neptune = Giant(50f).Globe, uranus = Giant(65f).Globe;
			Assert.Greater(neptune.b, neptune.r + 0.25f, "Neptune: deep blue");
			Assert.Greater(uranus.g, uranus.r + 0.1f, "Uranus: cyan");
			Assert.Greater(uranus.b, uranus.r + 0.1f, "Uranus: cyan");
		}

		[Test]
		public void AJupiterIsOchreWithStrongBelts()
		{
			BodyColours.Look jupiter = Giant(125f), uranus = Giant(65f);
			Assert.Greater(jupiter.Globe.r, jupiter.Globe.b + 0.15f, "warm, not blue");
			Assert.Greater(jupiter.Zone.grayscale, jupiter.Belt.grayscale + 0.2f, "pale zones over dark belts");
			Assert.Greater(jupiter.Banding, uranus.Banding * 4f, "Jupiter is banded, Uranus nearly featureless");
			Assert.Greater(jupiter.Storm.r, jupiter.Storm.b + 0.3f, "a red storm");
		}

		[Test]
		public void AWarmGiantHasWhiteWaterClouds()
		{
			Color warm = Giant(200f).Globe;
			Assert.Greater(Mathf.Min(warm.r, Mathf.Min(warm.g, warm.b)), 0.8f);
		}

		[Test]
		public void HotterGiantsAreCloudlessBlueThenDark()
		{
			Color cloudless = Giant(500f).Globe, alkali = Giant(1100f).Globe;
			Assert.Greater(cloudless.b, cloudless.r + 0.25f, "Class III: azure");
			Assert.Less(alkali.maxColorComponent, 0.4f, "Class IV: dark");
		}

		[Test]
		public void PeliarethsTemperatureMakesItAWaterCloudGiant()
		{
			// About 158 K at its cloud tops: just warm enough for water cloud, not the methane cyan the dice gave it.
			Color peliareth = Giant(158f).Globe;
			Assert.Greater(peliareth.r, 0.75f);
			Assert.Less(Mathf.Abs(peliareth.r - peliareth.b), 0.2f, "near white, neither cyan nor ochre");
		}

		[Test]
		public void MarsIsRust()
		{
			Color mars = Solid(AtmosphereKind.Thin, 0.02f, 210f).Globe;
			Assert.Greater(mars.r, mars.g + 0.15f);
			Assert.Greater(mars.g, mars.b);
			BodyColours.Look look = Solid(AtmosphereKind.Thin, 0.02f, 210f);
			Assert.Greater(look.Haze.r, look.Haze.b, "and its sky's dust is butterscotch");
		}

		[Test]
		public void VenusIsPaleYellowAndHidesItsGround()
		{
			BodyColours.Look venus = Solid(AtmosphereKind.Thick, 0f, 730f, Condensate.SulphuricAcid);
			Assert.Greater(venus.Globe.g, venus.Globe.b + 0.1f);
			Assert.Greater(venus.Globe.r, 0.8f);
			Assert.Greater(venus.Veil, 0.8f);
		}

		[Test]
		public void TitanIsOrange()
		{
			BodyColours.Look titan = Solid(AtmosphereKind.Thick, 0f, 94f, Condensate.Methane, kind: WorldBodyKind.Moon);
			Assert.Greater(titan.Globe.r, titan.Globe.g + 0.15f);
			Assert.Greater(titan.Globe.g, titan.Globe.b + 0.15f);
			Assert.Greater(titan.Haze.r, titan.Haze.b + 0.3f, "an orange sky");
		}

		[Test]
		public void AnOceanWorldIsBlueAndWhite()
		{
			Color earth = Solid(AtmosphereKind.Standard, 0.7f, 288f).Globe;
			Assert.Greater(earth.b, earth.r + 0.1f);
			Color snowball = Solid(AtmosphereKind.Standard, 0.7f, 220f).Globe;
			Assert.Greater(Mathf.Min(snowball.r, snowball.g), 0.8f, "frozen over, it is white");
		}

		[Test]
		public void AirlessBodiesAreIceSulphurOrGreyRock()
		{
			Color europa = Solid(AtmosphereKind.None, 0f, 100f, kind: WorldBodyKind.Moon).Globe;
			Color io = Solid(AtmosphereKind.None, 0f, 130f, tidal: 1f, kind: WorldBodyKind.Moon).Globe;
			Color moon = Solid(AtmosphereKind.None, 0f, 250f, kind: WorldBodyKind.Moon).Globe;
			Assert.Greater(europa.grayscale, 0.7f, "ice is bright");
			Assert.Greater(io.g, io.b + 0.25f, "sulphur is yellow");
			Assert.Less(moon.grayscale, europa.grayscale, "rock is darker than ice");
			Assert.Less(Mathf.Abs(moon.r - moon.b), 0.12f, "and grey");
		}

		[Test]
		public void AnIoIsSulphurEvenOverMoltenRock()
		{
			Color io = BodyColours.For(new BodyColours.Physics
			{
				Kind = WorldBodyKind.Moon, Atmosphere = AtmosphereKind.None, Kelvin = 244f, TidalHeat = 0.9f, Molten = true, Seed = 2u,
			}).Globe;
			Assert.Greater(io.g, io.b + 0.25f);
			Assert.Greater(io.grayscale, 0.5f, "not black lava");
		}

		[Test]
		public void AColdThinAirIsNotRust()
		{
			Color cold = Solid(AtmosphereKind.Thin, 0f, 150f, Condensate.Ammonia, kind: WorldBodyKind.Moon).Globe;
			Color mars = Solid(AtmosphereKind.Thin, 0.02f, 210f).Globe;
			Assert.Greater(cold.grayscale, mars.grayscale + 0.1f, "ice and tholin, paler than rust");
		}

		[Test]
		public void PlutoIsTholinTan()
		{
			Color pluto = Solid(AtmosphereKind.Thin, 0f, 40f, Condensate.Nitrogen, kind: WorldBodyKind.DwarfPlanet).Globe;
			Assert.Greater(pluto.r, pluto.b + 0.15f);
		}

		[Test]
		public void RingsAreIceWhenColdAndDustWhenWarm()
		{
			Assert.Greater(Giant(100f).Rings.grayscale, Giant(600f).Rings.grayscale + 0.3f);
		}

		[Test]
		public void TheSameWorldIsAlwaysTheSameColourAndItsSiblingsDifferALittle()
		{
			Assert.AreEqual(Giant(125f, 3u).Globe, Giant(125f, 3u).Globe);
			Color a = Giant(125f, 3u).Globe, b = Giant(125f, 4u).Globe;
			float apart = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
			Assert.Greater(apart, 0.001f, "not one swatch for every Jupiter");
			Assert.Less(apart, 0.2f, "but still a Jupiter");
		}
	}
}
