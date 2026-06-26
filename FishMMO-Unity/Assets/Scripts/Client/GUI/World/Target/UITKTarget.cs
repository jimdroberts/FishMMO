using System.Collections.Generic;
using FishNet;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit target frame. Replaces the legacy UGUI <see cref="UITarget"/>: shows the current
	/// target's name (faction-coloured), a health fill, and a strip of the target's buff/debuff icons.
	/// The overhead 3D label / outline / faction logic is rendering-agnostic and preserved verbatim.
	/// </summary>
	public class UITKTarget : UITKCharacterControl
	{
		/// <summary>Name of the target name label element.</summary>
		private const string NAME_LABEL_NAME = "target-name";

		/// <summary>Name of the target health fill element.</summary>
		private const string HEALTH_FILL_NAME = "target-health-fill";

		/// <summary>Name of the container that holds the target's buff icons.</summary>
		private const string BUFF_LIST_NAME = "target-buff-list";

		/// <summary>USS class applied to each generated target buff group root.</summary>
		private const string GROUP_CLASS = "buff-group";

		/// <summary>USS class applied to each target buff group's icon.</summary>
		private const string GROUP_ICON_CLASS = "buff-group__icon";

		/// <summary>USS class applied to each target buff group's depleting fill.</summary>
		private const string GROUP_FILL_CLASS = "buff-group__fill";

		/// <summary>
		/// The health attribute template ID used to identify the target's health resource.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int HealthAttributeID;

		/// <summary>
		/// Visual elements backing a single target buff icon.
		/// </summary>
		private struct TargetBuffView
		{
			/// <summary>Root container for the buff group.</summary>
			public VisualElement Root;
			/// <summary>Depleting duration fill element.</summary>
			public VisualElement Fill;
		}

		/// <summary>Cached reference to the target name label element.</summary>
		private Label nameLabel;
		/// <summary>Cached reference to the target health fill element.</summary>
		private VisualElement healthFill;
		/// <summary>Cached reference to the target buff list container element.</summary>
		private VisualElement buffList;

		/// <summary>Overhead 3D label displayed above the target.</summary>
		private Cached3DLabel targetLabel;
		/// <summary>Maps buff template IDs to their associated visual elements.</summary>
		private readonly Dictionary<int, TargetBuffView> targetBuffs = new Dictionary<int, TargetBuffView>();
		/// <summary>Scratch set used to track stale buff keys during refresh.</summary>
		private readonly HashSet<int> staleBuffKeys = new HashSet<int>();

		/// <summary>
		/// Queries the target frame elements.
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
			buffList = root.Q(BUFF_LIST_NAME);
		}

		/// <summary>
		/// Subscribes to the target controller after the character is set.
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
		/// Unsubscribes from the target controller and caches the overhead label.
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
		/// Updates the target frame and overhead label for a newly selected target.
		/// </summary>
		/// <param name="target">The new target transform.</param>
		public void TargetController_OnChangeTarget(Transform target)
		{
			if (target == null || UIManager.ControlHasFocus())
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

			if (interactable == null &&
				character == null &&
				teleporter == null &&
				characterAttributeController == null &&
				sceneObjectNamer == null)
			{
				return;
			}

			if (nameLabel != null)
			{
				Color color = Color.white;

				if (character != null &&
					Character.TryGet(out IFactionController factionController) &&
					character.TryGet(out IFactionController targetFactionController))
				{
					color = factionController.GetAllianceLevelColor(targetFactionController);
				}

				nameLabel.text = interactable != null ? interactable.Name : target.name.Replace("(Clone)", string.Empty);
				nameLabel.style.color = color;
			}

			if (characterAttributeController != null &&
				characterAttributeController.TryGetResourceAttribute(HealthAttributeID, out CharacterResourceAttribute health))
			{
				SetHealthFill(health.FinalValue > 0 ? health.CurrentValue / health.FinalValueAsFloat : 0.0f);

				if (nameLabel != null)
				{
					nameLabel.text += $" [{health.CurrentValue}/{health.FinalValue}]";
				}
			}
			else
			{
				SetHealthFill(0.0f);
			}

			RefreshTargetBuffs(buffController);

			Show();

			UpdateTargetLabel(target, character, interactable);
		}

		/// <summary>
		/// Handles a target update by reusing the change-target logic.
		/// </summary>
		/// <param name="target">The target transform.</param>
		public void TargetController_OnUpdateTarget(Transform target)
		{
			TargetController_OnChangeTarget(target);
		}

		/// <summary>
		/// Hides the frame and overhead labels when the target is cleared.
		/// </summary>
		/// <param name="lastTarget">The previous target transform, if any.</param>
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
					IPlayerCharacter playerCharacter = character as IPlayerCharacter;
					if (playerCharacter != null &&
						playerCharacter.NetworkObject.IsOwner)
					{
						return;
					}

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
		/// Sets the target health fill width as a 0-1 fraction.
		/// </summary>
		/// <param name="fraction">The health fraction.</param>
		private void SetHealthFill(float fraction)
		{
			if (healthFill != null)
			{
				healthFill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100.0f);
			}
		}

		/// <summary>
		/// Reconciles the displayed target buff icons with the target's current buff state.
		/// </summary>
		/// <param name="buffController">The target's buff controller, or null.</param>
		private void RefreshTargetBuffs(IBuffController buffController)
		{
			if (buffList == null)
			{
				return;
			}

			if (buffController == null || buffController.Buffs == null || buffController.Buffs.Count == 0)
			{
				ClearTargetBuffs();
				return;
			}

			uint currentTick = buffController.ResolveAuthoritativeTick(InstanceFinder.TimeManager.LocalTick);

			staleBuffKeys.Clear();
			foreach (int key in targetBuffs.Keys)
			{
				staleBuffKeys.Add(key);
			}

			foreach (KeyValuePair<int, Buff> kvp in buffController.Buffs)
			{
				Buff buff = kvp.Value;
				if (buff == null || buff.Template == null)
				{
					continue;
				}

				int templateID = buff.Template.ID;
				staleBuffKeys.Remove(templateID);

				float fraction = buff.Template.Duration > 0.0f
					? buff.RemainingSeconds(currentTick) / buff.Template.Duration
					: 1.0f;

				if (targetBuffs.TryGetValue(templateID, out TargetBuffView existing))
				{
					if (existing.Fill != null)
					{
						existing.Fill.style.height = Length.Percent(Mathf.Clamp01(fraction) * 100.0f);
					}
				}
				else
				{
					TargetBuffView view = CreateTargetBuff(buff.Template, fraction);
					buffList.Add(view.Root);
					targetBuffs.Add(templateID, view);
				}
			}

			foreach (int staleKey in staleBuffKeys)
			{
				if (targetBuffs.TryGetValue(staleKey, out TargetBuffView staleView))
				{
					staleView.Root?.RemoveFromHierarchy();
					targetBuffs.Remove(staleKey);
				}
			}
		}

		/// <summary>
		/// Builds the visual elements for a single target buff icon.
		/// </summary>
		/// <param name="template">The buff template to render.</param>
		/// <param name="fraction">The initial duration fraction.</param>
		/// <returns>The populated <see cref="TargetBuffView"/>.</returns>
		private TargetBuffView CreateTargetBuff(BaseBuffTemplate template, float fraction)
		{
			VisualElement groupRoot = new VisualElement();
			groupRoot.AddToClassList(GROUP_CLASS);
			if (template.IsDebuff)
			{
				groupRoot.AddToClassList("buff-group--debuff");
			}

			VisualElement fill = new VisualElement();
			fill.AddToClassList(GROUP_FILL_CLASS);
			fill.style.height = Length.Percent(Mathf.Clamp01(fraction) * 100.0f);
			groupRoot.Add(fill);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(GROUP_ICON_CLASS);
			if (template.Icon != null)
			{
				icon.style.backgroundImage = new StyleBackground(template.Icon);
			}
			groupRoot.Add(icon);

			TargetBuffView view;
			view.Root = groupRoot;
			view.Fill = fill;
			return view;
		}

		/// <summary>
		/// Removes all target buff icons.
		/// </summary>
		private void ClearTargetBuffs()
		{
			if (targetBuffs.Count == 0)
			{
				return;
			}

			foreach (TargetBuffView view in targetBuffs.Values)
			{
				view.Root?.RemoveFromHierarchy();
			}
			targetBuffs.Clear();
		}

		/// <summary>
		/// Updates the overhead 3D label for the target, displaying name, title, and faction colour.
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
			else if (interactable != null)
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
