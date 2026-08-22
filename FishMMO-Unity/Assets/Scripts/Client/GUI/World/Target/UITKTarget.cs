using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit target frame. Shows the current target's name (faction-coloured), a health bar,
	/// its faction standing, and strips of the buffs and debuffs the SERVER has chosen to show
	/// observers. The overhead 3D label, outline and faction colouring are rendering-agnostic and
	/// driven from here rather than from the visual tree.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Change-driven.</b> <c>TargetController</c> re-raises <c>OnUpdateTarget</c> twenty times a
	/// second for as long as the pointer rests on something, and this panel treated every one of
	/// them as a brand-new target: seven <c>GetComponent</c> calls, two or three string
	/// concatenations, a full reconcile of the buff strip and — worst — a destroy/recreate cycle of
	/// the overhead 3D label, twenty times a second, for a target that had not changed. Everything
	/// that depends only on WHICH target is resolved once, on the change; the update path only
	/// re-reads the values that actually move.
	/// </para>
	/// <para>
	/// <b>Model / view split.</b> The displayed values live in fields; the elements belong to one
	/// visual tree and are rebuilt from those fields in <c>OnAfterShow</c> / <c>OnAfterStarting</c>.
	/// <c>UIDocument</c> re-clones the UXML on every enable, so a frame that wrote its content
	/// before <c>Show()</c> — or cached elements across a hide/show — comes back blank.
	/// </para>
	/// </remarks>
	public class UITKTarget : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>Name of the target name label element.</summary>
		private const string NAME_LABEL_NAME = "target-name";

		/// <summary>Name of the target level badge element.</summary>
		private const string LEVEL_LABEL_NAME = "target-level";

		/// <summary>Name of the target faction badge element.</summary>
		private const string FACTION_LABEL_NAME = "target-faction";

		/// <summary>Name of the target health fill element.</summary>
		private const string HEALTH_FILL_NAME = "target-health-fill";

		/// <summary>Name of the target health bar container element.</summary>
		private const string HEALTH_BAR_NAME = "target-health";

		/// <summary>Name of the target health value label element.</summary>
		private const string HEALTH_TEXT_NAME = "target-health-text";

		/// <summary>Name of the container that holds the target's buff icons.</summary>
		private const string BUFF_LIST_NAME = "target-buff-list";

		/// <summary>Name of the container that holds the target's debuff icons.</summary>
		private const string DEBUFF_LIST_NAME = "target-debuff-list";

		/// <summary>USS class applied to each generated target buff group root.</summary>
		private const string GROUP_CLASS = "buff-group";

		/// <summary>USS class applied to a debuff group root.</summary>
		private const string GROUP_DEBUFF_CLASS = "buff-group--debuff";

		/// <summary>USS class applied to each target buff group's icon.</summary>
		private const string GROUP_ICON_CLASS = "buff-group__icon";

		/// <summary>USS class applied to each target buff group's depleting fill.</summary>
		private const string GROUP_FILL_CLASS = "buff-group__fill";

		/// <summary>USS class applied to each target buff group's stack label.</summary>
		private const string GROUP_LABEL_CLASS = "buff-group__label";

		/// <summary>Name of the shared tooltip overlay registered with the UIManager.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		/// <summary>
		/// The health attribute template ID used to identify the target's health resource.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int HealthAttributeID;

		/// <summary>
		/// Visual elements backing a single target buff icon. Pooled — never destroyed on a target
		/// swap, only detached and re-bound.
		/// </summary>
		private sealed class BuffIcon
		{
			/// <summary>Root container for the buff group.</summary>
			public VisualElement Root;
			/// <summary>Depleting duration fill element.</summary>
			public VisualElement Fill;
			/// <summary>Icon element.</summary>
			public VisualElement Icon;
			/// <summary>Stack count label.</summary>
			public Label Label;
			/// <summary>The template this icon is currently bound to.</summary>
			public BaseBuffTemplate Template;
			/// <summary>True while this icon carries the debuff modifier class.</summary>
			public bool IsDebuff;
		}

		/// <summary>Cached reference to the target name label element.</summary>
		private Label nameLabel;
		/// <summary>Cached reference to the target level badge element.</summary>
		private Label levelLabel;
		/// <summary>Cached reference to the target faction badge element.</summary>
		private Label factionLabel;
		/// <summary>Cached reference to the target health fill element.</summary>
		private VisualElement healthFill;
		/// <summary>Cached reference to the target health bar container element.</summary>
		private VisualElement healthBar;
		/// <summary>Cached reference to the target health value label element.</summary>
		private Label healthText;
		/// <summary>Cached reference to the target buff list container element.</summary>
		private VisualElement buffList;
		/// <summary>Cached reference to the target debuff list container element.</summary>
		private VisualElement debuffList;

		/// <summary>Overhead 3D label displayed above the target.</summary>
		private UITKWorldLabel targetLabel;

		#region Resolved target (recomputed only when the target actually changes)

		/// <summary>The transform currently framed.</summary>
		private Transform currentTarget;
		/// <summary>The framed target's attribute controller, or null.</summary>
		private ICharacterAttributeController targetAttributes;
		/// <summary>The framed target's buff controller, or null.</summary>
		private IBuffController targetBuffs;
		/// <summary>The framed target's character component, or null.</summary>
		private ICharacter targetCharacter;
		/// <summary>The framed target's interactable component, or null.</summary>
		private IInteractable targetInteractable;

		#endregion

		#region Displayed values (model)

		/// <summary>The target's display name without the health annotation.</summary>
		private string displayName = string.Empty;
		/// <summary>The colour the name is drawn in, from faction standing.</summary>
		private Color displayColor = Color.white;
		/// <summary>The faction standing badge text, or empty when unknown.</summary>
		private string displayFaction = string.Empty;
		/// <summary>True when the target has a health resource to show.</summary>
		private bool hasHealth;
		/// <summary>The health bar fill fraction.</summary>
		private float healthFraction;
		/// <summary>The health value text, e.g. "412/900".</summary>
		private string healthValueText = string.Empty;

		/// <summary>
		/// The last health numbers rendered, so the string is only rebuilt when they change.
		/// </summary>
		/// <remarks>
		/// The old frame rebuilt <c>"{CurrentValue}/{FinalValue}"</c> and appended it to the name
		/// every update tick — two to three string allocations twenty times a second per hovered
		/// target, all of them identical to the last set.
		/// </remarks>
		private int lastHealthCurrent = int.MinValue;
		/// <summary>The last maximum health rendered.</summary>
		private int lastHealthMax = int.MinValue;

		#endregion

		#region Buff icon pool

		/// <summary>Icons currently attached to the buff strip.</summary>
		private readonly List<BuffIcon> activeBuffIcons = new List<BuffIcon>();
		/// <summary>Icons currently attached to the debuff strip.</summary>
		private readonly List<BuffIcon> activeDebuffIcons = new List<BuffIcon>();
		/// <summary>Detached icons available for reuse.</summary>
		private readonly List<BuffIcon> iconPool = new List<BuffIcon>();

		/// <summary>
		/// The observed buff list this panel last rendered, kept so a target swap can be drawn
		/// after a tree rebuild without waiting for the next server push.
		/// </summary>
		private readonly List<ObservedBuffEntry> observedBuffModel = new List<ObservedBuffEntry>();

		/// <summary>Unscaled time the observed list was captured, for the local countdown.</summary>
		private float observedBuffCaptureTime;

		#endregion

		/// <summary>
		/// Queries the target frame elements and subscribes to the observed-buff push.
		/// </summary>
		/// <remarks>
		/// Re-runs on every tree rebuild. The pooled icons belong to the tree that was just
		/// replaced, so the pool is dropped rather than reused; the static subscription is removed
		/// before it is added so rebuilds cannot stack handlers.
		/// </remarks>
		public override void OnStarting()
		{
			activeBuffIcons.Clear();
			activeDebuffIcons.Clear();
			iconPool.Clear();

			nameLabel = null;
			levelLabel = null;
			factionLabel = null;
			healthFill = null;
			healthBar = null;
			healthText = null;
			buffList = null;
			debuffList = null;

			VisualElement root = Root;
			if (root != null)
			{
				nameLabel = root.Q<Label>(NAME_LABEL_NAME);
				levelLabel = root.Q<Label>(LEVEL_LABEL_NAME);
				factionLabel = root.Q<Label>(FACTION_LABEL_NAME);
				healthFill = root.Q(HEALTH_FILL_NAME);
				healthBar = root.Q(HEALTH_BAR_NAME);
				healthText = root.Q<Label>(HEALTH_TEXT_NAME);
				buffList = root.Q(BUFF_LIST_NAME);
				debuffList = root.Q(DEBUFF_LIST_NAME);
			}

			/* There is no level anywhere in the character model — no column, no attribute template,
			 * no broadcast field — so the badge has nothing to show. Hidden rather than left as a
			 * permanently empty box holding layout open. It is wired and ready for the day a level
			 * exists; SetLevel is the single place to fill it. */
			if (levelLabel != null)
			{
				levelLabel.style.display = DisplayStyle.None;
			}

			IBuffController.OnObservedBuffsChanged -= BuffController_OnObservedBuffsChanged;
			IBuffController.OnObservedBuffsChanged += BuffController_OnObservedBuffsChanged;
		}

		/// <summary>
		/// Releases the observed-buff subscription and the overhead label.
		/// </summary>
		public override void OnDestroying()
		{
			IBuffController.OnObservedBuffsChanged -= BuffController_OnObservedBuffsChanged;

			ReleaseTargetLabel();

			base.OnDestroying();
		}

		/// <summary>
		/// Unsubscribes from the target controller before a character is set.
		/// </summary>
		/// <remarks>
		/// Pairs with <see cref="OnPostSetCharacter"/> so the two can be run back to back without
		/// subscribing twice. That happens whenever this panel's visual tree is rebuilt and its
		/// state has to be re-applied — the unsubscribe already existed for the unset path, but
		/// re-applying the same character never went through it.
		/// </remarks>
		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out ITargetController targetController))
			{
				targetController.OnChangeTarget -= TargetController_OnChangeTarget;
				targetController.OnUpdateTarget -= TargetController_OnUpdateTarget;
				targetController.OnClearTarget -= TargetController_OnClearTarget;
			}
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
		/// Unsubscribes from the target controller and releases the overhead label.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character != null &&
				Character.TryGet(out ITargetController targetController))
			{
				targetController.OnChangeTarget -= TargetController_OnChangeTarget;
				targetController.OnUpdateTarget -= TargetController_OnUpdateTarget;
				targetController.OnClearTarget -= TargetController_OnClearTarget;
			}

			ClearTarget(null);
		}

		/// <inheritdoc />
		public override void OnQuitToLogin()
		{
			ClearTarget(null);

			base.OnQuitToLogin();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			ApplyTargetState();
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			ApplyTargetState();
		}

		/// <summary>
		/// Animates the buff strips' remaining-duration fills.
		/// </summary>
		/// <remarks>
		/// The server pushes an observed-buff list only when the SET changes, so the countdown
		/// between pushes is local. The entries carry the seconds remaining at send time; this
		/// subtracts the time elapsed since. Skipped entirely when nothing is on screen.
		/// </remarks>
		protected override void OnTick()
		{
			if (!Visible || observedBuffModel.Count == 0)
			{
				return;
			}

			float elapsed = Time.unscaledTime - observedBuffCaptureTime;

			for (int i = 0; i < observedBuffModel.Count; ++i)
			{
				ObservedBuffEntry entry = observedBuffModel[i];
				if (entry.TotalSeconds <= 0.0f)
				{
					continue;
				}

				float remaining = entry.RemainingSeconds - elapsed;
				float fraction = Mathf.Clamp01(remaining / entry.TotalSeconds);

				BuffIcon icon = FindIcon(entry.TemplateID);
				if (icon?.Fill != null)
				{
					icon.Fill.style.height = Length.Percent(fraction * 100.0f);
				}
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
				ClearTarget(null);
				return;
			}

			/* Resolved ONCE per target. Seven GetComponent calls is a fine price for a target
			 * change and an absurd one twenty times a second, which is what the update path used
			 * to pay by routing straight back into this method. */
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

			currentTarget = target;
			targetAttributes = characterAttributeController;
			targetBuffs = buffController;
			targetCharacter = character;
			targetInteractable = interactable;

			displayColor = Color.white;
			displayFaction = string.Empty;

			if (character != null &&
				Character != null &&
				Character.TryGet(out IFactionController factionController) &&
				character.TryGet(out IFactionController targetFactionController))
			{
				displayColor = factionController.GetAllianceLevelColor(targetFactionController);
				displayFaction = factionController.GetAllianceLevel(targetFactionController).ToString();
			}

			displayName = interactable != null
				? interactable.Name
				: target.name.Replace("(Clone)", string.Empty);

			/* Whether a health bar is shown follows from whether the target actually has a health
			 * resource, rather than from what kind of thing it is. A portal or a signpost is
			 * worth targeting and naming but has no health to show, and an empty bar reads as a
			 * dead one. Anything that does gain health later — a destructible structure, a siege
			 * engine — starts showing a bar the moment it has the attribute, with no change
			 * here. */
			lastHealthCurrent = int.MinValue;
			lastHealthMax = int.MinValue;
			RefreshHealth();

			CaptureObservedBuffs();

			// Show() re-clones the tree, so the state above must be written AFTER it. OnAfterShow
			// does exactly that; ApplyTargetState covers the already-visible case.
			if (!Visible)
			{
				Show();
			}
			else
			{
				ApplyTargetState();
			}

			UpdateTargetLabel(target, character, interactable);
		}

		/// <summary>
		/// Handles a target update — the same target, re-reported on the controller's poll.
		/// </summary>
		/// <param name="target">The target transform.</param>
		/// <remarks>
		/// This used to call straight into <see cref="TargetController_OnChangeTarget"/>, treating
		/// twenty polls a second as twenty target changes. It now only re-reads what moves.
		/// </remarks>
		public void TargetController_OnUpdateTarget(Transform target)
		{
			if (target == null || UIManager.ControlHasFocus())
			{
				ClearTarget(null);
				return;
			}

			if (!ReferenceEquals(target, currentTarget))
			{
				// The controller reported an update for something we have not resolved yet.
				TargetController_OnChangeTarget(target);
				return;
			}

			if (RefreshHealth())
			{
				ApplyHealthElements();
			}
		}

		/// <summary>
		/// Hides the frame and overhead labels when the target is cleared.
		/// </summary>
		/// <param name="lastTarget">The previous target transform, if any.</param>
		public void TargetController_OnClearTarget(Transform lastTarget = null)
		{
			ClearTarget(lastTarget);
		}

		/// <summary>
		/// Re-renders the observed buff strips when the server pushes a new list for this target.
		/// </summary>
		/// <param name="buffController">The controller whose observed list changed.</param>
		private void BuffController_OnObservedBuffsChanged(IBuffController buffController)
		{
			/* Static event: fires for every character on the client. Only the framed one matters.
			 * This is the same class of bug the local buff strips had — an unscoped static buff
			 * event painting every character's buffs onto one panel. */
			if (targetBuffs == null || !ReferenceEquals(buffController, targetBuffs))
			{
				return;
			}

			CaptureObservedBuffs();
			ApplyBuffElements();
		}

		/// <summary>
		/// Copies the framed target's server-filtered buff list into the model.
		/// </summary>
		private void CaptureObservedBuffs()
		{
			observedBuffModel.Clear();
			observedBuffCaptureTime = Time.unscaledTime;

			IReadOnlyList<ObservedBuffEntry> source = targetBuffs?.ObservedBuffs;
			if (source == null)
			{
				return;
			}

			for (int i = 0; i < source.Count; ++i)
			{
				observedBuffModel.Add(source[i]);
			}
		}

		/// <summary>
		/// Re-reads the target's health, reporting whether the displayed numbers changed.
		/// </summary>
		/// <returns>True if anything needs repainting.</returns>
		private bool RefreshHealth()
		{
			bool nowHasHealth = targetAttributes != null &&
				targetAttributes.TryGetResourceAttribute(HealthAttributeID, out CharacterResourceAttribute health);

			if (!nowHasHealth)
			{
				bool changed = hasHealth;
				hasHealth = false;
				healthFraction = 0.0f;
				healthValueText = string.Empty;
				lastHealthCurrent = int.MinValue;
				lastHealthMax = int.MinValue;
				return changed;
			}

			targetAttributes.TryGetResourceAttribute(HealthAttributeID, out CharacterResourceAttribute resource);

			int current = Mathf.RoundToInt(resource.CurrentValue);
			int max = resource.FinalValue;

			if (hasHealth && current == lastHealthCurrent && max == lastHealthMax)
			{
				return false;
			}

			hasHealth = true;
			lastHealthCurrent = current;
			lastHealthMax = max;
			healthFraction = max > 0 ? Mathf.Clamp01(resource.CurrentValue / resource.FinalValueAsFloat) : 0.0f;
			healthValueText = current + "/" + max;
			return true;
		}

		/// <summary>
		/// Writes the whole tracked target state into the current visual tree.
		/// </summary>
		/// <remarks>
		/// Called from <see cref="OnAfterShow"/> and <see cref="OnAfterStarting"/>. On a panel's
		/// very first open <c>hasStarted</c> is still false, so <c>ReinitializeIfTreeReplaced</c>
		/// bails out and only <c>OnAfterShow</c> runs; on later shows the tree may genuinely have
		/// been replaced and both fire. Writing the same state twice is harmless.
		/// </remarks>
		private void ApplyTargetState()
		{
			if (nameLabel != null)
			{
				nameLabel.text = displayName;
				nameLabel.style.color = displayColor;
			}

			if (factionLabel != null)
			{
				factionLabel.text = displayFaction;
				factionLabel.style.display = string.IsNullOrEmpty(displayFaction)
					? DisplayStyle.None
					: DisplayStyle.Flex;
			}

			ApplyHealthElements();
			ApplyBuffElements();
		}

		/// <summary>
		/// Writes the tracked health values into the health elements.
		/// </summary>
		private void ApplyHealthElements()
		{
			if (healthBar != null)
			{
				healthBar.style.display = hasHealth ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (healthFill != null)
			{
				healthFill.style.width = Length.Percent(Mathf.Clamp01(healthFraction) * 100.0f);
			}
			if (healthText != null)
			{
				// Was declared in the UXML and never written; the numbers were concatenated onto
				// the name label instead, which is what made the name allocate every tick.
				healthText.text = healthValueText;
			}
		}

		/// <summary>
		/// Reconciles the pooled buff/debuff icons with the observed-buff model.
		/// </summary>
		/// <remarks>
		/// Icons are POOLED. A target swap detaches the icons the previous target was using and
		/// re-binds them, so switching targets in a crowded fight allocates nothing: no
		/// <c>VisualElement</c>, no callback closure, no style object. Only a target carrying more
		/// buffs than any target so far grows the pool.
		/// </remarks>
		private void ApplyBuffElements()
		{
			if (buffList == null || debuffList == null)
			{
				return;
			}

			ReleaseIcons(activeBuffIcons);
			ReleaseIcons(activeDebuffIcons);

			float elapsed = Time.unscaledTime - observedBuffCaptureTime;

			for (int i = 0; i < observedBuffModel.Count; ++i)
			{
				ObservedBuffEntry entry = observedBuffModel[i];

				BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(entry.TemplateID);
				if (template == null)
				{
					continue;
				}

				float fraction = entry.TotalSeconds > 0.0f
					? Mathf.Clamp01((entry.RemainingSeconds - elapsed) / entry.TotalSeconds)
					: 1.0f;

				BuffIcon icon = RentIcon();
				BindIcon(icon, template, entry.Stacks, fraction);

				if (template.IsDebuff)
				{
					debuffList.Add(icon.Root);
					activeDebuffIcons.Add(icon);
				}
				else
				{
					buffList.Add(icon.Root);
					activeBuffIcons.Add(icon);
				}
			}

			buffList.style.display = activeBuffIcons.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			debuffList.style.display = activeDebuffIcons.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>
		/// Finds the rendered icon for a template ID, or null.
		/// </summary>
		/// <param name="templateID">The buff template ID.</param>
		private BuffIcon FindIcon(int templateID)
		{
			for (int i = 0; i < activeBuffIcons.Count; ++i)
			{
				if (activeBuffIcons[i].Template != null && activeBuffIcons[i].Template.ID == templateID)
				{
					return activeBuffIcons[i];
				}
			}
			for (int i = 0; i < activeDebuffIcons.Count; ++i)
			{
				if (activeDebuffIcons[i].Template != null && activeDebuffIcons[i].Template.ID == templateID)
				{
					return activeDebuffIcons[i];
				}
			}
			return null;
		}

		/// <summary>
		/// Detaches every icon in a list and returns them to the pool.
		/// </summary>
		/// <param name="icons">The icons to release.</param>
		private void ReleaseIcons(List<BuffIcon> icons)
		{
			for (int i = 0; i < icons.Count; ++i)
			{
				BuffIcon icon = icons[i];
				icon.Root?.RemoveFromHierarchy();
				icon.Template = null;
				iconPool.Add(icon);
			}
			icons.Clear();
		}

		/// <summary>
		/// Takes an icon from the pool, creating one only if the pool is empty.
		/// </summary>
		private BuffIcon RentIcon()
		{
			int last = iconPool.Count - 1;
			if (last >= 0)
			{
				BuffIcon pooled = iconPool[last];
				iconPool.RemoveAt(last);
				return pooled;
			}
			return CreateIcon();
		}

		/// <summary>
		/// Builds the visual elements for one pooled buff icon.
		/// </summary>
		/// <returns>The new icon.</returns>
		/// <remarks>
		/// The hover callbacks are registered ONCE, here, and read the icon's current template at
		/// invocation time. Registering them per bind would attach a new closure on every target
		/// swap and leak one handler per swap onto an element that is never destroyed.
		/// </remarks>
		private BuffIcon CreateIcon()
		{
			VisualElement groupRoot = new VisualElement();
			groupRoot.AddToClassList(GROUP_CLASS);

			VisualElement fill = new VisualElement();
			fill.AddToClassList(GROUP_FILL_CLASS);
			groupRoot.Add(fill);

			VisualElement iconElement = new VisualElement();
			iconElement.AddToClassList(GROUP_ICON_CLASS);
			groupRoot.Add(iconElement);

			Label label = new Label(string.Empty);
			label.AddToClassList(GROUP_LABEL_CLASS);
			label.pickingMode = PickingMode.Ignore;
			groupRoot.Add(label);

			BuffIcon icon = new BuffIcon
			{
				Root = groupRoot,
				Fill = fill,
				Icon = iconElement,
				Label = label,
			};

			groupRoot.RegisterCallback<PointerEnterEvent>(evt => OnIconPointerEnter(icon));
			groupRoot.RegisterCallback<PointerLeaveEvent>(evt => OnIconPointerLeave(icon));

			return icon;
		}

		/// <summary>
		/// Binds a pooled icon to a template, stack count and duration fraction.
		/// </summary>
		/// <param name="icon">The icon to bind.</param>
		/// <param name="template">The buff template.</param>
		/// <param name="stacks">Stack count above the base application.</param>
		/// <param name="fraction">Remaining duration fraction (0-1).</param>
		private void BindIcon(BuffIcon icon, BaseBuffTemplate template, int stacks, float fraction)
		{
			icon.Template = template;

			if (icon.IsDebuff != template.IsDebuff)
			{
				icon.IsDebuff = template.IsDebuff;
				icon.Root.EnableInClassList(GROUP_DEBUFF_CLASS, template.IsDebuff);
			}

			if (icon.Icon != null)
			{
				icon.Icon.style.backgroundImage = template.Icon != null
					? new StyleBackground(template.Icon)
					: new StyleBackground();
			}
			if (icon.Fill != null)
			{
				icon.Fill.style.height = Length.Percent(Mathf.Clamp01(fraction) * 100.0f);
			}
			if (icon.Label != null)
			{
				// Stacks counts applications ABOVE the base one, so one application shows nothing.
				icon.Label.text = stacks > 0 ? (stacks + 1).ToString() : string.Empty;
			}
		}

		/// <summary>
		/// Opens the buff tooltip for a hovered target buff icon.
		/// </summary>
		/// <param name="icon">The hovered icon.</param>
		private void OnIconPointerEnter(BuffIcon icon)
		{
			if (icon.Template == null)
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Open(icon.Template.Tooltip(), icon.Root);
			}
		}

		/// <summary>
		/// Closes the buff tooltip for a target buff icon.
		/// </summary>
		/// <param name="icon">The icon the pointer left.</param>
		private void OnIconPointerLeave(BuffIcon icon)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.HideFor(icon.Root);
			}
		}

		/// <summary>
		/// Drops the framed target, releases its overhead labels and hides the frame.
		/// </summary>
		/// <param name="lastTarget">The previous target transform, if it is still alive.</param>
		/// <remarks>
		/// <paramref name="lastTarget"/> is tested with Unity's overloaded <c>!=</c>, which reports
		/// a destroyed object as null — so a target that died rather than being deselected takes
		/// the null path and no <c>GetComponent</c> is attempted on a destroyed object. The frame
		/// comes down either way, which is the whole point: it used to stay on the corpse.
		/// </remarks>
		private void ClearTarget(Transform lastTarget)
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
					bool keepLabels = (playerCharacter != null && playerCharacter.NetworkObject.IsOwner);

					if (!keepLabels)
					{
						Pet pet = lastTarget.GetComponent<Pet>();
						keepLabels = pet != null && pet.NetworkObject.IsOwner;
					}

					if (!keepLabels)
					{
						if (character.CharacterNameLabel != null)
						{
							character.CharacterNameLabel.gameObject.SetActive(false);
						}
						if (character.CharacterGuildLabel != null)
						{
							character.CharacterGuildLabel.gameObject.SetActive(false);
						}
					}
				}
			}

			/* The original returned EARLY when the last target was the local player or their own
			 * pet, so the overhead 3D label was never released and the frame never hid — it just
			 * stopped updating. Keeping the nameplate visible and taking down the frame are
			 * separate decisions; only the first one depends on who the target was. */
			ReleaseTargetLabel();

			currentTarget = null;
			targetAttributes = null;
			targetBuffs = null;
			targetCharacter = null;
			targetInteractable = null;

			displayName = string.Empty;
			displayFaction = string.Empty;
			displayColor = Color.white;
			hasHealth = false;
			healthFraction = 0.0f;
			healthValueText = string.Empty;
			lastHealthCurrent = int.MinValue;
			lastHealthMax = int.MinValue;

			observedBuffModel.Clear();
			ReleaseIcons(activeBuffIcons);
			ReleaseIcons(activeDebuffIcons);

			Hide();
		}

		/// <summary>
		/// Returns the overhead 3D label to the label pool.
		/// </summary>
		private void ReleaseTargetLabel()
		{
			if (targetLabel != null)
			{
				UITKLabelMaker.Cache(targetLabel);
				targetLabel = null;
			}
		}

		/// <summary>
		/// Updates the overhead 3D label for the target, displaying name, title, and faction colour.
		/// </summary>
		/// <param name="target">The target transform.</param>
		/// <param name="character">The character component, if present.</param>
		/// <param name="interactable">The interactable component, if present.</param>
		/// <remarks>
		/// Only reached from the CHANGE path now. It destroys and recreates a pooled GameObject
		/// label, and the update path used to run it twenty times a second for a target that had
		/// not moved or changed — a destroy/create cycle per tick of hover.
		/// </remarks>
		private void UpdateTargetLabel(Transform target, ICharacter character, IInteractable interactable)
		{
			ReleaseTargetLabel();

			Color color = Color.grey;

			if (character != null)
			{
				if (Character != null &&
					Character.TryGet(out IFactionController factionController) &&
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

				targetLabel = UITKLabelMaker.Display3D(label, newPos, color, 1.0f, 0.0f, true);
			}
		}
	}
}
