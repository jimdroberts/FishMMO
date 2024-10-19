using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FishMMO.Client
{
	public static class InputManager
	{
		public static void ResetForcedMouseMode()
		{
			ForcedMouseMode = false;
		}
		
		public static bool ForcedMouseMode { get; private set; }

		public static bool MouseMode
		{
			get
			{
				return Cursor.visible;
			}
			set
			{
				if (Cursor.visible == value)
				{
					return;
				}
				Cursor.visible = value;
				Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;

				if (!value && EventSystem.current != null)
				{
					if (!EventSystem.current.alreadySelecting)
					{
						EventSystem.current.SetSelectedGameObject(null);
						EventSystem.current.sendNavigationEvents = false;
					}
				}

#if UNITY_EDITOR
				if (!value)
				{
					ForceClickMouseButtonInCenterOfGameWindow();
				}
#endif

				OnToggleMouseMode?.Invoke(value);
			}
		}

		//<VirtualKey, KeyMap>
		private static Dictionary<string, KeyMap> virtualKeyMaps = new Dictionary<string, KeyMap>();
		//<CustomAxis, UnityAxis>
		private static Dictionary<string, string> axisMaps = new Dictionary<string, string>();

		public static event System.Action<bool> OnToggleMouseMode;

		public static void ForceClickMouseButtonInCenterOfGameWindow()
		{
#if UNITY_EDITOR
			var game = UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView"));
			Vector2 gameWindowCenter = game.rootVisualElement.contentRect.center;

			Event leftClickDown = new Event();
			leftClickDown.button = 0;
			leftClickDown.clickCount = 1;
			leftClickDown.type = EventType.MouseDown;
			leftClickDown.mousePosition = gameWindowCenter;

			game.SendEvent(leftClickDown);
#endif
		}

		static InputManager()
		{
			virtualKeyMaps.Clear();
			axisMaps.Clear();

			AddAxis("Vertical", "Vertical");
			AddAxis("Horizontal", "Horizontal");
			AddAxis("Mouse X", "Mouse X");
			AddAxis("Mouse Y", "Mouse Y");
			AddAxis("Mouse ScrollWheel", "Mouse ScrollWheel");
			AddKey("Left Mouse", KeyCode.Mouse0);
			AddKey("Right Mouse", KeyCode.Mouse1);
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
			AddKey("Mouse Mode", KeyCode.LeftAlt);
			AddKey("Interact", KeyCode.E);
			AddKey("Inventory", KeyCode.I);
			AddKey("Abilities", KeyCode.K);
			AddKey("Achievements", KeyCode.J);
			AddKey("Equipment", KeyCode.O);
			AddKey("Guild", KeyCode.G);
			AddKey("Party", KeyCode.P);
			AddKey("Friends", KeyCode.H);
			AddKey("Menu", KeyCode.F1);
			AddKey("ToggleFirstPerson", KeyCode.F9);
			AddKey("Cancel", KeyCode.Escape);
			AddKey("Close Last UI", KeyCode.Escape);
			AddKey("Chat", KeyCode.Return);
			AddKey("Chat2", KeyCode.KeypadEnter);
			MouseMode = true;

			//InputManager.LoadConfig();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ToggleMouseMode(bool forceMouseMode = false)
		{
			ForcedMouseMode = forceMouseMode;
			MouseMode = !MouseMode;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void AddKey(string virtualKey, KeyCode keyCode)
		{
			KeyMap newMap = new KeyMap(virtualKey, keyCode);
			virtualKeyMaps[virtualKey] = newMap;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static KeyCode GetKeyCode(string virtualKey)
		{
			if (virtualKeyMaps.TryGetValue(virtualKey, out KeyMap keyMap))
			{
				return keyMap.Key;
			}
			return KeyCode.None;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool GetKey(string virtualKey)
		{
			if (virtualKeyMaps.TryGetValue(virtualKey, out KeyMap keyMap))
			{
				return keyMap.GetKey();
			}
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool GetKeyDown(string virtualKey)
		{
			if (virtualKeyMaps.TryGetValue(virtualKey, out KeyMap keyMap))
			{
				return keyMap.GetKeyDown();
			}
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool GetKeyUp(string virtualKey)
		{
			if (virtualKeyMaps.TryGetValue(virtualKey, out KeyMap keyMap))
			{
				return keyMap.GetKeyUp();
			}
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void AddAxis(string virtualAxis, string unityAxis)
		{
			axisMaps[virtualAxis] = unityAxis;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float GetAxis(string virtualAxis)
		{
			if (axisMaps.TryGetValue(virtualAxis, out string unityAxis))
			{
				return Input.GetAxis(unityAxis);
			}
			return 0.0f;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float GetAxisRaw(string virtualAxis)
		{
			if (axisMaps.TryGetValue(virtualAxis, out string unityAxis))
			{
				return Input.GetAxisRaw(unityAxis);
			}
			return 0.0f;
		}
	}
}