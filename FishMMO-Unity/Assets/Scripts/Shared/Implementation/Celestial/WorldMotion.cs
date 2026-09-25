using System;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// How fast the world's own motion runs against the wall clock: the sea's waves, the surf on a
	/// beach, rain and snow falling, the trees in the wind.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One dial, so time stops together.</b> The sky and the clouds run off the world clock, but
	/// the motion under them each kept a real-time clock of its own — so a preview that stopped the
	/// world clock went on surfing and snowing under a frozen sky. Everything that moves by itself
	/// advances by this instead of by the wall clock.
	/// </para>
	/// <para>
	/// <b>Never faster than real time.</b> A preview runs the world clock hundreds of times faster to
	/// watch a day go by, and the sky and the clouds keep up with it. The motion down here does not:
	/// stopped when the clock stops and slowed when it slows, but held to real time when it races.
	/// The sea was let follow the clock (2026-09-25) and it could not be made to look right: at the
	/// sky's hundred and eighty times a four-second wave comes forty times a second, and even at
	/// twelve times the sea looked wrong. Waves are motion everyone knows by eye; a sped-up cloud is
	/// just a cloud. What the sea IS still follows the clock — its wind, its waves' height and period,
	/// its tide are the sky's weather at the sky's time — only how fast it moves does not.
	/// </para>
	/// <para>
	/// The game leaves both at 1: only a preview has any reason to change the world's pace. They are
	/// put back to 1 whenever play starts, so a preview that stopped them cannot leave a later
	/// session's sea frozen.
	/// </para>
	/// </remarks>
	public static class WorldMotion
	{
		private static float rate = 1f;

		/// <summary>Seconds of motion per real second, 0 to 1: the sea and its surf, what falls, and the trees.</summary>
		public static float Rate
		{
			get => rate;
			set => rate = Mathf.Clamp01(value);
		}

		/// <summary>Follows a world clock running at this many world seconds per real second.</summary>
		public static void FollowClock(double worldSecondsPerSecond)
		{
			Rate = (float)Math.Max(0.0, Math.Min(1.0, worldSecondsPerSecond));
		}

		/// <summary>How many seconds of motion a stretch of real time is worth.</summary>
		public static float Scale(float realSeconds) => realSeconds * rate;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnPlay()
		{
			rate = 1f;
		}
	}
}
