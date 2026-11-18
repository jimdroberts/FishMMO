using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Plays every body's footfalls (<see cref="CharacterFootfalls"/>) on the ground it lands on: the ground's sound, a
	/// second ground where two blend, a splash where the ground is wet or under water, the brush of tall grass, and the
	/// echoes of the space round the listener (<see cref="AcousticEnvironment"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same step for everyone.</b> A footfall is raised where a body's foot visibly comes down, and every client
	/// poses every body from the same motion; which recorded variant plays and at what pitch is a hash of where the foot
	/// landed, not a roll of this client's dice. Two players watching a third walk hear the same steps.
	/// </para>
	/// <para>
	/// <b>How loud.</b> A walk is quieter than a run, a crouch quieter still, a big body heavier than a small one, and
	/// each ground has its own loudness (gravel and boards carry; grass and sand hush). Then spherical spreading from
	/// <see cref="NearMetres"/>: a walk carries about thirty metres, a landing or a plunge further.
	/// </para>
	/// </remarks>
	public sealed class FootstepPresenter : IDisposable
	{
		/// <summary>Within this distance, metres, a footfall is at its full level: about where a third-person camera sits.</summary>
		public const float NearMetres = 3f;

		/// <summary>The furthest a walking step is heard, metres.</summary>
		public const float StepRangeMetres = 32f;

		/// <summary>The furthest any footfall is heard, metres: a hard plunge (<see cref="Range"/>).</summary>
		public const float MaxRangeMetres = 100f;

		private readonly OneShotVoices voices;
		private readonly AcousticEnvironment acoustics;
		private readonly Func<Vector3> listener;
		private readonly List<EchoTap> taps = new List<EchoTap>();
		private bool subscribed;

		public FootstepPresenter(OneShotVoices voices, AcousticEnvironment acoustics, Func<Vector3> listener)
		{
			this.voices = voices;
			this.acoustics = acoustics;
			this.listener = listener;
			CharacterFootfalls.Fell += OnFootfall;
			subscribed = true;
		}

		/// <summary>How loud a ground is underfoot, against the others.</summary>
		public static float Loudness(SurfaceKind kind)
		{
			switch (kind)
			{
				case SurfaceKind.Grass: return 0.45f;
				case SurfaceKind.Leaves: return 0.6f;
				case SurfaceKind.Dirt: return 0.55f;
				case SurfaceKind.Mud: return 0.6f;
				case SurfaceKind.Sand: return 0.4f;
				case SurfaceKind.Gravel: return 0.75f;
				case SurfaceKind.Stone: return 0.65f;
				case SurfaceKind.Wood: return 0.75f;
				case SurfaceKind.Snow: return 0.6f;
				case SurfaceKind.Ice: return 0.6f;
				case SurfaceKind.Water: return 0.7f;
				case SurfaceKind.Metal: return 0.7f;
				default: return 0.5f;
			}
		}

		/// <summary>How hard a footfall is, 0..1 and a little over for a hard landing: from its kind and its speed.</summary>
		public static float Force(in Footfall footfall)
		{
			float force;
			switch (footfall.Kind)
			{
				case FootfallKind.Land:
					force = Mathf.Clamp(0.55f + 0.07f * footfall.Speed, 0.55f, 1.25f);
					break;
				case FootfallKind.Plunge:
					force = Mathf.Clamp(0.45f + 0.07f * footfall.Speed, 0.45f, 1.2f);
					break;
				case FootfallKind.Stroke:
					force = 0.5f;
					break;
				case FootfallKind.Climb:
					force = 0.45f;
					break;
				default:
					// A walk (1.4 m/s) about half, a run (4 m/s) about four fifths, a sprint full.
					force = Mathf.Clamp(0.3f + 0.12f * footfall.Speed, 0.25f, 1f);
					break;
			}
			if (footfall.Crouching)
			{
				force *= 0.45f;
			}
			float body = footfall.Height > 0.1f ? footfall.Height / 1.8f : 1f;
			return force * Mathf.Pow(Mathf.Clamp(body, 0.3f, 4f), 0.8f);
		}

		/// <summary>How far a footfall is heard, metres.</summary>
		public static float Range(in Footfall footfall, float force)
		{
			switch (footfall.Kind)
			{
				case FootfallKind.Land: return 45f * Mathf.Max(0.5f, force);
				case FootfallKind.Plunge: return 80f * Mathf.Max(0.5f, force);
				case FootfallKind.Stroke: return 40f;
				case FootfallKind.Climb: return 25f;
				default: return StepRangeMetres * Mathf.Clamp(force, 0.4f, 1.2f);
			}
		}

		/// <summary>Which variant of a sound a footfall plays: a hash of where it landed (and with which foot), the same for every client.</summary>
		public static int Variant(in Footfall footfall, int count, uint salt)
		{
			uint h = SoundSynth.Hash(SoundSynth.Hash(unchecked((uint)Mathf.FloorToInt(footfall.Point.x * 4f)), unchecked((uint)Mathf.FloorToInt(footfall.Point.z * 4f))),
				salt + (uint)footfall.Foot);
			return (int)(h % (uint)Math.Max(1, count));
		}

		private void OnFootfall(Footfall footfall)
		{
			Vector3 ear = listener();
			float distance = Vector3.Distance(footfall.Point, ear);
			float force = Force(footfall);
			float range = Range(footfall, force);
			if (distance > range)
			{
				return;
			}
			float level = force * OneShotVoices.Spreading(distance, NearMetres, range);
			if (level <= 0.002f)
			{
				return;
			}
			float pitch = Pitch(footfall);

			switch (footfall.Kind)
			{
				case FootfallKind.Stroke:
				case FootfallKind.Plunge:
				{
					bool plunge = footfall.Kind == FootfallKind.Plunge;
					int v = Variant(footfall, FootstepSounds.OtherVariants, plunge ? 11u : 13u);
					AudioClip clip = SoundLibrary.Get(FootstepSounds.SwimKey(plunge, v), () => FootstepSounds.Swim(plunge, v));
					PlayWithEchoes(clip, footfall.Point, level * Loudness(SurfaceKind.Water), pitch, distance);
					return;
				}
				case FootfallKind.Climb:
				{
					int v = Variant(footfall, FootstepSounds.OtherVariants, 17u);
					AudioClip clip = SoundLibrary.Get(FootstepSounds.RungKey(v), () => FootstepSounds.Rung(v));
					PlayWithEchoes(clip, footfall.Point, level * 0.7f, pitch, distance);
					return;
				}
			}

			bool landing = footfall.Kind == FootfallKind.Land;
			SurfaceReading reading = GroundSurface.Read(footfall);
			if (reading.Primary == SurfaceKind.Water)
			{
				// Ankle deep, a splash; knee deep, a wade.
				float wade = Mathf.Clamp(0.6f + reading.WaterDepth * 2f, 0.6f, 1.4f);
				level *= wade;
			}
			float primaryShare = 1f - 0.5f * reading.SecondaryShare;
			Ground(footfall, reading.Primary, landing, level * primaryShare * Loudness(reading.Primary), pitch, distance, true);
			if (reading.SecondaryShare > 0.2f && reading.Secondary != reading.Primary)
			{
				Ground(footfall, reading.Secondary, landing, level * reading.SecondaryShare * Loudness(reading.Secondary), pitch, distance, false);
			}
			// A wet hard floor splashes a little under the step; deep water is its own ground already.
			if (reading.Primary != SurfaceKind.Water && reading.Wet > 0.4f && Hard(reading.Primary))
			{
				Ground(footfall, SurfaceKind.Water, landing, level * 0.35f * (reading.Wet - 0.4f) / 0.6f * Loudness(SurfaceKind.Water), pitch, distance, false);
			}
			if (reading.GrassHeight > 0.15f)
			{
				int v = Variant(footfall, FootstepSounds.OtherVariants, 23u);
				AudioClip rustle = SoundLibrary.Get(FootstepSounds.RustleKey(v), () => FootstepSounds.Rustle(v));
				voices.Play(rustle, footfall.Point + Vector3.up * 0.3f, level * Mathf.Clamp01(reading.GrassHeight / 0.6f) * 0.7f, pitch);
			}
		}

		private void Ground(in Footfall footfall, SurfaceKind kind, bool landing, float gain, float pitch, float distance, bool echo)
		{
			int count = landing ? FootstepSounds.OtherVariants : FootstepSounds.StepVariants;
			int v = Variant(footfall, count, (uint)kind * 31u + (landing ? 7u : 3u));
			AudioClip clip = SoundLibrary.Get(FootstepSounds.StepKey(kind, landing, v), () => FootstepSounds.Step(kind, landing, v));
			if (echo)
			{
				PlayWithEchoes(clip, footfall.Point, gain, pitch, distance);
			}
			else
			{
				voices.Play(clip, footfall.Point, gain, pitch);
			}
		}

		/// <summary>Plays a sound and, near the listener, its echoes off the walls round it.</summary>
		private void PlayWithEchoes(AudioClip clip, Vector3 at, float gain, float pitch, float distance)
		{
			if (!voices.Play(clip, at, gain, pitch))
			{
				return;
			}
			if (acoustics == null || distance > 30f || acoustics.EchoWallCount == 0)
			{
				return;
			}
			acoustics.EchoesOf(at, taps);
			foreach (EchoTap tap in taps)
			{
				float echo = gain * tap.Gain;
				if (echo < 0.004f)
				{
					continue;
				}
				voices.Play(clip, tap.Position, echo, pitch, tap.Delay, OneShotVoices.AirCutoff(tap.Path));
			}
		}

		private static bool Hard(SurfaceKind kind)
		{
			return kind == SurfaceKind.Stone || kind == SurfaceKind.Wood || kind == SurfaceKind.Gravel || kind == SurfaceKind.Dirt
				|| kind == SurfaceKind.Metal || kind == SurfaceKind.Ice;
		}

		/// <summary>A little pitch spread by where the foot landed, and smaller bodies a little higher.</summary>
		private static float Pitch(in Footfall footfall)
		{
			uint h = SoundSynth.Hash(unchecked((uint)Mathf.FloorToInt(footfall.Point.x * 8f)), unchecked((uint)Mathf.FloorToInt(footfall.Point.z * 8f) + 977u));
			float spread = ((h & 0xFFFF) / 65535f - 0.5f) * 0.08f;
			float body = footfall.Height > 0.1f ? footfall.Height / 1.8f : 1f;
			return (1f + spread) * Mathf.Pow(Mathf.Clamp(body, 0.3f, 4f), -0.25f);
		}

		public void Dispose()
		{
			if (subscribed)
			{
				CharacterFootfalls.Fell -= OnFootfall;
				subscribed = false;
			}
		}
	}
}
