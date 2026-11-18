using System;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// THE slot function of the shared motion clock's episodes (2026-10-25, I6i; decisions 93 and 95): the clock cut into slots of a
	/// length that divides its 10 000 s wrap, each slot holding one episode at a hashed moment, early enough that it ends in its slot
	/// (so a slot is read alone, and the strength is continuous in time and across the wrap), ramped in and out, perhaps skipped, at a
	/// hashed strength. Shared by the lava fountains' episodes and the glows that follow them (BiomeFountainEpisodes, Glow EPISODE), the
	/// Strombolian vents' bursts and theirs (<see cref="VentBursts"/>, Glow BURST), and the later fountain and vent effects, which call it
	/// with the same seeds. Mirrored in FishGlowModes.hlsl (FishGlowSlot), constant for constant.
	/// </summary>
	public static class EpisodeSlots
	{
		/// <summary>Salts for a slot's skip draw and its strength draw (after its start's).</summary>
		public const uint SkipSalt = 0x51ED27u, StrengthSalt = 0xB5297A4Du;

		/// <summary>
		/// An episode's strength at a moment of the motion clock (<paramref name="seconds"/>, wrapped at 10 000 s): 0 between
		/// episodes; inside one its ramps (<paramref name="ramp"/> seconds, at most half the episode) times its strength, a hashed share
		/// between <paramref name="weakest"/> and <paramref name="strongest"/>; 0 through a slot skipped (chance <paramref name="skip"/>).
		/// </summary>
		public static float At(float seconds, float length, float onSeconds, float ramp, uint seed, float skip = 0f, float weakest = 1f, float strongest = 1f)
		{
			if (!(length > 0f) || !(onSeconds > 0f))
			{
				return 0f;
			}
			float on = Math.Min(onSeconds, length);
			float slot = (float)Math.Floor(seconds / length);
			uint h = TurbidityEpisodes.Mix((uint)(int)slot, seed);
			float start = slot * length + (float)TurbidityEpisodes.Unit(h) * (length - on);
			float into = seconds - start;
			if (into <= 0f || into >= on)
			{
				return 0f;
			}
			if (skip > 0f && TurbidityEpisodes.Unit(TurbidityEpisodes.Mix(h, seed ^ SkipSalt)) < skip)
			{
				return 0f;
			}
			float strength = weakest == strongest ? weakest
				: weakest + (strongest - weakest) * (float)TurbidityEpisodes.Unit(TurbidityEpisodes.Mix(h, seed ^ StrengthSalt));
			float r = Math.Max(1e-3f, Math.Min(ramp, 0.5f * on));
			return Smooth(into / r) * Smooth((on - into) / r) * strength;
		}

		private static float Smooth(float t)
		{
			t = t < 0f ? 0f : t > 1f ? 1f : t;
			return t * t * (3f - 2f * t);
		}
	}

	/// <summary>
	/// Strombolian vent bursts (2026-10-25, I6i; decision 95, the Volcanic Temple round): each vent bursts once a slot of 625, 1000 or
	/// 1250 s (by its seed; all divide the 10 000 s wrap), a few seconds of bombs at a hashed strength, one slot in seven-ish skipped
	/// (15 %). <see cref="EpisodeSlots.At"/> with these constants, so the later vent effect (GPU bombs, the Sciara rockfall) and the glow
	/// that follows the scene's vent (Glow BURST, <c>_FishVentBurst</c>) read the same burst.
	/// </summary>
	public static class VentBursts
	{
		/// <summary>The vents' periods, seconds (each divides 10 000).</summary>
		public static readonly int[] Periods = { 625, 1000, 1250 };

		/// <summary>How long a burst throws, how long it takes to rise and to die, seconds; the chance a slot is skipped; the weakest share.</summary>
		public const float BurstSeconds = 8f, RampSeconds = 1.5f, Skip = 0.15f, Weakest = 0.35f;

		/// <summary>A vent's period by its seed.</summary>
		public static int PeriodOf(uint seed) => Periods[(int)(seed % (uint)Periods.Length)];

		/// <summary>A vent's burst at a moment of the motion clock (wrapped at 10 000 s), 0..1.</summary>
		public static float At(float seconds, uint seed) => EpisodeSlots.At(seconds, PeriodOf(seed), BurstSeconds, RampSeconds, seed, Skip, Weakest, 1f);

		/// <summary>The scene's own vent's seed (what <c>_FishVentBurst</c> follows): a stable hash of the scene's name.</summary>
		public static uint SceneSeed(string sceneName) => TurbidityEpisodes.SeedOf(sceneName) ^ 0x5701B0u;
	}
}
