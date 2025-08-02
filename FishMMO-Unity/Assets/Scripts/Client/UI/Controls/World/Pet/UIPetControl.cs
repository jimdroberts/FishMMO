using UnityEngine.UI;
using TMPro;
using FishMMO.Shared;
using FishNet.Transporting;

namespace FishMMO.Client
{
	/// <summary>
	/// UIPetControl is responsible for controlling the pet's UI elements and interactions.
	/// </summary>
	public class UIPetControl : UICharacterControl
	{
		/// <summary>
		/// The label displaying the pet's name.
		/// </summary>
		public TMP_Text PetNameLabel;
		/// <summary>
		/// The slider displaying the pet's health.
		/// </summary>
		public Slider PetHealth;

		/// <summary>
		/// Called after the character is set. Subscribes to pet controller events and updates pet UI.
		/// </summary>
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
							// Note: This appears to be reversed; typically CurrentValue / FinalValue is used for health bars.
							PetHealth.value = health.FinalValue / health.CurrentValue;
						}
					}
				}
			}
		}

		/// <summary>
		/// Called before the character is unset. Unsubscribes from pet controller events and resets pet UI.
		/// </summary>
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

		/// <summary>
		/// Handles pet summoned event. Updates pet UI and makes it visible.
		/// </summary>
		/// <param name="pet">The summoned pet.</param>
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

		/// <summary>
		/// Handles pet destroyed event. Hides the pet UI.
		/// </summary>
		public void PetController_OnPetDestroyed()
		{
			Hide();
		}

		/// <summary>
		/// Returns true if the character has a pet.
		/// </summary>
		/// <returns>True if pet exists, false otherwise.</returns>
		private bool HasPet()
		{
			return Character != null &&
				  Character.TryGet(out IPetController petController) &&
				  petController.Pet != null;
		}

		/// <summary>
		/// Sends a follow command to the pet.
		/// </summary>
		public void OnFollowPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetFollowBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Sends a stay command to the pet.
		/// </summary>
		public void OnStayPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetStayBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Sends a summon command to the pet.
		/// </summary>
		public void OnSummonPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetSummonBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Sends a release command to the pet.
		/// </summary>
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