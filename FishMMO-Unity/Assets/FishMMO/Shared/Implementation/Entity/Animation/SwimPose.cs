using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// How a swimmer is drawn in the water: how far it leans, how near the surface it counts as, where its body turns, and
	/// how it tips on a wave. The arithmetic behind <see cref="CharacterAnimationController"/>'s procedural swim pose and the
	/// World Sim walker's figure, kept apart so both pose a swimmer the same way and the rules can be tested.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Presentation only.</b> The movement (<see cref="KCCController"/>) floats the capsule on the still water, a fixed
	/// depth under it, and never sees a wave. Nothing here feeds back into it.
	/// </para>
	/// <para>
	/// <b>Leans are degrees forward of upright</b>, as a model's local pitch: 0 stands, 90 lies flat along the swim, past
	/// 90 dives. Unity turns +X pitch nose down (forward (0, 0, 1) goes to (0, −sin θ, cos θ)), so a nose-up tilt is negative.
	/// </para>
	/// </remarks>
	public static class SwimPose
	{
		/// <summary>Leaning forward swimming at the surface, degrees: head up and out, the legs trailing under it.</summary>
		public const float SurfaceLean = 55f;

		/// <summary>Leaning forward treading water at the surface, degrees: all but upright.</summary>
		public const float TreadLean = 8f;

		/// <summary>The height over the feet a swimmer turns about, metres: the chest.</summary>
		/// <remarks>
		/// The capsule floats with its feet <see cref="Constants.Character.SwimFloatSubmersion"/> of its 1.6 m height (1.2 m)
		/// under the still water, so a body turned about this point lies just under the surface (0.1 m). Turned about its
		/// feet, a level swimmer lay the whole 1.2 m down.
		/// </remarks>
		public const float PivotMetres = 1.1f;

		/// <summary>How high a swimmer's head joint (the base of the skull) is held over the water at the surface, metres:
		/// the water at the chin, mouth and eyes clear.</summary>
		public const float HeadAboveWater = 0.05f;

		/// <summary>How much of a wave's slope a swimmer tips with: a body in the water follows the surface, but not all of it.</summary>
		public const float WaveTiltShare = 0.6f;

		/// <summary>The steepest a wave tips a swimmer, degrees.</summary>
		public const float MaxWaveTilt = 20f;

		/// <summary>The speed, m/s, by which a swimmer is fully into its stroke rather than treading.</summary>
		public const float StrokeSpeed = 0.8f;

		/// <summary>Depth below its float line, metres, down to which a swimmer still counts as fully at the surface.</summary>
		public const float SurfaceDepth = 0.2f;

		/// <summary>Depth below its float line, metres, past which a swimmer counts as fully under.</summary>
		public const float UnderDepth = 0.7f;

		/// <summary>
		/// 1 floating at the surface, 0 well under it, smoothly between: from how far the feet are below the line the
		/// controller floats them at (<paramref name="stillWater"/> less <see cref="Constants.Character.SwimFloatSubmersion"/>
		/// of <paramref name="capsuleHeight"/>). The controller itself calls a swimmer at the surface within 0.35 m of it.
		/// </summary>
		public static float SurfaceShare(float stillWater, float feetY, float capsuleHeight)
		{
			if (float.IsNaN(stillWater) || float.IsInfinity(stillWater))
			{
				return 0f;
			}
			float belowFloat = stillWater - Constants.Character.SwimFloatSubmersion * capsuleHeight - feetY;
			return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SurfaceDepth, UnderDepth, belowFloat));
		}

		/// <summary>
		/// Leaning along a swim whose direction climbs <paramref name="elevationDegrees"/> above level: flat (90) level,
		/// past flat diving, up toward standing rising; never quite upside down or upright (10 to 170).
		/// </summary>
		public static float AlongSwim(float elevationDegrees) => Mathf.Clamp(90f - elevationDegrees, 10f, 170f);

		/// <summary>The elevation of a velocity above level, degrees (−90 straight down to 90 straight up; 0 when still).</summary>
		public static float Elevation(Vector3 velocity)
		{
			float flat = Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
			return Mathf.Atan2(velocity.y, Mathf.Max(flat, 1e-3f)) * Mathf.Rad2Deg;
		}

		/// <summary>How far into its stroke a swimmer going <paramref name="speed"/> m/s is, 0 (treading) to 1.</summary>
		public static float Stroke(float speed) => Mathf.Clamp01(speed / StrokeSpeed);

		/// <summary>
		/// How far forward of upright a swimmer leans: at the surface (<paramref name="surfaceShare"/> 1) from treading
		/// upright into a head-up stroke as its level speed (<paramref name="levelStroke"/>, 0..1) grows; under, the lean
		/// given (<paramref name="underLean"/>: along its swim, or upright when still); between, the blend.
		/// </summary>
		public static float Lean(float surfaceShare, float levelStroke, float underLean)
		{
			float surfaceLean = Mathf.Lerp(TreadLean, SurfaceLean, Mathf.Clamp01(levelStroke));
			return Mathf.Lerp(underLean, surfaceLean, Mathf.Clamp01(surfaceShare));
		}

		/// <summary>
		/// How a wave tips a swimmer, degrees, from the drawn surface sampled <paramref name="ahead"/> and
		/// <paramref name="behind"/> it (<paramref name="length"/> metres apart) and to its <paramref name="right"/> and
		/// <paramref name="left"/> (<paramref name="width"/> apart): water rising ahead lifts its head (a negative pitch),
		/// water higher on its right rolls its right side up (a negative roll, Unity turning +Z right side down). Scaled by
		/// <see cref="WaveTiltShare"/> and <paramref name="surfaceShare"/>, and kept within <see cref="MaxWaveTilt"/>.
		/// </summary>
		public static void WaveTilt(float ahead, float behind, float length, float right, float left, float width, float surfaceShare,
			out float pitch, out float roll)
		{
			float share = WaveTiltShare * Mathf.Clamp01(surfaceShare);
			pitch = Mathf.Clamp(-Mathf.Atan2(ahead - behind, Mathf.Max(length, 1e-3f)) * Mathf.Rad2Deg * share, -MaxWaveTilt, MaxWaveTilt);
			roll = Mathf.Clamp(-Mathf.Atan2(right - left, Mathf.Max(width, 1e-3f)) * Mathf.Rad2Deg * share, -MaxWaveTilt, MaxWaveTilt);
		}

		/// <summary>
		/// The height of a point on a body over its feet once the body is turned <paramref name="lean"/> degrees forward
		/// about <see cref="PivotMetres"/>: a point <paramref name="height"/> up the body (stood upright) and
		/// <paramref name="forward"/> ahead of its axis.
		/// </summary>
		public static float TurnedHeight(float height, float forward, float lean)
		{
			float radians = lean * Mathf.Deg2Rad;
			float up = height - PivotMetres;
			// Turned about +X: up goes forward and down by cos, forward goes down by sin.
			return PivotMetres + up * Mathf.Cos(radians) - forward * Mathf.Sin(radians);
		}

		/// <summary>
		/// How far to raise (or, negative, lower) a swimmer at the surface so its head joint stands
		/// <see cref="HeadAboveWater"/> over the drawn water: the water's still line plus the wave there, against where the
		/// head joint is with the body turned but not raised (both world heights). Scaled by <paramref name="surfaceShare"/>,
		/// so a swimmer going under eases back onto its capsule.
		/// </summary>
		public static float HeadLift(float headAt, float stillWater, float wave, float surfaceShare)
		{
			if (float.IsNaN(stillWater) || float.IsInfinity(stillWater))
			{
				return 0f;
			}
			return (stillWater + wave + HeadAboveWater - headAt) * Mathf.Clamp01(surfaceShare);
		}
	}

	/// <summary>
	/// What can be sat on: a level top seat-high over the feet, deep enough to sit on. The World Sim walker sits by it (the
	/// game has no sitting yet).
	/// </summary>
	public static class SitRules
	{
		/// <summary>The lowest seat sat on rather than the ground, metres over the feet (a bench is 0.485).</summary>
		public const float MinSeatHeight = 0.38f;

		/// <summary>The highest seat: a stool, not a table (the tables are 0.72 and up).</summary>
		public const float MaxSeatHeight = 0.65f;

		/// <summary>How far round a sitter a seat's front edge is looked for, metres.</summary>
		public const float Reach = 0.8f;

		/// <summary>How deep a seat must be to sit on, metres back from its front edge.</summary>
		public const float Depth = 0.2f;

		/// <summary>How far the top may differ across that depth, metres: a seat is level, a slope is not.</summary>
		public const float LevelTolerance = 0.06f;

		/// <summary>How level a top must be to sit on (its normal's up component).</summary>
		public const float MinNormalUp = 0.85f;

		/// <summary>
		/// True when a top <paramref name="front"/> metres over the feet at its edge, <paramref name="back"/> metres over
		/// them <see cref="Depth"/> further in, with a surface whose normal points <paramref name="normalUp"/> up, is a seat.
		/// </summary>
		public static bool IsSeat(float front, float back, float normalUp)
		{
			return normalUp >= MinNormalUp
				&& front >= MinSeatHeight && front <= MaxSeatHeight
				&& back >= MinSeatHeight && back <= MaxSeatHeight
				&& Mathf.Abs(back - front) < LevelTolerance;
		}
	}
}
