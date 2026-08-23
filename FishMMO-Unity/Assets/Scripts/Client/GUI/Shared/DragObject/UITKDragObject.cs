using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit drag object. Displays a dragged icon that follows the cursor and carries the
	/// identity of whatever the drag was started from until something drops or cancels it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// THE DRAG IS NOT AUTHORITATIVE AND MUST NEVER BE TREATED AS IF IT WERE. What it holds is a
	/// record of what the player picked up and where they picked it up from, taken at the moment
	/// they clicked. The containers underneath it are replicated from the server and can change at
	/// any point during the drag — a loot broadcast, another panel's swap echo, a scene handover —
	/// so by the time the player releases, the slot named in <see cref="ReferenceID"/> may hold a
	/// completely different item. Submitting the slot index alone at that point moves the wrong
	/// item, and the player watches something they never touched go somewhere they never put it.
	/// </para>
	/// <para>
	/// The fix is identity, not optimism: <see cref="SetItemReference"/> records which item the
	/// drag started from, and <see cref="MatchesSource"/> re-checks that the source slot still
	/// holds it before the drop is allowed to submit anything. A drag that no longer matches is
	/// cancelled rather than guessed at.
	/// </para>
	/// <para>
	/// The payload is also cleared on every teardown path — <see cref="Hide(bool)"/> (which covers
	/// Escape via <c>UIManager.CloseNext</c> and quit-to-login via <c>Hide(false)</c>),
	/// <see cref="OnQuitToLogin"/>, and <see cref="NotifySlotChanged"/> from the panels — because a
	/// payload that survives its own panel is a drag the player cannot see and cannot cancel, and
	/// the next click anywhere completes it.
	/// </para>
	/// </remarks>
	public class UITKDragObject : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Drag;

		/// <summary>
		/// Constant representing a null reference ID for drag objects.
		/// </summary>
		public const long NULL_REFERENCE_ID = -1;

		/// <summary>
		/// Constant representing "no item identity" for <see cref="ItemID"/>.
		/// </summary>
		/// <remarks>
		/// Zero rather than -1 because <c>Item.ID</c> defaults to 0 for the display-only
		/// constructor, so an item that never received a server-issued ID is indistinguishable
		/// from no item at all — and must be treated as such rather than matching everything.
		/// </remarks>
		public const long NULL_ITEM_ID = 0;

		/// <summary>
		/// Name of the drag icon element.
		/// </summary>
		private const string DRAG_ICON_NAME = "drag-icon";

		/// <summary>
		/// The reference ID associated with the dragged object.
		/// </summary>
		/// <remarks>
		/// A container slot index for item drags, a database ID for ability and hotkey drags.
		/// Which of the two it is depends entirely on <see cref="Type"/>.
		/// </remarks>
		public long ReferenceID = NULL_REFERENCE_ID;

		/// <summary>
		/// The type of reference button (e.g., inventory, skill, etc.).
		/// </summary>
		public ReferenceButtonType Type = ReferenceButtonType.None;

		/// <summary>
		/// Instance ID of the item this drag was started from, or <see cref="NULL_ITEM_ID"/>
		/// when the drag carries no item identity.
		/// </summary>
		/// <remarks>
		/// <see cref="ReferenceID"/> alone is a container slot index, and a slot index is only
		/// meaningful for as long as the container does not change underneath the drag. The
		/// server can write to that slot at any moment — a loot broadcast, a queued swap echo,
		/// a trade — and the drop would then submit the slot the player picked, holding an item
		/// they never picked up. Recording which item the drag actually started from is what
		/// makes that detectable at drop time.
		/// </remarks>
		public long ItemID { get; private set; } = NULL_ITEM_ID;

		/// <summary>
		/// Version of the item this drag was started from, paired with <see cref="ItemID"/>.
		/// </summary>
		/// <remarks>
		/// Client-side items are currently constructed without a version (it stays 0), so this
		/// contributes nothing today. It is carried and compared anyway so that the drop-time
		/// validation starts rejecting stale drags for free the moment the server begins
		/// sending a real version, rather than needing this code to be revisited.
		/// </remarks>
		public long ItemVersion { get; private set; }

		/// <summary>
		/// True when this drag was seeded from a container item and therefore carries an
		/// identity that a drop can validate against.
		/// </summary>
		/// <remarks>
		/// Not every drag source has one: ability and hotkey drags carry a database ID in
		/// <see cref="ReferenceID"/> rather than a slot index, and have no item behind them.
		/// An item whose <c>ID</c> is still <see cref="NULL_ITEM_ID"/> also does not get one,
		/// because zero is the default and would otherwise match every other unsaved item.
		/// </remarks>
		public bool HasItemIdentity { get; private set; }

		/// <summary>
		/// Layer mask used for raycasting when dropping items.
		/// </summary>
		public LayerMask LayerMask;

		/// <summary>
		/// Maximum distance for drop raycast.
		/// </summary>
		public float DropDistance = 5.0f;

		/// <summary>
		/// The visual element used as the drag icon.
		/// </summary>
		private VisualElement dragIcon;

		/// <summary>
		/// The sprite displayed while dragging.
		/// </summary>
		private Sprite iconSprite;

		/// <summary>
		/// Re-entrancy guard for the <see cref="Clear"/> / <see cref="Hide(bool)"/> cycle.
		/// </summary>
		/// <remarks>
		/// <see cref="Clear"/> hides the panel and <see cref="Hide(bool)"/> clears the payload,
		/// which is exactly the mutual recursion it looks like. Both directions are wanted —
		/// clearing must hide, and every hide (Escape, quit-to-login, a panel closing us) must
		/// clear — so the cycle is broken with a flag rather than by dropping one of them.
		/// </remarks>
		private bool clearing;

		/// <summary>
		/// The sprite currently displayed by the drag object, or null when inactive.
		/// </summary>
		public Sprite IconSprite => iconSprite;

		/// <summary>
		/// True while a drag is actually carrying something.
		/// </summary>
		/// <remarks>
		/// Panels used to test <c>Visible</c>, which is a statement about the document rather
		/// than about the payload: a panel shown with no reference in it reads as an active drag
		/// and the next click "completes" a drag that never started.
		/// </remarks>
		public bool IsDragging => Visible && ReferenceID != NULL_REFERENCE_ID && Type != ReferenceButtonType.None;

		/// <summary>
		/// Resolves cached elements and prepares the drag icon for absolute positioning.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			dragIcon = Root.Q<VisualElement>(DRAG_ICON_NAME);
			Root.pickingMode = PickingMode.Ignore;
			if (dragIcon != null)
			{
				dragIcon.pickingMode = PickingMode.Ignore;
				dragIcon.style.position = Position.Absolute;
			}
		}

		/// <summary>
		/// Re-applies the drag icon after the visual tree has been rebuilt.
		/// </summary>
		/// <remarks>
		/// Per THE CONTRACT: <c>UIDocument</c> clones the UXML afresh on every enable, so the
		/// element <see cref="SetReference"/> wrote the icon into is discarded the moment the
		/// panel is shown. Without this the very first drag of a session is invisible — the
		/// classic symptom — and every drag after a hide/show is too.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			ApplyIcon();
		}

		/// <summary>
		/// Re-applies the drag icon on every show, including the first one.
		/// </summary>
		/// <remarks>
		/// <c>OnAfterStarting</c> alone is not enough. On the very first open <c>hasStarted</c> is
		/// still false, so <c>ReinitializeIfTreeReplaced</c> returns before re-running it, and the
		/// icon written before <see cref="Show"/> is lost with the discarded tree.
		/// </remarks>
		protected override void OnAfterShow()
		{
			ApplyIcon();
		}

		/// <summary>
		/// Per-frame update for the drag object. Handles drag visuals and drop logic.
		/// </summary>
		/// <remarks>
		/// An <c>OnTick</c> override rather than a private <c>Update</c>. Unity binds the
		/// most-derived <c>Update</c> only, so declaring one here silently replaced
		/// <see cref="UITKControl"/>'s — taking <c>PollLoseFocus</c> with it — which is the exact
		/// failure that method's own comment warns about.
		/// </remarks>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			/* The reference decides whether anything is being carried — not the icon. This used to
			 * cancel on a null sprite too, which meant an item whose template has no icon armed a
			 * drag on PointerDown and had it torn down on the very next frame: the panels reported
			 * a clean start and the release then found nothing, so dragging appeared simply not to
			 * be implemented. A project whose item art is not in yet has no icons at all, so this
			 * cancelled every drag it was ever asked to carry.
			 *
			 * An item with no art is still an item. It drags without a ghost, which is a cosmetic
			 * loss; refusing to move it is a functional one. */
			if (ReferenceID == NULL_REFERENCE_ID)
			{
				// Visible with nothing to carry. Whatever left it in that state, do not stay in it.
				Clear();
				return;
			}

			if (dragIcon == null)
			{
				/* The tree may not have been cloned yet on the frame the drag started. Re-resolve
				 * and wait rather than clearing: a payload the player is holding must not be
				 * thrown away because the element it draws into is one frame late. */
				ApplyIcon();
				if (dragIcon == null)
				{
					return;
				}
			}

			Mouse mouse = Mouse.current;

			// Clear the drag if clicking anywhere that isn't the UI.
			// Also handles dropping items to the ground from inventory.
			if (mouse != null && mouse.leftButton.wasPressedThisFrame && !UIManager.ControlHasFocus())
			{
				if (Type == ReferenceButtonType.Inventory && Camera.main != null)
				{
					Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
					if (Physics.Raycast(ray, out RaycastHit hit, DropDistance, LayerMask))
					{
						Log.Debug("UITKDragObject", "Dropping item at pos[" + hit.point + "]");
					}
				}
				Clear();
				return;
			}

			UpdatePosition();
		}

		/// <summary>
		/// Sets the reference data for a drag that carries no item identity.
		/// </summary>
		/// <remarks>
		/// For ability and hotkey drags, whose <paramref name="referenceID"/> is a database ID
		/// rather than a container slot and which therefore have nothing to re-validate against.
		/// Item panels must use <see cref="SetItemReference"/> instead.
		/// </remarks>
		/// <param name="icon">Sprite to display while dragging.</param>
		/// <param name="referenceID">Reference ID for the dragged object.</param>
		/// <param name="type">Type of reference button.</param>
		public void SetReference(Sprite icon, long referenceID, ReferenceButtonType type)
		{
			ApplyReference(icon, referenceID, type, NULL_ITEM_ID, 0L, false);
		}

		/// <summary>
		/// Sets the reference data for a drag started from a container slot, recording which
		/// item it was started from so the drop can re-validate it.
		/// </summary>
		/// <param name="icon">Sprite to display while dragging.</param>
		/// <param name="slotIndex">Slot index within the source container.</param>
		/// <param name="type">Which container the slot belongs to.</param>
		/// <param name="item">The item in that slot at the moment the drag began.</param>
		public void SetItemReference(Sprite icon, int slotIndex, ReferenceButtonType type, Item item)
		{
			/* An item with no server-issued ID cannot be told apart from any other item with no
			 * server-issued ID, so it gets no identity rather than a false one. The drop then
			 * falls back to "the source slot is still occupied", which is weaker but honest. */
			bool hasIdentity = item != null && item.ID != NULL_ITEM_ID;

			ApplyReference(
				icon,
				slotIndex,
				type,
				hasIdentity ? item.ID : NULL_ITEM_ID,
				item != null ? item.Version : 0L,
				hasIdentity);
		}

		/// <summary>
		/// Reports whether <paramref name="item"/> is still the item this drag was started from.
		/// </summary>
		/// <remarks>
		/// Call this at drop time against a fresh read of the source slot, never against the
		/// value the drag was seeded with. When the drag carries no identity the best that can be
		/// said is "the slot is still occupied", which is what an empty item fails and anything
		/// else passes.
		/// </remarks>
		/// <param name="item">The item currently occupying the source slot, or null if empty.</param>
		/// <returns>True when the drop may proceed.</returns>
		public bool MatchesSource(Item item)
		{
			if (item == null)
			{
				return false;
			}

			if (!HasItemIdentity)
			{
				return true;
			}

			return item.ID == ItemID && item.Version == ItemVersion;
		}

		/// <summary>
		/// Cancels the drag if a container write has invalidated its source slot.
		/// </summary>
		/// <remarks>
		/// Called by the item panels from their slot-updated handlers. This is the half of the
		/// lifecycle a drop-time check cannot cover on its own: without it the icon keeps
		/// following the cursor while the thing it represents has already gone, and the player is
		/// aiming a drop they are about to be told is invalid. Cancelling as it happens is both
		/// more honest and much easier to understand.
		/// </remarks>
		/// <param name="type">Container the write landed in.</param>
		/// <param name="slotIndex">Slot the write landed in.</param>
		/// <param name="item">The item now in that slot, or null if it was emptied.</param>
		public void NotifySlotChanged(ReferenceButtonType type, int slotIndex, Item item)
		{
			if (!IsDragging || Type != type || ReferenceID != slotIndex)
			{
				return;
			}

			if (MatchesSource(item))
			{
				return;
			}

			Log.Debug("UITKDragObject", $"Source slot [{type}:{slotIndex}] changed under the drag; cancelling.");
			Clear();
		}

		/// <summary>
		/// Clears the drag object state, hides it, and resets reference data.
		/// </summary>
		public void Clear()
		{
			if (clearing)
			{
				return;
			}

			clearing = true;
			try
			{
				iconSprite = null;
				ReferenceID = NULL_REFERENCE_ID;
				Type = ReferenceButtonType.None;
				ItemID = NULL_ITEM_ID;
				ItemVersion = 0L;
				HasItemIdentity = false;

				UITKItemIcon.Clear(dragIcon);

				Hide(false);
			}
			finally
			{
				clearing = false;
			}
		}

		/// <summary>
		/// Hides the drag overlay and drops whatever it was carrying.
		/// </summary>
		/// <remarks>
		/// <c>Hide(bool)</c> rather than <c>Hide()</c>, because <c>Hide()</c> is only one of the
		/// routes here. Escape arrives through <c>UIManager.CloseNext</c> and quit-to-login
		/// through <c>Hide(false)</c>; overriding the parameterless form would miss the second and
		/// leave a payload armed on the login screen, ready to complete itself against whatever
		/// character logs in next.
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (!Visible)
			{
				Clear();
			}
		}

		/// <summary>
		/// Drops the payload when the client returns to the login screen.
		/// </summary>
		/// <remarks>
		/// Belt and braces: <c>CloseOnQuitToMenu</c> is set on this panel in the scene, so the
		/// base class already routes quit-to-login through <c>Hide(false)</c>. That is a scene
		/// setting an editor can change, and a drag surviving into the next session is not
		/// something that should depend on one.
		/// </remarks>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();
			Clear();
		}

		/// <summary>
		/// Drops the payload when the overlay is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			Clear();
			base.OnDestroying();
		}

		/// <summary>
		/// Common tail of both <c>Set*Reference</c> forms.
		/// </summary>
		private void ApplyReference(Sprite icon, long referenceID, ReferenceButtonType type,
			long itemID, long itemVersion, bool hasItemIdentity)
		{
			iconSprite = icon;
			ReferenceID = referenceID;
			Type = type;
			ItemID = itemID;
			ItemVersion = itemVersion;
			HasItemIdentity = hasItemIdentity;

			/* Show first, then paint. Enabling the document re-clones the UXML, so an icon written
			 * into dragIcon before this point belongs to a tree that has already been thrown away
			 * — the drag then follows the cursor as an invisible 48x48 hole. ApplyIcon also runs
			 * from OnAfterShow, so this call is the belt to that braces. */
			Show();
			ApplyIcon();
			UpdatePosition();
		}

		/// <summary>
		/// Writes the current sprite into whichever drag icon element exists right now.
		/// </summary>
		private void ApplyIcon()
		{
			/* Re-resolve whenever the cached element is missing or has been detached. UIDocument
			 * hands out a whole new tree on every enable, so a dragIcon cached before a hide/show
			 * belongs to a tree nobody can see — painting into it is the invisible-drag-icon
			 * symptom THE CONTRACT describes. A detached element reports a null panel. */
			if (Root != null && (dragIcon == null || dragIcon.panel == null))
			{
				dragIcon = Root.Q<VisualElement>(DRAG_ICON_NAME);
				if (dragIcon != null)
				{
					dragIcon.pickingMode = PickingMode.Ignore;
					dragIcon.style.position = Position.Absolute;
				}
			}

			if (dragIcon == null)
			{
				return;
			}

			/* A null sprite draws the placeholder, not nothing. The drag is allowed to start
			 * without art (see OnTick), so without this the player would be carrying something
			 * invisible — which is the same "did that do anything?" as the drag being refused,
			 * and the reason the refusal looked reasonable in the first place. */
			UITKItemIcon.Apply(dragIcon, iconSprite);
		}

		/// <summary>
		/// Positions the drag icon under the cursor, converted to panel space and kept on screen.
		/// </summary>
		/// <remarks>
		/// Via <see cref="UITKScreenSpace"/> rather than a bare
		/// <c>RuntimePanelUtils.ScreenToPanel</c>. The Input System measures Y from the bottom of
		/// the screen and UI Toolkit lays out from the top, so the raw conversion mirrors the icon
		/// about the horizontal centre — pick something up near the top of the screen and the icon
		/// appears near the bottom. The same helper also keeps the icon inside the panel, which
		/// matters at the right and bottom edges where a 48x48 icon would otherwise hang off.
		/// </remarks>
		private void UpdatePosition()
		{
			if (dragIcon == null || Root == null || Root.panel == null)
			{
				return;
			}

			if (!UITKScreenSpace.TryGetPointerPanelPosition(Root.panel, out Vector2 panelPosition))
			{
				return;
			}

			UITKScreenSpace.PlaceClamped(Root, dragIcon, panelPosition);
		}
	}
}
