using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Shared behaviour for every panel that draws a character's item slots — the bag, the bank,
	/// and the equipment sockets.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="UITKItemGridPanel"/> already unified the bank and the inventory, and the comment
	/// at the top of it explains why. What it did not reach was the equipment panel, which sat
	/// beside it re-implementing eighteen identically named members: the tracker subscription and
	/// both of its handlers, the slot-blocked test, the slot painters, the lock overlay, the
	/// tooltip refresh and the enter/leave pair, the drag start, the container lookup and the drag
	/// release. Same names, same jobs, two bodies.
	/// </para>
	/// <para>
	/// AND THEY HAD ALREADY DRIFTED, which is the whole argument for this class rather than a
	/// preference about tidiness. Three ways, found by reading them side by side:
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// <c>IsSlotBlocked</c>: the grid refused a slot holding an item with no database identity yet
	/// (<c>ID &lt;= 0</c>), because the server keeps such a slot locked until the row lands and
	/// would refuse any request naming it. The equipment copy did not, so a socket could offer a
	/// drag the server was certain to reject.
	/// </description></item>
	/// <item><description>
	/// A slot update repainted the lock overlay in the grid and did not in the equipment panel.
	/// </description></item>
	/// <item><description>
	/// Shift-click quick transfer (issue #197) existed only in the grid. See
	/// <c>UITKEquipment.OnSlotPointerDown</c>, where that is now fixed and said out loud.
	/// </description></item>
	/// </list>
	/// <para>
	/// The first two are settled here, in the single body, in the grid's favour — it is the one
	/// that had the reasoning written down. What is NOT settled here is anything that is genuinely
	/// two operations wearing one name: a drop onto a bag slot is a swap or a split, a drop onto a
	/// socket is an equip, and those speak different halves of the protocol. Those stay in the two
	/// panels, and the click dispatch that reaches them stays with them, because hoisting a
	/// dispatcher whose every branch is abstract buys indirection and no shared behaviour.
	/// <c>SlotPanelSharingTests</c> is what stops that seam from drifting again.
	/// </para>
	/// <para>
	/// The one thing every slot panel must keep for itself is how its slots come into existence.
	/// The equipment sockets are authored in <c>UIEquipment.uxml</c> and found by name; the grids
	/// build theirs in code from the container's slot count. So this class owns what a slot DOES
	/// and never how it is made: it holds the <see cref="slotViews"/> list, and the derived panel
	/// fills it.
	/// </para>
	/// </remarks>
	public abstract class UITKSlotPanelBase : UITKCharacterControl
	{
		// ── What each panel must say about itself ─────────────────────────────

		/// <summary>
		/// Element and USS name prefix for this panel, without a trailing dash.
		/// </summary>
		/// <remarks>
		/// "bank", "inv", "eq". Every panel-specific class name is this prefix plus a fixed
		/// suffix, which is what lets the two USS names this class needs be derived rather than
		/// declared three times. A panel whose markup does not follow the pattern overrides the
		/// names below instead of bending the prefix.
		/// </remarks>
		protected abstract string Prefix { get; }

		/// <summary>The drag and operation-tracker identity of this panel's slots.</summary>
		protected abstract ReferenceButtonType DragType { get; }

		// ── Shared UI overlay names (panels resolved by GameObject name via UIManager) ──

		/// <summary>Name of the shared drag object overlay.</summary>
		protected const string DRAG_OBJECT_NAME = "UIDragObject";
		/// <summary>Name of the shared tooltip overlay.</summary>
		protected const string TOOLTIP_NAME = "UITooltip";
		/// <summary>Name of the shared transient-notice overlay.</summary>
		protected const string TOAST_NAME = "UIToast";

		// ── USS class names ───────────────────────────────────────────────────

		/// <summary>USS class hiding an element.</summary>
		protected virtual string CssHidden => Prefix + "-hidden";

		/// <summary>USS class marking a slot as waiting on the server.</summary>
		/// <remarks>
		/// The same overlay carries two meanings — the container's own slot lock and a request this
		/// panel is waiting on — because to the player they are one statement: this slot is busy,
		/// do not click it. This modifier distinguishes them visually without a second element in
		/// every slot.
		/// </remarks>
		protected virtual string CssLockPending => Prefix + "-slot__lock--pending";

		// ── Per-slot view data ────────────────────────────────────────────────

		/// <summary>Runtime view data for a single slot element.</summary>
		protected struct SlotView
		{
			/// <summary>Root VisualElement of the slot.</summary>
			public VisualElement Root;
			/// <summary>Icon element displaying the item sprite.</summary>
			public VisualElement Icon;
			/// <summary>Stack-count label.</summary>
			public Label Amount;
			/// <summary>Lock overlay element.</summary>
			public VisualElement Lock;
		}

		/// <summary>Slot views indexed by container slot index.</summary>
		/// <remarks>
		/// <para>
		/// Filled by the derived panel, because the two sources of a slot element are not alike:
		/// the grids create one element per container slot, the equipment panel queries ten
		/// authored sockets out of its UXML. What both owe this list is that index <c>i</c> is
		/// container slot <c>i</c> — for equipment that means a missing UXML element still
		/// occupies its place as a default <see cref="SlotView"/> rather than being skipped, or
		/// every socket after it would draw the wrong item.
		/// </para>
		/// <para>
		/// A list rather than an array, and empty rather than null before the tree has been read.
		/// The equipment panel used a null array and had to test for it in eight places; one of
		/// those tests read <c>.Length</c> first and was an NRE on the first equip of any session
		/// in which the player had not opened the window. An empty list fails every bounds check
		/// the same way without a special case.
		/// </para>
		/// </remarks>
		protected readonly List<SlotView> slotViews = new List<SlotView>();

		/// <summary>True while this panel holds a subscription on the shared operation tracker.</summary>
		private bool trackerSubscribed;

		// ── Container access ──────────────────────────────────────────────────

		/// <summary>
		/// This panel's own container, or null when the character does not have one yet.
		/// </summary>
		protected IItemContainer OwnContainer => ResolveContainer(DragType);

		/// <summary>
		/// Resolves the character's container for a drag source type.
		/// </summary>
		/// <remarks>
		/// Every one of these controllers derives from <see cref="IItemContainer"/> —
		/// <see cref="IEquipmentController"/> included — which is what lets one body serve a panel
		/// that thinks in sockets and a panel that thinks in bag slots. That was already true
		/// before this class existed; the panels simply were not leaning on it.
		/// </remarks>
		protected IItemContainer ResolveContainer(ReferenceButtonType type)
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

		// ── UITKControl lifecycle ─────────────────────────────────────────────

		/// <summary>
		/// Joins the shared operation tracker. Derived panels extend this to register broadcasts.
		/// </summary>
		public override void OnClientSet()
		{
			SubscribeTracker();
		}

		/// <summary>
		/// Leaves the shared operation tracker. Derived panels extend this to unregister broadcasts.
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
		/// Re-applies per-open content after the visual tree has been replaced.
		/// </summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Re-applies per-open content on every show, including the very first one.
		/// </summary>
		/// <remarks>
		/// THE CONTRACT, and both panels had written it out separately. Enabling the document
		/// re-clones the UXML, so anything written before <c>Show()</c> is discarded.
		/// <c>OnAfterStarting</c> covers later opens, but on the first ever open <c>hasStarted</c>
		/// is still false and <c>ReinitializeIfTreeReplaced</c> returns before calling it — and a
		/// panel opened by a broadcast has its first open triggered by something the player did
		/// rather than at startup. Both hooks do the work, and both are idempotent.
		/// </remarks>
		protected override void OnAfterShow()
		{
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Re-reads everything this panel shows from the character, without rebuilding the tree.
		/// </summary>
		/// <remarks>
		/// Abstract rather than shared, deliberately. The two bodies do not merely differ in
		/// content, they differ in ORDER, and the order is the part that was hard won: the grid
		/// repaints its currency chip ahead of the container null check because a character with
		/// no container still has money, and the equipment panel clears <c>previewFramed</c> after
		/// the slots so the camera re-frames against the silhouette it can now see. Averaging two
		/// carefully sequenced methods into one is how a refactor quietly undoes both.
		/// </remarks>
		protected abstract void ApplyPerOpenContent();

		/// <summary>
		/// Hides the panel and abandons anything it had in flight.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Closing is not a neutral act: walking out of range of the interactable that opened the
		/// panel is one of the ways the server refuses an operation, so a slot left marked as
		/// waiting after the panel closes has a very good chance of never being answered.
		/// </para>
		/// <para>
		/// <c>Hide(bool)</c> and not <c>Hide()</c>: <c>Hide()</c> delegates here, but Escape
		/// (<c>UIManager.CloseNext</c>) and quit-to-login (<c>Hide(false)</c>) both arrive at this
		/// overload directly.
		/// </para>
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (Visible)
			{
				return;
			}

			OnPanelHidden();
			ReleaseAndClearDrag();
		}

		/// <summary>
		/// Lets a panel release what only it holds once it has actually gone off screen.
		/// </summary>
		/// <remarks>
		/// Runs before the drag is abandoned, which is the order the equipment panel already used:
		/// it hands its preview camera back here, and the cost of that camera is paid every frame
		/// whether or not anyone is looking at it.
		/// </remarks>
		protected virtual void OnPanelHidden()
		{
		}

		/// <summary>
		/// Drops every subscription this class holds when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			UnsubscribeTracker();
			ReleaseAndClearDrag();
			OnPanelDestroying();
			base.OnDestroying();
		}

		/// <summary>
		/// Lets a panel release what only it holds, after the shared subscriptions are gone.
		/// </summary>
		protected virtual void OnPanelDestroying()
		{
		}

		// ── Shared operation tracker ──────────────────────────────────────────

		/// <summary>
		/// Joins the shared item-operation tracker, once.
		/// </summary>
		/// <remarks>
		/// <c>-=</c> before <c>+=</c> on a static event. <c>OnClientSet</c> can run more than once
		/// in a session (quit to login does <c>SetClient(null)</c> then <c>SetClient(client)</c>),
		/// and a static event outlives this component, so a missed unsubscribe is a handler
		/// running forever on a destroyed panel.
		/// </remarks>
		protected void SubscribeTracker()
		{
			if (trackerSubscribed)
			{
				return;
			}
			trackerSubscribed = true;

			ItemOperationTracker.SlotPendingChanged -= OnTrackerSlotPendingChanged;
			ItemOperationTracker.SlotPendingChanged += OnTrackerSlotPendingChanged;
			ItemOperationTracker.ResyncRequested -= OnTrackerResyncRequested;
			ItemOperationTracker.ResyncRequested += OnTrackerResyncRequested;
			ItemOperationTracker.Attach();
		}

		/// <summary>
		/// Leaves the shared item-operation tracker, once.
		/// </summary>
		protected void UnsubscribeTracker()
		{
			if (!trackerSubscribed)
			{
				return;
			}
			trackerSubscribed = false;

			ItemOperationTracker.SlotPendingChanged -= OnTrackerSlotPendingChanged;
			ItemOperationTracker.ResyncRequested -= OnTrackerResyncRequested;
			ItemOperationTracker.Detach();
		}

		/// <summary>
		/// Repaints a slot when it starts or stops waiting on the server.
		/// </summary>
		private void OnTrackerSlotPendingChanged(ReferenceButtonType type, int slot, bool pending)
		{
			if (type != DragType || slot < 0 || slot >= slotViews.Count)
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
			if (type != DragType)
			{
				return;
			}

			IItemContainer container = OwnContainer;
			if (container != null)
			{
				RefreshAllSlots(container);
			}
		}

		// ── Slot visuals ──────────────────────────────────────────────────────

		/// <summary>
		/// Repaints every slot's item and lock state from the container.
		/// </summary>
		/// <remarks>
		/// Clamped to the container as well as to the view, because the two can disagree: a grid
		/// built before the character had a container is a grid of zero slots, and one built
		/// against a tree that has since been replaced still reports a length. A slot with no root
		/// element is skipped rather than treated as broken — the equipment panel deliberately
		/// keeps a placeholder for a socket its UXML does not declare, so that the indices after
		/// it still line up with the container.
		/// </remarks>
		protected void RefreshAllSlots(IItemContainer container)
		{
			if (container == null)
			{
				return;
			}

			int slotCount = Mathf.Min(slotViews.Count, container.Items.Count);
			for (int i = 0; i < slotCount; ++i)
			{
				if (slotViews[i].Root == null)
				{
					continue;
				}

				if (container.TryGetItem(i, out Item item))
				{
					SetSlotItem(i, item);
				}
				else
				{
					ClearSlot(i);
				}

				ApplySlotLockVisual(i, IsSlotBlocked(i));
			}

			OnSlotContentChanged();
		}

		/// <summary>
		/// Told whenever what a slot holds has been repainted.
		/// </summary>
		/// <remarks>
		/// The grids recompute their capacity readout from here. The hook exists rather than a
		/// call to a capacity method because the equipment panel has no capacity: its ten sockets
		/// are not a resource that fills up, and "8 / 10 sockets used" is not a fact a player
		/// needs. That asymmetry was the only real difference between the two <c>SetSlotItem</c>
		/// bodies, so it is the only thing left configurable.
		/// </remarks>
		protected virtual void OnSlotContentChanged()
		{
		}

		/// <summary>
		/// Populates a slot's icon and stack-count badge from an item.
		/// </summary>
		protected void SetSlotItem(int slotIndex, Item item)
		{
			if (item == null || slotIndex < 0 || slotIndex >= slotViews.Count)
			{
				return;
			}

			SlotView view = slotViews[slotIndex];
			if (view.Root == null)
			{
				return;
			}

			OnSlotContentChanged();

			// Placeholder when the template has no icon: an occupied slot must look occupied.
			UITKItemIcon.Apply(view.Icon, item.Template != null ? item.Template.Icon : null);

			if (view.Amount != null)
			{
				if (item.IsStackable && item.Stackable != null)
				{
					view.Amount.text = item.Stackable.Amount.ToString();
					view.Amount.RemoveFromClassList(CssHidden);
				}
				else
				{
					view.Amount.text = "";
					view.Amount.AddToClassList(CssHidden);
				}
			}

			RefreshSlotTooltip(slotIndex, item);
		}

		/// <summary>
		/// Clears a slot's icon and hides its stack-count badge.
		/// </summary>
		protected void ClearSlot(int slotIndex)
		{
			if (slotIndex < 0 || slotIndex >= slotViews.Count)
			{
				return;
			}

			SlotView view = slotViews[slotIndex];
			if (view.Root == null)
			{
				return;
			}

			OnSlotContentChanged();

			UITKItemIcon.Clear(view.Icon);
			if (view.Amount != null)
			{
				view.Amount.text = "";
				view.Amount.AddToClassList(CssHidden);
			}

			RefreshSlotTooltip(slotIndex, null);
		}

		/// <summary>
		/// Keeps the tooltip for a slot in step with what the slot now shows.
		/// </summary>
		/// <remarks>
		/// Called from the two methods that paint a slot, so every path that changes one — a
		/// replicate arriving, a resync, a rebuild, a panel opening — keeps the tooltip honest
		/// without having to remember to. The tooltip was opened from the item the pointer found
		/// on the way in and nothing re-read it, so a swap under a stationary cursor left it
		/// describing the item that used to be in the slot the player is looking at. Issue #280,
		/// which had to be fixed twice — once in the grids and once in the sockets — and is now
		/// one place.
		/// <para>
		/// <see cref="UITKTooltip.RefreshFor"/> does nothing unless the pointer is over THIS slot,
		/// which is what makes the per-slot call safe from the refresh loop above.
		/// </para>
		/// </remarks>
		/// <param name="slotIndex">The slot that was just painted.</param>
		/// <param name="item">What it now holds, or null when it now holds nothing.</param>
		private void RefreshSlotTooltip(int slotIndex, Item item)
		{
			if (slotIndex < 0 || slotIndex >= slotViews.Count)
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
		protected void ApplySlotLockVisual(int slotIndex, bool isLocked)
		{
			if (slotIndex < 0 || slotIndex >= slotViews.Count)
			{
				return;
			}

			VisualElement lockEl = slotViews[slotIndex].Lock;
			if (lockEl == null)
			{
				return;
			}

			lockEl.EnableInClassList(CssHidden, !isLocked);
			lockEl.EnableInClassList(CssLockPending,
				isLocked && ItemOperationTracker.IsPending(DragType, slotIndex));
		}

		/// <summary>
		/// Reports whether a slot is unavailable for a new request, for any reason.
		/// </summary>
		/// <remarks>
		/// /* DELIBERATE BEHAVIOUR CHANGE, equipment panel. */ The two copies of this test had
		/// drifted: the grid refused a slot whose item has no database identity yet, and the
		/// equipment copy stopped at the container's own lock. The grid is right and the socket
		/// was wrong, so the socket now follows it. An item with <c>ID &lt;= 0</c> is one the
		/// database has not written yet; the server keeps its slot locked until the row lands and
		/// re-sends the slot with the assigned id, so until then every request naming it would be
		/// refused. Showing it as waiting costs the player nothing and saves a round trip that
		/// ends in a refusal they did not ask for. The practical effect on sockets is small —
		/// equipped items have identities — which is exactly why the divergence survived unnoticed.
		/// </remarks>
		protected bool IsSlotBlocked(int slotIndex)
		{
			if (ItemOperationTracker.IsPending(DragType, slotIndex))
			{
				return true;
			}

			IItemContainer container = OwnContainer;
			if (container == null)
			{
				return false;
			}
			if (container.IsSlotLocked(slotIndex))
			{
				return true;
			}

			return container.TryGetItem(slotIndex, out Item item) && item != null && item.ID <= 0;
		}

		/// <summary>
		/// Applies a slot the server has sent back: releases its pending mark, repaints it, and
		/// tells any drag that was started from it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The slot arriving from the server IS the acknowledgement of whatever this panel asked
		/// for, so the pending mark is released here rather than on a separate reply message. What
		/// the player is waiting to see is the slot.
		/// </para>
		/// <para>
		/// /* DELIBERATE BEHAVIOUR CHANGE, equipment panel. */ The grid repainted the lock overlay
		/// after applying an update and the equipment copy did not. The item itself can be the
		/// reason a slot is blocked (no identity yet), and the slot arriving with its identity is
		/// what unblocks it, so the repaint belongs on every path. It is idempotent — the release
		/// above already raises <c>SlotPendingChanged</c>, which repaints the same overlay — so
		/// the socket gains correctness in the one case it was missing and nothing else.
		/// </para>
		/// </remarks>
		/// <param name="container">The container the update came from.</param>
		/// <param name="item">What the slot now holds, as the event reported it.</param>
		/// <param name="slotIndex">The slot that changed.</param>
		protected void ApplySlotUpdate(IItemContainer container, Item item, int slotIndex)
		{
			if (container == null || slotIndex < 0)
			{
				return;
			}

			/* An index the view does not have means the view is smaller than the container — built
			 * before the container was sized, or against a tree that has been replaced. Rebuilding
			 * is the recovery where a panel can rebuild; dropping the update silently is what left
			 * a grid showing fewer slots than the character actually has. */
			if (slotIndex >= slotViews.Count &&
				(!TryGrowSlotsFor(slotIndex) || slotIndex >= slotViews.Count))
			{
				return;
			}

			ItemOperationTracker.Release(DragType, slotIndex);

			OnSlotUpdateReceived(slotIndex);

			bool empty = container.IsSlotEmpty(slotIndex);
			if (!empty)
			{
				SetSlotItem(slotIndex, item);
			}
			else
			{
				ClearSlot(slotIndex);
			}

			ApplySlotLockVisual(slotIndex, IsSlotBlocked(slotIndex));

			// A drag started from this slot no longer refers to what it was started from.
			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				dragObject.NotifySlotChanged(DragType, slotIndex, empty ? null : item);
			}
		}

		/// <summary>
		/// Asked to make room for a slot index the view does not have. Returns true if it grew.
		/// </summary>
		/// <remarks>
		/// False by default, because not every panel can: the equipment sockets are authored in
		/// UXML and there is no eleventh one to create, so an out-of-range socket index is a bug
		/// upstream rather than a view to repair. The grids rebuild from the container's count.
		/// </remarks>
		protected virtual bool TryGrowSlotsFor(int slotIndex)
		{
			return false;
		}

		/// <summary>
		/// Told that a slot update has been accepted, before the slot is repainted.
		/// </summary>
		/// <remarks>
		/// The equipment panel invalidates its preview framing here: equipment changes the
		/// silhouette, a two-handed weapon reaches further than a dagger, and a frame sized before
		/// it existed crops it.
		/// </remarks>
		protected virtual void OnSlotUpdateReceived(int slotIndex)
		{
		}

		// ── Slot interaction ──────────────────────────────────────────────────

		/// <summary>
		/// Shows the item tooltip when the pointer enters a slot that contains an item.
		/// </summary>
		/// <param name="slotIndex">The slot the pointer entered.</param>
		/// <param name="owner">The slot element, so the tooltip can close itself if it is rebuilt.</param>
		protected void OnSlotPointerEnter(int slotIndex, VisualElement owner)
		{
			IItemContainer container = OwnContainer;
			if (container == null || !container.TryGetItem(slotIndex, out Item item))
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
		/// Hides the item tooltip when the pointer leaves a slot.
		/// </summary>
		protected void OnSlotPointerLeave(VisualElement owner)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				// HideFor, so a stale leave cannot close a tooltip another slot has since opened.
				tooltip.HideFor(owner);
			}
		}

		/// <summary>
		/// Starts a drag from an occupied slot.
		/// </summary>
		/// <remarks>
		/// The refusal is logged, not just the success. A drag that will not start looks identical
		/// to a click the game did not receive, and the three reasons it can refuse — the slot is
		/// waiting, the slot is empty, the item is gone — are indistinguishable from the outside.
		/// </remarks>
		protected void BeginDragFromSlot(UITKDragObject dragObject, IItemContainer container, int slotIndex)
		{
			Item item = null;
			bool blocked = IsSlotBlocked(slotIndex);
			bool gotItem = container != null && container.TryGetItem(slotIndex, out item);

			if (blocked || !gotItem || item == null)
			{
				FishMMO.Logging.Log.Debug(GetType().Name,
					$"BeginDrag REFUSED slot {slotIndex}: blocked={blocked} " +
					$"(pending={ItemOperationTracker.IsPending(DragType, slotIndex)}) " +
					$"gotItem={gotItem} itemNull={item == null}.");
				return;
			}

			// A missing icon must not prevent the item being moved.
			Sprite sprite = item.Template != null ? item.Template.Icon : null;

			/* Carry the item, not just the slot number: the slot index stops being true the moment
			 * anything else writes to that slot, and the drop would then move the wrong item. */
			dragObject.SetItemReference(sprite, slotIndex, DragType, item);
		}

		/// <summary>
		/// Abandons this panel's in-flight operations and any drag that started here.
		/// </summary>
		protected void ReleaseAndClearDrag()
		{
			ItemOperationTracker.ReleaseAll(DragType);

			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) &&
				dragObject.IsDragging &&
				dragObject.Type == DragType)
			{
				dragObject.Clear();
			}
		}
	}
}
