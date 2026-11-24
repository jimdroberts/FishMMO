#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Makes the smiths: the Weaponsmith, who sells the weapons, and the Armorsmith, who sells the armour and shields. Their
	/// stock is taken off the general merchant, which keeps its potions, rations and scrolls.
	/// </summary>
	/// <remarks>
	/// <para><b>Stock.</b> Every <see cref="WeaponTemplate"/> under <see cref="ItemFolder"/> to the Weaponsmith, every
	/// <see cref="ArmorTemplate"/> there (the shield is one) to the Armorsmith, and every weapon and armour piece off
	/// <see cref="GeneralGoodsPath"/>. A new item template in that folder is on its smith's list at the next run.</para>
	/// <para><b>The NPCs.</b> Copies of the general merchant (its model, its brain, its naming) selling the smith's stock, made
	/// once and then kept. World Design places them (PointOfInterestServiceNpcs): at a settlement's smithy, the Armorsmith only in
	/// a town or larger or a garrison, and both in every capital. Re-cut a scene to put them in it.</para>
	/// <para><b>Idempotent.</b> A template is written over in place (its GUID, every reference to it and its addressable entry
	/// survive); everything made is registered in <see cref="SharedGroup"/>, as the general merchant is.</para>
	/// </remarks>
	public static class SmithContentBuilder
	{
		public const string MerchantRoot = "Assets/FishMMO/Modules/Commerce/Templates/NPCs/Human/Merchants";
		public const string GeneralGoodsPath = MerchantRoot + "/GeneralGoods/GeneralGoods.asset";
		public const string WeaponsmithPath = MerchantRoot + "/Weaponsmith/Weaponsmith.asset";
		public const string ArmorsmithPath = MerchantRoot + "/Armorsmith/Armorsmith.asset";

		public const string PrefabRoot = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Merchants";
		public const string MerchantSourcePath = PrefabRoot + "/GeneralGoods/HumanGeneralMerchant.prefab";
		/// <summary>Where World Design looks for them (PointOfInterestNpcTable.WeaponsmithPath, ArmorsmithPath).</summary>
		public const string WeaponsmithPrefabPath = PrefabRoot + "/Weaponsmith/HumanWeaponsmith.prefab";
		public const string ArmorsmithPrefabPath = PrefabRoot + "/Armorsmith/HumanArmorsmith.prefab";

		/// <summary>The items module's templates: what the smiths stock.</summary>
		public const string ItemFolder = "Assets/FishMMO/Modules/Items/Templates";
		public const string SharedGroup = "Shared_Static_Permanent";

		[DashboardTool(DashboardToolAttribute.Maintenance, "Build smiths", Section = "Merchants", Order = 1,
			Tooltip = "Makes the Weaponsmith and the Armorsmith (their stock and NPCs) and takes the weapons and armour off the general merchant. Idempotent. Re-cut a scene to place them.")]
		public static void BuildFromDashboard() => Debug.Log("[Smiths]\n  " + string.Join("\n  ", Build()));

		/// <summary><c>-executeMethod FishMMO.Shared.SmithContentBuilder.BuildBatch</c>: exits 0 unless something failed.</summary>
		public static void BuildBatch()
		{
			List<string> notes = Build();
			Debug.Log("[Smiths]\n  " + string.Join("\n  ", notes));
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(notes.Exists(n => n.StartsWith("FAILED")) ? 1 : 0);
			}
		}

		public static List<string> Build()
		{
			var notes = new List<string>();
			MerchantTemplate general = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(GeneralGoodsPath);
			if (general == null)
			{
				notes.Add($"FAILED: no general merchant at {GeneralGoodsPath}.");
				return notes;
			}

			List<BaseItemTemplate> weapons = Stock<WeaponTemplate>(general);
			List<BaseItemTemplate> armour = Stock<ArmorTemplate>(general);

			MerchantTemplate weaponsmith = SaveMerchant(WeaponsmithPath, "Weaponsmith", weapons, notes);
			MerchantTemplate armorsmith = SaveMerchant(ArmorsmithPath, "Armorsmith", armour, notes);

			// Off the general merchant: what a smith now sells.
			int before = general.Items != null ? general.Items.Count : 0;
			general.Items?.RemoveAll(item => item is WeaponTemplate || item is ArmorTemplate);
			int moved = before - (general.Items != null ? general.Items.Count : 0);
			EditorUtility.SetDirty(general);
			notes.Add($"General Goods: {moved} weapon/armour item(s) taken off ({general.Items?.Count ?? 0} left).");
			AssetDatabase.SaveAssets();

			Register(new[] { WeaponsmithPath, ArmorsmithPath }, notes);
			EnsureNpc(WeaponsmithPrefabPath, weaponsmith, notes);
			EnsureNpc(ArmorsmithPrefabPath, armorsmith, notes);
			AssetDatabase.SaveAssets();
			notes.Add($"Weaponsmith: {weapons.Count} item(s); Armorsmith: {armour.Count} item(s). Re-cut a scene to place them.");
			return notes;
		}

		/// <summary>Every <typeparamref name="T"/> under <see cref="ItemFolder"/> and on the general merchant, each once, by name.</summary>
		private static List<BaseItemTemplate> Stock<T>(MerchantTemplate general) where T : BaseItemTemplate
		{
			var stock = new List<BaseItemTemplate>();
			foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { ItemFolder }))
			{
				T item = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (item != null && !stock.Contains(item))
				{
					stock.Add(item);
				}
			}
			if (general.Items != null)
			{
				foreach (BaseItemTemplate item in general.Items)
				{
					if (item is T && !stock.Contains(item))
					{
						stock.Add(item);
					}
				}
			}
			stock.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			return stock;
		}

		/// <summary>The merchant template at <paramref name="path"/>, made or written over in place, selling <paramref name="items"/>.</summary>
		private static MerchantTemplate SaveMerchant(string path, string title, List<BaseItemTemplate> items, List<string> notes)
		{
			EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(path);
			bool fresh = merchant == null;
			if (fresh)
			{
				merchant = ScriptableObject.CreateInstance<MerchantTemplate>();
			}
			// The merchant's title is its description (Merchant.Title).
			merchant.Description = title;
			merchant.Items = new List<BaseItemTemplate>(items);
			merchant.Abilities ??= new List<AbilityTemplate>();
			merchant.AbilityEvents ??= new List<AbilityEvent>();
			merchant.PremadeAbilities ??= new List<PremadeAbilityTemplate>();
			// A smith takes old gear in trade.
			merchant.BuysItems = true;
			if (fresh)
			{
				AssetDatabase.CreateAsset(merchant, path);
				notes.Add($"Made {path}.");
			}
			EditorUtility.SetDirty(merchant);
			return merchant;
		}

		/// <summary>The smith's NPC: a copy of the general merchant selling <paramref name="merchant"/>'s stock (made once, then kept).</summary>
		private static void EnsureNpc(string path, MerchantTemplate merchant, List<string> notes)
		{
			if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
			{
				notes.Add($"Kept {path}.");
				return;
			}
			GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(MerchantSourcePath);
			NPCRecipe recipe = source != null ? NPCPrefabFactory.RecipeFrom(source) : null;
			if (recipe == null)
			{
				notes.Add($"FAILED: {MerchantSourcePath} is not an NPC prefab.");
				return;
			}
			string folder = Path.GetDirectoryName(path).Replace('\\', '/');
			EnsureFolder(folder);
			recipe.Name = Path.GetFileNameWithoutExtension(path);
			recipe.Folder = folder;
			recipe.Interaction = NPCInteraction.Merchant;
			recipe.MerchantTemplate = merchant;
			recipe.IsAggressive = false;
			recipe.IsCharmable = false;
			recipe.LootTable = null;
			recipe.RegisterAddressable = true;
			var problems = new List<string>();
			if (!NPCPrefabFactory.Validate(recipe, problems))
			{
				notes.Add($"FAILED: {path}: {string.Join("; ", problems)}");
				return;
			}
			GameObject created = NPCPrefabFactory.Create(recipe);
			notes.Add(created != null ? $"Made {path}." : $"FAILED: could not make {path}.");
		}

		private static void Register(IEnumerable<string> paths, List<string> notes)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			AddressableAssetGroup group = settings != null ? settings.FindGroup(SharedGroup) : null;
			if (group == null)
			{
				notes.Add($"FAILED: Addressable group '{SharedGroup}' not found: nothing registered.");
				return;
			}
			foreach (string path in paths)
			{
				string guid = AssetDatabase.AssetPathToGUID(path);
				if (string.IsNullOrEmpty(guid))
				{
					notes.Add($"FAILED: '{path}' does not exist.");
					continue;
				}
				AddressableAssetEntry entry = settings.FindAssetEntry(guid);
				if (entry == null || entry.parentGroup != group)
				{
					entry = settings.CreateOrMoveEntry(guid, group, false, false);
				}
				if (entry == null)
				{
					notes.Add($"FAILED: could not register '{path}'.");
					continue;
				}
				entry.address = Path.GetFileNameWithoutExtension(path);
				entry.SetLabel(SharedGroup, true, true, false);
			}
			EditorUtility.SetDirty(group);
		}

		private static void EnsureFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}
	}
}
#endif
