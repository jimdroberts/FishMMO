using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit tooltip control. Displays a text box near the mouse cursor and keeps it inside
	/// the panel.
	/// </summary>
	/// <remarks>
	/// The tooltip is one shared panel that every hoverable element in the game opens, and it has
	/// no idea what it is describing. That is what made it stick: the row under the pointer is
	/// destroyed by a container refresh, or its whole panel is closed, or the scene changes, and
	/// nothing ever sent the matching <c>Hide</c> — so a tooltip for an item that no longer
	/// exists stayed on screen following the cursor around.
	/// <para>
	/// The fix is an owner. <see cref="Open(string, VisualElement)"/> records the element the
	/// tooltip belongs to and the tooltip closes itself the moment that element stops being a
	/// live, displayed part of a panel. <see cref="HideFor(VisualElement)"/> lets a caller close
	/// only its own tooltip, so a stale <c>PointerLeave</c> from a row the player has already
	/// moved off cannot close the tooltip that a different row has since opened.
	/// </para>
	/// </remarks>
	public class UITKTooltip : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Tooltip;

		/// <summary>
		/// Name of the tooltip box container element.
		/// </summary>
		private const string TOOLTIP_BOX_NAME = "tooltip-box";
		/// <summary>
		/// Name of the element the rendered rows are written into.
		/// </summary>
		private const string TOOLTIP_CONTENT_NAME = "tooltip-content";

		/// <summary>
		/// The tooltip box container element.
		/// </summary>
		private VisualElement tooltipBox;
		/// <summary>
		/// The element rendered rows are written into.
		/// </summary>
		private VisualElement contentRoot;

		/// <summary>
		/// The content the current tooltip is showing.
		/// </summary>
		/// <remarks>
		/// Kept so it can be re-rendered after the document re-clones the UXML; building elements
		/// before <c>Show()</c> builds them into a tree that is about to be discarded.
		/// </remarks>
		private TooltipContent pendingContent;

		/// <summary>
		/// The element this tooltip is describing, when the caller supplied one.
		/// </summary>
		private VisualElement owner;

		/// <summary>
		/// Resolves cached elements and prepares the tooltip for absolute positioning.
		/// </summary>
		public override void OnStarting()
		{
			/* Before the Root check: a scene change has to close the tooltip whether or not the
			 * panel got as far as resolving its elements, and this runs again after every tree
			 * rebuild, so it unsubscribes first rather than stacking handlers. */
			SceneManager.activeSceneChanged -= OnActiveSceneChanged;
			SceneManager.activeSceneChanged += OnActiveSceneChanged;

			if (Root == null)
			{
				return;
			}

			tooltipBox = Root.Q<VisualElement>(TOOLTIP_BOX_NAME);
			contentRoot = Root.Q<VisualElement>(TOOLTIP_CONTENT_NAME);

			// The root should never intercept pointer events.
			Root.pickingMode = PickingMode.Ignore;
			if (tooltipBox != null)
			{
				tooltipBox.pickingMode = PickingMode.Ignore;
				tooltipBox.style.position = Position.Absolute;
			}
		}

		/// <summary>
		/// Subscribes to scene changes so a tooltip cannot survive one.
		/// </summary>
		/// <remarks>
		/// A world scene unload destroys everything the tooltip could be describing, and the
		/// hover that opened it never gets its matching <c>PointerLeave</c>.
		/// </remarks>
		public override void OnClientSet()
		{
			SceneManager.activeSceneChanged -= OnActiveSceneChanged;
			SceneManager.activeSceneChanged += OnActiveSceneChanged;
		}

		/// <summary>
		/// Drops the scene subscription.
		/// </summary>
		public override void OnDestroying()
		{
			SceneManager.activeSceneChanged -= OnActiveSceneChanged;
		}

		/// <summary>
		/// Closes the tooltip when the active scene changes.
		/// </summary>
		private void OnActiveSceneChanged(Scene from, Scene to)
		{
			ForceHide();
		}

		/// <summary>
		/// Closes the tooltip when the client quits to the login screen.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ForceHide();
			base.OnQuitToLogin();
		}

		/// <summary>
		/// Repositions the tooltip to follow the mouse and closes it when its owner goes away.
		/// </summary>
		/// <remarks>
		/// An override rather than a <c>private void Update</c>. Unity binds only the
		/// most-derived <c>Update</c>, so declaring one here silently disabled
		/// <c>UITKControl.Update</c> — and with it the focus polling every panel relies on.
		/// </remarks>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			if (!IsOwnerAlive())
			{
				ForceHide();
				return;
			}

			UpdatePosition();
		}

		/// <summary>
		/// Reports whether the element this tooltip belongs to is still on screen.
		/// </summary>
		/// <remarks>
		/// Three ways it can stop being: the element was removed from its tree (a list rebuild),
		/// its panel was hidden or destroyed (<c>panel</c> goes null when the document is
		/// disabled), or something in its ancestry was set to <c>display: none</c>.
		/// </remarks>
		private bool IsOwnerAlive()
		{
			if (owner == null)
			{
				// No owner was supplied; the caller is managing the lifetime itself.
				return true;
			}

			if (owner.panel == null || owner.parent == null)
			{
				return false;
			}

			for (VisualElement element = owner; element != null; element = element.parent)
			{
				if (element.resolvedStyle.display == DisplayStyle.None)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Opens the tooltip describing <paramref name="source"/>.
		/// </summary>
		/// <param name="source">The thing being described.</param>
		/// <param name="owner">The element being hovered. May be null.</param>
		public void Open(ITooltip source, VisualElement owner = null)
		{
			if (source == null)
			{
				ForceHide();
				return;
			}
			Open(source.BuildContent(), owner);
		}

		/// <summary>
		/// Opens the tooltip for a specific element, closing it automatically when that element
		/// is removed, hidden or destroyed.
		/// </summary>
		/// <param name="content">The content to display.</param>
		/// <param name="owner">The element being described. May be null.</param>
		public void Open(TooltipContent content, VisualElement owner = null)
		{
			if (content == null || content.IsEmpty)
			{
				ForceHide();
				return;
			}

			this.owner = owner;
			this.pendingContent = content;

			/* Show before writing. Enabling the document re-clones the UXML, so a label written
			 * to here would belong to a tree that is discarded microseconds later — the tooltip
			 * would open, every time, blank. OnAfterShow does the write against the live tree. */
			Show();

			// Already visible for a different owner: Show is a no-op, so render directly.
			ApplyContent();
			UpdatePosition();
		}

		/// <summary>
		/// Reports whether this tooltip is the one belonging to <paramref name="element"/>.
		/// </summary>
		/// <remarks>
		/// An element's tooltip is either the one opened on it or one opened on something inside it,
		/// so both a slot and a container holding that slot address the same tooltip. Identity is
		/// tested alongside ancestry because <c>VisualElement.Contains</c> is strictly ancestor-of:
		/// an element does not contain itself, and naming the owner exactly is the common case.
		/// <para>
		/// One method rather than the same rule spelled out at each call site. This is the check
		/// that decides whether a caller may close, or may rewrite, what is on screen, and two
		/// copies of it drifting apart is how a tooltip ends up describing a slot it is not on.
		/// </para>
		/// </remarks>
		private bool Describes(VisualElement element)
		{
			return owner != null && element != null &&
				   (ReferenceEquals(owner, element) || element.Contains(owner));
		}

		/// <summary>
		/// Closes the tooltip only if it is currently showing for <paramref name="owner"/>.
		/// </summary>
		/// <param name="owner">
		/// The element whose tooltip should be closed. When null, or when it is an ancestor of
		/// the current owner, the tooltip closes.
		/// </param>
		public void HideFor(VisualElement owner)
		{
			/* The null-owner branch is kept as it was: a tooltip with no owner at all has no claim
			 * on the screen that a caller asking to hide could be wrong about. */
			if (owner == null || this.owner == null || Describes(owner))
			{
				ForceHide();
			}
		}

		/// <summary>
		/// Re-describes <paramref name="owner"/> after the thing it stands for has changed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A tooltip is opened from whatever the pointer found at the moment it entered an element,
		/// and nothing re-reads that afterwards. For a slot that holds an item this is wrong the
		/// moment the slot changes: swapping two items repaints both slots under a cursor that never
		/// moved, so no leave and no enter ever fire and the tooltip goes on describing the item
		/// that used to be there.
		/// </para>
		/// <para>
		/// Guarded by ownership, like <see cref="HideFor"/>. A container repaints every one of its
		/// slots when it resyncs and only one of them is under the pointer, so a refresh addressed
		/// to some other element must not touch what is on screen — and, because this is the path
		/// a whole grid's repaint goes through, a refresh must never be what opens a tooltip.
		/// </para>
		/// </remarks>
		/// <param name="owner">The element being described, whose description has changed.</param>
		/// <param name="source">
		/// What that element describes now, or null when it has stopped describing anything — in
		/// which case the tooltip closes, since there is nothing left to follow the cursor with.
		/// </param>
		public void RefreshFor(VisualElement owner, ITooltip source)
		{
			if (!Describes(owner))
			{
				return;
			}

			if (source == null)
			{
				ForceHide();
				return;
			}

			/* The owner is handed straight back to Open so the tooltip is never unowned, not even
			 * for the length of this call — an unowned tooltip is one nothing is holding on to. */
			Open(source, this.owner);
		}

		/// <summary>
		/// Writes the pending text into the live tree once the document has finished cloning.
		/// </summary>
		protected override void OnAfterShow()
		{
			ApplyContent();
			UpdatePosition();
		}

		/// <summary>
		/// Re-applies the tooltip text after a visual tree rebuild.
		/// </summary>
		protected override void OnAfterStarting()
		{
			if (!Visible)
			{
				return;
			}
			ApplyContent();
		}

		/// <summary>
		/// Renders the pending content, if there is somewhere to render it.
		/// </summary>
		private void ApplyContent()
		{
			if (contentRoot != null)
			{
				UITKTooltipView.Render(contentRoot, pendingContent);
			}
		}

		/// <summary>
		/// Hides the tooltip and forgets what it was describing.
		/// </summary>
		/// <remarks>
		/// Goes through <c>Hide(false)</c> rather than <c>Hide()</c> so it closes even if the
		/// panel is ever marked always-open — a tooltip that cannot be dismissed is worse than
		/// no tooltip.
		/// </remarks>
		private void ForceHide()
		{
			owner = null;
			pendingContent = null;
			contentRoot?.Clear();
			Hide(false);
		}

		/// <summary>
		/// Clears the owner whenever the tooltip is hidden, however it was hidden.
		/// </summary>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			if (!overrideIsAlwaysOpen)
			{
				owner = null;
			}
			base.Hide(overrideIsAlwaysOpen);
		}

		/// <summary>
		/// Places the tooltip box next to the cursor, keeping it inside the panel.
		/// </summary>
		/// <remarks>
		/// The Y flip and the clamp both live in <see cref="UITKScreenSpace"/>. This used to hand
		/// a raw Input System position — Y measured from the bottom of the screen — to
		/// <c>ScreenToPanel</c>, which mirrors the tooltip about the middle of the screen; and it
		/// then offset upward by the box height when the cursor was in the <i>upper</i> half,
		/// which is the half where there is no room above.
		/// </remarks>
		private void UpdatePosition()
		{
			if (tooltipBox == null || Root == null || Root.panel == null)
			{
				return;
			}

			if (!UITKScreenSpace.TryGetPointerPanelPosition(Root.panel, out Vector2 position))
			{
				return;
			}

			/* Flipped rather than slid: a tooltip near the bottom edge that slid back up would
			 * sit under the cursor, which is exactly the thing it is describing. */
			UITKScreenSpace.PlaceClamped(Root, tooltipBox, position, flip: true);
		}
	}
}
