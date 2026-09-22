using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// What one term of an exposure state reads: a channel of the blended weather, or one of the
	/// driver's physical air properties.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The channels are the weather as it looks — how much is falling, how hard the wind blows, how
	/// thick the fog is. The air properties are the physics underneath it: the pressure field, how
	/// much water the air can hold, how willing it is to build a storm. They are not the same thing,
	/// and some states are only expressible in the second. "Soaked" is rain, a channel. "Clammy" is
	/// saturated air, which is present with no rain at all. "Ears popping" is a deep low, which is
	/// invisible in every channel there is.
	/// </para>
	/// <para>
	/// All of them are normalised to 0..1 so <see cref="WeatherExposureTerm.Onset"/> and
	/// <see cref="WeatherExposureTerm.Full"/> mean the same thing whichever is chosen — including
	/// <see cref="AirLowPressure"/>, which is the driver's signed pressure read as depth-of-low so
	/// that, like every other input here, more of it drives harder.
	/// </para>
	/// </remarks>
	public enum WeatherExposureInput : byte
	{
		/// <summary>A channel of the blended weather. The default, and what most states want.</summary>
		Channel = 0,
		/// <summary>0 a settled high … 1 the middle of a deep low.</summary>
		AirLowPressure = 1,
		/// <summary>0 dry air … 1 saturated. Present without any rain: this is what muggy is.</summary>
		AirHumidity = 2,
		/// <summary>0 … 1: how willing this air is to build a storm, before it has built one.</summary>
		AirInstability = 3,
		/// <summary>0 … 1: how strongly a storm tower stands over this spot.</summary>
		AirTower = 4,
		/// <summary>0 calm … 1 at 30 m/s. The driver's real wind, not the channel's blended one.</summary>
		AirWindSpeed = 5,
		/// <summary>0 … 1: how far the sun has stopped rising here. A polar winter, by itself.</summary>
		AirPolarNight = 6,
	}

	/// <summary>One input's contribution to an exposure state.</summary>
	[Serializable]
	public class WeatherExposureTerm
	{
		[Tooltip("What this term reads: a weather channel, or one of the driver's physical air properties.")]
		public WeatherExposureInput Input = WeatherExposureInput.Channel;
		[Tooltip("Which weather channel to read. Ignored unless Input is Channel.")]
		public WeatherChannel Channel;
		[Tooltip("The input's value below which this term contributes nothing.")]
		[Range(0f, 1f)] public float Onset = 0.05f;
		[Tooltip("The input's value at which this term contributes all of its weight.")]
		[Range(0f, 1f)] public float Full = 1f;
		[Tooltip("How much of the drive this term is worth, against the other terms.")]
		[Range(0f, 4f)] public float Weight = 1f;

		/// <summary>The wind speed, in m/s, that <see cref="WeatherExposureInput.AirWindSpeed"/> reads as 1.</summary>
		/// <remarks>
		/// The same 30 m/s <see cref="WeatherChannel.WindSpeed"/> is normalised against, so a term
		/// reading either one is on the same scale.
		/// </remarks>
		public const float FullWindSpeedMetersPerSecond = 30f;

		/// <summary>This term's input, normalised to 0..1.</summary>
		public float Read(in WeatherFrame frame, in WeatherDriver.Synoptic air)
		{
			switch (Input)
			{
				// Signed -1..1 read as depth-of-low, so that more of it drives harder like the rest.
				case WeatherExposureInput.AirLowPressure: return Mathf.Clamp01((1f - air.Pressure) * 0.5f);
				case WeatherExposureInput.AirHumidity: return Mathf.Clamp01(air.Humidity);
				case WeatherExposureInput.AirInstability: return Mathf.Clamp01(air.Instability);
				case WeatherExposureInput.AirTower: return Mathf.Clamp01(air.Tower);
				case WeatherExposureInput.AirWindSpeed: return Mathf.Clamp01(air.Wind.magnitude / FullWindSpeedMetersPerSecond);
				case WeatherExposureInput.AirPolarNight: return Mathf.Clamp01(air.PolarNight);
				default: return frame[Channel];
			}
		}

		/// <summary>How much this term drives the state, 0..Weight.</summary>
		public float Evaluate(in WeatherFrame frame, in WeatherDriver.Synoptic air)
		{
			float value = Read(frame, air);
			float span = Mathf.Max(1e-4f, Full - Onset);
			return Mathf.Clamp01((value - Onset) / span) * Mathf.Max(0f, Weight);
		}
	}

	/// <summary>How a state answers to how cold or hot it is where the character stands.</summary>
	public enum WeatherExposureTemperatureResponse : byte
	{
		/// <summary>Temperature does not drive this state.</summary>
		Ignore = 0,
		/// <summary>The colder it is, the harder it drives.</summary>
		Cold = 1,
		/// <summary>The warmer it is, the harder it drives.</summary>
		Heat = 2,
	}

	/// <summary>
	/// A state the weather can put a character into — wet, chilled, wind-burnt — and the buff that
	/// goes with it. Static content: what the weather has to be doing, how long it takes to build
	/// and to wear off, and what it applies. Never any runtime state; the levels live on
	/// <see cref="WeatherExposureController"/>, one set per character.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>It reads the weather, not the layer that caused it.</b> A template names inputs and a
	/// temperature response, and is evaluated against the blended <see cref="WeatherSample"/> where
	/// the character is standing. So a state does not care whether the rain came from the biome's
	/// background, a storm cell overhead or a GM's preset — only that rain is falling on this spot.
	/// </para>
	/// <para>
	/// <b>And it can read the physics, not only the look of it.</b> A term reads either a channel of
	/// the blended weather or one of the driver's air properties — the pressure field, the humidity,
	/// the instability, the real wind (see <see cref="WeatherExposureInput"/>). The second kind is
	/// what lets a state exist that has no visible weather at all: saturated air with nothing falling
	/// out of it, or a deep low that can only be felt.
	/// </para>
	/// <para>
	/// <b>The temperature it reads is the physical one.</b> <see cref="WeatherSample.Temperature"/>
	/// carries the biome's climate, the body's distance from its suns, its atmosphere's greenhouse,
	/// the latitude and the season — so Chilled comes on by itself on a world far from its star, at
	/// a high latitude, in its winter, without anybody authoring a cold region.
	/// </para>
	/// <para>
	/// <b>Shelter protects.</b> The drive is scaled by how exposed the character is, so standing
	/// under a roof stops the rain wetting them, while <see cref="ShelterProtection"/> at 0 marks a
	/// state a roof is no defence against — cold air is still cold indoors.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Weather Exposure", menuName = "FishMMO/Weather/Exposure State", order = 6)]
	public class WeatherExposureTemplate : CachedScriptableObject<WeatherExposureTemplate>, ICachedObject
	{
		[Tooltip("Shown in tooltips and logs. The asset's name is used when this is empty.")]
		public string Description;

		[Header("What the weather has to be doing")]
		[Tooltip("Weather channels that drive this state. Their weights are summed, then clamped to 1.")]
		public List<WeatherExposureTerm> Terms = new List<WeatherExposureTerm>();

		[Tooltip("Whether the local temperature drives this state, and which way.")]
		public WeatherExposureTemperatureResponse Temperature = WeatherExposureTemperatureResponse.Ignore;
		[Tooltip("The temperature at which the temperature term contributes nothing. Climate scale, -1 frozen to +1 scorching.")]
		[Range(-1f, 1f)] public float TemperatureOnset = 0f;
		[Tooltip("The temperature at which the temperature term contributes all of its weight.")]
		[Range(-1f, 1f)] public float TemperatureFull = -1f;
		[Tooltip("How much of the drive the temperature is worth, against the channel terms.")]
		[Range(0f, 4f)] public float TemperatureWeight = 1f;

		[Tooltip("All terms must drive it at once (rain AND cold), instead of any one of them being enough.")]
		public bool RequireEveryTerm;

		[Header("Shelter")]
		[Tooltip("How much a roof protects against this. 1: shelter stops it entirely. 0: shelter is no defence, which is right for cold and for fumes.")]
		[Range(0f, 1f)] public float ShelterProtection = 1f;

		[Header("How fast")]
		[Tooltip("Seconds of the worst weather to go from nothing to fully exposed.")]
		[Min(0.1f)] public float SecondsToFull = 45f;
		[Tooltip("Seconds out of it to wear off completely.")]
		[Min(0.1f)] public float SecondsToClear = 90f;
		[Tooltip("Wearing off is this much faster when sheltered. 2 halves the time under a roof.")]
		[Min(1f)] public float ShelteredRecovery = 2f;

		[Header("What it does")]
		[Tooltip("The buff applied while the state is held. Optional: a state with no buff is still tracked, and can still feed a recipe.")]
		public BaseBuffTemplate Buff;
		[Tooltip("The level at or above which the buff is applied.")]
		[Range(0f, 1f)] public float ApplyAt = 0.6f;
		[Tooltip("The level at or below which the buff is removed. Keep it under Apply at, so a state on the edge does not flicker.")]
		[Range(0f, 1f)] public float ReleaseAt = 0.35f;

		public string DisplayName => string.IsNullOrWhiteSpace(Description) ? name : Description;

		/// <summary>
		/// How hard the weather is pushing this state where the character stands, 0..1.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The whole sample, deliberately, and not a handful of numbers picked out of it. Everything
		/// a term can read — the blended channels, the driver's air, the physical temperature, how
		/// sheltered the spot is — comes from one sample, taken at one place and one tick. Hand the
		/// pieces in separately and it becomes possible to pass a frame from here with an air from
		/// somewhere else, and the state would be driven by weather that never existed.
		/// </para>
		/// <para>
		/// Pure: the same sample gives the same answer on the server and on the owner, which is what
		/// lets the level be predicted at all.
		/// </para>
		/// </remarks>
		/// <param name="sample">The weather at the character's position, as the controller sampled it.</param>
		public float Drive(in WeatherSample sample)
		{
			return Drive(sample.Frame, sample.Air, sample.Temperature, sample.Exposure);
		}

		/// <summary>
		/// <see cref="Drive(in WeatherSample)"/> with the sample's parts spelled out. For tests, and
		/// for tools that ask what some hypothetical weather would do.
		/// </summary>
		/// <param name="frame">The blended weather at the character's position.</param>
		/// <param name="air">What the driver says the air is doing there.</param>
		/// <param name="temperature">The local physical temperature, -1..1.</param>
		/// <param name="exposure">1 in the open, 0 fully sheltered.</param>
		public float Drive(in WeatherFrame frame, in WeatherDriver.Synoptic air, float temperature, float exposure)
		{
			float total = 0f;
			float weights = 0f;
			bool everyTermDrives = true;

			for (int i = 0; i < Terms.Count; i++)
			{
				WeatherExposureTerm term = Terms[i];
				if (term == null || term.Weight <= 0f)
				{
					continue;
				}
				float contribution = term.Evaluate(frame, air);
				total += contribution;
				weights += term.Weight;
				everyTermDrives &= contribution > 0f;
			}

			if (Temperature != WeatherExposureTemperatureResponse.Ignore && TemperatureWeight > 0f)
			{
				// Signed on purpose: Cold runs from Onset DOWN to Full, Heat from Onset UP to Full,
				// and one InverseLerp handles both because it copes with a reversed range.
				float t = Mathf.Clamp01(Mathf.InverseLerp(TemperatureOnset, TemperatureFull, temperature));
				total += t * TemperatureWeight;
				weights += TemperatureWeight;
				everyTermDrives &= t > 0f;
			}

			if (weights <= 0f)
			{
				return 0f;
			}

			// Every term, or any of them: "rain AND cold" is a different state from "rain or cold",
			// and a recipe made of two states cannot express the first without this.
			if (RequireEveryTerm && !everyTermDrives)
			{
				return 0f;
			}

			float drive = Mathf.Clamp01(total / weights);

			// Shelter, by how much this state answers to a roof.
			float sheltered = 1f - Mathf.Clamp01(exposure);
			return drive * (1f - sheltered * Mathf.Clamp01(ShelterProtection));
		}

		/// <summary>
		/// The level after one step, given the drive. Deterministic and frame-rate independent: the
		/// step is in seconds of simulated time, never <c>Time.deltaTime</c>.
		/// </summary>
		/// <remarks>
		/// The level chases the drive rather than merely rising and falling: parked in weather that
		/// is only driving at a third, a state settles at a third and stays there, which is what
		/// "drizzle never soaks you through" means. Both rates are full-scale, so a state with
		/// <see cref="SecondsToFull"/> of 45 takes 45 seconds from nothing to soaked in the worst of
		/// it, and proportionally longer in less.
		/// </remarks>
		/// <param name="level">The level now, 0..1.</param>
		/// <param name="drive">What the weather is pushing toward, 0..1.</param>
		/// <param name="seconds">How much time this step covers.</param>
		/// <param name="exposure">1 in the open, 0 fully sheltered; only affects wearing off.</param>
		public float Step(float level, float drive, float seconds, float exposure)
		{
			level = Mathf.Clamp01(level);
			drive = Mathf.Clamp01(drive);
			if (seconds <= 0f)
			{
				return level;
			}

			if (drive > level)
			{
				float rate = 1f / Mathf.Max(0.1f, SecondsToFull);
				return Mathf.Min(drive, level + rate * seconds);
			}

			float recovery = 1f / Mathf.Max(0.1f, SecondsToClear);
			// Out of the weather it wears off faster: a wet shirt dries indoors.
			float sheltered = 1f - Mathf.Clamp01(exposure);
			recovery *= Mathf.Lerp(1f, Mathf.Max(1f, ShelteredRecovery), sheltered);
			return Mathf.Max(drive, level - recovery * seconds);
		}
	}
}
