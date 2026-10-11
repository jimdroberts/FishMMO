using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Server.Implementation.World.SceneServer.Weather;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/admin weather</c>: changing the air of the scene the administrator stands in, and the
	/// storms in it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Admin-only because weather changes gameplay (exposure buffs, spawns, ability modifiers).
	/// Game masters have the read-only <c>/gm weather</c>. Every use is audited at the command gate.
	/// </para>
	/// <para>
	/// There are no named weathers to ask for. The weather is worked out from the air, so the only
	/// thing to change is the air — and only by adding to it: a few kelvin colder, a little damper, a
	/// lower pressure, less stable. Whatever that air then does on this world is the weather. A
	/// storm is a physical kind (a thunderstorm, a supercell, a haboob…) that does to the air under
	/// it what that storm does.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
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
		private IEnumerable<OperatorCommand> BuildAdminWeatherCommands()
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "weather", Category = "World",
					Summary = "Shows this scene's air and weather, or adds to its air. /admin weather help lists the actions.",
					Arguments = "action:Choice=show,air,cell,director,help?;details:Text?",
					Run = RunAdminWeather,
				},
			};
		}

		private void RunAdminWeather(IPlayerCharacter character, string arguments)
		{
			string action = OperatorCommandParsing.SplitFirstWord(arguments, out string rest).ToLowerInvariant();
			IWeatherService weather = WeatherService;
			if (action.Length == 0 || action == "show")
			{
				ReportWeather(character);
				return;
			}
			if (action == "help")
			{
				ReplyLines(character, WeatherActionHelp);
				return;
			}
			if (weather == null || character?.GameObject == null)
			{
				Reply(character, "Weather is not running on this server.");
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
						Reply(character, $"Director is {(weather.IsDirectorEnabled(scene) ? "on" : "off")}. Usage: /admin weather director on|off");
						return;
					}
					bool applied = weather.SetDirector(scene, on.Value);
					Reply(character, applied ? $"Storm director {(on.Value ? "on" : "off")} for {scene.name}." : "This scene has no weather of its own, so the director stays off.");
					return;
				}
				default:
					ReplyLines(character, WeatherActionHelp);
					return;
			}
		}

		private void AdminWeatherAir(IPlayerCharacter character, IWeatherService weather, Scene scene, string[] words)
		{
			const string Usage = "Usage: /admin weather air [add] <key> <±value> … [seconds] | air clear [seconds]. Keys: temperature, humidity, pressure, instability, wind, gravity.";
			if (words.Length == 0)
			{
				weather.TryGetAirOffsets(scene, out AirOffsets now);
				Reply(character, $"Added to this scene's air at runtime: {now}. {Usage}");
				return;
			}
			float seconds = 30f;
			if (words[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
			{
				if (words.Length > 1 && !TryParseWeatherSeconds(words[1], out seconds))
				{
					Reply(character, "Usage: /admin weather air clear [seconds]");
					return;
				}
				Reply(character, weather.SetAirOffsets(scene, default, seconds)
					? $"Handing {scene.name} back to its own air over {seconds:0}s."
					: "This scene has no weather.");
				return;
			}
			bool relative = words[0].Equals("add", StringComparison.OrdinalIgnoreCase);
			int start = relative ? 1 : 0;
			if (!TryParseAirOffsets(words, start, out AirOffsets offsets, ref seconds, out string problem))
			{
				Reply(character, $"{problem} {Usage}");
				return;
			}
			if (relative && weather.TryGetAirOffsets(scene, out AirOffsets current))
			{
				offsets = current + offsets;
			}
			Reply(character, weather.SetAirOffsets(scene, offsets, seconds)
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
					case "temperature": case "temp": case "t": offsets.Temperature = Mathf.Clamp(value, -150f, 150f); break;
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
					Reply(character, "Usage: /admin weather cell spawn <kind> [radius m] [minutes]. Kinds: thunderstorm, supercell, squall, hurricane, haboob, dustdevil, eruption.");
					return;
				}
				float radius = 0f;
				float minutes = 0f;
				if (words.Length > 2 && !TryParsePositive(words[2], 1f, 10000f, out radius) || words.Length > 3 && !TryParsePositive(words[3], 1f, 720f, out minutes))
				{
					Reply(character, "Usage: /admin weather cell spawn <kind> [radius 1-10000 m] [minutes 1-720]");
					return;
				}
				ushort id = weather.SpawnCell(scene, kind, here, radius, Vector2.zero, minutes * 60f);
				Reply(character, id != 0
					? $"Cell {id} ({StormPhysics.NameOf(kind)}) over you{(radius > 0f ? $", radius {radius:0} m" : ", sized by the air")}{(minutes > 0f ? $", for {minutes:0} min" : string.Empty)}. Steer it with /admin weather cell steer {id}."
					: "No storm cells here: this scene has no weather of its own, or its world has no air to make one.");
				return;
			}
			if (verb == "steer")
			{
				float speed = 5f;
				if (words.Length < 2 || !ushort.TryParse(words[1], out ushort id) || words.Length > 2 && !TryParsePositive(words[2], 0f, 100f, out speed))
				{
					Reply(character, "Usage: /admin weather cell steer <id> [m/s 0-100]");
					return;
				}
				Reply(character, weather.SteerCell(scene, id, here, speed) ? $"Cell {id} heading for you at {speed:0.#} m/s."
					: StormSchedule.IsScheduled(id) ? $"Cell {id} is one of the world's own storms, worked out from the world clock on every side: it cannot be steered. Start one of your own (/admin weather cell spawn)." : $"No cell {id} here.");
				return;
			}
			if (verb == "retire")
			{
				float seconds = 120f;
				if (words.Length < 2 || !ushort.TryParse(words[1], out ushort id) || words.Length > 2 && !TryParseWeatherSeconds(words[2], out seconds))
				{
					Reply(character, "Usage: /admin weather cell retire <id> [seconds]");
					return;
				}
				Reply(character, weather.RetireCell(scene, id, seconds) ? $"Cell {id} fading over {seconds:0}s."
					: StormSchedule.IsScheduled(id) ? $"Cell {id} is one of the world's own storms, worked out from the world clock on every side: it cannot be ended. Turn the director off to stop them (/admin weather director off)." : $"No cell {id} here (or it is already fading).");
				return;
			}
			Reply(character, "Usage: /admin weather cell spawn|steer|retire …  (/admin weather help)");
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
	}
}
