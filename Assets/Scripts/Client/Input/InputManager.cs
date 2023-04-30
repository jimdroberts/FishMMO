﻿using System.Collections.Generic;
using UnityEngine;

namespace Client
{
	public static class InputManager
	{
		public static bool MouseMode
		{
			get { return Cursor.visible; }
			set
			{
				Cursor.visible = value;
				Cursor.lockState = (value) ? CursorLockMode.None : CursorLockMode.Locked;
			}
		}

		//<VirtualKey, KeyMap>
		private static Dictionary<string, KeyMap> virtualKeyMaps = new Dictionary<string, KeyMap>();
		//<CustomAxis, UnityAxis>
		private static Dictionary<string, string> axisMaps = new Dictionary<string, string>();

		static InputManager()
		{
			virtualKeyMaps.Clear();
			axisMaps.Clear();

			AddAxis("Vertical", "Vertical");
			AddAxis("Horizontal", "Horizontal");
			AddAxis("Mouse X", "Mouse X");
			AddAxis("Mouse Y", "Mouse Y");
			AddKey("Hotkey 1", KeyCode.Alpha1);
			AddKey("Hotkey 2", KeyCode.Alpha2);
			AddKey("Hotkey 3", KeyCode.Alpha3);
			AddKey("Hotkey 4", KeyCode.Alpha4);
			AddKey("Hotkey 5", KeyCode.Alpha5);
			AddKey("Hotkey 6", KeyCode.Alpha6);
			AddKey("Hotkey 7", KeyCode.Alpha7);
			AddKey("Hotkey 8", KeyCode.Alpha8);
			AddKey("Hotkey 9", KeyCode.Alpha9);
			AddKey("Hotkey 0", KeyCode.Alpha0);
			AddKey("Run", KeyCode.LeftShift);
			AddKey("Jump", KeyCode.Space);
			AddKey("Crouch", KeyCode.C);
			AddKey("Mouse Mode", KeyCode.M);
			AddKey("Interact", KeyCode.E);
			AddKey("Inventory", KeyCode.I);
			AddKey("Abilities", KeyCode.K);
			AddKey("Equipment", KeyCode.O);
			AddKey("Party", KeyCode.P);
			AddKey("Menu", KeyCode.Escape);
			AddKey("ToggleFirstPerson", KeyCode.F9);
			MouseMode = true;

			//InputManager.LoadConfig();
		}

		public static void ToggleMouseMode()
		{
			MouseMode = !MouseMode;
		}

		public static void AddKey(string virtualKey, KeyCode keyCode)
		{
			KeyMap newMap = new KeyMap(virtualKey, keyCode);
			if (virtualKeyMaps.ContainsKey(virtualKey))
			{
				virtualKeyMaps[virtualKey] = newMap;
				return;
			}
			//Debug.Log("KeyMapManager: New key " + virtualKey +"->"+ keyCode);
			virtualKeyMaps.Add(virtualKey, newMap);
		}

		public static KeyCode GetKeyCode(string virtualKey)
		{
			KeyMap keyMap;
			if (!virtualKeyMaps.TryGetValue(virtualKey, out keyMap))
			{
				return KeyCode.None;
			}
			return keyMap.key;
		}

		public static bool GetKey(string virtualKey)
		{
			KeyMap keyMap;
			if (!virtualKeyMaps.TryGetValue(virtualKey, out keyMap))
			{
				return false;
			}
			return keyMap.GetKey();
		}

		public static bool GetKeyDown(string virtualKey)
		{
			KeyMap keyMap;
			if (!virtualKeyMaps.TryGetValue(virtualKey, out keyMap))
			{
				return false;
			}
			return keyMap.GetKeyDown();
		}

		public static bool GetKeyUp(string virtualKey)
		{
			KeyMap keyMap;
			if (!virtualKeyMaps.TryGetValue(virtualKey, out keyMap))
			{
				return false;
			}
			return keyMap.GetKeyUp();
		}

		public static void AddAxis(string virtualAxis, string unityAxis)
		{
			if (axisMaps.ContainsKey(virtualAxis))
			{
				axisMaps[virtualAxis] = unityAxis;
				return;
			}
			//Debug.Log("KeyMapManager: New axis " + virtualAxis + "->" + unityAxis);
			axisMaps.Add(virtualAxis, unityAxis);
		}

		public static float GetAxis(string virtualAxis)
		{
			string unityAxis;
			if (!axisMaps.TryGetValue(virtualAxis, out unityAxis))
			{
				return 0.0f;
			}
			return Input.GetAxis(unityAxis);
		}

		public static float GetAxisRaw(string virtualAxis)
		{
			string unityAxis;
			if (!axisMaps.TryGetValue(virtualAxis, out unityAxis))
			{
				return 0.0f;
			}
			return Input.GetAxisRaw(unityAxis);
		}
	}
}