using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using System.Runtime.CompilerServices;

namespace FishMMO.Client
{
	/// <summary>
	/// Helper class for our UI
	/// </summary>
	public static class UIManager
	{
		// controls map <GameObject Name, Control>
		private static Dictionary<string, UIControl> controls = new Dictionary<string, UIControl>();
		private static Dictionary<string, UICharacterControl> characterControls = new Dictionary<string, UICharacterControl>();
		private static CircularBuffer<UIControl> closeOnEscapeControls = new CircularBuffer<UIControl>();
		private static Client _client;

		/// <summary>
		/// Dependency injection for the Client.
		/// </summary>
		internal static void SetClient(Client client)
		{
			_client = client;

			foreach (UIControl control in controls.Values)
			{
				control.SetClient(client);
			}

			foreach (UICharacterControl control in characterControls.Values)
			{
				control.SetClient(client);
			}
		}

		internal static void SetCharacter(IPlayerCharacter character)
		{
			foreach (UICharacterControl control in characterControls.Values)
			{
				control.SetCharacter(character);
			}
		}

		internal static void UnsetCharacter()
		{
			foreach (UICharacterControl control in characterControls.Values)
			{
				control.UnsetCharacter();
			}
		}

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

			control.SetClient(_client);

			//Debug.Log("UIManager: Registered[" + control.Name + "]");
			controls.Add(control.Name, control);
		}

		internal static void Unregister(UIControl control)
		{
			if (control == null)
			{
				return;
			}
			else
			{
				//Debug.Log("UIManager: Unregistered[" + control.Name + "]");
				controls.Remove(control.Name);
				characterControls.Remove(control.Name);
			}
		}

		internal static void RegisterCloseOnEscapeUI(UIControl control)
		{
			if (control == null)
			{
				return;
			}
			//Debug.Log("UIManager: Registered CloseOnEscapeUI[" + control.Name + "]");
			closeOnEscapeControls.Add(control, control.UIManager_OnAdd, control.UIManager_OnRemove);
		}

		internal static void UnregisterCloseOnEscapeUI(UIControl control)
		{
			if (control == null)
			{
				return;
			}
			else
			{
				//Debug.Log("UIManager: Unregistered CloseOnEscapeUI[" + control.Name + "]");
				closeOnEscapeControls.Remove(control.CurrentNode);
			}
		}

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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Exists(string name)
		{
			if (controls.ContainsKey(name))
			{
				return true;
			}
			return false;
		}

		public static void ToggleVisibility(string name)
		{
			if (controls.TryGetValue(name, out UIControl result))
			{
				result.ToggleVisibility();
			}
			InputManager.ResetForcedMouseMode();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Show(string name)
		{
			if (controls.TryGetValue(name, out UIControl result))
			{
				result.Show();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Hide(string name)
		{
			if (controls.TryGetValue(name, out UIControl result) && result.Visible)
			{
				result.Hide();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void HideAll()
		{
			foreach (KeyValuePair<string, UIControl> p in controls)
			{
				p.Value.Hide();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ShowAll()
		{
			foreach (KeyValuePair<string, UIControl> p in controls)
			{
				p.Value.Show();
			}
		}

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