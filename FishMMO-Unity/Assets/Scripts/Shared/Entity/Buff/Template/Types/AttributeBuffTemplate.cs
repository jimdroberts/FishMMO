using System.Collections.Generic;
using Cysharp.Text;
using UnityEngine;

namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "New Attribute Buff Template", menuName = "Character/Buff/Attribute Buff", order = 1)]
    public class AttributeBuffTemplate : BaseBuffTemplate
    {
        public List<BuffAttributeTemplate> BonusAttributes;

        public override void SecondaryTooltip(Utf16ValueStringBuilder stringBuilder)
        {
            if (BonusAttributes != null &&
                    BonusAttributes.Count > 0)
            {
                stringBuilder.AppendLine();
                stringBuilder.Append(RichText.Format("Bonus Attributes", true, "f5ad6e", "140%"));

                foreach (BuffAttributeTemplate buffAttribute in BonusAttributes)
                {
                    stringBuilder.Append(RichText.Format(buffAttribute.Template.Name, buffAttribute.Value, true, "FFFFFFFF", "", "s"));
                }
            }
        }

        public override void OnApply(Buff buff, ICharacter target)
        {
            if (buff == null)
            {
                return;
            }
            if (target == null)
            {
                return;
            }
            if (!target.TryGet(out ICharacterAttributeController attributeController))
            {
                return;
            }
            foreach (BuffAttributeTemplate buffAttribute in BonusAttributes)
            {
                if (buffAttribute == null)
                {
                    continue;
                }
                if (buffAttribute.Template == null)
                {
                    continue;
                }
                if (attributeController.TryGetAttribute(buffAttribute.Template.ID, out CharacterAttribute characterAttribute))
                {
                    characterAttribute.AddValue(buffAttribute.Value);
                }
                else if (attributeController.TryGetResourceAttribute(buffAttribute.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
                {
                    characterResourceAttribute.AddValue(buffAttribute.Value);
                }
            }
        }

        public override void OnRemove(Buff buff, ICharacter target)
        {
            if (buff == null)
            {
                return;
            }
            if (target == null)
            {
                return;
            }
            if (!target.TryGet(out ICharacterAttributeController attributeController))
            {
                return;
            }
            foreach (BuffAttributeTemplate buffAttribute in BonusAttributes)
            {
                if (buffAttribute == null)
                {
                    continue;
                }
                if (buffAttribute.Template == null)
                {
                    continue;
                }
                if (attributeController.TryGetAttribute(buffAttribute.Template.ID, out CharacterAttribute characterAttribute))
                {
                    characterAttribute.AddValue(-buffAttribute.Value);
                }
                else if (attributeController.TryGetResourceAttribute(buffAttribute.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
                {
                    characterResourceAttribute.AddValue(-buffAttribute.Value);
                }
            }
        }

        public override void OnApplyStack(Buff buff, ICharacter target)
        {

        }

        public override void OnRemoveStack(Buff buff, ICharacter target)
        {

        }

        public override void OnTick(Buff buff, ICharacter target)
        {
        }
    }
}