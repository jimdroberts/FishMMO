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
	/// Binds to <c>UICharacterSheet.uxml</c> / <c>UICharacterSheet.uss</c> and renders the character's
	/// equipped items alongside the attributes they contribute to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This panel renders the server's answer and never its own guess. Every request it sends
	/// leaves the slot looking exactly as it did, marked as waiting, until the equipment container
	/// is replicated back — see <see cref="HandleSlotRightClick"/> for what that replaced and why.
	/// </para>
	/// <para>
	/// Everything a SLOT does — joining the operation tracker, deciding whether a slot is
	/// available, painting the icon and the stack badge and the lock overlay, keeping the tooltip
	/// honest, starting a drag, abandoning what is in flight — comes from
	/// <see cref="UITKSlotPanelBase"/>, which this panel shares with the bag and the bank. It used
	/// to be a second copy of all of it, eighteen identically named members, and it had drifted
	/// from the grid version in three ways; that class records which, and
	/// <see cref="OnSlotPointerDown"/> below carries the one that was a player-visible bug.
	/// </para>
	/// <para>
	/// What is genuinely this panel's own is the half a socket does differently: its slots are
	/// AUTHORED in <c>UICharacterSheet.uxml</c> rather than built from a container's slot count and a
	/// drop onto one is an equip rather than a swap. Everything else the sheet draws — the ten
	/// sockets' contents, the character preview, the HP / MP / Stamina chips, the attribute rows — is
	/// <see cref="CharacterSheetView"/>'s, which this window shares with the inspect window and which
	/// is why the two now read as one window showing two characters instead of two windows that
	/// happened to be about equipment.
	/// </para>
	/// </remarks>
	public class UITKEquipment : UITKSlotPanelBase
	{
		// ── What this panel says about itself ─────────────────────────────────

		/// <inheritdoc/>
		/// <remarks>
		/// "eq", which is already the prefix every class in <c>UICharacterSheet.uss</c> carries:
		/// <c>eq-hidden</c> and <c>eq-slot__lock--pending</c> are exactly the two names the shared
		/// painters derive from it. The two <c>const</c> strings that used to spell them out here
		/// were the same convention written a second time.
		/// </remarks>
		protected override string Prefix => "eq";

		/// <inheritdoc/>
		protected override ReferenceButtonType DragType => ReferenceButtonType.Equipment;

		// ── UXML element names ────────────────────────────────────────────────

		/// <summary>Name of the close button element in the UXML.</summary>
		private const string CLOSE_BTN_NAME     = "close-button";

		/* The attribute list, the preview viewport and the three status labels are resolved by
		 * CharacterSheetView, which is the class that draws them; this panel asks the sheet about them
		 * rather than holding a second set of references into the same markup. */

		/* DRAG_OBJECT_NAME, TOOLTIP_NAME and TOAST_NAME are on UITKSlotPanelBase — three string
		 * constants that were identical in this file and in the item grid, naming overlays neither
		 * panel owns. */

		/* SlotElementNames is on CharacterSheetView. It is the contract with the ItemSlot enum — one
		 * authored element per enum value, in enum order — and both windows that mount the sheet need
		 * it, so it is written once rather than once per panel. The inspect window has the same need
		 * for the same ten names and no reason to know about this class. */

		/* eq-hidden and eq-slot__lock--pending are derived from Prefix by UITKSlotPanelBase, which
		 * owns the painters that apply them. The attribute-row classes are the sheet's; they live with
		 * the code that creates the rows, in CharacterSheetView. */

		// ── Private state ─────────────────────────────────────────────────────

		/* The SlotView struct and the slotViews collection are on UITKSlotPanelBase. This panel
		 * used to hold them as an ARRAY that was null until OnStarting had seen a populated tree,
		 * which meant eight null tests, one of which read .Length before testing and was an NRE on
		 * the first equip of any session the player had not opened this window in. The shared list
		 * starts empty instead, so every bounds check answers the same way with no special case. */

		/// <summary>
		/// The shared sheet: sockets' contents, preview, chips and attribute rows.
		/// </summary>
		/// <remarks>
		/// Built in <see cref="OnStarting"/>, against the tree resolved in the same call, and rebuilt
		/// with it. Everything it owns belongs to that tree, so a tree replacement replaces it.
		/// </remarks>
		private CharacterSheetView sheet;

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

			/* The sheet resolves the attribute list, the preview viewport and the status labels in the
			 * same tree, and hides the regions this viewer may not see — for this panel, none. It is
			 * disposed rather than dropped because it owns a render texture and a camera reference, and
			 * a hide/show cycle would otherwise leak one of each every time. */
			sheet?.Dispose();
			sheet = new CharacterSheetView(root, CharacterSheetOptions.Equipment);

			// Close button
			Button closeBtn = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeBtn != null)
			{
				closeBtn.clicked += Hide;
			}

			BuildSlotViewsFromMarkup(root);
		}

		/// <summary>
		/// Fills the shared slot list from the sockets authored in the UXML.
		/// </summary>
		/// <remarks>
		/// <para>
		/// THIS IS THE PART A SHARED BASE MUST NOT OWN, and the reason
		/// <see cref="UITKSlotPanelBase"/> holds the list but never populates it. The item grids
		/// CREATE one element per container slot, so their slot count follows the container. These
		/// ten sockets are authored in <c>UICharacterSheet.uxml</c> and found by name, so their count
		/// follows the <see cref="ItemSlot"/> enum and the markup has to agree with it.
		/// </para>
		/// <para>
		/// The names come from <see cref="CharacterSheetView.SlotElementNames"/> and the elements
		/// from the sheet that already resolved them, so there is one description of which socket is
		/// which. What is built here is the INTERACTIVE record of each one — the icon, the stack
		/// badge and the lock overlay this panel paints and routes clicks through — which is not
		/// something the sheet shares, because the inspect window must not route a click at all.
		/// </para>
		/// <para>
		/// A socket whose element is missing is still ADDED, as a default
		/// <c>SlotView</c> with a null root. Skipping it would shorten the list and every socket
		/// after it would then draw the wrong item — the index into this list is the container slot
		/// index, and that is the contract the name array exists to keep. The shared painters already
		/// treat a null root as nothing to draw.
		/// </para>
		/// <para>
		/// The callbacks are registered on elements that belong to the tree being resolved right
		/// now — a rebuilt tree brings new elements and the old handlers go with the old ones, so
		/// there is nothing to unregister and no per-rebuild accumulation. The list is cleared
		/// first for the same reason: a stale SlotView points into a tree nobody can see.
		/// </para>
		/// </remarks>
		private void BuildSlotViewsFromMarkup(VisualElement root)
		{
			slotViews.Clear();

			int slotCount = CharacterSheetView.SlotElementNames.Length;
			for (int i = 0; i < slotCount; ++i)
			{
				VisualElement slotRoot = root.Q(CharacterSheetView.SlotElementNames[i]);

				SlotView view = default;
				if (slotRoot != null)
				{
					view.Root   = slotRoot;
					view.Icon   = slotRoot.Q(className: "fish-slot__icon");
					view.Amount = slotRoot.Q<Label>(className: "fish-slot__amount");
					view.Lock   = slotRoot.Q(className: "fish-slot__lock");
				}
				slotViews.Add(view);

				if (slotRoot == null)
				{
					continue;
				}

				int slotIndex = i;
				/* Marked as a slot so a press on it does not cancel the drag the player is carrying;
				 * the authored UXML carries the look, this carries the meaning. See
				 * UITKControl.OnRootPointerDownCancelDrag. */
				slotRoot.AddToClassList(SLOT_MARKER_CLASS);
				slotRoot.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(evt, slotIndex));
				slotRoot.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(slotIndex, slotRoot));
				slotRoot.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave(slotRoot));
			}
		}

		/* OnAfterStarting, OnAfterShow, OnClientSet, OnClientUnset and Hide are on
		 * UITKSlotPanelBase. All five were the same overrides the item grid had written, THE
		 * CONTRACT about the first open included — the same paragraph of explanation appeared in
		 * both files. */

		/// <summary>
		/// Refreshes the character preview while the panel is on screen.
		/// </summary>
		/// <remarks>
		/// The preview is refreshed here rather than on the equipment events because a preview
		/// shows an animating character: the idle pose moves every frame, and a texture rendered
		/// once when the panel opened would freeze it mid-stride. A frame is also when the
		/// viewport's layout first becomes measurable, so this is the hook that gets the preview
		/// its size on the opening frame. The base ticks the operation tracker.
		/// </remarks>
		protected override void OnTick()
		{
			base.OnTick();

			if (Visible)
			{
				sheet?.RefreshPreview();
			}
		}

		/// <summary>
		/// Releases the preview camera and texture, the runtime-created attribute elements, and the
		/// equipment subscriptions.
		/// </summary>
		/// <remarks>
		/// The tracker subscription and the drag are released by the base before this runs, which
		/// is the order this panel already used.
		/// </remarks>
		protected override void OnPanelDestroying()
		{
			if (Character != null && Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated     -= OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged -= OnEquipmentSlotLockChanged;
				equipmentController.OnRequestResolved -= OnEquipmentRequestResolved;
			}

			/* Disposes rather than dropping the reference. The camera belongs to the character and
			 * would otherwise stay enabled — rendering the character into a texture nobody draws,
			 * every frame, for the rest of the session. The attribute subscriptions and the
			 * runtime-created rows go with it. */
			sheet?.Dispose();
		}

		// ── Visibility overrides (preview sync) ──────────────────────────────

		/// <summary>
		/// Hands the preview camera back once the panel has actually gone off screen.
		/// </summary>
		/// <remarks>
		/// The camera is handed back rather than simply left enabled, because the preview's cost is
		/// paid every frame whether or not anyone is looking at it. The base decides WHEN this is —
		/// including the Escape and quit-to-login paths, which reach <c>Hide(bool)</c> directly
		/// rather than through <c>Hide()</c> — and abandons the pending marks and any half-finished
		/// drag afterwards. Both used to be spelled out here and in the item grid.
		/// </remarks>
		protected override void OnPanelHidden()
		{
			sheet?.ReleasePreview();
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
			sheet?.ReleasePreview();

			if (Character != null &&
				Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated     -= OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged -= OnEquipmentSlotLockChanged;
				equipmentController.OnRequestResolved -= OnEquipmentRequestResolved;
			}

			/* The sheet's attribute subscriptions are detached by SetSubject, which runs from
			 * OnPostSetCharacter with the incoming character. It does not need the outgoing one to do
			 * it: the list of attributes it is subscribed to is its own, so the detach cannot look up
			 * the wrong character's attributes the way the old walk over Character's did. */
		}

		/// <summary>
		/// Subscribes to equipment slot events, refreshes all slot visuals, and hands the new
		/// character to the sheet.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			/* Replaces the sheet's subject, which releases the outgoing character's attribute
			 * subscriptions and preview camera, paints the sockets, rebuilds the attribute rows and
			 * refreshes the chips. A null character is a legitimate subject here: the sheet empties
			 * itself rather than keeping the last character's gear on screen. */
			sheet?.SetSubject(Character);

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

			/* The sheet is unbound rather than merely un-previewed: the character is going away, so its
			 * camera is handed back, its attribute subscriptions are dropped and its gear is taken off
			 * the sockets. A sheet left bound to a character that no longer exists is the whole of what
			 * this method is for. */
			sheet?.SetSubject(null);

			ReleaseAndClearDrag();
		}

		// ── Equipment slot callbacks ──────────────────────────────────────────

		/// <summary>
		/// Called when the lock state of an equipment slot changes.
		/// </summary>
		public void OnEquipmentSlotLockChanged(IItemContainer container, int slot, bool isLocked)
		{
			/* The slot list is EMPTY until OnStarting has seen a populated tree, and a panel that
			 * starts hidden is handed a character — and therefore these events — long before that.
			 * An empty list fails the bounds check below; the array this used to be was null, and
			 * reading its .Length first was an NRE on the first equip of every session in which the
			 * player had not opened this window. */
			if (slot < 0 || slot >= slotViews.Count)
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
			ApplySlotUpdate(container, item, equipmentSlot);
		}

		/// <summary>
		/// Invalidates the preview framing when a socket has changed.
		/// </summary>
		/// <remarks>
		/// Equipment changes the silhouette: a two-handed weapon reaches further than a dagger, and
		/// a frame sized before it existed crops it. Re-framing on the next frame rather than this
		/// one is deliberate — the mesh is attached by the visual controller in the same call chain,
		/// and measuring before it lands would measure the old silhouette again.
		/// <para>
		/// A hook rather than part of the shared update, because it is the one thing a socket update
		/// does that a bag-slot update has no equivalent of. It runs only for an update the shared
		/// path accepted, which is where it sat before.
		/// </para>
		/// </remarks>
		protected override void OnSlotUpdateReceived(int slotIndex)
		{
			sheet?.InvalidatePreviewFraming();
		}

		/* OnAttributeUpdated is on CharacterSheetView, which owns the attribute labels and the
		 * subscriptions that feed them. This panel used to hold both, one file's worth of label
		 * bookkeeping beside the sockets, and the inspect window had no way to reuse any of it. */

		// ── Character preview ─────────────────────────────────────────────────

		/* RefreshPreview, ApplyPreviewTexture, TryMeasureViewport and the preview renderer are on
		 * CharacterSheetView. The preview is a property of the SHEET — a character photographed into
		 * the viewport the markup declares — and not of this panel, which is why an inspected
		 * character gets one for free rather than needing its own copy of a hundred lines. */

		/* SubscribeTracker, UnsubscribeTracker, OnTrackerSlotPendingChanged and
		 * OnTrackerResyncRequested are on UITKSlotPanelBase. Four methods, some sixty lines,
		 * character for character the same as the item grid's — including the -= before += on a
		 * static event, and the reason for it, written out twice. */

		// ── Private helpers ───────────────────────────────────────────────────

		/// <summary>
		/// Re-reads everything this panel shows from the character, without rebuilding the tree.
		/// </summary>
		protected override void ApplyPerOpenContent()
		{
			if (slotViews.Count == 0 || Character == null)
			{
				return;
			}

			if (Character.TryGet(out IEquipmentController equipmentController))
			{
				RefreshAllSlots(equipmentController);
			}

			/* The chips and the preview, both re-read from the character. The preview is rebuilt on
			 * every open, not just the first: the camera and texture belong to the character and are
			 * handed back when the panel closes, so the opening after that starts from nothing. The
			 * attribute rows are NOT rebuilt here — they are built once per character by SetSubject,
			 * because they carry subscriptions and rebuilding them per open would churn them. */
			sheet?.Refresh();
		}

		/* RefreshAllSlots, IsSlotBlocked, RefreshSlot, SetSlotItem, ClearSlot, RefreshSlotTooltip
		 * and ApplySlotLockVisual are on UITKSlotPanelBase — around two hundred lines that were a
		 * second copy of the item grid's, down to the comment about a missing icon and the one
		 * about issue #280. Two of them had drifted; read the base's remarks on IsSlotBlocked and
		 * ApplySlotUpdate for what was different and which way it was settled. RefreshSlot is gone
		 * entirely: it was a two-branch helper the shared refresh loop inlines. */

		/* OnSlotPointerUp is gone, and nothing replaced it. It was the third hand-written copy of
		 * "a release over a non-source slot completes the drop" — and that gesture is not one this
		 * game has. A socket is completed by PRESSING it, like every other slot; a release over one
		 * leaves the item on the cursor for the next press to place. See the note above Notify in
		 * UITKSlotPanelBase, which is where the rule is written down. */

		/// <summary>
		/// Handles pointer-down events on an equipment slot element.
		/// Left button: drag-and-drop equip, or start a drag.
		/// Shift + left button: send the item straight to the bag.
		/// Right button: unequip.
		/// </summary>
		/// <remarks>
		/// <para>
		/// /* DELIBERATE BEHAVIOUR CHANGE. */ THE SHIFT BRANCH IS NEW AND IT IS A BUG FIX, not a
		/// feature. Shift-click means "send this item to the other container" everywhere else in
		/// the game — issue #197 put it in the bag and in the bank, and
		/// <c>UITKItemGridPanel.OnSlotPointerDown</c> is where that lives — and a socket's other
		/// container is the bag. Here it did nothing at all. The reason it did nothing is the reason
		/// this whole file now has a shared base: the routing method existed twice under the same
		/// name, the fix was made to one copy, and nothing connected the two. A player who learns
		/// shift-click emptying their bags into the bank finds it dead the moment they try it on
		/// what they are wearing, and reads that as the game not registering the click.
		/// </para>
		/// <para>
		/// It routes to the same unequip a right-click performs, which is what makes it safe: no
		/// new request, no new server path, no new refusal to explain. The only difference from a
		/// right-click is that a shift-click is not also a way to cancel a drag, so it does not
		/// clear one.
		/// </para>
		/// <para>
		/// SHIFT DEFERS TO A DRAG IN FLIGHT. A drop onto a socket is completed by the press on it —
		/// there is no release path, by the shared base's rule — and the press should read no
		/// modifier while carrying, so a shift-click on a socket while the player is carrying
		/// something has to fall through to <see cref="HandleSlotLeftClick"/> and become that drop.
		/// Otherwise the press claims this socket for an unequip the player never asked for and the
		/// swap is lost in both directions: the item meant for the socket stays where it was AND
		/// the item already in the socket is sent to the bag. Reading
		/// the modifier is this router's job, not <see cref="TryQuickUnequip"/>'s, so the deferral
		/// is visible in the branch that consults it. <see cref="IsDragInFlight"/> is the same test
		/// <see cref="HandleSlotLeftClick"/> makes a line later.
		/// </para>
		/// <para>
		/// Shift is read from the event rather than from global input state: the modifier that
		/// matters is the one held when this click happened, and a poll can answer for a moment
		/// either side of it.
		/// </para>
		/// </remarks>
		private void OnSlotPointerDown(PointerDownEvent evt, int slotIndex)
		{
			if (Character == null || Client == null)
			{
				return;
			}

			if (evt.button == 0) // left
			{
				/* Shift only when nothing is being carried — see the remarks. With a drag in flight
				 * the press is the drop, and it is completed on release. */
				if (evt.shiftKey && !IsDragInFlight())
				{
					TryQuickUnequip(slotIndex);
				}
				else
				{
					HandleSlotLeftClick(slotIndex);
				}
			}
			else if (evt.button == 1) // right
			{
				HandleSlotRightClick(slotIndex);
			}
		}

		/* OnSlotPointerEnter and OnSlotPointerLeave are on UITKSlotPanelBase. Opening a tooltip for
		 * whatever a slot holds, and closing it with HideFor so a stale leave cannot shut a tooltip
		 * another slot has since opened, is the same act in a socket and in a bag slot. */

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

			/* The destination is tested with the SAME predicate that paints it, and not with the
			 * container's own lock alone. IsSlotBlocked is a superset — it adds a request this
			 * panel is already waiting on and an item the database has not written yet — and those
			 * two are exactly the reasons the socket is drawn locked. Consulting the narrower test
			 * here accepted a drop onto a socket the player could see was busy, and the server
			 * refuses every request naming an identity-less slot, so the drop cost a round trip to
			 * be told what the lock overlay had already said. Latent rather than live — nothing
			 * equips an item that has no identity yet — which is why it went unnoticed. */
			if (!equipmentController.IsValidSlot(slotIndex) ||
				sourceContainer.IsSlotLocked(sourceSlot) ||
				IsSlotBlocked(slotIndex))
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
					Notify($"{sourceItem.Name} goes in the {ItemSlotNames.DisplayName(equippable.Slot)} socket.", ToastSeverity.Warning);
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

		/* BeginDragFromSlot is on UITKSlotPanelBase, refusal log and all — this copy and the item
		 * grid's differed only in that the grid's had lost the log line. */

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

			TryQuickUnequip(slotIndex);
		}

		/// <summary>
		/// Sends the item in <paramref name="slotIndex"/> back to the bag.
		/// </summary>
		/// <remarks>
		/// Split out of <see cref="HandleSlotRightClick"/> so that shift-click can reach the same
		/// request — see <see cref="OnSlotPointerDown"/> for why a socket needed a shift-click at
		/// all. The one thing right-click does that shift-click does not is cancel a drag in
		/// progress, which is why that stayed behind rather than moving in here.
		/// <para>
		/// Nothing is written on this client. The slot keeps rendering the item and is marked as
		/// waiting; the equipment container being replicated back is what empties it.
		/// </para>
		/// </remarks>
		/// <param name="slotIndex">The socket to empty.</param>
		private void TryQuickUnequip(int slotIndex)
		{
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
				/* The claim comes straight off — the request was never sent, and a socket left
				 * marked as waiting on it would stay locked until the watchdog fired. */
				ItemOperationTracker.Release(ReferenceButtonType.Equipment, slotIndex);

				/* And the refusal is said out loud, for the reason the grid's quick transfer gives
				 * the same answer: a shift-click that silently does nothing is indistinguishable
				 * from one the game did not register, and the player's next move is to try it
				 * again. Everything else that can refuse here — a locked socket, an identity-less
				 * item, a character mid-teleport — was already ruled out by the guards above, so
				 * the one this can report is the destination: a bag with no room in it, which is
				 * also the only one the player can do anything about. */
				Notify("No room in your inventory.", ToastSeverity.Warning);
			}
		}

		/* ResolveContainer and ReleaseAndClearDrag are on UITKSlotPanelBase. ResolveContainer was
		 * character for character identical to the item grid's, switch arms and all — the clearest
		 * single sign that these two panels wanted one base: a method about the CHARACTER's
		 * containers, with no reference to the panel it was written in, existing twice. */

		/* Notify is on UITKSlotPanelBase now, along with the grid's copy. The two were identical —
		 * five lines and the same TOAST_NAME, which was already declared on the base — and the only
		 * reason given for leaving them was that QuickTransferTests located the grid's copy by its
		 * summary line. That anchor is a fixture's convenience, not a contract; the fixture was
		 * re-anchored rather than the duplication kept. */

		/* BuildAttributeRows, AddAttributeCategory, UnsubscribeAttributes, DestroyAttributeElements,
		 * UpdateStatusBar, ResetStatusBar and UpdateStatusBarChip are on CharacterSheetView. They were
		 * this file's alone — no item grid has an attribute list and the inspect window had no way to
		 * reuse any of it — and they are what the sheet SHARES between the two windows that mount it.
		 * The label bookkeeping and the subscriptions have to live with each other: the bug that had
		 * to be fixed twice was a subscription detached by walking the wrong character's attributes,
		 * which is only possible when the list of what was subscribed is kept away from the labels. */
	}
}
