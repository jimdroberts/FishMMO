using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using System.Runtime.CompilerServices;
using UnityEngine.InputSystem.EnhancedTouch;

namespace FishMMO.Client
{
	/// <summary>
	/// Helper class for our UI
	/// </summary>
	public static class UIManager
	{
		/// <summary>
		/// Maps GameObject names to their corresponding UIControl instances.
		/// </summary>
		private static Dictionary<string, UIControl> controls = new Dictionary<string, UIControl>();
		/// <summary>
		/// Maps GameObject names to their corresponding UICharacterControl instances.
		/// </summary>
		private static Dictionary<string, UICharacterControl> characterControls = new Dictionary<string, UICharacterControl>();
		/// <summary>
		/// Buffer of UIControls that should be closed when Escape is pressed, in last-opened order.
		/// </summary>
		private static CircularBuffer<UIControl> closeOnEscapeControls = new CircularBuffer<UIControl>();
		/// <summary>
		/// Reference to the current Client instance for dependency injection.
		/// </summary>
		private static Client client;

		// ── UI Toolkit parallel registries ────────────────────────────────

		/// <summary>
		/// Maps GameObject names to their corresponding UITKControl instances.
		/// </summary>
		private static Dictionary<string, UITKControl> tkControls = new Dictionary<string, UITKControl>();

		/// <summary>
		/// Visible UI Toolkit panels that Escape should close, oldest first.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="closeOnEscapeControls"/> only because that one is a
		/// <c>CircularBuffer&lt;UIControl&gt;</c> keyed on node bookkeeping the legacy class owns —
		/// a <see cref="UITKControl"/> cannot join it without inheriting from a UGUI base. A plain
		/// list is enough here: these are opened and closed by hand, never in bulk.
		/// </remarks>
		private static readonly List<UITKControl> tkCloseOnEscapeControls = new List<UITKControl>();

		/// <summary>
		/// Frame on which <see cref="CloseNext"/> last actually closed something.
		/// </summary>
		private static int lastCloseFrame = -1;

		/// <summary>
		/// True when a panel was closed earlier in this same frame.
		/// </summary>
		/// <remarks>
		/// Escape is bound to four separate actions — Player/Cancel, Player/CloseLastUI,
		/// Player/Menu and UI/Cancel — so one press runs several handlers in subscription order.
		/// CloseLastUI closes the top panel, and Menu then toggles the menu, which sees it closed
		/// and opens it straight back up: the menu becomes impossible to close with the key that
		/// opened it. A handler that would re-open something checks this first so the close wins.
		/// </remarks>
		internal static bool ClosedThisFrame => lastCloseFrame == UnityEngine.Time.frameCount;
		/// <summary>
		/// Maps GameObject names to their corresponding UITKCharacterControl instances.
		/// </summary>
		private static Dictionary<string, UITKCharacterControl> tkCharacterControls = new Dictionary<string, UITKCharacterControl>();

		/// <summary>
		/// Injects the Client instance into all registered controls for network/UI interaction.
		/// </summary>
		/// <param name="client">Client instance to inject.</param>
		internal static void SetClient(Client value)
		{
			UIManager.client = value;

			foreach (UIControl control in controls.Values)
			{
				control.SetClient(client);
			}

			foreach (UICharacterControl control in characterControls.Values)
			{
				control.SetClient(client);
			}

			foreach (UITKControl control in tkControls.Values)
			{
				control.SetClient(client);
			}
		}

		/// <summary>
		/// Injects the IPlayerCharacter instance into all registered character controls.
		/// </summary>
		/// <param name="character">Player character to inject.</param>
		internal static void SetCharacter(IPlayerCharacter character)
		{
			/* Each control is isolated. This runs inside Client.OnCharacterStartLocal, which
			 * also dismisses the loading screen and wires up the player input controller — so
			 * an exception from any single panel aborted world entry entirely, leaving the
			 * player on a black screen with the loading overlay never dismissed and no input.
			 * A misconfigured panel must cost only that panel.
			 *
			 * The dictionary key is used for the log label rather than control.Name: Name reads
			 * gameObject.name, which throws MissingReferenceException on a destroyed control —
			 * a throw from inside the handler meant to contain throws. The key is the same
			 * string (Register maps controls by Name) and is always safe to read. */
			foreach (KeyValuePair<string, UICharacterControl> kvp in characterControls)
			{
				try
				{
					kvp.Value.SetCharacter(character);
				}
				catch (Exception ex)
				{
					Log.Error("UIManager", $"SetCharacter failed for control '{kvp.Key}'.", ex);
				}
			}

			foreach (KeyValuePair<string, UITKCharacterControl> kvp in tkCharacterControls)
			{
				try
				{
					kvp.Value.SetCharacter(character);
				}
				catch (Exception ex)
				{
					Log.Error("UIManager", $"SetCharacter failed for UIToolkit control '{kvp.Key}'.", ex);
				}
			}
		}

