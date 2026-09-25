using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine.InputSystem;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The keys that drive the hotkey bars: one key per position, one modifier per extra bar.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Previously a static method on the uGUI <c>UIHotkeyBar</c> that named twelve inputs. It is not
	/// a rendering concern — it names input bindings — and the UI Toolkit hotkey bar needs it, so it
	/// moved out of the uGUI tree rather than being duplicated into it.
	/// </para>
	/// <para>
	/// <b>Positions.</b> Left and right mouse, then the <c>Hotkey1</c>..<c>Hotkey0</c> actions of the
	/// input asset, drive positions 0 to 11 of whichever bar a press lands on (see
	/// <see cref="HotkeyBarLayout"/> for which). The mouse buttons are read directly, as they always
	/// were: they are the attack buttons of the world view, not rows in the Key Bindings tab.
	/// </para>
	/// <para>
	/// <b>Bar modifiers are created here, at run time.</b> How many bars there are is a game setting
	/// (<see cref="Constants.Configuration.HotkeyBarCount"/>), so the actions cannot be authored into
	/// the input asset for every game: one <c>HotbarNModifier</c> per extra bar, plus a page up and
	/// page down pair for the paged layout, are added to the Player map when the controls are
	/// created — before any map is enabled, and before the saved overrides are applied, so the Key
	/// Bindings tab lists and rebinds them like any authored action. A single-bar game adds none.
	/// </para>
	/// <para>
	/// <b>Defaults, and why not Shift.</b> The second bar's modifier is Ctrl and the third's Alt;
	/// neither is bound to anything else. Shift is Sprint, so a Shift bar would fire whenever a
	/// sprinting player pressed a number — the overlap this scheme exists to avoid — and the fourth
	/// bar onwards start unbound: clickable, and given a key in Options if the player wants one.
	/// </para>
	/// <para>
	/// <b>Stable binding ids.</b> Saved overrides are matched to bindings by binding id and by
	/// nothing else (<c>LoadBindingOverridesFromJson</c>), and a binding added at run time gets a
	/// fresh random id every launch unless it is given one. Each binding's id is therefore derived
	/// from its action's name, so a rebound modifier is still rebound at the next login.
	/// </para>
	/// </remarks>
	public static class HotkeyKeyMap
	{
		/// <summary>Name of the action that pages a paged hotbar up to the next bar.</summary>
		public const string PageUpActionName = "HotbarPageUp";

		/// <summary>Name of the action that pages a paged hotbar down to the previous bar.</summary>
		public const string PageDownActionName = "HotbarPageDown";

		/// <summary>The input asset's keyboard-and-mouse control scheme group.</summary>
		private const string KeyboardMouseGroup = "Keyboard&Mouse";

		/// <summary>
		/// Default modifier path per bar, by zero-based bar index. The first bar has no modifier;
		/// bars past the end of this table start unbound.
		/// </summary>
		private static readonly string[] DefaultModifierPaths =
		{
			null,
			"<Keyboard>/ctrl",
			"<Keyboard>/alt",
		};

		/// <summary>The input asset's position actions, by position from 2.</summary>
		private static readonly string[] PositionActionNames =
		{
			"Hotkey1", "Hotkey2", "Hotkey3", "Hotkey4", "Hotkey5",
			"Hotkey6", "Hotkey7", "Hotkey8", "Hotkey9", "Hotkey0",
		};

		/// <summary>Positions driven by a mouse button rather than an action.</summary>
		private const int MousePositions = 2;

		/// <summary>The controls the cached actions below were resolved against.</summary>
		private static PlayerControls resolvedFor;

		/// <summary>Cached position actions, indexed like <see cref="PositionActionNames"/>.</summary>
		private static InputAction[] positionActions;

		/// <summary>Cached bar modifier actions, by zero-based bar index; [0] is always null.</summary>
		private static InputAction[] modifierActions;

		/// <summary>Cached page actions.</summary>
		private static InputAction pageUpAction;
		private static InputAction pageDownAction;

		/// <summary>The action name of a bar's modifier.</summary>
		/// <param name="bar">Zero-based bar index, from 1.</param>
		public static string ModifierActionName(int bar) => $"Hotbar{bar + 1}Modifier";

		/// <summary>
		/// The player-facing caption of an action this class creates, for the Key Bindings tab.
		/// </summary>
		/// <param name="actionName">The action's name.</param>
		/// <param name="displayName">The caption, when the action is one of these.</param>
		/// <returns>True when the action is a bar modifier or a page action.</returns>
		public static bool TryGetDisplayName(string actionName, out string displayName)
		{
			displayName = null;
			if (string.IsNullOrEmpty(actionName))
			{
				return false;
			}

			if (string.Equals(actionName, PageUpActionName, StringComparison.Ordinal))
			{
				displayName = "Hotbar Page Up";
				return true;
			}
			if (string.Equals(actionName, PageDownActionName, StringComparison.Ordinal))
			{
				displayName = "Hotbar Page Down";
				return true;
			}

			const string prefix = "Hotbar";
			const string suffix = "Modifier";
			if (actionName.StartsWith(prefix, StringComparison.Ordinal) &&
				actionName.EndsWith(suffix, StringComparison.Ordinal) &&
				int.TryParse(actionName.Substring(prefix.Length, actionName.Length - prefix.Length - suffix.Length), out int number) &&
				number > 1)
			{
				displayName = $"Hotbar {number} Modifier";
				return true;
			}
			return false;
		}

		/// <summary>
		/// Adds the bar modifier and page actions to the Player map. Idempotent.
		/// </summary>
		/// <param name="controls">Freshly created controls whose maps are not yet enabled.</param>
		/// <remarks>
		/// Must run before any map is enabled — an enabled map refuses new actions — and before the
		/// saved overrides are loaded, or the overrides for these bindings find nothing to apply to.
		/// <c>PlayerInputController.EnsureControlsCreated</c> calls it between the two.
		/// </remarks>
		public static void AddBarActions(PlayerControls controls)
		{
			AddBarActions(controls?.asset?.FindActionMap("Player", throwIfNotFound: false), HotkeyBarLayout.BarCount);
		}

		/// <summary>
		/// <see cref="AddBarActions(PlayerControls)"/> for a given map and bar count, so the actions
		/// and the survival of their overrides can be tested for several bars in a game that ships one.
		/// </summary>
		/// <param name="map">The Player action map, not yet enabled.</param>
		/// <param name="bars">How many bars to create modifiers for.</param>
		public static void AddBarActions(InputActionMap map, int bars)
		{
			if (map == null || map.enabled)
			{
				return;
			}

			if (bars <= 1)
			{
				// One bar is the classic strip: no modifier to hold and no page to turn.
				return;
			}

			for (int bar = 1; bar < bars; ++bar)
			{
				string path = bar < DefaultModifierPaths.Length ? DefaultModifierPaths[bar] : null;
				AddButton(map, ModifierActionName(bar), path);
			}

			AddButton(map, PageUpActionName, "<Keyboard>/pageUp");
			AddButton(map, PageDownActionName, "<Keyboard>/pageDown");

			resolvedFor = null;
		}

		/// <summary>Adds one button action with one keyboard binding under a stable id.</summary>
		private static void AddButton(InputActionMap map, string name, string defaultPath)
		{
			if (map.FindAction(name) != null)
			{
				return;
			}

			InputAction action = map.AddAction(name, InputActionType.Button);

			/* An unbound default is still one binding with an empty path, not no binding at all: the
			 * Key Bindings tab lists bindings, not actions, so an action with none would have no row
			 * to bind it from. */
			action.AddBinding(new InputBinding
			{
				path = defaultPath ?? string.Empty,
				groups = KeyboardMouseGroup,
				id = StableId(name),
			});
		}

		/// <summary>
		/// A binding id that is the same at every launch, derived from the action's name.
		/// </summary>
		/// <remarks>
		/// MD5 as a name-to-128-bit mapping, not as security: it only has to be stable and not
		/// collide with the asset's own random ids, and the namespace in the hashed text keeps it
		/// from colliding with anything else hashed the same way.
		/// </remarks>
		private static Guid StableId(string actionName)
		{
			using (MD5 md5 = MD5.Create())
			{
				byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("FishMMO.HotkeyKeyMap/" + actionName + "/0"));
				return new Guid(hash);
			}
		}

		/// <summary>Resolves and caches the actions against the current controls.</summary>
		/// <returns>False when the controls do not exist yet.</returns>
		private static bool EnsureResolved()
		{
			PlayerControls controls = PlayerInputController.Controls;
			if (controls == null)
			{
				return false;
			}
			if (ReferenceEquals(controls, resolvedFor))
			{
				return true;
			}

			InputActionMap map = controls.asset.FindActionMap("Player", throwIfNotFound: false);

			positionActions = new InputAction[PositionActionNames.Length];
			for (int i = 0; i < PositionActionNames.Length; ++i)
			{
				positionActions[i] = map?.FindAction(PositionActionNames[i], throwIfNotFound: false);
			}

			int bars = HotkeyBarLayout.BarCount;
			modifierActions = new InputAction[bars];
			for (int bar = 1; bar < bars; ++bar)
			{
				modifierActions[bar] = map?.FindAction(ModifierActionName(bar), throwIfNotFound: false);
			}

			pageUpAction = map?.FindAction(PageUpActionName, throwIfNotFound: false);
			pageDownAction = map?.FindAction(PageDownActionName, throwIfNotFound: false);

			resolvedFor = controls;
			return true;
		}

		/// <summary>Whether the key for a position is down.</summary>
		/// <param name="position">Zero-based position on a bar.</param>
		public static bool IsPositionPressed(int position)
		{
			if (!EnsureResolved() || position < 0)
			{
				return false;
			}

			switch (position)
			{
				case 0:
					return Mouse.current != null && Mouse.current.leftButton.isPressed;
				case 1:
					return Mouse.current != null && Mouse.current.rightButton.isPressed;
			}

			int index = position - MousePositions;
			return index < positionActions.Length &&
				positionActions[index] != null &&
				positionActions[index].IsPressed();
		}

		/// <summary>
		/// The first bar, in bar order from the second, whose modifier is held; -1 when none is.
		/// </summary>
		public static int FirstHeldModifierBar()
		{
			if (!EnsureResolved())
			{
				return -1;
			}

			for (int bar = 1; bar < modifierActions.Length; ++bar)
			{
				if (modifierActions[bar] != null && modifierActions[bar].IsPressed())
				{
					return bar;
				}
			}
			return -1;
		}

		/// <summary>Whether the page up key is down.</summary>
		public static bool IsPageUpPressed() =>
			EnsureResolved() && pageUpAction != null && pageUpAction.IsPressed();

		/// <summary>Whether the page down key is down.</summary>
		public static bool IsPageDownPressed() =>
			EnsureResolved() && pageDownAction != null && pageDownAction.IsPressed();

		/// <summary>
		/// The key hint drawn in a slot's corner: the position's key, prefixed with the bar's
		/// modifier after the first bar.
		/// </summary>
		/// <param name="bar">Zero-based bar index.</param>
		/// <param name="position">Zero-based position on the bar.</param>
		/// <returns>The hint, or empty when no key reaches the slot.</returns>
		/// <remarks>
		/// Read from the live bindings, so a rebind shows on the bar — it used to be a fixed table,
		/// and a player who moved slot 1 to Q still saw "1" on it. A bar whose modifier is unbound
		/// shows nothing: no key reaches it, and "1" would claim one did.
		/// </remarks>
		public static string SlotLabel(int bar, int position)
		{
			string key = PositionLabel(position);
			if (string.IsNullOrEmpty(key) || bar <= 0)
			{
				return key ?? string.Empty;
			}

			string prefix = ModifierPrefix(bar);
			return string.IsNullOrEmpty(prefix) ? string.Empty : prefix + key;
		}

		/// <summary>The short name of a position's key, or empty when it has none.</summary>
		private static string PositionLabel(int position)
		{
			switch (position)
			{
				case 0:
					return "LMB";
				case 1:
					return "RMB";
			}

			if (!EnsureResolved())
			{
				return string.Empty;
			}

			int index = position - MousePositions;
			if (index < 0 || index >= positionActions.Length || positionActions[index] == null)
			{
				return string.Empty;
			}

			return Shorten(KeyName(positionActions[index]), 4);
		}

		/// <summary>
		/// The one- or few-letter prefix a bar's modifier contributes to its slots' hints.
		/// </summary>
		/// <remarks>
		/// The three conventional modifiers read as the single lower-case letter players know from
		/// other games — c1, a1, s1 — whichever side of the keyboard was bound. Anything else is its
		/// own name, shortened. Null when the modifier is unbound.
		/// </remarks>
		private static string ModifierPrefix(int bar)
		{
			if (!EnsureResolved() || bar <= 0 || bar >= modifierActions.Length || modifierActions[bar] == null)
			{
				return null;
			}

			InputAction action = modifierActions[bar];
			if (action.bindings.Count == 0)
			{
				return null;
			}

			string path = action.bindings[0].effectivePath;
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}

			if (path.IndexOf("ctrl", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "c";
			}
			if (path.IndexOf("alt", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "a";
			}
			if (path.IndexOf("shift", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "s";
			}

			return Shorten(KeyName(action), 3);
		}

		/// <summary>
		/// The name of an action's first binding: the device's own name for the key when it has a
		/// printable one, else the layout's.
		/// </summary>
		/// <remarks>
		/// The same order the Key Bindings tab uses, for the same reason: a device reports some keys
		/// by the character they send, and the character Escape sends draws as a hollow box.
		/// </remarks>
		private static string KeyName(InputAction action)
		{
			if (action.bindings.Count == 0 || string.IsNullOrEmpty(action.bindings[0].effectivePath))
			{
				return string.Empty;
			}

			string display = action.GetBindingDisplayString(0);
			if (IsPrintable(display))
			{
				return display;
			}

			return InputControlPath.ToHumanReadableString(
				action.bindings[0].effectivePath,
				InputControlPath.HumanReadableStringOptions.OmitDevice) ?? string.Empty;
		}

		/// <summary>True when the text has at least one character a font can draw.</summary>
		private static bool IsPrintable(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}
			for (int i = 0; i < text.Length; ++i)
			{
				if (!char.IsWhiteSpace(text[i]) && !char.IsControl(text[i]))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Drops the spaces from a key name and caps its length to fit a slot's corner.</summary>
		private static string Shorten(string name, int maximum)
		{
			if (string.IsNullOrEmpty(name))
			{
				return string.Empty;
			}

			string compact = name.Replace(" ", string.Empty);
			return compact.Length <= maximum ? compact : compact.Substring(0, maximum);
		}
	}
}
