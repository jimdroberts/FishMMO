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
	/// Placement is the fiddly part. The pointer arrives in screen pixels and the panel is laid
	/// out in points, so the position is divided by the panel scale before it is applied — and
	/// then clamped, because a menu opened near the right or bottom edge would otherwise render
	/// partly off-screen with its last entries unreachable.
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

			/* Shown before the entries are built and positioned: a panel that has never been
			 * shown has no visual tree, so entryList would still be null and the menu would open
			 * empty the very first time it is used. */
			Show();

			if (entryList == null)
			{
				return;
			}

			ClearEntries();

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
					action?.Invoke();
					Hide();
				};

				entryList.Add(button);
			}

			PositionAtPointer();

			openedFrame = Time.frameCount;
			pointerInside = true;
		}

		/// <summary>
		/// Moves the menu to the pointer, keeping it fully on screen.
		/// </summary>
		private void PositionAtPointer()
		{
			if (menu == null || Root == null)
			{
				return;
			}

			Vector2 screenPosition = Mouse.current != null
				? Mouse.current.position.ReadValue()
				: Vector2.zero;

			/* Screen pixels to panel points. PanelSettings scales the panel against a reference
			 * resolution, so at anything other than that resolution the two spaces differ and
			 * using raw pixels puts the menu progressively further from the cursor. */
			float panelWidth = Root.resolvedStyle.width;
			float panelHeight = Root.resolvedStyle.height;
			if (float.IsNaN(panelWidth) || panelWidth <= 0.0f || Screen.width <= 0)
			{
				return;
			}

			float scaleX = panelWidth / Screen.width;
			float scaleY = panelHeight / Screen.height;

			// Input System reports Y from the bottom; UI Toolkit lays out from the top.
			float x = screenPosition.x * scaleX;
			float y = (Screen.height - screenPosition.y) * scaleY;

			menu.style.left = x;
			menu.style.top = y;

			/* The menu's own size is not resolved until after layout runs, so the clamp is
			 * deferred a frame rather than computed from a width that is still NaN. */
			menu.RegisterCallbackOnce<GeometryChangedEvent>(_ => ClampToPanel(x, y, panelWidth, panelHeight));
		}

		/// <summary>
		/// Nudges the menu back inside the panel when it would overhang an edge.
		/// </summary>
		/// <param name="x">Desired left position in panel points.</param>
		/// <param name="y">Desired top position in panel points.</param>
		/// <param name="panelWidth">Panel width in points.</param>
		/// <param name="panelHeight">Panel height in points.</param>
		private void ClampToPanel(float x, float y, float panelWidth, float panelHeight)
		{
			if (menu == null)
			{
				return;
			}

			float width = menu.resolvedStyle.width;
			float height = menu.resolvedStyle.height;
			if (float.IsNaN(width) || float.IsNaN(height))
			{
				return;
			}

			menu.style.left = Mathf.Clamp(x, 0.0f, Mathf.Max(0.0f, panelWidth - width));
			menu.style.top = Mathf.Clamp(y, 0.0f, Mathf.Max(0.0f, panelHeight - height));
		}

		/// <summary>
		/// Removes every entry button.
		/// </summary>
		private void ClearEntries()
		{
			entryList?.Clear();
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
		/// Hides the menu and drops its entries so stale callbacks cannot be invoked.
		/// </summary>
		public override void Hide()
		{
			ClearEntries();
			pointerInside = false;
			base.Hide();
		}
	}
}
