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
	/// four things on the prefab that NPC prefabs historically did not have: a
	/// <see cref="CharacterPredictionController"/> to drive the replicate stream, a
	/// <see cref="CooldownController"/> for the ability picker to consult, a
	/// <see cref="TargetController"/> for the cast to resolve its target and spawn through, and
	/// <c>EnablePrediction</c> set on the <see cref="NetworkObject"/>. Without all four an NPC
	/// spawns, ticks its brain, decides to cast — and nothing happens.
	/// </para>
	/// <para>
	/// The TargetController was the last one found (issue #232). With the other three in place
	/// the cast genuinely ran — cooldown started, resources spent, the AI's pacing timer armed —
	/// and <c>AbilityController.ResolveTargetAndSpawn</c> then spawned nothing, because the
	/// acquisition trace it needs lives on that component. One warning per controller was the
	/// only trace of it. The combat simulation never saw it because it added the component
	/// itself.
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

					/* NPC's RequireComponent adds a missing TargetController the moment the
					 * prefab contents load, with an EMPTY mask — so on a prefab saved before the
					 * requirement existed the second branch is the one that fires. A mask of
					 * nothing traces nothing: the component is present and every cast still
					 * resolves no target, which is the same outcome as a missing component. */
					TargetController targeting = root.GetComponent<TargetController>();
					if (targeting == null)
					{
						root.AddComponent<TargetController>().LayerMask = NpcTargetLayers;
						changes.Add("added TargetController");
					}
					else if (targeting.LayerMask.value == 0)
					{
						targeting.LayerMask = NpcTargetLayers;
						changes.Add("set the TargetController LayerMask (it was empty)");
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

				TargetController targeting = root.GetComponent<TargetController>();
				if (targeting == null)
				{
					lines.Add("no TargetController — every cast runs to completion and spawns nothing; the NPC never lands a hit");
				}
				else if (targeting.LayerMask.value == 0)
				{
					lines.Add("TargetController LayerMask is empty — the acquisition trace can hit nothing");
				}

				NetworkObject networkObject = root.GetComponent<NetworkObject>();
				if (networkObject != null && !networkObject.EnablePrediction)
				{
					lines.Add("NetworkObject prediction is disabled — observers will never see the NPC's casts");
				}

				AIController controller = root.GetComponent<AIController>();
				NPC npc = root.GetComponent<NPC>();
				bool isPet = root.GetComponent<Pet>() != null;

				/* The archetype is the whole brain: every state the controller runs is read from
				 * it, so a controller without one has no states at all. */
				if (controller != null && controller.Archetype == null)
				{
					lines.Add("no Archetype — the controller has no states, no LOD profile and no personality; it spawns and never ticks");
				}

				/* A merchant or a banker is an NPC with no combat wiring on purpose. Only report
				 * a prefab that is *partly* set up for combat, which is the case that indicates a
				 * mistake rather than a design choice. Pets are always combat-capable: theirs
				 * comes from the summoning template rather than the prefab. */
				bool hasCombatState = controller != null && controller.AttackingState != null;
				bool hasAbilities = npc != null && npc.Abilities != null && npc.Abilities.Count > 0;
				bool intendedForCombat = hasCombatState || hasAbilities || isPet;

				if (controller != null && controller.Archetype != null && intendedForCombat)
				{
					if (!hasCombatState)
					{
						lines.Add($"archetype '{controller.Archetype.name}' has no AttackingState — the NPC can never enter combat");
					}

					if (controller.IdleState == null)
					{
						lines.Add($"archetype '{controller.Archetype.name}' has no IdleState — TransitionToIdleState is a no-op");
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
		/// The layers an NPC's acquisition trace considers: the same set the shipped player
		/// prefabs use, so an NPC can hit what a player can hit. Player characters and NPCs both
		/// live on the Player layer; Default and Ground let a ground-targeted ability land on
		/// terrain and let a projectile stop at a wall.
		/// </summary>
		private static LayerMask NpcTargetLayers => LayerMask.GetMask("Default", "Player", "Ground");

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
