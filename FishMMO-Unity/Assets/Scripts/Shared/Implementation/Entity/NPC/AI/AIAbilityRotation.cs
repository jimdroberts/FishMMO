using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Determines how the rotation evaluates its entries.
	/// </summary>
	public enum AIRotationMode
	{
		/// <summary>
		/// Evaluates entries top-to-bottom. The first entry whose conditions
		/// are all met and whose ability is usable is selected.
		/// </summary>
		Priority,

		/// <summary>
		/// Advances through entries sequentially after each successful activation.
		/// If the next-in-sequence entry cannot be used, scans remaining entries
		/// in order before falling back.
		/// </summary>
		Sequence
	}

	/// <summary>
	/// A single entry in an AI ability rotation. Pairs an ability template with
	/// a set of conditions that must all be satisfied for the ability to be selected.
	/// </summary>
	[Serializable]
	public class AIAbilityRotationEntry
	{
		/// <summary>
		/// The ability template ID to activate when this entry is selected.
		/// Must correspond to a template the NPC has learned.
		/// </summary>
		[Tooltip("Ability template ID to use when this entry is selected.")]
		public int AbilityTemplateID;

		/// <summary>
		/// All conditions that must evaluate to true for this entry to be selected (AND logic).
		/// Leave empty to make this entry unconditional (always matches if the ability is usable).
		/// </summary>
		[Tooltip("All conditions must be true (AND). Empty = unconditional.")]
		public List<AIAbilityCondition> Conditions = new List<AIAbilityCondition>();
	}

	/// <summary>
	/// ScriptableObject that defines an ordered list of ability entries with conditions.
	/// Attach to an <see cref="AIController"/> to give NPCs intelligent, designer-driven
	/// ability selection instead of (or in addition to) the default scoring-based picker.
	/// <para>
	/// In <see cref="AIRotationMode.Priority"/> mode, entries are evaluated top-to-bottom
	/// and the first match wins — ideal for conditional behaviour such as
	/// "use Heal when health ≤ 40%, else use Fireball".
	/// </para>
	/// <para>
	/// In <see cref="AIRotationMode.Sequence"/> mode, the NPC advances through the list
	/// in order, trying the next entry each evaluation — ideal for structured rotations
	/// such as "Fireball → Frost Bolt → Pyroblast → repeat".
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Ability Rotation", menuName = "FishMMO/Character/NPC/AI/Ability Rotation")]
	public class AIAbilityRotation : ScriptableObject
	{
		/// <summary>
		/// How entries are evaluated.
		/// </summary>
		[Tooltip("Priority = first match wins. Sequence = cycle through list in order.")]
		public AIRotationMode Mode = AIRotationMode.Priority;

		/// <summary>
		/// Ordered list of ability entries. In Priority mode, higher entries have higher
		/// priority. In Sequence mode, entries are cycled through in order.
		/// </summary>
		[Tooltip("Ordered ability entries. Priority: first match wins. Sequence: cycled in order.")]
		public List<AIAbilityRotationEntry> Entries = new List<AIAbilityRotationEntry>();

		/// <summary>
		/// When true, if no rotation entry matches, the caller should fall back to the
		/// default scoring-based ability picker. When false, returns null (NPC waits/repositions).
		/// </summary>
		[Tooltip("If no entry matches, fall back to the default ability picker.")]
		public bool FallbackToDefault = true;

		/// <summary>
		/// Evaluates the rotation and returns the best ability to use, or null if no entry matches.
		/// </summary>
		/// <param name="controller">The NPC's AI controller.</param>
		/// <param name="abilityController">The NPC's ability controller.</param>
		/// <param name="cooldownController">The NPC's cooldown controller.</param>
		/// <param name="self">The NPC's character.</param>
		/// <param name="target">The NPC's current target character (may be null).</param>
		/// <returns>The ability to activate, or null if no entry matches.</returns>
		public Ability Evaluate(
			AIController controller,
			IAbilityController abilityController,
			ICooldownController cooldownController,
			ICharacter self,
			ICharacter target)
		{
			if (Entries == null || Entries.Count == 0)
				return null;

			switch (Mode)
			{
				case AIRotationMode.Priority:
					return EvaluatePriority(controller, abilityController, cooldownController, self, target);
				case AIRotationMode.Sequence:
					return EvaluateSequence(controller, abilityController, cooldownController, self, target);
				default:
					return null;
			}
		}

		/// <summary>
		/// Priority mode: evaluates entries top-to-bottom, returns the first match.
		/// </summary>
		private Ability EvaluatePriority(
			AIController controller,
			IAbilityController abilityController,
			ICooldownController cooldownController,
			ICharacter self,
			ICharacter target)
		{
			for (int i = 0; i < Entries.Count; i++)
			{
				Ability ability = TryEntry(Entries[i], controller, abilityController, cooldownController, self, target);
				if (ability != null)
					return ability;
			}
			return null;
		}

		/// <summary>
		/// Sequence mode: starting from the NPC's current <see cref="AIController.RotationIndex"/>,
		/// tries each entry in order (wrapping around). Advances the index on success.
		/// </summary>
		private Ability EvaluateSequence(
			AIController controller,
			IAbilityController abilityController,
			ICooldownController cooldownController,
			ICharacter self,
			ICharacter target)
		{
			int count = Entries.Count;
			int startIndex = controller.RotationIndex % count;

			for (int offset = 0; offset < count; offset++)
			{
				int idx = (startIndex + offset) % count;
				Ability ability = TryEntry(Entries[idx], controller, abilityController, cooldownController, self, target);
				if (ability != null)
				{
					// Advance past this entry for next evaluation.
					controller.RotationIndex = (idx + 1) % count;
					return ability;
				}
			}
			return null;
		}

		/// <summary>
		/// Attempts to match a single entry: checks all conditions, verifies the ability
		/// exists in the NPC's known abilities and is usable (off cooldown, meets activation conditions).
		/// </summary>
		private static Ability TryEntry(
			AIAbilityRotationEntry entry,
			AIController controller,
			IAbilityController abilityController,
			ICooldownController cooldownController,
			ICharacter self,
			ICharacter target)
		{
			if (entry == null)
				return null;

			// Check all conditions (AND logic).
			if (entry.Conditions != null)
			{
				for (int i = 0; i < entry.Conditions.Count; i++)
				{
					AIAbilityCondition condition = entry.Conditions[i];
					if (condition == null)
						continue;

					if (!condition.Evaluate(controller, self, target))
						return null;
				}
			}

			// Find the matching ability instance by template ID.
			Ability ability = AIUtility.FindAbilityByTemplate(abilityController, entry.AbilityTemplateID);
			if (ability == null)
				return null;

			// Verify usability: not on cooldown and meets activation conditions.
			if (cooldownController.IsOnCooldown(ability.ID))
				return null;

			if (!ability.MeetsActivationConditions(self))
				return null;

			return ability;
		}

	}
}