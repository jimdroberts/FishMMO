using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Registry and lifecycle helper for the client's UI Toolkit panels.
	/// </summary>
	/// <remarks>
	/// Every panel registers itself by GameObject name in <see cref="UITKControl"/>'s Awake, and
	/// the rest of the client reaches it through that name — <c>TryGetTK("UIChat", …)</c>,
	/// <c>ToggleVisibility("UIInventory")</c>. Nothing holds direct references between panels.
	///
	/// There is exactly one registry per lookup. A second, parallel set for a different panel
	/// hierarchy used to sit in front of these, and every generic entry point checked it first
	/// and fell through with <c>else if</c> — so wherever a name existed in both, one panel was
	/// silently unreachable. Keep the lookups single-path.
	/// </remarks>
	public static class UIManager
	{
		/// <summary>
		/// Maps GameObject names to their corresponding <see cref="UITKControl"/> instances.
		/// </summary>
		private static readonly Dictionary<string, UITKControl> controls = new Dictionary<string, UITKControl>();

		/// <summary>
		/// Maps GameObject names to their corresponding <see cref="UITKCharacterControl"/> instances.
		/// </summary>
		/// <remarks>
		/// A second index over a subset of <see cref="controls"/>, so character injection does not
		/// have to type-test every panel on every world entry.
		/// </remarks>
		private static readonly Dictionary<string, UITKCharacterControl> characterControls = new Dictionary<string, UITKCharacterControl>();

		/// <summary>
		/// Visible panels that Escape should close, oldest first.
		/// </summary>
		private static readonly List<UITKControl> closeOnEscapeControls = new List<UITKControl>();

		/// <summary>
		/// Reference to the current Client instance for dependency injection.
		/// </summary>
		private static Client client;

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
		/// Injects the Client instance into all registered controls for network/UI interaction.
		/// </summary>
		/// <param name="value">Client instance to inject.</param>
		internal static void SetClient(Client value)
		{
			client = value;

			foreach (UITKControl control in controls.Values)
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
			foreach (KeyValuePair<string, UITKCharacterControl> kvp in characterControls)
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
		}

		/// <summary>
		/// Clears the character from all registered character controls.
		/// </summary>
		internal static void UnsetCharacter()
		{
			// Isolated for the same reason as SetCharacter: teardown runs on logout and on
			// character despawn from Client.OnCharacterStopLocal, which still has to
			// deinitialize input and destroy the character object afterwards. One panel
			// failing here must not strand the rest or leak the character GameObject.
			foreach (KeyValuePair<string, UITKCharacterControl> kvp in characterControls)
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
		}

		/// <summary>
		/// Registers a panel, making it accessible by name.
		/// </summary>
		/// <param name="control">The control to register.</param>
		internal static void RegisterTK(UITKControl control)
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
					$"A UITKControl named '{control.Name}' is already registered; ignoring the duplicate on " +
					$"GameObject '{control.gameObject.name}'. Lookups by this name will resolve to the first one only.");
				return;
			}

			// Character controls are indexed separately for ease of use.
			UITKCharacterControl characterControl = control as UITKCharacterControl;
			if (characterControl != null)
			{
				characterControls.Add(characterControl.Name, characterControl);
			}

			control.SetClient(client);
			controls.Add(control.Name, control);
		}

		/// <summary>
		/// Unregisters a panel, removing it from the manager.
		/// </summary>
		/// <param name="control">The control to unregister.</param>
		internal static void UnregisterTK(UITKControl control)
		{
			if (control == null)
			{
				return;
			}
			controls.Remove(control.Name);
			characterControls.Remove(control.Name);
		}

		/// <summary>
		/// Tries to retrieve a panel by name and cast it to the specified type.
		/// </summary>
		/// <typeparam name="T">The control subtype to cast to.</typeparam>
		/// <param name="name">The name of the control.</param>
		/// <param name="control">The retrieved control, if found.</param>
		/// <returns>True if the control was found and cast successfully, false otherwise.</returns>
		public static bool TryGetTK<T>(string name, out T control) where T : UITKControl
		{
			if (controls.TryGetValue(name, out UITKControl result))
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
		/// Registers a panel to be closed when Escape is pressed.
		/// </summary>
		/// <param name="control">The control to register.</param>
		internal static void RegisterCloseOnEscapeTK(UITKControl control)
		{
			if (control == null || closeOnEscapeControls.Contains(control))
			{
				return;
			}
			closeOnEscapeControls.Add(control);
		}

		/// <summary>
		/// Removes a panel from the Escape-close list.
		/// </summary>
		/// <param name="control">The control to unregister.</param>
		internal static void UnregisterCloseOnEscapeTK(UITKControl control)
		{
			if (control == null)
			{
				return;
			}
			closeOnEscapeControls.Remove(control);
		}

		/// <summary>
		/// Checks whether a control with the given name is registered.
		/// </summary>
		/// <param name="name">The name of the control.</param>
		/// <returns>True if a control is registered under that name.</returns>
		public static bool Exists(string name)
		{
			return controls.ContainsKey(name);
		}

		/// <summary>
		/// Toggles the visibility of a control.
		/// </summary>
		/// <param name="name">The name of the control.</param>
		public static void ToggleVisibility(string name)
		{
			if (controls.TryGetValue(name, out UITKControl result))
			{
				result.ToggleVisibility();
			}
			PlayerInputController.ResetForcedMouseMode();
		}

		/// <summary>
		/// Shows a control by name.
		/// </summary>
		/// <param name="name">The name of the control.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Show(string name)
		{
			if (controls.TryGetValue(name, out UITKControl result))
			{
				result.Show();
			}
		}

		/// <summary>
		/// Hides a control by name.
		/// </summary>
		/// <param name="name">The name of the control.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Hide(string name)
		{
			if (controls.TryGetValue(name, out UITKControl result) && result.Visible)
			{
				result.Hide();
			}
		}

		/// <summary>
		/// Hides all registered controls.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void HideAll()
		{
			foreach (KeyValuePair<string, UITKControl> p in controls)
			{
				p.Value.Hide();
			}
		}

		/// <summary>
		/// Shows all registered controls.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ShowAll()
		{
			foreach (KeyValuePair<string, UITKControl> p in controls)
			{
				p.Value.Show();
			}
		}

		/// <summary>
		/// Checks if any control has focus, optionally ignoring a specific control.
		/// </summary>
		/// <param name="ignore">An optional control to ignore in the check.</param>
		/// <returns>True if any control has focus, false otherwise.</returns>
		public static bool ControlHasFocus(UITKControl ignore = null)
		{
			foreach (UITKControl control in controls.Values)
			{
				if (control == null)
				{
					continue;
				}
				if (ignore != null && control.Name.Equals(ignore.Name))
				{
					continue;
				}
				if (control.Visible && control.HasFocus)
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
			if (controls.TryGetValue(name, out UITKControl result))
			{
				if (result != null && result.Visible && result.IsInputFieldFocused)
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
		/// <remarks>
		/// This is the gate <c>PlayerInputController</c> puts in front of movement input. If it
		/// ever stops seeing a panel, typing into that panel's text field also drives the
		/// character — every letter bound to a movement key moves the player mid-sentence.
		/// </remarks>
		public static bool InputControlHasFocus(UITKControl ignore = null)
		{
			foreach (UITKControl control in controls.Values)
			{
				if (control == null)
				{
					continue;
				}
				if (ignore != null && control.Name.Equals(ignore.Name))
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
		/// True while any visible panel needs the mouse cursor.
		/// </summary>
		/// <returns>True when at least one visible panel has <c>ReleasesCursor</c> set.</returns>
		/// <remarks>
		/// <c>PlayerInputController</c> asks this before recapturing the cursor for gameplay. It
		/// used to ask <see cref="CloseNext"/> instead, treating "is anything Escape-closable" as
		/// a stand-in for "is anything on screen that needs clicking" — which forced every panel
		/// wanting the cursor to also be Escape-closable, and made a confirm dialog dismissable
		/// with Escape when the whole point of it was to make the player choose.
		/// </remarks>
		public static bool AnyCursorReleasingVisible()
		{
			foreach (UITKControl control in controls.Values)
			{
				if (control != null && control.Visible && control.ReleasesCursor)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Closes the most recently opened Escape-closable panel, if any.
		/// </summary>
		/// <param name="peakOnly">If true, only reports whether one is available without closing it.</param>
		/// <returns>True if a control was closed or is available to close.</returns>
		/// <remarks>
		/// <c>PlayerInputController</c> recaptures the mouse cursor on every frame where this
		/// returns false, so a panel missing from the list releases the cursor in Show() and has
		/// it taken back before the player can click anything — the whole symptom of "the menu
		/// opens but nothing is clickable".
		/// </remarks>
		public static bool CloseNext(bool peakOnly = false)
		{
			for (int i = closeOnEscapeControls.Count - 1; i >= 0; --i)
			{
				UITKControl control = closeOnEscapeControls[i];
				if (control == null || !control.Visible)
				{
					// Destroyed, or hidden by something other than Escape.
					closeOnEscapeControls.RemoveAt(i);
					continue;
				}

				if (peakOnly)
				{
					return true;
				}

				closeOnEscapeControls.RemoveAt(i);
				control.Hide();
				lastCloseFrame = UnityEngine.Time.frameCount;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Checks if every Escape-closable panel has been closed.
		/// </summary>
		/// <returns>True if nothing is left to close.</returns>
		public static bool ClosedAll()
		{
			return closeOnEscapeControls.Count == 0;
		}
	}
}
