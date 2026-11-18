using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>The cool tints a <see cref="SceneEmitterKind.Glow"/> is made in (bits 8–10 of its seed). Append only: stored in scene assets.</summary>
	public enum SceneGlowHue : byte
	{
		/// <summary>A crystal's violet (the <see cref="SceneEmitterKind.Crystal"/> hues 0–3 keep their numbers here).</summary>
		Violet = 0,
		Blue = 1,
		Green = 2,
		Amber = 3,
		/// <summary>Rune-light: a pale blue-white, the glow in a rune stone's cut runes.</summary>
		Rune = 4,
		/// <summary>Faerie-light: a soft green-gold, the mote in a glowjar.</summary>
		Faerie = 5,
	}

	/// <summary>
	/// The cold glow of faerie, rune and crystal light (2026-10-18, I6g; <see cref="SceneEmitterKind.Glow"/>): its tint, its
	/// soft slow pulse and when it shows. One definition read by the client's site lights (SiteLightPool) and, constant for
	/// constant, by the glowing parts of the pieces that give it off (FishStructureWeathering.hlsl FishStructureGlowLight),
	/// so a rune stone's runes brighten and dim with the light they cast.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shared by every player.</b> The pulse is a pure function of the shared motion clock (WorldMotion, wrapped at
	/// <see cref="WrapSeconds"/> as the shaders' _FishWeatherMisc.w is) and the glow's place: no per-client state, so two
	/// players side by side see the same rune brighten at the same instant. Each beat's rate is a whole number of cycles in the
	/// wrap, so the pulse meets itself across it instead of jumping. Its phase is a smooth field over the ground (two slow
	/// waves), not a hash, so a light and the stone it stands in front of — half a metre apart — pulse as one, while glows down
	/// a street breathe one after another.
	/// </para>
	/// <para>
	/// <b>When.</b> Lamps light at dusk (SkySystem's _FishNightLights, as the sun goes below 7°); a glow already shows, faintly,
	/// from the sun at 19° (<see cref="Visibility"/>, published as _FishGlowLights), and is full in the night. Underground it
	/// is full day and night, as a buried lamp is.
	/// </para>
	/// <para>
	/// <b>Flicker</b> (2026-10-18, decision 33: stormglass, a crystal full of cloud with threads of lightning in it). A glow
	/// whose seed carries <see cref="FlickerBit"/> crackles instead of breathing (<see cref="Flicker"/>): a low steady glow
	/// with bright, uneven flashes, the same pure function of the shared clock and the place, so the crystal on a post and the
	/// light it casts flash together while the next post's flash at their own moments. Its parts' material flickers by the
	/// same constants (FishStructureWeathering.hlsl FishGlowFlicker).
	/// </para>
	/// </remarks>
	public static class SceneGlow
	{
		/// <summary>The motion clock's wrap the pulse is read on, seconds (WorldMotion.ShaderWrapSeconds; the shaders' FISH_MOTION_WRAP).</summary>
		public const double WrapSeconds = Celestial.WorldMotion.ShaderWrapSeconds;

		/// <summary>The pulse's two beats, cycles a second: about nine seconds and about twenty-three a breath.</summary>
		public const float SlowHz = 0.11f, SlowerHz = 0.043f;

		/// <summary>The pulse's phase over the ground: cycles a metre along world x and z, for each beat.</summary>
		public const float SlowWaveX = 0.037f, SlowWaveZ = 0.029f, SlowerWaveX = -0.021f, SlowerWaveZ = 0.033f;

		/// <summary>The pulse: its middle and each beat's depth (it runs between 0.56 and 1).</summary>
		public const float PulseMiddle = 0.78f, SlowDepth = 0.14f, SlowerDepth = 0.08f;

		/// <summary>The sun's height (sine of its elevation) where a glow begins to show, where it is a third lit (lamps begin), and where it is full.</summary>
		public const float FirstShows = 0.32f, LampsLight = 0.12f, FullNight = -0.04f;

		/// <summary>How much of the glow shows by the time the lamps light.</summary>
		public const float DuskShare = 0.3f;

		/// <summary>A glow's light: its brightness at the top of its pulse, and its reach (when its emitter names none), metres.</summary>
		public const float Intensity = 1.3f, Range = 6f;

		/// <summary>The hue bits of a glow's seed.</summary>
		private const uint HueMask = 0x700u;

		/// <summary>The seed bit of a glow that flickers (<see cref="Flicker"/>) instead of pulsing (bit 11).</summary>
		public const uint FlickerBit = 0x800u;

		/// <summary>
		/// The flicker's waves, cycles a second: three crackles whose product flashes when they meet, and a fast shiver in the
		/// flash. Each a whole number of cycles in the wrap (tenths of a hertz in 10 000 s).
		/// </summary>
		public const float CrackleAHz = 2.3f, CrackleBHz = 3.7f, CrackleCHz = 0.7f, ShiverHz = 11.3f;

		/// <summary>The flicker's phase over the ground, cycles a metre (x, z) for each crackle: posts a street apart flash apart.</summary>
		public const float CrackleAX = 0.13f, CrackleAZ = 0.17f, CrackleBX = -0.11f, CrackleBZ = 0.07f, CrackleCX = 0.05f, CrackleCZ = -0.09f;

		/// <summary>
		/// The flicker: its steady glow between flashes, how much a full flash adds, where the crackles' product begins to flash
		/// (it runs between <see cref="FlickerBase"/> and 1), and how much of the flash shivers.
		/// </summary>
		public const float FlickerBase = 0.45f, FlashDepth = 0.55f, FlashFrom = 0.3f, ShiverDepth = 0.3f;

		/// <summary>A glow's tint (an emitter's seed bits 8–10); 6 and 7 read as rune-light.</summary>
		public static SceneGlowHue HueOf(uint seed)
		{
			uint hue = (seed & HueMask) >> 8;
			return hue > (uint)SceneGlowHue.Faerie ? SceneGlowHue.Rune : (SceneGlowHue)hue;
		}

		/// <summary>A seed carrying a glow's tint in bits 8–10, the rest of it kept.</summary>
		public static uint WithHue(uint seed, SceneGlowHue hue) => (seed & ~HueMask) | ((uint)hue << 8);

		/// <summary>
		/// A seed carrying a crystal's light (<paramref name="crystalHue"/> 0 violet, 1 blue, 2 green, 3 amber, as a
		/// <see cref="SceneEmitterKind.Crystal"/>'s), or for −1 one of the four by the seed itself (bits 8–9 kept, bit 10 cleared).
		/// </summary>
		public static uint WithCrystalHue(uint seed, int crystalHue) => crystalHue < 0 ? seed & ~0x400u : WithHue(seed, (SceneGlowHue)(crystalHue & 3));

		/// <summary>Whether a glow's seed asks for the flicker (<see cref="FlickerBit"/>) rather than the pulse.</summary>
		public static bool Flickers(uint seed) => (seed & FlickerBit) != 0u;

		/// <summary>A seed that flickers or pulses as <paramref name="flickers"/> says, the rest of it kept (the bit always set or cleared, never the hash's).</summary>
		public static uint WithFlicker(uint seed, bool flickers) => flickers ? seed | FlickerBit : seed & ~FlickerBit;

		/// <summary>A tint's colour: the crystals' as their cave lights are; rune-light blue-white; faerie-light green-gold.</summary>
		public static Color Colour(SceneGlowHue hue)
		{
			switch (hue)
			{
				case SceneGlowHue.Violet: return new Color(0.62f, 0.4f, 1f);
				case SceneGlowHue.Green: return new Color(0.4f, 1f, 0.7f);
				case SceneGlowHue.Amber: return new Color(1f, 0.68f, 0.32f);
				case SceneGlowHue.Rune: return new Color(0.72f, 0.86f, 1f);
				case SceneGlowHue.Faerie: return new Color(0.8f, 1f, 0.42f);
				default: return new Color(0.35f, 0.62f, 1f);
			}
		}

		/// <summary>
		/// The pulse at a moment of the shared motion clock (<paramref name="seconds"/>, wrapped at <see cref="WrapSeconds"/>)
		/// for a glow at world (x, z): between 0.56 and 1, smooth in both time and place.
		/// </summary>
		public static float Pulse(float seconds, float x, float z)
		{
			float slow = Fraction(SlowHz * seconds + Fraction(x * SlowWaveX + z * SlowWaveZ));
			float slower = Fraction(SlowerHz * seconds + Fraction(x * SlowerWaveX + z * SlowerWaveZ));
			const float Turn = 2f * Mathf.PI;
			return PulseMiddle + SlowDepth * (float)Math.Sin(Turn * slow) + SlowerDepth * (float)Math.Sin(Turn * slower);
		}

		/// <summary>
		/// The flicker at a moment of the shared motion clock (wrapped at <see cref="WrapSeconds"/>) for a glow at world (x, z):
		/// <see cref="FlickerBase"/> between flashes, up to 1 at a flash's height. Three crackles multiplied flash only where they
		/// meet (their product past <see cref="FlashFrom"/>), unevenly, a few times a second at most and often not for seconds.
		/// </summary>
		public static float Flicker(float seconds, float x, float z)
		{
			const float Turn = 2f * Mathf.PI;
			float a = (float)Math.Sin(Turn * Fraction(CrackleAHz * seconds + Fraction(x * CrackleAX + z * CrackleAZ)));
			float b = (float)Math.Sin(Turn * Fraction(CrackleBHz * seconds + Fraction(x * CrackleBX + z * CrackleBZ)));
			float c = (float)Math.Sin(Turn * Fraction(CrackleCHz * seconds + Fraction(x * CrackleCX + z * CrackleCZ)));
			float flash = Clamp01((a * b * c - FlashFrom) / (1f - FlashFrom));
			float shiver = 0.5f + 0.5f * (float)Math.Sin(Turn * Fraction(ShiverHz * seconds + Fraction(x * CrackleAX + z * CrackleAZ)));
			return FlickerBase + FlashDepth * flash * flash * (1f - ShiverDepth + ShiverDepth * shiver);
		}

		/// <summary>A glow's wave at a moment: the flicker for a glow that flickers, else the pulse.</summary>
		public static float Wave(float seconds, Vector3 at, bool flickers) => flickers ? Flicker(seconds, at.x, at.z) : Pulse(seconds, at.x, at.z);

		/// <summary>
		/// How much of a glow shows with the main sun at <paramref name="sunHeight"/> (the sine of its elevation): none by day,
		/// <see cref="DuskShare"/> by the time the lamps light, all of it in the night.
		/// </summary>
		public static float Visibility(float sunHeight)
		{
			float dusk = 1f - Smooth((sunHeight - LampsLight) / (FirstShows - LampsLight));
			float night = 1f - Smooth((sunHeight - FullNight) / (LampsLight - FullNight));
			return DuskShare * dusk + (1f - DuskShare) * night;
		}

		/// <summary>
		/// A glow's light at a moment: <see cref="Intensity"/> × its pulse (its flicker when <paramref name="flickers"/>) × how
		/// much shows (all of it underground).
		/// </summary>
		public static float Light(float seconds, Vector3 at, float visibility, bool underground, bool flickers = false)
		{
			return Intensity * Wave(seconds, at, flickers) * (underground ? 1f : Clamp01(visibility));
		}

		private static float Fraction(float v) => v - (float)Math.Floor(v);

		private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

		private static float Smooth(float t)
		{
			t = t < 0f ? 0f : t > 1f ? 1f : t;
			return t * t * (3f - 2f * t);
		}
	}
}
