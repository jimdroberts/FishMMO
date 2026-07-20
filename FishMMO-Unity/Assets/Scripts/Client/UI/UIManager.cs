using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
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
			foreach (UICharacterControl control in characterControls.Values)
			{
				control.SetCharacter(character);
			}

			foreach (UITKCharacterControl control in tkCharacterControls.Values)
			{
				control.SetCharacter(character);
			}
		}

		/// <summary>
		/// Removes the character reference from all registered character controls.
		/// </summary>
		internal static void UnsetCharacter()
		{
			foreach (UICharacterControl control in characterControls.Values)
			{
				control.UnsetCharacter();
			}

			foreach (UITKCharacterControl control in tkCharacterControls.Values)
			{
				control.UnsetCharacter();
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
			return false;
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