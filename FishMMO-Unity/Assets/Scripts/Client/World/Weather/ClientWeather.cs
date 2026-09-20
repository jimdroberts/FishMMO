using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's mirror of the world clock and its scene's weather timeline.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Nothing here predicts or decides anything. The server sends the clock anchor and the
	/// timeline; this evaluates them against the server tick FishNet already keeps in step, writes
	/// the scene's climate offsets exactly as the server does, keeps surface cover integrating
	/// between the server's snapshots, and hands the weather at the camera to the presenters.
	/// </para>
	/// <para>
	/// A delta that skips a revision is never guessed at: the client asks for the whole timeline.
	/// </para>
	/// </remarks>
	public class ClientWeather
	{
		private const float PresentInterval = 0.1f;
		private const float ClimateInterval = 1f;
		private const float ResyncCooldown = 2f;

		private NetworkManager networkManager;
		private readonly WeatherTimeline timeline = new WeatherTimeline();
		private Scene scene;
		private WorldSceneSettings settings;
		private bool hasTimeline;
		private float presentTimer;
		private float climateTimer;
		private float resyncCooldown;
		private readonly System.Collections.Generic.List<StormCell> newCells = new System.Collections.Generic.List<StormCell>();

		public WeatherTimeline Timeline => hasTimeline ? timeline : null;

		public void Initialize(NetworkManager manager)
		{
			networkManager = manager;
			if (networkManager == null)
			{
				return;
			}
			networkManager.ClientManager.RegisterBroadcast<WeatherTimelineBroadcast>(OnTimeline);
			networkManager.ClientManager.RegisterBroadcast<WeatherDeltaBroadcast>(OnDelta);
			networkManager.ClientManager.RegisterBroadcast<WorldClockBroadcast>(OnClock);
			WeatherQuery.TickSource = () => networkManager != null ? networkManager.TimeManager.Tick : 0u;
			WeatherPresentation.Ensure();
		}

		public void Shutdown()
		{
			if (networkManager != null)
			{
				networkManager.ClientManager.UnregisterBroadcast<WeatherTimelineBroadcast>(OnTimeline);
				networkManager.ClientManager.UnregisterBroadcast<WeatherDeltaBroadcast>(OnDelta);
				networkManager.ClientManager.UnregisterBroadcast<WorldClockBroadcast>(OnClock);
			}
			Clear(resetClock: true);
			WeatherQuery.TickSource = null;
			if (WeatherPresentation.Instance != null)
			{
				Object.Destroy(WeatherPresentation.Instance.gameObject);
			}
			networkManager = null;
		}

		/// <summary>
		/// Forgets the scene's weather (scene change), and the world clock too on disconnect: the
		/// next scene server sends its own anchor when the character arrives.
		/// </summary>
		public void Clear(bool resetClock)
		{
			if (hasTimeline)
			{
				WeatherQuery.Unregister(scene);
			}
			hasTimeline = false;
			settings = null;
			scene = default;
			if (resetClock)
			{
				WorldClock.Shared.Reset();
			}
			WeatherClient.ResetPresenters();
		}

		private void OnClock(WorldClockBroadcast msg, Channel channel)
		{
			WorldClock clock = WorldClock.Shared;
			clock.TickDelta = networkManager != null ? networkManager.TimeManager.TickDelta : clock.TickDelta;
			clock.Adopt(msg.Anchor, msg.Previous, msg.HasPrevious, msg.SlewTicks);
		}

		private void OnTimeline(WeatherTimelineBroadcast msg, Channel channel)
		{
			Scene target = SceneManager.GetSceneByName(msg.SceneName ?? string.Empty);
			if (!target.IsValid())
			{
				Log.Warning("ClientWeather", $"Weather for {msg.SceneName} arrived but that scene is not loaded.");
				return;
			}
			if (hasTimeline && scene.handle != target.handle)
			{
				WeatherQuery.Unregister(scene);
				WeatherClient.ResetPresenters();
			}
			scene = target;
			WorldSceneSettings.TryGetForScene(scene, out settings);
			timeline.Apply(msg);
			timeline.TickDelta = networkManager != null ? networkManager.TimeManager.TickDelta : timeline.TickDelta;
			hasTimeline = true;
			WeatherQuery.Register(scene, timeline);
			CatchUpCover();
			ApplyClimate();
			WeatherEvents.RaiseTimelineChanged(scene, timeline);
		}

		private void OnDelta(WeatherDeltaBroadcast msg, Channel channel)
		{
			if (!hasTimeline || msg.SceneName != timeline.SceneName)
			{
				return;
			}
			// Only cells this client has never seen are new; the rest are edits and retirements.
			newCells.Clear();
			if (msg.Revision == timeline.Revision + 1 && msg.Cells != null)
			{
				foreach (StormCell cell in msg.Cells)
				{
					if (!timeline.TryGetCell(cell.ID, out _))
					{
						newCells.Add(cell);
					}
				}
			}
			if (!timeline.TryApply(msg))
			{
				RequestResync();
				return;
			}
			foreach (StormCell cell in newCells)
			{
				WeatherEvents.RaiseCellSpawned(scene, cell);
			}
			if (msg.HasCover)
			{
				CatchUpCover();
			}
			WeatherEvents.RaiseTimelineChanged(scene, timeline);
		}

		private void RequestResync()
		{
			if (resyncCooldown > 0f || networkManager == null || !networkManager.IsClientStarted)
			{
				return;
			}
			resyncCooldown = ResyncCooldown;
			networkManager.ClientManager.Broadcast(new WeatherResyncRequestBroadcast { HaveRevision = timeline.Revision }, Channel.Reliable);
		}

		/// <summary>Advances the server's cover snapshot from its tick to now.</summary>
		private void CatchUpCover()
		{
			uint now = CurrentTick();
			if (now > timeline.CoverTick && scene.IsValid())
			{
				float seconds = Mathf.Min(120f, (float)((now - timeline.CoverTick) * timeline.TickDelta));
				WeatherSample sample = WeatherField.Sample(timeline, settings, scene, ViewerPosition(), now);
				double coverHours = WorldClock.Shared.HasAnchor ? WorldClock.Shared.WorldHoursAt(now) : 0;
				timeline.Cover.Integrate(sample.Frame, sample.Temperature, seconds, SceneTime.IsDaylight(settings, coverHours) ? 1f : 0f);
				timeline.CoverTick = now;
			}
		}

		private uint CurrentTick() => networkManager != null ? networkManager.TimeManager.Tick : 0u;

		private static Vector3 ViewerPosition()
		{
			Camera camera = Camera.main;
			return camera != null ? camera.transform.position : Vector3.zero;
		}

		/// <summary>The scene's runtime climate: the timeline's shift plus the body's starlight, as the server computes it.</summary>
		private void ApplyClimate()
		{
			if (settings == null)
			{
				return;
			}
			uint tick = CurrentTick();
			timeline.ClimateAt(tick, out float temperature, out float humidity);
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = SceneTime.BodyOf(settings);
			if (system != null && body != null && WorldClock.Shared.HasAnchor)
			{
				CelestialMath.ClimateOffsets(system, body, WorldClock.Shared.WorldHoursAt(tick), out float bt, out float bh, settings.Latitude);
				temperature += bt;
				humidity += bh;
			}
			settings.RuntimeTemperatureOffset = Mathf.Clamp(temperature, -2f, 2f);
			settings.RuntimeHumidityOffset = Mathf.Clamp(humidity, -2f, 2f);
		}

		public void Tick(float deltaTime)
		{
			if (resyncCooldown > 0f)
			{
				resyncCooldown -= deltaTime;
			}
			if (!hasTimeline || !scene.IsValid())
			{
				return;
			}
			climateTimer -= deltaTime;
			if (climateTimer <= 0f)
			{
				climateTimer = ClimateInterval;
				timeline.Prune(CurrentTick());
				ApplyClimate();
				CatchUpCover();
			}
			presentTimer -= deltaTime;
			if (presentTimer > 0f)
			{
				return;
			}
			presentTimer = PresentInterval;

			TimeManager tm = networkManager.TimeManager;
			uint tick = tm.Tick;
			Vector3 viewer = ViewerPosition();
			WeatherSample sample = WeatherField.Sample(timeline, settings, scene, viewer, tick);
			double preciseTick = tick + tm.GetTickPercentAsDouble();
			double hours = WorldClock.Shared.HasAnchor ? WorldClock.Shared.WorldHoursAt(preciseTick) : 0;
			var context = new WeatherContext
			{
				Scene = scene,
				Settings = settings,
				Timeline = timeline,
				ViewerPosition = viewer,
				Tick = preciseTick,
				WorldHours = hours,
				LocalTime01 = SceneTime.LocalTime01(settings, hours),
				IsDaylight = SceneTime.IsDaylight(settings, hours),
				Cover = timeline.Cover,
				Background = sample.Background,
				DriverWeight = sample.DriverWeight,
				Shelter = sample.Shelter,
				Temperature = sample.Temperature,
			};
			WeatherClient.Present(sample.Frame, context);
		}
	}
}
