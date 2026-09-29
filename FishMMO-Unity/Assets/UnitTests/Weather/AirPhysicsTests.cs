using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The physics the weather and the clouds are worked out from: how air cools as it rises, where
	/// it condenses and freezes, how far the weather goes up, what storms the air allows, how the
	/// world's spin sorts its winds.
	/// </summary>
	/// <remarks>
	/// Pinned to physical facts, not to whatever the code happened to produce: the rule of 125 m of
	/// cloud base per degree of dew-point spread, our own tropopause, our own thirty-degree wind
	/// cells, sand that starts to move at about six metres a second. Where a number is asserted it is
	/// a range a meteorologist would recognise, and the shape of the thing — lower gravity lifts the
	/// base, a high puts a lid on the sky — is asserted as a comparison.
	/// </remarks>
	[TestFixture]
	public class AirPhysicsTests
	{
		private static PlanetAir Earth => PlanetAir.Earthlike;

		// ── Lapse and condensation ────────────────────────────────────

		[Test]
		public void DryAirCoolsAtItsWeightOverItsHeatCapacity()
		{
			Assert.That(AirPhysics.DryLapse(9.80665f, 1004f) * 1000f, Is.EqualTo(9.77f).Within(0.02f), "our own dry adiabat, K/km");
			Assert.That(AirPhysics.DryLapse(5.515f, 1004f) * 1000f, Is.EqualTo(5.49f).Within(0.02f), "the home world's, at 0.56 g");
		}

		[Test]
		public void TheCloudBaseIsAHundredAndTwentyFiveMetresADegreeOfSpreadOnOurWorld()
		{
			AirColumn column = AirColumn.Of(Earth, 288f, 0.55f, 0f, 0.25f);
			float spread = column.SurfaceKelvin - column.DewPointKelvin;
			Assume.That(spread, Is.GreaterThan(2f));
			float perDegree = column.Base / spread;
			LogAssert.IsTrue(perDegree > 110f && perDegree < 135f, $"{perDegree:0} m of base per kelvin of spread");
		}

		[Test]
		public void DamperAirCondensesLower()
		{
			AirColumn dry = AirColumn.Of(Earth, 288f, 0.3f, 0f, 0.25f);
			AirColumn damp = AirColumn.Of(Earth, 288f, 0.8f, 0f, 0.25f);
			LogAssert.IsTrue(damp.Base < dry.Base, $"damp {damp.Base:0} m under dry {dry.Base:0} m");
			AirColumn saturated = AirColumn.Of(Earth, 288f, 1f, 0f, 0.25f);
			LogAssert.IsTrue(saturated.Base < 5f, $"saturated air condenses at the ground: {saturated.Base:0} m");
		}

		[Test]
		public void WeakerGravityLiftsTheBaseAndStretchesTheSky()
		{
			PlanetAir light = Earth;
			light.Gravity = Earth.Gravity * 0.5f;
			AirColumn here = AirColumn.Of(Earth, 288f, 0.5f, 0f, 0.25f);
			AirColumn there = AirColumn.Of(light, 288f, 0.5f, 0f, 0.25f);
			Assert.That(there.Base / here.Base, Is.EqualTo(2f).Within(0.15f), "the base goes as one over the pull");
			LogAssert.IsTrue(there.Tropopause > here.Tropopause * 1.5f, $"and the weather stands taller: {there.Tropopause:0} against {here.Tropopause:0} m");
		}

		[Test]
		public void SaturatedAirCoolsMoreSlowlyThanDryAir()
		{
			float moist = AirPhysics.MoistLapse(Earth.Gravity, 288f, 101325f, 1004f, 287.05f, Condensate.Water);
			float dry = AirPhysics.DryLapse(Earth.Gravity, 1004f);
			LogAssert.IsTrue(moist < dry, "condensing gives up heat");
			Assert.That(moist * 1000f, Is.InRange(3.5f, 5.5f), "about 4.5 K/km for warm air at the ground");
			float cold = AirPhysics.MoistLapse(Earth.Gravity, 230f, 30000f, 1004f, 287.05f, Condensate.Water);
			LogAssert.IsTrue(cold > moist && cold <= dry, "very cold air has almost nothing to condense, and cools nearly as fast as dry");
		}

		[Test]
		public void OurWorldsWeatherStopsAtAboutEleven()
		{
			AirColumn column = AirColumn.Of(Earth, 288f, 0.5f, 0f, 0.25f);
			Assert.That(column.Tropopause, Is.InRange(9000f, 14000f), "the tropopause");
			Assert.That(column.Freezing, Is.InRange(1800f, 3500f), "the freezing level at 15 °C");
			LogAssert.IsTrue(column.IceLevel > column.Freezing && column.IceLevel < column.Tropopause, "cirrus between the freezing level and the tropopause");
		}

		// ── Stability ─────────────────────────────────────────────────

		[Test]
		public void SettledAirMakesADeckAndUnsettledAirBuildsTowers()
		{
			AirColumn settled = AirColumn.Of(Earth, 290f, 0.7f, 0f, 0.05f);
			LogAssert.IsTrue(float.IsInfinity(settled.FreeConvection), "stable air never rises on its own");
			LogAssert.AreEqual(settled.Top, settled.TowerCeiling, "and grows no towers");
			LogAssert.IsTrue(settled.Top - settled.Base < 1600f, $"a deck: {settled.Top - settled.Base:0} m thick");

			AirColumn unsettled = AirColumn.Of(Earth, 300f, 0.8f, -0.5f, 0.7f);
			LogAssert.IsTrue(unsettled.FreeConvection < unsettled.Tropopause, "a lifted parcel rises on its own");
			LogAssert.IsTrue(unsettled.TowerCeiling >= unsettled.Tropopause, "and a tower reaches the top of the weather");
			LogAssert.IsTrue(unsettled.Cape > 100f, $"with energy to spare: {unsettled.Cape:0} J/kg");
			LogAssert.IsTrue(unsettled.Vigour > settled.Vigour);
		}

		[Test]
		public void AHighPutsALidOnTheSky()
		{
			AirColumn high = AirColumn.Of(Earth, 290f, 0.5f, 0.8f, 0.3f);
			AirColumn low = AirColumn.Of(Earth, 290f, 0.5f, -0.8f, 0.3f);
			LogAssert.IsTrue(high.Cap < high.Tropopause * 0.5f, $"a lid at {high.Cap:0} m");
			LogAssert.AreEqual(low.Tropopause, low.Cap, "a low has none");
			AirColumn dryHigh = AirColumn.Of(Earth, 290f, 0.05f, 1f, 0.3f);
			LogAssert.AreEqual(0f, dryHigh.LowCloudAllowed, "air too dry to condense before the lid has no low cloud");
		}

		// ── What condenses, and what it takes out of the view ─────────

		[Test]
		public void WhatCondensesFollowsHowWarmTheWorldIs()
		{
			LogAssert.AreEqual(Condensate.Water, AirPhysics.CondensateFor(288.0));
			LogAssert.AreEqual(Condensate.Water, AirPhysics.CondensateFor(210.0), "Mars has water-ice cloud");
			LogAssert.AreEqual(Condensate.Ammonia, AirPhysics.CondensateFor(160.0));
			LogAssert.AreEqual(Condensate.Methane, AirPhysics.CondensateFor(94.0), "Titan");
			LogAssert.AreEqual(Condensate.Nitrogen, AirPhysics.CondensateFor(50.0));
			LogAssert.AreEqual(Condensate.SulphuricAcid, AirPhysics.CondensateFor(700.0), "Venus");
		}

		[Test]
		public void ADenseFogIsTensOfMetresAndAMistIsKilometres()
		{
			float dense = 3.912f / AirPhysics.FogExtinction(1f);
			float thick = 3.912f / AirPhysics.FogExtinction(0.5f);
			float mist = 3.912f / AirPhysics.FogExtinction(0.16f);
			// The densest fog there is: a few tens of metres at most, the world gone past a stone's throw.
			Assert.That(dense, Is.InRange(10f, 30f), "visibility in the densest fog");
			Assert.That(thick, Is.InRange(100f, 250f), "visibility in a thick fog");
			float denseClass = 3.912f / AirPhysics.FogExtinction(0.75f);
			Assert.That(denseClass, Is.InRange(25f, 60f), "visibility in a dense fog (under 50 m)");
			// The humid noon's background: a mist the sky shows through, not a white-out.
			Assert.That(mist, Is.InRange(2000f, 8000f), "visibility in a mist");
			LogAssert.AreEqual(0f, AirPhysics.FogExtinction(0f));
		}

		[Test]
		public void HazeFollowsTheAirAndItsWater()
		{
			float ordinary = AirPhysics.HazeDistance(1f, 0.6f);
			Assert.That(ordinary, Is.InRange(18000f, 40000f), "an ordinary day's far sky, two e-foldings of it");
			LogAssert.IsTrue(AirPhysics.HazeDistance(1f, 0.9f) < ordinary, "humid air is milky");
			LogAssert.IsTrue(AirPhysics.HazeDistance(0.15f, 0.6f) > ordinary, "thin air is clear");
		}

		[Test]
		public void AnAdiabaticCloudIsGreyAtItsBaseAndDenseAtItsCrown()
		{
			AirColumn column = AirColumn.Of(Earth, 290f, 0.75f, 0f, 0.4f);
			float c = column.ExtinctionCoefficient(Earth);
			float nearBase = c * Mathf.Pow(50f, 2f / 3f);
			float halfway = c * Mathf.Pow(500f, 2f / 3f);
			Assert.That(halfway, Is.InRange(0.02f, 0.15f), "a cumulus half a kilometre up, 1/m");
			LogAssert.IsTrue(nearBase < halfway * 0.4f, "thin at the base");
		}

		// ── Wind belts ────────────────────────────────────────────────

		[Test]
		public void OurWorldsCellsAreThirtyDegrees()
		{
			Assert.That(WindBelts.For(Earth).CellDegrees, Is.EqualTo(30f).Within(1f));
			PlanetAir fast = Earth;
			fast.RotationRate *= 4f;
			LogAssert.IsTrue(WindBelts.For(fast).CellDegrees < 12f, "a world turning four times as fast packs its belts tight");
			PlanetAir slow = Earth;
			slow.RotationRate *= 0.05f;
			LogAssert.AreEqual(90f, WindBelts.For(slow).CellDegrees, "one that barely turns has one cell to the pole");
		}

		[Test]
		public void TheTradesBlowFromTheEastTowardTheEquatorAndTheWesterliesTowardThePole()
		{
			WindBelts belts = WindBelts.Earthlike;
			Vector2 trades = WeatherDriver.PrevailingWind(15f, belts);
			LogAssert.IsTrue(trades.x < 0f, "the trades blow toward the west");
			LogAssert.IsTrue(trades.y < 0f, "and toward the equator");
			Vector2 westerlies = WeatherDriver.PrevailingWind(45f, belts);
			LogAssert.IsTrue(westerlies.x > 0f && westerlies.y > 0f, "the westerlies blow east and poleward");
			Vector2 southern = WeatherDriver.PrevailingWind(-15f, belts);
			LogAssert.IsTrue(southern.x < 0f && southern.y > 0f, "the southern trades blow west and north, toward the equator");
			Vector2 backwards = WeatherDriver.PrevailingWind(15f, new WindBelts(30f, -1f));
			LogAssert.IsTrue(backwards.x > 0f, "a world that turns backwards turns its trades round");
		}

		// ── The lattices the sky and the server share ─────────────────

		[Test]
		public void TowersDoNotStandOnAGrid()
		{
			const uint seed = 2234047352u;
			float tile = WeatherDriver.TowerMetres;
			int peaks = 0, onGrid = 0;
			const float step = 0.05f;
			for (float y = step; y < 16f - step; y += step)
			{
				for (float x = step; x < 16f - step; x += step)
				{
					float here = WeatherDriver.Tower(seed, x * tile, y * tile);
					if (here < 0.95f)
					{
						continue;
					}
					// A flat core counts once: only its first sample in a row.
					if (WeatherDriver.Tower(seed, (x - step) * tile, y * tile) >= 0.95f)
					{
						continue;
					}
					peaks++;
					if (Mathf.Abs(x - Mathf.Round(x)) < 0.06f && Mathf.Abs(y - Mathf.Round(y)) < 0.06f)
					{
						onGrid++;
					}
				}
			}
			LogAssert.IsTrue(peaks > 20, $"a sky has towers in it: {peaks}");
			LogAssert.IsTrue(onGrid <= peaks / 10, $"and they are not stood on the lattice's points: {onGrid} of {peaks}");
		}

		[Test]
		public void TheFormationsStayWithinTheirCalibratedSpread()
		{
			const uint seed = 2234047352u;
			double sum = 0.0, squares = 0.0;
			int n = 0;
			for (int y = 0; y < 60; y++)
			{
				for (int x = 0; x < 60; x++)
				{
					float m = WeatherDriver.Mesoscale(seed, x * 3917.0, y * 4133.0, 0.25f);
					sum += m;
					squares += m * m;
					n++;
				}
			}
			double mean = sum / n;
			double spread = System.Math.Sqrt(squares / n - mean * mean);
			Assert.That(mean, Is.InRange(-0.15, 0.15), "banks and lanes balance out");
			Assert.That(spread, Is.InRange(0.25, 0.6), "and swing the cover as far as the contrast was calibrated for");
		}

		[Test]
		public void HailIsTheUpdraughtsAndMeltsOnTheWayDown()
		{
			LogAssert.AreEqual(0f, WeatherPhysics.HailShare(default, AirColumn.Of(Earth, 288f, 0.5f, 0f, 0.25f), Earth), "shallow convection grows no hail");

			// A strong ordinary storm grows centimetre stones, and they melt to rain on the way down.
			var storm = new WeatherDriver.Synoptic { Humidity = 1f, Pressure = -0.85f, Instability = 0.9f };
			AirColumn ordinary = AirColumn.Of(Earth, 298f, storm.Humidity, storm.Pressure, storm.Instability);
			LogAssert.IsTrue(ordinary.Deep, "the storm is deep convection");
			float rains = WeatherPhysics.HailShare(storm, ordinary, Earth);
			LogAssert.IsTrue(rains < 0.1f, $"an ordinary storm lands rain: {rains:0.00} hail");

			// A supercell's spinning core holds stones up long enough to land as hail.
			var supercell = new WeatherDriver.Synoptic { Humidity = 1f, Pressure = -1f, Instability = 1f, Mesocyclone = 1f };
			float hails = WeatherPhysics.HailShare(supercell, AirColumn.Of(Earth, 291f, 1f, -1f, 1f), Earth);
			LogAssert.IsTrue(hails > 0.3f, $"a supercell lands hail: {hails:0.00}");

			// Weaker pull holds bigger stones up: the same storm on a lighter world hails harder.
			PlanetAir light = Earth;
			light.Gravity = 5.5f;
			float lighter = WeatherPhysics.HailShare(supercell, AirColumn.Of(light, 291f, 1f, -1f, 1f), light);
			LogAssert.IsTrue(lighter >= hails, $"lighter pull, bigger stones: {lighter:0.00} vs {hails:0.00}");
		}

		[Test]
		public void AHighsLidStopsTheRain()
		{
			var damp = new WeatherDriver.Synoptic { Humidity = 0.8f, Pressure = 0f, Instability = 0.25f, LocalTime01 = 0.5f, Wind = new Vector2(4f, 0f) };
			WeatherFrame open = WeatherPhysics.Frame(damp, AirColumn.Of(Earth, 288f, damp.Humidity, damp.Pressure, damp.Instability), Earth, default, 0.3f, 1f, out _);
			LogAssert.IsTrue(open[WeatherChannel.Precipitation] > 0.1f, $"damp air rains: {open[WeatherChannel.Precipitation]:0.00}");

			// The same damp air under a strong high: the sinking air leaves no depth for rain.
			var settled = damp;
			settled.Pressure = 0.9f;
			WeatherFrame lidded = WeatherPhysics.Frame(settled, AirColumn.Of(Earth, 288f, settled.Humidity, settled.Pressure, settled.Instability), Earth, default, 0.3f, 1f, out _);
			LogAssert.IsTrue(lidded[WeatherChannel.Precipitation] < open[WeatherChannel.Precipitation] * 0.25f,
				$"under a high's lid it barely drizzles: {lidded[WeatherChannel.Precipitation]:0.000}");
		}

		// ── Storms ────────────────────────────────────────────────────

		[Test]
		public void AStormFormsOnlyWhereTheAirAllowsIt()
		{
			var settled = new WeatherDriver.Synoptic { Humidity = 0.4f, Pressure = 0.6f, Instability = 0.05f, Wind = new Vector2(3f, 0f), LocalTime01 = 0.5f };
			AirColumn calm = AirColumn.Of(Earth, 288f, settled.Humidity, settled.Pressure, settled.Instability);
			LogAssert.AreEqual(0f, StormPhysics.LikelihoodAt(settled, calm, default, 40f).Thunderstorm, "settled air makes no thunderstorms");

			var sultry = new WeatherDriver.Synoptic { Humidity = 0.85f, Pressure = -0.5f, Instability = 0.7f, Wind = new Vector2(4f, 0f), LocalTime01 = 0.6f };
			AirColumn unstable = AirColumn.Of(Earth, 302f, sultry.Humidity, sultry.Pressure, sultry.Instability);
			StormPhysics.Likelihood overLand = StormPhysics.LikelihoodAt(sultry, unstable, default, 15f);
			LogAssert.IsTrue(overLand.Thunderstorm > 0.1f, $"sultry, unstable air does: {overLand.Thunderstorm:0.00}");
			LogAssert.AreEqual(0f, overLand.TropicalCyclone, "but no hurricane over land");
			var sea = new GroundTraits { Water = true };
			LogAssert.IsTrue(StormPhysics.LikelihoodAt(sultry, unstable, sea, 15f).TropicalCyclone > 0f, "over warm water off the equator, one can");
			LogAssert.AreEqual(0f, StormPhysics.LikelihoodAt(sultry, unstable, sea, 1f).TropicalCyclone, "but not where the world's spin cannot turn it");
		}

		[Test]
		public void AStormDoesToTheAirWhatItsKindDoes()
		{
			var air = new WeatherDriver.Synoptic { Humidity = 0.5f, Pressure = 0f, Instability = 0.3f, Wind = new Vector2(5f, 0f) };
			WeatherDriver.Synoptic storm = StormPhysics.Perturb(air, StormKind.Thunderstorm, 1f);
			LogAssert.IsTrue(storm.Humidity > air.Humidity && storm.Instability > air.Instability && storm.Pressure < air.Pressure, "rising, condensing air");
			LogAssert.AreEqual(1f, storm.Tower, "a tower stands");
			WeatherDriver.Synoptic haboob = StormPhysics.Perturb(air, StormKind.Haboob, 1f);
			LogAssert.IsTrue(haboob.Wind.magnitude > air.Wind.magnitude + 10f, "a haboob is its wind");
			LogAssert.AreEqual(air.Humidity, StormPhysics.Perturb(air, StormKind.Supercell, 0f).Humidity, "no storm, no change");
			LogAssert.AreEqual(StormCellShape.Front, StormPhysics.ShapeOf(StormKind.SquallLine));
			LogAssert.AreEqual(StormCellShape.Eyewall, StormPhysics.ShapeOf(StormKind.TropicalCyclone));
			LogAssert.IsTrue(StormPhysics.TryParse("tornado", out StormKind tornado) && tornado == StormKind.Supercell);
		}

		[Test]
		public void SandMovesAtAboutSixMetresASecondAndAtLessOnALighterWorld()
		{
			var sand = ScriptableObject.CreateInstance<WeatherSubstance>();
			try
			{
				sand.GrainMetres = 2e-4f;
				sand.GrainDensity = 2650f;
				float here = WeatherPhysics.LiftingWind(sand, Earth, 288f);
				Assert.That(here, Is.InRange(4.5f, 8f), "our own sand");
				PlanetAir light = Earth;
				light.Gravity *= 0.5f;
				LogAssert.IsTrue(WeatherPhysics.LiftingWind(sand, light, 288f) < here, "less pull, less wind");
				PlanetAir thin = Earth;
				thin.SurfacePressure *= 0.15f;
				LogAssert.IsTrue(WeatherPhysics.LiftingWind(sand, thin, 288f) > here, "thin air needs a gale");
			}
			finally
			{
				Object.DestroyImmediate(sand);
			}
		}

		// ── Clouds ────────────────────────────────────────────────────

		[Test]
		public void TheRegimesStandInOrderAndTheWindRisesWithHeight()
		{
			CloudClimate.Scales scales = CloudClimate.ScalesFor(Earth, 45f, 238u);
			Assert.That(scales.LowTile, Is.InRange(6000f, 20000f));
			LogAssert.IsTrue(scales.MidWind > 1f && scales.HighWind > scales.MidWind, "the wind rises with height");
			LogAssert.AreEqual(Mathf.Round(scales.HighStretch), scales.HighStretch, "the cirrus stretch is a whole number, for the drift's wrap");

			var air = new WeatherDriver.Synoptic { Humidity = 0.6f, Pressure = -0.1f, Instability = 0.35f };
			AirColumn column = AirColumn.Of(Earth, 288f, air.Humidity, air.Pressure, air.Instability);
			var bands = new CloudBand[CloudClimate.BandCount];
			CloudClimate.Bands(scales, Earth, air, column, default, bands);
			LogAssert.IsTrue(bands[0].Column && bands[0].Present);
			LogAssert.AreEqual(1f, bands[0].WindScale, "the column rides the weather's own drift, exactly");
			LogAssert.IsTrue(bands[1].Present && bands[1].Bottom > bands[0].Bottom, "the middle above the base");
			LogAssert.IsTrue(bands[2].Present && bands[2].Bottom > bands[1].Bottom, "the ice above the middle");
			LogAssert.IsTrue(bands[2].Extinction < bands[0].Extinction * Mathf.Pow(300f, 2f / 3f), "cirrus is thin");
		}
	}
}
