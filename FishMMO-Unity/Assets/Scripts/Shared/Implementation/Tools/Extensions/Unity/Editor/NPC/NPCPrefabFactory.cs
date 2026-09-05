#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Which interactable role, if any, an NPC prefab carries.
	/// </summary>
	public enum NPCInteraction : byte
	{
		/// <summary>No interactable component. Monsters and pets.</summary>
		None = 0,

		/// <summary>A <see cref="Merchant"/>, backed by a <see cref="MerchantTemplate"/>.</summary>
		Merchant = 1,

		/// <summary>A <see cref="Banker"/>.</summary>
		Banker = 2,

		/// <summary>An <see cref="AbilityCrafter"/>.</summary>
		AbilityCrafter = 3,
	}

	/// <summary>
	/// Everything that makes one NPC different from another, in one plain object.
	/// </summary>
	/// <remarks>
	/// An NPC prefab is mostly boilerplate: the same fifteen components on every one, wired the
	/// same way. What a designer actually decides is what is here — which race supplies the model
	/// and faction, which archetype supplies the brain, which attribute databases, loot table and
	/// abilities. <see cref="NPCPrefabFactory.Create"/> turns a recipe into a prefab by cloning
	/// <see cref="BasePrefab"/> and rewriting exactly these fields, so the boilerplate is never
	/// authored by hand and never drifts between NPCs.
	/// </remarks>
	public sealed class NPCRecipe
	{
		/// <summary>Prefab and GameObject name. Also the Addressables address.</summary>
		public string Name;

		/// <summary>Project folder the prefab is written to, e.g. <c>Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs</c>.</summary>
		public string Folder;

		/// <summary>An existing NPC prefab whose component wiring is cloned.</summary>
		public GameObject BasePrefab;

		/// <summary>Supplies the model set and the faction. Required.</summary>
		public RaceTemplate Race;

		/// <summary>The whole AI brain. Required.</summary>
		public AIArchetypeTemplate Archetype;

		/// <summary>The attribute set the NPC spawns with. Without one it has no health.</summary>
		public CharacterAttributeTemplateDatabase AttributeDatabase;

		/// <summary>Optional per-NPC attribute bonuses rolled at spawn.</summary>
		public NPCAttributeDatabase AttributeBonuses;

		/// <summary>Optional corpse loot.</summary>
		public LootTableTemplate LootTable;

		/// <summary>Abilities learned on server start.</summary>
		public List<AbilityTemplate> Abilities = new List<AbilityTemplate>();

		/// <summary>Whether the faction controller treats everyone as an enemy.</summary>
		public bool IsAggressive;

		/// <summary>Whether a player can charm this NPC.</summary>
		public bool IsCharmable;

		/// <summary>Interactable role, for civilians.</summary>
		public NPCInteraction Interaction;

		/// <summary>Stock for a <see cref="NPCInteraction.Merchant"/>.</summary>
		public MerchantTemplate MerchantTemplate;

		/// <summary>Add the prefab to the base prefab's Addressables group so the servers can load it.</summary>
		public bool RegisterAddressable = true;
	}

	/// <summary>
	/// Creates NPC prefabs from an <see cref="NPCRecipe"/>, and reads recipes back out of
	/// existing prefabs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A prefab is produced by copying <see cref="NPCRecipe.BasePrefab"/> as an asset and then
	/// rewriting the designer-facing fields in prefab contents. Copying rather than building from
	/// components keeps the parts nobody should have to think about — the label sub-prefabs, the
	/// spawn and follow points, the placeholder model under the mesh root, the NetworkObject's
	/// prediction settings, the distance LOD bands — identical to a prefab that is known to work.
	/// </para>
	/// <para>
	/// The copy has its own asset guid, so the NetworkBehaviour owner caches inside it stay local
	/// file references; <see cref="NetworkObjectBindingValidator"/> is still run over the result
	/// and repairs it if a base prefab was already carrying a foreign reference. FishNet's prefab
	/// collection picks the new prefab up through its own asset postprocessor.
	/// </para>
	/// </remarks>
	public static class NPCPrefabFactory
	{
		/// <summary>Log category.</summary>
		private const string LOG = "NPCPrefabFactory";

		/// <summary>Folder new NPCs land in when the recipe names none.</summary>
		public const string DEFAULT_FOLDER = "Assets/Prefabs/Shared/Entity/NPCs/Monsters";

		/// <summary>Group label for a plain hostile or neutral creature.</summary>
		public const string KIND_MONSTER = "Monster";

		/// <summary>Group label for an NPC with a boss script.</summary>
		public const string KIND_BOSS = "Boss";

		/// <summary>Group label for an interactable NPC.</summary>
		public const string KIND_CIVILIAN = "Civilian";

		/// <summary>Group label for a summonable pet.</summary>
		public const string KIND_PET = "Pet";

		/// <summary>
		/// Every prefab under <c>Assets/</c> whose root carries an <see cref="NPC"/>.
		/// </summary>
		/// <remarks>
		/// Walks the asset database's own path list and takes everything with a <c>.prefab</c>
		/// extension, rather than asking the search index for <c>t:Prefab</c>. The path list is
		/// what the asset database actually knows about, so a prefab that exists is found whether
		/// or not the search index has caught up with it. Detection is by component, not by
		/// folder, so an NPC prefab saved anywhere in the project is picked up.
		/// </remarks>
		/// <param name="includeLocal">Whether prefabs under <c>Assets/LOCAL</c> are included.</param>
		/// <returns>The prefabs, sorted by name.</returns>
		public static List<GameObject> FindNPCPrefabs(bool includeLocal)
		{
			List<GameObject> prefabs = new List<GameObject>();

			foreach (string rawPath in AssetDatabase.GetAllAssetPaths())
			{
				string path = rawPath.Replace('\\', '/');
				if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
					!path.StartsWith("Assets/", StringComparison.Ordinal))
				{
					continue;
				}
				if (!includeLocal && path.StartsWith("Assets/LOCAL/", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (root != null && root.GetComponent<NPC>() != null)
				{
					prefabs.Add(root);
				}
			}

			prefabs.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
			return prefabs;
		}

		/// <summary>
		/// Names the kind of NPC a prefab is, for grouping.
		/// </summary>
		/// <param name="prefab">An NPC prefab.</param>
		/// <returns>One of the <c>KIND_</c> constants, or "Unknown".</returns>
		public static string Classify(GameObject prefab)
		{
			if (prefab == null || prefab.GetComponent<NPC>() == null)
			{
				return "Unknown";
			}
			if (prefab.GetComponent<Pet>() != null)
			{
				return KIND_PET;
			}
			AIController ai = prefab.GetComponent<AIController>();
			if (ai != null && ai.BossScript != null)
			{
				return KIND_BOSS;
			}
			if (prefab.GetComponent<Interactable>() != null)
			{
				return KIND_CIVILIAN;
			}
			return KIND_MONSTER;
		}

		/// <summary>
		/// Sort key matching <see cref="Classify"/>: monsters, bosses, civilians, pets.
		/// </summary>
		/// <param name="prefab">An NPC prefab.</param>
		/// <returns>Lower sorts first.</returns>
		public static int SortOrder(GameObject prefab)
		{
			switch (Classify(prefab))
			{
				case KIND_MONSTER: return 0;
				case KIND_BOSS: return 1;
				case KIND_CIVILIAN: return 2;
				case KIND_PET: return 3;
				default: return 4;
			}
		}

		/// <summary>
		/// The interactable role a prefab carries.
		/// </summary>
		/// <param name="prefab">An NPC prefab.</param>
		/// <returns>The role, or <see cref="NPCInteraction.None"/>.</returns>
		public static NPCInteraction InteractionOf(GameObject prefab)
		{
			if (prefab == null)
			{
				return NPCInteraction.None;
			}
			if (prefab.GetComponent<Merchant>() != null)
			{
				return NPCInteraction.Merchant;
			}
			if (prefab.GetComponent<Banker>() != null)
			{
				return NPCInteraction.Banker;
			}
			if (prefab.GetComponent<AbilityCrafter>() != null)
			{
				return NPCInteraction.AbilityCrafter;
			}
			return NPCInteraction.None;
		}

		/// <summary>
		/// The component type behind an interaction role.
		/// </summary>
		/// <param name="interaction">The role.</param>
		/// <returns>The component type, or null for <see cref="NPCInteraction.None"/>.</returns>
		public static Type InteractionComponentType(NPCInteraction interaction)
		{
			switch (interaction)
			{
				case NPCInteraction.Merchant: return typeof(Merchant);
				case NPCInteraction.Banker: return typeof(Banker);
				case NPCInteraction.AbilityCrafter: return typeof(AbilityCrafter);
				default: return null;
			}
		}

		/// <summary>
		/// Reads an existing NPC prefab back into a recipe, with the prefab as its own base.
		/// </summary>
		/// <remarks>
		/// This is how "new NPC like this one" works: every slot is pre-filled from the prefab a
		/// designer already has selected, and only what differs needs touching.
		/// </remarks>
		/// <param name="prefab">An NPC prefab.</param>
		/// <returns>A recipe describing it, or null if the prefab is not an NPC.</returns>
		public static NPCRecipe RecipeFrom(GameObject prefab)
		{
			if (prefab == null)
			{
				return null;
			}

			NPC npc = prefab.GetComponent<NPC>();
			if (npc == null)
			{
				return null;
			}

			string path = AssetDatabase.GetAssetPath(prefab);

			NPCRecipe recipe = new NPCRecipe
			{
				Name = prefab.name,
				Folder = string.IsNullOrEmpty(path) ? DEFAULT_FOLDER : Path.GetDirectoryName(path).Replace('\\', '/'),
				BasePrefab = prefab,
				AttributeBonuses = npc.AttributeBonuses,
				LootTable = npc.LootTable,
				IsCharmable = npc.IsCharmable,
				Interaction = InteractionOf(prefab),
			};

			if (npc.Abilities != null)
			{
				recipe.Abilities.AddRange(npc.Abilities);
			}

			AIController ai = prefab.GetComponent<AIController>();
			if (ai != null)
			{
				recipe.Archetype = ai.Archetype;
			}

			CharacterAttributeController attributes = prefab.GetComponent<CharacterAttributeController>();
			if (attributes != null)
			{
				recipe.AttributeDatabase = attributes.CharacterAttributeDatabase;
			}

			FactionController faction = prefab.GetComponent<FactionController>();
			if (faction != null)
			{
				/* The runtime accessor resolves through the template cache, which is empty in
				 * the editor, so the serialized ID is read and matched against the assets. */
				SerializedObject serialized = new SerializedObject(faction);
				recipe.Race = FindTemplateByID<RaceTemplate>(serialized.FindProperty("raceTemplateID").intValue);
				recipe.IsAggressive = faction.IsAggressive;
			}

			Merchant merchant = prefab.GetComponent<Merchant>();
			if (merchant != null)
			{
				recipe.MerchantTemplate = merchant.Template;
			}

			return recipe;
		}

		/// <summary>
		/// The path <see cref="Create"/> will write a recipe to.
		/// </summary>
		/// <param name="recipe">The recipe.</param>
		/// <returns>An asset path.</returns>
		public static string TargetPath(NPCRecipe recipe)
		{
			string folder = string.IsNullOrWhiteSpace(recipe.Folder) ? DEFAULT_FOLDER : recipe.Folder.Trim().Replace('\\', '/').TrimEnd('/');
			return $"{folder}/{(recipe.Name ?? string.Empty).Trim()}.prefab";
		}

		/// <summary>
		/// Checks a recipe for everything that would make <see cref="Create"/> fail or produce an
		/// NPC that spawns and then does nothing.
		/// </summary>
		/// <param name="recipe">The recipe.</param>
		/// <param name="problems">Receives one line per problem. Cleared first.</param>
		/// <returns>True when the recipe can be built.</returns>
		public static bool Validate(NPCRecipe recipe, List<string> problems)
		{
			if (problems == null)
			{
				problems = new List<string>();
			}
			problems.Clear();

			if (recipe == null)
			{
				problems.Add("No recipe.");
				return false;
			}

			string name = (recipe.Name ?? string.Empty).Trim();
			if (name.Length == 0)
			{
				problems.Add("Name is empty.");
			}
			else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("/") || name.Contains("\\"))
			{
				problems.Add("Name contains characters that cannot be used in a file name.");
			}

			string folder = (recipe.Folder ?? string.Empty).Trim().Replace('\\', '/');
			if (folder.Length == 0 || !folder.StartsWith("Assets/", StringComparison.Ordinal) && folder != "Assets")
			{
				problems.Add("Folder must be inside the project's Assets folder.");
			}

			if (recipe.BasePrefab == null)
			{
				problems.Add("No base prefab. The new NPC's component wiring is cloned from one.");
			}
			else
			{
				string basePath = AssetDatabase.GetAssetPath(recipe.BasePrefab);
				if (string.IsNullOrEmpty(basePath) || PrefabUtility.GetPrefabAssetType(recipe.BasePrefab) == PrefabAssetType.NotAPrefab)
				{
					problems.Add("Base prefab is not a prefab asset.");
				}
				else if (recipe.BasePrefab.GetComponent<NPC>() == null)
				{
					problems.Add($"Base prefab '{recipe.BasePrefab.name}' has no NPC component.");
				}
			}

			if (recipe.Race == null)
			{
				problems.Add("No race. The race supplies the model and the faction; without one the NPC is invisible and belongs to nobody.");
			}

			if (recipe.Archetype == null)
			{
				problems.Add("No AI archetype. The controller reads every state from it; without one the NPC spawns and never ticks.");
			}

			if (recipe.AttributeDatabase == null)
			{
				problems.Add("No attribute database. The NPC would have no health and count as dismissed by every save.");
			}

			if (recipe.Interaction == NPCInteraction.Merchant && recipe.MerchantTemplate == null)
			{
				problems.Add("A merchant needs a merchant template, or it has nothing to sell.");
			}

			if (recipe.Abilities != null)
			{
				for (int i = 0; i < recipe.Abilities.Count; i++)
				{
					if (recipe.Abilities[i] == null)
					{
						problems.Add($"Abilities[{i}] is empty.");
					}
				}
			}

			if (problems.Count == 0)
			{
				string target = TargetPath(recipe);
				if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(target) != null || File.Exists(target))
				{
					problems.Add($"'{target}' already exists. Pick another name or folder.");
				}
			}

			return problems.Count == 0;
		}

		/// <summary>
		/// Builds the prefab a recipe describes.
		/// </summary>
		/// <param name="recipe">A recipe that passes <see cref="Validate"/>.</param>
		/// <returns>The saved prefab asset.</returns>
		/// <exception cref="InvalidOperationException">The recipe does not validate, or the copy failed.</exception>
		public static GameObject Create(NPCRecipe recipe)
		{
			List<string> problems = new List<string>();
			if (!Validate(recipe, problems))
			{
				throw new InvalidOperationException("NPC recipe is not valid:\n" + string.Join("\n", problems));
			}

			string basePath = AssetDatabase.GetAssetPath(recipe.BasePrefab);
			string targetPath = TargetPath(recipe);
			string folder = Path.GetDirectoryName(targetPath).Replace('\\', '/');

			if (!AssetDatabase.IsValidFolder(folder))
			{
				CreateFolderRecursive(folder);
			}

			if (!AssetDatabase.CopyAsset(basePath, targetPath))
			{
				throw new InvalidOperationException($"Could not copy '{basePath}' to '{targetPath}'.");
			}

			GameObject root = PrefabUtility.LoadPrefabContents(targetPath);
			try
			{
				root.name = recipe.Name.Trim();
				Apply(root, recipe);
				PrefabUtility.SaveAsPrefabAsset(root, targetPath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}

			/* The copy carries its own guid, so the NetworkBehaviour owner caches stay local; this
			 * catches a base prefab that was already broken, rather than propagating it. */
			if (NetworkObjectBindingValidator.Scan(targetPath).Count > 0)
			{
				int repaired = NetworkObjectBindingValidator.Repair(targetPath);
				Debug.LogWarning($"[{LOG}] '{targetPath}' inherited {repaired} foreign NetworkObject binding(s) from '{basePath}' and was repaired. Repair the base prefab too.");
			}

			if (recipe.RegisterAddressable)
			{
				RegisterAddressableLike(targetPath, basePath);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			GameObject created = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
			Debug.Log($"[{LOG}] Created NPC prefab '{targetPath}' from '{basePath}'.");
			return created;
		}

		/// <summary>
		/// Writes the recipe's fields onto a prefab root opened with <see cref="PrefabUtility.LoadPrefabContents"/>.
		/// </summary>
		/// <param name="root">The prefab contents root.</param>
		/// <param name="recipe">The recipe.</param>
		private static void Apply(GameObject root, NPCRecipe recipe)
		{
			NPC npc = root.GetComponent<NPC>();
			npc.Abilities.Clear();
			if (recipe.Abilities != null)
			{
				for (int i = 0; i < recipe.Abilities.Count; i++)
				{
					AbilityTemplate template = recipe.Abilities[i];
					if (template != null && !npc.Abilities.Contains(template))
					{
						npc.Abilities.Add(template);
					}
				}
			}
			npc.LootTable = recipe.LootTable;
			npc.AttributeBonuses = recipe.AttributeBonuses;
			npc.IsCharmable = recipe.IsCharmable;

			CharacterAttributeController attributes = root.GetComponent<CharacterAttributeController>();
			if (attributes != null)
			{
				attributes.CharacterAttributeDatabase = recipe.AttributeDatabase;
			}

			AIController ai = root.GetComponent<AIController>();
			if (ai != null)
			{
				ai.Archetype = recipe.Archetype;
			}

			FactionController faction = root.GetComponent<FactionController>();
			if (faction != null)
			{
				/* Both fields are private and serialized; the race is stored as the template's
				 * deterministic ID, exactly as the TemplateReference drawer writes it. */
				SerializedObject serialized = new SerializedObject(faction);
				serialized.FindProperty("raceTemplateID").intValue = ComputeTemplateID(recipe.Race);
				serialized.FindProperty("isAggressive").boolValue = recipe.IsAggressive;
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}

			ApplyInteraction(root, recipe);
		}

		/// <summary>
		/// Makes the prefab carry exactly the interactable component the recipe asks for.
		/// </summary>
		/// <param name="root">The prefab contents root.</param>
		/// <param name="recipe">The recipe.</param>
		private static void ApplyInteraction(GameObject root, NPCRecipe recipe)
		{
			Type desired = InteractionComponentType(recipe.Interaction);
			bool hasDesired = false;

			Interactable[] existing = root.GetComponents<Interactable>();
			for (int i = 0; i < existing.Length; i++)
			{
				if (desired != null && existing[i].GetType() == desired)
				{
					hasDesired = true;
					continue;
				}
				UnityEngine.Object.DestroyImmediate(existing[i]);
			}

			if (desired != null && !hasDesired)
			{
				root.AddComponent(desired);
			}

			if (recipe.Interaction == NPCInteraction.Merchant)
			{
				Merchant merchant = root.GetComponent<Merchant>();
				if (merchant != null)
				{
					merchant.Template = recipe.MerchantTemplate;
				}
			}
		}

		/// <summary>
		/// The deterministic ID a cached template is registered under at runtime.
		/// </summary>
		/// <param name="template">A <see cref="CachedScriptableObject{T}"/> asset.</param>
		/// <returns>The ID, or 0 for null.</returns>
		public static int ComputeTemplateID(ScriptableObject template)
		{
			if (template == null)
			{
				return 0;
			}
			return (template.GetType().Name + template.name).GetDeterministicHashCode();
		}

		/// <summary>
		/// Finds the template asset whose runtime ID matches, by scanning the project.
		/// </summary>
		/// <typeparam name="T">The template type.</typeparam>
		/// <param name="id">The ID to match.</param>
		/// <returns>The asset, or null.</returns>
		public static T FindTemplateByID<T>(int id) where T : ScriptableObject
		{
			if (id == 0)
			{
				return null;
			}

			foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset != null && ComputeTemplateID(asset) == id)
				{
					return asset;
				}
			}
			return null;
		}

		/// <summary>
		/// Adds an asset to Addressables in the same group, with the same labels, as another
		/// asset, addressed by its file name.
		/// </summary>
		/// <remarks>
		/// Servers resolve NPC prefabs through Addressables, so a prefab that is not in a group is
		/// a prefab a spawner cannot use. Copying the base prefab's placement is the least
		/// surprising rule: a new orc lands wherever the orcs already are.
		/// </remarks>
		/// <param name="assetPath">The asset to register.</param>
		/// <param name="likeAssetPath">The asset whose group and labels are copied.</param>
		public static void RegisterAddressableLike(string assetPath, string likeAssetPath)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				Debug.LogWarning($"[{LOG}] No Addressables settings; '{assetPath}' was not registered.");
				return;
			}

			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			if (string.IsNullOrEmpty(guid))
			{
				return;
			}

			AddressableAssetEntry like = string.IsNullOrEmpty(likeAssetPath)
				? null
				: settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(likeAssetPath));
			AddressableAssetGroup group = like != null ? like.parentGroup : settings.DefaultGroup;
			if (group == null)
			{
				Debug.LogWarning($"[{LOG}] No Addressables group to put '{assetPath}' in.");
				return;
			}

			AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
			if (entry == null)
			{
				return;
			}

			entry.SetAddress(Path.GetFileNameWithoutExtension(assetPath));
			if (like != null)
			{
				foreach (string label in like.labels)
				{
					entry.SetLabel(label, true, true, false);
				}
			}

			settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
		}

		/// <summary>
		/// Keeps an asset's Addressables address equal to its file name after a rename.
		/// </summary>
		/// <param name="assetPath">The asset's current path.</param>
		public static void SyncAddress(string assetPath)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				return;
			}

			AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(assetPath));
			if (entry == null)
			{
				return;
			}

			string address = Path.GetFileNameWithoutExtension(assetPath);
			if (entry.address != address)
			{
				entry.SetAddress(address);
				settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
			}
		}

		/// <summary>
		/// Creates a folder and every missing parent.
		/// </summary>
		/// <param name="path">A folder path under Assets.</param>
		private static void CreateFolderRecursive(string path)
		{
			string[] parts = path.Replace('\\', '/').Split('/');
			string current = parts[0];
			for (int i = 1; i < parts.Length; i++)
			{
				string next = current + "/" + parts[i];
				if (!AssetDatabase.IsValidFolder(next))
				{
					AssetDatabase.CreateFolder(current, parts[i]);
				}
				current = next;
			}
		}
	}
}
#endif
