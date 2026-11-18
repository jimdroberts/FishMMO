using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>What body of molten lava a <see cref="SceneEmitterKind.Magma"/> stands over (bits 24–25 of its seed). Append only: stored in scene assets.</summary>
	/// <remarks>The same ordinals as the cave dressing's lava kinds (CaveLavaKind: River, Lake, Fall); a test keeps them so.</remarks>
	public enum SceneMagmaBody : byte
	{
		/// <summary>A river of lava down a tube's floor.</summary>
		River = 0,
		/// <summary>A lake or pit of lava in a chamber.</summary>
		Lake = 1,
		/// <summary>A fall of lava between levels: it churns faster and throws more sparks.</summary>
		Fall = 2,
	}

	/// <summary>
	/// The molten lava of a lava tube that still runs (2026-10-20; <see cref="SceneEmitterKind.Magma"/> and
	/// <see cref="SceneEmitterKind.Fumes"/>): its light's colour and slow churn, how its seed carries the body under it, how
	/// much it sparks, the hot air over it and the haze under the roof. One definition read by the client's site lights
	/// (SiteLightPool), its embers and fumes (SiteEmbers), its heat haze (the heat shimmer) and the cave walk render.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shared by every player.</b> The churn is a pure function of the shared motion clock (WorldMotion, wrapped at
	/// <see cref="WrapSeconds"/> as the shaders' _FishWeatherMisc.w is) and the light's place, each beat a whole number of
	/// cycles in the wrap, as <see cref="SceneGlow"/>'s pulse is: two players beside one river see it surge together.
	/// </para>
	/// <para>
	/// <b>The colour</b> is the light of lava's own glow: a crust's cracks at 1000–1150 °C shine a deep orange (blackbody
	/// ~1300 K, little blue), a fall's torn, fresher surface a little yellower. Underground no sky outshines it, so it burns
	/// the same by day as by night.
	/// </para>
	/// </remarks>
	public static class SceneMagma
	{
		/// <summary>The motion clock's wrap the churn is read on, seconds (WorldMotion.ShaderWrapSeconds; the shaders' FISH_MOTION_WRAP).</summary>
		public const double WrapSeconds = Celestial.WorldMotion.ShaderWrapSeconds;

		/// <summary>
		/// A lava light's brightness at the middle of its churn, a narrow river's (<see cref="Brightness"/> scales it with the body),
		/// and its reach when its emitter names none, metres.
		/// </summary>
		/// <remarks>
		/// 2026-10-20 (first render: "the lava barely lights its surroundings"): molten rock at ~1100 °C floods a tube with light,
		/// far more than a campfire (Fire 3.2): a river's light starts at 6 and grows with its width, a lake's and a fall's further.
		/// </remarks>
		public const float Intensity = 6f, Range = 16f;

		/// <summary>How much brighter each metre of a body's half-width (a lake's radius) makes its light, and the most it reaches, by body.</summary>
		public const float RiverPerMetre = 4f, RiverMost = 16f, LakeBase = 10f, LakePerMetre = 3f, LakeMost = 36f, FallBase = 12f, FallPerMetre = 4f, FallMost = 24f;

		/// <summary>
		/// The lava lights that win a place in a pool of lights first (SiteLightPool, the cave walk render): the nearest
		/// <see cref="PriorityLights"/> within <see cref="PriorityReach"/> of the eye, ahead of every other light however near.
		/// </summary>
		public const int PriorityLights = 12;
		public const float PriorityReach = 50f;

		/// <summary>The churn's beats, cycles a second (each whole in the 10 000 s wrap): a slow surge, a quicker roll, a fall's fast spatter.</summary>
		public const float SurgeHz = 0.07f, RollHz = 0.19f, SpatterHz = 1.3f;

		/// <summary>The churn: its middle and each beat's depth (a river's and a lake's runs between 0.85 and 1.15; a fall's to 1.2).</summary>
		public const float ChurnMiddle = 1f, SurgeDepth = 0.09f, RollDepth = 0.06f, SpatterDepth = 0.05f;

		/// <summary>The churn's phase over the ground, cycles a metre along world x and z: lights down a river surge one after another.</summary>
		public const float WaveX = 0.031f, WaveZ = 0.023f;

		/// <summary>The bits of a seed that carry the body's half-width (decimetres, 0–25.5 m) and its kind.</summary>
		public const int HalfWidthShift = 16, BodyShift = 24;
		private const uint HalfWidthMask = 0xFFu << HalfWidthShift, BodyMask = 0x3u << BodyShift;

		/// <summary>The share of an ember stream alive over each body (river, lake, fall): a fall throws the most.</summary>
		public const float RiverSparks = 0.55f, LakeSparks = 0.7f, FallSparks = 1f;

		/// <summary>The hot air over lava as the heat shimmer sees it: a ball this far over the surface, the body's half-width and this much more round it, never less than <see cref="HeatLeast"/>, metres.</summary>
		public const float HeatRise = 1.2f, HeatMargin = 1.5f, HeatLeast = 2.5f, HeatMost = 9f;

		/// <summary>The fumes' own colour, a pale sulphur-yellow haze, before the lava's light under it.</summary>
		public static readonly Color FumeColour = new Color(0.82f, 0.72f, 0.45f, 1f);

		/// <summary>A light's seed: <paramref name="hash"/>'s low 16 bits (its own phase), the body's half-width and its kind.</summary>
		public static uint Seed(uint hash, float halfWidth, SceneMagmaBody body)
		{
			uint dm = (uint)Mathf.Clamp(Mathf.RoundToInt(halfWidth * 10f), 0, 255);
			return (hash & 0xFFFFu) | (dm << HalfWidthShift) | (((uint)body & 3u) << BodyShift);
		}

		/// <summary>The half-width (a lake's radius) of the body a seed was made for, metres.</summary>
		public static float HalfWidthOf(uint seed) => ((seed & HalfWidthMask) >> HalfWidthShift) * 0.1f;

		/// <summary>The kind of body a seed was made for (3 reads as a river).</summary>
		public static SceneMagmaBody BodyOf(uint seed)
		{
			uint body = (seed & BodyMask) >> BodyShift;
			return body > (uint)SceneMagmaBody.Fall ? SceneMagmaBody.River : (SceneMagmaBody)body;
		}

		/// <summary>The light's colour: a deep blackbody orange, a fall's a little yellower.</summary>
		public static Color Colour(uint seed) => BodyOf(seed) == SceneMagmaBody.Fall ? new Color(1f, 0.54f, 0.2f) : new Color(1f, 0.44f, 0.15f);

		/// <summary>
		/// The churn at a moment of the shared motion clock (<paramref name="seconds"/>, wrapped at <see cref="WrapSeconds"/>) for a
		/// light at world (x, z): a slow surge and a quicker roll, a fall's spatter over them; smooth in time and place.
		/// </summary>
		public static float Churn(float seconds, float x, float z, SceneMagmaBody body)
		{
			const float Turn = 2f * Mathf.PI;
			float place = Fraction(x * WaveX + z * WaveZ);
			float surge = (float)Math.Sin(Turn * Fraction(SurgeHz * seconds + place));
			float roll = (float)Math.Sin(Turn * Fraction(RollHz * seconds + 2f * place + 0.37f));
			float churn = ChurnMiddle + SurgeDepth * surge + RollDepth * roll;
			if (body == SceneMagmaBody.Fall)
			{
				churn += SpatterDepth * (float)Math.Sin(Turn * Fraction(SpatterHz * seconds + 3f * place));
			}
			return churn;
		}

		/// <summary>
		/// A lava light's brightness at the middle of its churn, by its body and width (its seed): a river's 6 and 4 more a metre of
		/// half-width (to 16), a lake's 10 and 3 more a metre of radius (to 36), a fall's 12 and 4 more a metre (to 24).
		/// </summary>
		public static float Brightness(uint seed)
		{
			float half = HalfWidthOf(seed);
			switch (BodyOf(seed))
			{
				case SceneMagmaBody.Lake: return Mathf.Min(LakeMost, LakeBase + LakePerMetre * half);
				case SceneMagmaBody.Fall: return Mathf.Min(FallMost, FallBase + FallPerMetre * half);
				default: return Mathf.Min(RiverMost, Intensity + RiverPerMetre * half);
			}
		}

		/// <summary>How far a lava light over a body reaches, metres: a river's 12 + 3 a metre of half-width (14–22), a lake's 16 + 2.5 a metre of radius (18–32), a fall's 18 + 3 a metre (20–26).</summary>
		public static float ReachFor(SceneMagmaBody body, float half)
		{
			switch (body)
			{
				case SceneMagmaBody.Lake: return Mathf.Clamp(16f + 2.5f * half, 18f, 32f);
				case SceneMagmaBody.Fall: return Mathf.Clamp(18f + 3f * half, 20f, 26f);
				default: return Mathf.Clamp(12f + 3f * half, 14f, 22f);
			}
		}

		/// <summary>How far over the molten surface a lava light stands, metres: a lake's higher the wider it is (to fill its chamber), a fall's a metre.</summary>
		public static float RiseFor(SceneMagmaBody body, float half)
		{
			switch (body)
			{
				case SceneMagmaBody.Lake: return Mathf.Min(2.5f, 0.6f + 0.25f * half);
				case SceneMagmaBody.Fall: return 1f;
				default: return 0.8f;
			}
		}

		/// <summary>A lava light's intensity at a moment: its <see cref="Brightness"/> × its churn, day and night alike.</summary>
		public static float Light(float seconds, Vector3 at, uint seed) => Brightness(seed) * Churn(seconds, at.x, at.z, BodyOf(seed));

		/// <summary>
		/// Which of the lights round an eye a pool of <paramref name="max"/> real lights takes (SiteLightPool; the cave walk render
		/// mirrors it): from <paramref name="nearestFirst"/> (every candidate, nearest first, more than <paramref name="max"/> of them),
		/// first the nearest <see cref="PriorityLights"/> lava lights (<see cref="SceneEmitterKind.Magma"/>) within
		/// <see cref="PriorityReach"/>, then the rest nearest first until the pool is full; written to <paramref name="into"/> in
		/// <paramref name="nearestFirst"/>'s order. Deterministic: a pure function of the list, so every player's pool is the same.
		/// A lava tube's lava fills it with light however many crystals glow round it.
		/// </summary>
		public static void PickLights(IReadOnlyList<SceneEmitter> nearestFirst, Vector3 eye, int max, List<SceneEmitter> into)
		{
			into.Clear();
			int count = nearestFirst.Count;
			if (count <= max)
			{
				for (int i = 0; i < count; i++)
				{
					into.Add(nearestFirst[i]);
				}
				return;
			}
			if (taken.Length < count)
			{
				taken = new bool[Mathf.NextPowerOfTwo(count)];
			}
			Array.Clear(taken, 0, count);
			int chosen = 0, lava = 0;
			for (int i = 0; i < count && chosen < max && lava < PriorityLights; i++)
			{
				SceneEmitter e = nearestFirst[i];
				if (e.Kind == SceneEmitterKind.Magma && (e.Position - eye).sqrMagnitude <= PriorityReach * PriorityReach)
				{
					taken[i] = true;
					chosen++;
					lava++;
				}
			}
			for (int i = 0; i < count && chosen < max; i++)
			{
				if (!taken[i])
				{
					taken[i] = true;
					chosen++;
				}
			}
			for (int i = 0; i < count; i++)
			{
				if (taken[i])
				{
					into.Add(nearestFirst[i]);
				}
			}
		}

		/// <summary><see cref="PickLights"/>'s scratch: which candidates are taken (main thread only).</summary>
		private static bool[] taken = new bool[64];

		/// <summary>The share of an ember stream alive over a body.</summary>
		public static float Sparks(uint seed)
		{
			switch (BodyOf(seed))
			{
				case SceneMagmaBody.Fall: return FallSparks;
				case SceneMagmaBody.Lake: return LakeSparks;
				default: return RiverSparks;
			}
		}

		/// <summary>
		/// The hot air over a lava light's body as a ball (xyz its centre, w its radius, world metres): over the surface by
		/// <see cref="HeatRise"/>, as wide as the body and <see cref="HeatMargin"/> more, between <see cref="HeatLeast"/> and
		/// <see cref="HeatMost"/>.
		/// </summary>
		public static Vector4 HeatBall(in SceneEmitter e)
		{
			float r = Mathf.Clamp(HalfWidthOf(e.Seed) + HeatMargin, HeatLeast, HeatMost);
			return new Vector4(e.Position.x, e.Position.y + HeatRise, e.Position.z, r);
		}

		private static float Fraction(float v) => v - (float)Math.Floor(v);
	}
}
