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

		/// <summary>
		/// What happens where this cell and another overlap, or false when they do not touch.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Cells move at their own speeds on their own headings, so they overtake and cross one
		/// another — and until now nothing came of it: two storms slid through each other and the
		/// weather between them was just the greater of the two. Where storms meet is where the
		/// violence is. Air forced up along the boundary between two cells is what builds the
		/// tallest towers, and that is where the lightning comes from.
		/// </para>
		/// <para>
		/// A pure function of the two cells and the tick, like everything else about a cell: the
		/// server and every client work out the same collision in the same place from the timeline
		/// they already share, so not a byte is sent for it and no two machines disagree about
		/// where the bolt came down. The strength is how deep the overlap goes, how fast the two
		/// are moving against each other — a cell overtaking another at walking pace is a merger,
		/// two crossing at speed is a squall line — and how alive both cells are.
		/// </para>
		/// </remarks>
		public static bool TryCollide(in StormCell a, in StormCell b, uint tick, double tickDelta, out StormCollision collision)
		{
			collision = default;
			float alive = a.EnvelopeAt(tick) * a.PeakIntensity * b.EnvelopeAt(tick) * b.PeakIntensity;
			if (alive <= 0.001f || a.RadiusMeters <= 0f || b.RadiusMeters <= 0f)
			{
				return false;
			}
			Vector2 ca = a.CentreAt(tick, tickDelta), cb = b.CentreAt(tick, tickDelta);
			float distance = Vector2.Distance(ca, cb);
			float reach = a.RadiusMeters + b.RadiusMeters;
			if (distance >= reach)
			{
				return false;
			}
			float smaller = Mathf.Min(a.RadiusMeters, b.RadiusMeters);
			float overlap = Mathf.Clamp01((reach - distance) / smaller);
			// How fast they move against each other. The meander is left out: it is a slow wobble
			// about the track, not the track.
			float closing = new Vector2(a.VelocityX - b.VelocityX, a.VelocityZ - b.VelocityZ).magnitude;
			float violence = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(closing / 5f));

			// The lens the two discs share: its middle along the line between the centres, and its
			// half-width. One cell wholly inside the other is the inner cell.
			Vector2 centre;
			float radius;
			if (distance <= Mathf.Abs(a.RadiusMeters - b.RadiusMeters) || distance < 1f)
			{
				centre = a.RadiusMeters < b.RadiusMeters ? ca : cb;
				radius = smaller;
			}
			else
			{
				float along = (distance * distance + a.RadiusMeters * a.RadiusMeters - b.RadiusMeters * b.RadiusMeters) / (2f * distance);
				centre = ca + (cb - ca) / distance * along;
				radius = Mathf.Sqrt(Mathf.Max(0f, a.RadiusMeters * a.RadiusMeters - along * along));
			}
			collision = new StormCollision
			{
				Centre = centre,
				// A little wider than the lens itself: the boundary between two storms is a zone.
				Radius = Mathf.Max(60f, radius * 1.15f),
				Intensity = Mathf.Clamp01(Mathf.Pow(overlap, 0.7f) * violence * alive),
				Seed = unchecked(a.Seed * 0x9E3779B1u ^ b.Seed ^ ((uint)a.ID << 16) ^ b.ID),
			};
			return collision.Intensity > 0.01f;
		}
	}

	/// <summary>Where two storm cells meet: a zone of towers, lightning and harder weather.</summary>
	public struct StormCollision
	{
		public Vector2 Centre;
		public float Radius;
		/// <summary>0..1: how violent the meeting is.</summary>
		public float Intensity;
		/// <summary>Stable for the pair, for scheduling strikes.</summary>
		public uint Seed;

		/// <summary>How much of the collision reaches a position, 0..1.</summary>
		public float InfluenceAt(Vector3 position)
		{
			float distance = Vector2.Distance(new Vector2(position.x, position.z), Centre);
			return Intensity * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Radius * 0.5f, Radius, distance)));
		}
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
		/// <summary>Seconds a soaked ground takes to dry in still, cool, overcast air: the slowest it goes.</summary>
		public const float DrySeconds = 900f;
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
			float evaporation = (1f + sun * 4f) * (1f + Mathf.Max(0f, temperature)) * (1f + wind * 0.8f) * Mathf.Lerp(1.3f, 0.55f, damp);

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
