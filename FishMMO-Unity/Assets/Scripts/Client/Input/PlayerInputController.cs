using FishNet.Transporting;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using KinematicCharacterController;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;

namespace FishMMO.Client
{
	/// <summary>
	/// Handles player input using the new Unity Input System and converts it to character actions.
	/// Subscribes to input action callbacks and manages UI toggles, movement, camera, and context menus.
	/// </summary>
	public class PlayerInputController : MonoBehaviour
	{
		// ── Static mouse-mode & global input state ─────────────────────────

		/// <summary>
		/// True when the mouse mode was explicitly set by a UI element
		/// (prevents auto-dismiss from overriding a deliberate cursor show).
		/// </summary>
		public static bool ForcedMouseMode { get; private set; }

		/// <summary>
		/// Gets or sets the current mouse mode. True = cursor visible/unlocked.
		/// False = cursor hidden/locked to the game window.
		/// </summary>
		public static bool MouseMode
		{
			get => Cursor.visible;
			set
			{
				if (Cursor.visible == value) return;
				Cursor.visible = value;
				Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;

				if (!value && UnityEngine.EventSystems.EventSystem.current != null &&
					!UnityEngine.EventSystems.EventSystem.current.alreadySelecting)
				{
					UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
				}
#if UNITY_EDITOR
				if (!value) ForceClickMouseButtonInCenterOfGameWindow();
#endif
				OnToggleMouseMode?.Invoke(value);
			}
		}

		/// <summary>Invoked when MouseMode changes.</summary>
		public static event Action<bool> OnToggleMouseMode;

		/// <summary>Shared PlayerControls for read-only queries (hotkey bar, chat).</summary>
		public static PlayerControls Controls { get; private set; }

		/// <summary>Ensures the static Controls instance is created.</summary>
		public static void InitializeControls()
		{
			if (Controls != null) return;
			Controls = new PlayerControls();
			Controls.Enable();
			Controls.Player.Enable();
			Controls.UI.Enable();
		}

		/// <summary>Clears the forced-mouse-mode flag.</summary>
		public static void ResetForcedMouseMode() => ForcedMouseMode = false;

		/// <summary>Toggles mouse mode. Pass true to force the new state.</summary>
		public static void ToggleMouseMode(bool forceMouseMode = false)
		{
			ForcedMouseMode = forceMouseMode;
			MouseMode = !MouseMode;
		}

