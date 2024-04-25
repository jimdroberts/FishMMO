using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishMMO.Shared;

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
				targetController.OnChangeTarget += OnChangeTarget;
				targetController.OnUpdateTarget += OnUpdateTarget;
				targetController.OnClearTarget += TargetController_OnClearTarget;
				targetController.OnNewTarget += TargetController_OnNewTarget;
			}
		}

		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out ITargetController targetController))
			{
				targetController.OnChangeTarget -= OnChangeTarget;
				targetController.OnUpdateTarget -= OnUpdateTarget;
				targetController.OnClearTarget -= TargetController_OnClearTarget;
				targetController.OnNewTarget -= TargetController_OnNewTarget;

				LabelMaker.Cache(targetLabel);
				targetLabel = null;
			}
		}

		public void OnChangeTarget(GameObject obj)
		{
			if (obj == null)
			{
				// hide the UI
				Hide();
				return;
			}

			ICharacterAttributeController characterAttributeController = obj.GetComponent<ICharacterAttributeController>();
			IInteractable interactable = obj.GetComponent<IInteractable>();
			SceneTeleporter teleporter = obj.GetComponent<SceneTeleporter>();

			// must be an interactable or have an attribute controller
			if (characterAttributeController == null && interactable == null && teleporter == null)
			{
				// hide the UI
				Hide();
				return;
			}

			if (NameLabel != null)
			{
				NameLabel.text = obj.name;
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
		}

		public void OnUpdateTarget(GameObject obj)
		{
			if (obj == null)
			{
				// hide the UI
				Hide();
				return;
			}

			// update the health slider
			ICharacterAttributeController characterAttributeController = obj.GetComponent<ICharacterAttributeController>();
			if (characterAttributeController != null)
			{
				if (characterAttributeController.TryGetResourceAttribute(HealthAttribute, out CharacterResourceAttribute health))
				{
					HealthSlider.value = health.CurrentValue / health.FinalValue;
				}
			}
		}

		public void TargetController_OnClearTarget(Transform lastTarget)
		{
			Outline outline = lastTarget.GetComponent<Outline>();
			if (outline != null)
			{
				outline.enabled = false;
			}
			if (targetLabel != null)
			{
				LabelMaker.Cache(targetLabel);
			}
		}

		public void TargetController_OnNewTarget(Transform newTarget)
		{
			ICharacterAttributeController characterAttributeController = newTarget.GetComponent<ICharacterAttributeController>();
			IInteractable interactable = newTarget.GetComponent<IInteractable>();
			SceneTeleporter teleporter = newTarget.GetComponent<SceneTeleporter>();

			// must be an interactable or have an attribute controller
			if (characterAttributeController == null && interactable == null && teleporter == null)
			{
				return;
			}

			Vector3 newPos = newTarget.position;

			Collider collider = newTarget.GetComponent<Collider>();
			newPos.y += collider.bounds.extents.y + 0.15f;

			string label = newTarget.name;
			Color color = Color.grey;

			// apply merchant description
			if (interactable != null)
			{
				label += $"\r\n<{interactable.Title}>";
				newPos.y += 0.15f;
				color = Color.white;
			}

			targetLabel = LabelMaker.Display3D(label, newPos, color, 1.0f, 0.0f, true);

			Outline outline = newTarget.GetComponent<Outline>();
			if (outline != null)
			{
				outline.enabled = true;
			}
		}
	}
}