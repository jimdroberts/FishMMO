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
	/// The shape a storm cell covers the ground in.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything was a disc, which is right for a shower and wrong for most of the weather worth
	/// seeing. A front is a LINE that arrives as a wall; a hurricane has a calm eye with its worst
	/// weather in a ring around it; a tornado is a tiny violent core. None of those are a smooth
	/// falloff from a centre, so the shape has to be part of the cell rather than something a
	/// preset can imply.
	/// </para>
	/// <para>
	/// <b>A byte and one float</b> is the whole cost on the wire. The shapes share
	/// <see cref="StormCell.RadiusMeters"/> and <see cref="StormCell.ExtentMeters"/> and read them
	/// differently, rather than each carrying its own geometry — see each member for what the two
	/// mean to it.
	/// </para>
	/// </remarks>
	public enum StormCellShape : byte
	{
		/// <summary>
		/// A shower or a thunderhead: full inside 55% of the radius, fading to the edge.
		/// <c>Radius</c> is the edge; <c>Extent</c> is unused.
		/// </summary>
		Disc = 0,

		/// <summary>
		/// A front: a wall of weather that arrives along its whole length at once.
		/// <c>Radius</c> is the band's depth; <c>Extent</c> is its half-length along the line.
		/// </summary>
		/// <remarks>
		/// Its line runs PERPENDICULAR to its velocity, so a front needs no orientation of its own —
		/// a front advances at right angles to itself, which is exactly what its heading already
		/// says. One less thing to author, one less thing to send, and one less thing that can
		/// disagree with the direction it is travelling.
		/// </remarks>
		Front = 1,

		/// <summary>
		/// A hurricane: a calm eye, the worst of it in the ring around that, decaying outward.
		/// <c>Radius</c> is the outer edge; <c>Extent</c> is the eye's radius.
		/// </summary>
		Eyewall = 2,

		/// <summary>
		/// A tornado: a small violent core inside a much larger region of disturbed air.
		/// <c>Radius</c> is the core; <c>Extent</c> is how far it is felt at all.
		/// </summary>
		Funnel = 3,
	}

	/// <summary>
	/// A moving storm: a preset over a shape that drifts with the wind, grows, matures and decays.
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
		/// <summary>What this means depends on <see cref="Shape"/>; see <see cref="StormCellShape"/>.</summary>
		public float RadiusMeters;

		/// <summary>
		/// The shape's second measurement: a front's half-length, a hurricane's eye, a tornado's
		/// outer reach. Unused by <see cref="StormCellShape.Disc"/>.
		/// </summary>
		public float ExtentMeters;

		/// <summary>Disc unless something says otherwise, so every cell authored before this is unchanged.</summary>
		public StormCellShape Shape;

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

		/// <summary>How much of the cell applies at a position, 0..1.</summary>
		/// <remarks>
		/// The envelope and the peak are the cell's life and strength; the SHAPE decides how that is
		/// spread over the ground. Split out so each shape is readable on its own and can be tested
		/// without a tick or a timeline.
		/// </remarks>
		public float InfluenceAt(Vector3 position, uint tick, double tickDelta)
		{
			float envelope = EnvelopeAt(tick);
			if (envelope <= 0f || RadiusMeters <= 0f)
			{
				return 0f;
			}
			Vector2 centre = CentreAt(tick, tickDelta);
			var at = new Vector2(position.x, position.z);
			return envelope * PeakIntensity * Coverage(at - centre);
		}

		/// <summary>
		/// How strongly the shape covers a point, given as an offset from the cell's centre.
		/// </summary>
		/// <remarks>
		/// Pure: an offset in, a 0..1 out. No tick, no timeline, no scene — so every shape can be
		/// checked directly, which matters because the difference between a front and a disc is
		/// entirely in this function.
		/// </remarks>
		public float Coverage(Vector2 offset)
		{
			switch (Shape)
			{
				case StormCellShape.Front:
					return FrontCoverage(offset);
				case StormCellShape.Eyewall:
					return EyewallCoverage(offset.magnitude);
				case StormCellShape.Funnel:
					return FunnelCoverage(offset.magnitude);
				default:
					return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RadiusMeters * 0.55f, RadiusMeters, offset.magnitude));
			}
		}

		/// <summary>The direction a front advances in: its velocity, or east when it is not moving.</summary>
		/// <remarks>
		/// A front's LINE is perpendicular to this, so nothing has to be authored or sent for its
		/// orientation. A stationary front is a contradiction in terms but content can still ask for
		/// one, so it gets an arbitrary but stable facing rather than a divide by zero.
		/// </remarks>
		public Vector2 Facing
		{
			get
			{
				var v = new Vector2(VelocityX, VelocityZ);
				return v.sqrMagnitude > 1e-6f ? v.normalized : Vector2.right;
			}
		}

		/// <summary>
		/// A wall: deep across its line, long along it, and sharper in front than behind.
		/// </summary>
		/// <remarks>
		/// The leading edge is much steeper than the trailing one, because that is what a front is
		/// — it arrives all at once and clears slowly. Symmetric depth reads as a passing blob
		/// rather than as weather moving in.
		/// </remarks>
		private float FrontCoverage(Vector2 offset)
		{
			Vector2 forward = Facing;
			var along = new Vector2(-forward.y, forward.x);

			float across = Vector2.Dot(offset, forward);
			float sideways = Mathf.Abs(Vector2.Dot(offset, along));

			// Sharp ahead, long tail behind.
			float reach = across >= 0f ? RadiusMeters * 0.45f : RadiusMeters * 1.6f;
			float depth = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(across) / Mathf.Max(1e-3f, reach));

			// Soft ends, so a front tapers out rather than stopping at a hard line.
			float half = Mathf.Max(RadiusMeters, ExtentMeters);
			float length = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(half * 0.75f, half, sideways));
			return depth * length;
		}

		/// <summary>
		/// A hurricane: calm in the eye, worst in the ring around it, decaying outward.
		/// </summary>
		/// <remarks>
		/// The eye is quiet but not empty — 0.05 rather than 0. An eye that reported nothing at all
		/// would have the sky snap to clear and every exposure state stop dead in the middle of a
		/// hurricane, which is not what standing in one is like.
		/// </remarks>
		private float EyewallCoverage(float distance)
		{
			float eye = Mathf.Clamp(ExtentMeters, 0f, RadiusMeters * 0.6f);
			if (distance <= eye)
			{
				float t = eye > 1e-3f ? distance / eye : 1f;
				return Mathf.Lerp(0.05f, 1f, Mathf.SmoothStep(0f, 1f, t));
			}
			float outward = Mathf.InverseLerp(eye, RadiusMeters, distance);
			return 1f - Mathf.SmoothStep(0f, 1f, outward);
		}

		/// <summary>
		/// A tornado: everything inside the core, falling away steeply to the edge of what it disturbs.
		/// </summary>
		private float FunnelCoverage(float distance)
		{
			if (distance <= RadiusMeters)
			{
				return 1f;
			}
			float reach = Mathf.Max(ExtentMeters, RadiusMeters * 1.5f);
			float outward = Mathf.InverseLerp(RadiusMeters, reach, distance);
			// Squared, so it drops away fast: a tornado's edge is close to its middle.
			float falloff = 1f - Mathf.SmoothStep(0f, 1f, outward);
			return falloff * falloff;
		}

		/// <summary>
		/// A point somewhere inside the shape, from two numbers in 0..1. Offset from the centre.
		/// </summary>
		/// <remarks>
		/// <para>
		/// For anything that has to HAPPEN somewhere in a storm rather than merely be measured
		/// there — a lightning strike, and in time a debris spawn or a wind gust. It lives on the
		/// cell so the shapes are described once: a caller that placed strikes in a disc of its own
		/// would put a squall line's entire lightning display at the one point its centre happens to
		/// be, and would strike a hurricane's eye, which is the one calm place in it.
		/// </para>
		/// <para>
		/// Deterministic in its inputs, so a seeded hash gives every peer the same point.
		/// </para>
		/// </remarks>
		public Vector2 PointInside(float u, float v)
		{
			u = Mathf.Clamp01(u);
			v = Mathf.Clamp01(v);
			switch (Shape)
			{
				case StormCellShape.Front:
				{
					/* Spread along the wall, and offset across it with THE SAME ASYMMETRY the
					 * coverage has: a front reaches 0.45 of its depth ahead and 1.6 behind, so a
					 * symmetric spread put strikes out in front of its own sharp edge, where there
					 * is no weather to strike out of. Both bounds sit inside the covered band. */
					Vector2 forward = Facing;
					var along = new Vector2(-forward.y, forward.x);
					float half = Mathf.Max(RadiusMeters, ExtentMeters);
					float across = Mathf.Lerp(-RadiusMeters * 1.2f, RadiusMeters * 0.35f, v);
					return along * ((u * 2f - 1f) * half * 0.9f) + forward * across;
				}
				case StormCellShape.Eyewall:
				{
					// In the eyewall, never the eye: the middle of a hurricane is its quietest part.
					float eye = Mathf.Clamp(ExtentMeters, 0f, RadiusMeters * 0.6f);
					float angle = u * Mathf.PI * 2f;
					float r = Mathf.Lerp(eye, RadiusMeters * 0.8f, Mathf.Sqrt(v));
					return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * r;
				}
				case StormCellShape.Funnel:
				{
					// Tight to the core.
					float angle = u * Mathf.PI * 2f;
					return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * (Mathf.Sqrt(v) * RadiusMeters);
				}
				default:
				{
					float angle = u * Mathf.PI * 2f;
					return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * (Mathf.Sqrt(v) * RadiusMeters * 0.6f);
				}
			}
		}

		/// <summary>
		/// How far from its centre the cell reaches at all, for culling and scene-edge tests.
		/// </summary>
        /// <remarks>
        /// A front reaches much further along its line than across it, and a disc's radius would cut
        /// it off at the ends — retiring a front the moment its centre neared the scene edge even
        /// though most of it was still inside.
        /// </remarks>
		public float ReachMeters
		{
			get
			{
				switch (Shape)
				{
					case StormCellShape.Front:
						return Mathf.Max(RadiusMeters * 1.6f, Mathf.Max(RadiusMeters, ExtentMeters));
					case StormCellShape.Funnel:
						return Mathf.Max(ExtentMeters, RadiusMeters * 1.5f);
					default:
						return RadiusMeters;
				}
			}
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
