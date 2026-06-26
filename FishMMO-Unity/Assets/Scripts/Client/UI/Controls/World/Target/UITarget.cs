using System.Collections.Generic;
using FishNet;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for displaying target information including name, health, buffs and debuffs, and overhead labels.
	/// </summary>
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
		/// The health attribute template ID used to identify health resources.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int HealthAttributeID;

		/// <summary>
		/// The parent RectTransform for displaying the target's buff and debuff icons.
		/// Assign a horizontal layout group container in the inspector beneath the health bar.
		/// </summary>
		public RectTransform TargetBuffParent;
		/// <summary>
		/// The prefab used to instantiate individual buff/debuff icons for the target.
		/// </summary>
		public UIBuffGroup TargetBuffPrefab;

		/// <summary>
		/// Cached 3D label for displaying overhead information about the target.
		/// </summary>
		private Cached3DLabel targetLabel;

		/// <summary>
		/// Dictionary tracking currently displayed buff/debuff icons by template ID.
		/// </summary>
		private readonly Dictionary<int, UIBuffGroup> targetBuffs = new Dictionary<int, UIBuffGroup>();

		/// <summary>
		/// Reusable set for reconciliation — holds template IDs that may need removal.
		/// </summary>
		private readonly HashSet<int> staleBuffKeys = new HashSet<int>();

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
			IBuffController buffController = target.GetComponent<IBuffController>();
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
				if (characterAttributeController.TryGetResourceAttribute(HealthAttributeID, out CharacterResourceAttribute health))
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

			// Refresh the target's buff/debuff icons.
			RefreshTargetBuffs(buffController);

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

			ClearTargetBuffs();

			Hide();
		}

		/// <summary>
		/// Reconciles the displayed buff/debuff icons with the target's current IBuffController state.
		/// Adds new icons for buffs that appeared, updates duration sliders on existing ones,
		/// and destroys icons for buffs that expired or were removed.
		/// </summary>
		/// <param name="buffController">The target's buff controller, or null if the target has none.</param>
		private void RefreshTargetBuffs(IBuffController buffController)
		{
			if (TargetBuffParent == null || TargetBuffPrefab == null)
				return;

			if (buffController == null || buffController.Buffs == null || buffController.Buffs.Count == 0)
			{
				ClearTargetBuffs();
				return;
			}

			uint currentTick = buffController.ResolveAuthoritativeTick(InstanceFinder.TimeManager.LocalTick);

			// Mark all existing entries as stale candidates.
			staleBuffKeys.Clear();
			foreach (var key in targetBuffs.Keys)
			{
				staleBuffKeys.Add(key);
			}

			// Reconcile with current buff state.
			foreach (var kvp in buffController.Buffs)
			{
				var buff = kvp.Value;
				if (buff == null || buff.Template == null)
					continue;

				// Still active — not stale.
				staleBuffKeys.Remove(kvp.Key);

				if (targetBuffs.TryGetValue(kvp.Key, out UIBuffGroup existing))
				{
					// Update duration slider for an existing entry.
					if (existing.DurationSlider != null && buff.Template.Duration > 0f)
					{
						existing.DurationSlider.value = buff.RemainingSeconds(currentTick) / buff.Template.Duration;
					}
				}
				else
				{
					// Instantiate a new icon for this buff/debuff.
					UIBuffGroup group = Instantiate(TargetBuffPrefab, TargetBuffParent);
					if (group.Icon != null)
					{
						group.Icon.sprite = buff.Template.Icon;
					}
					if (group.ButtonText != null)
					{
						group.ButtonText.text = buff.Template.Name;
					}
					if (group.TooltipButton != null)
					{
						group.TooltipButton.Initialize(buff.Template.ID, null, null, buff.Template);
					}
					if (group.DurationSlider != null && buff.Template.Duration > 0f)
					{
						group.DurationSlider.value = buff.RemainingSeconds(currentTick) / buff.Template.Duration;
					}
					group.gameObject.SetActive(true);
					targetBuffs.Add(kvp.Key, group);
				}
			}

			// Remove stale entries that are no longer active on the target.
			foreach (var staleKey in staleBuffKeys)
			{
				if (targetBuffs.TryGetValue(staleKey, out UIBuffGroup staleGroup))
				{
					Destroy(staleGroup.gameObject);
					targetBuffs.Remove(staleKey);
				}
			}
		}

		/// <summary>
		/// Destroys all target buff/debuff icons and clears tracking state.
		/// </summary>
		private void ClearTargetBuffs()
		{
			if (targetBuffs.Count == 0)
				return;

			foreach (var group in targetBuffs.Values)
			{
				if (group != null)
				{
					Destroy(group.gameObject);
				}
			}
			targetBuffs.Clear();
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