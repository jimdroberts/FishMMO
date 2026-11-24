using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/admin region</c>: puts a test <see cref="Region"/> down around the caller, of any kind regions come in (the name
	/// toast, a buff while inside, an attribute bonus, a resource drain, damage over time, fog), sized by a bounding box,
	/// so each can be walked into and out of and checked on a live server.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Administrator only.</b> A region that grants a buff, an attribute or healing is the same power as
	/// <c>/admin setattr</c> and <c>/admin heal</c>, which game masters do not hold.
	/// </para>
	/// <para>
	/// The region is a networked <see cref="StaffTestRegion"/> spawned into the caller's scene, so every client near it
	/// builds the same region (see that type for why the client needs its own copy). It lasts until
	/// <c>/admin region clear</c> or until its scene unloads, and affects anyone who walks in, not only the caller.
	/// </para>
	/// <para>
	/// Box: <c>WxHxD</c> metres (<c>WxD</c> keeps the default height), anywhere among the words. The box stands on the
	/// caller's feet, sunk a metre so uneven ground does not leave a gap under it.
	/// </para>
	/// </remarks>
	public partial class StaffSystem
	{
		private const int MaxTestRegions = 64;
		private const float DefaultTestRegionWidth = 12f;
		private const float DefaultTestRegionHeight = 8f;
		/// <summary>How far below the caller's feet the box's floor is.</summary>
		private const float TestRegionSink = 1f;
		/// <summary>Distance from the caller to each region of <c>all</c>, beyond the box's own half-width.</summary>
		private const float TestRegionRingGap = 6f;

		/// <summary>What <c>all</c> puts down when nothing more specific is named.</summary>
		private const string DefaultTestBuff = "Minor Increase Move Speed";
		private const string DefaultTestAttribute = "Armor";
		private const int DefaultTestAttributeBonus = 50;
		private const string DefaultTestResource = "Mana";
		private const int DefaultTestResourcePerSecond = -5;
		private const int DefaultTestDamagePerSecond = 5;
		private const float DefaultTestFogDensity = 0.05f;

		private static readonly string[] TestRegionHelp =
		{
			"region name [label] — the \"You have entered\" toast only. Every region shows its name on entry.",
			"region buff <buff> — the buff is on while you stand inside, off when you leave (predicted).",
			"region attribute <attribute> <bonus> — a bonus while inside, released on leaving (not a resource).",
			"region resource <resource> <per second> — changes a resource each second inside; negative drains.",
			"region damage <per second> [damage type] — damage each second inside, through resistances; negative heals.",
			"region fog [density] — fog on entering, cleared on leaving. Density 0.001 to 1, default 0.05.",
			"region all — one of each, in a ring around you. region list, region clear [number].",
			"Box: add WxHxD in metres (e.g. 20x10x20, or 20x20 for the default height). Default 12x8x12, at most 200.",
		};

		/// <summary>A parsed <c>/admin region</c> spawn request, before any template is looked up.</summary>
		internal sealed class TestRegionRequest
		{
			public StaffTestRegionKind Kind;
			/// <summary>One of each kind.</summary>
			public bool All;
			/// <summary>The template's name (buff, attribute, resource, damage type) or the name kind's label.</summary>
			public string Subject = string.Empty;
			public int Amount;
			public float FogDensity = DefaultTestFogDensity;
			public Vector3 Size = new Vector3(DefaultTestRegionWidth, DefaultTestRegionHeight, DefaultTestRegionWidth);
		}

		/// <summary>A spawned test region, numbered for <c>list</c> and <c>clear</c>.</summary>
		private sealed class SpawnedTestRegion
		{
			public int Number;
			public NetworkObject Instance;
			public string Description;
			public string SceneName;
			public Vector3 Position;
		}

		private readonly List<SpawnedTestRegion> testRegions = new List<SpawnedTestRegion>();
		private int testRegionNumber;
		private NetworkObject testRegionPrefab;

		private IEnumerable<OperatorCommand> BuildAdminRegionCommands()
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "region", Aliases = new[] { "testregion" }, Category = "Testing",
					Summary = "Spawns a test region around you (name, buff, attribute, resource, damage, fog or all) with a WxHxD box. /admin region help.",
					Arguments = "kind:Choice=name,buff,attribute,resource,damage,fog,all,list,clear,help;details:Text?",
					Destructive = true,
					Run = RunTestRegionCommand,
				},
			};
		}

		private void RunTestRegionCommand(IPlayerCharacter character, string arguments)
		{
			if (character == null || character.GameObject == null)
			{
				return;
			}
			string word = OperatorCommandParsing.SplitFirstWord(arguments ?? string.Empty, out string rest).ToLowerInvariant();
			switch (word)
			{
				case "list":
					ListTestRegions(character);
					return;
				case "clear":
					ClearTestRegions(character, rest);
					return;
				case "":
				case "help":
					ReplyLines(character, TestRegionHelp);
					return;
			}

			if (!TryParseTestRegion(word, rest, out TestRegionRequest request, out string problem))
			{
				Reply(character, problem);
				return;
			}

			NetworkManager networkManager = Server?.NetworkWrapper?.NetworkManager;
			NetworkObject prefab = networkManager != null ? FindTestRegionPrefab(networkManager) : null;
			if (prefab == null)
			{
				Reply(character, "This build has no test region prefab (a network prefab carrying StaffTestRegion).");
				return;
			}
			PruneTestRegions();

			int pulseTicks = Mathf.Max(1, (int)networkManager.TimeManager.TickRate);
			var settings = new List<StaffTestRegionSettings>();
			if (request.All)
			{
				foreach (StaffTestRegionKind kind in new[] { StaffTestRegionKind.Name, StaffTestRegionKind.Buff, StaffTestRegionKind.Attribute,
					StaffTestRegionKind.Resource, StaffTestRegionKind.Damage, StaffTestRegionKind.Fog })
				{
					var one = new TestRegionRequest { Kind = kind, Size = request.Size };
					FillTestRegionDefaults(one);
					if (TryResolveTestRegion(one, pulseTicks, out StaffTestRegionSettings resolved, out problem))
					{
						settings.Add(resolved);
					}
					else
					{
						Reply(character, $"Skipping {kind}: {problem}");
					}
				}
			}
			else if (TryResolveTestRegion(request, pulseTicks, out StaffTestRegionSettings resolved, out problem))
			{
				settings.Add(resolved);
			}
			else
			{
				Reply(character, problem);
				return;
			}

			if (testRegions.Count + settings.Count > MaxTestRegions)
			{
				Reply(character, $"This server already has {testRegions.Count} test regions (at most {MaxTestRegions}). /admin region clear removes them.");
				return;
			}

			Scene scene = character.GameObject.scene;
			Vector3 feet = character.Transform.position;
			Quaternion facing = Quaternion.Euler(0f, character.Transform.eulerAngles.y, 0f);
			var lines = new List<string>();
			for (int i = 0; i < settings.Count; ++i)
			{
				StaffTestRegionSettings s = StaffTestRegion.Sanitize(settings[i]);
				Vector3 floor = feet;
				if (settings.Count > 1)
				{
					// A ring starting straight ahead: neighbours a gap apart, and the caller outside them all.
					float radius = TestRegionRingRadius(s.Size, settings.Count);
					floor += facing * (Quaternion.Euler(0f, 360f * i / settings.Count, 0f) * Vector3.forward) * radius;
				}
				Vector3 center = floor + Vector3.up * (s.Size.y * 0.5f - TestRegionSink);
				if (TrySpawnTestRegion(networkManager, prefab, scene, center, facing, s, out SpawnedTestRegion spawned))
				{
					lines.Add($"#{spawned.Number} {spawned.Description}");
				}
				else
				{
					lines.Add($"Could not spawn '{s.Label}'.");
				}
			}
			Log.Warning("StaffSystem", $"Administrator '{character.Account}' spawned {lines.Count} test region(s) in '{scene.name}' at {feet}.");
			ReplyLines(character, lines);
		}

		/// <summary>
		/// How far from the caller each of <paramref name="count"/> boxes of <paramref name="size"/> stands: neighbours a
		/// gap apart (measured by the box's diagonal, so the facing cannot make them touch) and the caller outside every one.
		/// </summary>
		internal static float TestRegionRingRadius(Vector3 size, int count)
		{
			float diagonal = new Vector2(size.x, size.z).magnitude;
			float clear = diagonal * 0.5f + TestRegionRingGap;
			if (count < 2)
			{
				return clear;
			}
			return Mathf.Max(clear, (diagonal + TestRegionRingGap) / (2f * Mathf.Sin(Mathf.PI / count)));
		}

		/// <summary>
		/// Reads the words after the kind into a request: the box (<c>WxHxD</c>, anywhere), then the kind's own words.
		/// Pure: templates are looked up later.
		/// </summary>
		internal static bool TryParseTestRegion(string kindWord, string rest, out TestRegionRequest request, out string problem)
		{
			request = new TestRegionRequest();
			problem = null;

			var words = new List<string>((rest ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
			for (int i = words.Count - 1; i >= 0; --i)
			{
				if (!LooksLikeBox(words[i]))
				{
					continue;
				}
				if (!TryParseBox(words[i], out Vector3 size, out problem))
				{
					return false;
				}
				request.Size = size;
				words.RemoveAt(i);
			}

			switch ((kindWord ?? string.Empty).ToLowerInvariant())
			{
				case "all":
					request.All = true;
					return true;

				case "name":
					request.Kind = StaffTestRegionKind.Name;
					request.Subject = string.Join(" ", words);
					return true;

				case "buff":
					request.Kind = StaffTestRegionKind.Buff;
					request.Subject = string.Join(" ", words);
					if (request.Subject.Length == 0)
					{
						problem = "Name the buff: /admin region buff <buff> [WxHxD].";
						return false;
					}
					return true;

				case "attribute":
				case "attr":
				case "resource":
				{
					bool resource = kindWord.Equals("resource", StringComparison.OrdinalIgnoreCase);
					request.Kind = resource ? StaffTestRegionKind.Resource : StaffTestRegionKind.Attribute;
					string usage = resource
						? "/admin region resource <resource> <per second> [WxHxD] (negative drains)."
						: "/admin region attribute <attribute> <bonus> [WxHxD].";
					if (words.Count < 2 || !TryParseWhole(words[words.Count - 1], out int amount) || amount == 0)
					{
						problem = "Name it and give a non-zero amount last: " + usage;
						return false;
					}
					request.Amount = amount;
					words.RemoveAt(words.Count - 1);
					request.Subject = string.Join(" ", words);
					return true;
				}

				case "damage":
				{
					request.Kind = StaffTestRegionKind.Damage;
					if (words.Count < 1 || !TryParseWhole(words[0], out int amount) || amount == 0)
					{
						problem = "Give a non-zero amount per second first: /admin region damage <per second> [damage type] [WxHxD] (negative heals).";
						return false;
					}
					request.Amount = amount;
					words.RemoveAt(0);
					request.Subject = string.Join(" ", words);
					return true;
				}

				case "fog":
					request.Kind = StaffTestRegionKind.Fog;
					if (words.Count > 0)
					{
						if (words.Count > 1 || !float.TryParse(words[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float density) ||
							density <= 0f || density > 1f)
						{
							problem = "Fog density is one number above 0, at most 1: /admin region fog [density] [WxHxD].";
							return false;
						}
						request.FogDensity = density;
					}
					return true;

				default:
					problem = $"No region kind '{OperatorCommandParsing.Truncate(kindWord, 16)}'. Kinds: name, buff, attribute, resource, damage, fog, all; or list, clear, help.";
					return false;
			}
		}

		/// <summary>True for a word shaped like a box, <c>20x10x20</c> or <c>20x20</c>.</summary>
		private static bool LooksLikeBox(string word)
		{
			int x = word.IndexOf('x');
			return x > 0 && x < word.Length - 1 && char.IsDigit(word[0]) && char.IsDigit(word[word.Length - 1]);
		}

		/// <summary>Parses <c>WxHxD</c> (or <c>WxD</c>, default height) into a size within the region limits.</summary>
		internal static bool TryParseBox(string word, out Vector3 size, out string problem)
		{
			size = default;
			problem = null;
			string[] parts = word.ToLowerInvariant().Split('x');
			var edges = new float[parts.Length];
			for (int i = 0; i < parts.Length; ++i)
			{
				if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out edges[i]) ||
					float.IsNaN(edges[i]) || edges[i] < StaffTestRegion.MinimumEdge || edges[i] > StaffTestRegion.MaximumEdge)
				{
					problem = $"'{OperatorCommandParsing.Truncate(word, 24)}' is not a box: WxHxD (or WxD), each {StaffTestRegion.MinimumEdge:0} to {StaffTestRegion.MaximumEdge:0} metres.";
					return false;
				}
			}
			switch (edges.Length)
			{
				case 2:
					size = new Vector3(edges[0], DefaultTestRegionHeight, edges[1]);
					return true;
				case 3:
					size = new Vector3(edges[0], edges[1], edges[2]);
					return true;
				default:
					problem = $"'{OperatorCommandParsing.Truncate(word, 24)}' is not a box: WxHxD (or WxD).";
					return false;
			}
		}

		private static bool TryParseWhole(string word, out int value)
		{
			return int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
		}

		/// <summary>What one region of <c>all</c> carries.</summary>
		private static void FillTestRegionDefaults(TestRegionRequest request)
		{
			switch (request.Kind)
			{
				case StaffTestRegionKind.Buff:
					request.Subject = DefaultTestBuff;
					break;
				case StaffTestRegionKind.Attribute:
					request.Subject = DefaultTestAttribute;
					request.Amount = DefaultTestAttributeBonus;
					break;
				case StaffTestRegionKind.Resource:
					request.Subject = DefaultTestResource;
					request.Amount = DefaultTestResourcePerSecond;
					break;
				case StaffTestRegionKind.Damage:
					request.Amount = DefaultTestDamagePerSecond;
					break;
			}
		}

		/// <summary>Looks up the request's template and turns it into the settings the region is built from.</summary>
		private static bool TryResolveTestRegion(TestRegionRequest request, int pulseTicks, out StaffTestRegionSettings settings, out string problem)
		{
			problem = null;
			settings = new StaffTestRegionSettings
			{
				Kind = request.Kind,
				Size = request.Size,
				Amount = request.Amount,
				PulseTicks = pulseTicks,
				FogDensity = request.FogDensity,
			};
			string box = $"{request.Size.x:0.#}x{request.Size.y:0.#}x{request.Size.z:0.#} m";
			switch (request.Kind)
			{
				case StaffTestRegionKind.Name:
					settings.Label = request.Subject.Length > 0 ? request.Subject : "Test Region";
					return true;

				case StaffTestRegionKind.Buff:
				{
					if (!TryFindTemplateByName(request.Subject, "buff", out BaseBuffTemplate buff, out problem))
					{
						return false;
					}
					settings.TemplateID = buff.ID;
					settings.Label = $"Buff: {buff.Name}";
					return true;
				}

				case StaffTestRegionKind.Attribute:
				case StaffTestRegionKind.Resource:
				{
					bool resource = request.Kind == StaffTestRegionKind.Resource;
					if (!TryFindTemplateByName(request.Subject, resource ? "resource" : "attribute", out CharacterAttributeTemplate attribute, out problem,
						t => !(t is DamageAttributeTemplate) && t.IsResourceAttribute == resource))
					{
						return false;
					}
					settings.TemplateID = attribute.ID;
					settings.Label = resource
						? $"{attribute.Name} {request.Amount:+#;-#}/s"
						: $"{attribute.Name} {request.Amount:+#;-#}";
					return true;
				}

				case StaffTestRegionKind.Damage:
				{
					string type = string.Empty;
					if (request.Subject.Length > 0)
					{
						if (!TryFindTemplateByName(request.Subject, "damage type", out DamageAttributeTemplate damageType, out problem))
						{
							return false;
						}
						settings.TemplateID = damageType.ID;
						type = " " + damageType.Name;
					}
					settings.Label = request.Amount > 0 ? $"Damage {request.Amount}/s{type}" : $"Healing {-request.Amount}/s";
					return true;
				}

				case StaffTestRegionKind.Fog:
					settings.Label = $"Fog {request.FogDensity:0.###}";
					return true;
			}
			problem = $"Unknown region kind {request.Kind} ({box}).";
			return false;
		}

		/// <summary>
		/// A loaded template of type <typeparamref name="T"/> by its display or asset name (case-insensitive), whatever
		/// concrete type it was cached under; near misses are offered when nothing matches.
		/// </summary>
		private static bool TryFindTemplateByName<T>(string text, string what, out T template, out string problem, Func<T, bool> filter = null)
			where T : CachedScriptableObject
		{
			template = null;
			problem = null;
			text = (text ?? string.Empty).Trim();
			var all = new List<T>();
			CachedScriptableObject.CollectAll(all);
			if (filter != null)
			{
				all.RemoveAll(t => !filter(t));
			}
			if (all.Count == 0)
			{
				problem = $"No {what} templates are loaded on this scene server.";
				return false;
			}
			template = all.FirstOrDefault(t => string.Equals(t.name, text, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(DisplayName(t), text, StringComparison.OrdinalIgnoreCase));
			if (template != null)
			{
				return true;
			}
			List<string> near = all
				.Where(t => t.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
				.Select(t => t.name)
				.OrderBy(n => n.Length)
				.Take(4)
				.ToList();
			problem = near.Count > 0
				? $"No {what} named '{OperatorCommandParsing.Truncate(text, 32)}'. Did you mean: {string.Join(", ", near)}?"
				: $"No {what} named '{OperatorCommandParsing.Truncate(text, 32)}'.";
			return false;
		}

		/// <summary>The name a template shows players, where it has one apart from its asset name.</summary>
		private static string DisplayName(CachedScriptableObject template)
		{
			switch (template)
			{
				case BaseBuffTemplate buff: return buff.Name;
				case CharacterAttributeTemplate attribute: return attribute.Name;
				default: return template.name;
			}
		}

		/// <summary>The one spawnable network prefab carrying a <see cref="StaffTestRegion"/>, found once.</summary>
		private NetworkObject FindTestRegionPrefab(NetworkManager networkManager)
		{
			if (testRegionPrefab != null)
			{
				return testRegionPrefab;
			}
			FishNet.Managing.Object.PrefabObjects prefabs = networkManager.SpawnablePrefabs;
			if (prefabs == null)
			{
				return null;
			}
			int count = prefabs.GetObjectCount();
			for (int i = 0; i < count; ++i)
			{
				NetworkObject prefab = prefabs.GetObject(true, i);
				if (prefab != null && prefab.GetComponent<StaffTestRegion>() != null)
				{
					testRegionPrefab = prefab;
					break;
				}
			}
			return testRegionPrefab;
		}

		private bool TrySpawnTestRegion(NetworkManager networkManager, NetworkObject prefab, Scene scene, Vector3 center, Quaternion rotation,
			StaffTestRegionSettings settings, out SpawnedTestRegion spawned)
		{
			spawned = null;
			/* Instantiated, not taken from the pool: the triggers it makes belong to this one instance and are destroyed with
			 * it, which a pooled copy would carry into its next life. */
			NetworkObject instance = UnityEngine.Object.Instantiate(prefab, center, rotation);
			bool completed = false;
			try
			{
				SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
				StaffTestRegion region = instance.GetComponent<StaffTestRegion>();
				region.Configure(settings);
				networkManager.ServerManager.Spawn(instance, null, scene);
				completed = true;
			}
			catch (Exception ex)
			{
				Log.Error("StaffSystem", $"Spawning test region '{settings.Label}' failed: {ex}");
			}
			finally
			{
				if (!completed)
				{
					if (instance.IsSpawned)
					{
						networkManager.ServerManager.Despawn(instance, DespawnType.Destroy);
					}
					else
					{
						UnityEngine.Object.Destroy(instance.gameObject);
					}
				}
			}
			if (!completed)
			{
				return false;
			}
			StaffTestRegionSettings applied = instance.GetComponent<StaffTestRegion>().Settings;
			spawned = new SpawnedTestRegion
			{
				Number = ++testRegionNumber,
				Instance = instance,
				SceneName = scene.name,
				Position = center,
				Description = $"{applied.Label} ({applied.Kind}), {applied.Size.x:0.#}x{applied.Size.y:0.#}x{applied.Size.z:0.#} m",
			};
			testRegions.Add(spawned);
			return true;
		}

		/// <summary>Forgets regions that are gone (their scene unloaded, or something else despawned them).</summary>
		private void PruneTestRegions()
		{
			testRegions.RemoveAll(r => r.Instance == null || !r.Instance.IsSpawned);
		}

		private void ListTestRegions(IPlayerCharacter character)
		{
			PruneTestRegions();
			if (testRegions.Count == 0)
			{
				Reply(character, "No test regions on this scene server.");
				return;
			}
			ReplyLines(character, testRegions.Select(r =>
				$"#{r.Number} {r.Description}, {r.SceneName} ({r.Position.x:0}, {r.Position.y:0}, {r.Position.z:0})"));
		}

		private void ClearTestRegions(IPlayerCharacter character, string rest)
		{
			PruneTestRegions();
			string word = OperatorCommandParsing.SplitFirstWord(rest ?? string.Empty, out _);
			int number = 0;
			if (word.Length > 0 && word != "all" && (!int.TryParse(word.TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out number) || number <= 0))
			{
				Reply(character, "Clear all test regions, or one by its number from /admin region list: /admin region clear [number].");
				return;
			}
			NetworkManager networkManager = Server?.NetworkWrapper?.NetworkManager;
			int removed = 0;
			for (int i = testRegions.Count - 1; i >= 0; --i)
			{
				SpawnedTestRegion region = testRegions[i];
				if (number != 0 && region.Number != number)
				{
					continue;
				}
				if (region.Instance != null && region.Instance.IsSpawned && networkManager != null)
				{
					// Despawning raises every member's Exit (Region.OnDisable), which releases its contributions.
					networkManager.ServerManager.Despawn(region.Instance, DespawnType.Destroy);
				}
				testRegions.RemoveAt(i);
				++removed;
			}
			Reply(character, number != 0 && removed == 0
				? $"No test region #{number}. /admin region list shows them."
				: $"Removed {removed} test region(s).");
		}
	}
}
