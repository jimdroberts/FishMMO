using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(CharacterAttributeController))]
public class CharacterRegenerationController : NetworkBehaviour
{
	public static Character localCharacter;
	public CharacterAttributeController AttributeController;

	public float nextRegenTick = 0.0f;
	public float regenerateTickRate = 1.0f;

	void Awake()
	{
		AttributeController = gameObject.GetComponent<CharacterAttributeController>();
	}

	void Update()
	{
		OnRegenerate();
	}

	private void OnRegenerate()
	{
		nextRegenTick -= Time.deltaTime;
		if (nextRegenTick <= regenerateTickRate)
		{
			nextRegenTick = regenerateTickRate;

			if (AttributeController.TryGetResourceAttribute("Health", out CharacterResourceAttribute health))
			{
				if (AttributeController.TryGetAttribute("Health Regeneration", out CharacterAttribute healthRegeneration))
				{
					health.Gain(healthRegeneration.FinalValue);
				}
			}
			if (AttributeController.TryGetResourceAttribute("Mana", out CharacterResourceAttribute mana))
			{
				if (AttributeController.TryGetAttribute("Mana Regeneration", out CharacterAttribute manaRegeneration))
				{
					mana.Gain(manaRegeneration.FinalValue);
				}
			}

			//stamina regeneration is handled by the run function in the character controller
		}
	}
}