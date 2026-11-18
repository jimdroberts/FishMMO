using System;
using FishMMO.Shared;
using static FishMMO.Client.SoundSynth;

namespace FishMMO.Client
{
	/// <summary>
	/// A cave's drips, synthesised (2026-10-20, caves livened): a drop into its puddle, a drop on wet rock or on the stalagmite it
	/// has built. Each is a recipe over <see cref="SoundSynth"/> run from a seed, so a variant is the same sound on every client.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Into water: the plink.</b> What is heard of a drop falling into still water is barely its impact (a faint tick of a few
	/// milliseconds) and mostly the small bubble it pinches off a millisecond or two later: the bubble rings at its Minnaert
	/// frequency (about 3.3 kHz over its radius in millimetres, so 1–3 kHz for a drop's 1–3 mm bubble) and its pitch climbs as it
	/// rises and shrinks against the surface, dying within a tenth of a second. Not every drop traps one: some only plop, a duller
	/// knock and a low short ring of the water's surface.
	/// </para>
	/// <para>
	/// <b>On rock: the splat.</b> A drop on wet rock is a short bright burst of a water film breaking (2–6 kHz, under a hundredth of
	/// a second) and its spatter landing round it a few tens of milliseconds after, softer.
	/// </para>
	/// <para>
	/// The cave's ring is not in the clip: the drips play through the world's sound (WorldAudio's Effects), where the cave's reverb
	/// round the listener and the echoes off its far walls (AcousticEnvironment) give each its tail.
	/// </para>
	/// </remarks>
	public static class DripSounds
	{
		/// <summary>Variants of each sound.</summary>
		public const int Variants = 8;

		/// <summary>The clip's key: bump the version when a recipe changes.</summary>
		public static string Key(SceneDripLanding landing, int variant) => $"Drip.v1.{(landing == SceneDripLanding.Puddle ? "Water" : "Rock")}.{variant}";

		/// <summary>A drop landing as <paramref name="landing"/> says, variant <paramref name="variant"/>.</summary>
		public static float[] Drip(SceneDripLanding landing, int variant)
		{
			return landing == SceneDripLanding.Puddle ? IntoWater(variant) : OnRock(variant);
		}

		/// <summary>A drop into a puddle: a faint tick, then the bubble's rising plink (or, a quarter of the time, a plop).</summary>
		public static float[] IntoWater(int variant)
		{
			var noise = new Noise(Hash(0xD819u, (uint)variant));
			var data = new float[(int)(0.24f * Rate)];
			// The drop's own impact: a few milliseconds of bright noise, faint.
			AddBurst(data, 0, ref noise, noise.Range(3500f, 6000f), 1.2f, 0.12f, 0.004f, Rate);
			bool bubble = noise.Next() < 0.75f;
			int pinch = (int)(noise.Range(0.0008f, 0.0025f) * Rate);
			if (bubble)
			{
				// The bubble: 1–3 mm, ringing at its Minnaert frequency, its pitch rising by half again or more as it shrinks.
				float radiusMm = noise.LogRange(1.1f, 3f);
				float frequency = 3260f / radiusMm;
				AddChirp(data, pinch, frequency, noise.Range(0.4f, 1.1f), 1f, noise.Range(0.05f, 0.11f), Rate);
				// Now and then a second, smaller bubble just after.
				if (noise.Next() < 0.3f)
				{
					AddChirp(data, pinch + (int)(noise.Range(0.004f, 0.012f) * Rate), frequency * noise.Range(1.3f, 1.8f), noise.Range(0.3f, 0.8f), 0.35f, noise.Range(0.03f, 0.06f), Rate);
				}
			}
			else
			{
				// A plop: the crater closing, duller, and the surface's low short ring.
				AddBurst(data, pinch, ref noise, noise.Range(900f, 1600f), 1.5f, 0.6f, 0.012f, Rate, 0.001f);
				AddRing(data, pinch, noise.Range(320f, 520f), 0.35f, noise.Range(0.03f, 0.06f), Rate);
			}
			return Finish(data);
		}

		/// <summary>A drop on wet rock (or a stalagmite's wet tip): the film's bright splat and its spatter landing round it.</summary>
		public static float[] OnRock(int variant)
		{
			var noise = new Noise(Hash(0xD8A5u, (uint)variant));
			var data = new float[(int)(0.16f * Rate)];
			AddBurst(data, 0, ref noise, noise.Range(2200f, 4200f), 1.4f, 1f, noise.Range(0.005f, 0.009f), Rate, 0.0003f);
			// The thin water on the rock rings an instant.
			AddRing(data, 0, noise.Range(1500f, 2600f), 0.25f, noise.Range(0.012f, 0.02f), Rate);
			int droplets = 2 + (int)(noise.Next() * 4f);
			for (int i = 0; i < droplets; i++)
			{
				int at = (int)(noise.Range(0.015f, 0.07f) * Rate);
				AddBurst(data, at, ref noise, noise.Range(3500f, 7000f), 1.8f, noise.Range(0.08f, 0.22f), 0.003f, Rate, 0.0002f);
			}
			return Finish(data);
		}

		private static float[] Finish(float[] data)
		{
			RemoveDc(data);
			FadeEnds(data, 8, (int)(0.03f * Rate));
			Normalize(data, 0.9f);
			return data;
		}
	}
}
