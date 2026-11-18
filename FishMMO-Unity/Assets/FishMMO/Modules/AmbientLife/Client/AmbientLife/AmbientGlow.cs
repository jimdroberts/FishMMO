using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A creature's own light (2026-10-20: "Bioluminescent creatures should also exist in caves"; "more glow is good inside the
	/// caves"): when it shines and how brightly, as a pure function of the shared world clock and the animal's own seed, so every
	/// player sees the same millipede's wave run down its back and the same snail flash at the same moment.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Split between the CPU and the shader.</b> The time course is worked out here per animal, in double precision on the world
	/// clock (a float clock would lose the flashes' timing within hours): <see cref="At"/> is the instance's <c>_Glow</c> — x the
	/// pattern's phase in cycles, y its brightness now. What varies over the body (a wave running nose to tail, a ripple from a
	/// jelly's crown, each lamp twinkling on its own) is the shader's, from that phase and the mesh's emission mask
	/// (FishAmbientLife.hlsl, <c>_EmitPattern</c>); the colour and strength are the kind's (<c>_EmitColour</c>).
	/// </para>
	/// <para>
	/// <b>Real rhythms.</b> Luminous millipedes (Motyxia) glow steadily, a waved light here for life; glowing land snails (Quantula)
	/// give short flashes about a second apart in trains; click beetles' headlamps glow steadily and brighten when touched; a jelly's
	/// light rides its bell's beat. The swarms' lights (fireflies, fungus gnats, spore motes) are their own (the swarm mode, per insect).
	/// </para>
	/// </remarks>
	public static class AmbientGlow
	{
		/// <summary>The shader's spatial patterns (<c>_EmitPattern.x</c>).</summary>
		public const float Even = 0f, Wave = 1f, Twinkle = 2f;

		/// <summary>Whether a kind gives off a light of its own through the emission (a swarm's light is its swarm mode's).</summary>
		public static bool Glows(AmbientCreatureKind kind) =>
			kind != null && kind.Shape != AmbientShape.Swarm && kind.GlowParts != AmbientGlowParts.None && kind.GlowStrength > 0f;

		/// <summary>Whether a kind has any light of its own, an emission or a swarm's.</summary>
		public static bool Luminous(AmbientCreatureKind kind) =>
			Glows(kind) || (kind != null && kind.Shape == AmbientShape.Swarm && kind.Glow > 0f);

		/// <summary>One animal's own round of its pattern, seconds: the kind's ± 15 %. A jelly's ripple rides its stroke.</summary>
		public static double PeriodOf(AmbientCreatureKind kind, uint own) => PeriodOf(kind.GlowPattern, kind.GlowPeriod, kind.StrokeHz, own);

		/// <summary>
		/// One animal's own round of a pattern, seconds, from the pattern's numbers alone (2026-10-25: the sea creatures' lights
		/// share these rhythms, SeaLifePlacement): the period ± 15 %, or a ripple's stroke.
		/// </summary>
		public static double PeriodOf(AmbientGlowPattern pattern, float glowPeriod, float strokeHz, uint own)
		{
			if (pattern == AmbientGlowPattern.Ripple)
			{
				return 1.0 / Math.Max(0.1, strokeHz);
			}
			return Math.Max(0.2, glowPeriod) * (0.85 + 0.3 * Unit(Mix(own ^ 0x61u)));
		}

		/// <summary>
		/// Where an animal is in its pattern's round at a moment, 0..1. A jelly's is its stroke's (the same seed as its swim's
		/// phase, AmbientCaveLife), so the light runs out as the bell squeezes.
		/// </summary>
		public static float PhaseAt(AmbientCreatureKind kind, uint own, double seconds) => PhaseAt(kind.GlowPattern, PeriodOf(kind, own), own, seconds);

		/// <summary>Where an animal is in a pattern's round of <paramref name="period"/> seconds at a moment, 0..1.</summary>
		public static float PhaseAt(AmbientGlowPattern pattern, double period, uint own, double seconds)
		{
			float offset = Unit(Mix(own ^ (pattern == AmbientGlowPattern.Ripple ? 0x95u : 0x62u)));
			return SeaLifePlacement.Cycle(seconds, period, offset);
		}

		/// <summary>An animal's brightness at a moment, between the kind's <see cref="AmbientCreatureKind.GlowRest"/> and 1.</summary>
		public static float BrightnessAt(AmbientCreatureKind kind, uint own, double seconds) => BrightnessAt(kind.GlowPattern, kind.GlowRest, PeriodOf(kind, own), own, seconds);

		/// <summary>An animal's brightness at a moment, between <paramref name="glowRest"/> and 1, from a pattern's numbers alone.</summary>
		public static float BrightnessAt(AmbientGlowPattern pattern, float glowRest, double period, uint own, double seconds)
		{
			float rest = Mathf.Clamp01(glowRest);
			switch (pattern)
			{
				case AmbientGlowPattern.Pulse:
				{
					// A slow breath: half a cosine up and down.
					float u = PhaseAt(pattern, period, own, seconds);
					float s = 0.5f - 0.5f * Mathf.Cos(u * 2f * Mathf.PI);
					return Mathf.Lerp(rest, 1f, s * s * (3f - 2f * s));
				}
				case AmbientGlowPattern.FlashTrain:
					return Mathf.Lerp(rest, 1f, Train(own, seconds, period));
				case AmbientGlowPattern.Ripple:
				{
					// A little brighter as the bell squeezes (the shader runs the light out over it).
					float u = PhaseAt(pattern, period, own, seconds);
					return Mathf.Lerp(Mathf.Max(rest, 0.6f), 1f, Squeeze(u));
				}
				case AmbientGlowPattern.Flicker:
				{
					// Three incommensurate wavers on the animal's own phases; their sum never repeats in any span that matters.
					double w = 2.0 * Math.PI / period;
					double a = Math.Sin(seconds * w + 6.2831853 * Unit(Mix(own ^ 0x63u)));
					double b = Math.Sin(seconds * w * 2.37 + 6.2831853 * Unit(Mix(own ^ 0x64u)));
					double c = Math.Sin(seconds * w * 5.13 + 6.2831853 * Unit(Mix(own ^ 0x65u)));
					float n = (float)(a + 0.6 * b + 0.35 * c) / 1.95f;
					return Mathf.Lerp(rest, 1f, Mathf.Clamp01(0.5f + 0.5f * n));
				}
				default:
				{
					// Wave and twinkle: the body's whole light steady, breathing a little over a few of its rounds.
					float u = SeaLifePlacement.Cycle(seconds, period * 3.7, Unit(Mix(own ^ 0x66u)));
					return 0.85f + 0.15f * (0.5f + 0.5f * Mathf.Sin(u * 2f * Mathf.PI));
				}
			}
		}

		/// <summary>
		/// A train of flashes, 0..1: each round of <paramref name="period"/> opens with two to four short flashes (a fifth of a second,
		/// about two thirds of a second apart; how many is hashed from the round), then it is dark till the next.
		/// </summary>
		public static float Train(uint own, double seconds, double period)
		{
			double c = seconds / Math.Max(0.2, period) + Unit(Mix(own ^ 0x67u));
			long round = (long)Math.Floor(c);
			float into = (float)((c - round) * period);
			int flashes = 2 + (int)(Unit(Mix(own ^ (uint)round * 0x9E3779B9u ^ 0x68u)) * 3f) % 3;
			const float apart = 0.65f, rise = 0.06f, fall = 0.16f;
			float best = 0f;
			for (int i = 0; i < flashes; i++)
			{
				float d = into - (0.1f + i * apart);
				if (d < -rise || d > fall)
				{
					continue;
				}
				float s = d < 0f ? 1f + d / rise : 1f - d / fall;
				best = Mathf.Max(best, s * s * (3f - 2f * s));
			}
			return best;
		}

		/// <summary>A jelly's bell squeeze over its stroke, 0..1: a quick squeeze (a fifth of the beat), a slow relaxing (the shader's own).</summary>
		public static float Squeeze(float phase)
		{
			float f = phase - Mathf.Floor(phase);
			return Mathf.SmoothStep(0f, 1f, f / 0.2f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - 0.2f) / 0.7f)));
		}

		/// <summary>The instance's <c>_Glow</c>: x the pattern's phase, y the brightness now; zero for a kind without a light.</summary>
		public static Vector4 At(AmbientCreatureKind kind, uint own, double seconds)
		{
			if (!Glows(kind))
			{
				return Vector4.zero;
			}
			return new Vector4(PhaseAt(kind, own, seconds), BrightnessAt(kind, own, seconds), 0f, 0f);
		}

		/// <summary>The material's <c>_EmitColour</c>: the kind's light at its brightest (linear, HDR), a 1 when it has one.</summary>
		public static Vector4 EmitColour(AmbientCreatureKind kind)
		{
			if (!Glows(kind))
			{
				return Vector4.zero;
			}
			Color c = kind.GlowColour.linear * kind.GlowStrength;
			return new Vector4(c.r, c.g, c.b, 1f);
		}

		/// <summary>
		/// The material's <c>_EmitPattern</c>: x the spatial pattern (<see cref="Even"/>, <see cref="Wave"/> along the body's
		/// 'along', <see cref="Twinkle"/> per lamp), y crests along the body at once, z how sharp a crest is, w the share lit
		/// between crests (or between a lamp's twinkles).
		/// </summary>
		public static Vector4 Pattern(AmbientCreatureKind kind)
		{
			if (!Glows(kind))
			{
				return Vector4.zero;
			}
			switch (kind.GlowPattern)
			{
				// A millipede's two or three waves at once; a fish's lateral line one and a half.
				case AmbientGlowPattern.Wave:
					return new Vector4(Wave, kind.Shape == AmbientShape.Millipede ? 2.5f : 1.5f, 3f, 0.35f);
				// A jelly's ring of light running out from the crown, one at a time.
				case AmbientGlowPattern.Ripple:
					return new Vector4(Wave, 1f, 4f, 0.3f);
				case AmbientGlowPattern.Twinkle:
					return new Vector4(Twinkle, 1f, 1f, 0.35f);
				default:
					return new Vector4(Even, 1f, 1f, 1f);
			}
		}

		private static uint Mix(uint x) => SeaLifePlacement.Mix(x);
		private static float Unit(uint h) => SeaLifePlacement.Unit(h);
	}
}
