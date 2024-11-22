using System;
using FishMMO.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UITarget : UICharacterControl
	{
		public TMP_Text NameLabel;
		public Slider HealthSlider;
		public CharacterAttributeTemplate HealthAttribute;

		private Cached3DLabel targetLabel;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

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
			SceneTeleporter teleporter = target.GetComponent<SceneTeleporter>();
			SceneObjectNamer sceneObjectNamer = target.GetComponent<SceneObjectNamer>();

			// Return if all conditions are false
			if (interactable == null &&
				teleporter == null &&
				characterAttributeController == null &&
				sceneObjectNamer == null)
			{
				return;
			}

			if (NameLabel != null)
			{
				NameLabel.text = interactable != null ? interactable.Name : target.name.Replace("(Clone)", "");
			}
			if (characterAttributeController != null)
			{
				if (characterAttributeController.TryGetResourceAttribute(HealthAttribute, out CharacterResourceAttribute health))
				{
					HealthSlider.value = health.CurrentValue / health.FinalValue;
				}
			}
			else
			{
				HealthSlider.value = 0;
			}

			// make the UI visible
			Show();

			UpdateTargetLabel(target, target.gameObject, interactable);
		}

		public void TargetController_OnUpdateTarget(Transform target)
		{
			TargetController_OnChangeTarget(target);
		}

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

		private void UpdateTargetLabel(Transform target, GameObject gameObject, IInteractable interactable)
		{
			if (targetLabel != null)
			{
				LabelMaker.Cache(targetLabel);
				targetLabel = null;
			}

			Color color = Color.grey;

			// Enable the character name labels
			if (gameObject != null)
			{
				ICharacter character = gameObject.GetComponent<ICharacter>();
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

					// apply title
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
}