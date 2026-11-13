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
	/// the scene's climate offsets exactly as the server does, works out the scene's own storms and
	/// its ground from the world time as the server does (<see cref="StormSchedule"/>,
	/// <see cref="SceneCover"/>), and hands the weather at the camera to the presenters.
	/// </para>
	/// <para>
	/// <b>A set of the clock lands at once.</b> When the world clock is set to another moment
	/// (<see cref="WorldClock.Jumps"/>), the climate, the storms, the ground and the weather at the
	/// camera are all worked out for the new moment on that frame, and the presenters are told to
	/// show it rather than ease toward it (<see cref="WeatherClient.Snap"/>).
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
		private readonly SceneCover cover = new SceneCover();
		private uint seenJumps;
		/// <summary>
		/// A timeline that arrived before its scene finished loading, held until it does (<see cref="OnSceneLoaded"/>).
		/// </summary>
		/// <remarks>
		/// The server sends a scene's whole timeline once, when the character spawns, and the client can still be
		/// activating the scene then (its content is large, and on the web it may still be downloading). Dropping it
		/// left the scene without weather for the whole visit: nothing else sends the whole timeline unasked, and the
		/// deltas that follow are ignored without one.
		/// </remarks>
		private WeatherTimelineBroadcast? pendingTimeline;

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
			networkManager.ClientManager.OnClientConnectionState += OnConnectionState;
			SceneManager.sceneLoaded += OnSceneLoaded;
			WeatherQuery.TickSource = () => networkManager != null ? networkManager.TimeManager.Tick : 0u;
			// The tick the world's motion (waves, wind, fog banks) reads the shared clock at.
			WorldMotion.TimeManager = networkManager.TimeManager;
			WeatherPresentation.Ensure();
			// A backdrop that finished loading before this was set up.
			for (int i = 0; i < SceneManager.sceneCount && !hasTimeline; i++)
			{
				Scene loaded = SceneManager.GetSceneAt(i);
				if (loaded.isLoaded)
				{
					OnSceneLoaded(loaded, LoadSceneMode.Additive);
				}
			}
		}

		public void Shutdown()
		{
			if (networkManager != null)
			{
				networkManager.ClientManager.UnregisterBroadcast<WeatherTimelineBroadcast>(OnTimeline);
				networkManager.ClientManager.UnregisterBroadcast<WeatherDeltaBroadcast>(OnDelta);
				networkManager.ClientManager.UnregisterBroadcast<WorldClockBroadcast>(OnClock);
				networkManager.ClientManager.OnClientConnectionState -= OnConnectionState;
			}
			SceneManager.sceneLoaded -= OnSceneLoaded;
			Clear(resetClock: true);
			WeatherQuery.TickSource = null;
			WorldMotion.TimeManager = null;
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
			pendingTimeline = null;
			settings = null;
			scene = default;
			if (resetClock)
			{
				WorldClock.Shared.Reset();
			}
			WeatherClient.ResetPresenters();
		}

		/// <summary>
		/// Forgets the world clock's anchor the moment the connection stops. The anchor names a tick of
		/// the server it came from, and FishNet restarts the tick at 0 on a disconnect and counts the
		/// next server's ticks after — so a scene hop or a reconnect read the old server's anchor against
		/// the new server's tick, and the sky (and everything on the world's clock) was off by the two
		/// servers' difference in uptime, often days, until the new anchor came with the character.
		/// Without one the clock is this machine's UTC, a fraction of a second out at most; the next
		/// server's anchor arrives on spawn.
		/// </summary>
		private void OnConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopped || args.ConnectionState == LocalConnectionState.Stopping)
			{
				WorldClock.Shared.Reset();
			}
		}

		private void OnClock(WorldClockBroadcast msg, Channel channel)
		{
			WorldClock clock = WorldClock.Shared;
			clock.TickDelta = networkManager != null ? networkManager.TimeManager.TickDelta : clock.TickDelta;
			bool first = !clock.HasAnchor;
			clock.Adopt(msg.Anchor, msg.Previous, msg.HasPrevious, msg.SlewTicks);
			if (first)
			{
				// Once per connection: what every viewer's sky and weather now run on, and at what pace.
				SolarSystemProfile system = SolarSystemProfile.Active;
				long epoch = system != null ? system.EpochUnixSeconds : CalendarProfile.DefaultEpochUnixSeconds;
				Log.Info("ClientWeather", $"World clock from the server: {WorldTimeText.Write(msg.Anchor.WorldSeconds, epoch)}, {WorldTimeText.Pace(msg.Anchor.Rate)}{(msg.Anchor.Verified ? string.Empty : " (unverified)")}.");
			}
		}

		private void OnTimeline(WeatherTimelineBroadcast msg, Channel channel)
		{
			Scene target = SceneManager.GetSceneByName(msg.SceneName ?? string.Empty);
			if (!target.IsValid() || !target.isLoaded)
			{
				// Still loading: applied when it has (OnSceneLoaded). A later timeline replaces an earlier one.
				pendingTimeline = msg;
				Log.Info("ClientWeather", $"Weather for {msg.SceneName} arrived before the scene finished loading; held until it has.");
				return;
			}
			ApplyTimeline(msg, target);
		}

		/// <summary>Applies a timeline held for a scene that was still loading, now that it has.</summary>
		private void OnSceneLoaded(Scene loaded, LoadSceneMode mode)
		{
			if (pendingTimeline is WeatherTimelineBroadcast msg && loaded.name == msg.SceneName)
			{
				pendingTimeline = null;
				ApplyTimeline(msg, loaded);
				return;
			}
			if (!hasTimeline && pendingTimeline == null && WorldSceneSettings.TryGetForScene(loaded, out WorldSceneSettings loadedSettings)
				&& loadedSettings.ClientBackdrop)
			{
				ApplyBackdropTimeline(loaded, loadedSettings);
			}
		}

		/// <summary>
		/// The weather of a backdrop only the client draws (the login screen's world), which no server hosts and so no
		/// server sends a timeline for: the one a server would make for it, worked out here. The weather is a function of
		/// the scene's seed and the world clock, so it is the same weather a server would have sent, with no hand-started
		/// storms. A world's own timeline replaces it on entering the game.
		/// </summary>
		private void ApplyBackdropTimeline(Scene target, WorldSceneSettings backdrop)
		{
			if (networkManager == null)
			{
				return;
			}
			WeatherSceneMode sceneMode = WeatherField.ResolveMode(backdrop, target.name);
			uint seed = unchecked((uint)target.name.GetDeterministicHashCode() ^ WeatherDriver.WorldSeed);
			TimeManager tm = networkManager.TimeManager;
			var local = new WeatherTimeline
			{
				SceneName = target.name,
				Seed = seed,
				SceneMode = sceneMode,
				TickDelta = tm.TickDelta,
			};
			local.Area = SceneCoverSampling.TryGetArea(backdrop, target, out Rect area) ? area : default;
			local.Director = backdrop.WeatherDirector && sceneMode == WeatherSceneMode.Own;
			local.LatitudeDegrees = backdrop.Latitude;
			local.LongitudeDegrees = backdrop.Longitude;
			local.Driver = sceneMode == WeatherSceneMode.Own;
			// Read from the world clock when a server has anchored it, else this machine's (the sky's own fallback).
			local.WorldSecondsTick = tm.Tick;
			local.WorldSecondsAtTick = WorldTime.CurrentHours(tm) * 3600.0;
			ApplyTimeline(local.ToBroadcast(), target);
		}

		private void ApplyTimeline(WeatherTimelineBroadcast msg, Scene target)
		{
			pendingTimeline = null;
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
			ApplyClimate();
			PresentStorms(announce: false);
			UpdateCover();
			WeatherEvents.RaiseTimelineChanged(scene, timeline);
			// A new scene's weather is shown as it is, not eased into from the last one's.
			ForceStep(snap: true);
			Log.Info("ClientWeather", $"Weather for {scene.name}: revision {timeline.Revision}, {timeline.SceneMode}, {timeline.Cells.Count} cell(s), director {(timeline.Director ? "on" : "off")}; world clock {(WorldClock.Shared.HasAnchor ? "anchored" : "NOT anchored (this machine's clock)")}.");
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
			if (msg.HasDirector)
			{
				// The scene's own storms started or stopped: on this frame, as on the server's pass.
				PresentStorms(announce: false);
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

		/// <summary>The scene's own ground figure now, worked out from the world time over its cover points as the server's is.</summary>
		private void UpdateCover()
		{
			if (scene.IsValid())
			{
				cover.Update(timeline, settings, scene, timeline.WorldSecondsAt(PreciseTick()));
			}
		}

		/// <summary>The scene's own storms of this moment into the timeline, as the server's pass puts them there.</summary>
		private void PresentStorms(bool announce)
		{
			if (!scene.IsValid())
			{
				return;
			}
			newCells.Clear();
			if (StormSchedule.Present(timeline, settings, scene, timeline.WorldSecondsAt(PreciseTick()), announce ? newCells : null))
			{
				foreach (StormCell cell in newCells)
				{
					WeatherEvents.RaiseCellSpawned(scene, cell);
				}
				WeatherEvents.RaiseTimelineChanged(scene, timeline);
			}
		}

		private double PreciseTick() => networkManager != null ? networkManager.TimeManager.Tick + networkManager.TimeManager.GetTickPercentAsDouble() : 0.0;

		private uint CurrentTick() => networkManager != null ? networkManager.TimeManager.Tick : 0u;

		private static Vector3 ViewerPosition()
		{
			Camera camera = Camera.main;
			return camera != null ? camera.transform.position : Vector3.zero;
		}

		/// <summary>The scene's runtime climate: the warmth added to its air plus its body's season, as the server computes it.</summary>
		private void ApplyClimate()
		{
			if (settings == null)
			{
				return;
			}
			uint tick = CurrentTick();
			// The one sum the server, this client and the bed all make (SceneClimate), of this moment.
			SceneClimate.Apply(settings, timeline, timeline.WorldSecondsAt(tick));
			// The season for foliage: from the place's own climate on the same clock, and the humidity swing the climate uses.
			if (WorldClock.Shared.HasAnchor
				&& SceneClimate.Foliage(settings, WorldClock.Shared.WorldHoursAt(tick), out SceneClimate.FoliageState season, timeline))
			{
				WeatherShaderGlobals.ApplySeason(season, settings.RuntimeHumidityOffset);
			}
			else
			{
				WeatherShaderGlobals.ClearSeason();
			}
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
			// The clock was set to another moment: everything of the new moment now, shown as it is.
			uint jumps = WorldClock.Shared.Jumps;
			if (jumps != seenJumps)
			{
				seenJumps = jumps;
				ForceStep(snap: true);
				return;
			}
			climateTimer -= deltaTime;
			if (climateTimer <= 0f)
			{
				climateTimer = ClimateInterval;
				timeline.Prune(CurrentTick());
				ApplyClimate();
			}
			presentTimer -= deltaTime;
			if (presentTimer > 0f)
			{
				return;
			}
			presentTimer = PresentInterval;
			Present();
		}

		/// <summary>
		/// Works the climate, the storms, the ground and the weather at the camera out now, off their timers; with
		/// <paramref name="snap"/>, the presenters show it at once instead of easing toward it.
		/// </summary>
		private void ForceStep(bool snap)
		{
			if (!hasTimeline || !scene.IsValid() || networkManager == null)
			{
				return;
			}
			climateTimer = ClimateInterval;
			presentTimer = PresentInterval;
			timeline.Prune(CurrentTick());
			ApplyClimate();
			if (snap)
			{
				WeatherClient.Snap();
			}
			Present();
		}

		private void Present()
		{
			PresentStorms(announce: true);
			UpdateCover();

			TimeManager tm = networkManager.TimeManager;
			uint tick = tm.Tick;
			Vector3 viewer = ViewerPosition();
			WeatherSample sample = WeatherField.Sample(timeline, settings, scene, viewer, tick);
			double preciseTick = tick + tm.GetTickPercentAsDouble();
			// The sky's own clock: the server's when anchored, else this machine's (the login backdrop, between servers).
			double hours = WorldClock.Shared.HasAnchor ? WorldClock.Shared.WorldHoursAt(preciseTick) : WorldTime.UnanchoredHours();
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
				Sample = sample,
				Shelter = sample.Shelter,
				Temperature = sample.Temperature,
				Substance = sample.Substance,
			};
			WeatherClient.Present(sample.Frame, context);
		}
	}
}
