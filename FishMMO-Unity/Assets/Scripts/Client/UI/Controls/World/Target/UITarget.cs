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
			SceneObjectNamer sceneObjectNamer = obj.GetComponent<SceneObjectNamer>();

			// must be an interactable or have an attribute controller
			if (characterAttributeController == null && interactable == null && teleporter == null && sceneObjectNamer == null)
			{
				// hide the UI
				Hide();
				return;
			}

			if (NameLabel != null)
			{
				NameLabel.text = obj.name.Replace("(Clone)", "");
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
			Outline outline = newTarget.GetComponent<Outline>();
			if (outline != null)
			{
				outline.enabled = true;
			}
			if (targetLabel != null)
			{
				LabelMaker.Cache(targetLabel);
			}

			ICharacterAttributeController characterAttributeController = newTarget.GetComponent<ICharacterAttributeController>();
			IInteractable interactable = newTarget.GetComponent<IInteractable>();
			SceneTeleporter teleporter = newTarget.GetComponent<SceneTeleporter>();
			SceneObjectNamer sceneObjectNamer = newTarget.GetComponent<SceneObjectNamer>();

			// must be an interactable or have an attribute controller
			if ((interactable != null && sceneObjectNamer == null) ||
				teleporter != null ||
				characterAttributeController == null ||
				sceneObjectNamer == null)
			{
				return;
			}

			Vector3 newPos = newTarget.position;

			Collider collider = newTarget.GetComponent<Collider>();
			newPos.y += collider.bounds.size.y;

			Debug.Log("Height of GameObject: " + collider.bounds.size.y);

			string label = newTarget.name.Replace("(Clone)", "");
			Color color = Color.grey;

			// apply title
			if (interactable != null &&
				!string.IsNullOrWhiteSpace(interactable.Title))
			{
				string hex = Color.green.ToHex();
				if (!string.IsNullOrWhiteSpace(hex))
				{
					label += $"\r\n<<color=#{hex}>{interactable.Title}</color>>";
				}
			}

			targetLabel = LabelMaker.Display3D(label, newPos, color, 1.0f, 0.0f, true);
		}
	}
}