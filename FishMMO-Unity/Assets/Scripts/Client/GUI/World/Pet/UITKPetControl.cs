using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Transporting;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit pet control panel. Replaces the legacy UGUI <see cref="UIPetControl"/>: shows the
	/// pet's name and health, and issues follow/stay/summon/release commands. All pet command
	/// broadcasts are preserved verbatim.
	/// </summary>
	public class UITKPetControl : UITKCharacterControl
	{
		/// <summary>Name of the pet name label element.</summary>
		private const string NAME_LABEL_NAME = "pet-name";

		/// <summary>Name of the pet health fill element.</summary>
		private const string HEALTH_FILL_NAME = "pet-health-fill";

		/// <summary>Name of the follow command button.</summary>
		private const string FOLLOW_BUTTON_NAME = "pet-follow";

		/// <summary>Name of the stay command button.</summary>
		private const string STAY_BUTTON_NAME = "pet-stay";

		/// <summary>Name of the summon command button.</summary>
		private const string SUMMON_BUTTON_NAME = "pet-summon";

		/// <summary>Name of the release command button.</summary>
		private const string RELEASE_BUTTON_NAME = "pet-release";

		/// <summary>Cached reference to the pet name label element.</summary>
		private Label nameLabel;
		/// <summary>Cached reference to the pet health fill element.</summary>
		private VisualElement healthFill;

		/// <summary>
		/// Queries elements and wires up the command buttons.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			nameLabel = root.Q<Label>(NAME_LABEL_NAME);
			healthFill = root.Q(HEALTH_FILL_NAME);

			Button follow = root.Q<Button>(FOLLOW_BUTTON_NAME);
			if (follow != null)
			{
				follow.clicked += OnFollowPet;
			}

			Button stay = root.Q<Button>(STAY_BUTTON_NAME);
			if (stay != null)
			{
				stay.clicked += OnStayPet;
			}

			Button summon = root.Q<Button>(SUMMON_BUTTON_NAME);
			if (summon != null)
			{
				summon.clicked += OnSummonPet;
			}

			Button release = root.Q<Button>(RELEASE_BUTTON_NAME);
			if (release != null)
			{
				release.clicked += OnReleasePet;
			}
		}

		/// <summary>
		/// Subscribes to pet lifecycle events and refreshes the panel for an existing pet.
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
					if (nameLabel != null)
					{
						nameLabel.text = petController.Pet.GameObject.name;

						if (petController.Pet.CharacterNameLabel != null)
						{
							petController.Pet.CharacterGuildLabel.gameObject.SetActive(true);
						}
						if (petController.Pet.CharacterGuildLabel != null)
						{
							petController.Pet.CharacterGuildLabel.gameObject.SetActive(true);
						}
					}
					if (petController.Pet.TryGet(out ICharacterAttributeController attributeController) &&
						attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
					{
						SetHealthFill(health.CurrentValue > 0 ? health.FinalValue / health.CurrentValue : 0.0f);
					}
				}
			}
		}

		/// <summary>
		/// Unsubscribes from pet lifecycle events and resets the panel.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out IPetController petController))
			{
				IPetController.OnPetSummoned -= PetController_OnPetSummoned;
				IPetController.OnPetDestroyed += PetController_OnPetDestroyed;

				if (nameLabel != null)
				{
					nameLabel.text = "Pet";
				}
				SetHealthFill(0.0f);
			}
		}

		/// <summary>
		/// Updates the panel when a pet is summoned and makes it visible.
		/// </summary>
		/// <param name="pet">The summoned pet.</param>
		public void PetController_OnPetSummoned(Pet pet)
		{
			if (pet == null)
			{
				Hide();
				return;
			}

			if (nameLabel != null)
			{
				nameLabel.text = pet.GameObject.name;

				if (pet.CharacterNameLabel != null)
				{
					pet.CharacterNameLabel.gameObject.SetActive(true);
				}
				if (pet.CharacterGuildLabel != null)
				{
					pet.CharacterGuildLabel.gameObject.SetActive(true);
				}
			}
			if (pet.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				SetHealthFill(health.CurrentValue > 0 ? health.FinalValue / health.CurrentValue : 0.0f);
			}

			Show();
		}

		/// <summary>
		/// Hides the panel when the pet is destroyed.
		/// </summary>
		public void PetController_OnPetDestroyed()
		{
			Hide();
		}

		/// <summary>
		/// Sets the pet health fill width as a 0-1 fraction.
		/// </summary>
		/// <param name="fraction">The health fraction.</param>
		private void SetHealthFill(float fraction)
		{
			if (healthFill != null)
			{
				healthFill.style.width = Length.Percent(UnityEngine.Mathf.Clamp01(fraction) * 100.0f);
			}
		}

		/// <summary>
		/// Returns whether the character currently has a pet.
		/// </summary>
		/// <returns>True if a pet exists.</returns>
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
