using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the corpse loot panel.
	/// Binds to <c>UILoot.uxml</c> / <c>UILoot.uss</c> and renders a dead NPC's contents as a
	/// list of rows plus a currency line.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A pure view of server state. Nothing here removes an item: a click sends a request naming
	/// a slot index, the row is marked as waiting, and what changes the display is the server
	/// broadcasting the corpse's contents back. That indirection is not ceremony — the pile is
	/// shared between everyone who earned rights to it, so the item a player is looking at may
	/// already be gone, and a client that optimistically removed rows would routinely show a
	/// corpse that is emptier than it really is.
	/// </para>
	/// <para>
	/// For the same reason the panel re-renders from whole snapshots rather than deltas. Every
	/// successful take by ANY looter causes the server to re-send the full contents to every
	/// viewer, so a client that missed an update still converges.
	/// </para>
	/// </remarks>
	public class UITKLoot : UITKCharacterControl
	{
		/// <summary>
		/// Opens the inventory alongside this panel when it is shown, if the inventory is closed.
		/// An open inventory is left alone. Issue #208.
		/// </summary>
		[Header("Interaction")]
		[Tooltip("Open the inventory panel when this panel opens (if it is closed). An open inventory is left as it is.")]
		[SerializeField]
		private bool openInventoryOnShow = false;

		/// <inheritdoc />
		protected override bool OpensInventoryOnShow => openInventoryOnShow;

		// ── UXML element names ────────────────────────────────────────────────

		/// <summary>Name of the container that runtime rows are appended to.</summary>
		private const string LIST_NAME = "loot-list";
		/// <summary>Name of the header line showing the corpse's name.</summary>
		private const string SUBTITLE_NAME = "header-subtitle";
		/// <summary>Name of the currency row container.</summary>
		private const string CURRENCY_ROW_NAME = "loot-currency-row";
		/// <summary>Name of the currency amount label.</summary>
		private const string CURRENCY_LABEL_NAME = "loot-currency-label";
		/// <summary>Name of the label shown when the corpse is empty.</summary>
		private const string EMPTY_NAME = "loot-empty";
		/// <summary>Name of the footer status line.</summary>
		private const string STATUS_NAME = "loot-status";
		/// <summary>Name of the take-all button.</summary>
		private const string TAKE_ALL_NAME = "loot-take-all";
		/// <summary>Name of the close button.</summary>
		private const string CLOSE_BTN_NAME = "close-button";

		// ── Shared UI overlay names (panels resolved by GameObject name via UIManager) ──

		/// <summary>Name of the shared tooltip panel.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		// ── USS class names ───────────────────────────────────────────────────

		/// <summary>Theme class shared with every other item slot in the game.</summary>
		private const string CSS_SLOT = "fish-slot";
		/// <summary>Layout class for a loot row.</summary>
		private const string CSS_ROW = "loot-row";
		/// <summary>Theme class for a slot icon.</summary>
		private const string CSS_ICON = "fish-slot__icon";
		/// <summary>Layout class for a loot row icon.</summary>
		private const string CSS_ICON_LAYOUT = "loot-row__icon";
		/// <summary>Theme + layout class for a row's item name.</summary>
		private const string CSS_NAME = "fish-label";
		/// <summary>Layout class for a row's item name.</summary>
		private const string CSS_NAME_LAYOUT = "loot-row__name";
		/// <summary>Theme class for a stack-count label.</summary>
		private const string CSS_AMOUNT = "fish-slot__amount";
		/// <summary>Layout class for a row's stack count.</summary>
		private const string CSS_AMOUNT_LAYOUT = "loot-row__amount";
		/// <summary>Theme class for the waiting overlay.</summary>
		private const string CSS_PENDING = "fish-slot__lock";
		/// <summary>Layout class for the waiting overlay.</summary>
		private const string CSS_PENDING_LAYOUT = "loot-row__pending";
		/// <summary>Hides an element.</summary>
		private const string CSS_HIDDEN = "loot-hidden";
		/// <summary>Hides the currency row.</summary>
		private const string CSS_CURRENCY_HIDDEN = "loot-currency--hidden";

		/// <summary>Slot value meaning "not an item slot" — currency and take-all.</summary>
		private const int NON_ITEM_SLOT = -1;

		/// <summary>
		/// Seconds a request may go unanswered before its row is made clickable again.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The server replies to every take, successful or not, so this should never fire. It
		/// exists because the alternative failure mode is unrecoverable: a reply lost to a dropped
		/// connection would leave the row waiting forever, and the player cannot clear it without
		/// closing a window whose corpse is about to decay.
		/// </para>
		/// <para>
		/// Passed explicitly to every <see cref="ItemSlotPendingSet.TryBegin"/> and
		/// <see cref="PendingReplyGuard.Begin"/> call below rather than letting the shared types
		/// apply their own defaults, which are longer — 8s for an item operation, 30s for a login
		/// round trip. A corpse is a shared pile that another looter is emptying while this panel
		/// watches, so a row locked for eight seconds after a lost reply is a row the player
		/// watches somebody else take.
		/// </para>
		/// </remarks>
		private const float PENDING_TIMEOUT_SECONDS = 5f;

		// ── Per-row view data ─────────────────────────────────────────────────

		/// <summary>
		/// One rendered loot row.
		/// </summary>
		private struct RowView
		{
			/// <summary>Root element of the row.</summary>
			public VisualElement Root;
			/// <summary>The corpse slot index this row addresses.</summary>
			public int Slot;
			/// <summary>Overlay shown while a take is in flight.</summary>
			public VisualElement Pending;
			/// <summary>What the row describes, built once when the row is created.</summary>
			public TooltipContent Tooltip;
		}

		// ── Private state ─────────────────────────────────────────────────────

		/// <summary>The corpse currently displayed, or 0 when the panel holds nothing.</summary>
		private long corpseID;

		/// <summary>The most recent contents the server sent for <see cref="corpseID"/>.</summary>
		private CorpseLootSlotData[] slotData = new CorpseLootSlotData[0];

		/// <summary>Currency remaining on the displayed corpse.</summary>
		private long corpseCurrency;

		/// <summary>Display name of the displayed corpse.</summary>
		private string corpseName = string.Empty;

		/// <summary>Rendered rows, one per non-empty corpse slot.</summary>
		private readonly List<RowView> rowViews = new List<RowView>();

		/*  Both of these used to be hand-rolled here: a Dictionary<int, float> of send times and a
		 *  float sentinel, swept against Time.time by the tick below. That was a live bug, not just
		 *  duplication. Time.time is scaled by Time.timeScale, so anything that pauses or slows the
		 *  game — a settings menu, a death screen, a cutscene — stops this panel's watchdog dead
		 *  while the network keeps running. A take whose reply is lost during a pause left its row
		 *  locked with nothing able to release it, and at timeScale 0 the row stayed locked for the
		 *  rest of the corpse's life however long the player waited.
		 *
		 *  ItemSlotPendingSet and PendingReplyGuard both measure in Time.unscaledTime, which is the
		 *  only correct clock for a deadline on something the server is doing: the server does not
		 *  slow down when this client opens a menu. Adopting them fixes the clock and removes the
		 *  second copy of the sweep in one move — see PendingSlotWatchdogTests for the guard that
		 *  keeps a third copy from appearing. */

		/// <summary>
		/// Corpse slots with a take in flight.
		/// </summary>
		/// <remarks>
		/// Keyed by slot rather than by row index because rows are rebuilt whenever the server
		/// re-sends the contents, and a row's position moves as other looters empty slots above
		/// it. The slot index is the only identifier that survives a rebuild.
		/// </remarks>
		private readonly ItemSlotPendingSet pendingSlots = new ItemSlotPendingSet();

		/// <summary>
		/// Watchdog for a currency or take-all request, which are not per-slot.
		/// </summary>
		/// <remarks>
		/// A single <see cref="PendingReplyGuard"/> rather than a slot in
		/// <see cref="pendingSlots"/>, because that is exactly what this is: one wait, not a set of
		/// them. Currency and take-all have no slot index — the server answers both with
		/// <see cref="NON_ITEM_SLOT"/> — and giving them a fake key in a map of corpse slots would
		/// make every reader of that map responsible for remembering which key is not a slot.
		/// <see cref="ItemSlotPendingSet"/> is the same guard keyed by slot, so this shares its
		/// clock and its self-clearing timeout without pretending to be per-slot state.
		/// </remarks>
		private readonly PendingReplyGuard bulkPending = new PendingReplyGuard();

		/// <summary>Reused by the prune below so a corpse refresh allocates nothing.</summary>
		private readonly List<int> pendingScratch = new List<int>();

		/// <summary>The container runtime rows are appended to.</summary>
		private VisualElement listRoot;
		/// <summary>Header line showing the corpse's name.</summary>
		private Label subtitleLabel;
		/// <summary>The currency row container.</summary>
		private VisualElement currencyRow;
		/// <summary>The currency amount label.</summary>
		private Label currencyLabel;
		/// <summary>Label shown when the corpse holds nothing.</summary>
		private Label emptyLabel;
		/// <summary>Footer status line for refusals.</summary>
		private Label statusLabel;
		/// <summary>The take-all button.</summary>
		private Button takeAllButton;

		// ── UITKControl lifecycle ─────────────────────────────────────────────

		/// <summary>
		/// Queries named elements and wires up the header and footer buttons.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			listRoot = root.Q(LIST_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			currencyRow = root.Q(CURRENCY_ROW_NAME);
			currencyLabel = root.Q<Label>(CURRENCY_LABEL_NAME);
			emptyLabel = root.Q<Label>(EMPTY_NAME);
			statusLabel = root.Q<Label>(STATUS_NAME);
			takeAllButton = root.Q<Button>(TAKE_ALL_NAME);

			Button closeBtn = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeBtn != null)
			{
				closeBtn.clicked += Hide;
			}

			if (takeAllButton != null)
			{
				takeAllButton.clicked += RequestTakeAll;
			}

			if (currencyRow != null)
			{
				currencyRow.RegisterCallback<PointerDownEvent>(OnCurrencyPointerDown);
			}
		}

		/// <summary>
		/// Rebuilds the rows after the visual tree has been replaced.
		/// </summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Fills the panel on every show, including the very first one.
		/// </summary>
		/// <remarks>
		/// Enabling the document re-clones the UXML, so anything written before <c>Show()</c> is
		/// discarded. This panel's first open is always driven by a server broadcast rather than
		/// by startup, which is precisely the case where <c>OnAfterStarting</c> alone is not
		/// enough — see the same note on the bank panel. Both hooks do the work and both are
		/// idempotent.
		/// </remarks>
		protected override void OnAfterShow()
		{
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Registers the corpse loot broadcast handlers.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<CorpseLootBroadcast>(OnClientCorpseLootBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<CorpseLootResultBroadcast>(OnClientCorpseLootResultBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<CorpseLootCloseWindowBroadcast>(OnClientCorpseLootCloseWindowBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the corpse loot broadcast handlers.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CorpseLootBroadcast>(OnClientCorpseLootBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CorpseLootResultBroadcast>(OnClientCorpseLootResultBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CorpseLootCloseWindowBroadcast>(OnClientCorpseLootCloseWindowBroadcastReceived);
		}

		/// <summary>
		/// Releases rows whose reply never arrived.
		/// </summary>
		protected override void OnTick()
		{
			ReleaseTimedOutRequests();
		}

		/// <summary>
		/// Destroys runtime rows when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			DestroyRows();
			base.OnDestroying();
		}

		/// <summary>
		/// Hides the panel and tells the server this player is no longer looking at the corpse.
		/// </summary>
		/// <remarks>
		/// Telling the server matters. It keeps a viewer list per corpse so one looter's take can
		/// refresh everyone else's window; a client that closed without saying so would keep
		/// receiving broadcasts for a panel nobody can see, for as long as the body lasts.
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			bool wasVisible = Visible;

			base.Hide(overrideIsAlwaysOpen);

			if (Visible || !wasVisible)
			{
				return;
			}

			if (corpseID != 0 && Client != null)
			{
				Client.Broadcast(new CorpseLootCloseBroadcast()
				{
					InteractableID = corpseID,
				}, Channel.Reliable);
			}

			ClearCorpse();
		}

		// ── Broadcast handling ────────────────────────────────────────────────

		/// <summary>
		/// Displays a corpse's contents, opening the panel if it is not already open.
		/// </summary>
		/// <remarks>
		/// Serves double duty as the open message and the refresh message. The server re-sends the
		/// whole contents after every successful take by any looter, which is what keeps a shared
		/// pile consistent between the players standing over it.
		/// </remarks>
		private void OnClientCorpseLootBroadcastReceived(CorpseLootBroadcast msg, Channel channel)
		{
			/* A different corpse means every pending slot index refers to a body this panel is no
			 * longer showing. Carrying them over would grey out arbitrary rows of the new one. */
			if (corpseID != msg.InteractableID)
			{
				/* Resign as a viewer of the corpse being replaced. The server keeps a viewer list
				 * per corpse so one looter's take refreshes everyone else's window; a player who
				 * walks from one body straight to the next would otherwise stay subscribed to
				 * every corpse they had ever opened, receiving refreshes for all of them until
				 * each decayed. */
				if (corpseID != 0 && Client != null)
				{
					Client.Broadcast(new CorpseLootCloseBroadcast()
					{
						InteractableID = corpseID,
					}, Channel.Reliable);
				}

				pendingSlots.ReleaseAll();
				bulkPending.Clear();
				SetStatus(string.Empty);
			}

			corpseID = msg.InteractableID;
			corpseName = msg.CorpseName ?? string.Empty;
			slotData = msg.Items ?? new CorpseLootSlotData[0];
			corpseCurrency = msg.Currency;

			/* Drop pending marks for slots the corpse no longer has. Without this, losing a race
			 * to another looter would leave the request that lost it pending until it timed out —
			 * on a row that is not even on screen any more. */
			PrunePendingSlots();

			if (!Visible)
			{
				// Show() ends in OnAfterShow, which renders; rendering here as well would write
				// into a tree the document is about to discard and re-clone.
				Show();
				return;
			}

			ApplyPerOpenContent();
		}

		/// <summary>
		/// Releases a request's pending mark and reports why it was refused.
		/// </summary>
		private void OnClientCorpseLootResultBroadcastReceived(CorpseLootResultBroadcast msg, Channel channel)
		{
			if (msg.InteractableID != corpseID)
			{
				return;
			}

			if (msg.Slot == NON_ITEM_SLOT)
			{
				bulkPending.Clear();
			}
			else
			{
				pendingSlots.Release(msg.Slot);
			}

			SetStatus(msg.Success ? string.Empty : DescribeFailure(msg.Reason));

			if (Visible)
			{
				ApplyPerOpenContent();
			}
		}

		/// <summary>
		/// Closes the window when the server retires the corpse.
		/// </summary>
		/// <remarks>
		/// Sent when the body decays, empties, or the looter walks away. Closing on the server's
		/// say-so is what stops the panel outliving the scene object ID its buttons submit against
		/// — an ID that a pooled NPC will eventually reuse for a different creature.
		/// </remarks>
		private void OnClientCorpseLootCloseWindowBroadcastReceived(CorpseLootCloseWindowBroadcast msg, Channel channel)
		{
			if (msg.InteractableID != corpseID)
			{
				return;
			}

			/* Cleared BEFORE hiding. Hide sends a close notice to the server for whatever corpse
			 * this panel holds, and answering the server's own close with a close is pure noise. */
			ClearCorpse();
			Hide();
		}

		// ── Requests ──────────────────────────────────────────────────────────

		/// <summary>
		/// Asks the server for one item.
		/// </summary>
		/// <param name="slot">The corpse slot to take.</param>
		private void RequestTakeItem(int slot)
		{
			if (corpseID == 0 || Client == null)
			{
				return;
			}

			// One request per slot at a time. A shared pile makes double-clicking a row the
			// natural reaction to it not disappearing immediately. TryBegin refusing rather than
			// re-arming is what lets a genuinely stuck row time out: re-arming would push the
			// deadline out on every impatient click.
			if (!pendingSlots.TryBegin(slot, PENDING_TIMEOUT_SECONDS))
			{
				return;
			}

			ApplyPendingVisuals();

			Client.Broadcast(new CorpseLootTakeItemBroadcast()
			{
				InteractableID = corpseID,
				Slot = slot,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Asks the server for the corpse's currency.
		/// </summary>
		private void RequestTakeCurrency()
		{
			if (corpseID == 0 || Client == null || corpseCurrency < 1 || bulkPending.IsPending)
			{
				return;
			}

			bulkPending.Begin(PENDING_TIMEOUT_SECONDS);
			ApplyPendingVisuals();

			Client.Broadcast(new CorpseLootTakeCurrencyBroadcast()
			{
				InteractableID = corpseID,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Asks the server for everything on the corpse.
		/// </summary>
		private void RequestTakeAll()
		{
			if (corpseID == 0 || Client == null || bulkPending.IsPending)
			{
				return;
			}

			bulkPending.Begin(PENDING_TIMEOUT_SECONDS);
			ApplyPendingVisuals();

			Client.Broadcast(new CorpseLootTakeAllBroadcast()
			{
				InteractableID = corpseID,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Re-enables requests whose reply never arrived.
		/// </summary>
		/// <remarks>
		/// Both watchdogs are self-clearing and report a timeout exactly once, so this can be driven
		/// straight from the tick without tracking anything itself — and a late reply arriving after
		/// the release is still handled normally, because releasing only re-enables the row.
		/// </remarks>
		private void ReleaseTimedOutRequests()
		{
			bool changed = bulkPending.HasExpired();

			if (pendingSlots.HasAnyPending && pendingSlots.CollectExpired().Count > 0)
			{
				changed = true;
			}

			if (changed && Visible)
			{
				ApplyPendingVisuals();
			}
		}

		// ── Rendering ─────────────────────────────────────────────────────────

		/// <summary>
		/// Renders the whole panel from the last snapshot the server sent.
		/// </summary>
		private void ApplyPerOpenContent()
		{
			if (Root == null || listRoot == null)
			{
				return;
			}

			if (subtitleLabel != null)
			{
				subtitleLabel.text = corpseName;
			}

			RebuildRows();
			ApplyCurrency();
			ApplyEmptyState();
			ApplyPendingVisuals();
		}

		/// <summary>
		/// Destroys and recreates one row per non-empty corpse slot.
		/// </summary>
		/// <remarks>
		/// A full rebuild rather than a diff. A corpse holds at most a handful of items and is
		/// re-rendered only when its contents actually change, so the cost is trivial next to the
		/// class of bug a hand-written diff invites here — rows whose captured slot index no
		/// longer matches the row they are attached to, which silently loots the wrong item.
		/// </remarks>
		private void RebuildRows()
		{
			DestroyRows();

			for (int i = 0; i < slotData.Length; ++i)
			{
				CorpseLootSlotData data = slotData[i];
				BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(data.TemplateID);
				if (template == null)
				{
					// An unknown template means the client's item cache disagrees with the
					// server's. Skipping the row is right: it cannot be labelled or pictured, and
					// offering an unnamed row invites a take the player cannot evaluate.
					continue;
				}

				rowViews.Add(CreateRow(data, template));
			}
		}

		/// <summary>
		/// Builds one loot row.
		/// </summary>
		/// <param name="data">The slot this row addresses.</param>
		/// <param name="template">The item in that slot.</param>
		/// <returns>The row's view record.</returns>
		private RowView CreateRow(CorpseLootSlotData data, BaseItemTemplate template)
		{
			VisualElement rowRoot = new VisualElement();
			rowRoot.AddToClassList(CSS_SLOT);
			rowRoot.AddToClassList(CSS_ROW);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(CSS_ICON);
			icon.AddToClassList(CSS_ICON_LAYOUT);
			UITKItemIcon.Apply(icon, template.Icon);
			rowRoot.Add(icon);

			Label name = new Label(template.Name);
			name.AddToClassList(CSS_NAME);
			name.AddToClassList(CSS_NAME_LAYOUT);
			rowRoot.Add(name);

			Label amount = new Label();
			amount.AddToClassList(CSS_AMOUNT);
			amount.AddToClassList(CSS_AMOUNT_LAYOUT);
			if (data.Amount > 1)
			{
				amount.text = data.Amount.ToString();
			}
			else
			{
				amount.text = string.Empty;
				amount.AddToClassList(CSS_HIDDEN);
			}
			rowRoot.Add(amount);

			VisualElement pending = new VisualElement();
			pending.AddToClassList(CSS_PENDING);
			pending.AddToClassList(CSS_PENDING_LAYOUT);
			pending.AddToClassList(CSS_HIDDEN);
			rowRoot.Add(pending);

			// Captured by value. The row is discarded on the next rebuild, so this closure can
			// never outlive the slot it names.
			int capturedSlot = data.Slot;
			rowRoot.RegisterCallback<PointerDownEvent>(evt => OnRowPointerDown(evt, capturedSlot));
			rowRoot.RegisterCallback<PointerEnterEvent>(evt => OnRowPointerEnter(rowRoot));
			rowRoot.RegisterCallback<PointerLeaveEvent>(evt => OnRowPointerLeave(rowRoot));

			listRoot.Add(rowRoot);

			RowView view;
			view.Root = rowRoot;
			view.Slot = capturedSlot;
			view.Pending = pending;
			view.Tooltip = template.BuildContent();
			return view;
		}

		/// <summary>
		/// Removes every runtime row.
		/// </summary>
		/// <remarks>
		/// <c>RemoveFromHierarchy</c> rather than <c>listRoot.Remove</c>: after the document
		/// re-clones the UXML these roots belong to the previous tree, and
		/// <c>VisualElement.Remove</c> throws for an element that is not its child — which would
		/// abandon the rebuild and leave the panel permanently blank.
		/// </remarks>
		private void DestroyRows()
		{
			for (int i = 0; i < rowViews.Count; ++i)
			{
				rowViews[i].Root?.RemoveFromHierarchy();
			}
			rowViews.Clear();
		}

		/// <summary>
		/// Shows or hides the currency line and sets its amount.
		/// </summary>
		private void ApplyCurrency()
		{
			if (currencyRow == null)
			{
				return;
			}

			if (corpseCurrency > 0)
			{
				currencyRow.RemoveFromClassList(CSS_CURRENCY_HIDDEN);
				if (currencyLabel != null)
				{
					currencyLabel.text = corpseCurrency.ToString();
				}
			}
			else
			{
				currencyRow.AddToClassList(CSS_CURRENCY_HIDDEN);
			}
		}

		/// <summary>
		/// Shows the empty message when there is nothing left, and disables take-all with it.
		/// </summary>
		private void ApplyEmptyState()
		{
			bool empty = rowViews.Count < 1 && corpseCurrency < 1;

			if (emptyLabel != null)
			{
				if (empty)
				{
					emptyLabel.RemoveFromClassList(CSS_HIDDEN);
				}
				else
				{
					emptyLabel.AddToClassList(CSS_HIDDEN);
				}
			}

			if (takeAllButton != null)
			{
				takeAllButton.SetEnabled(!empty);
			}
		}

		/// <summary>
		/// Applies the waiting overlay to rows with a request in flight.
		/// </summary>
		private void ApplyPendingVisuals()
		{
			bool bulkWaiting = bulkPending.IsPending;

			for (int i = 0; i < rowViews.Count; ++i)
			{
				RowView view = rowViews[i];
				if (view.Pending == null)
				{
					continue;
				}

				// A take-all covers every row, so all of them wait on it.
				bool waiting = bulkWaiting || pendingSlots.IsPending(view.Slot);
				if (waiting)
				{
					view.Pending.RemoveFromClassList(CSS_HIDDEN);
				}
				else
				{
					view.Pending.AddToClassList(CSS_HIDDEN);
				}
			}

			if (takeAllButton != null && (bulkWaiting || rowViews.Count > 0 || corpseCurrency > 0))
			{
				takeAllButton.SetEnabled(!bulkWaiting && (rowViews.Count > 0 || corpseCurrency > 0));
			}
		}

		/// <summary>
		/// Forgets pending marks for slots the corpse no longer holds.
		/// </summary>
		private void PrunePendingSlots()
		{
			if (!pendingSlots.HasAnyPending)
			{
				return;
			}

			/* Enumerated rather than asked slot by slot, because the slots worth releasing are the
			 * ones this panel no longer has — there is nothing left on screen to ask about. */
			pendingScratch.Clear();
			pendingSlots.CollectPending(pendingScratch);

			for (int i = 0; i < pendingScratch.Count; ++i)
			{
				int slot = pendingScratch[i];

				bool stillPresent = false;
				for (int j = 0; j < slotData.Length; ++j)
				{
					if (slotData[j].Slot == slot)
					{
						stillPresent = true;
						break;
					}
				}

				if (!stillPresent)
				{
					pendingSlots.Release(slot);
				}
			}
		}

		/// <summary>
		/// Forgets the displayed corpse entirely.
		/// </summary>
		private void ClearCorpse()
		{
			corpseID = 0;
			corpseName = string.Empty;
			corpseCurrency = 0;
			slotData = new CorpseLootSlotData[0];
			pendingSlots.ReleaseAll();
			bulkPending.Clear();
			SetStatus(string.Empty);
			DestroyRows();
		}

		/// <summary>
		/// Writes the footer status line.
		/// </summary>
		/// <param name="text">The message, or empty to clear it.</param>
		private void SetStatus(string text)
		{
			if (statusLabel != null)
			{
				statusLabel.text = text ?? string.Empty;
			}
		}

		/// <summary>
		/// Turns a refusal into something the player can act on.
		/// </summary>
		/// <param name="reason">The server's reason.</param>
		/// <returns>A short message for the footer.</returns>
		private static string DescribeFailure(CorpseLootFailureReason reason)
		{
			switch (reason)
			{
				case CorpseLootFailureReason.NoCorpse:
					return "The corpse is gone.";
				case CorpseLootFailureReason.NotEligible:
					return "You did not help kill this.";
				case CorpseLootFailureReason.OutOfRange:
					return "Too far away.";
				case CorpseLootFailureReason.AlreadyTaken:
					return "Someone else took it.";
				case CorpseLootFailureReason.InventoryFull:
					return "Your inventory is full.";
				default:
					return "That could not be looted.";
			}
		}

		// ── Input ─────────────────────────────────────────────────────────────

		/// <summary>
		/// Takes the item in a row on left click.
		/// </summary>
		private void OnRowPointerDown(PointerDownEvent evt, int slot)
		{
			if (evt.button != 0)
			{
				return;
			}
			evt.StopPropagation();
			RequestTakeItem(slot);
		}

		/// <summary>
		/// Takes the corpse's currency on left click.
		/// </summary>
		private void OnCurrencyPointerDown(PointerDownEvent evt)
		{
			if (evt.button != 0)
			{
				return;
			}
			evt.StopPropagation();
			RequestTakeCurrency();
		}

		/// <summary>
		/// Shows the item tooltip for a hovered row.
		/// </summary>
		private void OnRowPointerEnter(VisualElement rowRoot)
		{
			for (int i = 0; i < rowViews.Count; ++i)
			{
				if (!ReferenceEquals(rowViews[i].Root, rowRoot))
				{
					continue;
				}

				TooltipContent tooltip = rowViews[i].Tooltip;
				if (tooltip != null && !tooltip.IsEmpty &&
					UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltipPanel))
				{
					/* With the row as owner, so the tooltip closes itself when the row is
					 * destroyed under the pointer. That is the common case here rather than an
					 * edge one: another looter taking an item rebuilds every row while the player
					 * is hovering one, and an unowned tooltip would be left describing an item
					 * that is no longer on the body. */
					tooltipPanel.Open(tooltip, rowRoot);
				}
				return;
			}
		}

		/// <summary>
		/// Hides the item tooltip.
		/// </summary>
		/// <remarks>
		/// <c>HideFor</c>, so a leave event arriving after another row has already opened its own
		/// tooltip cannot close it.
		/// </remarks>
		private void OnRowPointerLeave(VisualElement rowRoot)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltipPanel))
			{
				tooltipPanel.HideFor(rowRoot);
			}
		}
	}
}