		/// <summary>
		/// Removes the character reference from all registered character controls.
		/// </summary>
		internal static void UnsetCharacter()
		{
			// Isolated for the same reason as SetCharacter: teardown runs on logout and on
			// character despawn from Client.OnCharacterStopLocal, which still has to
			// deinitialize input and destroy the character object afterwards. One panel
			// failing here must not strand the rest or leak the character GameObject.
			foreach (KeyValuePair<string, UICharacterControl> kvp in characterControls)
			{
				try
				{
					kvp.Value.UnsetCharacter();
				}
				catch (Exception ex)
				{
					Log.Error("UIManager", $"UnsetCharacter failed for control '{kvp.Key}'.", ex);
				}
			}

			foreach (KeyValuePair<string, UITKCharacterControl> kvp in tkCharacterControls)
			{
				try
				{
					kvp.Value.UnsetCharacter();
				}
				catch (Exception ex)
				{
					Log.Error("UIManager", $"UnsetCharacter failed for UIToolkit control '{kvp.Key}'.", ex);
				}
			}
		}

		/// <summary>
		/// Registers a new UIControl instance, making it accessible by name.
		/// </summary>
		/// <param name="control">The UIControl instance to register.</param>
		internal static void Register(UIControl control)
		{
			if (control == null)
			{
				return;
			}
			if (controls.ContainsKey(control.Name))
			{
				/* Controls are keyed by GameObject name, and every lookup in the codebase is by
				 * that string. Dropping a duplicate silently meant a control could be present in
				 * the scene, fully wired, and simply never reachable — indistinguishable from
				 * not being there at all, and only discovered when whatever needed it did
				 * nothing. Name it. */
				Log.Warning("UIManager",
					$"A UIControl named '{control.Name}' is already registered; ignoring the duplicate on " +
					$"GameObject '{control.gameObject.name}'. Lookups by this name will resolve to the first one only.");
				return;
			}

			// character controls are mapped separately for ease of use
			UICharacterControl characterControl = control as UICharacterControl;
			if (characterControl != null)
			{
				characterControls.Add(characterControl.Name, characterControl);
			}

			control.SetClient(client);

			//Log.Debug("UIManager: Registered[" + control.Name + "]");
			controls.Add(control.Name, control);
		}

		/// <summary>
		/// Unregisters a UIControl instance, removing it from the manager.
		/// </summary>
		/// <param name="control">The UIControl instance to unregister.</param>
		internal static void Unregister(UIControl control)
		{
			if (control == null)
			{
				return;
			}
			else
			{
				//Log.Debug("UIManager: Unregistered[" + control.Name + "]");
				controls.Remove(control.Name);
				characterControls.Remove(control.Name);
			}
		}

		/// <summary>
		/// Registers a UITKControl instance, making it accessible by name.
		/// </summary>
		/// <param name="control">The UITKControl instance to register.</param>
		internal static void RegisterTK(UITKControl control)
		{
			if (control == null)
			{
				return;
			}
			if (tkControls.ContainsKey(control.Name))
			{
				// Same silent-drop hazard as Register above.
				Log.Warning("UIManager",
					$"A UITKControl named '{control.Name}' is already registered; ignoring the duplicate on " +
					$"GameObject '{control.gameObject.name}'. Lookups by this name will resolve to the first one only.");
				return;
			}

			UITKCharacterControl characterControl = control as UITKCharacterControl;
			if (characterControl != null)
			{
				tkCharacterControls.Add(characterControl.Name, characterControl);
			}

			control.SetClient(client);

			tkControls.Add(control.Name, control);
		}

		/// <summary>
		/// Unregisters a UITKControl instance, removing it from the manager.
		/// </summary>
		/// <param name="control">The UITKControl instance to unregister.</param>
		internal static void UnregisterTK(UITKControl control)
		{
			if (control == null)
			{
				return;
			}
			tkControls.Remove(control.Name);
			tkCharacterControls.Remove(control.Name);
		}

		/// <summary>
		/// Tries to retrieve a UITKControl by name and cast it to the specified type.
		/// </summary>
		/// <typeparam name="T">The UITKControl subtype to cast to.</typeparam>
		/// <param name="name">The name of the control.</param>
		/// <param name="control">The retrieved control, if found.</param>
		/// <returns>True if the control was found and cast successfully, false otherwise.</returns>
		public static bool TryGetTK<T>(string name, out T control) where T : UITKControl
		{
			if (tkControls.TryGetValue(name, out UITKControl result))
			{
				control = result as T;
				if (control != null)
				{
					return true;
				}
			}
			control = null;
			return false;
		}

