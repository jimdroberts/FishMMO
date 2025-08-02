using FishMMO.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UITarget : UICharacterControl
	{
		/// <summary>
		/// The label displaying the target's name.
		/// </summary>
		public TMP_Text NameLabel;
		/// <summary>
		/// The slider displaying the target's health.
		/// </summary>
		public Slider HealthSlider;
		/// <summary>
		/// The health attribute template used to identify health resources.
		/// </summary>
		public CharacterAttributeTemplate HealthAttribute;

		/// <summary>
		/// Cached 3D label for displaying overhead information about the target.
		/// </summary>
		private Cached3DLabel targetLabel;

		/// <summary>
		/// Called after the character is set. Subscribes to target controller events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out ITargetController targetController))
			{
				targetController.OnChangeTarget += TargetController_OnChangeTarget;
				targetController.OnUpdateTarget += TargetController_OnUpdateTarget;
				targetController.OnClearTarget += TargetController_OnClearTarget;
			}
		}

		/// <summary>
		/// Called before the character is unset. Unsubscribes from target controller events and caches the label.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out ITargetController targetController))
			{
				targetController.OnChangeTarget -= TargetController_OnChangeTarget;
				targetController.OnUpdateTarget -= TargetController_OnUpdateTarget;
				targetController.OnClearTarget -= TargetController_OnClearTarget;

				LabelMaker.Cache(targetLabel);
				targetLabel = null;
			}
		}

		/// <summary>
		/// Handles target change event. Updates UI and overhead label for the new target.
		/// </summary>
		/// <param name="target">The new target transform.</param>
		public void TargetController_OnChangeTarget(Transform target)
		{
			if (target == null ||
				UIManager.ControlHasFocus())
			{
				TargetController_OnClearTarget();
				return;
			}

			ICharacterAttributeController characterAttributeController = target.GetComponent<ICharacterAttributeController>();
			IInteractable interactable = target.GetComponent<IInteractable>();
			ICharacter character = target.GetComponent<ICharacter>();
			SceneTeleporter teleporter = target.GetComponent<SceneTeleporter>();
			SceneObjectNamer sceneObjectNamer = target.GetComponent<SceneObjectNamer>();

			// Return if all conditions are false (target is not interactable, character, teleporter, or named object)
			if (interactable == null &&
				character == null &&
				teleporter == null &&
				characterAttributeController == null &&
				sceneObjectNamer == null)
			{
				return;
			}

			if (NameLabel != null)
			{
				Color color = Color.white;

				if (character != null &&
					Character.TryGet(out IFactionController factionController) &&
					character.TryGet(out IFactionController targetFactionController))
				{
					color = factionController.GetAllianceLevelColor(targetFactionController);
				}

				NameLabel.text = interactable != null ? interactable.Name : target.name.Replace("(Clone)", "");
				NameLabel.color = color;
			}
			if (characterAttributeController != null)
			{
				if (characterAttributeController.TryGetResourceAttribute(HealthAttribute, out CharacterResourceAttribute health))
				{
					HealthSlider.value = health.CurrentValue / health.FinalValue;

					if (NameLabel != null)
					{
						NameLabel.text += $" [{health.CurrentValue}/{health.FinalValue}]";
					}
				}
			}
			else
			{
				HealthSlider.value = 0;
			}

			// Make the UI visible
			Show();

			UpdateTargetLabel(target, character, interactable);
		}

		/// <summary>
		/// Handles target update event. Reuses change target logic.
		/// </summary>
		/// <param name="target">The target transform.</param>
		public void TargetController_OnUpdateTarget(Transform target)
		{
			TargetController_OnChangeTarget(target);
		}

		/// <summary>
		/// Handles target clear event. Hides UI and disables overhead labels.
		/// </summary>
		/// <param name="lastTarget">The last target transform (optional).</param>
		public void TargetController_OnClearTarget(Transform lastTarget = null)
		{
			if (lastTarget != null)
			{
				Outline outline = lastTarget.GetComponent<Outline>();
				if (outline != null)
				{
					outline.enabled = false;
				}

				ICharacter character = lastTarget.GetComponent<ICharacter>();
				if (character != null)
				{
					// Don't deactivate our own name label
					IPlayerCharacter playerCharacter = character as IPlayerCharacter;
					if (playerCharacter != null &&
						playerCharacter.NetworkObject.IsOwner)
					{
						return;
					}

					// Don't deactivate our pets name label
					Pet pet = lastTarget.GetComponent<Pet>();
					if (pet != null &&
						pet.NetworkObject.IsOwner)
					{
						return;
					}
#if !UNITY_SERVER
					if (character.CharacterNameLabel != null)
					{
						character.CharacterNameLabel.gameObject.SetActive(false);
					}
					if (character.CharacterGuildLabel != null)
					{
						character.CharacterGuildLabel.gameObject.SetActive(false);
					}
#endif
				}
			}

			if (targetLabel != null)
			{
				LabelMaker.Cache(targetLabel);
				targetLabel = null;
			}

			Hide();
		}

		/// <summary>
		/// Updates the overhead label for the target, displaying name, title, and color.
		/// </summary>
		/// <param name="target">The target transform.</param>
		/// <param name="character">The character component, if present.</param>
		/// <param name="interactable">The interactable component, if present.</param>
		private void UpdateTargetLabel(Transform target, ICharacter character, IInteractable interactable)
		{
			if (targetLabel != null)
			{
				LabelMaker.Cache(targetLabel);
				targetLabel = null;
			}

			Color color = Color.grey;

			// Enable the character name labels
			if (character != null)
			{
#if !UNITY_SERVER
				if (Character.TryGet(out IFactionController factionController) &&
					character.TryGet(out IFactionController targetFactionController))
				{
					color = factionController.GetAllianceLevelColor(targetFactionController);
				}

				if (character.CharacterNameLabel != null)
				{
					character.CharacterNameLabel.gameObject.SetActive(true);
					character.CharacterNameLabel.color = color;
				}
				if (character.CharacterGuildLabel != null)
				{
					character.CharacterGuildLabel.gameObject.SetActive(true);
				}
#endif
			}
			else if (interactable != null) // Otherwise display an overhead title for interactables
			{
				Vector3 newPos = target.position;

				float colliderHeight = 1.0f;

				Collider collider = target.GetComponent<Collider>();
				if (collider != null)
				{
					collider.TryGetDimensions(out colliderHeight, out float radius);
				}

				newPos.y += colliderHeight;

				string label = interactable.Name;

				// Apply title if present
				if (!string.IsNullOrWhiteSpace(interactable.Title))
				{
					string hex = interactable.TitleColor.ToHex();
					if (!string.IsNullOrWhiteSpace(hex))
					{
						label += $"\r\n<<color=#{hex}>{interactable.Title}</color>>";
					}
				}

				targetLabel = LabelMaker.Display3D(label, newPos, color, 1.0f, 0.0f, true);
			}
		}
	}
}