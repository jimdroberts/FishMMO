using FishNet.Object;
using UnityEngine;
using FishNet.Component.Transforming;
using FishNet.Observing;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(AIController))]
	[RequireComponent(typeof(BuffController))]
	[RequireComponent(typeof(CharacterAttributeController))]
	[RequireComponent(typeof(CharacterDamageController))]
	[RequireComponent(typeof(FactionController))]
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(NetworkTransform))]
	[RequireComponent(typeof(NetworkObserver))]
	public class NPC : BaseCharacter
	{
		public bool IsCharmable;
		public NPCAttributeDatabase AttributeBonuses;

		public override void OnAwake()
		{
			base.OnAwake();

			AddNPCAttributes();

#if !UNITY_SERVER
			GameObject.name = GameObject.name.Replace("(Clone)", "");
			if (CharacterNameLabel != null)
			{
				CharacterNameLabel.text = GameObject.name;
			}
#endif
		}

		private void AddNPCAttributes()
		{
			if (AttributeBonuses != null &&
				AttributeBonuses.Attributes != null &&
				this.TryGet(out ICharacterAttributeController attributeController))
			{
				foreach (NPCAttribute attribute in AttributeBonuses.Attributes)
				{
					int value;
					if (attribute.IsRandom)
					{
						value = Random.Range(attribute.Min, attribute.Max);
					}
					else
					{
						value = attribute.Max;
					}

					if (attributeController.TryGetAttribute(attribute.Template, out CharacterAttribute characterAttribute))
					{
						if (attribute.IsScalar)
						{
							characterAttribute.AddValue(characterAttribute.FinalValue.PercentOf(value));
						}
						else
						{
							characterAttribute.AddValue(value);
						}
					}
					else if (attributeController.TryGetResourceAttribute(attribute.Template, out CharacterResourceAttribute characterResourceAttribute))
					{
						if (attribute.IsScalar)
						{
							int additionalValue = characterResourceAttribute.FinalValue.PercentOf(value);

							characterResourceAttribute.AddValue(additionalValue);
							characterResourceAttribute.AddToCurrentValue(additionalValue);
						}
						else
						{
							characterResourceAttribute.AddValue(value);
							characterResourceAttribute.AddToCurrentValue(value);
						}
					}
				}
			}
		}
	}
}