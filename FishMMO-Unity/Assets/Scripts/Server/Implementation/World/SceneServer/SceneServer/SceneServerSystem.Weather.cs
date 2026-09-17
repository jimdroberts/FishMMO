using System;
using System.Collections.Generic;
using System.Globalization;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer.Weather;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The world clock and the weather, hosted by the scene server.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A partial rather than its own behaviour so it needs no scene wiring, like the server control
	/// partial. Other systems reach the weather through <see cref="WeatherService"/>.
	/// </para>
	/// <para>
	/// The reports in this file are read-only and neutral: both <c>/gm</c> (which may only look) and
	/// <c>/admin</c> (which may change the weather) use them. The changing commands live in
	/// <c>SceneServerSystem.AdminCommands.Weather</c>.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>How far around the caller the weather report lists storm cells.</summary>
		private const float ReportCellRadiusMeters = 3000f;

		private WeatherHost weatherHost;
		private WorldClockHost worldClockHost;
		private ICharacterSystem<NetworkConnection, Scene> weatherCharacterSystem;

		/// <summary>The scene weather, for server systems (spawners, AI, abilities). Null before start.</summary>
		public IWeatherService WeatherService => weatherHost;

		private void InitializeWeather(ICharacterSystem<NetworkConnection, Scene> characterSystem, ICharacterMappingData<NetworkConnection> characterMapping)
		{
			var networkManager = Server.NetworkWrapper.NetworkManager;
			SolarSystemProfile system = SolarSystemProfile.Active;
			long epoch = system != null ? system.EpochUnixSeconds : CalendarProfile.DefaultEpochUnixSeconds;
			if (system == null)
			{
				_ = Log.Warning("SceneServerSystem", "No SolarSystemProfile is loaded; the world clock uses the default epoch and scenes use the home world's defaults.");
			}

			worldClockHost = new WorldClockHost(networkManager, Server.Database, epoch);
			worldClockHost.Start();

			weatherHost = new WeatherHost(networkManager, characterMapping);
			weatherHost.Start();

			weatherCharacterSystem = characterSystem;
			if (weatherCharacterSystem != null)
			{
				weatherCharacterSystem.OnSpawnCharacter += CharacterSystem_OnWeatherCharacterSpawned;
			}
		}

		private void DeinitializeWeather()
		{
			if (weatherCharacterSystem != null)
			{
				weatherCharacterSystem.OnSpawnCharacter -= CharacterSystem_OnWeatherCharacterSpawned;
				weatherCharacterSystem = null;
			}
			weatherHost?.Stop();
			weatherHost = null;
			worldClockHost?.Stop();
			worldClockHost = null;
		}

		private void UpdateWeather(float deltaTime)
		{
			worldClockHost?.Tick(deltaTime);
			weatherHost?.Tick(deltaTime);
		}

		/// <summary>An arriving character learns the clock first, then its scene's weather.</summary>
		private void CharacterSystem_OnWeatherCharacterSpawned(NetworkConnection connection, IPlayerCharacter character, Scene scene)
		{
			worldClockHost?.SendTo(connection);
			weatherHost?.OnCharacterSpawned(connection, scene);
		}

		// ── Read-only reports (neutral: reachable by /gm and /admin) ──

		/// <summary>The weather in the caller's scene and where they stand.</summary>
		private void ReportWeather(IPlayerCharacter character)
		{
			if (character?.GameObject == null)
			{
				return;
			}
			Scene scene = character.GameObject.scene;
			if (weatherHost == null || !weatherHost.TryGetTimeline(scene, out WeatherTimeline timeline))
			{
				Reply(character, "This scene has no weather.");
				return;
			}
			var lines = new List<string>
			{
				$"Weather in {scene.name}: {timeline.SceneMode}, director {(weatherHost.IsDirectorEnabled(scene) ? "on" : "off")}, revision {timeline.Revision}.",
			};
			WeatherSample here = WeatherQuery.Sample(scene, character.Transform.position);
			lines.Add($"Here: {here.Frame} temp {here.Temperature:+0.00;-0.00} {(here.IsSheltered ? "sheltered" : "exposed")}.");
			WeatherCover cover = timeline.Cover;
			timeline.ClimateAt(WeatherQuery.CurrentTick, out float shiftT, out float shiftH);
			lines.Add($"Cover snow {cover.Snow:0.00} wet {cover.Wet:0.00} ash {cover.Ash:0.00} sand {cover.Sand:0.00}; scene shift T {shiftT:+0.00;-0.00} H {shiftH:+0.00;-0.00}.");

			uint tick = WeatherQuery.CurrentTick;
			foreach (WeatherLayerEntry entry in timeline.Layers)
			{
				WeatherLayerTemplate template = WeatherLayerTemplate.Get<WeatherLayerTemplate>(entry.TemplateID);
				lines.Add($"Layer #{entry.Handle} {(template != null ? template.name : "?")} {entry.IntensityAt(tick):0.00} → {entry.To:0.00}{(entry.RemoveWhenDone ? " (removing)" : string.Empty)}");
			}
			var cells = new List<StormCell>();
			WeatherQuery.CellsWithin(scene, character.Transform.position, ReportCellRadiusMeters, cells);
			Vector3 p = character.Transform.position;
			foreach (StormCell cell in cells)
			{
				WeatherPreset preset = WeatherPreset.Get<WeatherPreset>(cell.PresetID);
				Vector2 centre = cell.CentreAt(tick, timeline.TickDelta);
				Vector2 offset = centre - new Vector2(p.x, p.z);
				lines.Add($"Cell {cell.ID} {(preset != null ? preset.ResolvedName : "?")} {offset.magnitude:0} m {Compass(offset)}, radius {cell.RadiusMeters:0} m, strength {cell.EnvelopeAt(tick):0.00}");
			}
			int others = timeline.Cells.Count - cells.Count;
			if (others > 0)
			{
				lines.Add($"{others} more cell(s) further than {ReportCellRadiusMeters / 1000f:0} km.");
			}
			ReplyLines(character, lines);
		}

		/// <summary>The world clock and the caller's local time. Nothing can set the clock.</summary>
		private void ReportClock(IPlayerCharacter character)
		{
			if (character?.GameObject == null)
			{
				return;
			}
			WorldClock clock = WorldClock.Shared;
			if (!clock.HasAnchor)
			{
				Reply(character, "The world clock has not started yet.");
				return;
			}
			uint tick = WeatherQuery.CurrentTick;
			double hours = clock.WorldHoursAt(tick);
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldSceneSettings.TryGetForScene(character.GameObject.scene, out WorldSceneSettings settings);
			WorldBody body = SceneTime.BodyOf(settings);
			var lines = new List<string>
			{
				$"World clock: {(clock.Current.Verified ? "anchored to the database" : "host clock, not yet verified")}, last error {clock.LastMeasuredError * 1000.0:0} ms, next check in {(worldClockHost != null ? worldClockHost.SecondsUntilCheck : 0f):0}s.",
			};
			double local = SceneTime.LocalTime01(settings, hours);
			string where = body != null ? body.ResolvedName : "the home world";
			string timeMode = settings != null && settings.TimeMode == SceneTimeMode.Fixed ? " (fixed)" : string.Empty;
			lines.Add($"Here: {SceneTime.Format(local)}{timeMode}, {(SceneTime.IsDaylight(settings, hours) ? "day" : "night")} on {where}.");
			if (system != null && body != null)
			{
				double solarDay = CelestialMath.SolarDayHours(system, body);
				double daylight = CelestialMath.DaylightHours(system, body, hours, settings != null ? settings.Latitude : 0f);
				lines.Add($"{where} turns in {CelestialMath.RotationHours(system, body):0.##} h; solar day {solarDay:0.##} h; daylight here today {daylight:0.##} h.");
				if (system.Calendar != null)
				{
					long day = CelestialMath.HomeDay(system, hours);
					system.Calendar.ToDate(day, out long year, out int month, out int dayOfMonth);
					string monthName = month - 1 < system.Calendar.Months.Count ? system.Calendar.Months[month - 1].Name : "Month " + month;
					int weekdays = Math.Max(1, system.Calendar.Weekdays.Count);
					string weekday = system.Calendar.Weekdays.Count > 0 ? system.Calendar.Weekdays[(int)(((day % weekdays) + weekdays) % weekdays)] : string.Empty;
					lines.Add($"Date: {weekday} {dayOfMonth} {monthName}, year {year} {system.Calendar.EraName}".TrimEnd() + ".");
				}
			}
			ReplyLines(character, lines);
		}

		private static string Compass(Vector2 offset)
		{
			string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
			float angle = Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
			int index = Mathf.RoundToInt(((angle % 360f) + 360f) % 360f / 45f) % 8;
			return names[index];
		}

		/// <summary>Seconds from "45", "45s", "2m" or "1h". A bare number is seconds.</summary>
		private static bool TryParseWeatherSeconds(string word, out float seconds)
		{
			seconds = 0f;
			if (string.IsNullOrWhiteSpace(word))
			{
				return false;
			}
			word = word.Trim().ToLowerInvariant();
			float scale = 1f;
			char last = word[word.Length - 1];
			if (last == 's' || last == 'm' || last == 'h')
			{
				scale = last == 'm' ? 60f : last == 'h' ? 3600f : 1f;
				word = word.Substring(0, word.Length - 1);
			}
			if (!float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < 0f || value > 86400f)
			{
				return false;
			}
			seconds = value * scale;
			return true;
		}
	}
}