		/// <summary>Persists keybinding overrides to global configuration.</summary>
		public static void SaveBindingOverrides()
		{
			if (Configuration.GlobalSettings != null && Controls != null)
				Configuration.GlobalSettings.Set("InputBindingOverrides", Controls.SaveBindingOverridesAsJson());
		}

#if UNITY_EDITOR
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		public static void ForceClickMouseButtonInCenterOfGameWindow()
		{
			var game = UnityEditor.EditorWindow.GetWindow(
				typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView"));
			if (game == null) return;
			Vector2 center = game.rootVisualElement.contentRect.center;
			game.SendEvent(new Event { button = 0, clickCount = 1,
				type = EventType.MouseDown, mousePosition = center });
		}
#endif

		// ── Instance state ─────────────────────────────────────────────────

		/// <summary>
		/// The player character associated with this input controller.
		/// </summary>
		public IPlayerCharacter Character { get; private set; }

		/// <summary>
		/// Indicates if a jump input has been queued for processing.
		/// </summary>
		private bool jumpQueued = false;
		/// <summary>
		/// Indicates if crouch input is currently active.
		/// </summary>
		private bool crouchInputActive = false;
		/// <summary>
		/// Indicates if sprint input is currently active.
		/// </summary>
		private bool sprintInputActive = false;

		/// <summary>
		/// Current movement input vector from the Input System.
		/// </summary>
		private Vector2 moveInput;
		/// <summary>
		/// Current look input vector from the Input System.
		/// </summary>
		private Vector2 lookInput;
		/// <summary>
		/// Current mouse scroll input value (y component).
		/// </summary>
		private float mouseScrollInput;

		/// <summary>
		/// Initializes the input controller for the specified player character.
		/// Subscribes to input events and character input handling.
		/// </summary>
		/// <param name="character">The player character to control.</param>
		public void Initialize(IPlayerCharacter character)
		{
			Character = character;

			if (Character == null)
			{
				return;
			}

			if (Character.KCCPlayer != null)
			{
				Character.KCCPlayer.OnHandleCharacterInput += KCCPlayer_OnHandleCharacterInput;
			}

			// Ensure the shared PlayerControls exists and load saved keybinds.
			InitializeControls();
			LoadBindingOverrides();

			// Start with cursor visible (login/loading state).
			MouseMode = true;

			SubscribeToInputActions();
		}

		/// <summary>
		/// Deinitializes the input controller, unsubscribing from input events and character input handling.
		/// </summary>
		public void Deinitialize()
		{
			if (Character == null)
			{
				return;
			}

			if (Character.KCCPlayer != null)
			{
				Character.KCCPlayer.OnHandleCharacterInput -= KCCPlayer_OnHandleCharacterInput;
			}

			UnsubscribeFromInputActions();
		}

		/// <summary>
		/// Subscribes to all relevant input actions from this character's
		/// PlayerControls instance. Called once from Initialize().
		/// </summary>
		private void SubscribeToInputActions()
		{
			if (Controls == null) return;

			// Player continuous input
			Controls.Player.Move.performed += OnMovePerformed;
			Controls.Player.Move.canceled += OnMoveCanceled;

			Controls.Player.Look.performed += OnLookPerformed;
			Controls.Player.Look.canceled += OnLookCanceled;

			Controls.UI.ScrollWheel.performed += OnScrollWheelPerformed;
			Controls.UI.ScrollWheel.canceled += OnScrollWheelCanceled;

			Controls.Player.Jump.performed += OnJumpPerformed;
			Controls.Player.Crouch.performed += OnCrouchPerformed;
			Controls.Player.Crouch.canceled += OnCrouchCanceled;
			Controls.Player.Sprint.performed += OnSprintPerformed;
			Controls.Player.Sprint.canceled += OnSprintCanceled;

			// Player action callbacks
			Controls.Player.Interact.performed += OnInteractPerformed;
			Controls.Player.ToggleFirstPerson.performed += OnToggleFirstPersonPerformed;
			Controls.Player.ToggleMouseMode.performed += OnToggleMouseModePerformed;
			Controls.Player.Cancel.performed += OnCancelPerformed;
			Controls.Player.CloseLastUI.performed += OnCloseLastUIPerformed;
			Controls.Player.Chat.performed += OnChatPerformed;

			// UI/Menu toggles
			Controls.Player.Inventory.performed += OnInventoryPerformed;
			Controls.Player.Abilities.performed += OnAbilitiesPerformed;
			Controls.Player.Equipment.performed += OnEquipmentPerformed;
			Controls.Player.Guild.performed += OnGuildPerformed;
			Controls.Player.Party.performed += OnPartyPerformed;
			Controls.Player.Friends.performed += OnFriendsPerformed;
			Controls.Player.Achievements.performed += OnAchievementsPerformed;
			Controls.Player.Factions.performed += OnFactionsPerformed;
			Controls.Player.Minimap.performed += OnMinimapPerformed;
			Controls.Player.Menu.performed += OnMenuPerformed;
		}

		/// <summary>
		/// Unsubscribes from all input actions to prevent memory leaks and unwanted input processing.
		/// </summary>
		private void UnsubscribeFromInputActions()
		{
			if (Controls == null) return;

			// Player continuous input
			Controls.Player.Move.performed -= OnMovePerformed;
			Controls.Player.Move.canceled -= OnMoveCanceled;

			Controls.Player.Look.performed -= OnLookPerformed;
			Controls.Player.Look.canceled -= OnLookCanceled;

			Controls.UI.ScrollWheel.performed -= OnScrollWheelPerformed;
			Controls.UI.ScrollWheel.canceled -= OnScrollWheelCanceled;

			Controls.Player.Jump.performed -= OnJumpPerformed;
			Controls.Player.Crouch.performed -= OnCrouchPerformed;
			Controls.Player.Crouch.canceled -= OnCrouchCanceled;
			Controls.Player.Sprint.performed -= OnSprintPerformed;
			Controls.Player.Sprint.canceled -= OnSprintCanceled;

			// Player action callbacks
			Controls.Player.Interact.performed -= OnInteractPerformed;
			Controls.Player.ToggleFirstPerson.performed -= OnToggleFirstPersonPerformed;
			Controls.Player.ToggleMouseMode.performed -= OnToggleMouseModePerformed;
			Controls.Player.Cancel.performed -= OnCancelPerformed;
			Controls.Player.CloseLastUI.performed -= OnCloseLastUIPerformed;
			Controls.Player.Chat.performed -= OnChatPerformed;

			// UI/Menu toggles
			Controls.Player.Inventory.performed -= OnInventoryPerformed;
			Controls.Player.Abilities.performed -= OnAbilitiesPerformed;
			Controls.Player.Equipment.performed -= OnEquipmentPerformed;
			Controls.Player.Guild.performed -= OnGuildPerformed;
			Controls.Player.Party.performed -= OnPartyPerformed;
			Controls.Player.Friends.performed -= OnFriendsPerformed;
			Controls.Player.Achievements.performed -= OnAchievementsPerformed;
			Controls.Player.Factions.performed -= OnFactionsPerformed;
			Controls.Player.Minimap.performed -= OnMinimapPerformed;
			Controls.Player.Menu.performed -= OnMenuPerformed;
		}

		/// <summary>
		/// Unity event called when the object becomes enabled and active.
		/// Shows key UI elements for the player.
		/// </summary>
		private void OnEnable()
		{
			UIManager.Show("UIHealthBar");
			UIManager.Show("UIManaBar");
			UIManager.Show("UIStaminaBar");
			UIManager.Show("UIHotkeyBar");
			UIManager.Show("UIChat");
			UIManager.Show("UIBuff");
			UIManager.Show("UIDebuff");
			UIManager.Show("UIMinimap");
		}

		/// <summary>
		/// Unity event called when the object becomes disabled or inactive.
		/// Hides key UI elements for the player.
		/// </summary>
		private void OnDisable()
		{
			UIManager.Hide("UIHealthBar");
			UIManager.Hide("UIManaBar");
			UIManager.Hide("UIStaminaBar");
			UIManager.Hide("UIHotkeyBar");
			UIManager.Hide("UIChat");
			UIManager.Hide("UIBuff");
			UIManager.Hide("UIDebuff");
			UIManager.Hide("UIMinimap");
		}

		/// <summary>
		/// Cleans up input subscriptions on the static Controls to prevent
		/// leaks when the GameObject is destroyed without Deinitialize().
		/// </summary>
		private void OnDestroy()
		{
			if (Character != null && Character.KCCPlayer != null)
				Character.KCCPlayer.OnHandleCharacterInput -= KCCPlayer_OnHandleCharacterInput;
			UnsubscribeFromInputActions();
		}

		/// <summary>
		/// Determines if input should be processed for the player character.
		/// Input is only processed if the character is alive, mouse mode is off, and no UI input field has focus.
		/// </summary>
		/// <returns>True if input can be processed; otherwise, false.</returns>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		private bool CanUpdateInput()
		{
			if (Character.TryGet(out ICharacterDamageController damageController))
			{
				if (!damageController.IsAlive)
				{
					return false;
				}
			}
			return !PlayerInputController.MouseMode && !UIManager.InputControlHasFocus();
		}

		/// <summary>
		/// Handles character input for KinematicCharacterController.
		/// Converts input states into KCCInputReplicateData for movement replication.
		/// </summary>
		/// <returns>KCCInputReplicateData containing movement and camera input.</returns>
		public KCCInputReplicateData KCCPlayer_OnHandleCharacterInput()
		{
			int moveFlags = 0;
			moveFlags.EnableBit(KCCMoveFlags.IsActualData);

			if (!CanUpdateInput())
			{
				jumpQueued = false;
				crouchInputActive = false;
				sprintInputActive = false;
				moveInput = Vector2.zero;
				lookInput = Vector2.zero;

				return new KCCInputReplicateData(0.0f,
												 0.0f,
												 moveFlags,
												 Character.KCCPlayer.CharacterCamera.Transform.position,
												 Character.KCCPlayer.CharacterCamera.Transform.rotation);
			}

			if (jumpQueued)
			{
				moveFlags.EnableBit(KCCMoveFlags.Jump);
				jumpQueued = false;
			}
			if (crouchInputActive)
			{
				moveFlags.EnableBit(KCCMoveFlags.Crouch);
			}
			if (sprintInputActive)
			{
				moveFlags.EnableBit(KCCMoveFlags.Sprint);
			}

			return new KCCInputReplicateData(moveInput.y,
											 moveInput.x,
											 moveFlags,
											 Character.KCCPlayer.CharacterCamera.Transform.position,
											 Character.KCCPlayer.CharacterCamera.Transform.rotation);
		}

		/// <summary>
		/// Unity event called every frame. Handles right-click context menu and auto-dismiss logic.
		/// </summary>
		private void Update()
		{
			if (Character == null)
			{
				return;
			}

			HandleRightClickContextMenu();
			HandleAutoDismiss();
		}

		/// <summary>
		/// Unity event called every frame after all Update functions have been called.
		/// Handles camera input for the player character.
		/// </summary>
		private void LateUpdate()
		{
			if (Character.KCCPlayer.CharacterCamera == null)
			{
				return;
			}

			HandleCameraInput();
		}

		/// <summary>
		/// Processes camera input, including rotation and zoom, based on player input and physics movers.
		/// </summary>
		private void HandleCameraInput()
		{
			if (Character.Motor != null && Character.KCCPlayer.CharacterCamera.RotateWithPhysicsMover && Character.Motor.AttachedRigidbody != null)
			{
				PhysicsMover mover = Character.Motor.AttachedRigidbody.GetComponent<PhysicsMover>();
				if (mover != null)
				{
					Character.KCCPlayer.CharacterCamera.PlanarDirection = mover.RotationDeltaFromInterpolation * Character.KCCPlayer.CharacterCamera.PlanarDirection;
					Character.KCCPlayer.CharacterCamera.PlanarDirection = Vector3.ProjectOnPlane(Character.KCCPlayer.CharacterCamera.PlanarDirection, Character.Motor.CharacterUp).normalized;
				}
			}

			if (CanUpdateInput())
			{
				Vector3 lookInputVector = new Vector3(lookInput.x, lookInput.y, 0f);

				Character.KCCPlayer.UpdateCamera(-mouseScrollInput, lookInputVector);
				Character.KCCPlayer.SetOrientationMethod(Character.KCCPlayer.CharacterController.OrientationMethod);
			}
			else
			{
				lookInput = Vector2.zero;
				mouseScrollInput = 0f;
				Character.KCCPlayer.UpdateCamera(0.0f, Vector3.zero);
			}
		}

		/// <summary>
		/// Automatically dismisses mouse mode when forced mouse mode is inactive and no UI panels remain open.
		/// </summary>
		private void HandleAutoDismiss()
		{
			/* Asks whether any visible panel needs the cursor, rather than whether anything is
			 * Escape-closable. Those are different questions, and conflating them meant a panel
			 * could only keep the cursor by also agreeing to be closed with Escape. */
			if (!PlayerInputController.ForcedMouseMode && !UIManager.AnyCursorReleasingVisible())
			{
				if (PlayerInputController.MouseMode)
				{
					PlayerInputController.ToggleMouseMode();
				}
			}
		}

		/// <summary>
		/// Checks for right-click input while in MouseMode. If the current target is another player character
		/// within range, opens a context menu with interaction options.
		/// </summary>
		private void HandleRightClickContextMenu()
		{
			if (!PlayerInputController.MouseMode)
			{
				return;
			}

			Mouse mouse = Mouse.current;
			if (mouse == null || !mouse.rightButton.wasPressedThisFrame)
			{
				return;
			}

			if (!Character.TryGet(out ITargetController targetController))
			{
				return;
			}

			Transform target = targetController.Current.Target;
			if (target == null)
			{
				return;
			}

			IPlayerCharacter targetPlayer = target.GetComponent<IPlayerCharacter>();
			if (targetPlayer == null || targetPlayer.NetworkObject.IsOwner)
			{
				return;
			}

			long targetCharacterID = targetPlayer.ID;

			if (!UIManager.TryGetTK("UIContextMenu", out UITKContextMenu contextMenu))
			{
				return;
			}

			IPlayerCharacter capturedTarget = targetPlayer;

			var entries = new List<(string label, Action callback)>();
			entries.Add(("Inspect", new Action(() =>
			{
				if (UIManager.TryGetTK("UIInspect", out UITKInspect uiInspect))
				{
					uiInspect.Inspect(capturedTarget);
				}
			})));
			entries.Add(("Add Friend", new Action(() =>
			{
				Client.Broadcast(new FriendAddNewBroadcast()
				{
					CharacterID = targetCharacterID,
				}, Channel.Reliable);
			})));
			entries.Add(("Invite to Party", new Action(() =>
			{
				Client.Broadcast(new PartyInviteBroadcast()
				{
					InviterCharacterID = Character.ID,
					TargetCharacterID = targetCharacterID,
				}, Channel.Reliable);
			})));
			entries.Add(("Trade", new Action(() =>
			{
				Log.Debug("PlayerInputController", "Trade is not yet implemented.");
			})));

			contextMenu.Open(entries);
		}

		// --- Continuous Input Callbacks ---

		private void OnMovePerformed(InputAction.CallbackContext context)
		{
			moveInput = context.ReadValue<Vector2>();
		}

		private void OnMoveCanceled(InputAction.CallbackContext context)
		{
			moveInput = Vector2.zero;
		}

		private void OnLookPerformed(InputAction.CallbackContext context)
		{
			lookInput = context.ReadValue<Vector2>();
		}

		private void OnLookCanceled(InputAction.CallbackContext context)
		{
			lookInput = Vector2.zero;
		}

		private void OnScrollWheelPerformed(InputAction.CallbackContext context)
		{
			mouseScrollInput = context.ReadValue<Vector2>().y;
		}

		private void OnScrollWheelCanceled(InputAction.CallbackContext context)
		{
			mouseScrollInput = 0f;
		}

		private void OnJumpPerformed(InputAction.CallbackContext context)
		{
			jumpQueued = true;
		}

		private void OnCrouchPerformed(InputAction.CallbackContext context)
		{
			crouchInputActive = true;
		}

		private void OnCrouchCanceled(InputAction.CallbackContext context)
		{
			crouchInputActive = false;
		}

		private void OnSprintPerformed(InputAction.CallbackContext context)
		{
			sprintInputActive = true;
		}

		private void OnSprintCanceled(InputAction.CallbackContext context)
		{
			sprintInputActive = false;
		}

		// --- Action Callbacks ---

		/// <summary>
		/// Callback for when the Interact input action is performed.
		/// Attempts to interact with the current target if possible.
		/// </summary>
		private void OnInteractPerformed(InputAction.CallbackContext context)
		{
			if (!CanUpdateInput() || UIManager.ControlHasFocus()) return;

			if (Character.TryGet(out ITargetController targetController))
			{
				Transform target = targetController.Current.Target;
				if (target != null)
				{
					IInteractable interactable = target.GetComponent<IInteractable>();
					if (interactable != null && interactable.CanInteract(Character))
					{
						Client.Broadcast(new InteractableBroadcast()
						{
							InteractableID = interactable.ID,
						}, Channel.Reliable);
					}
				}
			}
		}

		/// <summary>
		/// Callback for toggling first-person camera mode.
		/// </summary>
		private void OnToggleFirstPersonPerformed(InputAction.CallbackContext context)
		{
			if (!CanUpdateInput()) return;
			Character.KCCPlayer.CharacterCamera.TargetDistance = (Character.KCCPlayer.CharacterCamera.TargetDistance == 0f) ? Character.KCCPlayer.CharacterCamera.DefaultDistance : 0f;
		}

		/// <summary>
		/// Callback for cancel input action.
		/// Interrupts the current ability if possible.
		/// </summary>
		private void OnCancelPerformed(InputAction.CallbackContext context)
		{
			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.Interrupt(Character);
			}
		}