		/// <summary>
		/// Registers a UIControl to be closed when Escape is pressed.
		/// </summary>
		/// <param name="control">The UIControl instance to register.</param>
		internal static void RegisterCloseOnEscapeUI(UIControl control)
		{
			if (control == null)
			{
				return;
			}
			//Log.Debug("UIManager: Registered CloseOnEscapeUI[" + control.Name + "]");
			closeOnEscapeControls.Add(control, control.UIManager_OnAdd, control.UIManager_OnRemove);
		}

		/// <summary>
		/// Unregisters a UIControl from the Escape close list.
		/// </summary>
		/// <param name="control">The UIControl instance to unregister.</param>
		internal static void UnregisterCloseOnEscapeUI(UIControl control)
		{
			if (control == null)
			{
				return;
			}
			else
			{
				//Log.Debug("UIManager: Unregistered CloseOnEscapeUI[" + control.Name + "]");
				closeOnEscapeControls.Remove(control.CurrentNode);
			}
		}

		/// <summary>
		/// Tries to retrieve a control by name and cast it to the specified type.
		/// </summary>
		/// <typeparam name="T">The type to cast the control to.</typeparam>
		/// <param name="name">The name of the control.</param>
		/// <param name="control">The retrieved control, if found.</param>
		/// <returns>True if the control was found and cast successfully, false otherwise.</returns>
		public static bool TryGet<T>(string name, out T control) where T : UIControl
		{
			if (controls.TryGetValue(name, out UIControl result))
			{
				control = result as T;
				if (control != null)
				{
					return true;
				}
			}
			control = null;

			WarnIfMigrated(name, typeof(T));

			return false;
		}

		/// <summary>
		/// Reports a lookup that failed only because the panel has moved to UI Toolkit.
		/// </summary>
		/// <remarks>
		/// A caller asking for a UGUI panel by name gets a plain false when that panel now exists
		/// as a <see cref="UITKControl"/>, because the two live in separate registries and are
		/// unrelated types. The call site is invariably an <c>if</c>, so the feature it guards
		/// stops working with no exception, no log, and nothing on screen — the keybind simply
		/// does nothing. Migrating a panel silently breaks every name lookup still pointing at it,
		/// and this is the only place that can notice.
		/// <para>
		/// Only the mismatched case is reported. A name absent from both registries is an ordinary
		/// miss — plenty of callers ask about panels that are not in the current scene — and
		/// logging those would bury the real signal.
		/// </para>
		/// </remarks>
		/// <param name="name">The name that was looked up.</param>
		/// <param name="requestedType">The type the caller asked for.</param>
		private static void WarnIfMigrated(string name, Type requestedType)
		{
			if (!tkControls.TryGetValue(name, out UITKControl migrated))
			{
				return;
			}

			Log.Error("UIManager",
				$"'{name}' was requested as {requestedType.Name} (UGUI) but is registered as " +
				$"{migrated.GetType().Name} (UI Toolkit). This lookup returns false and whatever " +
				$"it guards will silently do nothing. Use TryGetTK<{migrated.GetType().Name}>(\"{name}\", ...) instead.");
		}

		/// <summary>
		/// Marks a UI Toolkit panel as open, so Escape closes it and the cursor stays released.
		/// </summary>
		/// <param name="control">The panel that just became visible.</param>
		internal static void RegisterCloseOnEscapeTK(UITKControl control)
		{
			if (control == null)
			{
				return;
			}

			// Re-registering moves it to the top, so the newest panel closes first.
			tkCloseOnEscapeControls.Remove(control);
			tkCloseOnEscapeControls.Add(control);
		}

		/// <summary>
		/// Marks a UI Toolkit panel as closed.
		/// </summary>
		/// <param name="control">The panel that is no longer visible.</param>
		internal static void UnregisterCloseOnEscapeTK(UITKControl control)
		{
			if (control == null)
			{
				return;
			}

			tkCloseOnEscapeControls.Remove(control);
		}

		/// <summary>
		/// Checks if a control exists by name in either the UGUI or UIToolkit registry.
		/// </summary>
		/// <param name="name">The name of the control.</param>
		/// <returns>True if the control exists, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Exists(string name)
		{
			return controls.ContainsKey(name) || tkControls.ContainsKey(name);
		}

		/// <summary>
		/// Toggles the visibility of a control in either the UGUI or UIToolkit registry.
		/// </summary>
		/// <param name="name">The name of the control.</param>
		public static void ToggleVisibility(string name)
		{
			if (controls.TryGetValue(name, out UIControl result))
			{
				result.ToggleVisibility();
			}
			else if (tkControls.TryGetValue(name, out UITKControl tkResult))
			{
				tkResult.ToggleVisibility();
			}
			PlayerInputController.ResetForcedMouseMode();
		}

