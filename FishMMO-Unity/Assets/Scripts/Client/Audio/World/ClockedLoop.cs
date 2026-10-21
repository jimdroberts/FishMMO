using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Client
{
	/// <summary>
	/// Keeps a looping source at the place in its loop the shared world clock says (<see cref="WorldMotion.Seconds"/>),
	/// so a loop is the same moment of sound for every player: the same gust in the wind, the same patter in the rain.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The audio clock and the world clock run at the same rate but are not the same clock: the audio's own drifts from
	/// real time by a little, and the world's leans by up to a quarter onto the server's estimate after a hitch. So the
	/// loop is started where the clock says, then left to play, and only put back when it has wandered further than
	/// <see cref="ToleranceSeconds"/> — a jump that rare is never heard, where a constant correction would be.
	/// </para>
	/// <para>
	/// While the world's clock is held (an admin, a test bed), the sound plays on: the world stands still, the air does not
	/// go silent.
	/// </para>
	/// </remarks>
	public static class ClockedLoop
	{
		/// <summary>How far, seconds, a loop may wander from the clock before it is put back.</summary>
		public const float ToleranceSeconds = 0.25f;

		/// <summary>The sample a clip of this length should be at for the clock now.</summary>
		public static int SampleAt(AudioClip clip, double seconds)
		{
			if (clip == null || clip.samples <= 0)
			{
				return 0;
			}
			double samples = seconds * clip.frequency;
			return (int)WorldMotion.Repeat(samples, clip.samples);
		}

		/// <summary>Starts a looping source at the clock's place in its clip.</summary>
		public static void Start(AudioSource source)
		{
			if (source == null || source.clip == null)
			{
				return;
			}
			source.loop = true;
			source.timeSamples = SampleAt(source.clip, WorldMotion.Seconds);
			source.Play();
		}

		/// <summary>Puts a playing loop back on the clock if it has wandered too far from it.</summary>
		public static void Keep(AudioSource source)
		{
			if (source == null || source.clip == null || !source.isPlaying || WorldMotion.Pace < 0.99f)
			{
				return;
			}
			AudioClip clip = source.clip;
			int want = SampleAt(clip, WorldMotion.Seconds);
			int off = Mathf.Abs(source.timeSamples - want);
			// Round the loop: the end is next to the start.
			off = Mathf.Min(off, clip.samples - off);
			if (off > ToleranceSeconds * clip.frequency)
			{
				source.timeSamples = want;
			}
		}
	}
}
