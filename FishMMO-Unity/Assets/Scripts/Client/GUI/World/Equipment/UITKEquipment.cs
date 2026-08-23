using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the equipment panel.
	/// Binds to <c>UIEquipment.uxml</c> / <c>UIEquipment.uss</c> and renders the character's
	/// equipped items alongside the attributes they contribute to.
	/// </summary>
	/// <remarks>
	/// This panel renders the server's answer and never its own guess. Every request it sends
	/// leaves the slot looking exactly as it did, marked as waiting, until the equipment container
	/// is replicated back — see <see cref="HandleSlotRightClick"/> for what that replaced and why.
	/// </remarks>
	public class UITKEquipment : UITKCharacterControl
	{
		// ── UXML element names ────────────────────────────────────────────────

		/// <summary>Name of the attribute list ScrollView element in the UXML.</summary>
		private const string ATTR_LIST_NAME     = "attribute-list";
		/// <summary>Name of the preview render texture element in the UXML.</summary>
		private const string PREVIEW_RT_NAME    = "preview-rt";
		/// <summary>Name of the close button element in the UXML.</summary>
		private const string CLOSE_BTN_NAME     = "close-button";
		/// <summary>Name of the gear tab button element in the UXML.</summary>
		private const string TAB_GEAR_NAME      = "tab-gear";
		/// <summary>Name of the stats tab button element in the UXML.</summary>
		private const string TAB_STATS_NAME     = "tab-stats";
		/// <summary>Name of the sets tab button element in the UXML.</summary>
		private const string TAB_SETS_NAME      = "tab-sets";
		/// <summary>Name of the panel body element in the UXML.</summary>
		private const string PANEL_BODY_NAME    = "panel-body";
		/// <summary>Name of the panel footer element in the UXML.</summary>
		private const string PANEL_FOOTER_NAME  = "panel-footer";
		/// <summary>Name of the gear score label element in the UXML.</summary>
		private const string GEAR_SCORE_NAME    = "gear-score";
		/// <summary>Name of the HP stat label element in the UXML.</summary>
		private const string STAT_HP_NAME       = "stat-hp";
		/// <summary>Name of the MP stat label element in the UXML.</summary>
		private const string STAT_MP_NAME       = "stat-mp";
		/// <summary>Name of the stamina stat label element in the UXML.</summary>
		private const string STAT_STAM_NAME     = "stat-stam";

		// ── Shared UI overlay names (panels resolved by GameObject name via UIManager) ──

		/// <summary>Name of the shared drag object overlay.</summary>
		private const string DRAG_OBJECT_NAME = "UIDragObject";
		/// <summary>Name of the shared tooltip overlay.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		/// <summary>
		/// UXML element name for every <see cref="ItemSlot"/>, indexed by its enum value.
		/// </summary>
		/// <remarks>
		/// THIS ARRAY IS A CONTRACT, NOT A CONVENIENCE. <see cref="EquipmentController"/> sizes its
		/// container from <c>Enum.GetNames(typeof(ItemSlot)).Length</c>, so slot <c>i</c> of the
		/// container is <c>(ItemSlot)i</c> and must be drawn by <c>SlotElementNames[i]</c>. Every
		/// enum value needs an entry, in order, and every entry needs an element of that name in
		/// <c>UIEquipment.uxml</c>.
		/// <para>
		/// The comment that used to sit here claimed the array listed "only slots that exist in
		/// both the enum and the UXML", which was untrue in both directions: it named
		/// <c>slot-accessory</c>, which the UXML did not define, while the UXML defined
		/// <c>slot-neck</c> and <c>slot-ring</c>, which are not <see cref="ItemSlot"/> values and
		/// which this array did not name. The result was an <see cref="ItemSlot.Accessory"/> slot
		/// that could never render — <c>root.Q</c> returned null and the per-slot guard quietly
		/// skipped it — and two dead elements the player could click to no effect. Both halves are
		/// fixed; the UXML now declares exactly these ten names.
		/// </para>
		/// </remarks>
		private static readonly string[] SlotElementNames = new[]
		{
			"slot-head",      // ItemSlot.Head      = 0
			"slot-chest",     // ItemSlot.Chest     = 1
			"slot-shoulders", // ItemSlot.Shoulders = 2
			"slot-hands",     // ItemSlot.Hands     = 3
			"slot-legs",      // ItemSlot.Legs      = 4
			"slot-feet",      // ItemSlot.Feet      = 5
			"slot-back",      // ItemSlot.Back      = 6
			"slot-mainhand",  // ItemSlot.Primary   = 7
			"slot-offhand",   // ItemSlot.Secondary = 8
			"slot-accessory", // ItemSlot.Accessory = 9
		};

		// ── USS class names ───────────────────────────────────────────────────

		/// <summary>USS class for hiding equipment elements.</summary>
		private const string CSS_HIDDEN         = "eq-hidden";
		/// <summary>USS class for an active tab button.</summary>
		private const string CSS_TAB_ACTIVE     = "fish-tab--active";
		/// <summary>USS class marking a slot as waiting on the server.</summary>
		private const string CSS_LOCK_PENDING   = "eq-slot__lock--pending";
		/// <summary>USS class for an attribute category header.</summary>
		private const string CSS_ATTR_CATEGORY  = "fish-attr-category";
		/// <summary>USS class for an attribute row.</summary>
		private const string CSS_ATTR_ROW       = "fish-attr-row";
		/// <summary>USS class for an attribute row name label.</summary>
		private const string CSS_ATTR_NAME      = "fish-attr-row__name";
		/// <summary>USS class for an attribute row value label.</summary>
		private const string CSS_ATTR_VALUE     = "fish-attr-row__value";
		/// <summary>USS class for a resource attribute value label.</summary>
		private const string CSS_ATTR_RESOURCE  = "fish-attr-row__value--resource";
		/// <summary>USS class for a percentage attribute value label.</summary>
		private const string CSS_ATTR_PERCENT   = "fish-attr-row__value--percent";

		// ── Per-slot view data ────────────────────────────────────────────────

		/// <summary>Runtime view data for a single equipment slot element.</summary>
		private struct SlotView
		{
			/// <summary>Root VisualElement of the slot (e.g. "slot-head").</summary>
			public VisualElement Root;
			/// <summary>Icon element (fish-slot__icon).</summary>
			public VisualElement Icon;
			/// <summary>Amount label (fish-slot__amount).</summary>
			public Label Amount;
			/// <summary>Lock overlay element (fish-slot__lock).</summary>
			public VisualElement Lock;
		}

		// ── Private state ─────────────────────────────────────────────────────

		/// <summary>Indexed by (int)ItemSlot; null until <see cref="OnStarting"/> has run.</summary>
		private SlotView[] slotViews;

		/// <summary>Live attribute value labels keyed by attribute template ID.</summary>
		private readonly Dictionary<int, Label> attributeValueLabels = new Dictionary<int, Label>();

		/// <summary>Category header elements created at runtime.</summary>
		private readonly List<VisualElement> attributeCategoryElements = new List<VisualElement>();

		/// <summary>Attribute row elements created at runtime.</summary>
		private readonly List<VisualElement> attributeRowElements = new List<VisualElement>();

		/// <summary>
		/// The attributes this panel currently holds a subscription on.
		/// </summary>
		/// <remarks>
		/// Kept as its own list rather than re-derived from the character at unsubscribe time.
		/// <c>DestroyAttributeElements</c> used to walk <c>Character</c>'s attributes to detach —
		/// but on a character change it runs from <c>OnPostSetCharacter</c>, by which point
		/// <c>Character</c> is already the NEW one, so the outgoing character kept every
		/// subscription for the rest of the session and its updates went on repainting a panel
		/// that no longer showed it.
		/// </remarks>
		private readonly List<CharacterAttribute> subscribedAttributes = new List<CharacterAttribute>();

		/// <summary>The ScrollView that contains the attribute rows.</summary>
		private ScrollView attributeList;
		/// <summary>Root element of the gear tab panel body.</summary>
		private VisualElement panelBody;
		/// <summary>Root element of the stats tab panel footer.</summary>
		private VisualElement panelFooter;
		/// <summary>Character preview render texture element.</summary>
		private VisualElement previewRt;
		/// <summary>Label displaying the gear score.</summary>
		private Label gearScoreLabel;
		/// <summary>Label displaying the HP stat value.</summary>
		private Label statHpLabel;
		/// <summary>Label displaying the MP stat value.</summary>
		private Label statMpLabel;
		/// <summary>Label displaying the stamina stat value.</summary>
		private Label statStamLabel;

		/// <summary>Camera used for the 3D character preview viewport.</summary>
		private Camera equipmentViewCamera;

		/// <summary>Currently selected tab name; defaults to GEAR.</summary>
		private string activeTab = TAB_GEAR_NAME;

		/// <summary>True while this panel holds a subscription on the shared operation tracker.</summary>
		private bool trackerSubscribed;

		// ── UITKControl lifecycle ─────────────────────────────────────────────

		/// <summary>
		/// Queries all named elements from the visual tree and wires up button callbacks.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			// Body / footer panels used for tab switching
			panelBody   = root.Q(PANEL_BODY_NAME);
			panelFooter = root.Q(PANEL_FOOTER_NAME);

			// Attribute scroll list
			attributeList = root.Q<ScrollView>(ATTR_LIST_NAME);

			// Status-bar labels
			gearScoreLabel = root.Q<Label>(GEAR_SCORE_NAME);
			statHpLabel    = root.Q<Label>(STAT_HP_NAME);
			statMpLabel    = root.Q<Label>(STAT_MP_NAME);
			statStamLabel  = root.Q<Label>(STAT_STAM_NAME);

			// Character-preview render texture element
			previewRt = root.Q(PREVIEW_RT_NAME);

			// Close button
			Button closeBtn = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeBtn != null)
			{
				closeBtn.clicked += Hide;
			}

			// Tab buttons
			WireTab(root.Q<Button>(TAB_GEAR_NAME),  TAB_GEAR_NAME,  root);
			WireTab(root.Q<Button>(TAB_STATS_NAME), TAB_STATS_NAME, root);
			WireTab(root.Q<Button>(TAB_SETS_NAME),  TAB_SETS_NAME,  root);

			/* Equipment slots. The callbacks below are registered on elements that belong to the
			 * tree being resolved right now — a rebuilt tree brings new elements and the old
			 * handlers go with the old ones, so there is nothing to unregister and no per-rebuild
			 * accumulation. The rebuilt views are captured wholesale into a fresh array for the
			 * same reason: a stale SlotView points into a tree nobody can see. */
			int slotCount = SlotElementNames.Length;
			slotViews = new SlotView[slotCount];
			for (int i = 0; i < slotCount; ++i)
			{
				VisualElement slotRoot = root.Q(SlotElementNames[i]);
				if (slotRoot == null)
				{
					continue;
				}

				SlotView view;
				view.Root   = slotRoot;
				view.Icon   = slotRoot.Q(className: "fish-slot__icon");
				view.Amount = slotRoot.Q<Label>(className: "fish-slot__amount");
				view.Lock   = slotRoot.Q(className: "fish-slot__lock");
				slotViews[i] = view;

				int slotIndex = i;
				slotRoot.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(evt, slotIndex));
				slotRoot.RegisterCallback<PointerUpEvent>(evt => OnSlotPointerUp(evt, slotIndex));
				slotRoot.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(slotIndex, slotRoot));
				slotRoot.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave(slotRoot));
			}

			// Apply initial tab state
			ApplyTabState(root);
		}

		/// <summary>
		/// Re-applies per-open content after the visual tree has been rebuilt.
		/// </summary>
		/// <remarks>
		/// The base implementation re-runs the character pre/post pair, which repopulates the
		/// slots and attribute rows. What it does not carry across is the pending marks, which
		/// live in the shared tracker rather than in the tree, so they have to be repainted onto
		/// the new elements here.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Re-applies per-open content on every show, including the very first one.
		/// </summary>
		/// <remarks>
		/// <c>OnAfterStarting</c> alone is not enough, and this is the trap THE CONTRACT warns
		/// about: on the first ever open <c>hasStarted</c> is still false, so
		/// <c>ReinitializeIfTreeReplaced</c> returns before re-running it. Doing the work from
		/// both hooks is what makes the first open behave like every later one. Both paths are
		/// idempotent — this re-reads state and repaints, it does not accumulate anything.
		/// </remarks>
		protected override void OnAfterShow()
		{
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Registers with the shared item-operation tracker.
		/// </summary>
		public override void OnClientSet()
		{
			SubscribeTracker();
		}

		/// <summary>
		/// Detaches from the shared item-operation tracker.
		/// </summary>
		public override void OnClientUnset()
		{
			UnsubscribeTracker();
		}

		/// <summary>
		/// Times out item operations whose reply never arrived.
		/// </summary>
		/// <remarks>
		/// The tracker is shared and self-clearing, so it does not matter that all three item
		/// panels drive it; whichever ticks first in a frame does the work and the others find
		/// nothing outstanding.
		/// </remarks>
		protected override void OnTick()
		{
			ItemOperationTracker.Tick();
		}

		/// <summary>
		/// Cleans up the camera reference, runtime-created attribute elements and every
		/// subscription this panel holds.
		/// </summary>
		public override void OnDestroying()
		{
			UnsubscribeTracker();
			ReleaseAndClearDrag();

			if (Character != null && Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated     -= OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged -= OnEquipmentSlotLockChanged;
			}

			equipmentViewCamera = null;
			DestroyAttributeElements();
			base.OnDestroying();
		}

		// ── Visibility overrides (camera sync) ───────────────────────────────

		/// <summary>
		/// Shows the equipment panel and enables the equipment-view camera if assigned.
		/// </summary>
		public override void Show()
		{
			base.Show();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(true);
			}
		}

		/// <summary>
		/// Hides the equipment panel, disables the preview camera and abandons anything this
		/// panel had in flight.
		/// </summary>
		/// <remarks>
		/// <c>Hide(bool)</c> and not <c>Hide()</c>: <c>Hide()</c> delegates here, but Escape
		/// (<c>UIManager.CloseNext</c>) and quit-to-login (<c>Hide(false)</c>) both arrive at this
		/// overload directly. A pending mark or a half-finished drag that outlives the panel is
		/// invisible to the player and refuses their next click for no stated reason.
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (Visible)
			{
				return;
			}

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(false);
			}

			ReleaseAndClearDrag();
		}

		/// <summary>
		/// Toggles the panel and syncs the camera accordingly.
		/// </summary>
		public override void ToggleVisibility()
		{
			base.ToggleVisibility();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(Visible);
			}
		}

		// ── Character control ─────────────────────────────────────────────────

		/// <summary>
		/// Unsubscribes from equipment slot events before replacing the character.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated     -= OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged -= OnEquipmentSlotLockChanged;
			}

			/* Detach the attribute subscriptions while Character still points at the character
			 * that owns them. Doing it in OnPostSetCharacter, as this used to, looked up the
			 * attributes of the INCOMING character and left the outgoing one wired to this panel
			 * forever. */
			UnsubscribeAttributes();
		}

		/// <summary>
		/// Subscribes to equipment slot events, refreshes all slot visuals, and builds
		/// the attribute row list for the newly set character.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			DestroyAttributeElements();
			ResetStatusBar();

			if (Character == null)
			{
				return;
			}

			// ── Equipment slots ───────────────────────────────────────────────
			if (Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated     -= OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged -= OnEquipmentSlotLockChanged;

				RefreshAllSlots(equipmentController);

				equipmentController.OnSlotUpdated     += OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged += OnEquipmentSlotLockChanged;
			}

			// ── Character attributes ──────────────────────────────────────────
			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				BuildAttributeRows(attributeController);
				UpdateStatusBar(attributeController);
			}
		}

		/// <summary>
		/// Drops every subscription and in-flight operation before the character goes away.
		/// </summary>
		/// <remarks>
		/// Quit-to-login and a character switch both come through here. Without it the panel keeps
		/// its handlers on a character that is being destroyed, keeps equipment slots marked as
		/// waiting on a server it is no longer talking to, and can leave a drag armed with a slot
		/// index that means something entirely different to the next character.
		/// </remarks>
		public override void OnPreUnsetCharacter()
		{
			if (Character != null && Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated     -= OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged -= OnEquipmentSlotLockChanged;
			}

			UnsubscribeAttributes();
			ReleaseAndClearDrag();
		}

		// ── Equipment slot callbacks ──────────────────────────────────────────

		/// <summary>
		/// Called when the lock state of an equipment slot changes.
		/// </summary>
		public void OnEquipmentSlotLockChanged(IItemContainer container, int slot, bool isLocked)
		{
			/* slotViews is null until OnStarting has seen a populated tree, and a panel that
			 * starts hidden is handed a character — and therefore these events — long before
			 * that. Reading .Length first was an NRE on the first equip of every session in which
			 * the player had not opened this window. */
			if (slotViews == null || slot < 0 || slot >= slotViews.Length)
			{
				return;
			}

			/* IsSlotBlocked rather than the event's own isLocked: the container unlocking a slot
			 * says nothing about a request this panel is still waiting on, and taking the flag at
			 * face value would clear the overlay out from under one. */
			ApplySlotLockVisual(slot, IsSlotBlocked(slot));
		}

		/// <summary>
		/// Called when an equipment slot's item changes.
		/// </summary>
		/// <remarks>
		/// This is the panel's only source of truth about a slot and, for an operation this panel
		/// requested, its acknowledgement. Releasing the pending mark here rather than on a reply
		/// message is deliberate: what the player is waiting to see is the slot, and the slot
		/// arriving IS the reply.
		/// </remarks>
		public void OnEquipmentSlotUpdated(IItemContainer container, Item item, int equipmentSlot)
		{
			if (container == null || slotViews == null ||
				equipmentSlot < 0 || equipmentSlot >= slotViews.Length)
			{
				return;
			}

			ItemOperationTracker.Release(ReferenceButtonType.Equipment, equipmentSlot);

			bool empty = container.IsSlotEmpty(equipmentSlot);
			if (!empty)
			{
				SetSlotItem(equipmentSlot, item);
			}
			else
			{
				ClearSlot(equipmentSlot);
			}

			// A drag started from this slot no longer refers to what it was started from.
			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				dragObject.NotifySlotChanged(ReferenceButtonType.Equipment, equipmentSlot, empty ? null : item);
			}
		}

		// ── Attribute update callbacks ────────────────────────────────────────

		/// <summary>
		/// Refreshes the attribute label for an updated attribute.
		/// </summary>
		public void OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (!attributeValueLabels.TryGetValue(attribute.Template.ID, out Label valueLabel))
			{
				return;
			}

			if (attribute.Template.IsResourceAttribute)
			{
				CharacterResourceAttribute resource = attribute as CharacterResourceAttribute;
				if (resource != null)
				{
					valueLabel.text = Mathf.RoundToInt(resource.CurrentValue) + " / " + resource.FinalValue;
					UpdateStatusBarChip(attribute.Template.Name, valueLabel.text);
				}
			}
			else
			{
				valueLabel.text = attribute.Template.IsPercentage
					? attribute.FinalValue + "%"
					: attribute.FinalValue.ToString();
			}
		}

		// ── Camera ────────────────────────────────────────────────────────────

		/// <summary>
		/// Assigns the camera used for the 3D character preview viewport.
		/// Pass null to clear the reference.
		/// </summary>
		/// <param name="camera">Camera to use for preview rendering.</param>
		public void SetEquipmentViewCamera(Camera camera)
		{
			equipmentViewCamera = camera;
		}

		/// <summary>
		/// Assigns a RenderTexture to the preview-rt element so the camera feed is
		/// visible inside the UXML viewport.
		/// </summary>
		/// <param name="rt">The render texture to display.</param>
		public void SetPreviewRenderTexture(RenderTexture rt)
		{
			if (previewRt == null)
			{
				return;
			}

			previewRt.style.backgroundImage = rt != null
				? new StyleBackground(Background.FromRenderTexture(rt))
				: StyleKeyword.None;
		}

		// ── Shared operation tracker ──────────────────────────────────────────

		/// <summary>
		/// Joins the shared item-operation tracker, once.
		/// </summary>
		private void SubscribeTracker()
		{
			if (trackerSubscribed)
			{
				return;
			}
			trackerSubscribed = true;

			/* -= before += on a static event. OnClientSet can run more than once in a session
			 * (quit to login does SetClient(null) then SetClient(client)), and a static event
			 * outlives this component, so a missed unsubscribe is a handler running forever on a
			 * destroyed panel. */
			ItemOperationTracker.SlotPendingChanged -= OnTrackerSlotPendingChanged;
			ItemOperationTracker.SlotPendingChanged += OnTrackerSlotPendingChanged;
			ItemOperationTracker.ResyncRequested    -= OnTrackerResyncRequested;
			ItemOperationTracker.ResyncRequested    += OnTrackerResyncRequested;
			ItemOperationTracker.Attach();
		}

		/// <summary>
		/// Leaves the shared item-operation tracker, once.
		/// </summary>
		private void UnsubscribeTracker()
		{
			if (!trackerSubscribed)
			{
				return;
			}
			trackerSubscribed = false;

			ItemOperationTracker.SlotPendingChanged -= OnTrackerSlotPendingChanged;
			ItemOperationTracker.ResyncRequested    -= OnTrackerResyncRequested;
			ItemOperationTracker.Detach();
		}

		/// <summary>
		/// Repaints a slot when it starts or stops waiting on the server.
		/// </summary>
		private void OnTrackerSlotPendingChanged(ReferenceButtonType type, int slot, bool pending)
		{
			if (type != ReferenceButtonType.Equipment || slotViews == null ||
				slot < 0 || slot >= slotViews.Length)
			{
				return;
			}

			ApplySlotLockVisual(slot, IsSlotBlocked(slot));
		}

		/// <summary>
		/// Re-renders every slot from the replicated container.
		/// </summary>
		/// <remarks>
		/// Raised when the server said the outcome of an operation is unknown, which is not the
		/// same as saying it failed — see <c>ItemOperationFailureReason.ServerBusy</c>. Nothing is
		/// reverted; the container is simply read again.
		/// </remarks>
		private void OnTrackerResyncRequested(ReferenceButtonType type)
		{
			if (type != ReferenceButtonType.Equipment)
			{
				return;
			}

			if (Character != null && Character.TryGet(out IEquipmentController equipmentController))
			{
				RefreshAllSlots(equipmentController);
			}
		}

		// ── Private helpers ───────────────────────────────────────────────────

		/// <summary>
		/// Re-reads everything this panel shows from the character, without rebuilding the tree.
		/// </summary>
		private void ApplyPerOpenContent()
		{
			if (slotViews == null || Character == null)
			{
				return;
			}

			if (Character.TryGet(out IEquipmentController equipmentController))
			{
				RefreshAllSlots(equipmentController);
			}

			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				UpdateStatusBar(attributeController);
			}
		}

		/// <summary>
		/// Repaints every slot's item and lock state from the container.
		/// </summary>
		private void RefreshAllSlots(IEquipmentController container)
		{
			if (slotViews == null || container == null)
			{
				return;
			}

			for (int i = 0; i < slotViews.Length; ++i)
			{
				if (slotViews[i].Root == null)
				{
					continue;
				}
				RefreshSlot(container, i);
				ApplySlotLockVisual(i, IsSlotBlocked(i));
			}
		}

		/// <summary>
		/// Reports whether a slot is unavailable for a new request, for any reason.
		/// </summary>
		private bool IsSlotBlocked(int slotIndex)
		{
			if (ItemOperationTracker.IsPending(ReferenceButtonType.Equipment, slotIndex))
			{
				return true;
			}

			return Character != null &&
				   Character.TryGet(out IEquipmentController equipmentController) &&
				   equipmentController.IsSlotLocked(slotIndex);
		}

		/// <summary>
		/// Wires a tab button's clicked callback and initialises its active CSS class.
		/// </summary>
		private void WireTab(Button button, string tabName, VisualElement root)
		{
			if (button == null)
			{
				return;
			}
			button.clicked += () =>
			{
				activeTab = tabName;
				ApplyTabState(root);
			};
		}

		/// <summary>
		/// Applies the active/inactive CSS class to all tab buttons and shows or hides
		/// the panel body and attribute footer according to the active tab.
		/// </summary>
		private void ApplyTabState(VisualElement root)
		{
			SetTabActive(root.Q<Button>(TAB_GEAR_NAME),  activeTab == TAB_GEAR_NAME);
			SetTabActive(root.Q<Button>(TAB_STATS_NAME), activeTab == TAB_STATS_NAME);
			SetTabActive(root.Q<Button>(TAB_SETS_NAME),  activeTab == TAB_SETS_NAME);

			// GEAR  → show slot grid, hide attribute scroll
			// STATS → hide slot grid, show attribute scroll
			// SETS  → hide both (placeholder)
			bool showBody   = activeTab == TAB_GEAR_NAME;
			bool showFooter = activeTab == TAB_STATS_NAME;

			SetElementVisible(panelBody,   showBody);
			SetElementVisible(panelFooter, showFooter);
		}

		/// <summary>Adds or removes the active styling class on a tab button.</summary>
		/// <param name="button">The tab button to style.</param>
		/// <param name="active">Whether the tab is the active tab.</param>
		private static void SetTabActive(Button button, bool active)
		{
			if (button == null)
			{
				return;
			}
			button.EnableInClassList(CSS_TAB_ACTIVE, active);
		}

		/// <summary>
		/// Shows or hides a VisualElement using the display style (no layout space when hidden).
		/// </summary>
		private static void SetElementVisible(VisualElement element, bool visible)
		{
			if (element == null)
			{
				return;
			}
			element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>
		/// Refreshes a slot's icon and amount display from the equipment container.
		/// </summary>
		private void RefreshSlot(IEquipmentController container, int slotIndex)
		{
			if (container.TryGetItem(slotIndex, out Item item))
			{
				SetSlotItem(slotIndex, item);
			}
			else
			{
				ClearSlot(slotIndex);
			}
		}

		/// <summary>
		/// Populates a slot's icon and stack-count badge.
		/// </summary>
		private void SetSlotItem(int slotIndex, Item item)
		{
			if (item == null || slotViews == null || slotIndex < 0 || slotIndex >= slotViews.Length)
			{
				return;
			}

			ref SlotView view = ref slotViews[slotIndex];
			if (view.Root == null)
			{
				return;
			}

			// Placeholder when the template has no icon: an occupied slot must look occupied.
			UITKItemIcon.Apply(view.Icon, item.Template != null ? item.Template.Icon : null);

			if (view.Amount != null)
			{
				if (item.IsStackable && item.Stackable != null)
				{
					view.Amount.text = item.Stackable.Amount.ToString();
					view.Amount.RemoveFromClassList(CSS_HIDDEN);
				}
				else
				{
					view.Amount.text = "";
					view.Amount.AddToClassList(CSS_HIDDEN);
				}
			}
		}

		/// <summary>
		/// Clears a slot's icon and hides the stack-count badge.
		/// </summary>
		private void ClearSlot(int slotIndex)
		{
			if (slotViews == null || slotIndex < 0 || slotIndex >= slotViews.Length)
			{
				return;
			}

			ref SlotView view = ref slotViews[slotIndex];
			if (view.Root == null)
			{
				return;
			}

			UITKItemIcon.Clear(view.Icon);
			if (view.Amount != null)
			{
				view.Amount.text = "";
				view.Amount.AddToClassList(CSS_HIDDEN);
			}
		}

		/// <summary>
		/// Shows or hides the lock overlay on a slot.
		/// </summary>
		/// <remarks>
		/// The same overlay carries two meanings — the container's own slot lock and a request
		/// this panel is waiting on — because to the player they are the same statement: this slot
		/// is busy, do not click it. The <c>--pending</c> modifier distinguishes them visually
		/// without needing a second element in every slot.
		/// </remarks>
		private void ApplySlotLockVisual(int slotIndex, bool isLocked)
		{
			if (slotViews == null || slotIndex < 0 || slotIndex >= slotViews.Length)
			{
				return;
			}

			VisualElement lockEl = slotViews[slotIndex].Lock;
			if (lockEl == null)
			{
				return;
			}

			lockEl.EnableInClassList(CSS_HIDDEN, !isLocked);
			lockEl.EnableInClassList(CSS_LOCK_PENDING,
				isLocked && ItemOperationTracker.IsPending(ReferenceButtonType.Equipment, slotIndex));
		}

		/// <summary>
		/// Handles pointer-down events on an equipment slot element.
		/// Left button: drag-and-drop equip or start drag.
		/// Right button: unequip.
		/// </summary>
		/// <summary>
		/// Completes a press-and-drag when the pointer is released over an equipment slot.
		/// </summary>
		/// <remarks>
		/// See UITKInventory.OnSlotPointerUp — the same missing half. Releasing over the slot the
		/// drag started from is a click, not a drop, and is left alone so click-to-pick-up from
		/// an equipment slot still works.
		/// </remarks>
		private void OnSlotPointerUp(PointerUpEvent evt, int slotIndex)
		{
			if (Character == null || Client == null || evt.button != 0)
			{
				return;
			}

			bool draggingNow = UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) && dragObject.IsDragging;

			if (!draggingNow)
			{
				return;
			}

			if (dragObject.Type == ReferenceButtonType.Equipment &&
				(int)dragObject.ReferenceID == slotIndex)
			{
				return;
			}

			if (Character.TryGet(out IEquipmentController equipmentController))
			{
				CompleteDropOntoSlot(dragObject, equipmentController, slotIndex);
			}
		}

		private void OnSlotPointerDown(PointerDownEvent evt, int slotIndex)
		{
			if (Character == null || Client == null)
			{
				return;
			}

			if (evt.button == 0) // left
			{
				HandleSlotLeftClick(slotIndex);
			}
			else if (evt.button == 1) // right
			{
				HandleSlotRightClick(slotIndex);
			}
		}

		/// <summary>
		/// Shows the item tooltip when the pointer enters an equipment slot that contains an item.
		/// </summary>
		private void OnSlotPointerEnter(int slotIndex, VisualElement owner)
		{
			if (Character == null ||
				!Character.TryGet(out IEquipmentController equipmentController) ||
				!equipmentController.TryGetItem(slotIndex, out Item item))
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				// With an owner, so the tooltip closes itself if this slot is rebuilt under it.
				tooltip.Open(item.Tooltip(), owner);
			}
		}

		/// <summary>
		/// Hides the item tooltip when the pointer leaves an equipment slot.
		/// </summary>
		private void OnSlotPointerLeave(VisualElement owner)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				// HideFor, so a stale leave cannot close a tooltip a different slot has since opened.
				tooltip.HideFor(owner);
			}
		}

		/// <summary>
		/// Left-click: if a drag object is active, equip the dragged item into this slot;
		/// otherwise begin dragging the item currently in this slot.
		/// </summary>
		private void HandleSlotLeftClick(int slotIndex)
		{
			if (!UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				return;
			}

			if (!Character.TryGet(out IEquipmentController equipmentController))
			{
				return;
			}

			if (dragObject.IsDragging)
			{
				CompleteDropOntoSlot(dragObject, equipmentController, slotIndex);
				return;
			}

			BeginDragFromSlot(dragObject, equipmentController, slotIndex);
		}

		/// <summary>
		/// Equips whatever the drag is carrying into <paramref name="slotIndex"/>.
		/// </summary>
		/// <remarks>
		/// Every gate here is a client-side courtesy — the server re-validates all of it — but the
		/// courtesy is the point: a request the client already knows will be refused costs a round
		/// trip and, until <c>ItemOperationFailedBroadcast</c> existed, produced no answer at all.
		/// The source item is re-read from its container rather than taken from the drag, because
		/// the drag is a snapshot from whenever the player clicked and the container has been
		/// replicated since.
		/// </remarks>
		private void CompleteDropOntoSlot(UITKDragObject dragObject, IEquipmentController equipmentController, int slotIndex)
		{
			int sourceSlot = (int)dragObject.ReferenceID;

			IItemContainer sourceContainer = ResolveContainer(dragObject.Type);
			InventoryType sourceInventory = dragObject.Type == ReferenceButtonType.Bank
				? InventoryType.Bank
				: InventoryType.Inventory;

			/* Equipment-to-equipment is not an operation the protocol has: there is no "swap two
			 * equipment slots" broadcast, and equipping from equipment would need an inventory
			 * index it does not have. Drop the drag rather than send something meaningless. */
			if (dragObject.Type != ReferenceButtonType.Inventory &&
				dragObject.Type != ReferenceButtonType.Bank)
			{
				dragObject.Clear();
				return;
			}

			if (sourceContainer == null ||
				!sourceContainer.CanManipulate() ||
				!sourceContainer.IsValidSlot(sourceSlot) ||
				!sourceContainer.TryGetItem(sourceSlot, out Item sourceItem) ||
				!dragObject.MatchesSource(sourceItem))
			{
				// The slot the drag came from is not what it was when the drag started.
				dragObject.Clear();
				return;
			}

			if (!equipmentController.IsValidSlot(slotIndex) ||
				sourceContainer.IsSlotLocked(sourceSlot) ||
				equipmentController.IsSlotLocked(slotIndex))
			{
				dragObject.Clear();
				return;
			}

			/* Claim both ends before sending. Claiming one and failing on the other would leave a
			 * slot marked as waiting for a request that was never sent. */
			if (!ItemOperationTracker.TryBegin(dragObject.Type, sourceSlot))
			{
				dragObject.Clear();
				return;
			}
			if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Equipment, slotIndex))
			{
				ItemOperationTracker.Release(dragObject.Type, sourceSlot);
				dragObject.Clear();
				return;
			}

			Client.Broadcast(new EquipmentEquipItemBroadcast()
			{
				InventoryIndex = sourceSlot,
				Slot           = (byte)slotIndex,
				FromInventory  = sourceInventory,
			}, Channel.Reliable);

			dragObject.Clear();
		}

		/// <summary>
		/// Starts a drag from an occupied equipment slot.
		/// </summary>
		private void BeginDragFromSlot(UITKDragObject dragObject, IEquipmentController equipmentController, int slotIndex)
		{
			bool blocked = IsSlotBlocked(slotIndex);
			bool gotItem = equipmentController.TryGetItem(slotIndex, out Item item);

			// Report the refusal, not just the success — see UITKInventory.BeginDragFromSlot.
			if (blocked || !gotItem || item == null)
			{
				FishMMO.Logging.Log.Debug("UITKEquipment",
					$"BeginDrag REFUSED slot {slotIndex}: blocked={blocked} " +
					$"(pending={ItemOperationTracker.IsPending(ReferenceButtonType.Equipment, slotIndex)}) " +
					$"gotItem={gotItem} itemNull={item == null}.");
				return;
			}

			// Same as the inventory: a missing icon must not prevent the item being unequipped.
			Sprite icon = item.Template != null ? item.Template.Icon : null;

			/* The item, not just the slot index. A slot index alone is only true for as long as
			 * nothing writes to that slot, and the server can write to it at any moment. */
			dragObject.SetItemReference(icon, slotIndex, ReferenceButtonType.Equipment, item);
		}

		/// <summary>
		/// Right-click: unequip the item to the inventory.
		/// </summary>
		/// <remarks>
		/// This used to call <c>ClearSlot(slotIndex)</c> before broadcasting — an optimistic write
		/// with no way back. If the server refused (dead character, full inventory, a locked slot,
		/// a stale index) nothing ever told the client, so the slot rendered empty for the rest of
		/// the session while the item was still equipped and still contributing its attributes.
		/// The slot now keeps rendering the item and is marked as waiting; the equipment container
		/// being replicated back is what empties it.
		/// </remarks>
		private void HandleSlotRightClick(int slotIndex)
		{
			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) && dragObject.IsDragging)
			{
				dragObject.Clear();
			}

			if (!Character.TryGet(out IEquipmentController equipmentController))
			{
				return;
			}

			if (!equipmentController.CanManipulate() ||
				!equipmentController.IsValidSlot(slotIndex) ||
				equipmentController.IsSlotEmpty(slotIndex) ||
				IsSlotBlocked(slotIndex))
			{
				return;
			}

			if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Equipment, slotIndex))
			{
				return;
			}

			Client.Broadcast(new EquipmentUnequipItemBroadcast()
			{
				Slot        = (byte)slotIndex,
				ToInventory = InventoryType.Inventory,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Resolves the character's container for a drag source type.
		/// </summary>
		private IItemContainer ResolveContainer(ReferenceButtonType type)
		{
			if (Character == null)
			{
				return null;
			}

			switch (type)
			{
				case ReferenceButtonType.Inventory:
					return Character.TryGet(out IInventoryController inventoryController) ? inventoryController : null;
				case ReferenceButtonType.Bank:
					return Character.TryGet(out IBankController bankController) ? bankController : null;
				case ReferenceButtonType.Equipment:
					return Character.TryGet(out IEquipmentController equipmentController) ? equipmentController : null;
				default:
					return null;
			}
		}

		/// <summary>
		/// Abandons this panel's in-flight operations and any drag that started here.
		/// </summary>
		private void ReleaseAndClearDrag()
		{
			ItemOperationTracker.ReleaseAll(ReferenceButtonType.Equipment);

			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) &&
				dragObject.IsDragging &&
				dragObject.Type == ReferenceButtonType.Equipment)
			{
				dragObject.Clear();
			}
		}

		// ── Attribute row building ────────────────────────────────────────────

		/// <summary>
		/// Categorises all character attributes and creates the scrollable attribute rows
		/// inside the <c>attribute-list</c> ScrollView.
		/// </summary>
		private void BuildAttributeRows(ICharacterAttributeController attributeController)
		{
			if (attributeList == null)
			{
				return;
			}

			var resourceAttributes    = new List<CharacterAttribute>();
			var damageAttributes      = new List<CharacterAttribute>();
			var resistanceAttributes  = new List<CharacterAttribute>();
			var coreAttributes        = new List<CharacterAttribute>();

			foreach (CharacterResourceAttribute ra in attributeController.ResourceAttributes.Values)
			{
				resourceAttributes.Add(ra);
			}

			foreach (CharacterAttribute attr in attributeController.Attributes.Values)
			{
				if (attr.Template.Name.Contains("Regeneration"))
				{
					resourceAttributes.Add(attr);
				}
				else if (attr.Template is DamageAttributeTemplate)
				{
					damageAttributes.Add(attr);
				}
				else if (attr.Template is ResistanceAttributeTemplate)
				{
					resistanceAttributes.Add(attr);
				}
				else
				{
					coreAttributes.Add(attr);
				}
			}

			AddAttributeCategory("Resource",    resourceAttributes);
			AddAttributeCategory("Damage",      damageAttributes);
			AddAttributeCategory("Resistance",  resistanceAttributes);
			AddAttributeCategory("Core",        coreAttributes);

			resourceAttributes.Clear();
			damageAttributes.Clear();
			resistanceAttributes.Clear();
			coreAttributes.Clear();
		}

		/// <summary>
		/// Creates a category header label and one row per attribute, appending all to the
		/// attribute ScrollView and subscribing to <see cref="CharacterAttribute.OnAttributeUpdated"/>.
		/// </summary>
		private void AddAttributeCategory(string categoryName, List<CharacterAttribute> attributes)
		{
			if (attributes == null || attributes.Count == 0 || attributeList == null)
			{
				return;
			}

			// Category header
			Label header = new Label(categoryName);
			header.AddToClassList(CSS_ATTR_CATEGORY);
			attributeList.Add(header);
			attributeCategoryElements.Add(header);

			for (int i = 0; i < attributes.Count; ++i)
			{
				CharacterAttribute attribute = attributes[i];

				attribute.OnAttributeUpdated -= OnAttributeUpdated; // defensive dedup

				VisualElement row = new VisualElement();
				row.AddToClassList(CSS_ATTR_ROW);

				Label nameLabel = new Label(attribute.Template.Name);
				nameLabel.AddToClassList(CSS_ATTR_NAME);
				row.Add(nameLabel);

				Label valueLabel = new Label();
				valueLabel.AddToClassList(CSS_ATTR_VALUE);

				if (attribute.Template.IsResourceAttribute)
				{
					CharacterResourceAttribute resource = attribute as CharacterResourceAttribute;
					if (resource != null)
					{
						valueLabel.text = Mathf.RoundToInt(resource.CurrentValue) + " / " + resource.FinalValue;
					}
					valueLabel.AddToClassList(CSS_ATTR_RESOURCE);
				}
				else
				{
					valueLabel.text = attribute.Template.IsPercentage
						? attribute.FinalValue + "%"
						: attribute.FinalValue.ToString();

					if (attribute.Template.IsPercentage)
					{
						valueLabel.AddToClassList(CSS_ATTR_PERCENT);
					}
				}

				row.Add(valueLabel);
				attributeList.Add(row);
				attributeRowElements.Add(row);

				attributeValueLabels[attribute.Template.ID] = valueLabel;
				attribute.OnAttributeUpdated += OnAttributeUpdated;
				subscribedAttributes.Add(attribute);
			}
		}

		/// <summary>
		/// Detaches this panel from every attribute it is subscribed to.
		/// </summary>
		private void UnsubscribeAttributes()
		{
			for (int i = 0; i < subscribedAttributes.Count; ++i)
			{
				if (subscribedAttributes[i] != null)
				{
					subscribedAttributes[i].OnAttributeUpdated -= OnAttributeUpdated;
				}
			}
			subscribedAttributes.Clear();
		}

		/// <summary>
		/// Removes all runtime-created attribute elements and unsubscribes from all attribute events.
		/// </summary>
		private void DestroyAttributeElements()
		{
			UnsubscribeAttributes();

			/* RemoveFromHierarchy, not attributeList.Remove. VisualElement.Remove THROWS when the
			 * element is not its child, and after the document re-clones the UXML these rows
			 * belong to the previous tree while attributeList is the new one — so the old code
			 * threw part-way through, aborting the rebuild and leaving the panel permanently
			 * empty. RemoveFromHierarchy asks the element about its own parent and is a no-op when
			 * it has none. */
			for (int i = 0; i < attributeCategoryElements.Count; ++i)
			{
				attributeCategoryElements[i]?.RemoveFromHierarchy();
			}
			for (int i = 0; i < attributeRowElements.Count; ++i)
			{
				attributeRowElements[i]?.RemoveFromHierarchy();
			}

			attributeCategoryElements.Clear();
			attributeRowElements.Clear();
			attributeValueLabels.Clear();
		}

		// ── Status bar ────────────────────────────────────────────────────────

		/// <summary>
		/// Populates the HP, MP, and Stamina status-bar chips from the attribute controller.
		/// </summary>
		private void UpdateStatusBar(ICharacterAttributeController ac)
		{
			if (ac.TryGetHealthAttribute(out CharacterResourceAttribute hp) && statHpLabel != null)
			{
				statHpLabel.text = Mathf.RoundToInt(hp.CurrentValue) + " / " + hp.FinalValue;
			}
			if (ac.TryGetManaAttribute(out CharacterResourceAttribute mp) && statMpLabel != null)
			{
				statMpLabel.text = Mathf.RoundToInt(mp.CurrentValue) + " / " + mp.FinalValue;
			}
			if (ac.TryGetStaminaAttribute(out CharacterResourceAttribute stam) && statStamLabel != null)
			{
				statStamLabel.text = Mathf.RoundToInt(stam.CurrentValue) + " / " + stam.FinalValue;
			}
		}

		/// <summary>
		/// Resets all status-bar chip labels to the default placeholder.
		/// </summary>
		private void ResetStatusBar()
		{
			if (statHpLabel   != null) statHpLabel.text   = "—";
			if (statMpLabel   != null) statMpLabel.text   = "—";
			if (statStamLabel != null) statStamLabel.text = "—";
			if (gearScoreLabel != null) gearScoreLabel.text = "GS: —";
		}

		/// <summary>
		/// Updates a status-bar chip whose attribute name matches one of the known resource labels.
		/// Called from <see cref="OnAttributeUpdated"/> to keep the chips in sync.
		/// </summary>
		private void UpdateStatusBarChip(string templateName, string formattedValue)
		{
			if (templateName.Contains("Health") && statHpLabel != null)
			{
				statHpLabel.text = formattedValue;
			}
			else if (templateName.Contains("Mana") && statMpLabel != null)
			{
				statMpLabel.text = formattedValue;
			}
			else if (templateName.Contains("Stamina") && statStamLabel != null)
			{
				statStamLabel.text = formattedValue;
			}
		}
	}
}
