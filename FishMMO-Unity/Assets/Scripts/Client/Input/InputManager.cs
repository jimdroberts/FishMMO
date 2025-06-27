using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// A static utility class for managing game input, providing an abstraction layer
	/// over Unity's built-in Input and EventSystem. It allows for customizable
	/// virtual key and axis mappings, and controls mouse visibility and lock state.
	/// </summary>
	public static class InputManager
	{
		/// <summary>
		/// Resets the <see cref="ForcedMouseMode"/> flag to false, allowing
		/// the mouse mode to be toggled again via user input or other means.
		/// </summary>
		public static void ResetForcedMouseMode()
		{
			ForcedMouseMode = false;
		}

		/// <summary>
		/// Gets a value indicating whether the mouse cursor's visibility is currently being "forced"
		/// by an explicit call to <see cref="ToggleMouseMode(bool)"/> with `forceMouseMode = true`.
		/// When true, it suggests that the mouse mode might be locked in its current state,
		/// potentially overriding typical toggle behavior.
		/// </summary>
		public static bool ForcedMouseMode { get; private set; }

		/// <summary>
		/// Gets or sets the current mouse mode.
		/// When true, the cursor is visible and unlocked.
		/// When false, the cursor is hidden and locked to the center of the game window.
		/// Setting this property also manages UI interaction based on the mouse mode.
		/// </summary>
		public static bool MouseMode
		{
			get
			{
				// The cursor's visibility directly reflects the mouse mode state.
				return Cursor.visible;
			}
			set
			{
				// Only apply changes if the desired state is different from the current state to avoid unnecessary operations.
				if (Cursor.visible == value)
				{
					return;
				}

				// Set cursor visibility and lock state based on the new mouse mode.
				Cursor.visible = value;
				Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;

				// If mouse mode is being disabled (cursor hidden/locked), deselect any UI elements.
				// This prevents UI elements from retaining focus when the mouse is no longer interacting with the UI.
				if (!value && EventSystem.current != null)
				{
					// Only clear selection if EventSystem is not currently processing a selection,
					// to prevent interrupting its internal state.
					if (!EventSystem.current.alreadySelecting)
					{
						EventSystem.current.SetSelectedGameObject(null); // Clear selected UI element.
						EventSystem.current.sendNavigationEvents = false; // Disable UI navigation events.
					}
				}

#if UNITY_EDITOR
				// In the Unity Editor, when mouse mode is disabled, force a mouse click in the center
				// of the Game window. This is often a workaround to ensure the Game window captures
				// input focus when the cursor is locked.
				if (!value)
				{
					ForceClickMouseButtonInCenterOfGameWindow();
				}
#endif

				// Invoke the event to notify any subscribers that the mouse mode has been toggled.
				OnToggleMouseMode?.Invoke(value);
			}
		}

		/// <summary>
		/// A dictionary mapping custom virtual key names (e.g., "Jump") to their corresponding
		/// <see cref="KeyMap"/> objects, which encapsulate the actual <see cref="KeyCode"/>.
		/// This allows for flexible key rebinding.
		/// </summary>
		private static readonly Dictionary<string, KeyMap> virtualKeyMaps = new Dictionary<string, KeyMap>();

		/// <summary>
		/// A dictionary mapping custom virtual axis names (e.g., "Vertical") to their corresponding
		/// Unity Input Manager axis names (e.g., "Vertical").
		/// This allows for flexible axis rebinding.
		/// </summary>
		private static readonly Dictionary<string, string> axisMaps = new Dictionary<string, string>();

		/// <summary>
		/// An event that is invoked whenever the <see cref="MouseMode"/> property is toggled.
		/// Subscribers can listen to this event to react to changes in mouse visibility and lock state.
		/// The boolean parameter indicates the new state of <see cref="MouseMode"/> (true for visible/unlocked, false for hidden/locked).
		/// </summary>
		public static event System.Action<bool> OnToggleMouseMode;

		/// <summary>
		/// In the Unity Editor, sends a simulated left mouse button click event to the center
		/// of the Game window. This can help ensure that the Game window gains focus and input
		/// is properly captured, especially after the mouse cursor is locked.
		/// This method has no effect in a built game.
		/// </summary>
		[MethodImpl(MethodImplOptions.NoInlining)] // NoInlining as this is an editor-only utility, not performance critical for runtime.
		public static void ForceClickMouseButtonInCenterOfGameWindow()
		{
#if UNITY_EDITOR
			// Get the GameView window instance.
			var game = UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView"));

			// Calculate the center point of the Game window's content area.
			Vector2 gameWindowCenter = game.rootVisualElement.contentRect.center;

			// Create a new synthetic Event for a left mouse button down click.
			Event leftClickDown = new Event();
			leftClickDown.button = 0; // Left mouse button
			leftClickDown.clickCount = 1; // Single click
			leftClickDown.type = EventType.MouseDown; // Mouse down event
			leftClickDown.mousePosition = gameWindowCenter; // Position the click at the center

			// Send the event to the GameView window.
			game.SendEvent(leftClickDown);
#endif
		}

		/// <summary>
		/// The static constructor for the <see cref="InputManager"/> class.
		/// This code runs exactly once when the class is first accessed.
		/// It initializes default virtual key and axis mappings and sets the initial mouse mode.
		/// </summary>
		static InputManager()
		{
			// --- Default Axis Mappings ---
			AddAxis("Vertical", "Vertical"); // Maps virtual "Vertical" axis to Unity's "Vertical" axis.
			AddAxis("Horizontal", "Horizontal"); // Maps virtual "Horizontal" axis to Unity's "Horizontal" axis.
			AddAxis("Mouse X", "Mouse X"); // Maps virtual "Mouse X" to Unity's "Mouse X".
			AddAxis("Mouse Y", "Mouse Y"); // Maps virtual "Mouse Y" to Unity's "Mouse Y".
			AddAxis("Mouse ScrollWheel", "Mouse ScrollWheel"); // Maps virtual "Mouse ScrollWheel" to Unity's "Mouse ScrollWheel".

			// --- Default Key Mappings ---
			// Mouse Buttons
			AddKey("Left Mouse", KeyCode.Mouse0);
			AddKey("Right Mouse", KeyCode.Mouse1);

			// Hotkeys (typically for abilities, items, etc.)
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

			// Movement and Action Keys
			AddKey("Run", KeyCode.LeftShift);
			AddKey("Jump", KeyCode.Space);
			AddKey("Crouch", KeyCode.C);
			AddKey("Mouse Mode", KeyCode.LeftAlt); // Key to toggle mouse visibility/lock.
			AddKey("Interact", KeyCode.E);

			// UI/Menu Keys
			AddKey("Inventory", KeyCode.I);
			AddKey("Abilities", KeyCode.J);
			AddKey("Achievements", KeyCode.K);
			AddKey("Equipment", KeyCode.O);
			AddKey("Factions", KeyCode.L);
			AddKey("Guild", KeyCode.G);
			AddKey("Party", KeyCode.P);
			AddKey("Friends", KeyCode.H);
			AddKey("Minimap", KeyCode.M);
			AddKey("Menu", KeyCode.F1);
			AddKey("ToggleFirstPerson", KeyCode.F9);
			AddKey("Cancel", KeyCode.Tab); // Often used for cancelling actions or closing active UI.
			AddKey("Close Last UI", KeyCode.Escape); // General escape key behavior.

			// Chat Keys
			AddKey("Chat", KeyCode.Return); // Enter key on main keyboard.
			AddKey("Chat2", KeyCode.KeypadEnter); // Enter key on numeric keypad.

			// Set initial mouse mode to visible and unlocked.
			MouseMode = true;

			LoadConfiguration();
		}

		/// <summary>
		/// Returns an enumerable collection of all registered virtual key names.
		/// Useful for populating UI lists or validating input.
		/// </summary>
		public static IEnumerable<string> GetVirtualKeyNames()
		{
			return virtualKeyMaps.Keys;
		}

		/// <summary>
		/// Returns an enumerable collection of all registered virtual axis names.
		/// </summary>
		public static IEnumerable<string> GetVirtualAxisNames()
		{
			return axisMaps.Keys;
		}

		private static void LoadConfiguration()
		{
			// Apply Saved Overrides from Global Configuration
			if (Configuration.GlobalSettings != null)
			{
				Log.Debug("Attempting to load input overrides from Global Configuration.");

				// Key Mapping Overrides
				foreach (var virtualKey in new List<string>(virtualKeyMaps.Keys))
				{
					// Directly try to get the KeyCode from Global Configuration
					if (Configuration.GlobalSettings.TryGetEnum(virtualKey, out KeyCode savedKeyCode))
					{
						// Apply the override using AddKey (which updates virtualKeyMaps)
						AddKey(virtualKey, savedKeyCode);
						Debug.Log($"Overrode '{virtualKey}' to '{savedKeyCode}' from config.");
					}
				}

				// Axis Mapping Overrides
				foreach (var virtualAxis in new List<string>(axisMaps.Keys))
				{
					// Directly try to get the string from Global.Configuration.Input.AxisMaps
					if (Configuration.GlobalSettings.TryGetString(virtualAxis, out string savedUnityAxis))
					{
						// Apply the override using AddAxis (which updates axisMaps)
						AddAxis(virtualAxis, savedUnityAxis);
						Debug.Log($"Overrode '{virtualAxis}' to '{savedUnityAxis}' from config.");
					}
				}
			}
			else
			{
				Log.Warning("Global.Configuration or its Input settings were not available during InputManager initialization. Using default input settings only.");
			}
		}

		private static void SaveConfiguration()
		{
			// Apply Saved Overrides from Global Configuration
			if (Configuration.GlobalSettings != null)
			{
				Log.Debug("Attempting to save input to Global Configuration.");

				// Key Mapping Overrides
				foreach (var virtualKey in virtualKeyMaps)
				{
					// Set the KeyMap in Global Configuration
					Configuration.GlobalSettings.Set(virtualKey.Key, virtualKey.Value);
				}

				// Axis Mapping Overrides
				foreach (var virtualAxis in axisMaps)
				{
					// Set the KeyMap Axis in Global Configuration
					Configuration.GlobalSettings.Set(virtualAxis.Key, virtualAxis.Value);
				}
			}
		}

		/// <summary>
		/// Toggles the <see cref="MouseMode"/> between visible/unlocked and hidden/locked.
		/// </summary>
		/// <param name="forceMouseMode">If true, this call will set <see cref="ForcedMouseMode"/> to true,
		/// indicating that the mouse mode has been explicitly controlled. If false, it will clear the force flag.
		/// Note: The current implementation of <see cref="ForcedMouseMode"/> acts as a status flag, not a prevention mechanism for toggling.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)] // Hint to the compiler to inline this method for performance.
		public static void ToggleMouseMode(bool forceMouseMode = false)
		{
			ForcedMouseMode = forceMouseMode; // Update the force status flag.
			MouseMode = !MouseMode; // Toggle the actual mouse mode.
		}

		/// <summary>
		/// Adds or updates a virtual key mapping.
		/// </summary>
		/// <param name="virtualKey">The custom string name for the virtual key (e.g., "Jump").</param>
		/// <param name="keyCode">The Unity <see cref="KeyCode"/> to which the virtual key is mapped.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void AddKey(string virtualKey, KeyCode keyCode)
		{
			// Create a new KeyMap object for the given virtual key and keyCode.
			KeyMap newMap = new KeyMap(virtualKey, keyCode);
			// Store or update the KeyMap in the dictionary.
			virtualKeyMaps[virtualKey] = newMap;
		}

		/// <summary>
		/// Retrieves the <see cref="KeyCode"/> currently mapped to a given virtual key.
		/// </summary>
		/// <param name="virtualKey">The custom string name of the virtual key.</param>
		/// <returns>The mapped <see cref="KeyCode"/> if found; otherwise, <see cref="KeyCode.None"/>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static KeyCode GetKeyCode(string virtualKey)
		{
			// Attempt to get the KeyMap associated with the virtual key.
			if (virtualKeyMaps.TryGetValue(virtualKey, out KeyMap keyMap))
			{
				// Return the actual KeyCode from the found KeyMap.
				return keyMap.Key;
			}
			// If the virtual key is not mapped, return KeyCode.None.
			return KeyCode.None;
		}

		/// <summary>
		/// Checks if a virtual key is currently being held down.
		/// This is equivalent to <see cref="Input.GetKey(KeyCode)"/>.
		/// </summary>
		/// <param name="virtualKey">The custom string name of the virtual key.</param>
		/// <returns>True if the mapped key is held down; otherwise, false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool GetKey(string virtualKey)
		{
			if (virtualKeyMaps.TryGetValue(virtualKey, out KeyMap keyMap))
			{
				return keyMap.GetKey();
			}
			return false;
		}

		/// <summary>
		/// Checks if a virtual key was pressed down in the current frame.
		/// This is equivalent to <see cref="Input.GetKeyDown(KeyCode)"/>.
		/// </summary>
		/// <param name="virtualKey">The custom string name of the virtual key.</param>
		/// <returns>True if the mapped key was pressed in the current frame; otherwise, false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool GetKeyDown(string virtualKey)
		{
			if (virtualKeyMaps.TryGetValue(virtualKey, out KeyMap keyMap))
			{
				return keyMap.GetKeyDown();
			}
			return false;
		}

		/// <summary>
		/// Checks if a virtual key was released in the current frame.
		/// This is equivalent to <see cref="Input.GetKeyUp(KeyCode)"/>.
		/// </summary>
		/// <param name="virtualKey">The custom string name of the virtual key.</param>
		/// <returns>True if the mapped key was released in the current frame; otherwise, false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool GetKeyUp(string virtualKey)
		{
			if (virtualKeyMaps.TryGetValue(virtualKey, out KeyMap keyMap))
			{
				return keyMap.GetKeyUp();
			}
			return false;
		}

		/// <summary>
		/// Adds or updates a virtual axis mapping.
		/// </summary>
		/// <param name="virtualAxis">The custom string name for the virtual axis (e.g., "Vertical").</param>
		/// <param name="unityAxis">The name of the corresponding Unity Input Manager axis (e.g., "Vertical").</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void AddAxis(string virtualAxis, string unityAxis)
		{
			// Store or update the Unity axis name in the dictionary.
			axisMaps[virtualAxis] = unityAxis;
		}

		/// <summary>
		/// Retrieves the value of a virtual axis.
		/// This is equivalent to <see cref="Input.GetAxis(string)"/>, providing smoothed input.
		/// </summary>
		/// <param name="virtualAxis">The custom string name of the virtual axis.</param>
		/// <returns>The axis value (typically between -1.0 and 1.0) if the axis is mapped; otherwise, 0.0f.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float GetAxis(string virtualAxis)
		{
			if (axisMaps.TryGetValue(virtualAxis, out string unityAxis))
			{
				return Input.GetAxis(unityAxis);
			}
			return 0.0f;
		}

		/// <summary>
		/// Retrieves the raw value of a virtual axis (without smoothing).
		/// This is equivalent to <see cref="Input.GetAxisRaw(string)"/>.
		/// </summary>
		/// <param name="virtualAxis">The custom string name of the virtual axis.</param>
		/// <returns>The raw axis value (typically -1, 0, or 1) if the axis is mapped; otherwise, 0.0f.</returns>
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