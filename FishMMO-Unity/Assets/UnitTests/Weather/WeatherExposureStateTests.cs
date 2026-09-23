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

		// ---- Recipes: states that exist only where two others do at once ----

		private WeatherExposureRecipe Frozen(WeatherExposureTemplate wet, WeatherExposureTemplate chilled, float atLeast = 0.6f)
		{
			WeatherExposureRecipe frozen = Make<WeatherExposureRecipe>("Frozen");
			frozen.Ingredients = new List<WeatherExposureIngredient>
			{
				new WeatherExposureIngredient { State = wet, AtLeast = atLeast },
				new WeatherExposureIngredient { State = chilled, AtLeast = atLeast },
			};
			frozen.ReleaseMargin = 0.1f;
			return frozen;
		}

		/// <summary>A controller carrying these states and recipes, with those levels already in it.</summary>
		private WeatherExposureController Controller(
			GameObject host,
			List<WeatherExposureTemplate> states,
			List<WeatherExposureRecipe> recipes,
			params (WeatherExposureTemplate state, float level)[] levels)
		{
			WeatherExposureController controller = host.AddComponent<WeatherExposureController>();
			controller.States = states;
			controller.Recipes = recipes ?? new List<WeatherExposureRecipe>();
			SetLevels(controller, levels);
			return controller;
		}

		/// <summary>
		/// Puts levels into a controller the way the server does — through the reconcile — because
		/// that is also the path that re-derives which recipes hold.
		/// </summary>
		private static void SetLevels(WeatherExposureController controller, params (WeatherExposureTemplate state, float level)[] levels)
		{
			var entries = new ExposureReconcileEntry[levels.Length];
			for (int i = 0; i < levels.Length; i++)
			{
				entries[i] = new ExposureReconcileEntry
				{
					TemplateID = levels[i].state.ID,
					Level = ExposureReconcileEntry.Quantise(levels[i].level),
				};
			}
			controller.RestoreFromReconcile(entries);
		}

		[Test]
		public void SoakedAndChilledTogetherIsAStateNeitherOneIsAlone()
		{
			WeatherExposureTemplate wet = Wet();
			wet.AddToCache("Wet");
			WeatherExposureTemplate chilled = Chilled();
			chilled.AddToCache("Chilled");
			WeatherExposureRecipe frozen = Frozen(wet, chilled);
			frozen.AddToCache("Frozen");

			var states = new List<WeatherExposureTemplate> { wet, chilled };
			var recipes = new List<WeatherExposureRecipe> { frozen };

			var host = new GameObject("Exposure");
			try
			{
				// Soaked to the skin, but warm.
				WeatherExposureController controller = Controller(host, states, recipes, (wet, 1f), (chilled, 0f));
				Assert.That(controller.IsHolding(frozen), Is.False, "wet alone is not frozen");

				// Freezing, but dry.
				SetLevels(controller, (wet, 0f), (chilled, 1f));
				Assert.That(controller.IsHolding(frozen), Is.False, "cold alone is not frozen either");

				// Both at once, and only then.
				SetLevels(controller, (wet, 0.7f), (chilled, 0.7f));
				Assert.That(controller.IsHolding(frozen), Is.True, "soaked AND chilled is frozen");

				// One ingredient short of its level is enough to undo it.
				SetLevels(controller, (wet, 0.7f), (chilled, 0.59f));
				Assert.That(controller.IsHolding(frozen), Is.False, "every ingredient, or none of it");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void ARecipeOnItsThresholdDoesNotFlickerOnAndOff()
		{
			/* The reason a recipe needs two thresholds and not one. A character parked in weather
			 * that holds an ingredient exactly on its level would otherwise gain and lose the
			 * combined buff on alternate ticks — for weather that is not changing at all — and every
			 * one of those is a buff message to every observer watching them. */
			WeatherExposureTemplate wet = Wet();
			wet.AddToCache("WetHys");
			WeatherExposureTemplate chilled = Chilled();
			chilled.AddToCache("ChilledHys");
			WeatherExposureRecipe frozen = Frozen(wet, chilled);   // AtLeast 0.6, margin 0.1

			var levels = new Dictionary<WeatherExposureTemplate, float> { { wet, 0.6f }, { chilled, 0.6f } };
			float Read(WeatherExposureTemplate state) => levels[state];

			Assert.That(frozen.IsSatisfied(false, Read), Is.True, "at the level, not yet held: it comes on");
			Assert.That(frozen.IsSatisfied(true, Read), Is.True, "and sitting there does not take it off again");

			// Slipping under the level is not enough once it is held; it has to fall clear of the band.
			levels[wet] = 0.55f;
			Assert.That(frozen.IsSatisfied(true, Read), Is.True, "inside the margin, it holds");
			Assert.That(frozen.IsSatisfied(false, Read), Is.False, "though from cold it would not have come on");

			levels[wet] = 0.49f;
			Assert.That(frozen.IsSatisfied(true, Read), Is.False, "clear of the margin, it lets go");
		}

		[Test]
		public void AHoldingRecipeTakesItsIngredientsOwnBuffsOffWhileTheirLevelsRunOn()
		{
			/* Suppression is about what the character is showing, not about the weather stopping.
			 * Frozen replaces soaked and chilled rather than stacking three icons — but underneath,
			 * the character is still getting wetter, and will still have to dry off. If suppression
			 * froze the levels too, thawing out would leave them bone dry in the middle of a
			 * downpour. */
			WeatherExposureTemplate wet = Wet();
			wet.AddToCache("WetSup");
			WeatherExposureTemplate chilled = Chilled();
			chilled.AddToCache("ChilledSup");
			WeatherExposureRecipe frozen = Frozen(wet, chilled);
			frozen.AddToCache("FrozenSup");

			var states = new List<WeatherExposureTemplate> { wet, chilled };
			var host = new GameObject("Exposure");
			try
			{
				WeatherExposureController controller = Controller(host, states,
					new List<WeatherExposureRecipe> { frozen }, (wet, 0.8f), (chilled, 0.8f));

				Assert.That(controller.IsHolding(frozen), Is.True);
				Assert.That(controller.IsSuppressed(wet), Is.True, "the recipe holds this one's buff in its place");
				Assert.That(controller.IsSuppressed(chilled), Is.True);

				// The levels are untouched by any of that.
				Assert.That(controller.LevelOf(wet), Is.EqualTo(0.8f).Within(1e-4f), "still just as wet");
				Assert.That(controller.LevelOf(chilled), Is.EqualTo(0.8f).Within(1e-4f));

				// And when it thaws, the parts speak for themselves again.
				SetLevels(controller, (wet, 0.8f), (chilled, 0.2f));
				Assert.That(controller.IsHolding(frozen), Is.False);
				Assert.That(controller.IsSuppressed(wet), Is.False, "nothing is holding it any more");
				Assert.That(controller.IsSuppressed(chilled), Is.False);
				Assert.That(controller.LevelOf(wet), Is.EqualTo(0.8f).Within(1e-4f), "and still soaked, which is the point");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void ARecipeThatDoesNotSuppressLeavesItsIngredientsAlone()
		{
			WeatherExposureTemplate wet = Wet();
			wet.AddToCache("WetNoSup");
			WeatherExposureTemplate chilled = Chilled();
			chilled.AddToCache("ChilledNoSup");
			WeatherExposureRecipe frozen = Frozen(wet, chilled);
			frozen.AddToCache("FrozenNoSup");
			frozen.SuppressIngredients = false;

			var host = new GameObject("Exposure");
			try
			{
				WeatherExposureController controller = Controller(host,
					new List<WeatherExposureTemplate> { wet, chilled },
					new List<WeatherExposureRecipe> { frozen }, (wet, 0.8f), (chilled, 0.8f));

				Assert.That(controller.IsHolding(frozen), Is.True);
				Assert.That(controller.IsSuppressed(wet), Is.False, "this one stacks on top of its parts instead");
				Assert.That(controller.IsSuppressed(chilled), Is.False);
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void AnUnauthoredRecipeAppliesToNobody()
		{
			// An empty ingredient list is not "every condition met" — it is a recipe somebody has not
			// finished writing, and a half-written one that buffed the entire world would be a
			// miserable thing to track down.
			WeatherExposureRecipe empty = Make<WeatherExposureRecipe>("Empty");
			empty.Ingredients = new List<WeatherExposureIngredient>();
			Assert.That(empty.IsSatisfied(false, _ => 1f), Is.False);
			Assert.That(empty.IsSatisfied(true, _ => 1f), Is.False, "and it does not stay on once on, either");

			// Nor does one naming a state that has gone missing.
			WeatherExposureRecipe dangling = Make<WeatherExposureRecipe>("Dangling");
			dangling.Ingredients = new List<WeatherExposureIngredient> { new WeatherExposureIngredient { State = null, AtLeast = 0f } };
			Assert.That(dangling.IsSatisfied(false, _ => 1f), Is.False);
		}

		[Test]
		public void TwoRecipesCanShareAnIngredientAndBothHold()
		{
			WeatherExposureTemplate wet = Wet();
			wet.AddToCache("WetShared");
			WeatherExposureTemplate chilled = Chilled();
			chilled.AddToCache("ChilledShared");
			WeatherExposureTemplate windy = Make<WeatherExposureTemplate>("Windy");
			windy.Terms = new List<WeatherExposureTerm>();
			windy.AddToCache("WindyShared");

			WeatherExposureRecipe frozen = Frozen(wet, chilled);
			frozen.AddToCache("FrozenShared");
			WeatherExposureRecipe windchill = Frozen(wet, windy);
			windchill.AddToCache("WindchillShared");

			var host = new GameObject("Exposure");
			try
			{
				WeatherExposureController controller = Controller(host,
					new List<WeatherExposureTemplate> { wet, chilled, windy },
					new List<WeatherExposureRecipe> { frozen, windchill },
					(wet, 0.9f), (chilled, 0.9f), (windy, 0.9f));

				Assert.That(controller.IsHolding(frozen), Is.True);
				Assert.That(controller.IsHolding(windchill), Is.True, "sharing an ingredient does not make them exclusive");

				// Drop the one only Frozen needs. Windchill still has everything it wants.
				SetLevels(controller, (wet, 0.9f), (chilled, 0.1f), (windy, 0.9f));
				Assert.That(controller.IsHolding(frozen), Is.False);
				Assert.That(controller.IsHolding(windchill), Is.True);
				Assert.That(controller.IsSuppressed(wet), Is.True, "the shared ingredient is still suppressed by the one that holds");
				Assert.That(controller.IsSuppressed(chilled), Is.False, "and released by the one that does not");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void NoNumberOfRecipesCostsAnythingOnTheWire()
		{
			/* The property that makes recipes free: they are a pure function of the levels, and the
			 * levels already reconcile. So the snapshot carries one entry per STATE and nothing per
			 * recipe, however many there are, and both peers reach the same verdict from the same
			 * numbers. If this ever fails, something has started sending derived state. */
			WeatherExposureTemplate wet = Wet();
			wet.AddToCache("WetWire");
			WeatherExposureTemplate chilled = Chilled();
			chilled.AddToCache("ChilledWire");

			var states = new List<WeatherExposureTemplate> { wet, chilled };
			var host = new GameObject("Exposure");
			try
			{
				WeatherExposureController bare = Controller(host, states, null, (wet, 0.8f), (chilled, 0.8f));
				int withoutRecipes = bare.CreateReconcileSnapshot().Length;

				var many = new List<WeatherExposureRecipe>();
				for (int i = 0; i < 8; i++)
				{
					WeatherExposureRecipe recipe = Frozen(wet, chilled);
					recipe.AddToCache($"FrozenWire{i}");
					many.Add(recipe);
				}

				var second = new GameObject("Exposure2");
				try
				{
					WeatherExposureController loaded = Controller(second, states, many, (wet, 0.8f), (chilled, 0.8f));
					Assert.That(loaded.CreateReconcileSnapshot().Length, Is.EqualTo(withoutRecipes),
						"eight recipes add nothing to the reconcile");
					Assert.That(loaded.IsHolding(many[0]), Is.True, "and they are still evaluated, from the levels alone");
				}
				finally
				{
					Object.DestroyImmediate(second);
				}
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void AfterAReconcileARecipeIsReadAsNotHeldSoTheStricterThresholdDecides()
		{
			/* The one thing about a recipe that genuinely is not derivable from the levels: which
			 * side of the hysteresis band it was on. Reading it as NOT held is the safe way round —
			 * a recipe that should be on comes on again on the very next step, whereas one wrongly
			 * believed held would never be applied again at all. So a level inside the band, arriving
			 * from the server, reads as not satisfied. */
			WeatherExposureTemplate wet = Wet();
			wet.AddToCache("WetRecon");
			WeatherExposureTemplate chilled = Chilled();
			chilled.AddToCache("ChilledRecon");
			WeatherExposureRecipe frozen = Frozen(wet, chilled);   // AtLeast 0.6, margin 0.1
			frozen.AddToCache("FrozenRecon");

			var host = new GameObject("Exposure");
			try
			{
				WeatherExposureController controller = Controller(host,
					new List<WeatherExposureTemplate> { wet, chilled },
					new List<WeatherExposureRecipe> { frozen }, (wet, 0.8f), (chilled, 0.8f));
				Assert.That(controller.IsHolding(frozen), Is.True);

				// 0.55 is inside the band: it would have KEPT a held recipe, but cannot start one.
				SetLevels(controller, (wet, 0.55f), (chilled, 0.8f));
				Assert.That(controller.IsHolding(frozen), Is.False, "the stricter threshold decides after a reconcile");

				// And back above the level it returns, so nothing is stuck off.
				SetLevels(controller, (wet, 0.8f), (chilled, 0.8f));
				Assert.That(controller.IsHolding(frozen), Is.True);
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void ATemplateThatNothingCachedStillGetsAWorkableIdFromTheController()
		{
			/* An exposure template need not be addressable — the character prefab references it
			 * directly, so Unity loads it along with the prefab — and an asset that arrives that way
			 * has an ID of zero, because nothing called AddToCache on it. That zero is not harmless:
			 * every state would share it, so the reconcile array could neither be sorted stably nor
			 * have its entries told apart, and Get() could never find a template again, so no weather
			 * buff would survive a reconcile. The controller gives them their IDs on first use. */
			WeatherExposureTemplate first = Wet();
			first.name = "UncachedWet";
			WeatherExposureTemplate second = Chilled();
			second.name = "UncachedChilled";

			Assert.That(first.ID, Is.EqualTo(0), "nothing has cached these yet");
			Assert.That(second.ID, Is.EqualTo(0));

			var host = new GameObject("Exposure");
			try
			{
				WeatherExposureController controller = host.AddComponent<WeatherExposureController>();
				controller.States = new List<WeatherExposureTemplate> { first, second };

				// Any use of the ordered list is enough; the snapshot is the one the wire cares about.
				ExposureReconcileEntry[] snapshot = controller.CreateReconcileSnapshot();

				Assert.That(first.ID, Is.Not.EqualTo(0), "the controller gave it one");
				Assert.That(second.ID, Is.Not.EqualTo(0));
				Assert.That(first.ID, Is.Not.EqualTo(second.ID), "and two templates never share it");

				Assert.That(snapshot.Length, Is.EqualTo(2));
				Assert.That(snapshot[0].TemplateID, Is.LessThan(snapshot[1].TemplateID),
					"sorted by ID, which is only meaningful once they have distinct ones");

				// And the ID is derived, not allocated: the same asset name gives the same number on
				// every peer, which is what lets it go on the wire without being sent.
				WeatherExposureTemplate elsewhere = Wet();
				elsewhere.AddToCache("UncachedWet");
				Assert.That(elsewhere.ID, Is.EqualTo(first.ID), "same type and name, same ID, on any machine");
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
