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

			/* Each control is isolated, for exactly the reason SetCharacter documents. This runs
			 * on the connection path and injects into every registered panel; an override of
			 * OnClientSet that throws — a broadcast registration against a half-built network
			 * manager is the usual way — aborted the loop, so every panel after the failing one in
			 * dictionary order silently never received the client and none of them could send or
			 * receive anything for the rest of the session. A misconfigured panel must cost only
			 * that panel.
			 *
			 * The dictionary key is used for the log label rather than control.Name, which reads
			 * gameObject.name and throws MissingReferenceException on a destroyed control — a
			 * throw from inside the handler meant to contain throws. */
			foreach (KeyValuePair<string, UITKControl> kvp in controls)
			{
				try
				{
					kvp.Value.SetClient(client);
				}
				catch (Exception ex)
				{
					Log.Error("UIManager", $"SetClient failed for control '{kvp.Key}'.", ex);
				}
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

			/* Both indexes are populated BEFORE any control code runs. SetClient calls the panel's
			 * own OnClientSet, which can throw, and with the injection sitting between the two Adds
			 * a throw left the panel present in characterControls and absent from controls — so
			 * world entry still handed it a character while every lookup by name missed it, and
			 * InputControlHasFocus stopped seeing it, which is the one that hurts: typing in that
			 * panel's text field starts driving the character.
			 *
			 * Isolated as well as reordered, because a panel that fails to take the client is still
			 * a registered panel and Awake must not be aborted for it. */
			controls.Add(control.Name, control);

			try
			{
				control.SetClient(client);
			}
			catch (Exception ex)
			{
				Log.Error("UIManager", $"SetClient failed while registering control '{control.Name}'.", ex);
			}
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

			/* Identity, not just name. RegisterTK deliberately REFUSES a second control sharing a
			 * name and returns without adding it — so the rejected duplicate is not in either
			 * dictionary, and removing by name alone evicts the registered, working panel when the
			 * duplicate is destroyed. Everything downstream then fails silently: TryGetTK misses,
			 * SetCharacter skips it, and InputControlHasFocus stops seeing it, which is the one
			 * that hurts — typing in that panel's text field starts driving the character. */
			if (controls.TryGetValue(control.Name, out UITKControl registered) &&
				ReferenceEquals(registered, control))
			{
				controls.Remove(control.Name);
			}
			if (characterControls.TryGetValue(control.Name, out UITKCharacterControl registeredCharacter) &&
				ReferenceEquals(registeredCharacter, control))
			{
				characterControls.Remove(control.Name);
			}
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
				return;
			}

			/* A miss here is how a panel goes missing without anything reporting it: the caller
			 * asked for a HUD element by name, no control answered to that name, and the method
			 * returned as though it had worked. The name is authored in a scene and the string
			 * is authored in code, so the two drift apart silently — which looks identical to a
			 * panel that rendered nothing. */
			Log.Warning("UIManager", $"Show('{name}') found no registered control with that name. Registered: {string.Join(", ", controls.Keys)}");
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
		/// Re-reads every panel's stored position and moves the live panels to match.
		/// </summary>
		/// <remarks>
		/// For the case where the stored positions changed underneath the panels rather than
		/// because of them — loading a shared UI profile is the only one today. Panels read their
		/// position once, on first layout, so writing new coordinates into configuration alone has
		/// no visible effect until the client restarts.
		/// <para>
		/// Isolated per panel, as <see cref="ResetAllPanelPositions"/> is and for the same reason:
		/// one panel that throws must not strand the rest halfway through somebody else's layout.
		/// </para>
		/// </remarks>
		public static void ReloadAllPanelPositions()
		{
			foreach (KeyValuePair<string, UITKControl> kvp in controls)
			{
				try
				{
					kvp.Value.ReloadStoredPosition();
				}
				catch (Exception ex)
				{
					Log.Error("UIManager", $"ReloadStoredPosition failed for control '{kvp.Key}'.", ex);
				}
			}
		}

		/// <summary>
		/// Puts every panel back where its stylesheet places it and forgets the player's saved
		/// arrangement.
		/// </summary>
		/// <remarks>
		/// The escape hatch for a layout the player cannot fix by dragging — a panel moved to a
		/// corner of a monitor they no longer have, or an arrangement they simply want back.
		/// Offered from the options screen.
		/// <para>
		/// Isolated per panel for the reason <see cref="SetCharacter"/> documents: this is a
		/// recovery action, and one panel that throws on the way must not stop the rest from
		/// being recovered.
		/// </para>
		/// </remarks>
		public static void ResetAllPanelPositions()
		{
			foreach (KeyValuePair<string, UITKControl> kvp in controls)
			{
				try
				{
					kvp.Value.ResetPosition();
				}
				catch (Exception ex)
				{
					Log.Error("UIManager", $"ResetPosition failed for control '{kvp.Key}'.", ex);
				}
			}

			UITKPanelPositions.Flush();
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
		/// True when another visible, input-accepting panel is drawn above <paramref name="control"/>.
		/// </summary>
		/// <param name="control">The panel asking whether it is still the one in front.</param>
		/// <returns>True when something covers it.</returns>
		/// <remarks>
		/// This is the guard the login-flow keyboard shortcuts sit behind. UI Toolkit routes a key
		/// press to the focused element and up its own ancestors only, so a panel normally cannot
		/// see keys meant for another one — but nothing moves focus when a panel opens <i>over</i>
		/// another. Opening Options from the sign-in screen is the case that bit: Options draws on
		/// top and takes no focus, so the caret stayed in the password field behind it and pressing
		/// Enter signed the player in through a panel they could no longer see.
		/// <para>
		/// Decided on draw order rather than on layer so that two panels sharing a layer — which
		/// <see cref="UITKControl.BringToFront"/> separates by a focus offset — still resolve to
		/// exactly one front-most panel. Panels that do not accept pointer input are ignored; see
		/// <see cref="UITKControl.AcceptsPointerInput"/>.
		/// </para>
		/// </remarks>
		public static bool IsCoveredByHigherPanel(UITKControl control)
		{
			if (control == null || control.Document == null)
			{
				return false;
			}

			float order = control.SortingOrder;

			foreach (UITKControl other in controls.Values)
			{
				if (other == null || ReferenceEquals(other, control))
				{
					continue;
				}
				if (!other.Visible || other.Document == null)
				{
					continue;
				}
				if (other.SortingOrder > order && other.AcceptsPointerInput)
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

				/* An always-open panel cannot be closed, so it is not a candidate — but it used to
				 * be treated as one: the entry was removed, Hide() no-opped against IsAlwaysOpen,
				 * and lastCloseFrame was stamped anyway. The press was therefore consumed with
				 * nothing closing, ClosedThisFrame suppressed the menu toggle that would otherwise
				 * have handled it, and the panel the player actually wanted closed stayed up while
				 * appearing to have been dealt with. Left in the list, because it is still visible
				 * and still Escape-registered; skipping simply moves on to the next candidate. */
				if (control.IsAlwaysOpen)
				{
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
