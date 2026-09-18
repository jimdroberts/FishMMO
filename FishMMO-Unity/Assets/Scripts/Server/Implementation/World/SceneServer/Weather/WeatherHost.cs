using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
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
	/// </remarks>
	public sealed class WeatherHost : IWeatherService
	{
		/// <summary>Most storm cells a scene runs at once.</summary>
		public const int MaxCellsPerScene = 12;
		/// <summary>Scenes smaller than this get no automatic storms.</summary>
		public const float DirectorMinimumSquareKm = 2f;
		/// <summary>How often surface cover is re-sent so clients' integration cannot drift far.</summary>
		public const float CoverResyncSeconds = 30f;

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
			public float WindHeadingDegrees;
			public float CoverResync;
			public WeatherDeltaBroadcast Pending;
			public bool HasPending;
		}

		private readonly NetworkManager networkManager;
		private readonly ICharacterMappingData<NetworkConnection> characters;
		private readonly Dictionary<int, SceneWeather> scenes = new Dictionary<int, SceneWeather>();
		private readonly List<SceneWeather> flushList = new List<SceneWeather>();
		private float secondTimer;

		public WeatherHost(NetworkManager networkManager, ICharacterMappingData<NetworkConnection> characters)
		{
			this.networkManager = networkManager;
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
			uint seed = unchecked((uint)(scene.name.GetDeterministicHashCode() ^ scene.handle ^ Environment.TickCount));
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
			sw.WindHeadingDegrees = sw.Rng.Range(0f, 360f);
			sw.NextSpawnTick = NowTick + timeline.SecondsToTicks(sw.Rng.Range(5f, 30f));
			scenes[scene.handle] = sw;
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
				networkManager.ServerManager.Broadcast(connection, sw.Timeline.ToBroadcast(), true, Channel.Reliable);
			}
		}

		private void OnResyncRequest(NetworkConnection connection, WeatherResyncRequestBroadcast msg, Channel channel)
		{
			if (connection == null || !characters.ConnectionCharacters.TryGetValue(connection, out IPlayerCharacter character) || character?.GameObject == null)
			{
				return;
			}
			OnCharacterSpawned(connection, character.GameObject.scene);
		}

		private void SendToScene<T>(SceneWeather sw, T message) where T : struct, FishNet.Broadcast.IBroadcast
		{
			int handle = sw.Scene.handle;
			foreach (IPlayerCharacter character in characters.CharactersByID.Values)
			{
				if (character?.GameObject != null && character.GameObject.scene.handle == handle && character.Owner != null && character.Owner.IsActive)
				{
					networkManager.ServerManager.Broadcast(character.Owner, message, true, Channel.Reliable);
				}
			}
		}

		private ref WeatherDeltaBroadcast Pending(SceneWeather sw)
		{
			if (!sw.HasPending)
			{
				sw.Pending = new WeatherDeltaBroadcast { SceneName = sw.Timeline.SceneName };
				sw.HasPending = true;
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

		private void Flush()
		{
			flushList.Clear();
			foreach (SceneWeather sw in scenes.Values)
			{
				if (sw.HasPending)
				{
					flushList.Add(sw);
				}
			}
			foreach (SceneWeather sw in flushList)
			{
				sw.Timeline.Revision++;
				sw.Pending.Revision = sw.Timeline.Revision;
				SendToScene(sw, sw.Pending);
				sw.HasPending = false;
				sw.Pending = default;
				WeatherEvents.RaiseTimelineChanged(sw.Scene, sw.Timeline);
			}
		}

		// ── Tick ──────────────────────────────────────────────────────

		public void Tick(float deltaTime)
		{
			secondTimer += deltaTime;
			if (secondTimer >= 1f)
			{
				float seconds = secondTimer;
				secondTimer = 0f;
				uint now = NowTick;
				double worldHours = WorldClock.Shared.HasAnchor ? WorldClock.Shared.WorldHoursAt(now) : 0;
				foreach (SceneWeather sw in scenes.Values)
				{
					if (sw.Settings == null)
					{
						continue;
					}
					sw.Timeline.Prune(now);
					ApplyClimate(sw, now, worldHours);
					if (sw.Timeline.SceneMode == WeatherSceneMode.None)
					{
						continue;
					}
					IntegrateCover(sw, now, seconds);
					RunDirector(sw, now);
				}
			}
			Flush();
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
			Vector2[] points =
			{
				c,
				c + new Vector2(-area.width, -area.height) * 0.25f,
				c + new Vector2(area.width, -area.height) * 0.25f,
				c + new Vector2(-area.width, area.height) * 0.25f,
				c + new Vector2(area.width, area.height) * 0.25f,
			};
			foreach (Vector2 p in points)
			{
				WeatherSample sample = WeatherField.Sample(sw.Timeline, sw.Settings, sw.Scene, new Vector3(p.x, 0f, p.y), tick);
				average.Add(sample.Frame, 1f / points.Length);
				temperature += sample.Temperature / points.Length;
			}
			WeatherFrame frame = average.Resolve();
			sw.Timeline.Cover.Integrate(frame, temperature, seconds);
			sw.Timeline.CoverTick = tick;
			sw.CoverResync -= seconds;
			if (sw.CoverResync <= 0f)
			{
				sw.CoverResync = CoverResyncSeconds;
				QueueCover(sw);
			}
		}

		/// <summary>The scene's world-space X/Z rectangle, from its biome map or its terrain.</summary>
		private static bool TryGetArea(SceneWeather sw, out Rect area)
		{
			SceneBiomeMap map = sw.Settings != null ? sw.Settings.BiomeMap : null;
			if (map != null && map.WorldSize.x > 0f && map.WorldSize.y > 0f)
			{
				area = new Rect(map.WorldOrigin, map.WorldSize);
				return true;
			}
			bool found = false;
			area = default;
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain == null || terrain.terrainData == null || terrain.gameObject.scene.handle != sw.Scene.handle)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				var rect = new Rect(origin.x, origin.z, size.x, size.z);
				area = found ? Rect.MinMaxRect(Mathf.Min(area.xMin, rect.xMin), Mathf.Min(area.yMin, rect.yMin), Mathf.Max(area.xMax, rect.xMax), Mathf.Max(area.yMax, rect.yMax)) : rect;
				found = true;
			}
			return found;
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
				Rect grown = new Rect(area.xMin - cell.RadiusMeters, area.yMin - cell.RadiusMeters, area.width + cell.RadiusMeters * 2f, area.height + cell.RadiusMeters * 2f);
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
				float speed = sw.Rng.Range(2f, 6f);
				float radius = sw.Rng.Range(preset.CellRadiusMeters.x, Mathf.Max(preset.CellRadiusMeters.x, preset.CellRadiusMeters.y));
				float minutes = sw.Rng.Range(preset.DurationMinutes.x, Mathf.Max(preset.DurationMinutes.x, preset.DurationMinutes.y));
				SpawnCellInternal(sw, preset, p, radius, new Vector2(Mathf.Sin(heading), Mathf.Cos(heading)) * speed, minutes * 60f, now);
				return;
			}
		}

		private float NearestPlayerDistance(SceneWeather sw, Vector3 p)
		{
			float best = float.MaxValue;
			int handle = sw.Scene.handle;
			foreach (IPlayerCharacter character in characters.CharactersByID.Values)
			{
				if (character?.GameObject == null || character.GameObject.scene.handle != handle)
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
				PeakIntensity = 1f,
				MeanderMeters = Mathf.Max(10f, radius) * 0.15f,
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
