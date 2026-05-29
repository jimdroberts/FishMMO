using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Buff template that applies cumulative attribute modifiers on each tick.
	/// Each tick adds a modifier; on remove, the total accumulated modifier is reversed.
	/// Tracks total applied ticks internally on the buff to ensure perfect symmetry.
	/// Useful for "ramping" buffs that grow stronger over time.
	/// </summary>
	[CreateAssetMenu(fileName = "New Attribute Tick Buff Template", menuName = "FishMMO/Character/Buff/Attribute Tick Buff", order = 4)]
	public class AttributeTickBuffTemplate : BaseBuffTemplate
	{
		/// <summary>
		/// List of attribute modifications applied per tick.
		/// </summary>
		[Tooltip("Attribute modifiers added on each tick. Cumulative — reversed on removal.")]
		public List<BuffAttributeTemplate> TickAttributes;

		/// <summary>
		/// Appends a secondary tooltip describing the per-tick attribute effects.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public override void SecondaryTooltip(TooltipBuilder builder)
		{
			if (TickAttributes == null || TickAttributes.Count < 1) return;

			builder.AddLine("Per Tick (Cumulative)", 20, TooltipColors.Title, false, "140%");
			for (int i = 0; i < TickAttributes.Count; i++)
			{
				BuffAttributeTemplate tickAttribute = TickAttributes[i];
				if (tickAttribute?.Template == null) continue;

				string label = tickAttribute.Value >= 0 ? "+" : "";
				builder.AddLine($"{tickAttribute.Template.Name}: {label}{tickAttribute.Value}/tick", 21 + i, TooltipColors.Stat);
			}
		}

		/// <summary>
		/// No immediate modifier on apply. All modifications happen via OnTick.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public override void OnApply(Buff buff, ICharacter target)
		{
			// No immediate modifier; cumulative modifiers applied via OnTick.
		}

		/// <summary>
		/// Removes all accumulated tick modifiers from the target.
		/// Uses TickCount tracked on the buff to ensure exact reversal.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public override void OnRemove(Buff buff, ICharacter target)
		{
			if (buff == null || target == null || TickAttributes == null) return;
			if (!target.TryGet(out ICharacterAttributeController attributeController)) return;

			// Use CumulativeTickMultiplier rather than (TickCount * (1 + currentStacks)).
			// If stacks were added or removed while the buff was active, each tick fired
			// with the Stacks value at THAT moment. CumulativeTickMultiplier is the exact
			// sum of (1 + Stacks) across every tick that fired, so reversing it produces
			// perfect symmetry regardless of stack changes between ticks.
			for (int i = 0; i < TickAttributes.Count; i++)
			{
				BuffAttributeTemplate tickAttribute = TickAttributes[i];
				if (tickAttribute?.Template == null) continue;

				int totalModifier = tickAttribute.Value * buff.CumulativeTickMultiplier;
				if (totalModifier == 0) continue;

				if (attributeController.TryGetAttribute(tickAttribute.Template.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddModifier(-totalModifier);
				}
				else if (attributeController.TryGetResourceAttribute(tickAttribute.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddModifier(-totalModifier);
				}
			}
		}

		/// <summary>
		/// Stacking does not apply immediate modifiers. The multiplier in OnTick scales with stacks.
		/// </summary>
		/// <param name="buff">The buff instance being stacked.</param>
		/// <param name="target">The character receiving the stack.</param>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
			// Stacking increases the multiplier used in OnTick.
		}

		/// <summary>
		/// Stack removal does not immediately remove modifiers. Tick multiplier decreases naturally.
		/// </summary>
		/// <param name="buff">The buff instance being unstacked.</param>
		/// <param name="target">The character losing the stack.</param>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
			// Stacking multiplier decreases in OnTick.
		}

		/// <summary>
		/// Called each tick. Adds attribute modifiers scaled by (1 + Stacks) and increments TickCount.
		/// </summary>
		/// <param name="buff">The buff instance.</param>
		/// <param name="target">The character affected.</param>
		public override void OnTick(Buff buff, ICharacter target)
		{
			if (buff == null || target == null || TickAttributes == null) return;
			if (!target.TryGet(out ICharacterAttributeController attributeController)) return;

			int multiplier = 1 + buff.Stacks;

			for (int i = 0; i < TickAttributes.Count; i++)
			{
				BuffAttributeTemplate tickAttribute = TickAttributes[i];
				if (tickAttribute?.Template == null) continue;

				int modifier = tickAttribute.Value * multiplier;
				if (modifier == 0) continue;

				if (attributeController.TryGetAttribute(tickAttribute.Template.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddModifier(modifier);
				}
				else if (attributeController.TryGetResourceAttribute(tickAttribute.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddModifier(modifier);
				}
			}

			// TickCount is incremented by Buff.TryTick after OnTick returns.
			// Do NOT increment here — doing so would double-count ticks,
			// causing OnRemove to reverse 2x the actual applied modifiers.
		}
	}
}