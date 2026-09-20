using System;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// One scene-wide layer on the timeline: a template whose intensity moves from
	/// <see cref="From"/> to <see cref="To"/> between two server ticks.
	/// </summary>
	[Serializable]
	public struct WeatherLayerEntry
	{
		/// <summary>Stable per scene, so the API can retarget or remove it.</summary>
		public ushort Handle;
		public int TemplateID;
		public float From;
		public float To;
		public uint StartTick;
		public uint EndTick;
		/// <summary>Drop the entry once the transition to <see cref="To"/> (zero) has finished.</summary>
		public bool RemoveWhenDone;

		public float IntensityAt(uint tick)
		{
			if (tick <= StartTick || EndTick <= StartTick)
			{
				return tick >= EndTick ? To : From;
			}
			if (tick >= EndTick)
			{
				return To;
			}
			float t = (tick - StartTick) / (float)(EndTick - StartTick);
			return Mathf.Lerp(From, To, t * t * (3f - 2f * t));
		}

		/// <summary>True once a removal has finished and the entry can be forgotten.</summary>
		public bool IsFinished(uint tick) => RemoveWhenDone && tick >= EndTick;
	}

	/// <summary>
	/// A moving storm: a preset over a disc that drifts with the wind, grows, matures and decays.
	/// Motion and life are pure functions of the tick, so the network only hears about births,
	/// edits and deaths.
	/// </summary>
	[Serializable]
	public struct StormCell
	{
		public ushort ID;
		public int PresetID;
		public uint Seed;
		/// <summary>World X/Z of the centre at <see cref="MotionTick"/>.</summary>
		public float OriginX, OriginZ;
		/// <summary>Metres per second.</summary>
		public float VelocityX, VelocityZ;
		public float RadiusMeters;
		public float PeakIntensity;
		/// <summary>Amplitude of the seeded wander, in metres.</summary>
		public float MeanderMeters;
		public uint MotionTick;
		public uint BirthTick, MatureTick, DecayTick, DeathTick;

		/// <summary>The centre at a tick.</summary>
		public Vector2 CentreAt(uint tick, double tickDelta)
		{
			double seconds = ((long)tick - MotionTick) * tickDelta;
			double phase = Seed % 1000 * 0.00628;
			float wanderX = (float)(Math.Sin(seconds * 0.0021 + phase) * MeanderMeters);
			float wanderZ = (float)(Math.Cos(seconds * 0.0017 + phase * 1.3) * MeanderMeters);
			return new Vector2(
				(float)(OriginX + VelocityX * seconds) + wanderX,
				(float)(OriginZ + VelocityZ * seconds) + wanderZ);
		}

		/// <summary>Strength over the cell's life, 0..1: grows, holds, fades.</summary>
		public float EnvelopeAt(uint tick)
		{
			if (tick <= BirthTick || tick >= DeathTick)
			{
				return 0f;
			}
			if (tick < MatureTick)
			{
				float t = (tick - BirthTick) / (float)Math.Max(1u, MatureTick - BirthTick);
				return t * t * (3f - 2f * t);
			}
			if (tick <= DecayTick)
			{
				return 1f;
			}
			float d = (tick - DecayTick) / (float)Math.Max(1u, DeathTick - DecayTick);
			return 1f - d * d * (3f - 2f * d);
		}

		/// <summary>How much of the cell applies at a position: full inside 55% of the radius, fading to the edge.</summary>
		public float InfluenceAt(Vector3 position, uint tick, double tickDelta)
		{
			float envelope = EnvelopeAt(tick);
			if (envelope <= 0f || RadiusMeters <= 0f)
			{
				return 0f;
			}
			Vector2 centre = CentreAt(tick, tickDelta);
			float distance = Vector2.Distance(new Vector2(position.x, position.z), centre);
			float falloff = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RadiusMeters * 0.55f, RadiusMeters, distance));
			return envelope * PeakIntensity * falloff;
		}

		public bool IsDead(uint tick) => tick >= DeathTick;

	}

	/// <summary>A scene-wide climate shift moving from one value to another.</summary>
	[Serializable]
	public struct WeatherClimateEntry
	{
		public float FromTemperature, ToTemperature;
		public float FromHumidity, ToHumidity;
		public uint StartTick, EndTick;

		public void At(uint tick, out float temperature, out float humidity)
		{
			float t = EndTick <= StartTick ? (tick >= EndTick ? 1f : 0f) : Mathf.Clamp01((tick - (float)StartTick) / (EndTick - StartTick));
			if (tick < StartTick)
			{
				t = 0f;
			}
			t = t * t * (3f - 2f * t);
			temperature = Mathf.Lerp(FromTemperature, ToTemperature, t);
			humidity = Mathf.Lerp(FromHumidity, ToHumidity, t);
		}
	}

	/// <summary>How much snow, water, ash and sand lies on exposed ground, 0..1 each.</summary>
	[Serializable]
	public struct WeatherCover
	{
		public float Snow, Wet, Ash, Sand;

		/// <summary>Seconds to go from bare to fully covered at full rate.</summary>
		public const float SnowFillSeconds = 900f;
		public const float WetFillSeconds = 120f;
		/// <summary>Seconds a soaked ground takes to dry at an evaporation of 1: a mild, still, half-lit hour.</summary>
		public const float DrySeconds = 1500f;
		/// <summary>
		/// How much faster than real time the ground's clock runs. 1 in the game, where the world's
		/// clock is the real one. A test bed that runs the sky at a hundred and eighty times real time
		/// sets this to match, or the weather races past overhead while the puddles dry at the
		/// speed of the wall clock and nothing ever seems to dry at all.
		/// </summary>
		public static float TimeScale = 1f;
		public const float MeltSeconds = 1200f;
		public const float DustFillSeconds = 1200f;
		public const float DustClearSeconds = 3600f;

		/// <summary>
		/// Advances cover by <paramref name="seconds"/> under a frame. Snow melts above freezing;
		/// ground dries when the rain stops; ash and sand settle and slowly clear.
		/// </summary>
		/// <param name="sunlight">
		/// 0 at night, 1 by day. What actually reaches the ground is worked out here from the
		/// frame's own cloud, so a caller only says whether the sun is up.
		/// </param>
		public void Integrate(in WeatherFrame frame, float temperature, float seconds, float sunlight = 0.5f)
		{
			seconds *= Mathf.Max(0f, TimeScale);
			if (seconds <= 0f)
			{
				return;
			}
			// What dries a ground and takes the snow off it: the sun that gets through the cloud,
			// the warmth of the air, and the wind across it. It used to dry at one rate whatever
			// the sky was doing — a quarter of an hour from soaked, under a noon sun or at midnight
			// in the rain's own overcast alike — which read as the ground never drying at all,
			// because a shower had usually come round again first. Now a wet road under a clear
			// warm breezy noon is dry in about two minutes, and at night under cloud it takes the
			// full quarter hour.
			float sun = Mathf.Clamp01(sunlight) * (1f - Mathf.Clamp01(frame[WeatherChannel.CloudCover]) * 0.8f);
			float wind = Mathf.Clamp01(frame[WeatherChannel.WindSpeed]);
			// And the damp of the air itself: nothing dries into air that is already full.
			float damp = Mathf.Clamp01(0.5f + frame[WeatherChannel.HumidityOffset] * 2.5f);
			// Kept to a range a player watches happen. At its first setting the four factors could
			// multiply to more than twenty, which dried a soaked road in under a minute in the game
			// and in a fraction of a second on the test bed: about two and a half minutes at the
			// very fastest now — a hot, clear, windy noon in dry air — six or so on an ordinary
			// sunny day, and the better part of an hour on a cold damp still night.
			float evaporation = (1f + sun * 2.5f) * (1f + Mathf.Max(0f, temperature) * 0.6f) * (1f + wind * 0.5f) * Mathf.Lerp(1.2f, 0.7f, damp);

			float snowRate = frame[WeatherChannel.SnowCoverRate];
			// Snow goes above freezing, and — slowly — just below it in direct sun: that is what
			// takes the snow off a south slope on a bright cold day.
			float thaw = temperature > 0f ? Mathf.Lerp(0.3f, 3f, Mathf.Clamp01(temperature)) : 0f;
			float melt = thaw * (0.6f + sun * 0.9f) + (temperature > -0.12f ? sun * 0.25f : 0f);
			Snow = Mathf.Clamp01(Snow + seconds * (snowRate / SnowFillSeconds - melt / MeltSeconds));

			float wetTarget = Mathf.Max(frame[WeatherChannel.WetnessTarget], melt > 0f && Snow > 0f ? 0.4f : 0f);
			Wet = Wet < wetTarget
				? Mathf.MoveTowards(Wet, wetTarget, seconds / WetFillSeconds)
				: Mathf.MoveTowards(Wet, wetTarget, seconds * evaporation / DrySeconds);

			Ash = Mathf.Clamp01(Ash + seconds * (frame[WeatherChannel.AshCoverRate] / DustFillSeconds - (frame[WeatherChannel.AshCoverRate] <= 0f ? 1f / DustClearSeconds : 0f)));
			Sand = Mathf.Clamp01(Sand + seconds * (frame[WeatherChannel.SandCoverRate] / DustFillSeconds - (frame[WeatherChannel.SandCoverRate] <= 0f ? 1f / DustClearSeconds : 0f)));
		}
	}
}
