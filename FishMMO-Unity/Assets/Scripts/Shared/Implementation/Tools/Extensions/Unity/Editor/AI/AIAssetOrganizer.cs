using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sorts every AI asset in the project into the canonical folder layout.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Assets are moved with <see cref="AssetDatabase.MoveAsset"/>, which carries the <c>.meta</c>
	/// file — and therefore the GUID — along with the asset. Every existing reference from a
	/// prefab, archetype or scene survives the move untouched.
	/// </para>
	/// <para>
	/// The layout is keyed off asset <em>type</em> rather than off a hard-coded list of paths, so
	/// it keeps working as new assets are authored. Run it again at any time; assets already in
	/// the right place are left alone.
	/// </para>
	/// </remarks>
	public static class AIAssetOrganizer
	{
		/// <summary>Log category.</summary>
		private const string LOG = "AIAssetOrganizer";

		/// <summary>Root of the AI asset tree.</summary>
		public const string AI_ROOT = "Assets/Templates/Entity/NPCs/AI";

		/// <summary>
		/// The canonical destination folder for each AI asset type, most-derived first.
		/// </summary>
		/// <remarks>
		/// Order matters: the first entry whose type is assignable from the asset's type wins, so
		/// the specific attacking states must precede <see cref="BaseAttackingState"/>, which must
		/// itself precede <see cref="BaseAIState"/>.
		/// </remarks>
		private static readonly List<(Type Type, string Folder)> layout = new List<(Type, string)>
		{
			// The primary authoring surface gets the top-level folder.
			(typeof(AIArchetypeTemplate), AI_ROOT + "/Archetypes"),
			(typeof(AICombatPersonality), AI_ROOT + "/Personalities"),

			// Combat: everything the NPC does while it has a target.
			(typeof(BaseAttackingState), AI_ROOT + "/States/Attack"),
			(typeof(OrbitState), AI_ROOT + "/States/Combat"),
			(typeof(GetBehindState), AI_ROOT + "/States/Combat"),
			(typeof(RetreatState), AI_ROOT + "/States/Combat"),

			// Out of combat.
			(typeof(PetIdleState), AI_ROOT + "/States/Pet"),
			(typeof(IdleState), AI_ROOT + "/States/Movement"),
			(typeof(WanderState), AI_ROOT + "/States/Movement"),
			(typeof(PatrolState), AI_ROOT + "/States/Movement"),
			(typeof(ReturnHomeState), AI_ROOT + "/States/Movement"),

			// Catch-all for any state type added later, so nothing is left loose at the root.
			(typeof(BaseAIState), AI_ROOT + "/States"),

			// Ability selection.
			(typeof(AIAbilityRotation), AI_ROOT + "/Rotations"),
			(typeof(AIAbilityCondition), AI_ROOT + "/Conditions"),

			// Decision layer.
			(typeof(AIBehaviorTree), AI_ROOT + "/BehaviorTrees"),
			(typeof(AIBehaviorNode), AI_ROOT + "/BehaviorNodes"),

			// Encounter scripting and performance.
			(typeof(BossScript), AI_ROOT + "/Boss"),
			(typeof(AILodSettings), AI_ROOT + "/LOD"),
		};

		/// <summary>
		/// Moves every AI asset into its canonical folder.
		/// </summary>
		[MenuItem("FishMMO/AI/Organize AI Assets", priority = 203)]
		public static void OrganizeAIAssets()
		{
			StringBuilder report = new StringBuilder();
			int moved = 0;
			int scanned = 0;
			int failed = 0;

			/* Every destination folder is created up front, and deliberately NOT inside a
			 * StartAssetEditing / StopAssetEditing batch. CreateFolder is deferred while asset
			 * editing is paused, so MoveAsset inside the same batch sees a destination that does
			 * not exist yet and fails every single move with "Parent directory is not in asset
			 * database". Moving sixty assets unbatched costs nothing worth optimising. */
			for (int i = 0; i < layout.Count; i++)
			{
				EnsureFolder(layout[i].Folder);
			}
			AssetDatabase.Refresh();

			foreach ((Type type, string folder) in layout)
			{
				// t:<Type> also matches subclasses, so filter to the entry that actually owns
				// each asset rather than moving a derived asset with its base's rule.
				foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name))
				{
					string path = AssetDatabase.GUIDToAssetPath(guid);
					UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
					if (asset == null)
					{
						continue;
					}

					if (ResolveFolder(asset.GetType()) != folder)
					{
						continue;
					}

					scanned++;

					string destination = folder + "/" + System.IO.Path.GetFileName(path);
					if (path == destination)
					{
						continue;
					}

					string error = AssetDatabase.MoveAsset(path, destination);
					if (string.IsNullOrEmpty(error))
					{
						moved++;
						report.AppendLine($"  {path}\n    -> {destination}");
					}
					else
					{
						failed++;
						report.AppendLine($"  FAILED {path} -> {destination}: {error}");
					}
				}
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			int removed = RemoveEmptyFolders(AI_ROOT);
			if (removed > 0)
			{
				report.AppendLine($"  removed {removed} folder(s) left empty by the move");
				AssetDatabase.Refresh();
			}

			string summary = $"[{LOG}] Considered {scanned} AI asset(s), moved {moved}, failed {failed}.";
			if (failed > 0)
			{
				Debug.LogWarning(summary + "\n" + report);
				return;
			}
			Debug.Log(summary + (moved > 0 ? "\n" + report : string.Empty));
		}

		/// <summary>
		/// Rewrites every AI asset so newly added serialized fields appear in the YAML.
		/// </summary>
		/// <remarks>
		/// Unity fills a field missing from an asset's YAML with the C# field initializer, so
		/// behaviour is already correct after a script change — but the value is invisible in the
		/// file and a designer diffing or hand-editing the asset cannot see it. Re-serializing
		/// writes the effective value out, which also means a later change to the initializer does
		/// not silently retune every existing asset.
		/// </remarks>
		[MenuItem("FishMMO/AI/Re-serialize AI Assets", priority = 204)]
		public static void ReserializeAIAssets()
		{
			List<string> paths = new List<string>();

			foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { AI_ROOT }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!AssetDatabase.IsValidFolder(path))
				{
					paths.Add(path);
				}
			}

			if (paths.Count < 1)
			{
				Debug.Log($"[{LOG}] No AI assets found to re-serialize.");
				return;
			}

			AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeAssets);
			AssetDatabase.SaveAssets();

			Debug.Log($"[{LOG}] Re-serialized {paths.Count} AI asset(s).");
		}

		/// <summary>
		/// Returns the canonical folder for an asset type, or null when the type is not an AI asset.
		/// </summary>
		/// <param name="assetType">The concrete asset type.</param>
		/// <returns>The destination folder, or null.</returns>
		public static string ResolveFolder(Type assetType)
		{
			if (assetType == null)
			{
				return null;
			}

			for (int i = 0; i < layout.Count; i++)
			{
				if (layout[i].Type.IsAssignableFrom(assetType))
				{
					return layout[i].Folder;
				}
			}
			return null;
		}

		/// <summary>
		/// Deletes folders under the AI root that hold no assets, depth-first.
		/// </summary>
		/// <remarks>
		/// Scoped to <see cref="AI_ROOT"/> deliberately — an organizer that can delete anywhere in
		/// the project is a hazard, and the only empty folders this creates are the ones it just
		/// emptied. The AI root itself is never removed.
		/// </remarks>
		/// <param name="folder">Folder to clean, project-relative.</param>
		/// <returns>Number of folders deleted.</returns>
		private static int RemoveEmptyFolders(string folder)
		{
			if (!AssetDatabase.IsValidFolder(folder))
			{
				return 0;
			}

			int removed = 0;

			foreach (string child in AssetDatabase.GetSubFolders(folder))
			{
				removed += RemoveEmptyFolders(child);

				/* Never delete a folder the layout owns, even when it is empty. These are the
				 * directories the FishMMO Dashboard creates new assets into, so removing one
				 * because nothing happens to live there yet breaks asset creation for that
				 * category until somebody notices. */
				if (IsLayoutFolder(child))
				{
					continue;
				}

				// Re-check after the recursion: a folder that only held empty folders is now empty.
				if (IsFolderEmpty(child) && AssetDatabase.DeleteAsset(child))
				{
					removed++;
				}
			}

			return removed;
		}

		/// <summary>
		/// True when a folder is one of the canonical destinations, or a parent of one.
		/// </summary>
		/// <param name="folder">Folder to test, project-relative.</param>
		/// <returns>True if the layout owns this folder.</returns>
		private static bool IsLayoutFolder(string folder)
		{
			for (int i = 0; i < layout.Count; i++)
			{
				string owned = layout[i].Folder;
				if (owned == folder || owned.StartsWith(folder + "/", StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// True when a folder contains no sub-folders and no assets.
		/// </summary>
		/// <param name="folder">Folder to test, project-relative.</param>
		/// <returns>True if the folder is empty.</returns>
		private static bool IsFolderEmpty(string folder)
		{
			if (AssetDatabase.GetSubFolders(folder).Length > 0)
			{
				return false;
			}

			// FindAssets with no type filter returns every asset beneath the folder.
			return AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length == 0;
		}

		/// <summary>
		/// Creates a folder and every missing parent above it.
		/// </summary>
		/// <param name="folder">Project-relative folder path, e.g. "Assets/Foo/Bar".</param>
		public static void EnsureFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
			{
				return;
			}

			string[] parts = folder.Split('/');
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
