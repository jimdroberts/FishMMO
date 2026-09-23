using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>Which of an ability's numbers a <see cref="WeatherAbilityModifier"/> scales.</summary>
	public enum WeatherAbilityTarget : byte
	{
		/// <summary>How long it takes to cast. Above 1 is slower.</summary>
		ActivationTime = 0,
		/// <summary>How long before it can be cast again. Above 1 is longer.</summary>
		Cooldown = 1,
		/// <summary>How fast the ability object travels.</summary>
		Speed = 2,
		/// <summary>How long the ability object lives, and so how far it reaches.</summary>
		LifeTime = 3,
		/// <summary>
		/// A general strength scalar, for the ECA actions that choose to read it. Nothing applies
		/// this on its own — it is a number an authored effect can multiply by.
		/// </summary>
		Power = 4,
	}

	/// <summary>
	/// One rule scaling an ability's numbers by what the weather is doing where it is cast.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this can be predicted (Q16).</b> The weather is a pure function of a tick and a
	/// position; the tick is mapped through <see cref="WeatherExposureTick"/> and the position
	/// already reconciles. So the owner computes the same multiplier the server will, and a replay
	/// of that tick computes it again identically. Nothing is sent, and the cast bar the player sees
	/// is the cast the server runs.
	/// </para>
	/// <para>
	/// <b>The cast length is decided once, at the moment of the cast.</b> An ability whose duration
	/// changed as a cloud drifted over would have its remaining ticks disagree with the server's the
	/// moment the two sampled on different sides of an edge — so the activation path locks its tick
	/// count at cast start, and this multiplier is part of what it locks. The cast BAR is redrawn
	/// from the current weather each tick and can drift from that if the weather turns mid-cast,
	/// which is display only and is already true of the speed attribute beside it.
	/// </para>
	/// <para>
	/// <b>Immutable static content.</b> A rule is authored on a template and never mutated; the
	/// multiplier it produces is a return value, not a field.
	/// </para>
	/// </remarks>
	[Serializable]
	public class WeatherAbilityModifier
	{
		[Tooltip("Which of the ability's numbers this scales.")]
		public WeatherAbilityTarget Target = WeatherAbilityTarget.Power;

		[Tooltip("What about the weather this reads.")]
		public WeatherAbilityReads Reads = WeatherAbilityReads.Severity;

		[Tooltip("The kinds of weather this is about. Used by Severity, and required by it.")]
		public WeatherKindMask Weather = WeatherKindMask.Rain;

		[Tooltip("Which channel to read. Only used when Reads is Channel.")]
		public WeatherChannel Channel = WeatherChannel.Precipitation;

		[Tooltip("The reading at which the multiplier is At none. Temperature runs -1 to 1; everything else 0 to 1.")]
		[Range(-1f, 1f)] public float From;

		[Tooltip("The reading at which the multiplier is At full.")]
		[Range(-1f, 1f)] public float To = 1f;

		[Tooltip("The multiplier when the reading is at From. 1 changes nothing.")]
		[Min(0f)] public float AtNone = 1f;

		[Tooltip("The multiplier when the reading is at To. Below 1 shrinks the number, above 1 grows it.")]
		[Min(0f)] public float AtFull = 1f;

		/// <summary>
		/// The multiplier this rule produces for that weather. 1 when it has nothing to say.
		/// </summary>
		public float Evaluate(in WeatherSample sample)
		{
			float reading;
			switch (Reads)
			{
				case WeatherAbilityReads.Exposure:
					reading = sample.Exposure;
					break;

				case WeatherAbilityReads.Temperature:
					reading = sample.Temperature;
					break;

				case WeatherAbilityReads.Channel:
					reading = sample.Frame[Channel];
					break;

				default:
					/* Severity asks about KINDS, so what is falling has to be one of them. Read from
					 * the channel alone, a rule waiting on snow would be satisfied by rain: both
					 * arrive on the same precipitation channel and differ only in the mix. */
					if (Weather == WeatherKindMask.None || !sample.Matches(Weather))
					{
						return AtNone;
					}
					reading = WeatherSeverity.Of(Weather, sample);
					break;
			}

			/* InverseLerp, so a band written the other way round (From 1 To 0) works as written:
			 * "the drier it is, the harder this hits" is as reasonable a rule as its opposite, and
			 * a subtraction would have silently produced a negative multiplier for it. */
			float t = Mathf.Clamp01(Mathf.InverseLerp(From, To, reading));
			return Mathf.Max(0f, Mathf.Lerp(AtNone, AtFull, t));
		}
	}

	/// <summary>What a <see cref="WeatherAbilityModifier"/> measures. Mirrors the ECA condition's.</summary>
	public enum WeatherAbilityReads : byte
	{
		/// <summary>How hard the chosen kinds are coming down, 0..1.</summary>
		Severity = 0,
		/// <summary>How much of the sky reaches the caster: 1 in the open, 0 under a roof.</summary>
		Exposure = 1,
		/// <summary>The local physical temperature, -1 frozen to +1 scorching.</summary>
		Temperature = 2,
		/// <summary>One named channel of the blended weather, 0..1.</summary>
		Channel = 3,
	}

	/// <summary>
	/// How hard a set of weather kinds is coming down, in one place so every reader of it agrees.
	/// </summary>
	/// <remarks>
	/// Read from the kinds actually asked about, never from one fixed channel: a rule about a
	/// thunderstorm must not be satisfied by heavy rain with no lightning in it.
	/// </remarks>
	public static class WeatherSeverity
	{
		public static float Of(WeatherKindMask kinds, in WeatherSample sample)
		{
			float worst = 0f;
			if ((kinds & WeatherKindMask.Precipitation) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.Precipitation]);
			}
			if ((kinds & WeatherKindMask.Wind) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.WindSpeed]);
			}
			if ((kinds & WeatherKindMask.Fog) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.FogDensity]);
			}
			if ((kinds & WeatherKindMask.Lightning) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.LightningRate]);
			}
			if ((kinds & WeatherKindMask.Clouds) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.CloudCover]);
			}
			if ((kinds & WeatherKindMask.Aurora) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.Aurora]);
			}
			if ((kinds & WeatherKindMask.ClearSky) != 0)
			{
				// "Clear" is the absence of the rest, so its severity is how clear it is.
				worst = Mathf.Max(worst, 1f - sample.Frame[WeatherChannel.CloudCover]);
			}
			return Mathf.Clamp01(worst);
		}
	}

	/// <summary>Combines a set of rules into one multiplier per target.</summary>
	public static class WeatherAbilityModifiers
	{
		/// <summary>
		/// The combined multiplier for one target, from every rule that names it.
		/// </summary>
		/// <remarks>
		/// Rules MULTIPLY together rather than summing. Two rules each halving a cooldown should
		/// leave a quarter of it, not none of it: summed reductions reach zero and then go negative,
		/// and a set of otherwise sensible rules could remove a cooldown entirely by accident.
		/// </remarks>
		public static float Multiplier(List<WeatherAbilityModifier> rules, WeatherAbilityTarget target, in WeatherSample sample)
		{
			if (rules == null || rules.Count == 0)
			{
				return 1f;
			}
			float product = 1f;
			for (int i = 0; i < rules.Count; i++)
			{
				WeatherAbilityModifier rule = rules[i];
				if (rule != null && rule.Target == target)
				{
					product *= rule.Evaluate(sample);
				}
			}
			return product;
		}

		/// <summary>True when any rule in the set names that target, so a caller can skip the work.</summary>
		public static bool Any(List<WeatherAbilityModifier> rules, WeatherAbilityTarget target)
		{
			if (rules == null)
			{
				return false;
			}
			for (int i = 0; i < rules.Count; i++)
			{
				if (rules[i] != null && rules[i].Target == target)
				{
					return true;
				}
			}
			return false;
		}
	}
}