		/// <summary>
		/// Callback for the mouse-mode input action. Releases or recaptures the cursor on demand.
		/// </summary>
		/// <remarks>
		/// Releasing is forced so it survives <see cref="HandleAutoDismiss"/>, which recaptures the
		/// cursor as soon as no panel is open. That is right when a panel released it and wrong when
		/// the player asked for it — unforced, this key would release the cursor and have it taken
		/// straight back. Pressing it again clears the flag and hands control back to the panels.
		/// </remarks>
		/// <param name="context">Input callback context.</param>
		private void OnToggleMouseModePerformed(InputAction.CallbackContext context)
		{
			PlayerInputController.ToggleMouseMode(!PlayerInputController.MouseMode);
		}

		/// <summary>
		/// Callback for closing the last UI element.
		/// If no UI can be closed and mouse mode is active, toggles mouse mode off.
		/// </summary>
		private void OnCloseLastUIPerformed(InputAction.CallbackContext context)
		{
			if (!UIManager.CloseNext())
			{
				if (PlayerInputController.MouseMode)
				{
					PlayerInputController.ToggleMouseMode();
				}
			}
		}

		/// <summary>
		/// Callback for chat input action.
		/// Activates the chat input field and enables mouse mode if not already active.
		/// </summary>
		private void OnChatPerformed(InputAction.CallbackContext context)
		{
			if (UIManager.TryGetTK("UIChat", out UITKChat chat))
			{
				chat.EnableChatInput();
			}
		}

