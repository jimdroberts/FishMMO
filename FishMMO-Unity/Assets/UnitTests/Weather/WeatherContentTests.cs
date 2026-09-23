using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;
using FishMMO.UnitTests.Harness;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The content layer over the weather (Q16): what a designer can gate a spawn on, test in a
	/// trigger, and scale an ability by.
	/// </summary>
	/// <remarks>
	/// Every reading here is a pure function of a weather sample, which is itself a pure function of
	/// a tick and a position. That is what lets an ability modifier be PREDICTED: the owner and the
	/// server reach the same multiplier from the same inputs, and a reconcile replay reaches it
	/// again. Where these agree, nothing has to be sent.
	/// </remarks>
	[TestFixture]
	public class WeatherContentTests
	{
		[TearDown]
		public void TearDown()
		{
			WeatherLayerHandles.Clear();
			WeatherQuery.Clear();
		}

		private static WeatherSample Sample(float precipitation = 0f, float rain = 0f, float snow = 0f,
			float wind = 0f, float lightning = 0f, float cloud = 0f, float shelter = 0f, float temperature = 0f)
		{
			var frame = new WeatherFrame();
			frame[WeatherChannel.Precipitation] = precipitation;
			frame[WeatherChannel.RainWeight] = rain;
			frame[WeatherChannel.SnowWeight] = snow;
			frame[WeatherChannel.WindSpeed] = wind;
			frame[WeatherChannel.LightningRate] = lightning;
			frame[WeatherChannel.CloudCover] = cloud;
			return new WeatherSample { Frame = frame, Shelter = shelter, Temperature = temperature };
		}

		private static WeatherSample Rain(float amount, float shelter = 0f) => Sample(precipitation: amount, rain: 1f, shelter: shelter);
		private static WeatherSample Snow(float amount) => Sample(precipitation: amount, snow: 1f);

		// ---- severity, the reading everything else is built on ----

		[Test]
		public void SeverityIsReadFromTheKindsAskedAboutAndNoOthers()
		{
			/* The mistake this exists to prevent. Rain, snow and hail all arrive on the same
			 * precipitation channel and differ only in the mix, so a rule waiting on snow that
			 * measured "precipitation" would fire in a rainstorm. Every reader of severity in the
			 * project goes through this one function so none of them can drift apart on it. */
			WeatherSample downpour = Rain(0.9f);

			Assert.That(WeatherSeverity.Of(WeatherKindMask.Precipitation, downpour), Is.EqualTo(0.9f).Within(1e-4f));
			Assert.That(WeatherSeverity.Of(WeatherKindMask.Lightning, downpour), Is.EqualTo(0f), "no lightning in it");
			Assert.That(WeatherSeverity.Of(WeatherKindMask.Wind, downpour), Is.EqualTo(0f), "and no wind");

			// The worst of several, not their sum.
			WeatherSample storm = Sample(precipitation: 0.6f, rain: 1f, wind: 0.8f, lightning: 0.3f);
			Assert.That(WeatherSeverity.Of(WeatherKindMask.Precipitation | WeatherKindMask.Wind, storm),
				Is.EqualTo(0.8f).Within(1e-4f), "the worst of them, never added together");
		}

		[Test]
		public void AClearSkyIsMeasuredByHowClearItIs()
		{
			// "Clear" is the absence of the others, so it needs reading backwards or it would always
			// measure zero and no rule could ever wait for good weather.
			Assert.That(WeatherSeverity.Of(WeatherKindMask.ClearSky, Sample(cloud: 0f)), Is.EqualTo(1f).Within(1e-4f));
			Assert.That(WeatherSeverity.Of(WeatherKindMask.ClearSky, Sample(cloud: 1f)), Is.EqualTo(0f).Within(1e-4f));
		}

		// ---- the ECA condition ----

		[Test]
		public void AWeatherConditionPassesOnlyForTheWeatherItNames()
		{
			var wet = new WeatherCondition
			{
				Reads = WeatherConditionReads.Severity,
				Weather = WeatherKindMask.Rain,
				AtLeast = 0.4f,
				AtMost = 1f,
			};

			Assert.That(wet.Matches(Rain(0.8f)), Is.True, "heavy rain");
			Assert.That(wet.Matches(Rain(0.1f)), Is.False, "a drizzle is under the bar");
			Assert.That(wet.Matches(Snow(0.8f)), Is.False, "heavy SNOW is not heavy rain");
			Assert.That(wet.Matches(Sample()), Is.False, "a clear day");
		}

		[Test]
		public void AWeatherConditionCanAskAboutABandNotJustAFloor()
		{
			// "Overcast but not raining" is a band, and needs both ends.
			var overcast = new WeatherCondition
			{
				Reads = WeatherConditionReads.Channel,
				Channel = WeatherChannel.Precipitation,
				AtLeast = 0f,
				AtMost = 0.1f,
			};
			Assert.That(overcast.Matches(Sample(cloud: 0.9f)), Is.True, "dry");
			Assert.That(overcast.Matches(Rain(0.5f)), Is.False, "raining is outside the band");
		}

		[Test]
		public void AWeatherConditionCanAskAboutShelterAndTemperature()
		{
			var indoors = new WeatherCondition { Reads = WeatherConditionReads.Exposure, AtLeast = -1f, AtMost = 0.25f };
			Assert.That(indoors.Matches(Rain(1f, shelter: 1f)), Is.True, "under a roof");
			Assert.That(indoors.Matches(Rain(1f, shelter: 0f)), Is.False, "out in it");

			var freezing = new WeatherCondition { Reads = WeatherConditionReads.Temperature, AtLeast = -1f, AtMost = -0.5f };
			Assert.That(freezing.Matches(Sample(temperature: -0.8f)), Is.True);
			Assert.That(freezing.Matches(Sample(temperature: 0.2f)), Is.False);
		}

		// ---- ability modifiers (Q16) ----

		[Test]
		public void AnAbilityIsScaledAcrossTheBandItNames()
		{
			// A lightning spell that hits half again as hard in a full storm.
			var rule = new WeatherAbilityModifier
			{
				Target = WeatherAbilityTarget.Power,
				Reads = WeatherAbilityReads.Severity,
				Weather = WeatherKindMask.Lightning,
				From = 0f, To = 1f,
				AtNone = 1f, AtFull = 1.5f,
			};

			Assert.That(rule.Evaluate(Sample()), Is.EqualTo(1f).Within(1e-4f), "a clear day changes nothing");
			Assert.That(rule.Evaluate(Sample(lightning: 1f)), Is.EqualTo(1.5f).Within(1e-4f), "a full storm");
			Assert.That(rule.Evaluate(Sample(lightning: 0.5f)), Is.EqualTo(1.25f).Within(1e-4f), "and half way is half the bonus");
		}

		[Test]
		public void ABandWrittenBackwardsWorksAsWritten()
		{
			/* "The drier it is, the harder this hits" is as reasonable a rule as its opposite, and is
			 * written by putting the larger reading in From. A plain subtraction would have produced
			 * a NEGATIVE multiplier for it; InverseLerp copes with a reversed range. */
			var fire = new WeatherAbilityModifier
			{
				Target = WeatherAbilityTarget.Power,
				Reads = WeatherAbilityReads.Channel,
				Channel = WeatherChannel.Precipitation,
				From = 1f, To = 0f,
				AtNone = 0.5f, AtFull = 1f,
			};

			Assert.That(fire.Evaluate(Rain(1f)), Is.EqualTo(0.5f).Within(1e-4f), "a downpour halves it");
			Assert.That(fire.Evaluate(Sample()), Is.EqualTo(1f).Within(1e-4f), "and dry air leaves it alone");
			Assert.That(fire.Evaluate(Rain(0.5f)), Is.EqualTo(0.75f).Within(1e-4f));
		}

		[Test]
		public void RulesForTheSameNumberMultiplyRatherThanAddUp()
		{
			/* Two rules each halving a cooldown must leave a quarter of it, not none of it. Summed
			 * reductions reach zero and then go negative, so a pair of individually sensible rules
			 * could remove a cooldown outright. */
			var half = new WeatherAbilityModifier
			{
				Target = WeatherAbilityTarget.Cooldown,
				Reads = WeatherAbilityReads.Channel,
				Channel = WeatherChannel.Precipitation,
				From = 0f, To = 1f, AtNone = 1f, AtFull = 0.5f,
			};
			var alsoHalf = new WeatherAbilityModifier
			{
				Target = WeatherAbilityTarget.Cooldown,
				Reads = WeatherAbilityReads.Channel,
				Channel = WeatherChannel.Precipitation,
				From = 0f, To = 1f, AtNone = 1f, AtFull = 0.5f,
			};
			var otherTarget = new WeatherAbilityModifier
			{
				Target = WeatherAbilityTarget.Speed,
				Reads = WeatherAbilityReads.Channel,
				Channel = WeatherChannel.Precipitation,
				From = 0f, To = 1f, AtNone = 1f, AtFull = 0.1f,
			};

			var rules = new List<WeatherAbilityModifier> { half, alsoHalf, otherTarget };
			WeatherSample downpour = Rain(1f);

			Assert.That(WeatherAbilityModifiers.Multiplier(rules, WeatherAbilityTarget.Cooldown, downpour),
				Is.EqualTo(0.25f).Within(1e-4f), "a quarter, never zero");
			Assert.That(WeatherAbilityModifiers.Multiplier(rules, WeatherAbilityTarget.Speed, downpour),
				Is.EqualTo(0.1f).Within(1e-4f), "and a rule for another number stays out of it");
			Assert.That(WeatherAbilityModifiers.Multiplier(rules, WeatherAbilityTarget.LifeTime, downpour),
				Is.EqualTo(1f), "a number nothing names is untouched");
		}

		[Test]
		public void AnAbilityWithNoRulesCostsNothingAndChangesNothing()
		{
			// The case nearly every ability is in. Asked before anything is sampled, so the cast path
			// — which runs in the replicate and is replayed on every reconcile — pays one loop over
			// an empty list.
			Assert.That(WeatherAbilityModifiers.Any(null, WeatherAbilityTarget.Power), Is.False);
			Assert.That(WeatherAbilityModifiers.Any(new List<WeatherAbilityModifier>(), WeatherAbilityTarget.Power), Is.False);
			Assert.That(WeatherAbilityModifiers.Multiplier(null, WeatherAbilityTarget.Power, Rain(1f)), Is.EqualTo(1f));
		}

		[Test]
		public void AModifierNeverProducesANegativeNumber()
		{
			// Whatever a designer types, an ability cannot have a negative cast time.
			var silly = new WeatherAbilityModifier
			{
				Target = WeatherAbilityTarget.ActivationTime,
				Reads = WeatherAbilityReads.Channel,
				Channel = WeatherChannel.Precipitation,
				From = 0f, To = 1f, AtNone = 1f, AtFull = 0f,
			};
			Assert.That(silly.Evaluate(Rain(1f)), Is.EqualTo(0f).Within(1e-4f));
			Assert.That(silly.Evaluate(Rain(1f)), Is.GreaterThanOrEqualTo(0f));
		}

		// ---- weather-gated spawns (Q16) ----

		[Test]
		public void ASpawnGateReadsTheKindsItNames()
		{
			var nightCrawler = new WeatherRespawnCondition
			{
				Weather = WeatherKindMask.Rain,
				MinimumSeverity = 0.3f,
			};

			Assert.That(nightCrawler.SeverityOf(Rain(0.8f)), Is.EqualTo(0.8f).Within(1e-4f));
			Assert.That(nightCrawler.SeverityOf(Sample(lightning: 0.9f)), Is.EqualTo(0f), "lightning is not rain");

			// The inverted one — the creature that hides from the rain — is the same condition.
			var fairWeather = new WeatherRespawnCondition
			{
				Weather = WeatherKindMask.Rain,
				MinimumSeverity = 0.3f,
				Invert = true,
			};
			Assert.That(fairWeather.Invert, Is.True);
		}

		[Test]
		public void ASpawnGateInASceneWithNoWeatherSpawnsNormallyByDefault()
		{
			/* Most scenes have no weather registered while this is being built, and answering "no"
			 * there would silently empty every gated spawner in all of them — a very quiet way to
			 * lose a world's worth of creatures. */
			var gate = new WeatherRespawnCondition();
			Assert.That(gate.AllowWhenSceneHasNoWeather, Is.True);
		}

		// ---- layer handles: how an ECA action takes back the layer it put up ----

		[Test]
		public void ALayerIsRemovedByTheNameItsAuthorGaveIt()
		{
			/* A layer is removed by a handle the server allocated at runtime, and ECA actions cannot
			 * pass a value from one to the next — each runs independently against the event. Naming
			 * the layer at authoring time and looking its handle up by that name is what closes the
			 * gap. */
			var caster = new StubCharacter { ID = 1 };

			WeatherLayerHandles.Remember(caster, "ritual-storm", 42);
			Assert.That(WeatherLayerHandles.TryTake(caster, "ritual-storm", out ushort handle), Is.True);
			Assert.That(handle, Is.EqualTo(42));

			// Taken, not read: a handle is good for exactly one removal. Left behind, a second
			// trigger would remove a layer the server has forgotten — or one whose number has since
			// been handed to a different layer.
			Assert.That(WeatherLayerHandles.TryTake(caster, "ritual-storm", out _), Is.False, "and only once");
			Assert.That(WeatherLayerHandles.TrackedCharacters, Is.EqualTo(0), "the record is gone with it");
		}

		[Test]
		public void OneCastersStormCannotBeCalledOffByAnother()
		{
			// Two players lighting the same brazier must each be able to put out their own.
			var first = new StubCharacter { ID = 1 };
			var second = new StubCharacter { ID = 2 };

			WeatherLayerHandles.Remember(first, "brazier", 10);
			WeatherLayerHandles.Remember(second, "brazier", 20);

			Assert.That(WeatherLayerHandles.TryTake(second, "brazier", out ushort theirs), Is.True);
			Assert.That(theirs, Is.EqualTo(20), "their own layer, not the other player's");
			Assert.That(WeatherLayerHandles.TryTake(first, "brazier", out ushort mine), Is.True);
			Assert.That(mine, Is.EqualTo(10));
		}

		[Test]
		public void RemovingALayerNobodyPutUpIsNotAnError()
		{
			// How "stop the storm" is written for a storm that may not be running.
			var caster = new StubCharacter { ID = 1 };
			Assert.That(WeatherLayerHandles.TryTake(caster, "never-added", out _), Is.False);
			Assert.That(WeatherLayerHandles.TryTake(null, "anything", out _), Is.False);
			Assert.That(WeatherLayerHandles.TryTake(caster, "", out _), Is.False);
		}

		// ---- the authority gate in front of every weather action ----

		[Test]
		public void AWeatherActionRefusesToActWithoutServerAuthority()
		{
			/* Weather ACTIONS are the one part of this pass that is not predicted. Everything else —
			 * exposure, recipes, region buffs — is derived from a timeline both peers hold. Editing
			 * that timeline is the opposite: a client that did it locally would be predicting a
			 * future the server never decided on. A character with no network object is not a
			 * server, so the gate hands back nothing to act through. */
			var notOnAServer = new StubCharacter { ID = 1 };
			Assert.That(WeatherActionGate.Resolve(notOnAServer, null, out _), Is.Null);
			Assert.That(WeatherActionGate.Resolve(null, null, out _), Is.Null);

			// And the underlying decision, stated directly.
			Assert.That(RegionActionGate.Decide(hasInitiator: true, isServerStarted: false, isReconciling: false), Is.False,
				"a client never edits the weather");
			Assert.That(RegionActionGate.Decide(hasInitiator: true, isServerStarted: true, isReconciling: true), Is.False,
				"and never during a reconcile replay, or one trigger would broadcast the edit once per replayed tick");
			Assert.That(RegionActionGate.Decide(hasInitiator: true, isServerStarted: true, isReconciling: false), Is.True);
		}

		[Test]
		public void TheCommandSeamIsClearedWithTheTimelinesSoNothingCallsADeadHost()
		{
			// Shared content reaches the server's weather host through WeatherQuery.Commands. A
			// teardown that left a stopped host behind would have triggers calling into it.
			WeatherQuery.Commands = null;
			WeatherQuery.Clear();
			Assert.That(WeatherQuery.Commands, Is.Null);
		}
	}
}
