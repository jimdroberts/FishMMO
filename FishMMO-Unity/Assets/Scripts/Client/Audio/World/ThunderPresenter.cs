using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Client
{
	/// <summary>
	/// The thunder of every strike within earshot: rendered for where the listener stands from the strike's own channel
	/// (<see cref="ThunderSynth"/>), and played when its sound gets there.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>When.</b> A strike happens at a moment of the shared world clock, the same for every player
	/// (<c>SkySchedule.Lightning</c>), and its flash is seen then. Its sound sets off at the same moment and travels at
	/// 343 m/s, so the first of it — the nearest piece of channel — reaches a listener <c>d/c</c> later: three seconds a
	/// kilometre. The delay is worked out on the world's motion clock (<see cref="WorldMotion.Seconds"/>), which runs at
	/// real time for every player alike, and the clip is scheduled on the audio clock to the sample. A player who
	/// arrives, or hitches, partway through hears it from where it has got to, not from its start.
	/// </para>
	/// <para>
	/// <b>How loud.</b> A clap a few hundred metres off is as loud as the game gets; from there it falls with distance,
	/// gentler than 1/r so a storm kilometres away still grumbles (the ear's loudness, not pressure), and goes silent
	/// past <see cref="AudibleMetres"/>, where the air's refraction bends thunder up over the listener's head.
	/// </para>
	/// <para>
	/// <b>Where from.</b> The clip is heard from the direction of the channel's nearest piece, spread wider the nearer
	/// the strike is: a bolt overhead fills the sky, one on the horizon is a point.
	/// </para>
	/// </remarks>
	public sealed class ThunderPresenter : IDisposable
	{
		/// <summary>Past this distance, metres, thunder is not heard: the warm air near the ground bends it upward.</summary>
		public const float AudibleMetres = 15000f;

		/// <summary>Seconds of strikes looked back over on arriving: their thunder may still be on its way.</summary>
		public const double LookbackSeconds = AudibleMetres / SoundSynth.SpeedOfSound + 1.0;

		/// <summary>At this distance and nearer, metres, thunder is at its loudest.</summary>
		public const float LoudestMetres = 400f;

		/// <summary>The most claps waiting for their sound to arrive; a busier storm drops its furthest. Claps already heard do not count.</summary>
		public const int MaxClaps = 8;

		/// <summary>A clap quieter than this (<see cref="Level"/>) is not made at all.</summary>
		public const float QuietestLevel = 0.03f;

		private sealed class Clap
		{
			public Task<ThunderSynth.Result> Job;
			public ThunderSynth.Result Made;
			public bool Ready;
			/// <summary>When (motion clock) the strike happened.</summary>
			public double StruckAt;
			public Vector3 Listener;
			/// <summary>Metres along the ground to the strike's foot, when it struck: how claps are ranked before they are made.</summary>
			public float Ground;
			public float Intensity;
			public AudioClip Clip;
			public double EndsAt;
			public bool Played;
		}

		private readonly OneShotVoices voices;
		private readonly List<Clap> claps = new List<Clap>();
		private readonly List<Vector3> trunk = new List<Vector3>();
		private readonly List<List<Vector3>> branches = new List<List<Vector3>>();

		public ThunderPresenter(OneShotVoices voices)
		{
			this.voices = voices;
		}

		/// <summary>How loud a clap is from its nearest piece's distance, 0..1.</summary>
		public static float Level(float nearestMetres, float intensity)
		{
			float near = Mathf.Max(LoudestMetres, nearestMetres);
			// Spreading takes 6 dB a doubling; loudness (sones) halves for every 10 dB, so it falls as r^-0.66. Eased to
			// r^-0.55: thunder is low, and the ear's equal-loudness curves flatten the low end as it gets louder.
			float loud = Mathf.Pow(LoudestMetres / near, 0.55f);
			float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(AudibleMetres * 0.7f, AudibleMetres, nearestMetres));
			return Mathf.Clamp01(loud * fade * Mathf.Lerp(0.7f, 1f, intensity));
		}

		/// <summary>
		/// A strike that has happened: its thunder is made for where the listener is and played when it arrives.
		/// </summary>
		/// <param name="ageSeconds">Real seconds since the strike (0 for one just seen).</param>
		public void Strike(in LightningStrike strike, float ageSeconds, Vector3 listener)
		{
			float ground = Vector3.Distance(new Vector3(strike.Ground.x, listener.y, strike.Ground.z), listener);
			if (ground > AudibleMetres)
			{
				return;
			}
			SkySchedule.BoltPath(strike, trunk, branches);
			var channel = new ThunderSynth.Channel { Seed = strike.Seed, Intensity = strike.Intensity, ListenerGround = GroundUnder(listener) };
			channel.Lines.Add(trunk.ToArray());
			foreach (List<Vector3> branch in branches)
			{
				channel.Lines.Add(branch.ToArray());
			}
			ThunderSynth.AddCloudChannel(channel, strike.Ground + Vector3.up * strike.CloudHeight, strike.CloudHeight, strike.Seed);

			// Its sound is already past the listener entirely: nothing to make.
			float furthest = ground + strike.CloudHeight + 12500f;
			if (ageSeconds * SoundSynth.SpeedOfSound > furthest)
			{
				return;
			}
			if (Level(ground, strike.Intensity) < QuietestLevel)
			{
				return;
			}
			if (Waiting() >= MaxClaps && !DropFurthest(ground))
			{
				return;
			}
			var clap = new Clap
			{
				StruckAt = WorldMotion.Seconds - ageSeconds,
				Listener = listener,
				Ground = ground,
				Intensity = strike.Intensity,
			};
#if UNITY_WEBGL && !UNITY_EDITOR
			clap.Job = Task.FromResult(ThunderSynth.Render(channel, listener));
#else
			clap.Job = Task.Run(() => ThunderSynth.Render(channel, listener));
#endif
			claps.Add(clap);
		}

		/// <summary>Makes the claps whose renders are done and plays each at its moment.</summary>
		public void Update()
		{
			double dsp = AudioSettings.dspTime;
			double now = WorldMotion.Seconds;
			for (int i = claps.Count - 1; i >= 0; i--)
			{
				Clap clap = claps[i];
				if (!clap.Ready)
				{
					if (!clap.Job.IsCompleted)
					{
						continue;
					}
					if (clap.Job.IsFaulted || clap.Job.IsCanceled)
					{
						Debug.LogWarning("[Thunder] A clap could not be made: " + clap.Job.Exception?.GetBaseException().Message);
						claps.RemoveAt(i);
						continue;
					}
					clap.Made = clap.Job.Result;
					clap.Job = null;
					clap.Ready = true;
					if (clap.Made.Samples == null || clap.Made.Samples.Length == 0)
					{
						claps.RemoveAt(i);
						continue;
					}
					clap.Clip = SoundSynth.ToClip("Thunder", clap.Made.Samples, ThunderSynth.ThunderRate);
					clap.Made.Samples = null;
				}
				if (!clap.Played)
				{
					// The first of it arrives the nearest piece's distance after the strike.
					double arrives = clap.StruckAt + clap.Made.NearestMetres / SoundSynth.SpeedOfSound;
					double delay = arrives - now;
					float level = Level(clap.Made.NearestMetres, clap.Intensity);
					// From the direction of the strike: the clip is flat, so the point need only lie that way.
					Vector3 toward = clap.Made.NearestMetres > 1f ? NearestDirection(clap) : Vector3.up;
					float spread = Mathf.Lerp(150f, 20f, Mathf.InverseLerp(300f, 5000f, clap.Made.NearestMetres));
					voices.Play(clap.Clip, clap.Listener + toward * 50f, level, 1f, delay, 0f, spread);
					clap.Played = true;
					clap.EndsAt = dsp + Math.Max(0.0, delay) + clap.Clip.length + 0.5;
				}
				else if (dsp > clap.EndsAt)
				{
					UnityEngine.Object.Destroy(clap.Clip);
					claps.RemoveAt(i);
				}
			}
		}

		/// <summary>Which way the strike lies from where its thunder was made for: toward its foot, lifted a little.</summary>
		private static Vector3 NearestDirection(Clap clap)
		{
			Vector3 d = clap.Made.NearestPoint - clap.Listener;
			return d.sqrMagnitude > 1f ? d.normalized : Vector3.up;
		}

		/// <summary>How many claps have not been heard yet (being made, or waiting for their sound to arrive).</summary>
		private int Waiting()
		{
			int waiting = 0;
			foreach (Clap clap in claps)
			{
				if (!clap.Played)
				{
					waiting++;
				}
			}
			return waiting;
		}

		/// <summary>Makes room for a nearer clap by dropping the furthest one not yet heard; false if this one is the furthest.</summary>
		private bool DropFurthest(float metres)
		{
			int furthest = -1;
			float most = metres;
			for (int i = 0; i < claps.Count; i++)
			{
				if (claps[i].Played)
				{
					continue;
				}
				if (claps[i].Ground > most)
				{
					most = claps[i].Ground;
					furthest = i;
				}
			}
			if (furthest < 0)
			{
				return false;
			}
			// A render still running finishes on its own; its result is simply not kept.
			if (claps[furthest].Clip != null)
			{
				UnityEngine.Object.Destroy(claps[furthest].Clip);
			}
			claps.RemoveAt(furthest);
			return true;
		}

		private static float GroundUnder(Vector3 at)
		{
			Terrain terrain = Terrain.activeTerrain;
			foreach (Terrain t in Terrain.activeTerrains)
			{
				Vector3 o = t.GetPosition();
				Vector3 s = t.terrainData != null ? t.terrainData.size : Vector3.zero;
				if (at.x >= o.x && at.z >= o.z && at.x <= o.x + s.x && at.z <= o.z + s.z)
				{
					terrain = t;
					break;
				}
			}
			return terrain != null ? terrain.SampleHeight(at) + terrain.GetPosition().y : at.y - 1.7f;
		}

		/// <summary>Forgets every clap (a scene change, the clock set to another moment).</summary>
		public void Clear()
		{
			foreach (Clap clap in claps)
			{
				if (clap.Clip != null)
				{
					UnityEngine.Object.Destroy(clap.Clip);
				}
			}
			claps.Clear();
		}

		public void Dispose()
		{
			Clear();
		}
	}
}