		/// <summary>
		/// Callback for equipment input action.
		/// Shows the equipment UI and sets the equipment view camera.
		/// </summary>
		private void OnEquipmentPerformed(InputAction.CallbackContext context)
		{
			if (UIManager.TryGetTK("UIEquipment", out UITKEquipment equipment))
			{
				equipment.SetEquipmentViewCamera(Character.EquipmentViewCamera);
				equipment.ToggleVisibility();
			}
		}

		// --- UI Toggle Callbacks ---

		private void OnInventoryPerformed(InputAction.CallbackContext context)
		{
			UIManager.ToggleVisibility("UIInventory");
		}

		private void OnAbilitiesPerformed(InputAction.CallbackContext context)
		{
			UIManager.ToggleVisibility("UIAbilities");
		}

		private void OnGuildPerformed(InputAction.CallbackContext context)
		{
			UIManager.ToggleVisibility("UIGuild");
		}

		private void OnPartyPerformed(InputAction.CallbackContext context)
		{
			UIManager.ToggleVisibility("UIParty");
		}

		private void OnFriendsPerformed(InputAction.CallbackContext context)
		{
			UIManager.ToggleVisibility("UIFriendList");
		}

		private void OnAchievementsPerformed(InputAction.CallbackContext context)
		{
			UIManager.ToggleVisibility("UIAchievements");
		}

