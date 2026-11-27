#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Shared.Core;
using FishNet.Object;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Makes the Mercenaries module's assets (issue #135): the four kits, each kit's hired mercenary and its Mercenary for
	/// hire, the server system, and their Addressables entries. Idempotent: a run keeps what exists and brings every
	/// kit's abilities, archetype, price and prefab back to the specs below, so a re-run is how a kit is changed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Both NPCs are made from the human banker, as the auctioneer is: it carries a <see cref="SceneObjectNamer"/> ahead of
	/// its <see cref="NPC"/>, which the payload order requires and which is what lets the hired mercenary wear the name and
	/// face of the one hired. The hired one takes the guard's attribute database and bonus (a fighter's numbers, fixed per
	/// kit), the matching pet archetype (so it follows and supports by the pet rules), the kit's abilities, and the
	/// <see cref="Mercenary"/> script in place of <see cref="NPC"/>. The one for hire keeps the banker's brain, stands at its
	/// post, and carries a <see cref="MercenaryRecruiter"/> with the "Mercenary Interact" trigger.
	/// </para>
	/// <para>Batch: <c>-executeMethod FishMMO.Shared.MercenaryBuilder.BuildBatch</c>.</para>
	/// </remarks>
	public static class MercenaryBuilder
	{
		public const string ModuleFolder = "Assets/FishMMO/Modules/Mercenaries";
		public const string PrefabFolder = ModuleFolder + "/Prefabs";
		public const string KitFolder = ModuleFolder + "/Templates/Kits";
		public const string TriggerPath = ModuleFolder + "/Templates/Interactions/Mercenary Interact.asset";
		public const string SystemPath = ModuleFolder + "/Server/MercenarySystem.asset";
		public const string PanelPath = ModuleFolder + "/Client/UIMercenary.prefab";
		public const string MercenaryScriptPath = ModuleFolder + "/Runtime/Mercenary.cs";

		public const string BankerPath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab";
		public const string GuardPath = "Assets/Prefabs/Shared/Entity/NPCs/Settlement/a guard.prefab";
		public const string CurrencyPath = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Misc/Currency.asset";
		public const string ArchetypeFolder = "Assets/FishMMO/Modules/NPCs/Templates/AI/Archetypes";
		public const string AbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types";

		public const string SharedGroup = "Shared_Static_Permanent";
		public const string ServerGroup = "Server_Static_Permanent";
		public const string ClientGroup = "Client_Dynamic";
		public const string SystemsLabel = "SceneServer_Systems";
		public const string PanelsLabel = "ClientWorldGUI_Panels";

		/// <summary>One kit as the builder makes it.</summary>
		public sealed class KitSpec
		{
			public string Key;
			public string DisplayName;
			public string Description;
			public long HireCost;
			public string Archetype;
			public string[] Abilities;
		}

		/// <summary>The kits: Warrior, Archer, Wizard, Healer.</summary>
		public static readonly KitSpec[] Kits =
		{
			new KitSpec
			{
				Key = "Warrior", DisplayName = "Warrior", HireCost = 500, Archetype = "Pet - Melee",
				Description = "Fights up close: cuts, cripples and stuns what attacks you.",
				Abilities = new[] { "Melee/Stab", "Melee/Swipe", "Melee/Hamstring", "Control/Stunning Blow" },
			},
			new KitSpec
			{
				Key = "Archer", DisplayName = "Archer", HireCost = 600, Archetype = "Pet - Archer",
				Description = "Shoots from range and roots what closes in.",
				Abilities = new[] { "Projectile/Piercing Arrow", "Projectile/Crossbow Shot", "Projectile/Ricochet Shot", "Control/Root" },
			},
			new KitSpec
			{
				Key = "Wizard", DisplayName = "Wizard", HireCost = 750, Archetype = "Pet - Caster",
				Description = "Casts fire, lightning and frost from range.",
				Abilities = new[] { "Lesser Fireball", "Projectile/Chain Lightning", "Control/Freezing Bolt", "Over Time/Ignite" },
			},
			new KitSpec
			{
				Key = "Healer", DisplayName = "Healer", HireCost = 800, Archetype = "Pet - Healer",
				Description = "Keeps you and your party alive, and burns what threatens you.",
				Abilities = new[] { "Heal/Lesser Heal", "Over Time/Rejuvenate", "Area/Area Lesser Heal", "Beam/Searing Beam" },
			},
		};

		public static string KitPath(KitSpec spec) => $"{KitFolder}/{spec.Key} Mercenary Kit.asset";

		public static string MercenaryPath(KitSpec spec) => $"{PrefabFolder}/a mercenary {spec.Key.ToLowerInvariant()}.prefab";

		public static string RecruiterPath(KitSpec spec) => $"{PrefabFolder}/{Article(spec.Key)} {spec.Key.ToLowerInvariant()} for hire.prefab";

		/// <summary>"an" before a vowel, else "a".</summary>
		private static string Article(string word) => !string.IsNullOrEmpty(word) && "AEIOUaeiou".IndexOf(word[0]) >= 0 ? "an" : "a";

		/// <summary>The path a first build wrote before the article was right ("a archer"); renamed in place when found.</summary>
		private static string LegacyRecruiterPath(KitSpec spec) => $"{PrefabFolder}/a {spec.Key.ToLowerInvariant()} for hire.prefab";

		[DashboardTool(DashboardToolAttribute.Maintenance, "Build mercenaries", Section = "Mercenaries", Order = 1,
			Tooltip = "Makes the mercenary kits, the hired NPCs and the Mercenaries for hire, the server system and their Addressables entries (issue #135). Idempotent.")]
		public static void BuildFromDashboard() => Debug.Log("[Mercenaries]\n  " + string.Join("\n  ", Build()));

		public static void BuildBatch()
		{
			List<string> notes;
			try
			{
				notes = Build();
			}
			catch (Exception ex)
			{
				notes = new List<string> { "FAILED: " + ex };
			}
			Debug.Log("[Mercenaries]\n  " + string.Join("\n  ", notes));
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(notes.Exists(n => n.StartsWith("FAILED")) ? 1 : 0);
			}
		}

		public static List<string> Build()
		{
			var notes = new List<string>();
			GameObject banker = AssetDatabase.LoadAssetAtPath<GameObject>(BankerPath);
			GameObject guard = AssetDatabase.LoadAssetAtPath<GameObject>(GuardPath);
			Trigger trigger = AssetDatabase.LoadAssetAtPath<Trigger>(TriggerPath);
			MonoScript mercenaryScript = AssetDatabase.LoadAssetAtPath<MonoScript>(MercenaryScriptPath);
			if (banker == null || guard == null || trigger == null || mercenaryScript == null)
			{
				notes.Add($"FAILED: missing {(banker == null ? BankerPath : guard == null ? GuardPath : trigger == null ? TriggerPath : MercenaryScriptPath)}.");
				return notes;
			}
			NPCRecipe guardRecipe = NPCPrefabFactory.RecipeFrom(guard);
			EnsureFolder(KitFolder);
			EnsureFolder(PrefabFolder);

			foreach (KitSpec spec in Kits)
			{
				MercenaryKitTemplate kit = EnsureKit(spec, notes);
				if (kit == null)
				{
					return notes;
				}
				GameObject hired = EnsureMercenaryPrefab(spec, kit, banker, guardRecipe, mercenaryScript, notes);
				if (hired == null)
				{
					return notes;
				}
				kit.Prefab = hired.GetComponent<NetworkObject>();
				EditorUtility.SetDirty(kit);
				if (EnsureRecruiterPrefab(spec, kit, banker, trigger, notes) == null)
				{
					return notes;
				}
				RegisterAddressable(KitPath(spec), SharedGroup, SharedGroup);
			}

			RegisterAddressable(TriggerPath, SharedGroup, SharedGroup);
			EnsureSystemAsset(notes);
			RegisterAddressable(SystemPath, ServerGroup, SystemsLabel, ServerGroup);
			if (AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath) != null)
			{
				RegisterAddressable(PanelPath, ClientGroup, PanelsLabel);
			}
			else
			{
				notes.Add($"FAILED: {PanelPath} is missing.");
			}

			AssetDatabase.SaveAssets();
			notes.Add("Done.");
			return notes;
		}

		private static MercenaryKitTemplate EnsureKit(KitSpec spec, List<string> notes)
		{
			string path = KitPath(spec);
			MercenaryKitTemplate kit = AssetDatabase.LoadAssetAtPath<MercenaryKitTemplate>(path);
			if (kit == null)
			{
				kit = ScriptableObject.CreateInstance<MercenaryKitTemplate>();
				AssetDatabase.CreateAsset(kit, path);
				notes.Add($"Made {path}.");
			}
			kit.DisplayName = spec.DisplayName;
			kit.Description = spec.Description;
			kit.HireCost = spec.HireCost;
			kit.Abilities.Clear();
			foreach (AbilityTemplate ability in LoadAbilities(spec, notes))
			{
				kit.Abilities.Add(ability);
			}
			if (kit.Abilities.Count != spec.Abilities.Length)
			{
				notes.Add($"FAILED: {path} is missing abilities.");
				return null;
			}
			EditorUtility.SetDirty(kit);
			return kit;
		}

		private static List<AbilityTemplate> LoadAbilities(KitSpec spec, List<string> notes)
		{
			var abilities = new List<AbilityTemplate>(spec.Abilities.Length);
			foreach (string name in spec.Abilities)
			{
				string path = $"{AbilityFolder}/{name}.asset";
				AbilityTemplate ability = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(path);
				if (ability == null)
				{
					notes.Add($"{spec.Key}: {path} not found.");
					continue;
				}
				abilities.Add(ability);
			}
			return abilities;
		}

		/// <summary>The hired NPC for a kit: the banker made a fighter, with the kit's brain and abilities, as a Mercenary.</summary>
		private static GameObject EnsureMercenaryPrefab(KitSpec spec, MercenaryKitTemplate kit, GameObject banker, NPCRecipe guardRecipe, MonoScript mercenaryScript, List<string> notes)
		{
			string path = MercenaryPath(spec);
			AIArchetypeTemplate archetype = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>($"{ArchetypeFolder}/{spec.Archetype}.asset");
			if (archetype == null)
			{
				notes.Add($"FAILED: archetype '{spec.Archetype}' not found.");
				return null;
			}

			if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
			{
				NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(banker);
				recipe.Name = Path.GetFileNameWithoutExtension(path);
				recipe.Folder = PrefabFolder;
				recipe.Interaction = NPCInteraction.None;
				recipe.IsAggressive = false;
				recipe.IsCharmable = false;
				recipe.LootTable = null;
				recipe.Archetype = archetype;
				recipe.AttributeDatabase = guardRecipe.AttributeDatabase;
				recipe.AttributeBonuses = guardRecipe.AttributeBonuses;
				recipe.Abilities.Clear();
				recipe.Abilities.AddRange(LoadAbilities(spec, notes));
				recipe.RegisterAddressable = true;
				var problems = new List<string>();
				if (!NPCPrefabFactory.Validate(recipe, problems) || NPCPrefabFactory.Create(recipe) == null)
				{
					notes.Add($"FAILED: could not make {path}: {string.Join("; ", problems)}");
					return null;
				}
				notes.Add($"Made {path}.");
			}

			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				NPC npc = root.GetComponent<NPC>();
				if (npc == null)
				{
					notes.Add($"FAILED: {path} has no NPC.");
					return null;
				}
				if (!(npc is Mercenary))
				{
					// The same component, another script: every field the NPC had is the Mercenary's too.
					var serializedNpc = new SerializedObject(npc);
					serializedNpc.FindProperty("m_Script").objectReferenceValue = mercenaryScript;
					serializedNpc.ApplyModifiedPropertiesWithoutUndo();
					npc = root.GetComponent<NPC>();
				}
				npc.Abilities.Clear();
				npc.Abilities.AddRange(kit.Abilities);
				npc.IsCharmable = false;
				npc.LootTable = null;
				MapMarker marker = root.GetComponent<MapMarker>();
				if (marker != null)
				{
					marker.Label = string.Empty;
				}
				PrefabUtility.SaveAsPrefabAsset(root, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}

			GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (saved == null || !(saved.GetComponent<NPC>() is Mercenary))
			{
				notes.Add($"FAILED: {path} is not a Mercenary after the script swap.");
				return null;
			}
			AIBrainCatalogueEditorUtility.SetArchetype(saved, archetype);
			NPCPrefabFactory.RegisterAddressableLike(path, BankerPath);
			return saved;
		}

		/// <summary>The Mercenary for hire for a kit: the banker with the recruiter in place of the bank.</summary>
		private static GameObject EnsureRecruiterPrefab(KitSpec spec, MercenaryKitTemplate kit, GameObject banker, Trigger trigger, List<string> notes)
		{
			string path = RecruiterPath(spec);
			string legacy = LegacyRecruiterPath(spec);
			if (legacy != path && AssetDatabase.LoadAssetAtPath<GameObject>(path) == null && AssetDatabase.LoadAssetAtPath<GameObject>(legacy) != null)
			{
				// Same asset, same guid (scenes and Addressables keep it): only its name changes.
				string error = AssetDatabase.MoveAsset(legacy, path);
				if (!string.IsNullOrEmpty(error))
				{
					notes.Add($"FAILED: could not rename {legacy}: {error}");
					return null;
				}
				NPCPrefabFactory.SyncAddress(path);
				notes.Add($"Renamed {legacy} to {path}.");
			}
			if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
			{
				NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(banker);
				recipe.Name = Path.GetFileNameWithoutExtension(path);
				recipe.Folder = PrefabFolder;
				// None strips the Banker; the recruiter (a module component the factory does not know) is added below.
				recipe.Interaction = NPCInteraction.None;
				recipe.IsAggressive = false;
				recipe.IsCharmable = false;
				recipe.LootTable = null;
				recipe.RegisterAddressable = true;
				var problems = new List<string>();
				if (!NPCPrefabFactory.Validate(recipe, problems) || NPCPrefabFactory.Create(recipe) == null)
				{
					notes.Add($"FAILED: could not make {path}: {string.Join("; ", problems)}");
					return null;
				}
				notes.Add($"Made {path}.");
			}

			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				MercenaryRecruiter recruiter = root.GetComponent<MercenaryRecruiter>() ?? root.AddComponent<MercenaryRecruiter>();
				recruiter.InteractionRange = 3.5f;
				recruiter.SetKitForEditor(kit);
				var serialized = new SerializedObject(recruiter);
				SerializedProperty triggers = serialized.FindProperty("onInteractTriggers");
				triggers.ClearArray();
				triggers.InsertArrayElementAtIndex(0);
				triggers.GetArrayElementAtIndex(0).objectReferenceValue = trigger;
				serialized.ApplyModifiedPropertiesWithoutUndo();
				// A service, as the banker and the auctioneer are.
				MapMarker marker = root.GetComponent<MapMarker>();
				if (marker != null)
				{
					marker.Type = MapMarkerType.Service;
					marker.Label = string.Empty;
				}
				PrefabUtility.SaveAsPrefabAsset(root, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
			return AssetDatabase.LoadAssetAtPath<GameObject>(path);
		}

		private static void EnsureSystemAsset(List<string> notes)
		{
			MercenarySystem system = AssetDatabase.LoadAssetAtPath<MercenarySystem>(SystemPath);
			if (system == null)
			{
				system = ScriptableObject.CreateInstance<MercenarySystem>();
				AssetDatabase.CreateAsset(system, SystemPath);
				notes.Add($"Made {SystemPath}.");
			}
			CharacterAttributeTemplate currency = AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(CurrencyPath);
			if (currency == null)
			{
				notes.Add($"FAILED: {CurrencyPath} not found.");
				return;
			}
			var serialized = new SerializedObject(system);
			serialized.FindProperty("currencyTemplate").objectReferenceValue = currency;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(system);
		}

		/// <summary>Puts an asset in a group with labels, addressed by its file name.</summary>
		private static void RegisterAddressable(string assetPath, string groupName, params string[] labels)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			AddressableAssetGroup group = settings != null ? settings.FindGroup(groupName) : null;
			if (group == null || string.IsNullOrEmpty(guid))
			{
				Debug.LogWarning($"[Mercenaries] '{assetPath}' was not registered: no group '{groupName}' or no asset.");
				return;
			}
			AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
			if (entry == null)
			{
				return;
			}
			entry.SetAddress(Path.GetFileNameWithoutExtension(assetPath));
			foreach (string label in labels)
			{
				entry.SetLabel(label, true, true, false);
			}
			settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
		}

		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}
	}
}
#endif
