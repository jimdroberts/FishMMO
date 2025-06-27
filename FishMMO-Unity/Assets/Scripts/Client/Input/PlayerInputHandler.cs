using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using FishMMO.Shared; // Assuming FishMMO.Shared contains Log and Configuration

namespace FishMMO.Client
{
	/// <summary>
	/// Manages game input using Unity's new Input System, providing a centralized
	/// point for controlling action maps, cursor state, and loading/saving bindings.
	/// This replaces the static InputManager.
	/// </summary>
	public class PlayerInputHandler : MonoBehaviour
	{
		// Reference to the generated Input Actions asset.
		private PlayerControls _playerControls;

		/// <summary>
		/// Gets a value indicating whether the mouse cursor's visibility is currently being "forced"
		/// by an explicit call to ToggleMouseMode(bool) with `forceMouseMode = true`.
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
			get => Cursor.visible;
			set
			{
				if (Cursor.visible == value)
				{
					return;
				}

				Cursor.visible = value;
				Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;

				// If mouse mode is being disabled (cursor hidden/locked), deselect any UI elements.
				// This prevents UI elements from retaining focus when the mouse is no longer interacting with the UI.
				if (!value && EventSystem.current != null)
				{
					if (!EventSystem.current.alreadySelecting)
					{
						EventSystem.current.SetSelectedGameObject(null);
						// In the new Input System, disabling EventSystem.current.sendNavigationEvents
						// is typically handled by disabling the UI Input Action Map.
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
				OnToggleMouseMode?.Invoke(value);
			}
		}

		/// <summary>
		/// An event that is invoked whenever the <see cref="MouseMode"/> property is toggled.
		/// Subscribers can listen to this event to react to changes in mouse visibility and lock state.
		/// The boolean parameter indicates the new state of <see cref="MouseMode"/> (true for visible/unlocked, false for hidden/locked).
		/// </summary>
		public static event System.Action<bool> OnToggleMouseMode;

		// Static reference to the active PlayerControls instance
		public static PlayerControls Controls { get; private set; }

		private void Awake()
		{
			// Ensure only one PlayerInputHandler exists (or handle this with a proper singleton pattern)
			if (Controls != null)
			{
				Destroy(gameObject);
				return;
			}

			_playerControls = new PlayerControls();
			Controls = _playerControls; // Assign to static property for global access

			// Load saved bindings when the application starts
			LoadBindingOverrides();

			// Initial mouse mode state
			MouseMode = true; // Start with mouse visible and unlocked

			// Subscribe to the ToggleMouseMode action
			_playerControls.Player.ToggleMouseMode.performed += ctx => ToggleMouseMode(true);
		}

		private void OnEnable()
		{
			_playerControls.Enable();
			// Ensure Player map is active initially. UI map will be enabled/disabled by EventSystem.
			_playerControls.Player.Enable();
			_playerControls.UI.Enable(); // UI map should generally always be enabled for UI interaction, EventSystem manages its behavior
		}

		private void OnDisable()
		{
			_playerControls.Disable();
		}

		private void OnDestroy()
		{
			_playerControls.Player.ToggleMouseMode.performed -= ctx => ToggleMouseMode(true);
			_playerControls?.Dispose(); // Dispose of the input actions when no longer needed
			Controls = null; // Clear static reference
		}

		/// <summary>
		/// Resets the <see cref="ForcedMouseMode"/> flag to false, allowing
		/// the mouse mode to be toggled again via user input or other means.
		/// </summary>
		public static void ResetForcedMouseMode()
		{
			ForcedMouseMode = false;
		}

		/// <summary>
		/// Toggles the <see cref="MouseMode"/> between visible/unlocked and hidden/locked.
		/// </summary>
		/// <param name="forceMouseMode">If true, this call will set <see cref="ForcedMouseMode"/> to true,
		/// indicating that the mouse mode has been explicitly controlled. If false, it will clear the force flag.
		/// </param>
		public static void ToggleMouseMode(bool forceMouseMode = false)
		{
			ForcedMouseMode = forceMouseMode;
			MouseMode = !MouseMode;

			// When mouse mode is disabled (locked), enable player input and disable UI input.
			// When mouse mode is enabled (unlocked), enable UI input and disable player input.
			if (MouseMode) // If mouse is visible/unlocked (UI mode)
			{
				Controls.Player.Disable();
				Controls.UI.Enable();
			}
			else // If mouse is hidden/locked (Game mode)
			{
				Controls.UI.Disable();
				Controls.Player.Enable();
			}
		}

#if UNITY_EDITOR
		/// <summary>
		/// In the Unity Editor, sends a simulated left mouse button click event to the center
		/// of the Game window. This can help ensure that the Game window gains focus and input
		/// is properly captured, especially after the mouse cursor is locked.
		/// This method has no effect in a built game.
		/// </summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static void ForceClickMouseButtonInCenterOfGameWindow()
		{
			var game = UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView"));
			if (game == null) return;

			Vector2 gameWindowCenter = game.rootVisualElement.contentRect.center;

			Event leftClickDown = new Event
			{
				button = 0, // Left mouse button
				clickCount = 1, // Single click
				type = EventType.MouseDown, // Mouse down event
				mousePosition = gameWindowCenter // Position the click at the center
			};

			game.SendEvent(leftClickDown);
		}
#endif

		/// <summary>
		/// Saves the current input binding overrides to Configuration.GlobalSettings.
		/// This allows user-remapped controls to persist.
		/// </summary>
		public static void SaveBindingOverrides()
		{
			if (Configuration.GlobalSettings != null && Controls != null)
			{
				Log.Debug("Attempting to save input binding overrides to Global Configuration.");
				// Serialize the binding overrides to a JSON string
				string overridesJson = Controls.SaveBindingOverridesAsJson();
				Configuration.GlobalSettings.Set("InputBindingOverrides", overridesJson);
				Debug.Log("Input binding overrides saved.");
			}
			else
			{
				Log.Warning("Global.Configuration or PlayerControls were not available during input override saving. Binding overrides not saved.");
			}
		}

		/// <summary>
		/// Loads input binding overrides from Configuration.GlobalSettings.
		/// This applies previously saved user-remapped controls.
		/// </summary>
		private void LoadBindingOverrides()
		{
			if (Configuration.GlobalSettings != null && Controls != null)
			{
				Log.Debug("Attempting to load input binding overrides from Global Configuration.");
				if (Configuration.GlobalSettings.TryGetString("InputBindingOverrides", out string overridesJson))
				{
					if (!string.IsNullOrEmpty(overridesJson))
					{
						// Apply the loaded JSON string to the input actions
						Controls.LoadBindingOverridesFromJson(overridesJson);
						Debug.Log("Input binding overrides loaded.");
					}
					else
					{
						Log.Warning("No input binding overrides found in configuration. Using default bindings.");
					}
				}
				else
				{
					Log.Warning("Input binding overrides key not found in configuration. Using default bindings.");
				}
			}
			else
			{
				Log.Warning("Global.Configuration or PlayerControls were not available during input override loading. Using default bindings.");
			}
		}

		// Example of how you might expose a binding remapping method
		// This would typically be called from a UI for remapping a specific action
		public static void RemapBinding(InputAction action, int bindingIndex, InputBinding newBinding)
		{
			// You would normally get the newBinding from an interactive rebind UI.
			// For example: action.PerformInteractiveRebinding().WithControlsExcluding("Mouse").Start();
			// Then in the callback of that interactive rebind, you apply the new binding.
			// This is a simplified example.
			action.ApplyBindingOverride(bindingIndex, newBinding);
			SaveBindingOverrides(); // Save after remapping
		}
	}
}