		/// <summary>
		/// Shows a control by name, searching both UGUI and UIToolkit registries.
		/// </summary>
		/// <param name="name">The name of the control.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Show(string name)
		{
			if (controls.TryGetValue(name, out UIControl result))
			{
				result.Show();
			}
			else if (tkControls.TryGetValue(name, out UITKControl tkResult))
			{
				tkResult.Show();
			}
		}

		/// <summary>
		/// Hides a control by name, searching both UGUI and UIToolkit registries.
		/// </summary>
		/// <param name="name">The name of the control.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Hide(string name)
		{
			if (controls.TryGetValue(name, out UIControl result) && result.Visible)
			{
				result.Hide();
			}
			else if (tkControls.TryGetValue(name, out UITKControl tkResult) && tkResult.Visible)
			{
				tkResult.Hide();
			}
		}

		/// <summary>
		/// Hides all registered controls in both UGUI and UIToolkit registries.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void HideAll()
		{
			foreach (KeyValuePair<string, UIControl> p in controls)
			{
				p.Value.Hide();
			}
			foreach (KeyValuePair<string, UITKControl> p in tkControls)
			{
				p.Value.Hide();
			}
		}

		/// <summary>
		/// Shows all registered controls in both UGUI and UIToolkit registries.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ShowAll()
		{
			foreach (KeyValuePair<string, UIControl> p in controls)
			{
				p.Value.Show();
			}
			foreach (KeyValuePair<string, UITKControl> p in tkControls)
			{
				p.Value.Show();
			}
		}

		/// <summary>
		/// Checks if any control has focus, optionally ignoring a specific control.
		/// </summary>
		/// <param name="ignore">An optional control to ignore in the check.</param>
		/// <returns>True if any control has focus, false otherwise.</returns>
		public static bool ControlHasFocus(UIControl ignore = null)
		{
			foreach (UIControl control in controls.Values)
			{
				if (ignore != null &&
					control.Name.Equals(ignore.Name))
				{
					continue;
				}
				if (control.Visible &&
					control.HasFocus)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Checks if a specific input control has focus.
		/// </summary>
		/// <param name="name">The name of the control.</param>
		/// <returns>True if the control is an input field and has focus, false otherwise.</returns>
		public static bool InputControlHasFocus(string name)
		{
			if (controls.TryGetValue(name, out UIControl result))
			{
				if (result.Visible && result.IsInputFieldFocused)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Checks if any input control has focus, optionally ignoring a specific control.
		/// </summary>
		/// <param name="ignore">An optional control to ignore in the check.</param>
		/// <returns>True if any input control has focus, false otherwise.</returns>
		public static bool InputControlHasFocus(UIControl ignore = null)
		{
			foreach (UIControl control in controls.Values)
			{
				if (ignore != null &&
					control.Name.Equals(ignore.Name))
				{
					continue;
				}
				if (control.Visible && control.IsInputFieldFocused)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Closes the next UIControl in the close-on-escape list, if available.
		/// </summary>
		/// <param name="peakOnly">If true, only peeks at the next control without closing it.</param>
		/// <returns>True if a control was closed or peeked, false otherwise.</returns>
		public static bool CloseNext(bool peakOnly = false)
		{
			/* UI Toolkit panels are checked first, so the most recently opened panel wins while
			 * the migration has both kinds on screen at once.
			 *
			 * They must be checked at all: PlayerInputController recaptures the mouse cursor on
			 * every frame where this returns false, so a panel missing from here releases the
			 * cursor in Show() and has it taken back before the player can click anything. That
			 * was the whole symptom of "the menu opens but nothing is clickable". */
			for (int i = tkCloseOnEscapeControls.Count - 1; i >= 0; --i)
			{
				UITKControl tkControl = tkCloseOnEscapeControls[i];
				if (tkControl == null || !tkControl.Visible)
				{
					// Destroyed, or hidden by something other than Escape.
					tkCloseOnEscapeControls.RemoveAt(i);
					continue;
				}

				if (peakOnly)
				{
					return true;
				}

				tkCloseOnEscapeControls.RemoveAt(i);
				tkControl.Hide();
				lastCloseFrame = UnityEngine.Time.frameCount;
				return true;
			}

			if (closeOnEscapeControls != null)
			{
				if (peakOnly)
				{
					return closeOnEscapeControls.Peek();
				}
				else
				{
					// get the last opened or focused UI control
					UIControl control = closeOnEscapeControls.Pop();
					if (control != null)
					{
						control.Hide();
						lastCloseFrame = UnityEngine.Time.frameCount;
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Checks if all UIControls in the close-on-escape list have been closed.
		/// </summary>
		/// <returns>True if all controls are closed, false otherwise.</returns>
		public static bool ClosedAll()
		{
			if (closeOnEscapeControls != null &&
				closeOnEscapeControls.Empty())
			{
				return true;
			}
			return false;
		}
	}
}