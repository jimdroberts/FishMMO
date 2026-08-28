using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Composite buff template that combines attribute modifiers, state flags, and resource ticks
	/// into a single buff. Follows the Composite pattern to avoid requiring multiple separate buffs
	/// for complex effects (e.g., a frost spell that slows movement speed AND freezes AND deals DoT).
	/// </summary>
	[CreateAssetMenu(fileName = "New Composite Buff Template", menuName = "FishMMO/Character/Buff/Composite Buff", order = 5)]
	public class CompositeBuffTemplate : BaseBuffTemplate
	{
		[Header("Attribute Modifiers (Applied on Apply/Remove)")]
		/// <summary>
		/// Attribute modifiers applied when the buff is active. Removed symmetrically on removal.
		/// </summary>
		[Tooltip("Attribute modifiers applied while the buff is active.")]
		public List<BuffAttributeTemplate> BonusAttributes;

		[Header("State Flags")]
		/// <summary>
		/// Character flags to enable while the buff is active (e.g., IsFrozen, IsStunned).
		/// </summary>
		[Tooltip("Character flags to enable while the buff is active.")]
		public List<CharacterFlags> Flags;

		[Header("Resource Tick Effects")]
		/// <summary>
		/// Resource modifications applied on each tick. Positive = heal, negative = damage.
		/// Scales with stacks: amount * (1 + Stacks).
		/// </summary>
		[Tooltip("Resource modifications per tick. Positive = heal, negative = damage. Scales with stacks.")]
		public List<BuffAttributeTemplate> TickAttributes;

		/// <summary>
		/// Damage type used to resolve resistance when a tick reduces health.
		/// </summary>
		/// <remarks>
		/// Leave empty for true damage — a null damage type bypasses resistance entirely, which is
		/// the right default for environmental and pure-magic effects. Set it to make a poison
		/// respect nature resistance, a burn respect fire resistance, and so on.
		/// <para>
		/// Ignored by ticks that heal or that target a resource other than health.
		/// </para>
		/// </remarks>
		[Tooltip("Damage type used for resistance when a tick reduces health. Leave empty for true damage.")]
		public DamageAttributeTemplate DamageAttribute;

		/// <summary>
		/// Appends a secondary tooltip describing all combined effects.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public override void SecondaryTooltip(TooltipBuilder builder)
		{
			int order = 20;

			if (BonusAttributes != null && BonusAttributes.Count > 0)
			{
				builder.AddLine("Attribute Bonuses", order++, TooltipColors.Title, false, "140%");
				for (int i = 0; i < BonusAttributes.Count; i++)
				{
					BuffAttributeTemplate attr = BonusAttributes[i];
					if (attr?.Template == null) continue;
					builder.AddLine($"{attr.Template.Name}: {attr.Value}", order++, TooltipColors.Stat);
				}
			}

			if (Flags != null && Flags.Count > 0)
			{
				builder.AddLine("State Effects", order++, TooltipColors.Title, false, "140%");
				for (int i = 0; i < Flags.Count; i++)
				{
					builder.AddLine($"{Flags[i]}", order++, TooltipColors.Stat);
				}
			}

			if (TickAttributes != null && TickAttributes.Count > 0)
			{
				builder.AddLine("Per Tick", order++, TooltipColors.Title, false, "140%");
				for (int i = 0; i < TickAttributes.Count; i++)
				{
					BuffAttributeTemplate tick = TickAttributes[i];
					if (tick?.Template == null) continue;
					string label = tick.Value >= 0 ? "+" : "";
					builder.AddLine($"{tick.Template.Name}: {label}{tick.Value}/tick", order++, TooltipColors.Stat);
				}
			}
		}

		/// <summary>
		/// Applies attribute modifiers and enables state flags.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public override void OnApply(Buff buff, ICharacter target)
		{
			if (target == null) return;

			ApplyAttributeModifiers(target, 1);
			EnableStateFlags(target);
		}

		/// <summary>
		/// Removes attribute modifiers and disables state flags.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public override void OnRemove(Buff buff, ICharacter target)
		{
			if (target == null) return;

			ApplyAttributeModifiers(target, -1);
			DisableStateFlags(target);
		}

		/// <summary>
		/// A stack applies only the attribute half of the composite, never the state flags.
		/// </summary>
		/// <remarks>
		/// Without these two overrides the base class routes stacking to <see cref="OnApply"/> and
		/// <see cref="OnRemove"/>, so removing ONE stack of a multi-stack composite ran
		/// <c>DisableStateFlags</c> and cleared the crowd-control flag while the buff was still
		/// active with stacks remaining. <see cref="StateBuffTemplate"/> carries the same pair for
		/// the same reason. Attribute modifiers do stack, and symmetrically, so they stay.
		/// </remarks>
		/// <param name="buff">The buff instance gaining a stack.</param>
		/// <param name="target">The character receiving the buff.</param>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
			if (target == null) return;

			// Flags are already enabled by the base apply; a stack only adds its attributes.
			ApplyAttributeModifiers(target, 1);
		}

		/// <summary>
		/// Removes one stack's attribute modifiers, leaving the state flags to <see cref="OnRemove"/>.
		/// </summary>
		/// <param name="buff">The buff instance losing a stack.</param>
		/// <param name="target">The character losing the stack.</param>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
			if (target == null) return;

			ApplyAttributeModifiers(target, -1);
		}

		/// <summary>
		/// Applies resource tick effects scaled by (1 + Stacks).
		/// </summary>
		/// <param name="buff">The buff instance.</param>
		/// <param name="target">The character affected.</param>
		public override void OnTick(Buff buff, ICharacter target)
		{
			base.OnTick(buff, target);
			ApplyResourceTick(buff, target, TickAttributes, DamageAttribute);
		}

		/// <summary>
		/// Applies or removes attribute modifiers based on the sign multiplier.
		/// </summary>
		/// <param name="target">The character to modify.</param>
		/// <param name="sign">+1 to apply, -1 to remove.</param>
		private void ApplyAttributeModifiers(ICharacter target, int sign)
		{
			if (BonusAttributes == null || BonusAttributes.Count < 1) return;
			if (!target.TryGet(out ICharacterAttributeController attributeController)) return;

			for (int i = 0; i < BonusAttributes.Count; i++)
			{
				BuffAttributeTemplate attr = BonusAttributes[i];
				if (attr?.Template == null) continue;

				int modifier = attr.Value * sign;
				if (attributeController.TryGetAttribute(attr.Template.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddModifier(modifier);
				}
				else if (attributeController.TryGetResourceAttribute(attr.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddModifier(modifier);
				}
			}
		}

		/// <summary>
		/// Enables all configured state flags on the target.
		/// </summary>
		/// <param name="target">The character to flag.</param>
		private void EnableStateFlags(ICharacter target)
		{
			if (Flags == null) return;
			for (int i = 0; i < Flags.Count; i++)
			{
				target.EnableFlags(Flags[i]);
			}
		}

		/// <summary>
		/// Disables all configured state flags on the target.
		/// </summary>
		/// <param name="target">The character to unflag.</param>
		private void DisableStateFlags(ICharacter target)
		{
			if (Flags == null) return;
			for (int i = 0; i < Flags.Count; i++)
			{
				CharacterFlags flag = Flags[i];
				if (!IsFlagProvidedByAnotherBuff(target, flag))
				{
					target.DisableFlags(flag);
				}
			}
		}

		/// <summary>
		/// Checks whether this composite buff template applies the specified character flag.
		/// </summary>
		/// <param name="flag">The character flag to check.</param>
		/// <returns>True if this template's <see cref="Flags"/> list contains the flag.</returns>
		internal bool AppliesFlag(CharacterFlags flag)
		{
			if (Flags == null) return false;
			for (int i = 0; i < Flags.Count; i++)
			{
				if (Flags[i] == flag)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Checks whether the specified <see cref="CharacterFlags"/> is currently provided by another
		/// active buff on the target. Used before disabling a flag to avoid clearing a flag that
		/// another buff still depends on.
		/// </summary>
		/// <param name="target">The character to check.</param>
		/// <param name="flag">The character flag to look for.</param>
		/// <returns>True if another active buff on the target provides the same flag.</returns>
		private bool IsFlagProvidedByAnotherBuff(ICharacter target, CharacterFlags flag)
		{
			if (!target.TryGet(out IBuffController buffController)) return false;

			foreach (Buff activeBuff in buffController.Buffs.Values)
			{
				if (activeBuff == null || ReferenceEquals(activeBuff.Template, this)) continue;

				if (activeBuff.Template is StateBuffTemplate stateTemplate && stateTemplate.Flag == flag)
				{
					return true;
				}

				if (activeBuff.Template is CompositeBuffTemplate compositeTemplate && compositeTemplate.AppliesFlag(flag))
				{
					return true;
				}
			}

			return false;
		}
	}
}