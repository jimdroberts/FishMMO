using UnityEngine.UI;
using TMPro;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIPetControl : UICharacterControl
	{
		public CharacterAttributeTemplate HealthTemplate;
		public TMP_Text PetNameLabel;
		public Slider PetHealth;
		public Button AttackButton;
		public Button StayButton;
		public Button FollowButton;
		public Button BanishButton;
		public Button SummonButton;
		public Button ReleaseButton;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IPetController petController))
			{
				IPetController.OnPetSummoned += PetController_OnPetSummoned;
				IPetController.OnPetDestroyed += PetController_OnPetDestroyed;

				if (petController.Pet != null)
				{
					if (PetNameLabel != null)
					{
						PetNameLabel.text = petController.Pet.GameObject.name;

#if !UNITY_SERVER
						if (petController.Pet.CharacterNameLabel != null)
						{
							petController.Pet.CharacterGuildLabel.gameObject.SetActive(true);
						}
						if (petController.Pet.CharacterGuildLabel != null)
						{
							petController.Pet.CharacterGuildLabel.gameObject.SetActive(true);
						}
#endif
					}
					if (petController.Pet.TryGet(out CharacterAttributeController attributeController) &&
						PetHealth != null)
					{
						if (attributeController.TryGetResourceAttribute(HealthTemplate, out CharacterResourceAttribute health))
						{
							PetHealth.value = health.FinalValue / health.CurrentValue;
						}
					}
				}
			}
		}

		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out IPetController petController))
			{
				IPetController.OnPetSummoned -= PetController_OnPetSummoned;
				IPetController.OnPetDestroyed += PetController_OnPetDestroyed;

				if (PetNameLabel != null)
				{
					PetNameLabel.text = "Pet";
				}
				if (PetHealth != null)
				{
					PetHealth.value = 0;
				}
			}
		}

		public void PetController_OnPetSummoned(Pet pet)
		{
			if (pet == null)
			{
				Hide();
				return;
			}

			if (PetNameLabel != null)
			{
				PetNameLabel.text = pet.GameObject.name;

#if !UNITY_SERVER
				if (pet.CharacterNameLabel != null)
				{
					pet.CharacterNameLabel.gameObject.SetActive(true);
				}
				if (pet.CharacterGuildLabel != null)
				{
					pet.CharacterGuildLabel.gameObject.SetActive(true);
				}
#endif
			}
			if (pet.TryGet(out CharacterAttributeController attributeController) &&
				PetHealth != null)
			{
				if (attributeController.TryGetResourceAttribute(HealthTemplate, out CharacterResourceAttribute health))
				{
					PetHealth.value = health.FinalValue / health.CurrentValue;
				}
			}

			Show();
		}

		public void PetController_OnPetDestroyed()
		{
			Hide();
		}
	}
}