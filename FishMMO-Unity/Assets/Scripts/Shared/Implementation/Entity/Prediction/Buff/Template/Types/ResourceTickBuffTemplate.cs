using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Buff template that applies periodic resource modifications (heal-over-time / damage-over-time).
	/// Each tick adds or subtracts from the current value of target resource attributes.
	/// The tick amount scales linearly with the number of stacks (base + stacks).
	/// This buff is deterministic — identical inputs produce identical state changes.
	/// </summary>
	[CreateAssetMenu(fileName = "New Resource Tick Buff Template", menuName = "FishMMO/Character/Buff/Resource Tick Buff", order = 3)]
	public class ResourceTickBuffTemplate : BaseBuffTemplate
	{
		/// <summary>
		/// List of resource attribute modifications applied per tick.
		/// Positive values heal, negative values deal damage.
		/// </summary>
		[Tooltip("Resource attributes to modify per tick. Positive = heal, negative = damage.")]
		public List<BuffAttributeTemplate> TickAttributes;

		/// <summary>
		/// Appends a secondary tooltip describing the per-tick resource effects.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public override void SecondaryTooltip(TooltipBuilder builder)
		{
			if (TickAttributes == null || TickAttributes.Count < 1) return;

			builder.AddLine("Per Tick", 20, TooltipColors.Title, false, "140%");
			for (int i = 0; i < TickAttributes.Count; i++)
			{
				BuffAttributeTemplate tickAttribute = TickAttributes[i];
				if (tickAttribute?.Template == null) continue;

				string label = tickAttribute.Value >= 0 ? "+" : "";
				builder.AddLine($"{tickAttribute.Template.Name}: {label}{tickAttribute.Value}/tick", 21 + i, TooltipColors.Stat);
			}
		}

		/// <summary>
		/// No immediate effect on apply — all effects are tick-based.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public override void OnApply(Buff buff, ICharacter target)
		{
			// No immediate effect; resources are modified via OnTick.
		}

		/// <summary>
		/// No cleanup needed on remove — tick effects modify current values, not modifiers.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public override void OnRemove(Buff buff, ICharacter target)
		{
			// Tick effects modify current resource values directly; no undo needed.
		}

		/// <summary>
		/// No immediate effect on stack add — stacking increases the tick multiplier.
		/// </summary>
		/// <param name="buff">The buff instance being stacked.</param>
		/// <param name="target">The character receiving the stack.</param>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
			// Stacking only affects the multiplier used in OnTick.
		}

		/// <summary>
		/// No cleanup on stack remove — tick multiplier decreases naturally.
		/// </summary>
		/// <param name="buff">The buff instance being unstacked.</param>
		/// <param name="target">The character losing the stack.</param>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
			// Stacking only affects the multiplier used in OnTick.
		}

		/// <summary>
		/// Called each tick. Applies resource modifications scaled by (1 + Stacks) to the target.
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

				float amount = tickAttribute.Value * multiplier;

				if (attributeController.TryGetResourceAttribute(tickAttribute.Template.ID, out CharacterResourceAttribute resourceAttribute))
				{
					resourceAttribute.AddToCurrentValue(amount);
				}
			}
		}
	}
}