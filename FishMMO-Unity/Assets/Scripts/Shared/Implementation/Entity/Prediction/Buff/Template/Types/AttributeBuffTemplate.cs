using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Buff template that grants bonus attributes to a character while active.
	/// Applies additive modifiers on apply/stack and removes them symmetrically on remove/unstack.
	/// </summary>
	[CreateAssetMenu(fileName = "New Attribute Buff Template", menuName = "FishMMO/Character/Buff/Attribute Buff", order = 1)]
	public class AttributeBuffTemplate : BaseBuffTemplate
	{
		/// <summary>
		/// List of bonus attributes applied by this buff.
		/// </summary>
		public List<BuffAttributeTemplate> BonusAttributes;

		/// <summary>
		/// Appends a secondary tooltip describing the bonus attributes granted by this buff.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public override void SecondaryTooltip(TooltipBuilder builder)
		{
			if (BonusAttributes == null || BonusAttributes.Count < 1) return;

			builder.AddLine("Bonus Attributes", 20, TooltipColors.Title, false, "140%");
			for (int i = 0; i < BonusAttributes.Count; i++)
			{
				BuffAttributeTemplate buffAttribute = BonusAttributes[i];
				if (buffAttribute?.Template == null) continue;
				builder.AddLine($"{buffAttribute.Template.Name}: {buffAttribute.Value}", 21 + i, TooltipColors.Stat);
			}
		}

		/// <summary>
		/// Applies the bonus attributes to the target when the buff is applied.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public override void OnApply(Buff buff, ICharacter target)
		{
			WriteModifiers(target, buff, 1 + (buff?.Stacks ?? 0));
		}

		/// <summary>
		/// Restates the buff's contribution at the stack count this application leaves behind.
		/// </summary>
		/// <remarks>
		/// <c>Buff.AddStack</c> calls this BEFORE incrementing <see cref="Buff.Stacks"/>, so the
		/// count after the operation is <c>Stacks + 1</c> and the multiplier is <c>Stacks + 2</c>.
		/// The base class would have routed this to <see cref="OnApply"/>, which was correct only
		/// while modifiers accumulated; a ledger entry is restated rather than added to, so the hook
		/// has to know the post-operation count.
		/// </remarks>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
			WriteModifiers(target, buff, 2 + (buff?.Stacks ?? 0));
		}

		/// <summary>
		/// Restates the buff's contribution after losing one stack.
		/// </summary>
		/// <remarks>
		/// <c>Buff.RemoveStack</c> decrements after this returns, so the post-operation count is
		/// <c>Stacks - 1</c> and the multiplier is <c>Stacks</c>. At the last stack that is zero,
		/// which releases the entry outright.
		/// </remarks>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
			WriteModifiers(target, buff, buff?.Stacks ?? 0);
		}

		/// <summary>
		/// Removes the bonus attributes from the target when the buff is removed.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public override void OnRemove(Buff buff, ICharacter target)
		{
			ClearModifiers(target, buff);
		}

		/// <summary>
		/// States this buff's whole contribution at a given stack multiplier.
		/// </summary>
		/// <remarks>
		/// One ledger entry per attribute, keyed by this template — which is also the buff's key in
		/// the character's buff container, so one buff instance owns exactly one entry. Restating is
		/// idempotent, so a payload restore or a reconcile replay that re-applies the same buff
		/// cannot double its bonus.
		/// </remarks>
		/// <param name="target">The character to modify.</param>
		/// <param name="buff">The buff instance whose entry is being written.</param>
		/// <param name="multiplier">Stack multiplier the contribution should reflect.</param>
		private void WriteModifiers(ICharacter target, Buff buff, int multiplier)
		{
			if (target == null || BonusAttributes == null) return;
			if (!target.TryGet(out ICharacterAttributeController attributeController)) return;

			ModifierSource source = ModifierSource.Buff(ID);

			for (int i = 0; i < BonusAttributes.Count; i++)
			{
				BuffAttributeTemplate buffAttribute = BonusAttributes[i];
				if (buffAttribute?.Template == null) continue;

				int modifier = buffAttribute.Value * multiplier;
				if (attributeController.TryGetAttribute(buffAttribute.Template.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.SetSource(source, modifier);
				}
				else if (attributeController.TryGetResourceAttribute(buffAttribute.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.SetSource(source, modifier);
				}
			}
		}

		/// <summary>Releases this buff's entry from every attribute it touched.</summary>
		private void ClearModifiers(ICharacter target, Buff buff)
		{
			if (target == null || BonusAttributes == null) return;
			if (!target.TryGet(out ICharacterAttributeController attributeController)) return;

			ModifierSource source = ModifierSource.Buff(ID);

			for (int i = 0; i < BonusAttributes.Count; i++)
			{
				BuffAttributeTemplate buffAttribute = BonusAttributes[i];
				if (buffAttribute?.Template == null) continue;

				if (attributeController.TryGetAttribute(buffAttribute.Template.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.ClearSource(source);
				}
				else if (attributeController.TryGetResourceAttribute(buffAttribute.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.ClearSource(source);
				}
			}
		}
	}
}