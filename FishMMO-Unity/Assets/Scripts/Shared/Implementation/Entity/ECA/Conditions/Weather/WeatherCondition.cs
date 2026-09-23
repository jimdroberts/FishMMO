using System;
using UnityEngine;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>What a <see cref="WeatherCondition"/> measures where the character is standing.</summary>
	public enum WeatherConditionReads : byte
	{
		/// <summary>How hard the chosen kinds are coming down, 0..1.</summary>
		Severity = 0,
		/// <summary>How much of the sky reaches this spot: 1 in the open, 0 under a roof.</summary>
		Exposure = 1,
		/// <summary>The local physical temperature, -1 frozen to +1 scorching.</summary>
		Temperature = 2,
		/// <summary>One named channel of the blended weather, 0..1.</summary>
		Channel = 3,
	}

	/// <summary>
	/// Passes while the weather where the character stands is doing what it is asked about.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>It reads, so it runs anywhere.</b> Unlike the weather ACTIONS, which edit the timeline and
	/// are therefore server-only, a reading of the weather is derived from a timeline both peers
	/// already hold, at a position both peers already agree on. So this answers the same on the
	/// server and on the owning client, and can gate a predicted ability without the two disagreeing.
	/// </para>
	/// <para>
	/// <b>At the character, not at the scene.</b> Every reading is taken where the character is
	/// standing, so a condition means the same thing for someone sheltering under a ledge as for
	/// someone twenty metres away in the open — and a storm cell at the other end of the map does
	/// not unlock an ability for people standing in the sun.
	/// </para>
	/// </remarks>
	[Serializable]
	public class WeatherCondition : BaseCondition
	{
		[Tooltip("What to measure where the character is standing.")]
		public WeatherConditionReads Reads = WeatherConditionReads.Severity;

		[Tooltip("The kinds of weather this is about. Used by Severity, and required by it.")]
		public WeatherKindMask Weather = WeatherKindMask.Rain;

		[Tooltip("Which channel to read. Only used when Reads is Channel.")]
		public WeatherChannel Channel = WeatherChannel.Precipitation;

		[Tooltip("The value has to be at least this. Temperature runs -1 to 1; everything else 0 to 1.")]
		[Range(-1f, 1f)] public float AtLeast = 0.3f;

		[Tooltip("And at most this. Leave at the top of the range for an open-ended test.")]
		[Range(-1f, 1f)] public float AtMost = 1f;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter subject = ResolveSubject(initiator, eventData);
			if (subject?.GameObject == null || subject.Transform == null)
			{
				return false;
			}

			WeatherSample sample = WeatherQuery.Sample(subject.GameObject.scene, subject.Transform.position);
			return Matches(sample);
		}

		/// <summary>
		/// The whole test, against a sample. Pure, so it can be checked without a scene, a character
		/// or a running server.
		/// </summary>
		public bool Matches(in WeatherSample sample)
		{
			float value;
			switch (Reads)
			{
				case WeatherConditionReads.Exposure:
					value = sample.Exposure;
					break;

				case WeatherConditionReads.Temperature:
					value = sample.Temperature;
					break;

				case WeatherConditionReads.Channel:
					value = sample.Frame[Channel];
					break;

				default:
					/* Severity asks about KINDS, so it also has to check that those kinds are what
					 * is happening. Reading the worst channel alone would have a condition waiting
					 * on snow satisfied by rain, since both arrive on the same precipitation
					 * channel and differ only in the mix. */
					if (Weather == WeatherKindMask.None || !sample.Matches(Weather))
					{
						return false;
					}
					value = SeverityOf(sample);
					break;
			}

			return value >= AtLeast && value <= AtMost;
		}

		/// <summary>How hard the chosen kinds are coming down, 0..1: the worst of them.</summary>
		public float SeverityOf(in WeatherSample sample)
		{
			float worst = 0f;
			if ((Weather & WeatherKindMask.Precipitation) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.Precipitation]);
			}
			if ((Weather & WeatherKindMask.Wind) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.WindSpeed]);
			}
			if ((Weather & WeatherKindMask.Fog) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.FogDensity]);
			}
			if ((Weather & WeatherKindMask.Lightning) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.LightningRate]);
			}
			if ((Weather & WeatherKindMask.Clouds) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.CloudCover]);
			}
			if ((Weather & WeatherKindMask.Aurora) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.Aurora]);
			}
			if ((Weather & WeatherKindMask.ClearSky) != 0)
			{
				// "Clear" is the absence of the rest, so its severity is how clear it is.
				worst = Mathf.Max(worst, 1f - sample.Frame[WeatherChannel.CloudCover]);
			}
			return Mathf.Clamp01(worst);
		}

		/// <summary>The character this condition is about: the event's target, else the initiator.</summary>
		private static ICharacter ResolveSubject(ICharacter initiator, EventData eventData)
		{
			if (eventData != null && eventData.TargetCharacter != null)
			{
				return eventData.TargetCharacter;
			}
			return initiator;
		}
	}
}
