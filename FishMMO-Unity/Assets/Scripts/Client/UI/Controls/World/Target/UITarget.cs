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

			// must be an interactable or have an attribute controller
			if ((interactable != null && sceneObjectNamer == null) ||
				teleporter != null ||
				characterAttributeController == null ||
				sceneObjectNamer == null)
			{
				TargetController_OnClearTarget();
				return;
			}

			if (NameLabel != null)
			{
				NameLabel.text = target.name.Replace("(Clone)", "");
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

			UpdateTargetLabel(target, interactable);
		}

		public void TargetController_OnUpdateTarget(Transform target)
		{
			TargetController_OnChangeTarget(target);
		}

		public void TargetController_OnClearTarget(Transform lastTarget = null)
		{
			if (lastTarget != null)
			{
#if !UNITY_SERVER
				ICharacter character = lastTarget.GetComponent<ICharacter>();
				if (character != null)
				{
					IPlayerCharacter playerCharacter = character as IPlayerCharacter;
					if (playerCharacter != null &&
						playerCharacter.NetworkObject.IsOwner)
					{
						return;
					}
					character.CharacterNameLabel.gameObject.SetActive(false);
					character.CharacterGuildLabel.gameObject.SetActive(false);
				}
#endif

				Outline outline = lastTarget.GetComponent<Outline>();
				if (outline != null)
				{
					outline.enabled = false;
				}
			}

			if (targetLabel != null)
			{
				LabelMaker.Cache(targetLabel);
				targetLabel = null;
			}

			Hide();
		}

		private void UpdateTargetLabel(Transform target, IInteractable interactable = null)
		{
			if (targetLabel != null)
			{
				LabelMaker.Cache(targetLabel);
				targetLabel = null;
			}

			// Enable the character name labels
			ICharacter character = interactable.GameObject.GetComponent<ICharacter>();
			if (character != null)
			{
#if !UNITY_SERVER
				character.CharacterNameLabel.gameObject.SetActive(true);
				character.CharacterGuildLabel.gameObject.SetActive(true);
#endif
			}
			else // Otherwise display an overhead title
			{
				Vector3 newPos = target.position;

				Collider collider = target.GetComponent<Collider>();

				float colliderHeight = collider.bounds.size.y * 0.5f;

				newPos.y += colliderHeight;

				string label = target.name.Replace("(Clone)", "");
				Color color = Color.grey;

				// apply title
				if (interactable != null &&
					!string.IsNullOrWhiteSpace(interactable.Title))
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