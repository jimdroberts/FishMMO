#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.Core;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Makes the Auctioneer NPC (issue #299): a copy of the human banker with its Banker swapped for an
	/// <see cref="Auctioneer"/> whose interaction is "Auctioneer Interact". Made once and then kept, like the Stablemaster.
	/// </summary>
	public static class AuctioneerBuilder
	{
		/// <summary>Where the Auctioneer lives.</summary>
		public const string PrefabFolder = "Assets/FishMMO/Modules/AuctionHouse/Prefabs";

		/// <summary>The Auctioneer prefab (also its Addressables address, without the extension).</summary>
		public const string AuctioneerPrefabPath = PrefabFolder + "/an auctioneer.prefab";

		/// <summary>The NPC it is cloned from: a service NPC standing still, with no stock.</summary>
		public const string SourcePath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab";

		/// <summary>Its interaction trigger.</summary>
		public const string TriggerPath = "Assets/FishMMO/Modules/AuctionHouse/Templates/Interactions/Auctioneer Interact.asset";

		[DashboardTool(DashboardToolAttribute.Maintenance, "Build auctioneer", Section = "Auction House", Order = 1,
			Tooltip = "Makes the Auctioneer NPC from the human banker (issue #299). Idempotent: an existing Auctioneer is kept.")]
		public static void BuildFromDashboard() => Debug.Log("[AuctionHouse]\n  " + string.Join("\n  ", Build()));

		/// <summary><c>-executeMethod FishMMO.Shared.AuctioneerBuilder.BuildBatch</c>: exits 0 unless something failed.</summary>
		public static void BuildBatch()
		{
			List<string> notes = Build();
			Debug.Log("[AuctionHouse]\n  " + string.Join("\n  ", notes));
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(notes.Exists(n => n.StartsWith("FAILED")) ? 1 : 0);
			}
		}

		/// <summary>Makes the Auctioneer if it does not exist. Returns what it did.</summary>
		public static List<string> Build()
		{
			var notes = new List<string>();
			if (AssetDatabase.LoadAssetAtPath<GameObject>(AuctioneerPrefabPath) != null)
			{
				notes.Add($"Kept {AuctioneerPrefabPath}.");
				return notes;
			}
			Trigger trigger = AssetDatabase.LoadAssetAtPath<Trigger>(TriggerPath);
			if (trigger == null)
			{
				notes.Add($"FAILED: {TriggerPath} is missing.");
				return notes;
			}
			GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
			NPCRecipe recipe = source != null ? NPCPrefabFactory.RecipeFrom(source) : null;
			if (recipe == null)
			{
				notes.Add($"FAILED: {SourcePath} is not an NPC prefab.");
				return notes;
			}
			recipe.Name = Path.GetFileNameWithoutExtension(AuctioneerPrefabPath);
			recipe.Folder = PrefabFolder;
			// None strips the Banker; the Auctioneer (a module component the factory does not know) is added below.
			recipe.Interaction = NPCInteraction.None;
			recipe.IsAggressive = false;
			recipe.IsCharmable = false;
			recipe.LootTable = null;
			recipe.RegisterAddressable = true;
			var problems = new List<string>();
			if (!NPCPrefabFactory.Validate(recipe, problems))
			{
				notes.Add($"FAILED: {AuctioneerPrefabPath}: {string.Join("; ", problems)}");
				return notes;
			}
			if (NPCPrefabFactory.Create(recipe) == null)
			{
				notes.Add($"FAILED: could not make {AuctioneerPrefabPath}.");
				return notes;
			}

			GameObject root = PrefabUtility.LoadPrefabContents(AuctioneerPrefabPath);
			try
			{
				Auctioneer auctioneer = root.GetComponent<Auctioneer>() ?? root.AddComponent<Auctioneer>();
				auctioneer.InteractionRange = 3.5f;
				var serialized = new SerializedObject(auctioneer);
				SerializedProperty triggers = serialized.FindProperty("onInteractTriggers");
				triggers.ClearArray();
				triggers.InsertArrayElementAtIndex(0);
				triggers.GetArrayElementAtIndex(0).objectReferenceValue = trigger;
				serialized.ApplyModifiedPropertiesWithoutUndo();
				// The factory drew an NPC with no role as a plain NPC; an auctioneer is a service, as the banker is.
				MapMarker marker = root.GetComponent<MapMarker>();
				if (marker != null)
				{
					marker.Type = MapMarkerType.Service;
				}
				PrefabUtility.SaveAsPrefabAsset(root, AuctioneerPrefabPath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
			AssetDatabase.SaveAssets();
			notes.Add($"Made {AuctioneerPrefabPath}.");
			return notes;
		}
	}
}
#endif
