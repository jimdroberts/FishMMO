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
	/// <c>/admin weather</c> and <c>/admin climate</c>: changing the weather of the scene the
	/// administrator stands in.
	/// </summary>
	/// <remarks>
	/// Admin-only because weather changes gameplay (exposure buffs, spawns, ability modifiers).
	/// Game masters have the read-only <c>/gm weather</c>. Every use is audited at the command gate.
	/// </remarks>
	public partial class SceneServerSystem
	{
		private static readonly string[] WeatherActionHelp =
		{
			"weather show — this scene's layers, nearby cells and the weather where you stand",
			"weather preset <name> [intensity 0-1] [seconds] — replace the scene layers with a preset",
			"weather layer add <template> [intensity] [seconds] | layer set <handle> <intensity> [seconds]",
			"weather layer remove <handle> [seconds] | clear [seconds] — fade layers out",
			"weather cell spawn <preset> [radius m] [minutes] — a storm at your position",
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
					Summary = "Shows or changes this scene's weather. /admin weather help lists the actions.",
					Arguments = "action:Choice=show,preset,layer,cell,director,clear,help?;details:Text?",
					Run = RunAdminWeather,
				},
				new OperatorCommand
				{
					Name = "climate", Category = "World",
					Summary = "Shifts this scene's temperature and humidity (-1..1) over a time.",
					Arguments = "temperature:Number;humidity:Number;seconds:Word?",
					Run = RunAdminClimate,
				},
				new OperatorCommand
				{
					Name = "clock", Category = "World",
					Summary = "Reports the world clock, the date and your local time. Nothing can set it.",
					Run = (c, a) => ReportClock(c),
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
				case "preset":
					AdminWeatherPreset(character, weather, scene, words);
					return;
				case "layer":
					AdminWeatherLayer(character, weather, scene, words);
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
				case "clear":
				{
					float seconds = 30f;
					if (words.Length > 0 && !TryParseWeatherSeconds(words[0], out seconds))
					{
						Reply(character, "Usage: /admin weather clear [seconds]");
						return;
					}
					Reply(character, weather.ClearLayers(scene, seconds) ? $"Fading every scene layer out over {seconds:0}s. Storm cells are untouched." : "This scene has no weather.");
					return;
				}
				default:
					ReplyLines(character, WeatherActionHelp);
					return;
			}
		}

		private void AdminWeatherPreset(IPlayerCharacter character, IWeatherService weather, Scene scene, string[] words)
		{
			if (words.Length == 0 || !TryFindPreset(words[0], out WeatherPreset preset))
			{
				Reply(character, $"Usage: /admin weather preset <name> [intensity] [seconds]. Presets: {ListNames(AllPresets())}");
				return;
			}
			float intensity = 1f;
			float seconds = preset.TransitionSeconds;
			if (words.Length > 1 && !TryParseUnit(words[1], out intensity) || words.Length > 2 && !TryParseWeatherSeconds(words[2], out seconds))
			{
				Reply(character, "Usage: /admin weather preset <name> [intensity 0-1] [seconds]");
				return;
			}
			Reply(character, weather.ApplyPreset(scene, preset, intensity, seconds)
				? $"{preset.ResolvedName} at {intensity:0.00} over {seconds:0}s."
				: "This scene has no weather.");
		}

		private void AdminWeatherLayer(IPlayerCharacter character, IWeatherService weather, Scene scene, string[] words)
		{
			string verb = words.Length > 0 ? words[0].ToLowerInvariant() : string.Empty;
			if (verb == "add")
			{
				if (words.Length < 2 || !TryFindTemplate(words[1], out WeatherLayerTemplate template))
				{
					Reply(character, $"Usage: /admin weather layer add <template> [intensity] [seconds]. Templates: {ListNames(AllTemplates())}");
					return;
				}
				float intensity = 1f;
				float seconds = template.DefaultTransitionSeconds;
				if (words.Length > 2 && !TryParseUnit(words[2], out intensity) || words.Length > 3 && !TryParseWeatherSeconds(words[3], out seconds))
				{
					Reply(character, "Usage: /admin weather layer add <template> [intensity 0-1] [seconds]");
					return;
				}
				ushort handle = weather.AddLayer(scene, template, intensity, seconds);
				Reply(character, handle != 0 ? $"Layer #{handle} {template.name} to {intensity:0.00} over {seconds:0}s." : "This scene has no weather.");
				return;
			}
			if (verb == "set")
			{
				if (words.Length < 3 || !ushort.TryParse(words[1], out ushort handle) || !TryParseUnit(words[2], out float intensity))
				{
					Reply(character, "Usage: /admin weather layer set <handle> <intensity 0-1> [seconds]");
					return;
				}
				float seconds = 30f;
				if (words.Length > 3 && !TryParseWeatherSeconds(words[3], out seconds))
				{
					Reply(character, "Usage: /admin weather layer set <handle> <intensity 0-1> [seconds]");
					return;
				}
				Reply(character, weather.SetLayerIntensity(scene, handle, intensity, seconds) ? $"Layer #{handle} to {intensity:0.00} over {seconds:0}s." : $"No layer #{handle} here.");
				return;
			}
			if (verb == "remove")
			{
				float seconds = 30f;
				if (words.Length < 2 || !ushort.TryParse(words[1], out ushort handle) || words.Length > 2 && !TryParseWeatherSeconds(words[2], out seconds))
				{
					Reply(character, "Usage: /admin weather layer remove <handle> [seconds]");
					return;
				}
				Reply(character, weather.RemoveLayer(scene, handle, seconds) ? $"Layer #{handle} fading out over {seconds:0}s." : $"No layer #{handle} here.");
				return;
			}
			Reply(character, "Usage: /admin weather layer add|set|remove …  (/admin weather help)");
		}

		private void AdminWeatherCell(IPlayerCharacter character, IWeatherService weather, Scene scene, string[] words)
		{
			string verb = words.Length > 0 ? words[0].ToLowerInvariant() : string.Empty;
			Vector3 here = character.Transform.position;
			if (verb == "spawn")
			{
				if (words.Length < 2 || !TryFindPreset(words[1], out WeatherPreset preset))
				{
					Reply(character, $"Usage: /admin weather cell spawn <preset> [radius m] [minutes]. Presets: {ListNames(AllPresets())}");
					return;
				}
				float radius = Mathf.Max(preset.CellRadiusMeters.x, 250f);
				float minutes = Mathf.Max(preset.DurationMinutes.x, 10f);
				if (words.Length > 2 && !TryParsePositive(words[2], 10f, 10000f, out radius) || words.Length > 3 && !TryParsePositive(words[3], 1f, 720f, out minutes))
				{
					Reply(character, "Usage: /admin weather cell spawn <preset> [radius 10-10000 m] [minutes 1-720]");
					return;
				}
				ushort id = weather.SpawnCell(scene, preset, here, radius, Vector2.zero, minutes * 60f);
				Reply(character, id != 0 ? $"Cell {id} ({preset.ResolvedName}) over you, radius {radius:0} m, for {minutes:0} min. Steer it with /admin weather cell steer {id}." : "This scene has no storm cells (no weather, or fixed weather).");
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
				Reply(character, weather.SteerCell(scene, id, here, speed) ? $"Cell {id} heading for you at {speed:0.#} m/s." : $"No cell {id} here.");
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
				Reply(character, weather.RetireCell(scene, id, seconds) ? $"Cell {id} fading over {seconds:0}s." : $"No cell {id} here (or it is already fading).");
				return;
			}
			Reply(character, "Usage: /admin weather cell spawn|steer|retire …  (/admin weather help)");
		}

		private void RunAdminClimate(IPlayerCharacter character, string arguments)
		{
			string[] words = SplitWords(arguments);
			IWeatherService weather = WeatherService;
			if (words.Length < 2
				|| !float.TryParse(words[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float temperature)
				|| !float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float humidity)
				|| temperature < -1f || temperature > 1f || humidity < -1f || humidity > 1f)
			{
				Reply(character, "Usage: /admin climate <temperature -1..1> <humidity -1..1> [seconds]. 0 0 returns to normal.");
				return;
			}
			float seconds = 600f;
			if (words.Length > 2 && !TryParseWeatherSeconds(words[2], out seconds))
			{
				Reply(character, "Usage: /admin climate <temperature -1..1> <humidity -1..1> [seconds]");
				return;
			}
			if (weather == null || character?.GameObject == null || !weather.SetClimateOffset(character.GameObject.scene, temperature, humidity, seconds))
			{
				Reply(character, "This scene has no weather.");
				return;
			}
			Reply(character, $"Climate shift T {temperature:+0.00;-0.00} H {humidity:+0.00;-0.00} over {seconds:0}s for {character.GameObject.scene.name}.");
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

		private static bool TryParseUnit(string word, out float value)
		{
			return float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0f && value <= 1f;
		}

		private static bool TryParsePositive(string word, float min, float max, out float value)
		{
			return float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
		}

		private static bool TryFindPreset(string word, out WeatherPreset preset)
		{
			string key = NameKey(word);
			foreach (WeatherPreset candidate in AllPresets())
			{
				if (candidate != null && (NameKey(candidate.name) == key || NameKey(candidate.DisplayName) == key))
				{
					preset = candidate;
					return true;
				}
			}
			preset = null;
			return false;
		}

		private static bool TryFindTemplate(string word, out WeatherLayerTemplate template)
		{
			string key = NameKey(word);
			foreach (WeatherLayerTemplate candidate in AllTemplates())
			{
				if (candidate != null && (NameKey(candidate.name) == key || NameKey(candidate.Kind.ToString()) == key))
				{
					template = candidate;
					return true;
				}
			}
			template = null;
			return false;
		}

		/// <summary>Every loaded preset. The cache is null until the first one loads.</summary>
		private static IEnumerable<WeatherPreset> AllPresets()
		{
			Dictionary<int, WeatherPreset> cache = WeatherPreset.GetCache<WeatherPreset>();
			return cache != null ? (IEnumerable<WeatherPreset>)cache.Values : Array.Empty<WeatherPreset>();
		}

		/// <summary>Every loaded layer template.</summary>
		private static IEnumerable<WeatherLayerTemplate> AllTemplates()
		{
			Dictionary<int, WeatherLayerTemplate> cache = WeatherLayerTemplate.GetCache<WeatherLayerTemplate>();
			return cache != null ? (IEnumerable<WeatherLayerTemplate>)cache.Values : Array.Empty<WeatherLayerTemplate>();
		}

		/// <summary>Lowercase letters only, so "Heavy Rain" and "heavyrain" match.</summary>
		private static string NameKey(string text) => FishMMO.Shared.Biomes.BiomeClimateVariant.Normalize(text);

		private static string ListNames<T>(IEnumerable<T> assets) where T : UnityEngine.Object
		{
			var names = new List<string>();
			foreach (T asset in assets)
			{
				if (asset != null)
				{
					names.Add(asset.name.Replace(" ", string.Empty));
				}
			}
			names.Sort(StringComparer.OrdinalIgnoreCase);
			string joined = names.Count > 0 ? string.Join(", ", names) : "(none loaded)";
			return OperatorCommandParsing.Truncate(joined, 60);
		}
	}
}
