using System.Collections.Generic;
using System.Text;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Editor tooling that brings NPC prefabs up to the requirements of the ability system, and
	/// reports any that still cannot fight.
	/// </summary>
	/// <remarks>
	/// <para>
	/// NPC ability activation runs through the same predicted pipeline players use, which needs
	/// three things on the prefab that NPC prefabs historically did not have: a
	/// <see cref="CharacterPredictionController"/> to drive the replicate stream, a
	/// <see cref="CooldownController"/> for the ability picker to consult, and
	/// <c>EnablePrediction</c> set on the <see cref="NetworkObject"/>. Without all three an NPC
	/// spawns, ticks its brain, decides to cast — and nothing happens.
	/// </para>
	/// <para>
	/// This is a one-shot migration plus an ongoing audit. New prefabs get the components
	/// automatically from <see cref="NPC"/>'s <c>RequireComponent</c> attributes; existing ones
	/// need this.
	/// </para>
	/// </remarks>
	public static class AIPrefabWiringTool
	{
		/// <summary>Log category.</summary>
		private const string LOG = "AIPrefabWiringTool";

		/// <summary>
		/// Adds any missing ability-system components to every NPC prefab in the project and
		/// enables prediction on their NetworkObject.
		/// </summary>
		[MenuItem("FishMMO/AI/Repair NPC Prefabs For Combat", priority = 200)]
		public static void RepairNPCPrefabs()
		{
			StringBuilder report = new StringBuilder();
			int repaired = 0;
			int scanned = 0;

			foreach (string path in FindNPCPrefabPaths())
			{
				scanned++;
				GameObject root = PrefabUtility.LoadPrefabContents(path);
				try
				{
					List<string> changes = new List<string>();

					if (root.GetComponent<CooldownController>() == null)
					{
						root.AddComponent<CooldownController>();
						changes.Add("added CooldownController");
					}

					if (root.GetComponent<CharacterPredictionController>() == null)
					{
						root.AddComponent<CharacterPredictionController>();
						changes.Add("added CharacterPredictionController");
					}

					if (EnablePrediction(root))
					{
						changes.Add("enabled prediction on the NetworkObject");
					}

					if (changes.Count > 0)
					{
						PrefabUtility.SaveAsPrefabAsset(root, path);
						repaired++;
						report.AppendLine($"  {path}: {string.Join(", ", changes)}");
					}
				}
				finally
				{
					PrefabUtility.UnloadPrefabContents(root);
				}
			}

			AssetDatabase.SaveAssets();

			Debug.Log($"[{LOG}] Scanned {scanned} NPC prefab(s), repaired {repaired}.\n{report}");
		}

		/// <summary>
		/// Reports every NPC prefab that still cannot fight, and why.
		/// </summary>
		[MenuItem("FishMMO/AI/Audit NPC Prefabs", priority = 201)]
		public static void AuditNPCPrefabs()
		{
			StringBuilder report = new StringBuilder();
			int problems = 0;

			foreach (string path in FindNPCPrefabPaths())
			{
				GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (root == null)
				{
					continue;
				}

				List<string> lines = new List<string>();

				if (root.GetComponent<CooldownController>() == null)
				{
					lines.Add("no CooldownController — the ability picker bails out and the NPC never attacks");
				}
				if (root.GetComponent<CharacterPredictionController>() == null)
				{
					lines.Add("no CharacterPredictionController — queued abilities are never drained and the NPC freezes on its first cast");
				}

				NetworkObject networkObject = root.GetComponent<NetworkObject>();
				if (networkObject != null && !networkObject.EnablePrediction)
				{
					lines.Add("NetworkObject prediction is disabled — observers will never see the NPC's casts");
				}

				AIController controller = root.GetComponent<AIController>();
				NPC npc = root.GetComponent<NPC>();
				bool isPet = root.GetComponent<Pet>() != null;

				/* A merchant or a banker is an NPC with no combat wiring on purpose. Only report
				 * a prefab that is *partly* set up for combat, which is the case that indicates a
				 * mistake rather than a design choice. Pets are always combat-capable: theirs
				 * comes from the summoning template rather than the prefab. */
				bool hasCombatState = controller != null &&
					(controller.Archetype != null || controller.AttackingState != null);
				bool hasAbilities = npc != null && npc.Abilities != null && npc.Abilities.Count > 0;
				bool intendedForCombat = hasCombatState || hasAbilities || isPet;

				if (controller != null && intendedForCombat)
				{
					if (!hasCombatState)
					{
						lines.Add("no Archetype and no AttackingState — the NPC can never enter combat");
					}

					if (controller.Archetype == null && controller.IdleState == null)
					{
						lines.Add("no Archetype and no IdleState — TransitionToIdleState is a no-op");
					}

					if (!hasAbilities && !isPet)
					{
						lines.Add("no abilities assigned — it will chase its target and never strike");
					}
				}

				if (lines.Count > 0)
				{
					problems++;
					report.AppendLine(path + ":");
					for (int i = 0; i < lines.Count; i++)
					{
						report.AppendLine("    " + lines[i]);
					}
				}
			}

			if (problems == 0)
			{
				Debug.Log($"[{LOG}] All NPC prefabs are wired for combat.");
				return;
			}

			Debug.LogWarning($"[{LOG}] {problems} NPC prefab(s) cannot fight as configured:\n{report}");
		}

		/// <summary>
		/// Validates every <see cref="AIArchetypeTemplate"/> in the project.
		/// </summary>
		[MenuItem("FishMMO/AI/Validate Archetypes", priority = 202)]
		public static void ValidateArchetypes()
		{
			List<string> problems = new List<string>();
			StringBuilder report = new StringBuilder();
			int broken = 0;
			int total = 0;

			foreach (string guid in AssetDatabase.FindAssets("t:AIArchetypeTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				AIArchetypeTemplate archetype = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(path);
				if (archetype == null)
				{
					continue;
				}

				total++;
				if (archetype.Validate(problems))
				{
					continue;
				}

				broken++;
				report.AppendLine(archetype.name + ":");
				for (int i = 0; i < problems.Count; i++)
				{
					report.AppendLine("    " + problems[i]);
				}
			}

			if (broken == 0)
			{
				Debug.Log($"[{LOG}] All {total} archetype(s) are valid.");
				return;
			}

			Debug.LogWarning($"[{LOG}] {broken} of {total} archetype(s) have problems:\n{report}");
		}

		/// <summary>
		/// Runs every AI maintenance step in order. Used by the batch-mode entry point.
		/// </summary>
		public static void RepairAndAudit()
		{
			RepairNPCPrefabs();
			AuditNPCPrefabs();
			ValidateArchetypes();
		}

		/// <summary>
		/// Enables prediction on a prefab root's NetworkObject.
		/// </summary>
		/// <remarks>
		/// The flag is a private serialized field with only a read-only public accessor, so it has
		/// to be written through <see cref="SerializedObject"/>.
		/// </remarks>
		/// <param name="root">The prefab root.</param>
		/// <returns>True if the flag was changed.</returns>
		private static bool EnablePrediction(GameObject root)
		{
			NetworkObject networkObject = root.GetComponent<NetworkObject>();
			if (networkObject == null || networkObject.EnablePrediction)
			{
				return false;
			}

			SerializedObject serialized = new SerializedObject(networkObject);
			SerializedProperty property = serialized.FindProperty("_enablePrediction");
			if (property == null)
			{
				return false;
			}

			property.boolValue = true;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			return true;
		}

		/// <summary>
		/// Returns the asset path of every prefab in the project whose root carries an
		/// <see cref="NPC"/> component.
		/// </summary>
		/// <returns>Prefab asset paths.</returns>
		private static IEnumerable<string> FindNPCPrefabPaths()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (root != null && root.GetComponent<NPC>() != null)
				{
					yield return path;
				}
			}
		}
	}
}
