using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// A floating context menu that displays a dynamic list of action buttons at the mouse position.
	/// Used for right-click interactions on player targets (Inspect, Add Friend, Invite to Party, Trade).
	/// </summary>
	/// <remarks>
	/// Entries are plain <c>Button</c> elements built here rather than instantiated from a prefab
	/// into a layout-group parent, so the panel has no scene dependencies that can go missing.
	///
	/// The entries a caller passes to <see cref="Open"/> are recorded rather than built on the
	/// spot. Enabling the document re-clones the UXML, and on the very first open the visual tree
	/// does not exist at all — so building at call time filled a tree that was either discarded a
	/// moment later or was never there. <see cref="OnAfterShow"/> and
	/// <see cref="OnAfterStarting"/> build them into whichever tree is actually live.
	///
	/// Placement goes through <see cref="UITKScreenSpace"/>: the pointer arrives in screen pixels
	/// with Y measured from the bottom, the panel is laid out in points with Y from the top, and a
	/// menu opened near an edge has to be flipped to the other side of the cursor rather than
	/// sliding back over it.
	/// </remarks>
	public class UITKContextMenu : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Popup;

		/// <summary>Name of the element menu entries are added to.</summary>
		private const string ENTRY_LIST_NAME = "context-entry-list";

		/// <summary>Name of the positioned menu element.</summary>
		private const string MENU_NAME = "context-menu";

		/// <summary>USS class applied to each entry button.</summary>
		private const string ENTRY_CLASS = "context-entry";

		/// <summary>The element entries are added to.</summary>
		private VisualElement entryList;

		/// <summary>The element that gets moved to the pointer.</summary>
		private VisualElement menu;

		/// <summary>The entries the current menu was opened with.</summary>
		private readonly List<(string label, Action callback)> entries = new List<(string label, Action callback)>();

		/// <summary>
		/// Frame the menu was opened on.
		/// </summary>
		/// <remarks>
		/// The same click that opens the menu is still down when <see cref="OnTick"/> first runs,
		/// and the outside-click check would read it as a click elsewhere and close the menu
		/// immediately. Ignoring the opening frame is what makes the menu stay up.
		/// </remarks>
		private int openedFrame = -1;

		/// <summary>
		/// True while the pointer is over the menu.
		/// </summary>
		/// <remarks>
		/// Tracked from the inner menu element's pointer enter/leave events rather than read from
		/// <see cref="UITKControl.HasFocus"/>, which answers for the whole panel root — and that
		/// root fills the screen, so the pointer is always inside it.
		/// </remarks>
		private bool pointerInside;

		/// <summary>
		/// Index of the keyboard-highlighted entry, or -1.
		/// </summary>
		private int focusedIndex = -1;

		/// <summary>
		/// Resolves elements and wires the hover tracking used by the outside-click check.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			entryList = Root.Q<VisualElement>(ENTRY_LIST_NAME);
			menu = Root.Q<VisualElement>(MENU_NAME);

			if (entryList == null)
			{
				Log.Error("UITKContextMenu", "Entry list element is missing.");
			}

			if (menu != null)
			{
				menu.UnregisterCallback<PointerEnterEvent>(OnPointerEnter);
				menu.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
				menu.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
				menu.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
			}

			Root.UnregisterCallback<KeyDownEvent>(OnMenuKeyDown, TrickleDown.TrickleDown);
			Root.RegisterCallback<KeyDownEvent>(OnMenuKeyDown, TrickleDown.TrickleDown);
		}

		/// <summary>
		/// Rebuilds the recorded entries after the visual tree was replaced.
		/// </summary>
		protected override void OnAfterStarting()
		{
			if (!Visible)
			{
				return;
			}
			BuildEntries();
		}

		/// <summary>
		/// Clears entries on teardown.
		/// </summary>
		public override void OnDestroying()
		{
			if (menu != null)
			{
				menu.UnregisterCallback<PointerEnterEvent>(OnPointerEnter);
				menu.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
			}
			if (Root != null)
			{
				Root.UnregisterCallback<KeyDownEvent>(OnMenuKeyDown, TrickleDown.TrickleDown);
			}
			entries.Clear();
			ClearEntries();
		}

		private void OnPointerEnter(PointerEnterEvent evt)
		{
			pointerInside = true;
		}

		private void OnPointerLeave(PointerLeaveEvent evt)
		{
			pointerInside = false;
		}

		/// <summary>
		/// Opens the context menu at the current mouse position with the specified entries.
		/// </summary>
		/// <param name="entries">List of (label, callback) pairs for the menu buttons.</param>
		public void Open(List<(string label, Action callback)> entries)
		{
			if (entries == null || entries.Count == 0)
			{
				return;
			}

			/* Copied, not held by reference: callers build these lists per right-click and are
			 * free to reuse or clear them the moment Open returns, and the menu still has to be
			 * able to rebuild itself from them after a tree rebuild. */
			this.entries.Clear();
			this.entries.AddRange(entries);

			openedFrame = Time.frameCount;
			pointerInside = true;

			/* Show, then build. The document re-clones the UXML on enable, so anything added to
			 * entryList before this call goes into a tree that is discarded — and on the first
			 * ever open there is no tree to add to in the first place. */
			Show();

			// Already visible: Show is a no-op, so replace the contents directly.
			BuildEntries();
			PositionAtPointer();
		}

		/// <summary>
		/// Builds the recorded entries into the live menu element and positions it.
		/// </summary>
		protected override void OnAfterShow()
		{
			BuildEntries();
			PositionAtPointer();
		}

		/// <summary>
		/// Builds every recorded entry into the live entry list.
		/// </summary>
		/// <remarks>
		/// Idempotent — it clears first — so it is safe to run from both the show path and a
		/// tree rebuild.
		/// </remarks>
		private void BuildEntries()
		{
			if (entryList == null)
			{
				return;
			}

			entryList.Clear();
			focusedIndex = -1;

			for (int i = 0; i < entries.Count; ++i)
			{
				(string label, Action callback) = entries[i];

				Button button = new Button { text = label };
				button.AddToClassList("fish-button");
				button.AddToClassList(ENTRY_CLASS);

				// Captured by value; the loop variable would be shared by every handler.
				Action action = callback;
				button.clicked += () =>
				{
					/* Closed before the action runs. These actions open dialogs and other
					 * panels, and closing afterwards would tear down what they just put up. */
					Hide();
					action?.Invoke();
				};

				entryList.Add(button);
			}
		}

		/// <summary>
		/// Moves the menu to the pointer, keeping it fully on screen.
		/// </summary>
		private void PositionAtPointer()
		{
			if (menu == null || Root == null || Root.panel == null)
			{
				return;
			}

			if (!UITKScreenSpace.TryGetPointerPanelPosition(Root.panel, out Vector2 position))
			{
				return;
			}

			UITKScreenSpace.PlaceClamped(Root, menu, position, flip: true);
		}

		/// <summary>
		/// Removes every entry button.
		/// </summary>
		private void ClearEntries()
		{
			entryList?.Clear();
			focusedIndex = -1;
		}

		/// <summary>
		/// Closes the context menu when clicking outside of it.
		/// </summary>
		protected override void OnTick()
		{
			if (!Visible || Time.frameCount == openedFrame)
			{
				return;
			}

			Mouse mouse = Mouse.current;
			if (mouse == null)
			{
				return;
			}

			// Either button dismisses: a right-click elsewhere opens a different menu.
			bool clicked = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
			if (clicked && !pointerInside)
			{
				Hide();
			}
		}

		/// <summary>
		/// Escape closes the menu and the arrow keys walk its entries.
		/// </summary>
		private void OnMenuKeyDown(KeyDownEvent evt)
		{
			if (!Visible || entryList == null)
			{
				return;
			}

			switch (evt.keyCode)
			{
				case KeyCode.Escape:
					evt.StopPropagation();
					Hide();
					return;
				case KeyCode.UpArrow:
					evt.StopPropagation();
					MoveFocus(-1);
					return;
				case KeyCode.DownArrow:
					evt.StopPropagation();
					MoveFocus(1);
					return;
			}
		}

		/// <summary>
		/// Moves keyboard focus through the menu entries.
		/// </summary>
		/// <param name="direction">-1 for up, 1 for down.</param>
		private void MoveFocus(int direction)
		{
			int count = entryList.childCount;
			if (count < 1)
			{
				return;
			}

			// Stops at the ends rather than wrapping, so a held key cannot cycle forever.
			focusedIndex = focusedIndex < 0
				? (direction > 0 ? 0 : count - 1)
				: Mathf.Clamp(focusedIndex + direction, 0, count - 1);

			entryList[focusedIndex].Focus();
		}

		/// <summary>
		/// Hides the menu and drops its entries so stale callbacks cannot be invoked.
		/// </summary>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		/// <remarks>
		/// The override is on <c>Hide(bool)</c>. <c>Hide()</c> forwards here, but quit-to-login
		/// calls <c>Hide(false)</c> directly — so an override on the parameterless form alone
		/// left a menu's captured callbacks, which reference the character that has just gone
		/// away, alive across a return to the login screen.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (overrideIsAlwaysOpen || Document == null)
			{
				// The base refused the hide; the menu is still up, so keep its entries.
				return;
			}

			entries.Clear();
			pointerInside = false;
			ClearEntries();
		}
	}
}
