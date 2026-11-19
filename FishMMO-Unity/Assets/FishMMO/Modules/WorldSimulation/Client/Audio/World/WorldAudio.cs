using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The world's own sound, outside the weather's loops: footfalls on every ground, the wind in the grass and the trees,
	/// thunder from each strike's channel, and what the space round the listener does to all of it (a cave's ring, a
	/// canyon's echo). One for the client, made when play starts and kept across scenes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Nothing here is an asset: every sound is synthesised from a seed (<see cref="SoundLibrary"/>), and every choice
	/// that could differ between players — which variant of a step, where in a loop, when thunder arrives — is a function
	/// of the shared world (where a foot landed, the world's motion clock, the strike's own moment), so players together
	/// hear the same thing at the same time.
	/// </para>
	/// <para>
	/// Not made in batch mode: a headless run has no audio device and nothing to listen.
	/// </para>
	/// </remarks>
	[AddComponentMenu("")]
	public sealed class WorldAudio : MonoBehaviour
	{
		/// <summary>The most footfalls, splashes and echoes heard at once.</summary>
		public const int EffectVoices = 32;

		/// <summary>The most thunderclaps heard (or waiting to be) at once.</summary>
		public const int AmbientVoices = 8;

		public static WorldAudio Instance { get; private set; }

		/// <summary>Footfalls, splashes, their echoes: the Effects channel.</summary>
		public OneShotVoices Effects { get; private set; }

		/// <summary>Thunder: the Ambient channel, with the weather.</summary>
		public OneShotVoices Ambient { get; private set; }

		public AcousticEnvironment Acoustics { get; private set; }

		public ThunderPresenter Thunder { get; private set; }

		/// <summary>Where the sound is heard from: the active listener, or the main camera before there is one.</summary>
		public Vector3 ListenerPosition { get; private set; }

		private FootstepPresenter footsteps;
		private WindBedsPresenter windBeds;
		private AudioListener listener;
		private float listenerLookedAt = float.NegativeInfinity;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Boot()
		{
			if (Application.isBatchMode)
			{
				return;
			}
			Ensure();
		}

		/// <summary>Makes the world's sound (once) and returns it.</summary>
		public static WorldAudio Ensure()
		{
			if (Instance != null)
			{
				return Instance;
			}
			var go = new GameObject("World Audio");
			DontDestroyOnLoad(go);
			return go.AddComponent<WorldAudio>();
		}

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
			Effects = new OneShotVoices(transform, "Footfall", AudioChannel.Effects, EffectVoices);
			Ambient = new OneShotVoices(transform, "Thunder", AudioChannel.Ambient, AmbientVoices);
			Acoustics = new AcousticEnvironment(transform);
			Thunder = new ThunderPresenter(Ambient);
			footsteps = new FootstepPresenter(Effects, Acoustics, () => ListenerPosition);
			windBeds = new WindBedsPresenter(transform);
		}

		private void Update()
		{
			float deltaTime = Time.deltaTime;
			ListenerPosition = ResolveListener();
			CharacterFootfalls.Ear = ListenerPosition;
			CharacterFootfalls.Reach = FootstepPresenter.MaxRangeMetres;
			Acoustics.Update(ListenerPosition, PhysicsAt(), deltaTime);
			Thunder.Update();
			WeatherPresentation presentation = WeatherPresentation.Instance;
			float shelter = presentation != null ? presentation.Shelter : 0f;
			// Under a roof or in a cave the growth outside is all but gone; in a forest the canopy is not a roof.
			float roofed = Mathf.Max(shelter, Acoustics.Roofed * Mathf.InverseLerp(0.6f, 1f, Acoustics.Enclosure));
			float outdoors = presentation != null && presentation.Underground ? 0f : 1f - 0.85f * Mathf.Clamp01(roofed);
			windBeds.Update(ListenerPosition, outdoors, deltaTime);
		}

		/// <summary>The listener's position, finding the listener again when it goes (a camera swapped, a scene unloaded).</summary>
		private Vector3 ResolveListener()
		{
			if ((listener == null || !listener.isActiveAndEnabled) && Time.unscaledTime - listenerLookedAt > 1f)
			{
				listenerLookedAt = Time.unscaledTime;
				Camera main = Camera.main;
				listener = main != null ? main.GetComponent<AudioListener>() : null;
				if (listener == null || !listener.isActiveAndEnabled)
				{
					listener = FindAnyObjectByType<AudioListener>();
				}
			}
			if (listener != null && listener.isActiveAndEnabled)
			{
				return listener.transform.position;
			}
			Camera camera = Camera.main;
			return camera != null ? camera.transform.position : ListenerPosition;
		}

		/// <summary>
		/// The physics the listener's surroundings are in: the weather's scene's (world scenes load with their own
		/// physics), else the active scene's.
		/// </summary>
		private static PhysicsScene PhysicsAt()
		{
			WeatherPresentation presentation = WeatherPresentation.Instance;
			if (presentation != null && presentation.HasContext && presentation.Context.Scene.IsValid())
			{
				return presentation.Context.Scene.GetPhysicsScene();
			}
			Scene active = SceneManager.GetActiveScene();
			return active.IsValid() ? active.GetPhysicsScene() : Physics.defaultPhysicsScene;
		}

		private void OnDestroy()
		{
			if (Instance != this)
			{
				return;
			}
			footsteps?.Dispose();
			CharacterFootfalls.Reach = float.MaxValue;
			windBeds?.Dispose();
			Thunder?.Dispose();
			Acoustics?.Dispose();
			Effects?.Dispose();
			Ambient?.Dispose();
			Instance = null;
		}
	}
}
