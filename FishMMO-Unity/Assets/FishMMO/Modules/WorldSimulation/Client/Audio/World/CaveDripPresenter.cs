using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// The sound of the caves' drips round the listener (2026-10-20, caves livened): each drop the drip field draws lands with its
	/// plink or splat (<see cref="DripSounds"/>) at the very moment it lands on the shared clock (SceneDrip), through the world's
	/// sound, so the cave's reverb and echoes carry it (WorldAudio, AcousticEnvironment).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same drop for everyone.</b> When each drop lands is a function of the shared motion clock and the drip; which variant
	/// it plays and at what pitch is a hash of the drip and the drop's number. Landings due within the next
	/// <see cref="LookaheadSeconds"/> are scheduled on the audio clock to the sample (OneShotVoices' scheduled start), each once.
	/// </para>
	/// <para>
	/// <b>Cost.</b> A scan of the scene's emitters each time the listener has moved <see cref="Refind"/> metres, then a few
	/// arithmetic steps a frame for each of the nearest <see cref="MaxSources"/> drips; a ray (is there rock between?) and a voice
	/// for a few tenths of a second a drop (a couple a second near a busy drip). The clips are made once, off the main thread.
	/// </para>
	/// <para>Play mode, client only, self-starting; not in batch mode (no audio device).</para>
	/// </remarks>
	[AddComponentMenu("")]
	public sealed class CaveDripPresenter : MonoBehaviour
	{
		/// <summary>The drips listened to at once, nearest first.</summary>
		public const int MaxSources = 16;
		/// <summary>Within this distance, metres, a drop is at its full level.</summary>
		public const float NearMetres = 1.5f;
		/// <summary>The furthest a drop is heard, metres: a cave is quiet, and a drip carries.</summary>
		public const float RangeMetres = 30f;
		/// <summary>Seconds of the clock ahead a landing is scheduled.</summary>
		public const float LookaheadSeconds = 0.25f;
		/// <summary>Echoes are given to drops within this distance, metres.</summary>
		public const float EchoMetres = 18f;
		private const float Refind = 3f;

		private static CaveDripPresenter instance;
		private readonly List<SceneEmitter> near = new List<SceneEmitter>();
		private readonly List<EchoTap> taps = new List<EchoTap>();
		/// <summary>The last drop scheduled for each drip (by its place and seed): a drop is played once.</summary>
		private readonly Dictionary<ulong, long> played = new Dictionary<ulong, long>();
		private readonly HashSet<ulong> kept = new HashSet<ulong>();
		private readonly List<ulong> gone = new List<ulong>();
		private Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private int foundVersion = -1;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Boot()
		{
			if (Application.isBatchMode || instance != null)
			{
				return;
			}
			var go = new GameObject("Cave Drips Audio");
			DontDestroyOnLoad(go);
			instance = go.AddComponent<CaveDripPresenter>();
			SceneManager.sceneUnloaded += _ =>
			{
				if (instance != null)
				{
					instance.foundVersion = -1;
				}
			};
		}

		private void OnDestroy()
		{
			if (instance == this)
			{
				instance = null;
			}
		}

		private void Update()
		{
			WorldAudio audio = WorldAudio.Instance;
			if (audio == null || audio.Effects == null)
			{
				return;
			}
			Vector3 ear = audio.ListenerPosition;
			if (SceneSiteEmitters.Version != foundVersion || (ear - foundAt).sqrMagnitude > Refind * Refind)
			{
				foundVersion = SceneSiteEmitters.Version;
				foundAt = ear;
				SceneSiteEmitters.Near(ear, RangeMetres + Refind, kind => kind == SceneEmitterKind.Drip, MaxSources, near);
				// Forget the drips gone out of reach (one coming back is heard from its next drop).
				kept.Clear();
				foreach (SceneEmitter e in near)
				{
					kept.Add(Key(e));
				}
				gone.Clear();
				foreach (ulong key in played.Keys)
				{
					if (!kept.Contains(key))
					{
						gone.Add(key);
					}
				}
				foreach (ulong key in gone)
				{
					played.Remove(key);
				}
			}
			if (near.Count == 0)
			{
				return;
			}
			// The clock held or crawling (an admin's pause): the drops hang where they are, and so does their sound.
			float pace = WorldMotion.Pace;
			if (pace < 0.05f)
			{
				return;
			}
			double seconds = WorldMotion.Seconds;
			foreach (SceneEmitter e in near)
			{
				float distance = Vector3.Distance(e.Position, ear);
				if (distance > RangeMetres)
				{
					continue;
				}
				uint seed = e.Seed;
				SceneDrip.ClockAt(seconds, seed, out long cycle, out float into);
				ulong key = Key(e);
				for (int m = -1; m <= 3; m++)
				{
					float due = SceneDrip.SecondsToLanding(seed, cycle, into, m);
					if (due >= LookaheadSeconds)
					{
						break;
					}
					// Just landed (a long frame, or the drip just came in reach): heard now; long gone: not at all.
					if (due < -0.06f)
					{
						continue;
					}
					long number = (cycle + m + SceneDrip.CycleModulus) % SceneDrip.CycleModulus;
					if (played.TryGetValue(key, out long last) && !After(number, last))
					{
						continue;
					}
					played[key] = number;
					Play(audio, e, number, distance, Mathf.Max(0f, due) / pace);
				}
			}
		}

		/// <summary>Whether drop <paramref name="number"/> comes after <paramref name="last"/> (both modulo SceneDrip.CycleModulus).</summary>
		private static bool After(long number, long last)
		{
			long ahead = (number - last + SceneDrip.CycleModulus) % SceneDrip.CycleModulus;
			return ahead > 0 && ahead < SceneDrip.CycleModulus / 2;
		}

		private static ulong Key(in SceneEmitter e)
		{
			unchecked
			{
				uint a = SoundSynth.Hash((uint)Mathf.RoundToInt(e.Position.x * 20f), (uint)Mathf.RoundToInt(e.Position.z * 20f));
				uint b = SoundSynth.Hash((uint)Mathf.RoundToInt(e.Position.y * 20f), e.Seed);
				return ((ulong)a << 32) | b;
			}
		}

		/// <summary>A drop with rock between it and the listener: its level's share (about −18 dB) and the low-pass it is heard through.</summary>
		public const float HiddenGain = 0.12f, HiddenCutoffHz = 1200f;

		private int solidMask = -1;

		/// <summary>Whether rock (the cave's own, the ground) stands between the listener and a drop: one ray, on the world scene's physics.</summary>
		private bool Hidden(Vector3 ear, Vector3 at)
		{
			if (solidMask == -1)
			{
				int solid = LayerMask.GetMask("Default", "Ground");
				solidMask = solid != 0 ? solid : Physics.DefaultRaycastLayers;
				int player = LayerMask.NameToLayer("Player");
				if (player >= 0)
				{
					solidMask &= ~(1 << player);
				}
			}
			Vector3 way = at - ear;
			float length = way.magnitude;
			if (length < 0.5f)
			{
				return false;
			}
			// World scenes run their own physics (LocalPhysicsMode): the weather's scene's, else the active one's.
			WeatherPresentation presentation = WeatherPresentation.Instance;
			Scene scene = presentation != null && presentation.HasContext && presentation.Context.Scene.IsValid() ? presentation.Context.Scene : SceneManager.GetActiveScene();
			PhysicsScene physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
			return physics.Raycast(ear, way / length, out _, length - 0.3f, solidMask, QueryTriggerInteraction.Ignore);
		}

		/// <summary>How loud a drop is at its source, 0..1: into water louder than on rock, and harder from higher.</summary>
		public static float Level(SceneDripLanding landing, float fall)
		{
			float base_ = landing == SceneDripLanding.Puddle ? 0.42f : 0.3f;
			return base_ * (0.55f + 0.45f * Mathf.Clamp01(fall / 12f));
		}

		/// <summary>Which variant drop <paramref name="number"/> of a drip plays, and at what pitch: the same for every client.</summary>
		public static void Pick(uint seed, long number, out int variant, out float pitch)
		{
			uint h = SoundSynth.Hash(seed, (uint)number + 0x51u);
			variant = (int)(h % DripSounds.Variants);
			pitch = 1f + (((h >> 8) & 0xFFFF) / 65535f - 0.5f) * 0.12f;
		}

		private void Play(WorldAudio audio, in SceneEmitter e, long number, float distance, float delay)
		{
			SceneDripLanding landing = SceneDrip.LandingOf(e.Seed);
			float gain = Level(landing, e.Reach) * OneShotVoices.Spreading(distance, NearMetres, RangeMetres);
			if (gain <= 0.002f)
			{
				return;
			}
			// Rock between: round a bend of the passage only what bends round the corner is heard, dulled; through the hill over a
			// cave, next to nothing.
			bool hidden = Hidden(audio.ListenerPosition, e.Position + Vector3.up * 0.15f);
			if (hidden)
			{
				gain *= HiddenGain;
				if (gain <= 0.002f)
				{
					return;
				}
			}
			Pick(e.Seed, number, out int variant, out float pitch);
			AudioClip clip = SoundLibrary.Get(DripSounds.Key(landing, variant), () => DripSounds.Drip(landing, variant));
			if (clip == null || !audio.Effects.Play(clip, e.Position, gain, pitch, delay, hidden ? HiddenCutoffHz : 0f))
			{
				return;
			}
			AcousticEnvironment acoustics = audio.Acoustics;
			if (hidden || acoustics == null || distance > EchoMetres || acoustics.EchoWallCount == 0)
			{
				return;
			}
			acoustics.EchoesOf(e.Position, taps);
			foreach (EchoTap tap in taps)
			{
				float echo = gain * tap.Gain;
				if (echo >= 0.004f)
				{
					audio.Effects.Play(clip, tap.Position, echo, pitch, delay + tap.Delay, OneShotVoices.AirCutoff(tap.Path));
				}
			}
		}
	}
}
