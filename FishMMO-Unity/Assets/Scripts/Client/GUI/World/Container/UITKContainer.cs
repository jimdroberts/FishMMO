using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the world container panel — chests, crates, wardrobes.
	/// Binds to <c>UIContainer.uxml</c> / <c>UIContainer.uss</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A pure view of server state, for the same reason the corpse loot panel is one: a container
	/// in the world is not private, so the item a player is looking at may already have been taken
	/// by somebody else standing at the same chest. Nothing here removes a row — a click sends a
	/// request naming a slot index, the row is marked as waiting, and what changes the display is
	/// the server sending the container's contents back.
	/// </para>
	/// <para>
	/// <c>ContainerOpenBroadcast</c> serves as both the open message and the refresh, so there is
	/// exactly one code path for "here is what is in the box". A client that missed an update
	/// converges on the next one rather than compounding the error.
	/// </para>
	/// </remarks>
	public class UITKContainer : UITKCharacterControl
	{
		// ── UXML element names ────────────────────────────────────────────────

		private const string LIST_NAME = "container-list";
		private const string SUBTITLE_NAME = "header-subtitle";
		private const string EMPTY_NAME = "container-empty";
		private const string STATUS_NAME = "container-status";
		private const string TAKE_ALL_NAME = "container-take-all";
		private const string CLOSE_BTN_NAME = "close-button";

		// ── USS class names ───────────────────────────────────────────────────

		private const string CSS_SLOT = "fish-slot";
		private const string CSS_ROW = "container-row";
		private const string CSS_ICON = "fish-slot__icon";
		private const string CSS_ICON_LAYOUT = "container-row__icon";
		private const string CSS_NAME = "fish-label";
		private const string CSS_NAME_LAYOUT = "container-row__name";
		private const string CSS_AMOUNT = "fish-slot__amount";
		private const string CSS_AMOUNT_LAYOUT = "container-row__amount";
		private const string CSS_PENDING = "fish-slot__lock";
		private const string CSS_PENDING_LAYOUT = "container-row__pending";
		private const string CSS_HIDDEN = "container-hidden";

		/// <summary>
		/// Seconds a request may go unanswered before its row is made clickable again.
		/// </summary>
		/// <remarks>
		/// The server replies to every take, successful or not, so this should never fire. It
		/// exists because the alternative failure mode is unrecoverable: a reply lost to a dropped
		/// connection would leave the row waiting for the life of the window.
		/// </remarks>
		private const float PENDING_TIMEOUT_SECONDS = 5f;

		/// <summary>
		/// One rendered container row.
		/// </summary>
		private struct RowView
		{
			/// <summary>Root element of the row.</summary>
			public VisualElement Root;
			/// <summary>The container slot index this row addresses.</summary>
			public int Slot;
			/// <summary>Overlay shown while a take is in flight.</summary>
			public VisualElement Pending;
		}

		// ── Private state ─────────────────────────────────────────────────────

		/// <summary>The container currently displayed, or 0 when the panel holds nothing.</summary>
		private long containerID;

		/// <summary>The most recent contents the server sent for <see cref="containerID"/>.</summary>
		private ContainerSlotData[] slotData = new ContainerSlotData[0];

		/// <summary>Display name of the displayed container.</summary>
		private string containerName = string.Empty;

		/// <summary>Rendered rows, one per non-empty container slot.</summary>
		private readonly List<RowView> rowViews = new List<RowView>();

		/// <summary>
		/// Container slots with a take in flight, mapped to the time the request was sent.
		/// </summary>
		/// <remarks>
		/// Keyed by slot rather than by row index because rows are rebuilt whenever the server
		/// re-sends the contents, and a row's position moves as slots above it empty. The slot
		/// index is the only identifier that survives a rebuild.
		/// </remarks>
		private readonly Dictionary<int, float> pendingSlots = new Dictionary<int, float>();

		private VisualElement listRoot;
		private Label subtitleLabel;
		private Label emptyLabel;
		private Label statusLabel;
		private Button takeAllButton;

		// ── UITKControl lifecycle ─────────────────────────────────────────────

		/// <inheritdoc />
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			listRoot = root.Q(LIST_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
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
		}

		/// <inheritdoc />
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
		/// by startup, which is exactly the case where <c>OnAfterStarting</c> alone is not enough.
		/// Both hooks do the work and both are idempotent.
		/// </remarks>
		protected override void OnAfterShow()
		{
			ApplyPerOpenContent();
		}

		/// <inheritdoc />
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ContainerOpenBroadcast>(OnClientContainerOpenBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ContainerTakeResultBroadcast>(OnClientContainerTakeResultBroadcastReceived);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ContainerOpenBroadcast>(OnClientContainerOpenBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ContainerTakeResultBroadcast>(OnClientContainerTakeResultBroadcastReceived);
		}

		/// <inheritdoc />
		protected override void OnTick()
		{
			ReleaseTimedOutRequests();
		}

		/// <inheritdoc />
		public override void OnDestroying()
		{
			DestroyRows();
			base.OnDestroying();
		}

		/// <inheritdoc />
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			bool wasVisible = Visible;

			base.Hide(overrideIsAlwaysOpen);

			if (Visible || !wasVisible)
			{
				return;
			}

			ClearContainer();
		}

		// ── Broadcast handling ────────────────────────────────────────────────

		/// <summary>
		/// Displays a container's contents, opening the panel if it is not already open.
		/// </summary>
		/// <remarks>
		/// Serves double duty as the open message and the refresh message — the server re-sends
		/// the whole contents after every successful take.
		/// </remarks>
		private void OnClientContainerOpenBroadcastReceived(ContainerOpenBroadcast msg, Channel channel)
		{
			/* A refresh for a container the panel is not showing is discarded rather than allowed
			 * to hijack the window. Opening a second chest while the first is up replaces it —
			 * that is a new open, and it arrives with the new ID — but a stale refresh for a chest
			 * the player has walked away from must not. */
			if (containerID != 0 && msg.InteractableID != containerID && Visible)
			{
				// A different container: treat it as a fresh open.
				ClearPending();
			}

			containerID = msg.InteractableID;
			slotData = msg.Items ?? new ContainerSlotData[0];

			ContainerTemplate template = ContainerTemplate.Get<ContainerTemplate>(msg.TemplateID);
			containerName = template != null && !string.IsNullOrWhiteSpace(template.Description)
				? template.Description
				: string.Empty;

			if (!Visible)
			{
				Show();
			}
			else
			{
				ApplyPerOpenContent();
			}
		}

		/// <summary>
		/// Releases the pending lock on a slot and reports a refusal.
		/// </summary>
		private void OnClientContainerTakeResultBroadcastReceived(ContainerTakeResultBroadcast msg, Channel channel)
		{
			if (msg.InteractableID != containerID)
			{
				return;
			}

			pendingSlots.Remove(msg.Slot);
			ApplyPendingOverlays();

			SetStatus(msg.Success ? string.Empty : DescribeFailure(msg.Reason));

			/* A container that despawns when emptied takes its scene object with it, so there is
			 * no refresh coming and no window worth leaving open. */
			if (msg.Success && msg.Reason == ContainerFailureReason.None && slotData.Length == 0)
			{
				Hide();
			}
		}

		/// <summary>
		/// Turns a refusal into something worth showing the player.
		/// </summary>
		private static string DescribeFailure(ContainerFailureReason reason)
		{
			switch (reason)
			{
				case ContainerFailureReason.NoContainer: return "Too far away.";
				case ContainerFailureReason.AlreadyTaken: return "Already taken.";
				case ContainerFailureReason.InventoryFull: return "Your inventory is full.";
				case ContainerFailureReason.ServerError: return "Try again.";
				default: return string.Empty;
			}
		}

		// ── Rendering ─────────────────────────────────────────────────────────

		/// <summary>
		/// Writes everything that has to survive the visual tree being re-cloned.
		/// </summary>
		private void ApplyPerOpenContent()
		{
			if (Root == null)
			{
				return;
			}

			// Re-query: Show() replaces the tree, so cached references are stale after it.
			listRoot = Root.Q(LIST_NAME);
			subtitleLabel = Root.Q<Label>(SUBTITLE_NAME);
			emptyLabel = Root.Q<Label>(EMPTY_NAME);
			statusLabel = Root.Q<Label>(STATUS_NAME);

			if (subtitleLabel != null)
			{
				subtitleLabel.text = containerName;
			}

			RebuildRows();
			ApplyPendingOverlays();
			UpdateEmptyState();
		}

		/// <summary>
		/// Rebuilds every row from the latest snapshot.
		/// </summary>
		private void RebuildRows()
		{
			DestroyRows();

			if (listRoot == null)
			{
				return;
			}

			for (int i = 0; i < slotData.Length; ++i)
			{
				ContainerSlotData data = slotData[i];
				BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(data.TemplateID);
				if (template == null)
				{
					/* An unknown template means the client's item cache disagrees with the
					 * server's. Skipping the row is right: it cannot be labelled or pictured, and
					 * offering an unnamed row invites a take the player cannot evaluate. */
					continue;
				}

				rowViews.Add(CreateRow(data, template));
			}
		}

		/// <summary>
		/// Builds one row for a filled container slot.
		/// </summary>
		private RowView CreateRow(ContainerSlotData data, BaseItemTemplate template)
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
			rowRoot.RegisterCallback<PointerDownEvent>(evt => OnRowPointerDown(capturedSlot));

			listRoot.Add(rowRoot);

			RowView view;
			view.Root = rowRoot;
			view.Slot = capturedSlot;
			view.Pending = pending;
			return view;
		}

		/// <summary>
		/// Removes every rendered row.
		/// </summary>
		private void DestroyRows()
		{
			for (int i = 0; i < rowViews.Count; ++i)
			{
				rowViews[i].Root?.RemoveFromHierarchy();
			}
			rowViews.Clear();
		}

		/// <summary>
		/// Shows or hides the "empty" line and the take-all button.
		/// </summary>
		private void UpdateEmptyState()
		{
			bool empty = rowViews.Count < 1;

			if (emptyLabel != null)
			{
				emptyLabel.EnableInClassList(CSS_HIDDEN, !empty);
			}

			takeAllButton = Root?.Q<Button>(TAKE_ALL_NAME);
			if (takeAllButton != null)
			{
				takeAllButton.SetEnabled(!empty);
			}
		}

		/// <summary>
		/// Applies the waiting overlay to rows whose take is in flight.
		/// </summary>
		private void ApplyPendingOverlays()
		{
			for (int i = 0; i < rowViews.Count; ++i)
			{
				RowView view = rowViews[i];
				bool waiting = pendingSlots.ContainsKey(view.Slot);
				view.Pending?.EnableInClassList(CSS_HIDDEN, !waiting);
			}
		}

		// ── Requests ──────────────────────────────────────────────────────────

		/// <summary>
		/// Sends a take request for one slot, unless one is already in flight for it.
		/// </summary>
		private void OnRowPointerDown(int slot)
		{
			if (Client == null || containerID == 0)
			{
				return;
			}

			if (pendingSlots.ContainsKey(slot))
			{
				return;
			}

			pendingSlots[slot] = UnityEngine.Time.unscaledTime;
			ApplyPendingOverlays();
			SetStatus(string.Empty);

			Client.Broadcast(new ContainerTakeItemBroadcast()
			{
				InteractableID = containerID,
				Slot = slot,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Requests every slot the panel is currently showing.
		/// </summary>
		/// <remarks>
		/// There is no server-side take-all for containers, so this sends one request per row. The
		/// server's per-connection ingress guard serialises them, and each reply releases its own
		/// row — so a partial success (an inventory that fills halfway through) leaves exactly the
		/// rows that could not be taken, which is the honest result.
		/// </remarks>
		private void RequestTakeAll()
		{
			if (Client == null || containerID == 0)
			{
				return;
			}

			// Copied first: sending mutates pendingSlots, and the reply for an early row can
			// rebuild rowViews before the loop finishes.
			List<int> slots = new List<int>(rowViews.Count);
			for (int i = 0; i < rowViews.Count; ++i)
			{
				slots.Add(rowViews[i].Slot);
			}

			for (int i = 0; i < slots.Count; ++i)
			{
				OnRowPointerDown(slots[i]);
			}
		}

		/// <summary>
		/// Clears rows whose reply never arrived.
		/// </summary>
		private void ReleaseTimedOutRequests()
		{
			if (pendingSlots.Count < 1)
			{
				return;
			}

			float now = UnityEngine.Time.unscaledTime;
			List<int> expired = null;

			foreach (KeyValuePair<int, float> pair in pendingSlots)
			{
				if (now - pair.Value >= PENDING_TIMEOUT_SECONDS)
				{
					(expired ??= new List<int>()).Add(pair.Key);
				}
			}

			if (expired == null)
			{
				return;
			}

			for (int i = 0; i < expired.Count; ++i)
			{
				pendingSlots.Remove(expired[i]);
			}
			ApplyPendingOverlays();
		}

		// ── Helpers ───────────────────────────────────────────────────────────

		/// <summary>
		/// Writes the footer status line.
		/// </summary>
		private void SetStatus(string text)
		{
			statusLabel = Root?.Q<Label>(STATUS_NAME);
			if (statusLabel != null)
			{
				statusLabel.text = text ?? string.Empty;
			}
		}

		/// <summary>
		/// Drops every pending request without touching the displayed rows.
		/// </summary>
		private void ClearPending()
		{
			pendingSlots.Clear();
		}

		/// <summary>
		/// Forgets the container the panel was showing.
		/// </summary>
		private void ClearContainer()
		{
			containerID = 0;
			slotData = new ContainerSlotData[0];
			containerName = string.Empty;
			ClearPending();
			DestroyRows();
		}
	}
}
