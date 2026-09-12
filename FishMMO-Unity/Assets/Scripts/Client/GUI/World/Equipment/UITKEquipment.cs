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
		/// <summary>Name of the shared transient-notice overlay.</summary>
		private const string TOAST_NAME = "UIToast";

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
		/// <summary>Character preview render texture element.</summary>
		private VisualElement previewRt;
		/// <summary>Label displaying the HP stat value.</summary>
		private Label statHpLabel;
		/// <summary>Label displaying the MP stat value.</summary>
		private Label statMpLabel;
		/// <summary>Label displaying the stamina stat value.</summary>
		private Label statStamLabel;

		/// <summary>Owns the preview camera and the render texture the viewport draws.</summary>
		private readonly EquipmentPreviewRenderer previewRenderer = new EquipmentPreviewRenderer();

		/// <summary>
		/// True once the preview camera has been framed against the character actually on screen.
		/// </summary>
		/// <remarks>
		/// Cleared whenever the thing being photographed changes — a different character, or a
		/// different set of equipped meshes — and set again the first time the renderer can see
		/// one. Frame is otherwise a per-open cost, not a per-frame one: re-deriving the camera
		/// every frame would make the picture breathe as the character animates.
		/// </remarks>
		private bool previewFramed;

		/// <summary>The render texture currently on the preview element, or null.</summary>
		/// <remarks>
		/// Held so the style is only rewritten when the texture is actually replaced. The preview
		/// is refreshed every frame, and assigning a fresh <see cref="StyleBackground"/> each time
		/// would rebuild the style object, invalidate the element and allocate — for a value that
		/// only changes when the viewport is resized.
		/// </remarks>
		private RenderTexture previewTexture;

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

			/* Attribute scroll list. The slot grid above it and this list are both always
			 * visible, so nothing here decides which half of the panel to draw. */
			attributeList = root.Q<ScrollView>(ATTR_LIST_NAME);

			// Status-bar labels
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
		/// Times out item operations whose reply never arrived, and refreshes the character
		/// preview while the panel is on screen.
		/// </summary>
		/// <remarks>
		/// <para>The tracker is shared and self-clearing, so it does not matter that all three item
		/// panels drive it; whichever ticks first in a frame does the work and the others find
		/// nothing outstanding.</para>
		/// <para>The preview is refreshed here rather than on the equipment events because a
		/// preview shows an animating character: the idle pose moves every frame, and a texture
		/// rendered once when the panel opened would freeze it mid-stride. A frame is also when
		/// the viewport's layout first becomes measurable, so this is the hook that gets the
		/// preview its size on the opening frame.</para>
		/// </remarks>
		protected override void OnTick()
		{
			ItemOperationTracker.Tick();

			if (Visible)
			{
				RefreshPreview();
			}
		}

		/// <summary>
		/// Releases the preview camera and texture, runtime-created attribute elements and every
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
				equipmentController.OnRequestResolved -= OnEquipmentRequestResolved;
			}

			/* Disposes rather than dropping the reference. The camera belongs to the character and
			 * would otherwise stay enabled — rendering the character into a texture nobody draws,
			 * every frame, for the rest of the session. */
			ApplyPreviewTexture(null);
			previewRenderer.Dispose();
			previewFramed = false;

			DestroyAttributeElements();
			base.OnDestroying();
		}

		// ── Visibility overrides (preview sync) ──────────────────────────────

		/// <summary>
		/// Hides the equipment panel, releases the preview camera and abandons anything this panel
		/// had in flight.
		/// </summary>
		/// <remarks>
		/// <para><c>Hide(bool)</c> and not <c>Hide()</c>: <c>Hide()</c> delegates here, but Escape
		/// (<c>UIManager.CloseNext</c>) and quit-to-login (<c>Hide(false)</c>) both arrive at this
		/// overload directly. A pending mark or a half-finished drag that outlives the panel is
		/// invisible to the player and refuses their next click for no stated reason.</para>
		/// <para>The camera is handed back here rather than simply left enabled, because the
		/// preview's cost is paid every frame whether or not anyone is looking at it.</para>
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (Visible)
			{
				return;
			}

			/* The element stops drawing the texture before the renderer destroys it, so no frame
			 * can find a destroyed texture still on the element's style. */
			ApplyPreviewTexture(null);
			previewRenderer.Configure(null);
			previewFramed = false;

			ReleaseAndClearDrag();
		}

		// ── Character control ─────────────────────────────────────────────────

		/// <summary>
		/// Unsubscribes from equipment slot events before replacing the character.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			/* The outgoing character is still the one this panel is pointed at, so its camera has
			 * to be handed back before the reference moves — otherwise that character's camera
			 * stays enabled and renders into a texture nothing is drawing. */
			/* The element stops drawing the texture before the renderer destroys it, so no frame
			 * can find a destroyed texture still on the element's style. */
			ApplyPreviewTexture(null);
			previewRenderer.Configure(null);
			previewFramed = false;

			if (Character != null &&
				Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated     -= OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged -= OnEquipmentSlotLockChanged;
				equipmentController.OnRequestResolved -= OnEquipmentRequestResolved;
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
				equipmentController.OnRequestResolved -= OnEquipmentRequestResolved;

				RefreshAllSlots(equipmentController);

				equipmentController.OnSlotUpdated     += OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged += OnEquipmentSlotLockChanged;
				equipmentController.OnRequestResolved += OnEquipmentRequestResolved;
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
				equipmentController.OnRequestResolved -= OnEquipmentRequestResolved;
			}

			/* The element stops drawing the texture before the renderer destroys it, so no frame
			 * can find a destroyed texture still on the element's style. */
			ApplyPreviewTexture(null);
			previewRenderer.Configure(null);
			previewFramed = false;

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

			/* Equipment changes the silhouette: a two-handed weapon reaches further than a dagger,
			 * and a frame sized before it existed crops it. Re-framing on the next frame rather
			 * than this one is deliberate — the mesh is attached by the visual controller in the
			 * same call chain, and measuring before it lands would measure the old silhouette
			 * again. */
			previewFramed = false;

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

		// ── Character preview ─────────────────────────────────────────────────

		/// <summary>
		/// Adopts the current character's preview camera and renders it into the viewport.
		/// </summary>
		/// <remarks>
		/// <para>Safe to call every frame: it returns immediately until the UI layout engine has
		/// measured the viewport, and once the preview is framed it is one small orthographic
		/// render into a texture already sized for the element.</para>
		/// <para>The camera is taken from the character rather than injected by whoever opened the
		/// panel. It used to be handed over by the equipment hotkey, which meant the preview
		/// depended on how the panel was opened and not on what it was showing.</para>
		/// </remarks>
		private void RefreshPreview()
		{
			if (previewRt == null)
			{
				return;
			}

			Camera camera = Character != null ? Character.EquipmentViewCamera : null;
			if (camera == null)
			{
				ApplyPreviewTexture(null);
				previewFramed = false;
				return;
			}

			previewRenderer.Configure(camera);

			if (!TryMeasureViewport(previewRt, out int width, out int height))
			{
				return;
			}

			if (!previewRenderer.Render(width, height))
			{
				return;
			}

			/* Render before Frame, deliberately: Frame needs the texture's aspect to know whether
			 * the subject is limited by its height or its width, and the texture is only created
			 * once a size is known. The first frame of an opening therefore draws with the
			 * prefab's authored framing and the second with the corrected one. */
			if (!previewFramed)
			{
				previewFramed = previewRenderer.Frame(Character.MeshRoot);
			}

			ApplyPreviewTexture(previewRenderer.Texture);
		}

		/// <summary>
		/// Displays <paramref name="rt"/> inside the preview element.
		/// </summary>
		/// <param name="rt">The render texture to show, or null to show nothing.</param>
		/// <remarks>
		/// Toolkit cannot sample a RenderTexture through a texture slot, so it goes on as a
		/// background image. That is also why the viewport element is a plain
		/// <see cref="VisualElement"/> and not an <c>Image</c>: nothing here needs a sprite.
		/// </remarks>
		private void ApplyPreviewTexture(RenderTexture rt)
		{
			/* A raw reference compare, not Unity's overloaded == : this is about the texture having
			 * been replaced, and the renderer nulls its own field on release rather than leaving a
			 * destroyed object behind. */
			if (object.ReferenceEquals(previewTexture, rt))
			{
				return;
			}

			previewTexture = rt;

			if (previewRt == null)
			{
				return;
			}

			previewRt.style.backgroundImage = rt != null
				? new StyleBackground(Background.FromRenderTexture(rt))
				: StyleKeyword.None;
		}

		/// <summary>
		/// Reads the viewport's measured size, in whole pixels.
		/// </summary>
		/// <param name="element">The element to measure.</param>
		/// <param name="width">Receives the width in pixels.</param>
		/// <param name="height">Receives the height in pixels.</param>
		/// <returns>True when the layout engine has produced a usable size.</returns>
		/// <remarks>
		/// <c>resolvedStyle</c> reports NaN for a property that has never been resolved, which is
		/// the state of every element between being added to a panel and its first layout pass —
		/// so the NaN test is the real guard here and the size test only rejects a degenerate box.
		/// </remarks>
		private static bool TryMeasureViewport(VisualElement element, out int width, out int height)
		{
			width = 0;
			height = 0;

			float measuredWidth = element.resolvedStyle.width;
			float measuredHeight = element.resolvedStyle.height;

			if (float.IsNaN(measuredWidth) || float.IsNaN(measuredHeight) ||
				measuredWidth < 2.0f || measuredHeight < 2.0f)
			{
				return false;
			}

			width = Mathf.RoundToInt(measuredWidth);
			height = Mathf.RoundToInt(measuredHeight);
			return true;
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

			/* The preview is rebuilt on every open, not just the first. The camera and texture
			 * belong to the character and are handed back when the panel closes, so the opening
			 * after that starts from nothing. */
			previewFramed = false;
			RefreshPreview();
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

			RefreshSlotTooltip(slotIndex, item);
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

			RefreshSlotTooltip(slotIndex, null);
		}

		/// <summary>
		/// Keeps the tooltip for a slot in step with what the slot now shows.
		/// </summary>
		/// <remarks>
		/// The same defect the item grids had (issue #280), and the same place to fix it: an
		/// equipment slot that changes under a stationary cursor — a swap, an unequip, or the
		/// replicate that acknowledges either — never gets a pointer leave or enter, so the tooltip
		/// went on describing the item that was there when the pointer arrived.
		/// <para>
		/// <see cref="UITKTooltip.RefreshFor"/> ignores a slot the pointer is not over, which is what
		/// makes it safe to call from the refresh loop that repaints all ten.
		/// </para>
		/// </remarks>
		/// <param name="slotIndex">The slot that was just painted.</param>
		/// <param name="item">What it now holds, or null when it now holds nothing.</param>
		private void RefreshSlotTooltip(int slotIndex, Item item)
		{
			if (slotViews == null || slotIndex < 0 || slotIndex >= slotViews.Length)
			{
				return;
			}

			VisualElement owner = slotViews[slotIndex].Root;
			if (owner == null)
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.RefreshFor(owner, item);
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
				tooltip.Open(item, owner);
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

			/* A split half is a quantity, not an item: nothing exists to equip until the server
			 * has made it. Drop the drag rather than equip the whole stack it was taken from,
			 * which is not what the player picked up. Issue #198. */
			if (dragObject.SplitAmount > 0)
			{
				dragObject.Clear();
				return;
			}

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
				if (dragObject.Type == ReferenceButtonType.Equipment)
				{
					// Dropping one socket onto another reads as a move, and it silently is not one.
					Notify("Unequip it first to move it to another socket.", ToastSeverity.Warning);
				}
				dragObject.Clear();
				return;
			}

			if (sourceContainer == null ||
				!sourceContainer.CanManipulate() ||
				!CharacterStateValidation.CanAct(Character) ||
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
				/* One of the two slots is answering a request of its own. Said out loud because
				 * the alternative — the drag simply disappearing — is what the player reads as a
				 * lock that will not clear. */
				Notify("That slot is busy; try again in a moment.", ToastSeverity.Warning);
				dragObject.Clear();
				return;
			}

			/* Claim both ends before sending. Claiming one and failing on the other would leave a
			 * slot marked as waiting for a request that was never sent. */
			if (!ItemOperationTracker.TryBegin(dragObject.Type, sourceSlot))
			{
				Notify("That slot is busy; try again in a moment.", ToastSeverity.Warning);
				dragObject.Clear();
				return;
			}
			if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Equipment, slotIndex))
			{
				ItemOperationTracker.Release(dragObject.Type, sourceSlot);
				Notify("That socket is busy; try again in a moment.", ToastSeverity.Warning);
				dragObject.Clear();
				return;
			}

			/* Queued on the controller, not sent. The equip rides the owner's next replicate and
			 * is applied inside that tick on both peers — see IEquipmentController. A refusal here
			 * is local and immediate (the item is not where the drag said, or has no identity yet),
			 * so the marks come straight off; a refusal by the server arrives as a reconcile that
			 * moves the item back, which updates the slots and releases the marks the same way. */
			if (!equipmentController.RequestEquip(sourceItem, sourceSlot, sourceInventory, (ItemSlot)slotIndex))
			{
				ItemOperationTracker.Release(dragObject.Type, sourceSlot);
				ItemOperationTracker.Release(ReferenceButtonType.Equipment, slotIndex);

				/* The one refusal a drop onto a socket can be given that the player can act on is
				 * aiming at the wrong socket — a sword dropped on the off-hand — and it is silent
				 * everywhere else: the item is not where the request says, or has no identity yet,
				 * and neither is something the player did. Naming the socket the item does fit is
				 * what turns "the bank will not let me equip this" into a second attempt that
				 * works. There is no other way to equip out of the bank, so silence here is the
				 * whole bug report. Issue #268. */
				if (sourceItem.Template is EquippableItemTemplate equippable && (ItemSlot)slotIndex != equippable.Slot)
				{
					Notify($"{sourceItem.Name} goes in the {equippable.Slot} socket.", ToastSeverity.Warning);
				}
			}

			dragObject.Clear();
		}

		/// <summary>
		/// Releases the marks of a request the controller could not apply when its tick ran.
		/// </summary>
		/// <remarks>
		/// A request that WAS applied changes the slots, and the slot updates release the marks;
		/// one that was not changes nothing, and without this the slots would stay waiting until
		/// the tracker's timeout.
		/// </remarks>
		private void OnEquipmentRequestResolved(EquipmentRequestKind kind, ItemSlot socket, InventoryType container, int index, bool applied)
		{
			if (applied)
			{
				return;
			}
			ItemOperationTracker.Release(ReferenceButtonType.Equipment, (int)socket);
			if (index >= 0)
			{
				ItemOperationTracker.Release(ItemOperationTracker.FromInventoryType(container), index);
			}
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
				!CharacterStateValidation.CanAct(Character) ||
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

			// Queued for the next replicate tick; see CompleteDropOntoSlot for the contract.
			if (!equipmentController.RequestUnequip((ItemSlot)slotIndex, InventoryType.Inventory))
			{
				ItemOperationTracker.Release(ReferenceButtonType.Equipment, slotIndex);
			}
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

		/// <summary>
		/// Shows a transient notice, if the toast panel is up.
		/// </summary>
		/// <remarks>
		/// The same helper the item grids use. A drop this panel refuses is a gesture the player
		/// made and expects an answer to — see <see cref="CompleteDropOntoSlot"/> — and the
		/// equipment sockets are the one place a refusal has nowhere else to surface.
		/// </remarks>
		private static void Notify(string text, ToastSeverity severity)
		{
			if (UIManager.TryGetTK(TOAST_NAME, out UITKToast toast))
			{
				toast.Show(text, severity);
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
