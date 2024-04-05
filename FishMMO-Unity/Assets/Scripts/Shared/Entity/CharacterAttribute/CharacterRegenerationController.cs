using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(CharacterAttributeController))]
	public class CharacterRegenerationController : NetworkBehaviour
	{
		public ICharacterAttributeController AttributeController;

		public CharacterAttributeTemplate HealthTemplate;
		public CharacterAttributeTemplate ManaTemplate;
		public CharacterAttributeTemplate HealthRegenerationTemplate;
		public CharacterAttributeTemplate ManaRegenerationTemplate;

		public float nextRegenTick = 0.0f;
		public float regenerateTickRate = 1.0f;

		void Awake()
		{
			AttributeController = gameObject.GetComponent<ICharacterAttributeController>();
		}

		void Update()
		{
			OnRegenerate();
		}

		private void OnRegenerate()
		{
			if (nextRegenTick < regenerateTickRate)
			{
				nextRegenTick = regenerateTickRate;

				if (AttributeController.TryGetResourceAttribute(HealthTemplate, out CharacterResourceAttribute health))
				{
					if (AttributeController.TryGetAttribute(HealthRegenerationTemplate, out CharacterAttribute healthRegeneration))
					{
						health.Gain(healthRegeneration.FinalValue);
					}
				}
				if (AttributeController.TryGetResourceAttribute(ManaTemplate, out CharacterResourceAttribute mana))
				{
					if (AttributeController.TryGetAttribute(ManaRegenerationTemplate, out CharacterAttribute manaRegeneration))
					{
						mana.Gain(manaRegeneration.FinalValue);
					}
				}

				//stamina regeneration is handled by the run function in the character controller
			}
			nextRegenTick -= Time.deltaTime;
		}
	}
}