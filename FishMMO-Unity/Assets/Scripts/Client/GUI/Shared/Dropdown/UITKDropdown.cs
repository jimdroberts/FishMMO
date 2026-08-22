using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit dropdown / context menu. Dynamically builds buttons and toggles and positions
	/// itself at the mouse cursor.
	/// </summary>
	/// <remarks>
	/// Callers build the menu and then show it:
	/// <code>
	/// dropdown.AddButton("Kick", OnKick);
	/// dropdown.AddToggle("Show offline", OnToggleOffline);
	/// dropdown.Show();
	/// </code>
	/// Those <c>AddButton</c> calls cannot create elements straight away. The menu starts hidden,
	/// so before the first <c>Show()</c> there is no visual tree to add them to at all, and after
	/// a hide there is one that is about to be replaced — enabling the document re-clones the
	/// UXML. Either way the entries went into a tree nobody sees and the menu opened empty.
	/// <para>
	/// So the entries are recorded as data here and built into the live tree from
	/// <see cref="OnAfterShow"/> and after any tree rebuild. That also makes the ordering of the
	/// calls stop mattering: build-then-show and show-then-build both work.
	/// </para>
	/// </remarks>
	public class UITKDropdown : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Popup;

		/// <summary>
		/// Name of the dropdown menu container element.
		/// </summary>
		private const string DROPDOWN_MENU_NAME = "dropdown-menu";

		/// <summary>
		/// One recorded menu entry.
		/// </summary>
		/// <remarks>
		/// A button when <see cref="OnClick"/> is set and a toggle when <see cref="OnToggle"/>
		/// is, kept in one list so the entries render in the order the caller added them rather
		/// than with all the buttons above all the toggles.
		/// </remarks>
		private struct Entry
		{
			/// <summary>Label and element name.</summary>
			public string Name;
			/// <summary>Click handler for a button entry.</summary>
			public UnityAction OnClick;
			/// <summary>Change handler for a toggle entry.</summary>
			public UnityAction<bool> OnToggle;
			/// <summary>Initial state for a toggle entry.</summary>
			public bool ToggleValue;
		}

		/// <summary>
		/// Dictionary mapping button names to their Button instances.
		/// </summary>
		/// <remarks>
		/// Rebuilt whenever the entries are built into a tree; a reference kept across a
		/// hide/show would point at an element that has already been discarded.
		/// </remarks>
		public Dictionary<string, Button> Buttons = new Dictionary<string, Button>();

		/// <summary>
		/// Dictionary mapping toggle names to their Toggle instances.
		/// </summary>
		public Dictionary<string, Toggle> Toggles = new Dictionary<string, Toggle>();

		/// <summary>
		/// The entries the caller asked for, in the order they were added.
		/// </summary>
		private readonly List<Entry> entries = new List<Entry>();

		/// <summary>
		/// The dropdown menu container element.
		/// </summary>
		private VisualElement dropdownMenu;

		/// <summary>
		/// Index of the keyboard-highlighted entry, or -1.
		/// </summary>
		private int focusedIndex = -1;

		/// <summary>
		/// Frame the menu was shown on, so the opening click is not read as a click outside it.
		/// </summary>
		private int openedFrame = -1;

		/// <summary>
		/// Resolves cached elements and configures the dropdown for outside-click dismissal.
		/// </summary>
		public override void OnStarting()
		{
			/* The dropdown dismisses itself when the pointer moves off it — no click required. */
			OnLoseFocus -= OnLostFocus;
			OnLoseFocus += OnLostFocus;

			if (Root == null)
			{
				return;
			}

			dropdownMenu = Root.Q<VisualElement>(DROPDOWN_MENU_NAME);
			if (dropdownMenu != null)
			{
				dropdownMenu.style.position = Position.Absolute;
			}

			/* The authored root fills the panel, so leaving it pickable made UITKControl.HasFocus
			 * — which asks the panel what is under the cursor — answer true wherever the pointer
			 * was. OnLoseFocus is driven off that, so the "closes when the pointer moves off it"
			 * behaviour this menu documents could never fire. Ignoring picking on it narrows the
			 * question to "is the pointer over the menu itself", which is the one being asked.
			 *
			 * The element is `dropdown-root`, which is Root's FIRST CHILD, not Root. Root is the
			 * UIDocumentRootElement that UIDocument creates and clones the UXML into, and
			 * UIDocument already sets that one to Ignore — so writing this onto Root was a no-op
			 * against an element that was never the one being picked, and the dropdown went on
			 * reporting focus over the whole screen. Declared in the UXML as picking-mode="Ignore"
			 * (see UIDropdown.uxml, matching UICrosshair and UITooltip); this is the belt-and-braces
			 * copy for a tree built some other way. */
			if (Root.childCount > 0)
			{
				Root[0].pickingMode = PickingMode.Ignore;
			}

			Root.UnregisterCallback<KeyDownEvent>(OnMenuKeyDown, TrickleDown.TrickleDown);
			Root.RegisterCallback<KeyDownEvent>(OnMenuKeyDown, TrickleDown.TrickleDown);
		}

		/// <summary>
		/// Rebuilds the recorded entries into a tree that has just been replaced.
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
		/// Unregisters the outside-click handler.
		/// </summary>
		public override void OnDestroying()
		{
			OnLoseFocus -= OnLostFocus;
			if (Root != null)
			{
				Root.UnregisterCallback<KeyDownEvent>(OnMenuKeyDown, TrickleDown.TrickleDown);
			}
		}

		/// <summary>
		/// Builds the recorded entries and moves the menu to the current mouse position.
		/// </summary>
		protected override void OnAfterShow()
		{
			BuildEntries();
			PositionAtPointer();
			openedFrame = Time.frameCount;
		}

		/// <summary>
		/// Hides the dropdown and removes all buttons and toggles.
		/// </summary>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		/// <remarks>
		/// The override is on <c>Hide(bool)</c>, not on <c>Hide()</c>. <c>Hide()</c> simply
		/// forwards to this one, but quit-to-login calls <c>Hide(false)</c> directly — so an
		/// override that only covered the parameterless form left a menu's entries, and their
		/// captured callbacks, alive across a return to the login screen.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (overrideIsAlwaysOpen || Document == null)
			{
				// The base refused the hide; the menu is still up, so keep its entries.
				return;
			}

			ClearItems();
		}

		/// <summary>
		/// Adds a new button to the dropdown with the specified name and click callback.
		/// </summary>
		/// <param name="buttonName">Name of the button.</param>
		/// <param name="onClick">Callback for button click.</param>
		public void AddButton(string buttonName, UnityAction onClick)
		{
			if (string.IsNullOrEmpty(buttonName) || ContainsEntry(buttonName))
			{
				return;
			}

			entries.Add(new Entry
			{
				Name = buttonName,
				OnClick = onClick,
			});

			// Added while the menu is already up: build it straight into the live tree.
			if (Visible)
			{
				BuildEntries();
			}
		}

		/// <summary>
		/// Adds a new toggle to the dropdown with the specified name and state change callback.
		/// </summary>
		/// <param name="toggleName">Name of the toggle.</param>
		/// <param name="onToggleStateChanged">Callback for toggle state change.</param>
		/// <param name="value">Initial state of the toggle.</param>
		public void AddToggle(string toggleName, UnityAction<bool> onToggleStateChanged, bool value = false)
		{
			if (string.IsNullOrEmpty(toggleName) || ContainsEntry(toggleName))
			{
				return;
			}

			entries.Add(new Entry
			{
				Name = toggleName,
				OnToggle = onToggleStateChanged,
				ToggleValue = value,
			});

			if (Visible)
			{
				BuildEntries();
			}
		}

		/// <summary>
		/// Reports whether an entry with this name has already been added.
		/// </summary>
		private bool ContainsEntry(string name)
		{
			for (int i = 0; i < entries.Count; ++i)
			{
				if (entries[i].Name == name)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Builds every recorded entry into the live menu element.
		/// </summary>
		/// <remarks>
		/// Idempotent: it clears the container first, so it can be called again after a tree
		/// rebuild or after a late <see cref="AddButton"/> without duplicating anything.
		/// </remarks>
		private void BuildEntries()
		{
			if (dropdownMenu == null)
			{
				return;
			}

			dropdownMenu.Clear();
			Buttons.Clear();
			Toggles.Clear();

			for (int i = 0; i < entries.Count; ++i)
			{
				Entry entry = entries[i];

				if (entry.OnToggle != null)
				{
					Toggle toggle = new Toggle(entry.Name)
					{
						name = entry.Name,
						value = entry.ToggleValue,
					};
					toggle.AddToClassList("dropdown-item");

					// Captured by value; the loop variable would be shared by every handler.
					UnityAction<bool> onToggle = entry.OnToggle;
					toggle.RegisterValueChangedCallback((evt) => onToggle?.Invoke(evt.newValue));

					dropdownMenu.Add(toggle);
					Toggles[entry.Name] = toggle;
					continue;
				}

				UnityAction onClick = entry.OnClick;
				Button button = new Button(() =>
				{
					/* Hidden before the callback runs, not after: these callbacks open dialogs
					 * and other menus, and hiding afterwards would pull the rug from under
					 * whatever they just put on screen. */
					Hide();
					onClick?.Invoke();
				})
				{
					text = entry.Name,
					name = entry.Name,
				};
				button.AddToClassList("fish-button");
				button.AddToClassList("dropdown-item");

				dropdownMenu.Add(button);
				Buttons[entry.Name] = button;
			}

			focusedIndex = -1;
		}

		/// <summary>
		/// Removes and clears all tracked buttons and toggles from the menu.
		/// </summary>
		private void ClearItems()
		{
			if (dropdownMenu != null)
			{
				dropdownMenu.Clear();
			}
			Buttons.Clear();
			Toggles.Clear();
			entries.Clear();
			focusedIndex = -1;
		}

		/// <summary>
		/// Moves the menu to the pointer, keeping it inside the panel.
		/// </summary>
		/// <remarks>
		/// Went through <c>ScreenToPanel</c> with a raw Input System position, which measures Y
		/// from the bottom of the screen while UI Toolkit lays out from the top — so a menu
		/// opened near the top of the screen appeared near the bottom — and it never clamped, so
		/// one opened near an edge hung off it with its last entries unreachable. Both live in
		/// <see cref="UITKScreenSpace"/> now.
		/// </remarks>
		private void PositionAtPointer()
		{
			if (dropdownMenu == null || Root == null || Root.panel == null)
			{
				return;
			}

			if (!UITKScreenSpace.TryGetPointerPanelPosition(Root.panel, out Vector2 position))
			{
				return;
			}

			UITKScreenSpace.PlaceClamped(Root, dropdownMenu, position, flip: true);
		}

		/// <summary>
		/// Hides the dropdown when the pointer leaves it.
		/// </summary>
		private void OnLostFocus()
		{
			Hide();
		}

		/// <summary>
		/// Closes the menu on a click anywhere outside it.
		/// </summary>
		/// <remarks>
		/// Polled rather than handled as an event on the root, because the root no longer takes
		/// part in picking — see <see cref="OnStarting"/>. This is also the safety net for a menu
		/// that opens away from the cursor and so never gets a pointer-leave to close on.
		/// <para>
		/// The frame the menu opened on is skipped: the click that opened it is still down when
		/// this first runs, and would be read as a click elsewhere.
		/// </para>
		/// </remarks>
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

			bool clicked = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
			if (clicked && !HasFocus)
			{
				Hide();
			}
		}

		/// <summary>
		/// Escape closes the menu; the arrow keys walk it; Enter activates the highlighted entry.
		/// </summary>
		private void OnMenuKeyDown(KeyDownEvent evt)
		{
			if (!Visible || dropdownMenu == null)
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
			int count = dropdownMenu.childCount;
			if (count < 1)
			{
				return;
			}

			int next = focusedIndex < 0
				? (direction > 0 ? 0 : count - 1)
				: Mathf.Clamp(focusedIndex + direction, 0, count - 1);

			focusedIndex = next;
			dropdownMenu[next].Focus();
		}
	}
}
