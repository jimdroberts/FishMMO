using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishNet.Serializing;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// What the weather has to be doing to put a character into a state, how fast it builds and
	/// wears off, and how those levels survive the wire.
	/// </summary>
	/// <remarks>
	/// The template is pure — it takes a weather frame, a temperature and an exposure, and returns
	/// numbers — which is the property the whole prediction rests on: the owner and the server run
	/// this same arithmetic on the same inputs and must reach the same answer.
	/// </remarks>
	[TestFixture]
	public class WeatherExposureStateTests
	{
		private readonly List<ScriptableObject> created = new List<ScriptableObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (ScriptableObject asset in created)
			{
				Object.DestroyImmediate(asset);
			}
			created.Clear();
		}

		private T Make<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = name;
			created.Add(asset);
			return asset;
		}

		/// <summary>Soaked by rain, and a roof stops it.</summary>
		private WeatherExposureTemplate Wet()
		{
			WeatherExposureTemplate wet = Make<WeatherExposureTemplate>("Wet");
			wet.Terms = new List<WeatherExposureTerm>
			{
				new WeatherExposureTerm { Channel = WeatherChannel.Precipitation, Onset = 0.05f, Full = 0.6f, Weight = 1f },
			};
			wet.ShelterProtection = 1f;
			wet.SecondsToFull = 40f;
			wet.SecondsToClear = 80f;
			wet.ShelteredRecovery = 2f;
			wet.ApplyAt = 0.6f;
			wet.ReleaseAt = 0.35f;
			return wet;
		}

		/// <summary>Chilled by cold air, which a roof is no defence against.</summary>
		private WeatherExposureTemplate Chilled()
		{
			WeatherExposureTemplate chilled = Make<WeatherExposureTemplate>("Chilled");
			chilled.Terms = new List<WeatherExposureTerm>();
			chilled.Temperature = WeatherExposureTemperatureResponse.Cold;
			chilled.TemperatureOnset = 0f;
			chilled.TemperatureFull = -1f;
			chilled.TemperatureWeight = 1f;
			chilled.ShelterProtection = 0f;
			return chilled;
		}

		private static WeatherFrame Raining(float amount)
		{
			var frame = new WeatherFrame();
			frame[WeatherChannel.Precipitation] = amount;
			frame[WeatherChannel.RainWeight] = 1f;
			return frame;
		}

		/// <summary>
		/// One place, as the controller would have sampled it. The template takes the whole sample —
		/// channels, air, temperature and shelter together — so these tests hand it one too, rather
		/// than a set of loose numbers no real caller could produce.
		/// </summary>
		private static WeatherSample Spot(WeatherFrame frame, float temperature, float exposure)
		{
			return new WeatherSample
			{
				Frame = frame,
				Temperature = temperature,
				Shelter = 1f - exposure,
			};
		}

		/// <summary>The same, with the driver's air as well.</summary>
		private static WeatherSample Spot(WeatherFrame frame, WeatherDriver.Synoptic air, float temperature, float exposure)
		{
			WeatherSample sample = Spot(frame, temperature, exposure);
			sample.Air = air;
			return sample;
		}

		[Test]
		public void RainDrivesWetAndAShelterStopsIt()
		{
			WeatherExposureTemplate wet = Wet();

			Assert.That(wet.Drive(Spot(Raining(0f), 0f, 1f)), Is.EqualTo(0f), "no rain, nothing to get wet from");
			Assert.That(wet.Drive(Spot(Raining(0.03f), 0f, 1f)), Is.EqualTo(0f), "under the onset, still nothing");
			Assert.That(wet.Drive(Spot(Raining(1f), 0f, 1f)), Is.EqualTo(1f).Within(1e-4f), "a downpour drives it fully");
			Assert.That(wet.Drive(Spot(Raining(0.325f), 0f, 1f)), Is.EqualTo(0.5f).Within(0.02f), "half way up the band is half the drive");

			// Shelter. The same downpour, under a roof.
			Assert.That(wet.Drive(Spot(Raining(1f), 0f, 0f)), Is.EqualTo(0f), "a roof keeps the rain off entirely");
			Assert.That(wet.Drive(Spot(Raining(1f), 0f, 0.5f)), Is.EqualTo(0.5f).Within(1e-4f), "half sheltered is half the rain");
		}

		[Test]
		public void ColdDrivesChilledAndAShelterDoesNot()
		{
			WeatherExposureTemplate chilled = Chilled();

			Assert.That(chilled.Drive(Spot(new WeatherFrame(), 0.5f, 1f)), Is.EqualTo(0f), "a mild day chills nobody");
			Assert.That(chilled.Drive(Spot(new WeatherFrame(), -1f, 1f)), Is.EqualTo(1f).Within(1e-4f), "the coldest there is drives it fully");
			Assert.That(chilled.Drive(Spot(new WeatherFrame(), -0.5f, 1f)), Is.EqualTo(0.5f).Within(1e-4f), "half way down the band is half the drive");

			// The distinction that makes shelter mean something: a roof keeps rain off, not cold out.
			Assert.That(chilled.Drive(Spot(new WeatherFrame(), -1f, 0f)), Is.EqualTo(1f).Within(1e-4f),
				"cold air is still cold indoors — ShelterProtection 0 says so");
		}

		[Test]
		public void TheColdOfAWorldFarFromItsSunIsWhatDrivesChilled()
		{
			/* The point of the physical inputs. Nobody authors "this region is cold": the
			 * temperature the exposure reads is the climate's, and the climate's comes from where
			 * the world is. A world four times the home world's distance from its star receives a
			 * sixteenth of the light, which the fourth-root law puts at the bottom of the scale —
			 * and that alone is enough to chill anybody standing on it. */
			StarBody sun = Make<StarBody>("Sun");
			sun.SkyRadiusKm = 696000f;

			WorldBody home = Make<WorldBody>("Home");
			home.Parent = sun;
			home.Orbit = new OrbitSettings { Distance = 1f };
			home.RotationHours = 6f;

			WorldBody far = Make<WorldBody>("Far");
			far.Parent = sun;
			far.Orbit = new OrbitSettings { Distance = 4f };
			far.RotationHours = 6f;

			SolarSystemProfile system = Make<SolarSystemProfile>("System");
			system.Bodies.Add(sun);
			system.Bodies.Add(home);
			system.Bodies.Add(far);
			system.HomeWorld = home;

			CelestialMath.ClimateOffsets(system, home, 100.0, out float homeTemperature, out _);
			CelestialMath.ClimateOffsets(system, far, 100.0, out float farTemperature, out _);

			WeatherExposureTemplate chilled = Chilled();
			float atHome = chilled.Drive(Spot(new WeatherFrame(), homeTemperature, 1f));
			float outThere = chilled.Drive(Spot(new WeatherFrame(), farTemperature, 1f));

			Assert.That(atHome, Is.LessThan(0.1f), "the home world is the temperate reference and chills nobody");
			Assert.That(outThere, Is.EqualTo(1f).Within(1e-4f), "four times as far out is frozen, with no region authored to say so");
		}

		[Test]
		public void AStateCanBeDrivenByAirThatHasNoWeatherToShowForItself()
		{
			/* The reason a term can read the driver's air at all. Every channel here is zero — nothing
			 * falling, no wind, no fog, a clear sky by every visible measure — and the air over the
			 * spot is saturated and sitting in a deep low. A state built out of channels cannot say
			 * anything about that place; one built out of the air can. */
			WeatherExposureTemplate clammy = Make<WeatherExposureTemplate>("Clammy");
			clammy.Terms = new List<WeatherExposureTerm>
			{
				new WeatherExposureTerm { Input = WeatherExposureInput.AirHumidity, Onset = 0.6f, Full = 0.95f, Weight = 1f },
			};
			clammy.ShelterProtection = 0.5f;

			var dry = new WeatherDriver.Synoptic { Humidity = 0.2f };
			var muggy = new WeatherDriver.Synoptic { Humidity = 0.95f };

			Assert.That(clammy.Drive(Spot(new WeatherFrame(), dry, 0f, 1f)), Is.EqualTo(0f), "dry air does nothing");
			Assert.That(clammy.Drive(Spot(new WeatherFrame(), muggy, 0f, 1f)), Is.EqualTo(1f).Within(1e-4f),
				"saturated air drives it fully, with not a drop of rain in the frame");

			// And the channels really are empty, so nothing else could have driven it.
			Assert.That(new WeatherFrame()[WeatherChannel.Precipitation], Is.EqualTo(0f));
		}

		[Test]
		public void EachAirInputIsNormalisedOntoTheSameZeroToOneScale()
		{
			/* Onset and Full are authored 0..1 whichever input a term reads, so every input has to
			 * arrive on that scale. Two of them are not naturally on it and are the ones worth
			 * pinning: pressure is signed, and is read as how deep the low is so that — like every
			 * other input — more of it drives harder; wind is metres per second, and is divided by
			 * the same 30 m/s the wind CHANNEL is normalised against, so a term reading either one
			 * means the same thing by 0.5. */
			var term = new WeatherExposureTerm { Weight = 1f };
			var frame = new WeatherFrame();

			term.Input = WeatherExposureInput.AirLowPressure;
			Assert.That(term.Read(frame, new WeatherDriver.Synoptic { Pressure = 1f }), Is.EqualTo(0f).Within(1e-6f), "a settled high is the bottom of the scale");
			Assert.That(term.Read(frame, new WeatherDriver.Synoptic { Pressure = 0f }), Is.EqualTo(0.5f).Within(1e-6f), "neutral is the middle");
			Assert.That(term.Read(frame, new WeatherDriver.Synoptic { Pressure = -1f }), Is.EqualTo(1f).Within(1e-6f), "a deep low is the top");

			term.Input = WeatherExposureInput.AirWindSpeed;
			Assert.That(term.Read(frame, new WeatherDriver.Synoptic { Wind = Vector2.zero }), Is.EqualTo(0f).Within(1e-6f));
			Assert.That(term.Read(frame, new WeatherDriver.Synoptic { Wind = new Vector2(15f, 0f) }), Is.EqualTo(0.5f).Within(1e-6f),
				"half of 30 m/s, the same full scale the wind channel uses");
			Assert.That(term.Read(frame, new WeatherDriver.Synoptic { Wind = new Vector2(0f, -60f) }), Is.EqualTo(1f).Within(1e-6f),
				"past full scale is clamped, and direction is not speed");

			// The default still reads the channel, so an existing template is untouched by any of this.
			term.Input = WeatherExposureInput.Channel;
			term.Channel = WeatherChannel.FogDensity;
			frame[WeatherChannel.FogDensity] = 0.42f;
			Assert.That(term.Read(frame, new WeatherDriver.Synoptic { Pressure = -1f, Humidity = 1f }), Is.EqualTo(0.42f).Within(1e-6f),
				"a channel term ignores the air entirely");
		}

		[Test]
		public void EveryTermTogetherIsADifferentStateFromAnyOfThem()
		{
			// Freezing rain: it must be raining AND be cold. Either alone is not it.
			WeatherExposureTemplate sleet = Make<WeatherExposureTemplate>("Sleet");
			sleet.Terms = new List<WeatherExposureTerm>
			{
				new WeatherExposureTerm { Channel = WeatherChannel.Precipitation, Onset = 0.05f, Full = 0.6f, Weight = 1f },
			};
			sleet.Temperature = WeatherExposureTemperatureResponse.Cold;
			sleet.TemperatureOnset = 0f;
			sleet.TemperatureFull = -1f;
			sleet.TemperatureWeight = 1f;
			sleet.RequireEveryTerm = true;

			Assert.That(sleet.Drive(Spot(Raining(1f), -1f, 1f)), Is.EqualTo(1f).Within(1e-4f), "cold rain drives it");
			Assert.That(sleet.Drive(Spot(Raining(1f), 0.5f, 1f)), Is.EqualTo(0f), "warm rain does not");
			Assert.That(sleet.Drive(Spot(new WeatherFrame(), -1f, 1f)), Is.EqualTo(0f), "dry cold does not");

			// Without the flag the same two terms average instead, which is the other state entirely.
			sleet.RequireEveryTerm = false;
			Assert.That(sleet.Drive(Spot(Raining(1f), 0.5f, 1f)), Is.EqualTo(0.5f).Within(1e-4f));
		}

		[Test]
		public void ALevelBuildsAtTheAuthoredRateAndSettlesWhereTheWeatherHoldsIt()
		{
			WeatherExposureTemplate wet = Wet();   // 40s to full, 80s to clear

			// Forty seconds of a downpour, a second at a time, is exactly soaked.
			float level = 0f;
			for (int second = 0; second < 40; second++)
			{
				level = wet.Step(level, 1f, 1f, 1f);
			}
			Assert.That(level, Is.EqualTo(1f).Within(1e-3f), "SecondsToFull means what it says");

			// Eighty seconds out of it is exactly dry again.
			for (int second = 0; second < 80; second++)
			{
				level = wet.Step(level, 0f, 1f, 1f);
			}
			Assert.That(level, Is.EqualTo(0f).Within(1e-3f), "SecondsToClear likewise");

			/* And it CHASES the drive rather than merely rising: parked in drizzle that only drives
			 * at a third, a character settles at a third and stays there however long they stand in
			 * it. Without this, any weather at all would eventually soak anybody through. */
			level = 0f;
			for (int second = 0; second < 300; second++)
			{
				level = wet.Step(level, 0.33f, 1f, 1f);
			}
			Assert.That(level, Is.EqualTo(0.33f).Within(1e-3f), "drizzle never soaks you through");
		}

		[Test]
		public void GettingOutOfTheWeatherDriesYouFaster()
		{
			WeatherExposureTemplate wet = Wet();   // ShelteredRecovery 2

			float inTheOpen = wet.Step(1f, 0f, 10f, 1f);
			float underARoof = wet.Step(1f, 0f, 10f, 0f);

			Assert.That(underARoof, Is.LessThan(inTheOpen), "a wet shirt dries indoors");
			Assert.That(1f - underARoof, Is.EqualTo((1f - inTheOpen) * 2f).Within(1e-4f), "twice as fast, as authored");
		}

		[Test]
		public void AStepIsTheSameWhateverSizeItIsTakenIn()
		{
			/* The determinism the prediction needs. The server samples on the same weather ticks the
			 * owner does, but a replay may cover the same span in a different number of calls; the
			 * level must not depend on how the time was divided. */
			WeatherExposureTemplate wet = Wet();

			float oneStep = wet.Step(0f, 1f, 10f, 1f);

			float manySteps = 0f;
			for (int i = 0; i < 100; i++)
			{
				manySteps = wet.Step(manySteps, 1f, 0.1f, 1f);
			}

			Assert.That(manySteps, Is.EqualTo(oneStep).Within(1e-4f));
		}

		[Test]
		public void LevelsSurviveTheWireAtBothEndsAndEveryChangeIsSent()
		{
			// Quantisation must be exact at the ends, or a state would never quite reach its
			// threshold, or never quite release.
			Assert.That(ExposureReconcileEntry.Dequantise(ExposureReconcileEntry.Quantise(0f)), Is.EqualTo(0f));
			Assert.That(ExposureReconcileEntry.Dequantise(ExposureReconcileEntry.Quantise(1f)), Is.EqualTo(1f));
			for (float level = 0f; level <= 1f; level += 0.05f)
			{
				float round = ExposureReconcileEntry.Dequantise(ExposureReconcileEntry.Quantise(level));
				Assert.That(round, Is.EqualTo(level).Within(1e-4f), $"level {level} must survive the round trip");
			}

			// Equals decides what goes on the wire: a field it ignores is a field that silently
			// stops reconciling.
			var baseline = new ExposureReconcileEntry { TemplateID = 4, Level = 100 };
			Assert.That(baseline.Equals(new ExposureReconcileEntry { TemplateID = 4, Level = 100 }), Is.True);
			Assert.That(baseline.Equals(new ExposureReconcileEntry { TemplateID = 5, Level = 100 }), Is.False, "a changed template must be sent");
			Assert.That(baseline.Equals(new ExposureReconcileEntry { TemplateID = 4, Level = 101 }), Is.False, "a changed level must be sent");
		}

		[Test]
		public void EachSnapshotIsItsOwnArraySoAChangeIsNeverInvisible()
		{
			/* The delta serializer keeps the array it was handed last tick as its baseline and
			 * shortcuts on ReferenceEquals. So a snapshot that refilled the SAME array would move
			 * the baseline and the new value together: the comparison would find them equal and the
			 * level would silently stop reconciling — no error, just a client whose exposure slowly
			 * parts company with the server's. Every other snapshot in the pipeline allocates fresh
			 * for this reason, and this is the test that says so for exposure. */
			WeatherExposureTemplate wet = Wet();
			wet.AddToCache("Wet");

			var host = new GameObject("Exposure");
			try
			{
				WeatherExposureController controller = host.AddComponent<WeatherExposureController>();
				controller.States = new List<WeatherExposureTemplate> { wet };

				controller.RestoreFromReconcile(new[] { new ExposureReconcileEntry { TemplateID = wet.ID, Level = 10_000 } });
				ExposureReconcileEntry[] first = controller.CreateReconcileSnapshot();

				controller.RestoreFromReconcile(new[] { new ExposureReconcileEntry { TemplateID = wet.ID, Level = 50_000 } });
				ExposureReconcileEntry[] second = controller.CreateReconcileSnapshot();

				Assert.That(ReferenceEquals(first, second), Is.False, "a new snapshot must be a new array");
				Assert.That(first[0].Level, Is.EqualTo(10_000), "the old snapshot must still hold the old level");
				Assert.That(second[0].Level, Is.EqualTo(50_000), "and the new one the new level");

				// Unchanged: the same instance comes back, which is what makes the shortcut free.
				Assert.That(ReferenceEquals(controller.CreateReconcileSnapshot(), second), Is.True,
					"an unchanged snapshot must be the same instance, so the serializer can skip it");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void TheArrayRoundTripsThroughTheRealSerializer()
		{
			var previous = new[]
			{
				new ExposureReconcileEntry { TemplateID = 1, Level = 0 },
				new ExposureReconcileEntry { TemplateID = 2, Level = 30_000 },
			};
			var next = new[]
			{
				new ExposureReconcileEntry { TemplateID = 1, Level = 0 },
				new ExposureReconcileEntry { TemplateID = 2, Level = 45_000 },
			};

			var writer = new Writer();
			bool wrote = ExposureReconcileEntry.WriteArrayDelta(writer, previous, next, DeltaSerializerOption.Unset);
			Assert.That(wrote, Is.True, "a changed level must be written");

			var reader = new Reader(writer.GetArraySegment(), null);
			ExposureReconcileEntry[] read = ExposureReconcileEntry.ReadArrayDelta(reader, previous);

			Assert.That(read.Length, Is.EqualTo(2));
			Assert.That(read[0].Level, Is.EqualTo(0), "the unchanged entry comes back unchanged");
			Assert.That(read[1].Level, Is.EqualTo(45_000), "the changed one comes back changed");

			// Nothing changed: nothing on the wire, and the reader keeps what it had. This is the
			// common case — exposure moves slowly — so it has to cost nothing.
			var quiet = new Writer();
			Assert.That(ExposureReconcileEntry.WriteArrayDelta(quiet, next, next, DeltaSerializerOption.Unset), Is.False);
			Assert.That(quiet.Length, Is.EqualTo(0), "an unchanged array must not leave bytes behind");
		}
	}
}
