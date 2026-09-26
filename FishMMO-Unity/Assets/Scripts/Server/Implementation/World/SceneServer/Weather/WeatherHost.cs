using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer.Weather
{
	/// <summary>
	/// The scene server's weather: one timeline per hosted world scene, the automatic storm
	/// director, surface cover and climate offsets, and the messages that keep clients in step.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Clients evaluate the same timeline against the same tick, so the only traffic is a full
	/// timeline when a character arrives and a small delta when something is started, edited or
	/// ended. Edits in one frame are batched into one revision.
	/// </para>
	/// <para>
	/// Weather is not persisted (Q4): a restarted server regenerates it from a fresh seed.
	/// </para>
	/// <para>
	/// <b>One scene per frame.</b> Each scene's pass — prune, climate, cover, director — runs about
	/// once a second as before, but the scenes take turns across the frames of that second instead
	/// of all landing on one (see <see cref="ScenePassesThisFrame"/>). A pass samples the weather
	/// and the biomes at a dozen or more points, so the one-frame version cost that many times the
	/// scene count on a single frame. Each pass is also isolated: one scene that throws is
	/// reported to its own fault log and cannot skip the scenes after it or the frame's flush.
	/// Nothing reads one scene's weather against another's, so no consumer depends on them
	/// updating together: every timeline, climate offset and cover value is per scene, and the
	/// timeline events have no subscribers that compare scenes.
	/// </para>
	/// </remarks>
	public sealed class WeatherHost : IWeatherService
	{
		/// <summary>Most storm cells a scene runs at once.</summary>
		public const int MaxCellsPerScene = 12;
		/// <summary>Scenes smaller than this get no automatic storms.</summary>
		public const float DirectorMinimumSquareKm = 2f;
		/// <summary>How often surface cover is re-sent so clients' integration cannot drift far.</summary>
		public const float CoverResyncSeconds = 30f;
		/// <summary>How often each scene's pass runs.</summary>
		public const float ScenePassSeconds = 1f;
		/// <summary>
		/// The frame rate the scene rotation is sized for: at or above it every scene gets its pass
		/// once per <see cref="ScenePassSeconds"/>; below it passes slow in proportion, never burst.
		/// </summary>
		public const int MinimumFramesPerScenePass = 30;
		/// <summary>
		/// Shortest gap the server allows between one connection's full-timeline requests. Half the
		/// honest client's own two-second cooldown, so jitter never refuses a real one.
		/// </summary>
		public const int ResyncDebounceMilliseconds = 1000;

		/// <summary>The <see cref="IngressGuard"/> operation code for a resync request.</summary>
		private const byte ResyncOperation = 1;

		private sealed class SceneWeather
		{
			public Scene Scene;
			public WorldSceneSettings Settings;
			public WeatherTimeline Timeline;
			public DeterministicRNG Rng;
			public ushort NextHandle = 1;
			public ushort NextCellID = 1;
			public bool Director;
			public uint NextSpawnTick;
			/// <summary>Degrees the prevailing wind blows TOWARD. Derived; see RefreshPrevailingWind.</summary>
			public float WindHeadingDegrees;

			/// <summary>How hard it is blowing, in metres per second. Storm cells are carried by it.</summary>
			public float WindSpeedMetersPerSecond;
			public float CoverResync;
			public WeatherDeltaBroadcast Pending;
			public bool HasPending;
			/// <summary>When, on the host's clock, this scene's pass last ran (or it was added).</summary>
			public double LastPassAt;
			/// <summary>Set when the scene unloads, so a flush already queued for it is dropped.</summary>
			public bool Removed;
			/// <summary>Fault logs for this scene's pass and flush, created on the first failure.</summary>
			public RepeatingFaultLog PassFaults;
			public RepeatingFaultLog FlushFaults;
		}

		private readonly INetworkManagerWrapper network;
		private readonly NetworkManager networkManager;
		private readonly ICharacterMappingData<NetworkConnection> characters;
		private readonly Dictionary<int, SceneWeather> scenes = new Dictionary<int, SceneWeather>();

		/// <summary>The scenes in the order their passes take turns.</summary>
		private readonly List<SceneWeather> rotation = new List<SceneWeather>();

		/// <summary>The next scene in <see cref="rotation"/> to run its pass.</summary>
		private int rotationCursor;

		/// <summary>Scene passes owed; see <see cref="ScenePassesThisFrame"/>.</summary>
		private float passCredits;

		/// <summary>Seconds of game time since this host started, advanced by each tick.</summary>
		private double clock;

		/// <summary>Scenes with a delta waiting for this frame's flush, in the order they queued.</summary>
		private readonly List<SceneWeather> flushList = new List<SceneWeather>();

		/// <summary>Debounces full-timeline requests per connection.</summary>
		private readonly IngressGuard resyncGuard = new IngressGuard();

		/// <summary>
		/// Picks each scene's cover-resync phase. Not the scene's own RNG: that stream belongs to
		/// the director, and drawing from it would change which storms every scene gets.
		/// </summary>
		private readonly System.Random phaseRandom = new System.Random();

		public WeatherHost(INetworkManagerWrapper network, ICharacterMappingData<NetworkConnection> characters)
		{
			this.network = network ?? throw new ArgumentNullException(nameof(network));
			networkManager = network.NetworkManager;
			this.characters = characters;
		}

		private uint NowTick => networkManager.TimeManager.Tick;

		// ── Lifecycle ─────────────────────────────────────────────────

		public void Start()
		{
			WeatherQuery.Clear();
			WeatherQuery.TickSource = () => networkManager.TimeManager.Tick;
			networkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
			networkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;
			networkManager.ServerManager.RegisterBroadcast<WeatherResyncRequestBroadcast>(OnResyncRequest, true);
			for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
			{
				TryAddScene(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i));
			}
		}

		public void Stop()
		{
			networkManager.SceneManager.OnLoadEnd -= SceneManager_OnLoadEnd;
			networkManager.SceneManager.OnUnloadEnd -= SceneManager_OnUnloadEnd;
			networkManager.ServerManager.UnregisterBroadcast<WeatherResyncRequestBroadcast>(OnResyncRequest);
			scenes.Clear();
			rotation.Clear();
			rotationCursor = 0;
			passCredits = 0f;
			flushList.Clear();
			resyncGuard.Clear();
			WeatherQuery.Clear();
			WeatherQuery.TickSource = null;
		}

		private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
		{
			if (args.LoadedScenes == null)
			{
				return;
			}
			foreach (Scene scene in args.LoadedScenes)
			{
				TryAddScene(scene);
			}
		}

		private void SceneManager_OnUnloadEnd(SceneUnloadEndEventArgs args)
		{
			if (args.UnloadedScenesV2 == null)
			{
				return;
			}
			foreach (UnloadedScene unloaded in args.UnloadedScenesV2)
			{
				if (scenes.TryGetValue(unloaded.Handle, out SceneWeather sw))
				{
					WeatherQuery.Unregister(sw.Scene);
					scenes.Remove(unloaded.Handle);
					RemoveFromRotation(sw);
					sw.Removed = true;
					flushList.Remove(sw);
				}
			}
		}

		private void TryAddScene(Scene scene)
		{
			if (!scene.IsValid() || scenes.ContainsKey(scene.handle) || !WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings settings))
			{
				return;
			}
			WeatherSceneMode mode = WeatherField.ResolveMode(settings, scene.name);
			// The scene's own seed, and nothing else. It used to mix in the scene handle and
			// Environment.TickCount, which meant the weather was different after every restart and
			// could not be worked out from the clock — the opposite of what the driver needs. The
			// name is stable, and the world seed on the solar system is what varies a world's
			// climate history.
			uint seed = unchecked((uint)scene.name.GetDeterministicHashCode() ^ WeatherDriver.WorldSeed);
			var timeline = new WeatherTimeline
			{
				SceneName = scene.name,
				Seed = seed,
				SceneMode = mode,
				TickDelta = networkManager.TimeManager.TickDelta,
				FixedIntensity = settings.FixedWeatherIntensity,
				FixedPresetID = CachedID(settings.FixedWeather),
				CoverTick = NowTick,
			};
			var sw = new SceneWeather
			{
				Scene = scene,
				Settings = settings,
				Timeline = timeline,
				Rng = new DeterministicRNG(unchecked((int)seed)),
				Director = settings.WeatherDirector && mode == WeatherSceneMode.Own,
			};
			/* A starting guess only. RefreshPrevailingWind replaces it with the real thing on the
			 * first director run — it cannot be derived here, because the scene's timeline has not
			 * been given its clock anchor yet and the driver has nothing to answer from. */
			sw.WindHeadingDegrees = sw.Rng.Range(0f, 360f);
			sw.WindSpeedMetersPerSecond = 4f;
			sw.NextSpawnTick = NowTick + timeline.SecondsToTicks(sw.Rng.Range(5f, 30f));
			/* Its own phase in (0, interval], so scenes loaded together — every scene at startup —
			 * do not all queue their cover on the same pass forever after. */
			sw.CoverResync = CoverResyncSeconds * (float)(1.0 - phaseRandom.NextDouble());
			sw.LastPassAt = clock;
			scenes[scene.handle] = sw;
			rotation.Add(sw);
			WeatherQuery.Register(scene, timeline);
			_ = Log.Debug("WeatherHost", $"Weather for {scene.name} (handle {scene.handle}): {mode}, director {(sw.Director ? "on" : "off")}.");
		}

		/// <summary>
		/// The cache id of an asset referenced directly (for example by a scene). Registers it when
		/// the addressable load has not, so the id the client receives resolves.
		/// </summary>
		private static int CachedID(WeatherPreset preset)
		{
			if (preset == null)
			{
				return 0;
			}
			if (preset.ID == 0)
			{
				preset.AddToCache(preset.name);
			}
			return preset.ID;
		}

		// ── Clients ───────────────────────────────────────────────────

		/// <summary>Sends a newly arrived character its scene's whole timeline.</summary>
		public void OnCharacterSpawned(NetworkConnection connection, Scene scene)
		{
			if (connection != null && scenes.TryGetValue(scene.handle, out SceneWeather sw))
			{
				network.Broadcast(connection, sw.Timeline.ToBroadcast(), true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Answers a client that found a gap in its deltas with the whole timeline.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Each answer copies the layer and cell lists and sends the full timeline reliably, and
		/// only the honest client paces itself, so a modified one could make the server amplify
		/// traffic at will. Two checks stop that. A request whose revision is already the
		/// current one is answered with nothing (<see cref="ResyncWanted"/>), and a connection
		/// gets at most one answer per <see cref="ResyncDebounceMilliseconds"/>.
		/// </para>
		/// <para>
		/// A dropped request costs an honest client nothing it cannot recover: it only asks after
		/// a delta skips a revision, and the next delta that does so asks again.
		/// </para>
		/// </remarks>
		private void OnResyncRequest(NetworkConnection connection, WeatherResyncRequestBroadcast msg, Channel channel)
		{
			if (connection == null || !characters.ConnectionCharacters.TryGetValue(connection, out IPlayerCharacter character) || character?.GameObject == null)
			{
				return;
			}
			if (!scenes.TryGetValue(character.GameObject.scene.handle, out SceneWeather sw) ||
				!ResyncWanted(msg.HaveRevision, sw.Timeline.Revision))
			{
				return;
			}
			if (!resyncGuard.TryBegin(connection.ClientId, ResyncOperation, ResyncDebounceMilliseconds, out long guardKey))
			{
				return;
			}
			try
			{
				network.Broadcast(connection, sw.Timeline.ToBroadcast(), true, Channel.Reliable);
			}
			finally
			{
				resyncGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Whether a resync request needs an answer.
		/// </summary>
		/// <remarks>
		/// Not when the client already holds the current revision: the server's revision moves
		/// only when a delta is sent, so there is nothing it lacks. An honest client never asks in
		/// that state — it asks when a delta arrives more than one revision ahead of it, and the
		/// server is at least there — so only a client asking for the sake of it is refused.
		/// Anything else is answered, including a revision AHEAD of the server's: that is another
		/// scene's counter, left over from a scene change whose own timeline is on its way.
		/// </remarks>
		/// <param name="haveRevision">The revision the client says it holds.</param>
		/// <param name="currentRevision">The scene's current revision.</param>
		internal static bool ResyncWanted(uint haveRevision, uint currentRevision)
		{
			return haveRevision != currentRevision;
		}

		/// <summary>
		/// Sends a message to every connection in a scene, serialised once.
		/// </summary>
		/// <remarks>
		/// FishNet's own per-scene connection set rather than a walk over every character on the
		/// server asking each for its scene, and one serialisation rather than one per recipient.
		/// A delta that reaches a client which has not yet received its full timeline, or holds
		/// another scene's, is ignored there by scene name and revision.
		/// </remarks>
		private void SendToScene<T>(SceneWeather sw, T message) where T : struct, FishNet.Broadcast.IBroadcast
		{
			network.BroadcastToScene(sw.Scene, message, true, Channel.Reliable);
		}

		private ref WeatherDeltaBroadcast Pending(SceneWeather sw)
		{
			if (!sw.HasPending)
			{
				sw.Pending = new WeatherDeltaBroadcast { SceneName = sw.Timeline.SceneName };
				sw.HasPending = true;
				flushList.Add(sw);
			}
			return ref sw.Pending;
		}

		private void QueueLayer(SceneWeather sw, WeatherLayerEntry entry)
		{
			sw.Timeline.UpsertLayer(entry);
			ref WeatherDeltaBroadcast d = ref Pending(sw);
			(d.Layers ??= new List<WeatherLayerEntry>()).RemoveAll(l => l.Handle == entry.Handle);
			d.Layers.Add(entry);
		}

		private void QueueCell(SceneWeather sw, StormCell cell, bool isNew)
		{
			sw.Timeline.UpsertCell(cell);
			ref WeatherDeltaBroadcast d = ref Pending(sw);
			(d.Cells ??= new List<StormCell>()).RemoveAll(c => c.ID == cell.ID);
			d.Cells.Add(cell);
			if (isNew)
			{
				WeatherEvents.RaiseCellSpawned(sw.Scene, cell);
			}
		}

		private void QueueClimate(SceneWeather sw, WeatherClimateEntry climate)
		{
			sw.Timeline.Climate = climate;
			ref WeatherDeltaBroadcast d = ref Pending(sw);
			d.HasClimate = true;
			d.Climate = climate;
		}

		private void QueueCover(SceneWeather sw)
		{
			ref WeatherDeltaBroadcast d = ref Pending(sw);
			d.HasCover = true;
			d.Cover = sw.Timeline.Cover;
			d.CoverTick = sw.Timeline.CoverTick;
		}

		/// <summary>
		/// Sends each scene's batched edits as one delta.
		/// </summary>
		/// <remarks>
		/// Each scene is isolated, like its pass. The delta is taken off the scene before it is
		/// sent, so a send that throws is not retried every frame under a fresh revision; the
		/// clients see the gap on the next delta and ask for the whole timeline. Anything queued
		/// while flushing — a timeline listener making an edit — waits for the next frame.
		/// </remarks>
		private void Flush()
		{
			int count = flushList.Count;
			if (count < 1)
			{
				return;
			}
			for (int i = 0; i < count; i++)
			{
				SceneWeather sw = flushList[i];
				if (sw.Removed || !sw.HasPending)
				{
					continue;
				}
				WeatherDeltaBroadcast delta = sw.Pending;
				sw.HasPending = false;
				sw.Pending = default;
				sw.Timeline.Revision++;
				delta.Revision = sw.Timeline.Revision;
				try
				{
					SendToScene(sw, delta);
					WeatherEvents.RaiseTimelineChanged(sw.Scene, sw.Timeline);
					sw.FlushFaults?.ReportSuccess();
				}
				catch (Exception ex)
				{
					sw.FlushFaults ??= new RepeatingFaultLog("WeatherHost", $"Weather flush for {sw.Timeline.SceneName} (handle {sw.Scene.handle})");
					sw.FlushFaults.Report(ex, Time.realtimeSinceStartupAsDouble);
				}
			}
			flushList.RemoveRange(0, count);
		}

		// ── Tick ──────────────────────────────────────────────────────

		public void Tick(float deltaTime)
		{
			clock += deltaTime;

			int passes = ScenePassesThisFrame(ref passCredits, rotation.Count, deltaTime);
			for (int i = 0; i < passes && rotation.Count > 0; i++)
			{
				if (rotationCursor >= rotation.Count)
				{
					rotationCursor = 0;
				}
				RunScenePass(rotation[rotationCursor++]);
			}

			Flush();
			resyncGuard.Sweep(sweepIntervalSeconds: 10f, entryTtlSeconds: 30f, maxRemovals: 256);
		}

		/// <summary>
		/// How many scene passes to run this frame, so that every scene gets one about once per
		/// <see cref="ScenePassSeconds"/>, spread across the frames rather than all on one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A token bucket: each frame earns passes in proportion to the scene count and the time
		/// that passed, and at most a whole round of them is ever owed, so a hitch does not bank a
		/// second pass for any scene. The passes run are capped at
		/// <c>ceil(scenes / <see cref="MinimumFramesPerScenePass"/>)</c> a frame — one, for thirty
		/// scenes or fewer — so the backlog after a hitch drains over the following frames instead
		/// of bursting on one.
		/// </para>
		/// <para>
		/// <b>No scene is starved.</b> The rotation takes scenes strictly in turn, and at or above
		/// <see cref="MinimumFramesPerScenePass"/> frames a second the cap allows at least a whole
		/// round a second; below it every scene still gets its turn, only less often, and its pass
		/// is handed the real time since its last one.
		/// </para>
		/// </remarks>
		/// <param name="credits">Passes owed and not yet run; carried from frame to frame.</param>
		/// <param name="sceneCount">Scenes in the rotation.</param>
		/// <param name="deltaTime">Seconds since the last frame.</param>
		/// <returns>The passes to run this frame.</returns>
		internal static int ScenePassesThisFrame(ref float credits, int sceneCount, float deltaTime)
		{
			if (sceneCount < 1)
			{
				credits = 0f;
				return 0;
			}

			credits += sceneCount * Mathf.Max(0f, deltaTime) / ScenePassSeconds;
			if (credits > sceneCount)
			{
				credits = sceneCount;
			}

			int cap = Mathf.Max(1, (sceneCount + MinimumFramesPerScenePass - 1) / MinimumFramesPerScenePass);
			int passes = Mathf.Min((int)credits, cap);
			credits -= passes;
			return passes;
		}

		/// <summary>
		/// One scene's pass: prune, climate, driver, cover and director, over the time since its
		/// last one. A throw is reported to the scene's own fault log.
		/// </summary>
		private void RunScenePass(SceneWeather sw)
		{
			float seconds = (float)(clock - sw.LastPassAt);
			sw.LastPassAt = clock;
			if (sw.Settings == null)
			{
				return;
			}
			try
			{
				uint now = NowTick;
				double worldHours = WorldClock.Shared.HasAnchor ? WorldClock.Shared.WorldHoursAt(now) : 0;
				sw.Timeline.Prune(now);
				ApplyClimate(sw, now, worldHours);
				DriveWeather(sw, now, worldHours);
				if (sw.Timeline.SceneMode != WeatherSceneMode.None)
				{
					IntegrateCover(sw, now, seconds);
					RunDirector(sw, now);
				}
				sw.PassFaults?.ReportSuccess();
			}
			catch (Exception ex)
			{
				sw.PassFaults ??= new RepeatingFaultLog("WeatherHost", $"Weather pass for {sw.Timeline.SceneName} (handle {sw.Scene.handle})");
				sw.PassFaults.Report(ex, Time.realtimeSinceStartupAsDouble);
			}
		}

		/// <summary>Takes a scene out of the pass rotation, keeping the cursor on the scene it was on.</summary>
		private void RemoveFromRotation(SceneWeather sw)
		{
			int index = rotation.IndexOf(sw);
			if (index < 0)
			{
				return;
			}
			rotation.RemoveAt(index);
			if (index < rotationCursor)
			{
				rotationCursor--;
			}
		}

		/// <summary>
		/// Hands the weather driver where and when this scene is.
		/// </summary>
		/// <remarks>
		/// The driver is a pure function of the world clock, the place and the season, so these five
		/// numbers are the whole of what it needs — and they are carried on the timeline precisely
		/// because the timeline is what the client already has. Given them, a client works out the
		/// same highs, lows and fronts the server does without a byte being sent for the weather
		/// itself, and a player who logs in tomorrow gets the weather that was always going to be
		/// there rather than whatever the server happened to roll.
		/// </remarks>
		private static void DriveWeather(SceneWeather sw, uint tick, double worldHours)
		{
			WeatherTimeline timeline = sw.Timeline;
			timeline.WorldSecondsAtTick = worldHours * 3600.0;
			timeline.WorldSecondsTick = tick;
			timeline.LatitudeDegrees = sw.Settings.Latitude;
			timeline.LongitudeDegrees = sw.Settings.Longitude;
			timeline.Driver = sw.Timeline.SceneMode == WeatherSceneMode.Own;
		}

		/// <summary>
		/// Writes the scene's runtime climate offsets: the timeline's shift plus what the scene's
		/// body gets from its star. The client runs the same sum.
		/// </summary>
		private static void ApplyClimate(SceneWeather sw, uint tick, double worldHours)
		{
			sw.Timeline.ClimateAt(tick, out float temperature, out float humidity);
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = SceneTime.BodyOf(sw.Settings);
			if (system != null && body != null)
			{
				CelestialMath.ClimateOffsets(system, body, worldHours, out float bodyTemperature, out float bodyHumidity, sw.Settings.Latitude);
				temperature += bodyTemperature;
				humidity += bodyHumidity;
			}
			sw.Settings.RuntimeTemperatureOffset = Mathf.Clamp(temperature, -2f, 2f);
			sw.Settings.RuntimeHumidityOffset = Mathf.Clamp(humidity, -2f, 2f);
		}

		/// <summary>
		/// Advances the scene's snow, wetness, ash and sand from the weather at five sample points.
		/// A per-area grid replaces this with the surface shaders (phase P3).
		/// </summary>
		private void IntegrateCover(SceneWeather sw, uint tick, float seconds)
		{
			if (!TryGetArea(sw, out Rect area))
			{
				area = new Rect(-50f, -50f, 100f, 100f);
			}
			var average = new WeatherAccumulator();
			float temperature = 0f;
			Vector2 c = area.center;
			for (int i = 0; i < CoverSampleOffsets.Length; i++)
			{
				Vector2 p = c + Vector2.Scale(CoverSampleOffsets[i], area.size);
				WeatherSample sample = WeatherField.Sample(sw.Timeline, sw.Settings, sw.Scene, new Vector3(p.x, 0f, p.y), tick);
				average.Add(sample.Frame, 1f / CoverSampleOffsets.Length);
				temperature += sample.Temperature / CoverSampleOffsets.Length;
			}
			WeatherFrame frame = average.Resolve();
			double coverHours = WorldClock.Shared.HasAnchor ? WorldClock.Shared.WorldHoursAt(tick) : 0;
			sw.Timeline.Cover.Integrate(frame, temperature, seconds, SceneTime.IsDaylight(sw.Settings, coverHours) ? 1f : 0f);
			sw.Timeline.CoverTick = tick;
			sw.CoverResync -= seconds;
			if (sw.CoverResync <= 0f)
			{
				sw.CoverResync = CoverResyncSeconds;
				QueueCover(sw);
			}
		}

		/// <summary>
		/// Where cover is sampled, as fractions of the scene's area from its centre: the centre and
		/// the middle of each quarter. Static, so a pass does not allocate the list.
		/// </summary>
		private static readonly Vector2[] CoverSampleOffsets =
		{
			Vector2.zero,
			new Vector2(-0.25f, -0.25f),
			new Vector2(0.25f, -0.25f),
			new Vector2(-0.25f, 0.25f),
			new Vector2(0.25f, 0.25f),
		};

		/// <summary>The scene's world-space X/Z rectangle, from its biome map or its terrain.</summary>
		private static bool TryGetArea(SceneWeather sw, out Rect area)
		{
			SceneBiomeMap map = sw.Settings != null ? sw.Settings.BiomeMap : null;
			if (map != null && map.WorldSize.x > 0f && map.WorldSize.y > 0f)
			{
				area = new Rect(map.WorldOrigin, map.WorldSize);
				return true;
			}
			/* One measurement of the scene's ground, shared with the biome sampler. This used to
			 * union the tiles here as well, which was the same arithmetic written twice — and the
			 * two could drift, leaving the director working over a different landmass from the one
			 * the climate was being read against. */
			SceneTerrainExtent extent = SceneTerrainExtent.Of(sw.Scene);
			area = extent.Area;
			return extent.Found;
		}

		// ── Director ──────────────────────────────────────────────────

		private void RunDirector(SceneWeather sw, uint now)
		{
			if (!sw.Director || sw.Timeline.SceneMode != WeatherSceneMode.Own || !TryGetArea(sw, out Rect area))
			{
				return;
			}
			float squareKm = area.width * area.height / 1_000_000f;
			if (squareKm < DirectorMinimumSquareKm)
			{
				return;
			}
			WeatherTimeline timeline = sw.Timeline;

			// Retire cells that left the scene or drifted over a biome they cannot live in.
			int alive = 0;
			for (int i = 0; i < timeline.Cells.Count; i++)
			{
				StormCell cell = timeline.Cells[i];
				if (cell.DecayTick <= now)
				{
					continue;
				}
				alive++;
				Vector2 centre = cell.CentreAt(now, timeline.TickDelta);
				/* ReachMeters, not the radius: a front reaches far further along its line than
				 * across it, so measuring by radius would retire one the moment its CENTRE neared
				 * the edge, with most of the wall still over the scene. */
				float reach = cell.ReachMeters;
				Rect grown = new Rect(area.xMin - reach, area.yMin - reach, area.width + reach * 2f, area.height + reach * 2f);
				bool outside = !grown.Contains(centre);
				bool hostile = false;
				if (!outside)
				{
					BiomeReading reading = BiomeSampler.Read(new Vector3(centre.x, 0f, centre.y), sw.Settings);
					BiomeWeatherProfile profile = WeatherField.ProfileFor(reading.Biome, sw.Settings);
					WeatherPreset preset = WeatherPreset.Get<WeatherPreset>(cell.PresetID);
					hostile = profile != null && profile.SuitabilityOf(preset) < 0.25f;
				}
				if (outside || hostile)
				{
					Retire(sw, ref cell, now, outside ? 30f : 120f);
					timeline.Cells[i] = cell;
					alive--;
				}
			}

			if (now < sw.NextSpawnTick || alive >= MaxCellsPerScene)
			{
				return;
			}
			RefreshPrevailingWind(sw, area, now);
			float density = AverageDensity(sw, area);
			int target = Mathf.Min(MaxCellsPerScene, Mathf.RoundToInt(squareKm * density));
			if (alive >= target)
			{
				sw.NextSpawnTick = now + timeline.SecondsToTicks(sw.Rng.Range(30f, 90f));
				return;
			}
			TrySpawnFromBiomes(sw, area, now);
			sw.NextSpawnTick = now + timeline.SecondsToTicks(sw.Rng.Range(60f, 240f));
		}

		/// <summary>
		/// Takes the scene's prevailing wind from the driver, rather than from a number rolled once
		/// when the scene loaded.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The heading used to be <c>Rng.Range(0, 360)</c> at startup and never touched again, so
		/// every storm in a scene drifted the same way for as long as the server ran — and for a
		/// FRONT that is worse than for a shower, because a front's line is perpendicular to its
		/// velocity, so a stale heading gives a permanently wrongly-oriented wall.
		/// </para>
		/// <para>
		/// The driver has the real answer: trade winds, westerlies and polar easterlies by latitude,
		/// leaning poleward, breathing over hours. <see cref="WeatherSample.Air"/> carries it
		/// already, so this only has to ask — at the scene's centre, once per director pass, which is
		/// every half minute or so. Weather turns slowly; it does not need asking more often.
		/// </para>
		/// <para>
		/// Keeps the last good answer when the driver has nothing to say — a scene with no driver, or
		/// air so still there is no direction in it. A zero vector would otherwise snap every storm
		/// to due north.
		/// </para>
		/// </remarks>
		private static void RefreshPrevailingWind(SceneWeather sw, Rect area, uint now)
		{
			var centre = new Vector3(area.center.x, 0f, area.center.y);
			WeatherSample sample = WeatherField.Sample(sw.Timeline, sw.Settings, sw.Scene, centre, now);
			Vector2 wind = sample.Air.Wind;
			if (wind.sqrMagnitude < 1e-4f)
			{
				return;
			}
			sw.WindHeadingDegrees = Mathf.Repeat(Mathf.Atan2(wind.x, wind.y) * Mathf.Rad2Deg, 360f);
			sw.WindSpeedMetersPerSecond = wind.magnitude;
		}

		private static float AverageDensity(SceneWeather sw, Rect area)
		{
			float total = 0f;
			int samples = 0;
			for (int x = 0; x < 3; x++)
			{
				for (int z = 0; z < 3; z++)
				{
					var p = new Vector3(area.xMin + area.width * (x + 0.5f) / 3f, 0f, area.yMin + area.height * (z + 0.5f) / 3f);
					BiomeWeatherProfile profile = WeatherField.ProfileFor(BiomeSampler.Read(p, sw.Settings).Biome, sw.Settings);
					total += profile != null ? profile.CellsPerSquareKm : 0f;
					samples++;
				}
			}
			return samples > 0 ? total / samples : 0f;
		}

		private void TrySpawnFromBiomes(SceneWeather sw, Rect area, uint now)
		{
			const float PreferredDistanceFromPlayers = 600f;
			for (int attempt = 0; attempt < 8; attempt++)
			{
				var p = new Vector3(sw.Rng.Range(area.xMin, area.xMax), 0f, sw.Rng.Range(area.yMin, area.yMax));
				BiomeReading reading = BiomeSampler.Read(p, sw.Settings);
				BiomeWeatherProfile profile = WeatherField.ProfileFor(reading.Biome, sw.Settings);
				WeatherPreset preset = profile?.PickCellPreset(sw.Rng);
				if (preset == null)
				{
					continue;
				}
				// Storms should roll in, not appear on top of someone: prefer spots out of sight.
				if (attempt < 6 && NearestPlayerDistance(sw, p) < PreferredDistanceFromPlayers)
				{
					continue;
				}
				float heading = (sw.WindHeadingDegrees + sw.Rng.Range(-35f, 35f)) * Mathf.Deg2Rad;
				/* Carried by the wind that is actually blowing, not by a fixed 2–6 m/s. A cell moves
				 * with the air it is in, so a stiff day drives weather across a scene in minutes and
				 * a still one leaves it hanging about — which is most of what makes one day feel
				 * different from another. Kept below the wind itself: a storm lags its steering flow. */
				float carried = Mathf.Clamp(sw.WindSpeedMetersPerSecond * 0.55f, 1.5f, 14f);
				float speed = carried * sw.Rng.Range(0.75f, 1.25f);
				float radius = sw.Rng.Range(preset.CellRadiusMeters.x, Mathf.Max(preset.CellRadiusMeters.x, preset.CellRadiusMeters.y));
				float minutes = sw.Rng.Range(preset.DurationMinutes.x, Mathf.Max(preset.DurationMinutes.x, preset.DurationMinutes.y));
				SpawnCellInternal(sw, preset, p, radius, new Vector2(Mathf.Sin(heading), Mathf.Cos(heading)) * speed, minutes * 60f, now);
				return;
			}
		}

		/// <summary>
		/// Distance in X/Z from a point to the nearest player in the scene, or
		/// <see cref="float.MaxValue"/> when there is none.
		/// </summary>
		/// <remarks>
		/// Walks the scene's own connections rather than every character on the server asking each
		/// for its scene.
		/// </remarks>
		private float NearestPlayerDistance(SceneWeather sw, Vector3 p)
		{
			float best = float.MaxValue;
			if (!network.TryGetSceneConnections(sw.Scene, out HashSet<NetworkConnection> connections))
			{
				return best;
			}
			foreach (NetworkConnection connection in connections)
			{
				if (connection == null ||
					!characters.ConnectionCharacters.TryGetValue(connection, out IPlayerCharacter character) ||
					character?.GameObject == null)
				{
					continue;
				}
				Vector3 c = character.Transform.position;
				best = Mathf.Min(best, Vector2.Distance(new Vector2(c.x, c.z), new Vector2(p.x, p.z)));
			}
			return best;
		}

		private ushort SpawnCellInternal(SceneWeather sw, WeatherPreset preset, Vector3 at, float radius, Vector2 velocity, float lifetimeSeconds, uint now)
		{
			WeatherTimeline timeline = sw.Timeline;
			uint birth = now + timeline.LeadTicks;
			float matureSeconds = Mathf.Clamp(lifetimeSeconds * 0.15f, 20f, 180f);
			float decaySeconds = Mathf.Clamp(lifetimeSeconds * 0.2f, 20f, 240f);
			float holdSeconds = Mathf.Max(0f, lifetimeSeconds - matureSeconds - decaySeconds);
			var cell = new StormCell
			{
				ID = NextCellID(sw),
				PresetID = CachedID(preset),
				Seed = (uint)sw.Rng.Next(),
				OriginX = at.x,
				OriginZ = at.z,
				VelocityX = velocity.x,
				VelocityZ = velocity.y,
				RadiusMeters = Mathf.Max(10f, radius),
				/* The shape comes from the PRESET, not from whoever asked for the cell. A front is
				 * a property of the weather, not of the call that spawned it, so an admin command,
				 * an ECA action and the director all produce the same shape for the same preset. */
				Shape = preset != null ? preset.CellShape : StormCellShape.Disc,
				ExtentMeters = preset != null
					? sw.Rng.Range(preset.CellExtentMeters.x, Mathf.Max(preset.CellExtentMeters.x, preset.CellExtentMeters.y))
					: 0f,
				PeakIntensity = 1f,
				// A wall wanders less than a shower: it is held in shape by the air pushing it.
				MeanderMeters = Mathf.Max(10f, radius) * (preset != null && preset.CellShape == StormCellShape.Front ? 0.04f : 0.15f),
				MotionTick = birth,
				BirthTick = birth,
				MatureTick = birth + timeline.SecondsToTicks(matureSeconds),
			};
			cell.DecayTick = cell.MatureTick + timeline.SecondsToTicks(holdSeconds);
			cell.DeathTick = cell.DecayTick + timeline.SecondsToTicks(decaySeconds);
			QueueCell(sw, cell, isNew: true);
			return cell.ID;
		}

		private static ushort NextCellID(SceneWeather sw)
		{
			for (int guard = 0; guard < 65535; guard++)
			{
				ushort id = sw.NextCellID++;
				if (sw.NextCellID == 0) sw.NextCellID = 1;
				if (id != 0 && !sw.Timeline.TryGetCell(id, out _))
				{
					return id;
				}
			}
			return 0;
		}

		private static ushort NextHandle(SceneWeather sw)
		{
			for (int guard = 0; guard < 65535; guard++)
			{
				ushort handle = sw.NextHandle++;
				if (sw.NextHandle == 0) sw.NextHandle = 1;
				if (handle != 0 && !sw.Timeline.TryGetLayer(handle, out _))
				{
					return handle;
				}
			}
			return 0;
		}

		private void Retire(SceneWeather sw, ref StormCell cell, uint now, float fadeSeconds)
		{
			uint start = now + sw.Timeline.LeadTicks;
			if (cell.DecayTick <= start)
			{
				return;
			}
			// Keep the envelope continuous: a still-growing cell fades from where it is.
			if (cell.MatureTick > start)
			{
				cell.MatureTick = start;
			}
			cell.DecayTick = start;
			cell.DeathTick = start + sw.Timeline.SecondsToTicks(Mathf.Max(1f, fadeSeconds));
			QueueCell(sw, cell, isNew: false);
			WeatherEvents.RaiseCellRetired(sw.Scene, cell.ID);
		}

		// ── IWeatherService ───────────────────────────────────────────

		private bool TryGet(Scene scene, out SceneWeather sw)
		{
			return scenes.TryGetValue(scene.handle, out sw) && sw.Timeline.SceneMode != WeatherSceneMode.None;
		}

		public bool TryGetTimeline(Scene scene, out WeatherTimeline timeline)
		{
			bool found = scenes.TryGetValue(scene.handle, out SceneWeather sw);
			timeline = found ? sw.Timeline : null;
			return found;
		}

		public bool IsDirectorEnabled(Scene scene) => scenes.TryGetValue(scene.handle, out SceneWeather sw) && sw.Director;

		public bool ApplyPreset(Scene scene, WeatherPreset preset, float intensity, float transitionSeconds)
		{
			if (preset == null || !TryGet(scene, out SceneWeather sw))
			{
				return false;
			}
			// The weather going out holds on while the new weather rises: cloud and fog take the
			// greater of the layers over them, so two straight ramps would cross halfway and the sky
			// would pass through half-clear between one weather and the next.
			ClearLayers(scene, transitionSeconds * (1f + WeatherTimeline.HandoverHold));
			foreach (WeatherPresetLayer layer in preset.Layers)
			{
				if (layer?.Template != null)
				{
					AddLayer(scene, layer.Template, Mathf.Clamp01(layer.Intensity * intensity), transitionSeconds * (1f - WeatherTimeline.HandoverHold * 0.5f));
				}
			}
			return true;
		}

		public ushort AddLayer(Scene scene, WeatherLayerTemplate template, float intensity, float transitionSeconds)
		{
			if (template == null || !TryGet(scene, out SceneWeather sw))
			{
				return 0;
			}
			if (template.ID == 0)
			{
				template.AddToCache(template.name);
			}
			uint start = NowTick + sw.Timeline.LeadTicks;
			var entry = new WeatherLayerEntry
			{
				Handle = NextHandle(sw),
				TemplateID = template.ID,
				From = 0f,
				To = Mathf.Clamp01(intensity),
				StartTick = start,
				EndTick = start + sw.Timeline.SecondsToTicks(Mathf.Max(0f, transitionSeconds)),
			};
			if (entry.Handle == 0)
			{
				return 0;
			}
			QueueLayer(sw, entry);
			return entry.Handle;
		}

		public bool SetLayerIntensity(Scene scene, ushort handle, float intensity, float transitionSeconds)
		{
			if (!TryGet(scene, out SceneWeather sw) || !sw.Timeline.TryGetLayer(handle, out int index))
			{
				return false;
			}
			WeatherLayerEntry entry = sw.Timeline.Layers[index];
			uint start = NowTick + sw.Timeline.LeadTicks;
			entry.From = entry.IntensityAt(start);
			entry.To = Mathf.Clamp01(intensity);
			entry.StartTick = start;
			entry.EndTick = start + sw.Timeline.SecondsToTicks(Mathf.Max(0f, transitionSeconds));
			entry.RemoveWhenDone = false;
			QueueLayer(sw, entry);
			return true;
		}

		public bool RemoveLayer(Scene scene, ushort handle, float transitionSeconds)
		{
			if (!SetLayerIntensity(scene, handle, 0f, transitionSeconds))
			{
				return false;
			}
			SceneWeather sw = scenes[scene.handle];
			sw.Timeline.TryGetLayer(handle, out int index);
			WeatherLayerEntry entry = sw.Timeline.Layers[index];
			entry.RemoveWhenDone = true;
			QueueLayer(sw, entry);
			return true;
		}

		public bool ClearLayers(Scene scene, float transitionSeconds)
		{
			if (!TryGet(scene, out SceneWeather sw))
			{
				return false;
			}
			var handles = new List<ushort>();
			foreach (WeatherLayerEntry entry in sw.Timeline.Layers)
			{
				if (!entry.RemoveWhenDone)
				{
					handles.Add(entry.Handle);
				}
			}
			foreach (ushort handle in handles)
			{
				RemoveLayer(scene, handle, transitionSeconds);
			}
			return true;
		}

		public ushort SpawnCell(Scene scene, WeatherPreset preset, Vector3 at, float radiusMeters, Vector2 velocity, float lifetimeSeconds)
		{
			if (preset == null || !TryGet(scene, out SceneWeather sw) || sw.Timeline.SceneMode != WeatherSceneMode.Own)
			{
				return 0;
			}
			return SpawnCellInternal(sw, preset, at, radiusMeters, velocity, Mathf.Max(30f, lifetimeSeconds), NowTick);
		}

		public bool SteerCell(Scene scene, ushort id, Vector3 towards, float speed)
		{
			if (!TryGet(scene, out SceneWeather sw) || !sw.Timeline.TryGetCell(id, out int index))
			{
				return false;
			}
			StormCell cell = sw.Timeline.Cells[index];
			uint start = NowTick + sw.Timeline.LeadTicks;
			// Re-base the motion where the cell will be when the change lands; the meander restarts from there.
			Vector2 from = cell.CentreAt(start, sw.Timeline.TickDelta);
			Vector2 direction = new Vector2(towards.x, towards.z) - from;
			Vector2 velocity = direction.sqrMagnitude > 1f ? direction.normalized * Mathf.Max(0f, speed) : Vector2.zero;
			cell.OriginX = from.x;
			cell.OriginZ = from.y;
			cell.VelocityX = velocity.x;
			cell.VelocityZ = velocity.y;
			cell.MotionTick = start;
			cell.MeanderMeters = 0f;
			QueueCell(sw, cell, isNew: false);
			return true;
		}

		public bool RetireCell(Scene scene, ushort id, float fadeSeconds)
		{
			if (!TryGet(scene, out SceneWeather sw) || !sw.Timeline.TryGetCell(id, out int index))
			{
				return false;
			}
			StormCell cell = sw.Timeline.Cells[index];
			Retire(sw, ref cell, NowTick, fadeSeconds);
			return true;
		}

		public bool SetDirector(Scene scene, bool enabled)
		{
			if (!scenes.TryGetValue(scene.handle, out SceneWeather sw))
			{
				return false;
			}
			sw.Director = enabled && sw.Timeline.SceneMode == WeatherSceneMode.Own;
			return sw.Director == enabled;
		}

		public bool SetClimateOffset(Scene scene, float temperature, float humidity, float transitionSeconds)
		{
			if (!scenes.TryGetValue(scene.handle, out SceneWeather sw))
			{
				return false;
			}
			uint start = NowTick + sw.Timeline.LeadTicks;
			sw.Timeline.Climate.At(start, out float fromT, out float fromH);
			QueueClimate(sw, new WeatherClimateEntry
			{
				FromTemperature = fromT,
				FromHumidity = fromH,
				ToTemperature = Mathf.Clamp(temperature, -1f, 1f),
				ToHumidity = Mathf.Clamp(humidity, -1f, 1f),
				StartTick = start,
				EndTick = start + sw.Timeline.SecondsToTicks(Mathf.Max(0f, transitionSeconds)),
			});
			return true;
		}
	}
}