		private void OnFactionsPerformed(InputAction.CallbackContext context)
		{
			UIManager.ToggleVisibility("UIFactions");
		}

		private void OnMinimapPerformed(InputAction.CallbackContext context)
		{
			UIManager.ToggleVisibility("UIMinimap");
		}

		private void OnMenuPerformed(InputAction.CallbackContext context)
		{
			/* Escape is bound to CloseLastUI as well as Menu, and CloseLastUI is handled first.
			 * If it already closed a panel, this press is spent — toggling here would see the
			 * menu closed and immediately reopen it, so Escape could never close the menu. */
			if (UIManager.ClosedThisFrame)
			{
				return;
			}

			UIManager.ToggleVisibility("UIMenu");
		}

		/// <summary>Loads keybinding overrides from global config.</summary>
		private static void LoadBindingOverrides()
		{
			if (Configuration.GlobalSettings == null || Controls == null) return;
			if (!Configuration.GlobalSettings.TryGetString("InputBindingOverrides", out string json)
				|| string.IsNullOrEmpty(json))
			{
				return;
			}

			/* The overrides live as one value in a plain-text config file the player can edit and
			 * that a crash can truncate mid-write. LoadBindingOverridesFromJson throws on anything
			 * malformed, and this runs unguarded from Initialize() during world entry — so a
			 * single corrupt character aborted input initialisation entirely, leaving the player
			 * in the world with no controls at all. Worse, the only route to "Reset All Keys" is
			 * the Options panel, which needs working input to reach: the failure was
			 * unrecoverable in-game and could only be fixed by hand-editing the file.
			 *
			 * Dropping the key and continuing costs the player their rebinds once. That is a far
			 * better outcome than a session that cannot be played, and it is self-correcting —
			 * the next rebind writes a clean value back. */
			try
			{
				Controls.LoadBindingOverridesFromJson(json);
			}
			catch (Exception ex)
			{
				Log.Error("PlayerInputController",
					"Saved keybinding overrides could not be parsed and have been discarded; " +
					"the default bindings are in effect.", ex);
				Configuration.GlobalSettings.Set("InputBindingOverrides", string.Empty);
			}
		}
	}
}