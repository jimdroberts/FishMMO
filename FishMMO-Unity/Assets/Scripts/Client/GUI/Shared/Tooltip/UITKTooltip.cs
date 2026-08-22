using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

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
		/// Name of the tooltip text label element.
		/// </summary>
		private const string TOOLTIP_TEXT_NAME = "tooltip-text";

		/// <summary>
		/// The tooltip box container element.
		/// </summary>
		private VisualElement tooltipBox;
		/// <summary>
		/// The tooltip text label.
		/// </summary>
		private Label tooltipText;

		/// <summary>
		/// Text the current tooltip is showing.
		/// </summary>
		/// <remarks>
		/// Kept so it can be re-applied after the document re-clones the UXML; writing it into
		/// the label before <c>Show()</c> writes into a tree that is about to be discarded.
		/// </remarks>
		private string pendingText = string.Empty;

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
			tooltipText = Root.Q<Label>(TOOLTIP_TEXT_NAME);

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
		/// Opens the tooltip with the specified text and shows it near the cursor.
		/// </summary>
		/// <param name="text">Text to display in the tooltip.</param>
		public void Open(string text)
		{
			Open(text, null);
		}

		/// <summary>
		/// Opens the tooltip for a specific element, closing it automatically when that element
		/// is removed, hidden or destroyed.
		/// </summary>
		/// <param name="text">Text to display in the tooltip.</param>
		/// <param name="owner">The element being described. May be null.</param>
		public void Open(string text, VisualElement owner)
		{
			if (string.IsNullOrEmpty(text))
			{
				ForceHide();
				return;
			}

			this.owner = owner;
			this.pendingText = text;

			/* Show before writing. Enabling the document re-clones the UXML, so a label written
			 * to here would belong to a tree that is discarded microseconds later — the tooltip
			 * would open, every time, blank. OnAfterShow does the write against the live tree. */
			Show();

			// Already visible for a different owner: Show is a no-op, so apply the text directly.
			ApplyText();
			UpdatePosition();
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
			if (owner == null || this.owner == null ||
				ReferenceEquals(this.owner, owner) || owner.Contains(this.owner))
			{
				ForceHide();
			}
		}

		/// <summary>
		/// Writes the pending text into the live tree once the document has finished cloning.
		/// </summary>
		protected override void OnAfterShow()
		{
			ApplyText();
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
			ApplyText();
		}

		/// <summary>
		/// Writes the pending text into the label, if there is one to write into.
		/// </summary>
		private void ApplyText()
		{
			if (tooltipText != null)
			{
				tooltipText.text = pendingText;
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
			pendingText = string.Empty;
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
