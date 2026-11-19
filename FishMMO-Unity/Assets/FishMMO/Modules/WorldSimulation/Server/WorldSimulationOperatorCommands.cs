using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Database;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer.Clock;
using FishMMO.Server.Implementation.World.SceneServer.Weather;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;
using FishMMO.Shared;
using FishNet.Connection;
using System.Collections.Generic;
using System.Globalization;
using System;
using UnityEngine.SceneManagement;
using UnityEngine;

[assembly: FishMMO.Server.Implementation.World.SceneServer.OperatorCommandProvider(typeof(FishMMO.Server.Implementation.World.SceneServer.WorldSimulationOperatorCommands))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The World Simulation module's operator commands (moved out of the Staff module, Phase 4 of the core and modules plan,
	/// 2026-11-18): <c>/gm weather</c> and <c>/gm clock</c> (read-only reports both sets reach) and <c>/admin weather</c>
	/// (adds to a scene's air, spawns and steers storm cells, switches the director). Weather changes gameplay, so a game
	/// master may look but not touch.
	/// </summary>
	public sealed class WorldSimulationOperatorCommands : OperatorCommandProvider
	{
		public override IEnumerable<OperatorCommand> GameMasterCommands(IOperatorCommandHost host)
		{
			var commands = new Commands(host);
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "weather", Category = "World",
					Summary = "Reports this scene's weather: layers, nearby storm cells and the weather where you stand.",
					Run = (c, a) => commands.ReportWeather(c),
				},
				new OperatorCommand
				{
					Name = "clock", Category = "World",
					Summary = "Reports the world clock, the date and your local time. Nothing can set it.",
					Run = (c, a) => commands.ReportClock(c),
				},
			};
		}

		public override IEnumerable<OperatorCommand> AdministratorCommands(IOperatorCommandHost host)
		{
			var commands = new Commands(host);
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "weather", Category = "World",
					Summary = "Shows this scene's air and weather, or adds to its air. /admin weather help lists the actions.",
					Arguments = "action:Choice=show,air,cell,director,help?;details:Text?",
					Run = commands.RunAdminWeather,
				},
			};
		}

		/// <summary>The commands' bodies, as the staff system ran them, replying through the host.</summary>
		private sealed class Commands
		{
			private readonly IOperatorCommandHost host;

			public Commands(IOperatorCommandHost host)
			{
				this.host = host;
			}

			private SceneServerSystem SceneServer => host.TryGetSystem(out SceneServerSystem system) ? system : null;

			private WeatherServerService Weather => WeatherServerService.Of(SceneServer);

			private static readonly string[] WeatherActionHelp =
			{
				"weather show — this scene's air, what is added to it, nearby storms and the weather where you stand",
				"weather air <key> <±value> [<key> <±value> …] [seconds] — SET what is added to this scene's air",
				"weather air add <key> <±value> … [seconds] — add to what is already added | air clear [seconds]",
				"  keys: temperature (K), humidity (0..1 scale), pressure (−1 low … +1 high), instability (0..1), wind (m/s), gravity (m/s²)",
				"weather cell spawn <kind> [radius m] [minutes] — a storm at your position; omitted sizes come from the air",
				"  kinds: thunderstorm, supercell (tornado), squall, hurricane, haboob, dustdevil, eruption",
				"weather cell steer <id> [m/s] — send a cell toward you | cell retire <id> [seconds]",
				"weather director on|off — the automatic storm director for this scene",
				"Times: a bare number is seconds; 2m and 1h also work.",
			};

			/// <summary>The weather rows of the <c>/admin</c> table.</summary>


			public void RunAdminWeather(IPlayerCharacter character, string arguments)
			{
				string action = OperatorCommandParsing.SplitFirstWord(arguments, out string rest).ToLowerInvariant();
				IWeatherService weather = Weather?.WeatherService;
				if (action.Length == 0 || action == "show")
				{
					ReportWeather(character);
					return;
				}
				if (action == "help")
				{
					host.ReplyLines(character, WeatherActionHelp);
					return;
				}
				if (weather == null || character?.GameObject == null)
				{
					host.Reply(character, "Weather is not running on this server.");
					return;
				}
				Scene scene = character.GameObject.scene;
				string[] words = SplitWords(rest);
				switch (action)
				{
					case "air":
						AdminWeatherAir(character, weather, scene, words);
						return;
					case "cell":
						AdminWeatherCell(character, weather, scene, words);
						return;
					case "director":
					{
						bool? on = words.Length > 0 ? ParseOnOff(words[0]) : null;
						if (on == null)
						{
							host.Reply(character, $"Director is {(weather.IsDirectorEnabled(scene) ? "on" : "off")}. Usage: /admin weather director on|off");
							return;
						}
						bool applied = weather.SetDirector(scene, on.Value);
						host.Reply(character, applied ? $"Storm director {(on.Value ? "on" : "off")} for {scene.name}." : "This scene has no weather of its own, so the director stays off.");
						return;
					}
					default:
						host.ReplyLines(character, WeatherActionHelp);
						return;
				}
			}

			private void AdminWeatherAir(IPlayerCharacter character, IWeatherService weather, Scene scene, string[] words)
			{
				const string Usage = "Usage: /admin weather air [add] <key> <±value> … [seconds] | air clear [seconds]. Keys: temperature, humidity, pressure, instability, wind, gravity.";
				if (words.Length == 0)
				{
					weather.TryGetAirOffsets(scene, out AirOffsets now);
					host.Reply(character, $"Added to this scene's air at runtime: {now}. {Usage}");
					return;
				}
				float seconds = 30f;
				if (words[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
				{
					if (words.Length > 1 && !TryParseWeatherSeconds(words[1], out seconds))
					{
						host.Reply(character, "Usage: /admin weather air clear [seconds]");
						return;
					}
					host.Reply(character, weather.SetAirOffsets(scene, default, seconds)
						? $"Handing {scene.name} back to its own air over {seconds:0}s."
						: "This scene has no weather.");
					return;
				}
				bool relative = words[0].Equals("add", StringComparison.OrdinalIgnoreCase);
				int start = relative ? 1 : 0;
				if (!TryParseAirOffsets(words, start, out AirOffsets offsets, ref seconds, out string problem))
				{
					host.Reply(character, $"{problem} {Usage}");
					return;
				}
				if (relative && weather.TryGetAirOffsets(scene, out AirOffsets current))
				{
					offsets = current + offsets;
				}
				host.Reply(character, weather.SetAirOffsets(scene, offsets, seconds)
					? $"Air of {scene.name} now has added: {offsets} (over {seconds:0}s)."
					: "This scene has no weather.");
			}

			/// <summary>
			/// Reads key/value pairs into offsets; a lone trailing word is the transition time.
			/// </summary>
			internal static bool TryParseAirOffsets(string[] words, int start, out AirOffsets offsets, ref float seconds, out string problem)
			{
				offsets = default;
				problem = null;
				int i = start;
				bool any = false;
				while (i < words.Length)
				{
					string key = words[i].ToLowerInvariant();
					if (i + 1 >= words.Length)
					{
						// A lone last word is the time.
						if (!TryParseWeatherSeconds(words[i], out seconds))
						{
							problem = $"'{words[i]}' is neither a key with a value nor a time.";
							return false;
						}
						break;
					}
					if (!float.TryParse(words[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
					{
						problem = $"'{words[i + 1]}' is not a number for {key}.";
						return false;
					}
					switch (key)
					{
						case "temperature": case "temp": case "t": offsets.Temperature = Mathf.Clamp(value, -SceneClimate.MaxOffsetKelvin, SceneClimate.MaxOffsetKelvin); break;
						case "humidity": case "hum": case "h": offsets.Humidity = Mathf.Clamp(value, -1f, 1f); break;
						case "pressure": case "p": offsets.Pressure = Mathf.Clamp(value, -2f, 2f); break;
						case "instability": case "i": offsets.Instability = Mathf.Clamp(value, -1f, 1f); break;
						case "wind": case "w": offsets.Wind = Mathf.Clamp(value, -60f, 60f); break;
						case "gravity": case "g": offsets.Gravity = Mathf.Clamp(value, -20f, 20f); break;
						default:
							problem = $"'{key}' is not something the air has.";
							return false;
					}
					any = true;
					i += 2;
				}
				if (!any)
				{
					problem = "Nothing to add.";
					return false;
				}
				return true;
			}

			private void AdminWeatherCell(IPlayerCharacter character, IWeatherService weather, Scene scene, string[] words)
			{
				string verb = words.Length > 0 ? words[0].ToLowerInvariant() : string.Empty;
				Vector3 here = character.Transform.position;
				if (verb == "spawn")
				{
					if (words.Length < 2 || !StormPhysics.TryParse(words[1], out StormKind kind))
					{
						host.Reply(character, "Usage: /admin weather cell spawn <kind> [radius m] [minutes]. Kinds: thunderstorm, supercell, squall, hurricane, haboob, dustdevil, eruption.");
						return;
					}
					float radius = 0f;
					float minutes = 0f;
					if (words.Length > 2 && !TryParsePositive(words[2], 1f, 10000f, out radius) || words.Length > 3 && !TryParsePositive(words[3], 1f, 720f, out minutes))
					{
						host.Reply(character, "Usage: /admin weather cell spawn <kind> [radius 1-10000 m] [minutes 1-720]");
						return;
					}
					ushort id = weather.SpawnCell(scene, kind, here, radius, Vector2.zero, minutes * 60f);
					host.Reply(character, id != 0
						? $"Cell {id} ({StormPhysics.NameOf(kind)}) over you{(radius > 0f ? $", radius {radius:0} m" : ", sized by the air")}{(minutes > 0f ? $", for {minutes:0} min" : string.Empty)}. Steer it with /admin weather cell steer {id}."
						: "No storm cells here: this scene has no weather of its own, or its world has no air to make one.");
					return;
				}
				if (verb == "steer")
				{
					float speed = 5f;
					if (words.Length < 2 || !ushort.TryParse(words[1], out ushort id) || words.Length > 2 && !TryParsePositive(words[2], 0f, 100f, out speed))
					{
						host.Reply(character, "Usage: /admin weather cell steer <id> [m/s 0-100]");
						return;
					}
					host.Reply(character, weather.SteerCell(scene, id, here, speed) ? $"Cell {id} heading for you at {speed:0.#} m/s."
						: StormSchedule.IsScheduled(id) ? $"Cell {id} is one of the world's own storms, worked out from the world clock on every side: it cannot be steered. Start one of your own (/admin weather cell spawn)." : $"No cell {id} here.");
					return;
				}
				if (verb == "retire")
				{
					float seconds = 120f;
					if (words.Length < 2 || !ushort.TryParse(words[1], out ushort id) || words.Length > 2 && !TryParseWeatherSeconds(words[2], out seconds))
					{
						host.Reply(character, "Usage: /admin weather cell retire <id> [seconds]");
						return;
					}
					host.Reply(character, weather.RetireCell(scene, id, seconds) ? $"Cell {id} fading over {seconds:0}s."
						: StormSchedule.IsScheduled(id) ? $"Cell {id} is one of the world's own storms, worked out from the world clock on every side: it cannot be ended. Turn the director off to stop them (/admin weather director off)." : $"No cell {id} here (or it is already fading).");
					return;
				}
				host.Reply(character, "Usage: /admin weather cell spawn|steer|retire …  (/admin weather help)");
			}

			// ── Helpers ───────────────────────────────────────────────────

			private static string[] SplitWords(string text)
			{
				return (text ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
			}

			private static bool? ParseOnOff(string word)
			{
				switch ((word ?? string.Empty).ToLowerInvariant())
				{
					case "on": case "true": case "yes": case "1": return true;
					case "off": case "false": case "no": case "0": return false;
					default: return null;
				}
			}

			private static bool TryParsePositive(string word, float min, float max, out float value)
			{
				return float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
			}
	

			/// <summary>How far around the caller the weather report lists storm cells.</summary>
			private const float ReportCellRadiusMeters = 3000f;

			// ── Read-only reports (neutral: reachable by /gm and /admin) ──

			/// <summary>The weather in the caller's scene and where they stand.</summary>
			public void ReportWeather(IPlayerCharacter character)
			{
				if (character?.GameObject == null)
				{
					return;
				}
				Scene scene = character.GameObject.scene;
				WeatherHost weatherHost = Weather?.WeatherHost;
				if (weatherHost == null || !weatherHost.TryGetTimeline(scene, out WeatherTimeline timeline))
				{
					host.Reply(character, "This scene has no weather.");
					return;
				}
				var lines = new List<string>
				{
					$"Weather in {scene.name}: {timeline.SceneMode}, director {(weatherHost.IsDirectorEnabled(scene) ? "on" : "off")}, revision {timeline.Revision}.",
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
				host.ReplyLines(character, lines);
			}

			/// <summary>The world clock — its time to the millisecond, its pace — and the caller's local time.</summary>
			public void ReportClock(IPlayerCharacter character)
			{
				if (character?.GameObject == null)
				{
					return;
				}
				WorldClock clock = WorldClock.Shared;
				if (!clock.HasAnchor)
				{
					host.Reply(character, "The world clock has not started yet.");
					return;
				}
				uint tick = WeatherQuery.CurrentTick;
				double hours = clock.WorldHoursAt(tick);
				SolarSystemProfile system = SolarSystemProfile.Active;
				WorldSceneSettings.TryGetForScene(character.GameObject.scene, out WorldSceneSettings settings);
				WorldBody body = SceneTime.BodyOf(settings);
				long epoch = FishMMO.Shared.Celestial.WorldEpoch.UnixSeconds;
				long revision = SceneServer.WorldClock != null ? SceneServer.WorldClock.AdoptedRevision : long.MinValue;
				var lines = new List<string>
				{
					$"World time: {WorldTimeText.Write(clock.WorldSecondsAt(tick + SceneServer.Server.NetworkWrapper.NetworkManager.TimeManager.GetTickPercentAsDouble()), epoch)} ({WorldTimeText.Pace(clock.Rate)}).",
					$"World clock: {(clock.Current.Verified ? "anchored to the database" : "host clock, not yet verified")}, "
						+ (revision == long.MinValue ? "no time control (it runs at real time)" : $"control revision {revision}")
						+ $", last error {clock.LastMeasuredError * 1000.0:0} ms, next check in {(SceneServer.WorldClock != null ? SceneServer.WorldClock.SecondsUntilCheck : 0f):0}s.",
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
				host.ReplyLines(character, lines);
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
}
