using System;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// How fast the world's small-scale motion runs against the wall clock: the sea's waves, the surf
	/// on a beach, rain and snow falling, the trees in the wind.
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
	/// watch a day go by, and waves at that pace are a blur, not a faster sea. So a clock running at
	/// or above real time runs this at 1, a slower one slows it, and a stopped one stops it.
	/// </para>
	/// <para>
	/// The game leaves it at 1: only a preview has any reason to stop the world. It is put back to 1
	/// whenever play starts, so a preview that stopped it cannot leave a later session's sea frozen.
	/// </para>
	/// </remarks>
	public static class WorldMotion
	{
		private static float rate = 1f;

		/// <summary>Seconds of motion per real second, 0 to 1.</summary>
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
