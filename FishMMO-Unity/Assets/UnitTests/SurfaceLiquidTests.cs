using NUnit.Framework;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// What stands in a world's low ground — sea, lava or nothing — follows from the world's physics:
	/// water wins wherever it is liquid, rock is molten above its solidus or where an Io is heated from
	/// within, and an ice moon, a dead moon and a Venus get neither.
	/// </summary>
	/// <remarks>
	/// The worlds are the solar system's own, at the numbers the climate model gives them (see
	/// ClimateModel.InternalHeat's remarks: Io 0.80 by tides, Europa 0.44, the Moon 0.16, Earth 1.00).
	/// Earth's heat is the reason the water rule must come first — it is as "volcanic" as Io in this
	/// model and has no lava seas.
	/// </remarks>
	[TestFixture]
	public class SurfaceLiquidTests
	{
		private static BiomeWorldConditions World(AtmosphereKind air, float temperature, float water, float heat)
		{
			return new BiomeWorldConditions
			{
				Atmosphere = air,
				MeanTemperature = temperature,
				Water = water,
				InternalHeat = heat,
			};
		}

		[Test]
		public void AnEarthIsWaterHoweverHotItsInterior()
		{
			LogAssert.AreEqual(SurfaceLiquid.Water, SurfaceLiquids.Decide(BiomeWorldConditions.Earthlike, 288.0));
		}

		[Test]
		public void AnIoKeepsLavaLakes()
		{
			// Airless, −140 °C, dry, tidally kneaded past the lake threshold.
			BiomeWorldConditions io = World(AtmosphereKind.None, -1f, 0f, 0.85f);
			LogAssert.AreEqual(SurfaceLiquid.Lava, SurfaceLiquids.Decide(io, 110.0));
			LogAssert.IsTrue(SurfaceLiquids.HasLavaLakes(io));
		}

		[Test]
		public void IoItselfIsAtTheThresholdAndKeepsItsLakes()
		{
			// ClimateModel computes Io at exactly LavaLakeHeat (0.80), so the threshold is inclusive:
			// a strict test left the solar system's best-known lava world without its lakes.
			BiomeWorldConditions io = World(AtmosphereKind.None, -1f, 0f, SurfaceLiquids.LavaLakeHeat);
			LogAssert.IsTrue(SurfaceLiquids.HasLavaLakes(io));
			LogAssert.IsFalse(SurfaceLiquids.HasLavaLakes(World(AtmosphereKind.None, -1f, 0f, SurfaceLiquids.LavaLakeHeat - 0.01f)));
		}

		[Test]
		public void ADeadMoonAndAnIceMoonHaveNoLiquid()
		{
			LogAssert.AreEqual(SurfaceLiquid.None, SurfaceLiquids.Decide(World(AtmosphereKind.None, -1f, 0f, 0.16f), 250.0),
				"the Moon is geologically dead");
			LogAssert.AreEqual(SurfaceLiquid.None, SurfaceLiquids.Decide(World(AtmosphereKind.None, -1f, 0.5f, 0.9f), 100.0),
				"a frozen shell over a hot interior is Europa: cryovolcanism, not lava");
		}

		[Test]
		public void AVenusHasNoLavaStandingOpen()
		{
			// 737 K under a thick atmosphere and an Earth-sized interior: resurfaced, long since set.
			LogAssert.AreEqual(SurfaceLiquid.None, SurfaceLiquids.Decide(World(AtmosphereKind.Thick, 1f, 0f, 0.93f), 737.0));
		}

		[Test]
		public void AWorldAboveTheSolidusIsAMagmaOcean()
		{
			// Starlight alone melts the ground, whatever the air and the interior.
			LogAssert.AreEqual(SurfaceLiquid.Lava, SurfaceLiquids.Decide(World(AtmosphereKind.Thick, 1f, 0f, 0.2f), 1600.0));
			LogAssert.AreEqual(SurfaceLiquid.None, SurfaceLiquids.Decide(World(AtmosphereKind.Thick, 1f, 0f, 0.2f), 1200.0),
				"below the solidus the crust holds");
		}

		[Test]
		public void AWorldAtOrAboveTheSolidus_NeverHasASea_HoweverMuchWaterItHas()
		{
			// The water rule comes first, so it must never hold on a magma-ocean world: an ocean world
			// moved inside the solidus would otherwise turn back into a sea.
			foreach (AtmosphereKind air in new[] { AtmosphereKind.Thin, AtmosphereKind.Standard, AtmosphereKind.Thick })
			{
				foreach (double kelvin in new[] { SurfaceLiquids.SolidusKelvin, 1600.0, 3000.0 })
				{
					var wet = new BiomeWorldConditions
					{
						Atmosphere = air,
						MeanTemperature = (float)ClimateModel.ToScaleUnclamped(kelvin),
						Water = 0.9f,
						InternalHeat = 0.5f,
					};
					LogAssert.IsFalse(wet.HasLiquidWater, $"{air} air at {kelvin} K: no liquid water");
					LogAssert.AreEqual(SurfaceLiquid.Lava, SurfaceLiquids.Decide(wet, kelvin), $"{air} air at {kelvin} K is a magma ocean");
				}
			}
		}

		[Test]
		public void TheSolidusTest_AndTheWaterTest_ReadOneTemperature()
		{
			// SurfaceLiquids' kelvin and the conditions' mean are the same number for a body.
			LogAssert.AreEqual(ClimateModel.MeanSurfaceKelvin(null, null), SurfaceLiquids.MeanSurfaceKelvin(null, null));
		}

		[Test]
		public void FumesNeedAirToHangIn()
		{
			LogAssert.AreEqual(0f, SurfaceLiquids.FumeDensity(AtmosphereKind.None), "Io's gas leaves ballistically; nothing lingers");
			float thin = SurfaceLiquids.FumeDensity(AtmosphereKind.Thin);
			LogAssert.IsTrue(thin > 0f && thin < 1f, $"thin air holds a faint haze, was {thin}");
			LogAssert.AreEqual(1f, SurfaceLiquids.FumeDensity(AtmosphereKind.Standard));
			LogAssert.AreEqual(1f, SurfaceLiquids.FumeDensity(AtmosphereKind.Thick));
		}

		[Test]
		public void NoBodyIsTheEarthlikeDefault()
		{
			LogAssert.AreEqual(SurfaceLiquid.Water, SurfaceLiquids.For(null, null, out float level));
			LogAssert.AreEqual(0f, level);
			double kelvin = SurfaceLiquids.MeanSurfaceKelvin(null, null);
			LogAssert.IsTrue(kelvin > 270.0 && kelvin < 300.0, $"an Earth-like world should be about 286 K, was {kelvin:0.0}");
		}
	}
}
