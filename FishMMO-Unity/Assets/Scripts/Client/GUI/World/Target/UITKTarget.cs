using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit target frame: two cards side by side. The HOVER card follows the pointer, as
	/// the frame always has; the PINNED card holds a character the player chose to track and stays
	/// up until they release it, it dies, it despawns or it leaves range. Each card shows its
	/// target's name (faction-coloured), a health bar, its faction standing, and strips of the
	/// buffs and debuffs the SERVER has chosen to show observers. The overhead 3D label, outline
	/// and faction colouring are rendering-agnostic and driven from here rather than from the
	/// visual tree.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two cards, one panel.</b> Action combat does not want a sticky target: abilities go
	/// where the player aims, and the hover frame is the readout for that. What players asked
	/// for is a way to keep ONE opponent's health and debuffs on screen while the pointer sweeps
	/// across everyone else in the fight — so the pin is a second card, not a change to the
	/// first. Nothing gameplay-authoritative reads either card. The two never show the same
	/// character: when the pointer rests on the pinned character the hover card stands down,
	/// because two identical cards say nothing that one does not.
	/// </para>
	/// <para>
	/// <b>Change-driven.</b> <c>TargetController</c> re-raises <c>OnUpdateTarget</c> twenty times a
	/// second for as long as the pointer rests on something, and this panel treated every one of
	/// them as a brand-new target: seven <c>GetComponent</c> calls, two or three string
	/// concatenations, a full reconcile of the buff strip and — worst — a destroy/recreate cycle of
	/// the overhead 3D label, twenty times a second, for a target that had not changed. Everything
	/// that depends only on WHICH target is resolved once, on the change; the update path only
	/// re-reads the values that actually move. The pinned card gets no update event at all, so
	/// its health is re-read from <see cref="OnTick"/> — an integer comparison per frame.
	/// </para>
	/// <para>
	/// <b>Model / view split.</b> The displayed values live in each card's fields; the elements
	/// belong to one visual tree and are rebuilt from those fields in <c>OnAfterShow</c> /
	/// <c>OnAfterStarting</c>. <c>UIDocument</c> re-clones the UXML on every enable, so a frame
	/// that wrote its content before <c>Show()</c> — or cached elements across a hide/show — comes
	/// back blank.
	/// </para>
	/// </remarks>
	public class UITKTarget : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>Name of the card that follows the pointer.</summary>
		private const string HOVER_CARD_NAME = "target-card-hover";

		/// <summary>Name of the card that holds the pinned character.</summary>
		private const string PINNED_CARD_NAME = "target-card-pinned";

		/// <summary>Name of a card's target name label element.</summary>
		private const string NAME_LABEL_NAME = "target-name";

		/// <summary>Name of a card's target level badge element.</summary>
		private const string LEVEL_LABEL_NAME = "target-level";

		/// <summary>Name of a card's target faction badge element.</summary>
		private const string FACTION_LABEL_NAME = "target-faction";

		/// <summary>Name of a card's target health fill element.</summary>
		private const string HEALTH_FILL_NAME = "target-health-fill";

		/// <summary>Name of a card's target health bar container element.</summary>
		private const string HEALTH_BAR_NAME = "target-health";

		/// <summary>Name of a card's target health value label element.</summary>
		private const string HEALTH_TEXT_NAME = "target-health-text";

		/// <summary>Name of the container that holds a card's buff icons.</summary>
		private const string BUFF_LIST_NAME = "target-buff-list";

		/// <summary>Name of the container that holds a card's debuff icons.</summary>
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
		/// swap, only detached and re-bound. The pool is shared by both cards.
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

		/// <summary>
		/// One card of the frame: the elements it draws into, the target it has resolved, and the
		/// values it shows. The hover and pinned cards are two instances of this with different
		/// drivers; everything that renders a card is written once against it.
		/// </summary>
		private sealed class TargetCard
		{
			/// <summary>Name of the card's root element in the UXML.</summary>
			public readonly string RootName;

			/// <summary>True for the card that holds the pinned character.</summary>
			public readonly bool IsPinned;

			public TargetCard(string rootName, bool isPinned)
			{
				RootName = rootName;
				IsPinned = isPinned;
			}

			#region Elements (belong to the current visual tree)

			/// <summary>The card's root element.</summary>
			public VisualElement Root;
			/// <summary>Cached reference to the name label element.</summary>
			public Label NameLabel;
			/// <summary>Cached reference to the level badge element.</summary>
			public Label LevelLabel;
			/// <summary>Cached reference to the faction badge element.</summary>
			public Label FactionLabel;
			/// <summary>Cached reference to the health fill element.</summary>
			public VisualElement HealthFill;
			/// <summary>Cached reference to the health bar container element.</summary>
			public VisualElement HealthBar;
			/// <summary>Cached reference to the health value label element.</summary>
			public Label HealthText;
			/// <summary>Cached reference to the buff list container element.</summary>
			public VisualElement BuffList;
			/// <summary>Cached reference to the debuff list container element.</summary>
			public VisualElement DebuffList;

			#endregion

			#region Resolved target (recomputed only when the target actually changes)

			/// <summary>The transform this card frames.</summary>
			public Transform Target;
			/// <summary>The framed target's attribute controller, or null.</summary>
			public ICharacterAttributeController Attributes;
			/// <summary>The framed target's buff controller, or null.</summary>
			public IBuffController Buffs;
			/// <summary>The framed target's character component, or null.</summary>
			public ICharacter Character;
			/// <summary>The framed target's interactable component, or null.</summary>
			public IInteractable Interactable;

			#endregion

			#region Displayed values (model)

			/// <summary>The target's display name without the health annotation.</summary>
			public string DisplayName = string.Empty;
			/// <summary>The colour the name is drawn in, from faction standing.</summary>
			public Color DisplayColor = Color.white;
			/// <summary>The faction standing badge text, or empty when unknown.</summary>
			public string DisplayFaction = string.Empty;
			/// <summary>True when the target has a health resource to show.</summary>
			public bool HasHealth;
			/// <summary>The health bar fill fraction.</summary>
			public float HealthFraction;
			/// <summary>The health value text, e.g. "412/900".</summary>
			public string HealthValueText = string.Empty;

			/// <summary>
			/// The last health numbers rendered, so the string is only rebuilt when they change.
			/// </summary>
			/// <remarks>
			/// The old frame rebuilt <c>"{CurrentValue}/{FinalValue}"</c> and appended it to the
			/// name every update tick — two to three string allocations twenty times a second per
			/// hovered target, all of them identical to the last set.
			/// </remarks>
			public int LastHealthCurrent = int.MinValue;
			/// <summary>The last maximum health rendered.</summary>
			public int LastHealthMax = int.MinValue;

			#endregion

			#region Buff strip

			/// <summary>Icons currently attached to the buff strip.</summary>
			public readonly List<BuffIcon> ActiveBuffIcons = new List<BuffIcon>();
			/// <summary>Icons currently attached to the debuff strip.</summary>
			public readonly List<BuffIcon> ActiveDebuffIcons = new List<BuffIcon>();

			/// <summary>
			/// The observed buff list this card last rendered, kept so a target swap can be drawn
			/// after a tree rebuild without waiting for the next server push.
			/// </summary>
			public readonly List<ObservedBuffEntry> ObservedBuffModel = new List<ObservedBuffEntry>();

			/// <summary>Unscaled time the observed list was captured, for the local countdown.</summary>
			public float ObservedBuffCaptureTime;

			#endregion

			/// <summary>Overhead 3D label displayed above a framed interactable.</summary>
			public UITKWorldLabel OverheadLabel;

			/// <summary>
			/// True while a target is resolved. Reference identity on purpose: a target that was
			/// destroyed rather than released still counts until the card is released, so the
			/// release path runs exactly once for it.
			/// </summary>
			public bool HasTarget => !ReferenceEquals(Target, null);

			/// <summary>Queries this card's elements from a freshly cloned tree.</summary>
			/// <param name="panelRoot">The panel's root element.</param>
			public void QueryElements(VisualElement panelRoot)
			{
				Root = panelRoot?.Q(RootName);
				NameLabel = Root?.Q<Label>(NAME_LABEL_NAME);
				LevelLabel = Root?.Q<Label>(LEVEL_LABEL_NAME);
				FactionLabel = Root?.Q<Label>(FACTION_LABEL_NAME);
				HealthFill = Root?.Q(HEALTH_FILL_NAME);
				HealthBar = Root?.Q(HEALTH_BAR_NAME);
				HealthText = Root?.Q<Label>(HEALTH_TEXT_NAME);
				BuffList = Root?.Q(BUFF_LIST_NAME);
				DebuffList = Root?.Q(DEBUFF_LIST_NAME);
			}

			/// <summary>Drops the resolved target and every displayed value.</summary>
			public void ClearModel()
			{
				Target = null;
				Attributes = null;
				Buffs = null;
				Character = null;
				Interactable = null;

				DisplayName = string.Empty;
				DisplayFaction = string.Empty;
				DisplayColor = Color.white;
				HasHealth = false;
				HealthFraction = 0.0f;
				HealthValueText = string.Empty;
				LastHealthCurrent = int.MinValue;
				LastHealthMax = int.MinValue;

				ObservedBuffModel.Clear();
			}
		}

		/// <summary>The card that follows the pointer.</summary>
		private readonly TargetCard hoverCard = new TargetCard(HOVER_CARD_NAME, isPinned: false);

		/// <summary>The card that holds the pinned character.</summary>
		private readonly TargetCard pinnedCard = new TargetCard(PINNED_CARD_NAME, isPinned: true);

		/// <summary>Detached icons available for reuse by either card.</summary>
		private readonly List<BuffIcon> iconPool = new List<BuffIcon>();

		/// <summary>
		/// Queries both cards' elements and subscribes to the observed-buff push.
		/// </summary>
		/// <remarks>
		/// Re-runs on every tree rebuild. The pooled icons belong to the tree that was just
		/// replaced, so the pool is dropped rather than reused; the static subscription is removed
		/// before it is added so rebuilds cannot stack handlers.
		/// </remarks>
		public override void OnStarting()
		{
			hoverCard.ActiveBuffIcons.Clear();
			hoverCard.ActiveDebuffIcons.Clear();
			pinnedCard.ActiveBuffIcons.Clear();
			pinnedCard.ActiveDebuffIcons.Clear();
			iconPool.Clear();

			VisualElement root = Root;
			hoverCard.QueryElements(root);
			pinnedCard.QueryElements(root);

			/* There is no level anywhere in the character model — no column, no attribute template,
			 * no broadcast field — so the badge has nothing to show. Hidden rather than left as a
			 * permanently empty box holding layout open. It is wired and ready for the day a level
			 * exists. */
			if (hoverCard.LevelLabel != null)
			{
				hoverCard.LevelLabel.style.display = DisplayStyle.None;
			}
			if (pinnedCard.LevelLabel != null)
			{
				pinnedCard.LevelLabel.style.display = DisplayStyle.None;
			}

			IBuffController.OnObservedBuffsChanged -= BuffController_OnObservedBuffsChanged;
			IBuffController.OnObservedBuffsChanged += BuffController_OnObservedBuffsChanged;
		}

		/// <summary>
		/// Releases the observed-buff subscription and the overhead labels.
		/// </summary>
		public override void OnDestroying()
		{
			IBuffController.OnObservedBuffsChanged -= BuffController_OnObservedBuffsChanged;

			ReleaseOverheadLabel(hoverCard);
			ReleaseOverheadLabel(pinnedCard);

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
				Unsubscribe(targetController);
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
				Subscribe(targetController);
			}
		}

		/// <summary>
		/// Unsubscribes from the target controller and releases both cards.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character != null &&
				Character.TryGet(out ITargetController targetController))
			{
				Unsubscribe(targetController);
			}

			ReleaseCard(hoverCard, null);
			ReleaseCard(pinnedCard, null);
		}

		/// <inheritdoc />
		public override void OnQuitToLogin()
		{
			ReleaseCard(hoverCard, null);
			ReleaseCard(pinnedCard, null);

			base.OnQuitToLogin();
		}

		/// <summary>Attaches every handler this panel needs on the controller.</summary>
		private void Subscribe(ITargetController targetController)
		{
			targetController.OnChangeTarget += TargetController_OnChangeTarget;
			targetController.OnUpdateTarget += TargetController_OnUpdateTarget;
			targetController.OnClearTarget += TargetController_OnClearTarget;
			targetController.OnPinTarget += TargetController_OnPinTarget;
			targetController.OnUnpinTarget += TargetController_OnUnpinTarget;
		}

		/// <summary>Detaches every handler <see cref="Subscribe"/> attached.</summary>
		private void Unsubscribe(ITargetController targetController)
		{
			targetController.OnChangeTarget -= TargetController_OnChangeTarget;
			targetController.OnUpdateTarget -= TargetController_OnUpdateTarget;
			targetController.OnClearTarget -= TargetController_OnClearTarget;
			targetController.OnPinTarget -= TargetController_OnPinTarget;
			targetController.OnUnpinTarget -= TargetController_OnUnpinTarget;
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
		/// Re-reads the pinned card's health and animates both cards' remaining-duration fills.
		/// </summary>
		/// <remarks>
		/// The pinned card has no update event: the controller only re-reports what the pointer
		/// is on. Its health is therefore polled here, and <see cref="RefreshHealth"/> makes that
		/// two integer comparisons per frame until a number actually moves. The buff countdown is
		/// local for both cards — the server pushes an observed-buff list only when the SET
		/// changes, so the entries carry the seconds remaining at send time and this subtracts
		/// the time elapsed since. Skipped entirely when nothing is on screen.
		/// </remarks>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			if (pinnedCard.HasTarget && RefreshHealth(pinnedCard))
			{
				ApplyHealthElements(pinnedCard);
			}

			TickBuffFills(hoverCard);
			TickBuffFills(pinnedCard);
		}

		/// <summary>Advances one card's depleting buff fills.</summary>
		/// <param name="card">The card whose strips to animate.</param>
		private static void TickBuffFills(TargetCard card)
		{
			if (card.ObservedBuffModel.Count == 0)
			{
				return;
			}

			float elapsed = Time.unscaledTime - card.ObservedBuffCaptureTime;

			for (int i = 0; i < card.ObservedBuffModel.Count; ++i)
			{
				ObservedBuffEntry entry = card.ObservedBuffModel[i];
				if (entry.TotalSeconds <= 0.0f)
				{
					continue;
				}

				float remaining = entry.RemainingSeconds - elapsed;
				float fraction = Mathf.Clamp01(remaining / entry.TotalSeconds);

				BuffIcon icon = FindIcon(card, entry.TemplateID);
				if (icon?.Fill != null)
				{
					icon.Fill.style.height = Length.Percent(fraction * 100.0f);
				}
			}
		}

		#region Hover card drivers

		/// <summary>
		/// Frames a newly hovered target in the hover card.
		/// </summary>
		/// <param name="target">The new target transform.</param>
		public void TargetController_OnChangeTarget(Transform target)
		{
			if (target == null || UIManager.ControlHasFocus())
			{
				ReleaseCard(hoverCard, null);
				return;
			}

			/* The pinned card already frames this character. The hover card stands down rather
			 * than duplicating it: the controller's clear event for the previous hover target has
			 * already run, so there is nothing to take down but the card itself. */
			if (ReferenceEquals(target, pinnedCard.Target))
			{
				ReleaseCard(hoverCard, hoverCard.Target);
				return;
			}

			if (!ResolveCard(hoverCard, target))
			{
				return;
			}

			PresentCard(hoverCard);
			UpdateOverheadLabel(hoverCard);
		}

		/// <summary>
		/// Handles a hover update — the same target, re-reported on the controller's poll.
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
				ReleaseCard(hoverCard, null);
				return;
			}

			if (ReferenceEquals(target, pinnedCard.Target))
			{
				// Resting on the pinned character: its card is the pinned one.
				if (hoverCard.HasTarget)
				{
					ReleaseCard(hoverCard, hoverCard.Target);
				}
				return;
			}

			if (!ReferenceEquals(target, hoverCard.Target))
			{
				// The controller reported an update for something we have not resolved yet —
				// including the character that was pinned a moment ago and has just been released.
				TargetController_OnChangeTarget(target);
				return;
			}

			if (RefreshHealth(hoverCard))
			{
				ApplyHealthElements(hoverCard);
			}
		}

		/// <summary>
		/// Releases the hover card when the pointer leaves its target.
		/// </summary>
		/// <param name="lastTarget">The previous target transform, if any.</param>
		public void TargetController_OnClearTarget(Transform lastTarget = null)
		{
			ReleaseCard(hoverCard, lastTarget);
		}

		#endregion

		#region Pinned card drivers

		/// <summary>
		/// Frames a newly pinned character in the pinned card.
		/// </summary>
		/// <param name="target">The pinned transform.</param>
		private void TargetController_OnPinTarget(Transform target)
		{
			if (target == null)
			{
				return;
			}

			if (!ResolveCard(pinnedCard, target))
			{
				return;
			}

			/* The player pinned the character the hover card was showing — the usual case, since
			 * the key pins whatever is under the pointer. The hover card hands over rather than
			 * duplicating; resolved into the pinned card FIRST so the release below sees the
			 * character as still framed and leaves its nameplate up. */
			if (ReferenceEquals(hoverCard.Target, target))
			{
				ReleaseCard(hoverCard, target);
			}

			PresentCard(pinnedCard);
			ShowCharacterLabels(pinnedCard.Character, pinnedCard.DisplayColor);
		}

		/// <summary>
		/// Releases the pinned card. The pointer may still be resting on the character that was
		/// just released; the hover card stood down while it was pinned, and the controller's
		/// next update re-frames it there.
		/// </summary>
		/// <param name="lastTarget">The released transform, or null when it was destroyed.</param>
		private void TargetController_OnUnpinTarget(Transform lastTarget)
		{
			ReleaseCard(pinnedCard, lastTarget);
		}

		#endregion

		/// <summary>
		/// Re-renders the observed buff strips when the server pushes a new list for a framed
		/// character.
		/// </summary>
		/// <param name="buffController">The controller whose observed list changed.</param>
		private void BuffController_OnObservedBuffsChanged(IBuffController buffController)
		{
			/* Static event: fires for every character on the client. Only the framed ones matter.
			 * This is the same class of bug the local buff strips had — an unscoped static buff
			 * event painting every character's buffs onto one panel. */
			if (buffController == null)
			{
				return;
			}

			if (ReferenceEquals(buffController, hoverCard.Buffs))
			{
				CaptureObservedBuffs(hoverCard);
				ApplyBuffElements(hoverCard);
			}
			if (ReferenceEquals(buffController, pinnedCard.Buffs))
			{
				CaptureObservedBuffs(pinnedCard);
				ApplyBuffElements(pinnedCard);
			}
		}

		/// <summary>
		/// Resolves a target into a card: the components that decide what the card can show,
		/// the name and faction colour, the first health read and the buff capture.
		/// </summary>
		/// <param name="card">The card to fill.</param>
		/// <param name="target">The transform to frame.</param>
		/// <returns>False when the target is nothing a card can describe; the card is untouched.</returns>
		private bool ResolveCard(TargetCard card, Transform target)
		{
			/* Resolved ONCE per target. Seven GetComponent calls is a fine price for a target
			 * change and an absurd one twenty times a second, which is what the update path used
			 * to pay by routing straight back into this method. */
			ICharacterAttributeController characterAttributeController = target.GetComponent<ICharacterAttributeController>();
			IBuffController buffController = target.GetComponent<IBuffController>();
			/* Resolved through the shared rule, not a raw GetComponent. The target frame and the
			 * interact key have to agree about which component the player is looking at: a dead
			 * merchant carries both a Merchant and the NPC that is its own corpse, and
			 * PlayerInputController already resolves that pair through InteractableResolver. With
			 * a raw GetComponent here the frame could name and describe the shop while pressing
			 * the key looted the body. */
			IInteractable interactable = InteractableResolver.Resolve(target.gameObject);
			ICharacter character = target.GetComponent<ICharacter>();
			SceneTeleporter teleporter = target.GetComponent<SceneTeleporter>();
			SceneObjectNamer sceneObjectNamer = target.GetComponent<SceneObjectNamer>();

			if (interactable == null &&
				character == null &&
				teleporter == null &&
				characterAttributeController == null &&
				sceneObjectNamer == null)
			{
				return false;
			}

			card.Target = target;
			card.Attributes = characterAttributeController;
			card.Buffs = buffController;
			card.Character = character;
			card.Interactable = interactable;

			card.DisplayColor = Color.white;
			card.DisplayFaction = string.Empty;

			if (character != null &&
				Character != null &&
				Character.TryGet(out IFactionController factionController) &&
				character.TryGet(out IFactionController targetFactionController))
			{
				card.DisplayColor = factionController.GetAllianceLevelColor(targetFactionController);
				card.DisplayFaction = factionController.GetAllianceLevel(targetFactionController).ToString();
			}

			card.DisplayName = interactable != null
				? interactable.Name
				: target.name.Replace("(Clone)", string.Empty);

			/* Whether a health bar is shown follows from whether the target actually has a health
			 * resource, rather than from what kind of thing it is. A portal or a signpost is
			 * worth targeting and naming but has no health to show, and an empty bar reads as a
			 * dead one. Anything that does gain health later — a destructible structure, a siege
			 * engine — starts showing a bar the moment it has the attribute, with no change
			 * here. */
			card.LastHealthCurrent = int.MinValue;
			card.LastHealthMax = int.MinValue;
			RefreshHealth(card);

			CaptureObservedBuffs(card);
			return true;
		}

		/// <summary>
		/// Puts a freshly resolved card on screen.
		/// </summary>
		/// <param name="card">The card that was just resolved.</param>
		/// <remarks>
		/// Show() re-clones the tree, so the card's state must be written AFTER it. OnAfterShow
		/// does exactly that, for both cards; ApplyCardState covers the already-visible case.
		/// </remarks>
		private void PresentCard(TargetCard card)
		{
			if (!Visible)
			{
				Show();
			}
			else
			{
				ApplyCardState(card);
			}
		}

		/// <summary>
		/// Copies a framed character's server-filtered buff list into the card's model.
		/// </summary>
		/// <param name="card">The card to capture into.</param>
		private void CaptureObservedBuffs(TargetCard card)
		{
			card.ObservedBuffModel.Clear();
			card.ObservedBuffCaptureTime = Time.unscaledTime;

			/* Built from the target's real buff container. Every peer now holds the same entries —
			 * an observer materialises them from the server's message and counts them down from its
			 * own TimeManager — so there is no separate display list to read, and the remaining
			 * seconds below are computed against THIS client's tick rather than re-based by however
			 * long ago a message arrived. */
			if (card.Buffs?.Buffs == null)
			{
				return;
			}

			uint currentTick = card.Buffs.GetCurrentDomainTick();

			foreach (Buff buff in card.Buffs.Buffs.Values)
			{
				BaseBuffTemplate template = buff?.Template;
				if (template == null)
				{
					continue;
				}

				card.ObservedBuffModel.Add(new ObservedBuffEntry()
				{
					TemplateID = template.ID,
					Stacks = buff.Stacks,
					RemainingSeconds = buff.RemainingSeconds(currentTick),
					TotalSeconds = template.Duration,
				});
			}
		}

		/// <summary>
		/// Re-reads a card's health, reporting whether the displayed numbers changed.
		/// </summary>
		/// <param name="card">The card to refresh.</param>
		/// <returns>True if anything needs repainting.</returns>
		private bool RefreshHealth(TargetCard card)
		{
			CharacterResourceAttribute resource = default;
			bool nowHasHealth = card.Attributes != null &&
				card.Attributes.TryGetResourceAttribute(HealthAttributeID, out resource);

			if (!nowHasHealth)
			{
				bool changed = card.HasHealth;
				card.HasHealth = false;
				card.HealthFraction = 0.0f;
				card.HealthValueText = string.Empty;
				card.LastHealthCurrent = int.MinValue;
				card.LastHealthMax = int.MinValue;
				return changed;
			}

			int current = Mathf.RoundToInt(resource.CurrentValue);
			int max = resource.FinalValue;

			if (card.HasHealth && current == card.LastHealthCurrent && max == card.LastHealthMax)
			{
				return false;
			}

			card.HasHealth = true;
			card.LastHealthCurrent = current;
			card.LastHealthMax = max;
			card.HealthFraction = max > 0 ? Mathf.Clamp01(resource.CurrentValue / resource.FinalValueAsFloat) : 0.0f;
			card.HealthValueText = current + "/" + max;
			return true;
		}

		/// <summary>
		/// Writes both cards' tracked state into the current visual tree.
		/// </summary>
		/// <remarks>
		/// Called from <see cref="OnAfterShow"/> and <see cref="OnAfterStarting"/>. On a panel's
		/// very first open <c>hasStarted</c> is still false, so <c>ReinitializeIfTreeReplaced</c>
		/// bails out and only <c>OnAfterShow</c> runs; on later shows the tree may genuinely have
		/// been replaced and both fire. Writing the same state twice is harmless.
		/// </remarks>
		private void ApplyTargetState()
		{
			ApplyCardState(hoverCard);
			ApplyCardState(pinnedCard);
		}

		/// <summary>
		/// Writes one card's tracked state into its elements, and shows or hides the card by
		/// whether it frames anything.
		/// </summary>
		/// <param name="card">The card to paint.</param>
		private void ApplyCardState(TargetCard card)
		{
			if (card.Root != null)
			{
				card.Root.style.display = card.HasTarget ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (!card.HasTarget)
			{
				return;
			}

			if (card.NameLabel != null)
			{
				card.NameLabel.text = card.DisplayName;
				card.NameLabel.style.color = card.DisplayColor;
			}

			if (card.FactionLabel != null)
			{
				card.FactionLabel.text = card.DisplayFaction;
				card.FactionLabel.style.display = string.IsNullOrEmpty(card.DisplayFaction)
					? DisplayStyle.None
					: DisplayStyle.Flex;
			}

			ApplyHealthElements(card);
			ApplyBuffElements(card);
		}

		/// <summary>
		/// Writes a card's tracked health values into its health elements.
		/// </summary>
		/// <param name="card">The card to paint.</param>
		private static void ApplyHealthElements(TargetCard card)
		{
			if (card.HealthBar != null)
			{
				card.HealthBar.style.display = card.HasHealth ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (card.HealthFill != null)
			{
				card.HealthFill.style.width = Length.Percent(Mathf.Clamp01(card.HealthFraction) * 100.0f);
			}
			if (card.HealthText != null)
			{
				// Was declared in the UXML and never written; the numbers were concatenated onto
				// the name label instead, which is what made the name allocate every tick.
				card.HealthText.text = card.HealthValueText;
			}
		}

		/// <summary>
		/// Reconciles a card's pooled buff/debuff icons with its observed-buff model.
		/// </summary>
		/// <param name="card">The card to paint.</param>
		/// <remarks>
		/// Icons are POOLED, and the pool is shared by both cards. A target swap detaches the
		/// icons the previous target was using and re-binds them, so switching targets in a
		/// crowded fight allocates nothing: no <c>VisualElement</c>, no callback closure, no
		/// style object. Only a pair of targets carrying more buffs than any pair so far grows the
		/// pool.
		/// </remarks>
		private void ApplyBuffElements(TargetCard card)
		{
			if (card.BuffList == null || card.DebuffList == null)
			{
				return;
			}

			ReleaseIcons(card.ActiveBuffIcons);
			ReleaseIcons(card.ActiveDebuffIcons);

			float elapsed = Time.unscaledTime - card.ObservedBuffCaptureTime;

			for (int i = 0; i < card.ObservedBuffModel.Count; ++i)
			{
				ObservedBuffEntry entry = card.ObservedBuffModel[i];

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
					card.DebuffList.Add(icon.Root);
					card.ActiveDebuffIcons.Add(icon);
				}
				else
				{
					card.BuffList.Add(icon.Root);
					card.ActiveBuffIcons.Add(icon);
				}
			}

			card.BuffList.style.display = card.ActiveBuffIcons.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			card.DebuffList.style.display = card.ActiveDebuffIcons.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>
		/// Finds a card's rendered icon for a template ID, or null.
		/// </summary>
		/// <param name="card">The card whose strips to search.</param>
		/// <param name="templateID">The buff template ID.</param>
		private static BuffIcon FindIcon(TargetCard card, int templateID)
		{
			for (int i = 0; i < card.ActiveBuffIcons.Count; ++i)
			{
				if (card.ActiveBuffIcons[i].Template != null && card.ActiveBuffIcons[i].Template.ID == templateID)
				{
					return card.ActiveBuffIcons[i];
				}
			}
			for (int i = 0; i < card.ActiveDebuffIcons.Count; ++i)
			{
				if (card.ActiveDebuffIcons[i].Template != null && card.ActiveDebuffIcons[i].Template.ID == templateID)
				{
					return card.ActiveDebuffIcons[i];
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
		private static void BindIcon(BuffIcon icon, BaseBuffTemplate template, int stacks, float fraction)
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
		/// Drops a card's framed target, releases its overhead labels, and hides the card — or
		/// the whole frame, when the other card is empty too.
		/// </summary>
		/// <param name="card">The card to release.</param>
		/// <param name="lastTarget">The previous target transform, if it is still alive.</param>
		/// <remarks>
		/// <para>
		/// <paramref name="lastTarget"/> is tested with Unity's overloaded <c>!=</c>, which reports
		/// a destroyed object as null — so a target that died rather than being deselected takes
		/// the null path and no <c>GetComponent</c> is attempted on a destroyed object. The card
		/// comes down either way, which is the whole point: it used to stay on the corpse.
		/// </para>
		/// <para>
		/// A character the OTHER card still frames keeps its nameplate and outline: the pointer
		/// leaving the pinned character must not take down labels the pin is holding up, and
		/// releasing a pin while the pointer rests on that character must not either.
		/// </para>
		/// </remarks>
		private void ReleaseCard(TargetCard card, Transform lastTarget)
		{
			if (lastTarget != null)
			{
				TargetCard other = ReferenceEquals(card, hoverCard) ? pinnedCard : hoverCard;
				bool framedElsewhere = ReferenceEquals(other.Target, lastTarget);

				if (!framedElsewhere)
				{
					Outline outline = lastTarget.GetComponent<Outline>();
					if (outline != null)
					{
						outline.enabled = false;
					}

					ICharacter character = lastTarget.GetComponent<ICharacter>();
					if (character != null)
					{
						/* One rule for "does this nameplate stay up when it stops being the
						 * target", shared with the sweep that puts nameplates up in the first
						 * place. It used to be answered here alone — own character, own pet —
						 * and once NPCs in range kept theirs too, answering it in two places meant
						 * an untargeted NPC beside the player blinked off for a sweep before
						 * coming back. */
						bool keepLabels = ClientNameplateDisplay.ShouldStayVisible(character);

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
			}

			/* The original returned EARLY when the last target was the local player or their own
			 * pet, so the overhead 3D label was never released and the frame never hid — it just
			 * stopped updating. Keeping the nameplate visible and taking down the card are
			 * separate decisions; only the first one depends on who the target was. */
			ReleaseOverheadLabel(card);

			card.ClearModel();
			ReleaseIcons(card.ActiveBuffIcons);
			ReleaseIcons(card.ActiveDebuffIcons);

			if (!hoverCard.HasTarget && !pinnedCard.HasTarget)
			{
				Hide();
				return;
			}

			if (Visible)
			{
				ApplyCardState(card);
			}
		}

		/// <summary>
		/// Returns a card's overhead 3D label to the label pool.
		/// </summary>
		/// <param name="card">The card whose label to release.</param>
		private static void ReleaseOverheadLabel(TargetCard card)
		{
			if (card.OverheadLabel != null)
			{
				UITKLabelMaker.Cache(card.OverheadLabel);
				card.OverheadLabel = null;
			}
		}

		/// <summary>
		/// Turns a character's authored nameplates on, in the frame's faction colour.
		/// </summary>
		/// <param name="character">The character, or null for nothing.</param>
		/// <param name="color">The faction colour to draw the name in.</param>
		private static void ShowCharacterLabels(ICharacter character, Color color)
		{
			if (character == null)
			{
				return;
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

		/// <summary>
		/// Updates the overhead 3D label for a card's target: a character's authored nameplates
		/// in its faction colour, or a pooled caption over an interactable.
		/// </summary>
		/// <param name="card">The card whose target to label.</param>
		/// <remarks>
		/// Only reached from the CHANGE path. It destroys and recreates a pooled GameObject
		/// label, and the update path used to run it twenty times a second for a target that had
		/// not moved or changed — a destroy/create cycle per tick of hover.
		/// </remarks>
		private void UpdateOverheadLabel(TargetCard card)
		{
			ReleaseOverheadLabel(card);

			Transform target = card.Target;
			if (target == null)
			{
				return;
			}

			if (card.Character != null)
			{
				Color color = Color.grey;
				if (Character != null &&
					Character.TryGet(out IFactionController factionController) &&
					card.Character.TryGet(out IFactionController targetFactionController))
				{
					color = factionController.GetAllianceLevelColor(targetFactionController);
				}

				ShowCharacterLabels(card.Character, color);
			}
			else if (card.Interactable != null)
			{
				Vector3 newPos = target.position;

				float colliderHeight = 1.0f;

				Collider collider = target.GetComponent<Collider>();
				if (collider != null)
				{
					collider.TryGetDimensions(out colliderHeight, out float radius);
				}

				newPos.y += colliderHeight;

				string label = card.Interactable.Name;

				if (!string.IsNullOrWhiteSpace(card.Interactable.Title))
				{
					string hex = card.Interactable.TitleColor.ToHex();
					if (!string.IsNullOrWhiteSpace(hex))
					{
						label += $"\r\n<<color=#{hex}>{card.Interactable.Title}</color>>";
					}
				}

				card.OverheadLabel = UITKLabelMaker.Display3D(label, newPos, Color.grey, 0.25f, 0.0f, true);
				if (card.OverheadLabel != null && card.OverheadLabel.Label != null)
				{
					/* Pooled labels are ungrouped by default because their transform root is the
					 * pool. This one is pinned over a specific object, so it opts into that
					 * object's nameplate stack explicitly — with a sort order above the authored
					 * nameplates (name 0, guild 10) so the caption stacks on top of them instead
					 * of painting through them. */
					card.OverheadLabel.Label.GroupAnchor = target.root;
					card.OverheadLabel.Label.SortOrder = 100;
				}
			}
		}
	}
}
