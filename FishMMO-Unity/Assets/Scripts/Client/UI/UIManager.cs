using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Helper class for our UI
	/// </summary>
	public static class UIManager
	{
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
		}

		internal static void SetCharacter(Character character)
		{
			foreach (UICharacterControl control in characterControls.Values)
			{
				control.SetCharacter(character);
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

			Debug.Log("UIManager: Registered[" + control.Name + "]");
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
				Debug.Log("UIManager: Unregistered[" + control.Name + "]");
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
		}

		public static void Show(string name)
		{
			if (controls.TryGetValue(name, out UIControl result))
			{
				result.Show();
			}
		}

		public static void Hide(string name)
		{
			if (controls.TryGetValue(name, out UIControl result) && result.Visible)
			{
				result.Hide();
			}
		}

		public static void HideAll()
		{
			foreach (KeyValuePair<string, UIControl> p in controls)
			{
				p.Value.Hide();
			}
		}

		public static void ShowAll()
		{
			foreach (KeyValuePair<string, UIControl> p in controls)
			{
				p.Value.Show();
			}
		}

		public static bool ControlHasFocus()
		{
			if (EventSystem.current.currentSelectedGameObject != null)
			{
				return true;
			}
			foreach (UIControl control in controls.Values)
			{
				if (control.Visible &&
					control.HasFocus)
				{
					return true;
				}
			}
			return false;
		}

		public static bool InputControlHasFocus()
		{
			foreach (UIControl control in controls.Values)
			{
				if (control.Visible &&
					control.InputField != null &&
					control.InputField.isFocused)
				{
					return true;
				}
			}
			return false;
		}

		public static bool CloseNext()
		{
			if (closeOnEscapeControls != null)
			{
				// get the last opened or focused UI control
				UIControl control = closeOnEscapeControls.Pop();
				if (control != null)
				{
					control.Hide();
					return true;
				}
			}
			return false;
		}
	}
}