using System;
using System.Collections.Generic;
using System.Globalization;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer.Weather;
using FishMMO.Server.Implementation.World.SceneServer.Clock;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>The read-only world reports both command sets reach (moved from <c>SceneServerSystem.Weather</c>).</summary>
	public partial class StaffSystem
	{
		/// <summary>How far around the caller the weather report lists storm cells.</summary>
		private const float ReportCellRadiusMeters = 3000f;

		// ── Read-only reports (neutral: reachable by /gm and /admin) ──

		/// <summary>The weather in the caller's scene and where they stand.</summary>
		private void ReportWeather(IPlayerCharacter character)
		{
			if (character?.GameObject == null)
			{
				return;
			}
			Scene scene = character.GameObject.scene;
			if (sceneServer.WeatherHost == null || !sceneServer.WeatherHost.TryGetTimeline(scene, out WeatherTimeline timeline))
			{
				Reply(character, "This scene has no weather.");
				return;
			}
			var lines = new List<string>
			{
				$"Weather in {scene.name}: {timeline.SceneMode}, director {(sceneServer.WeatherHost.IsDirectorEnabled(scene) ? "on" : "off")}, revision {timeline.Revision}.",
			};
			WeatherSample here = WeatherTimelines.SampleFull(scene, character.Transform.position);
			AirColumn column = here.Column;
			WeatherAir air = here.Air;
			float heading = Mathf.Repeat(Mathf.Atan2(air.Wind.x, air.Wind.y) * Mathf.Rad2Deg, 360f);
			lines.Add($"Air here: {column.SurfaceKelvin - 273.15f:0.0} °C, dew point {column.DewPointKelvin - 273.15f:0.0} °C ({column.RelativeHumidity * 100f:0}% RH), pressure {air.Pressure:+0.00;-0.00}, "
				+ $"lapse {column.EnvironmentLapse * 1000f:0.0} K/km (moist {column.MoistLapse * 1000f:0.0}, dry {column.DryLapse * 1000f:0.0}), wind {air.Wind.magnitude:0.0} m/s toward {heading:0}°.");
			string towers = column.Deep
				? $"towers to {column.TowerCeiling:0} m (CAPE {column.Cape:0} J/kg, updraughts {column.Updraft:0} m/s)"
				: "no deep convection";
			lines.Add($"Column: base {column.Base:0} m, tops {column.Top:0} m, {towers}, lid {column.Cap:0} m, freezing {column.Freezing:0} m, ice {column.IceLevel:0} m, tropopause {column.Tropopause:0} m; {here.Planet.Condensate} clouds, g {here.Planet.Gravity:0.00} m/s².");
			lines.Add($"Weather: {here.Frame} temp {here.Temperature:+0.00;-0.00} {(here.IsSheltered ? "sheltered" : "exposed")}{(here.Substance != null ? ", falling: " + here.Substance.ResolvedDisplayName : string.Empty)}.");
			uint tick = WeatherQuery.CurrentTick;
			WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings sceneSettings);
			AirOffsets authored = sceneSettings != null ? sceneSettings.AuthoredAir() : default;
			lines.Add($"Added to the air — authored: {(authored.IsZero ? "nothing" : authored.ToString())}; runtime: {(timeline.AirAt(tick).IsZero ? "nothing" : timeline.AirAt(tick).ToString())}.");
			WeatherCover cover = timeline.Cover;
			lines.Add($"Cover snow {cover.Snow:0.00} wet {cover.Wet:0.00} ash {cover.Ash:0.00} sand {cover.Sand:0.00}.");

			var cells = new List<StormCell>();
			WeatherTimelines.CellsWithin(scene, character.Transform.position, ReportCellRadiusMeters, cells);
			Vector3 p = character.Transform.position;
			double nowSeconds = timeline.WorldSecondsAt(tick);
			foreach (StormCell cell in cells)
			{
				Vector2 centre = cell.CentreAtSeconds(nowSeconds);
				Vector2 offset = centre - new Vector2(p.x, p.z);
				lines.Add($"Cell {cell.ID} {StormPhysics.NameOf(cell.Kind)} {offset.magnitude:0} m {Compass(offset)}, radius {cell.RadiusMeters:0} m, strength {cell.EnvelopeAtSeconds(nowSeconds):0.00}");
			}
			int others = timeline.Cells.Count - cells.Count;
			if (others > 0)
			{
				lines.Add($"{others} more cell(s) further than {ReportCellRadiusMeters / 1000f:0} km.");
			}
			ReplyLines(character, lines);
		}

		/// <summary>The world clock — its time to the millisecond, its pace — and the caller's local time.</summary>
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
			long epoch = WorldEpoch();
			long revision = sceneServer.WorldClock != null ? sceneServer.WorldClock.AdoptedRevision : long.MinValue;
			var lines = new List<string>
			{
				$"World time: {WorldTimeText.Write(clock.WorldSecondsAt(tick + Server.NetworkWrapper.NetworkManager.TimeManager.GetTickPercentAsDouble()), epoch)} ({WorldTimeText.Pace(clock.Rate)}).",
				$"World clock: {(clock.Current.Verified ? "anchored to the database" : "host clock, not yet verified")}, "
					+ (revision == long.MinValue ? "no time control (it runs at real time)" : $"control revision {revision}")
					+ $", last error {clock.LastMeasuredError * 1000.0:0} ms, next check in {(sceneServer.WorldClock != null ? sceneServer.WorldClock.SecondsUntilCheck : 0f):0}s.",
			};
			double local = SceneTime.LocalTime01(settings, hours);
			string where = body != null ? body.ResolvedName : "the home world";
			string timeMode = settings != null && settings.TimeMode() == SceneTimeMode.Fixed ? " (fixed)" : string.Empty;
			lines.Add($"Here: {SceneTime.Format(local)}{timeMode}, {(SceneTime.IsDaylight(settings, hours) ? "day" : "night")} on {where}.");
			if (system != null && body != null)
			{
				double solarDay = CelestialMath.SolarDayHours(system, body);
				double daylight = CelestialMath.DaylightHours(system, body, hours, settings != null ? settings.Latitude() : 0f);
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
