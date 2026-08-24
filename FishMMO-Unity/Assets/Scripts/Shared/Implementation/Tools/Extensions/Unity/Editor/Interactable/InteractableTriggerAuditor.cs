using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Reports interactables that would do nothing when a player uses them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// There is no default behaviour left in the interactable classes — <see cref="Banker"/>,
	/// <see cref="Merchant"/>, <see cref="Bindstone"/> and the rest are data holders, and the ECA
	/// trigger list is the entire implementation. That is the right shape, but it moves a whole
	/// class of failure out of the compiler's reach: an interactable with an empty list compiles,
	/// imports, spawns, shows its title, accepts the interaction, and does nothing at all.
	/// </para>
	/// <para>
	/// Every interactable in the project was in exactly that state at once, which is what this
	/// exists to stop happening again. Run it after adding an interactable, and before shipping a
	/// scene.
	/// </para>
	/// </remarks>
	public static class InteractableTriggerAuditor
	{
		/// <summary>Log category.</summary>
		private const string LOG = "InteractableTriggerAuditor";

		/// <summary>
		/// Reports every interactable in the project's prefabs and scenes that has no usable
		/// interaction triggers.
		/// </summary>
		[MenuItem("FishMMO/Interactables/Audit Interact Triggers", priority = 220)]
		public static void AuditInteractTriggers()
		{
			List<string> problems = new List<string>();
			int examined = 0;

			examined += AuditPrefabs(problems);
			examined += AuditScenes(problems);

			if (examined == 0)
			{
				Debug.Log($"[{LOG}] No interactables found.");
				return;
			}

			if (problems.Count == 0)
			{
				Debug.Log($"[{LOG}] {examined} interactable(s) examined; every one has at least one interaction trigger.");
				return;
			}

			StringBuilder report = new StringBuilder();
			report.AppendLine($"[{LOG}] {examined} interactable(s) examined, {problems.Count} problem(s):");
			problems.Sort();
			foreach (string problem in problems)
			{
				report.AppendLine(problem);
			}
			report.AppendLine();
			report.AppendLine("Interaction behaviour comes entirely from ECA triggers. An interactable with none " +
							  "accepts the interaction and does nothing. Assign a Trigger asset " +
							  "(FishMMO/ECA/Trigger, or the shipped ones under Assets/Templates/Entity/ECA/Interactions).");

			Debug.LogWarning(report.ToString());
		}

		/// <summary>
		/// Walks every prefab in the project.
		/// </summary>
		/// <param name="problems">Receives one line per problem found.</param>
		/// <returns>How many interactables were examined.</returns>
		private static int AuditPrefabs(List<string> problems)
		{
			int examined = 0;

			foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/", System.StringComparison.Ordinal))
				{
					continue;
				}

				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null)
				{
					continue;
				}

				examined += Examine(prefab, path, problems);
			}

			return examined;
		}

		/// <summary>
		/// Walks every scene under Assets/Scenes.
		/// </summary>
		/// <param name="problems">Receives one line per problem found.</param>
		/// <returns>How many interactables were examined.</returns>
		private static int AuditScenes(List<string> problems)
		{
			int examined = 0;

			foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.StartsWith("Assets/Scenes/", System.StringComparison.Ordinal))
				{
					continue;
				}

				Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
				try
				{
					if (!scene.IsValid())
					{
						continue;
					}
					foreach (GameObject root in scene.GetRootGameObjects())
					{
						examined += Examine(root, path, problems);
					}
				}
				finally
				{
					EditorSceneManager.CloseScene(scene, removeScene: true);
				}
			}

			return examined;
		}

		/// <summary>
		/// Examines one hierarchy for interactables with unusable trigger lists.
		/// </summary>
		/// <remarks>
		/// NPCs are exempt from the empty-list check. Corpse looting is wired directly into the
		/// interaction handler precisely so a creature with no triggers is still lootable, so an
		/// empty list on an NPC means "no extras", not "broken". A null <em>entry</em> is still
		/// reported — that is an authoring slip either way.
		/// </remarks>
		/// <param name="root">The hierarchy root.</param>
		/// <param name="path">The asset path, for the report.</param>
		/// <param name="problems">Receives one line per problem found.</param>
		/// <returns>How many interactables were examined.</returns>
		private static int Examine(GameObject root, string path, List<string> problems)
		{
			int examined = 0;

			foreach (Interactable interactable in root.GetComponentsInChildren<Interactable>(includeInactive: true))
			{
				examined++;

				List<Trigger> triggers = interactable.OnInteractTriggers;

				if (triggers == null || triggers.Count < 1)
				{
					problems.Add($"  {interactable.GetType().Name} '{interactable.name}' has no interaction triggers ({path})");
					continue;
				}

				for (int i = 0; i < triggers.Count; ++i)
				{
					if (triggers[i] == null)
					{
						problems.Add($"  {interactable.GetType().Name} '{interactable.name}' has an empty trigger at index {i} ({path})");
					}
				}
			}

			// NPCs carry their own trigger list and are lootable without one; only null entries matter.
			foreach (NPC npc in root.GetComponentsInChildren<NPC>(includeInactive: true))
			{
				examined++;

				List<Trigger> triggers = npc.OnInteractTriggers;
				if (triggers == null)
				{
					continue;
				}
				for (int i = 0; i < triggers.Count; ++i)
				{
					if (triggers[i] == null)
					{
						problems.Add($"  NPC '{npc.name}' has an empty trigger at index {i} ({path})");
					}
				}
			}

			return examined;
		}
	}
}
