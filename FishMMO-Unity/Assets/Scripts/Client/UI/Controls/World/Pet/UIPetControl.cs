using UnityEngine.UI;
using TMPro;
using FishMMO.Shared;
using FishNet.Transporting;

namespace FishMMO.Client
{
	public class UIPetControl : UICharacterControl
	{
		public TMP_Text PetNameLabel;
		public Slider PetHealth;

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
					if (petController.Pet.TryGet(out ICharacterAttributeController attributeController) &&
						PetHealth != null)
					{
						if (attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
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
			if (pet.TryGet(out ICharacterAttributeController attributeController) &&
				PetHealth != null)
			{
				if (attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
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

		private bool HasPet()
		{
			return Character != null &&
				  Character.TryGet(out IPetController petController) &&
				  petController.Pet != null;
		}

		public void OnFollowPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetFollowBroadcast(), Channel.Reliable);
		}

		public void OnStayPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetStayBroadcast(), Channel.Reliable);
		}

		public void OnSummonPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetSummonBroadcast(), Channel.Reliable);
		}

		public void OnReleasePet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetReleaseBroadcast(), Channel.Reliable);
		}
	}
}