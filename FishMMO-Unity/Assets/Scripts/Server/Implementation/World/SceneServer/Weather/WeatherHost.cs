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
	/// The scene server's weather: one timeline per hosted world scene, its own storms
	/// (<see cref="StormSchedule"/>), surface cover and climate offsets, and the messages that keep
	/// clients in step.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Clients evaluate the same timeline against the same tick, so the only traffic is a full
	/// timeline when a character arrives and a small delta when something is started, edited or
	/// ended. Edits in one frame are batched into one revision.
	/// </para>
	/// <para>
	/// <b>Nothing of the scene's own weather is sent.</b> Its storms and its ground are pure functions
	/// of the world's seed, the scene and the world clock (<see cref="StormSchedule"/>,
	/// <see cref="GroundCover"/>), worked out by the server and by each client alike; a restart, a
	/// join or a set of the clock lands every side on the same storms and the same ground. Only what
	/// a person did — an admin's storm, the air an admin added, the director's switch — is sent.
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
			/// <summary>The dice for the size of a storm an admin starts without saying (sent, so it need not be pure).</summary>
			public DeterministicRNG Rng;
			/// <summary>The next id for a storm an admin or a script starts: always below <see cref="StormSchedule.FirstID"/>.</summary>
			public ushort NextCellID = 1;
			/// <summary>The scene's ground, worked out from the world time.</summary>
			public readonly SceneCover Cover = new SceneCover();
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

		/// <summary>The schedule's storms born on a pass, to announce.</summary>
		private readonly List<StormCell> born = new List<StormCell>();

		public WeatherHost(INetworkManagerWrapper network, ICharacterMappingData<NetworkConnection> characters)
		{
			this.network = network ?? throw new ArgumentNullException(nameof(network));
			networkManager = network.NetworkManager;
			this.characters = characters;
		}

		private uint NowTick => networkManager.TimeManager.Tick;

		/// <summary>World seconds now, by a scene's timeline (the world clock, at the pace an admin set).</summary>
		private double NowSeconds(SceneWeather sw) => sw.Timeline.WorldSecondsAt(NowTick);

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
			};
			// The rectangle the scene's own storms are born in and its cover is read over: measured here, and sent, so a
			// client works its storms out over the same ground whatever of the scene it has loaded.
			timeline.Area = SceneCoverSampling.TryGetArea(settings, scene, out Rect area) ? area : default;
			timeline.Director = settings.WeatherDirector && mode == WeatherSceneMode.Own;
			timeline.LatitudeDegrees = settings.Latitude;
			timeline.LongitudeDegrees = settings.Longitude;
			var sw = new SceneWeather
			{
				Scene = scene,
				Settings = settings,
				Timeline = timeline,
				Rng = new DeterministicRNG(unchecked((int)seed)),
			};
			sw.LastPassAt = clock;
			scenes[scene.handle] = sw;
			rotation.Add(sw);
			WeatherQuery.Register(scene, timeline);
			_ = Log.Debug("WeatherHost", $"Weather for {scene.name} (handle {scene.handle}): {mode}, director {(timeline.Director ? "on" : "off")}.");
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

		private void QueueAir(SceneWeather sw, AirOffsetEntry air)
		{
			sw.Timeline.Air = air;
			ref WeatherDeltaBroadcast d = ref Pending(sw);
			d.HasAir = true;
			d.Air = air;
		}

		private void QueueDirector(SceneWeather sw, bool enabled)
		{
			sw.Timeline.Director = enabled;
			ref WeatherDeltaBroadcast d = ref Pending(sw);
			d.HasDirector = true;
			d.Director = enabled;
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
		/// One scene's pass: prune, climate, driver, its own storms and its cover, at the world's
		/// time now. A throw is reported to the scene's own fault log.
		/// </summary>
		private void RunScenePass(SceneWeather sw)
		{
			sw.LastPassAt = clock;
			if (sw.Settings == null)
			{
				return;
			}
			try
			{
				uint now = NowTick;
				double worldHours = WorldClock.Shared.HasAnchor ? WorldClock.Shared.WorldHoursAt(now) : 0;
				double nowSeconds = sw.Timeline.WorldSecondsAt(now);
				sw.Timeline.Prune(now);
				SceneClimate.Apply(sw.Settings, sw.Timeline, nowSeconds);
				DriveWeather(sw, now, worldHours);
				if (sw.Timeline.SceneMode != WeatherSceneMode.None)
				{
					// The scene's own storms of this moment, as every client works them out: nothing is sent.
					born.Clear();
					if (StormSchedule.Present(sw.Timeline, sw.Settings, sw.Scene, nowSeconds, born))
					{
						foreach (StormCell cell in born)
						{
							WeatherEvents.RaiseCellSpawned(sw.Scene, cell);
						}
						WeatherEvents.RaiseTimelineChanged(sw.Scene, sw.Timeline);
					}
					// The ground of this moment over the scene's cover points: the figure gameplay reads.
					sw.Cover.Update(sw.Timeline, sw.Settings, sw.Scene, nowSeconds);
					RetireSentFromTheFuture(sw, now);
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
		/// Lets go of a storm an admin started that is born after now: the world was set back past its birth. It would
		/// hang unseen until the clock caught up, and the retirement goes out to every client like any other edit.
		/// </summary>
		private void RetireSentFromTheFuture(SceneWeather sw, uint now)
		{
			WeatherTimeline timeline = sw.Timeline;
			double nowSeconds = timeline.WorldSecondsAt(now);
			for (int i = 0; i < timeline.Cells.Count; i++)
			{
				StormCell cell = timeline.Cells[i];
				if (StormSchedule.IsScheduled(cell.ID) || cell.DecaySeconds <= nowSeconds)
				{
					continue;
				}
				if (cell.BirthSeconds > nowSeconds + timeline.LeadWorldSeconds + 1.0)
				{
					Retire(sw, ref cell, now, 1f);
					timeline.Cells[i] = cell;
				}
			}
		}

		private ushort SpawnCellInternal(SceneWeather sw, StormKind kind, Vector3 at, float radius, float extent, Vector2 velocity, float lifetimeSeconds, uint now)
		{
			WeatherTimeline timeline = sw.Timeline;
			double birth = timeline.WorldSecondsAt(now) + timeline.LeadWorldSeconds;
			ushort id = NextCellID(sw);
			if (id == 0)
			{
				return 0;
			}
			StormCell cell = StormSchedule.NewCell(id, (uint)sw.Rng.Next(), kind, new Vector2(at.x, at.z), radius, extent, velocity, lifetimeSeconds, birth);
			QueueCell(sw, cell, isNew: true);
			return cell.ID;
		}

		private static ushort NextCellID(SceneWeather sw)
		{
			// Below the schedule's ids, so a storm someone started can never be taken for one of the world's own.
			for (int guard = 0; guard < StormSchedule.FirstID; guard++)
			{
				ushort id = sw.NextCellID++;
				if (sw.NextCellID == 0 || sw.NextCellID >= StormSchedule.FirstID) sw.NextCellID = 1;
				if (id != 0 && !sw.Timeline.TryGetCell(id, out _))
				{
					return id;
				}
			}
			return 0;
		}

		private void Retire(SceneWeather sw, ref StormCell cell, uint now, float fadeSeconds)
		{
			double start = sw.Timeline.WorldSecondsAt(now) + sw.Timeline.LeadWorldSeconds;
			if (!StormSchedule.Retire(ref cell, start, fadeSeconds))
			{
				return;
			}
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

		public bool IsDirectorEnabled(Scene scene) => scenes.TryGetValue(scene.handle, out SceneWeather sw) && sw.Timeline.Director;

		public bool SetAirOffsets(Scene scene, AirOffsets offsets, float transitionSeconds)
		{
			if (!scenes.TryGetValue(scene.handle, out SceneWeather sw))
			{
				return false;
			}
			double start = NowSeconds(sw) + sw.Timeline.LeadWorldSeconds;
			QueueAir(sw, new AirOffsetEntry
			{
				From = sw.Timeline.Air.AtSeconds(start),
				To = offsets,
				StartSeconds = start,
				EndSeconds = start + Mathf.Max(0f, transitionSeconds),
			});
			return true;
		}

		public bool TryGetAirOffsets(Scene scene, out AirOffsets offsets)
		{
			offsets = default;
			if (!scenes.TryGetValue(scene.handle, out SceneWeather sw))
			{
				return false;
			}
			// Where it is heading, not where it is: a relative change stacks onto the settled value.
			offsets = sw.Timeline.Air.To;
			return true;
		}

		public ushort SpawnCell(Scene scene, StormKind kind, Vector3 at, float radiusMeters, Vector2 velocity, float lifetimeSeconds)
		{
			if (!TryGet(scene, out SceneWeather sw) || sw.Timeline.SceneMode != WeatherSceneMode.Own)
			{
				return 0;
			}
			uint now = NowTick;
			// What is not asked for, the air where it starts decides.
			WeatherSample sample = WeatherField.Sample(sw.Timeline, sw.Settings, sw.Scene, at, now);
			// And a world with no air has none to make any storm of: refused, not spawned as nothing.
			if (!StormPhysics.CanForm(sample.Planet))
			{
				return 0;
			}
			StormPhysics.Dimensions(kind, sample.OpenColumn, sw.Rng.Range(0f, 1f), sw.Rng.Range(0f, 1f),
				out float radius, out float extent, out float lifetime);
			if (radiusMeters > 0f)
			{
				// Asked for a size: keep the kind's own proportions.
				extent *= radiusMeters / Mathf.Max(1f, radius);
				radius = radiusMeters;
			}
			if (lifetimeSeconds > 0f)
			{
				lifetime = lifetimeSeconds;
			}
			return SpawnCellInternal(sw, kind, at, radius, extent, velocity, Mathf.Max(30f, lifetime), now);
		}

		public bool SteerCell(Scene scene, ushort id, Vector3 towards, float speed)
		{
			// The world's own storms are the world clock's (StormSchedule): every side works them out, so none can be edited.
			if (StormSchedule.IsScheduled(id) || !TryGet(scene, out SceneWeather sw) || !sw.Timeline.TryGetCell(id, out int index))
			{
				return false;
			}
			StormCell cell = sw.Timeline.Cells[index];
			double start = NowSeconds(sw) + sw.Timeline.LeadWorldSeconds;
			// Re-base the motion where the cell will be when the change lands; the meander restarts from there.
			Vector2 from = cell.CentreAtSeconds(start);
			Vector2 direction = new Vector2(towards.x, towards.z) - from;
			Vector2 velocity = direction.sqrMagnitude > 1f ? direction.normalized * Mathf.Max(0f, speed) : Vector2.zero;
			cell.OriginX = from.x;
			cell.OriginZ = from.y;
			cell.VelocityX = velocity.x;
			cell.VelocityZ = velocity.y;
			cell.MotionSeconds = start;
			cell.MeanderMeters = 0f;
			QueueCell(sw, cell, isNew: false);
			return true;
		}

		public bool RetireCell(Scene scene, ushort id, float fadeSeconds)
		{
			if (StormSchedule.IsScheduled(id) || !TryGet(scene, out SceneWeather sw) || !sw.Timeline.TryGetCell(id, out int index))
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
			bool director = enabled && sw.Timeline.SceneMode == WeatherSceneMode.Own;
			if (director != sw.Timeline.Director)
			{
				// Sent: the scene's own storms start or stop on every side at once.
				QueueDirector(sw, director);
			}
			return director == enabled;
		}
	}
}
