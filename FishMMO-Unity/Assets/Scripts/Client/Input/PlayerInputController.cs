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
#if !UNITY_SERVER
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
		/// Subscribes to all relevant input actions from the Input System.
		/// </summary>
		private void SubscribeToInputActions()
		{
			if (PlayerInputHandler.Controls == null)
			{
				Log.Error("PlayerInputController", "PlayerControls not initialized. Ensure PlayerInputHandler is active in the scene.");
				return;
			}

			// Player continuous input
			PlayerInputHandler.Controls.Player.Move.performed += OnMovePerformed;
			PlayerInputHandler.Controls.Player.Move.canceled += OnMoveCanceled;

			PlayerInputHandler.Controls.Player.Look.performed += OnLookPerformed;
			PlayerInputHandler.Controls.Player.Look.canceled += OnLookCanceled;

			PlayerInputHandler.Controls.UI.ScrollWheel.performed += OnScrollWheelPerformed;
			PlayerInputHandler.Controls.UI.ScrollWheel.canceled += OnScrollWheelCanceled;

			PlayerInputHandler.Controls.Player.Jump.performed += OnJumpPerformed;
			PlayerInputHandler.Controls.Player.Crouch.performed += OnCrouchPerformed;
			PlayerInputHandler.Controls.Player.Crouch.canceled += OnCrouchCanceled;
			PlayerInputHandler.Controls.Player.Sprint.performed += OnSprintPerformed;
			PlayerInputHandler.Controls.Player.Sprint.canceled += OnSprintCanceled;

			// Player action callbacks
			PlayerInputHandler.Controls.Player.Interact.performed += OnInteractPerformed;
			PlayerInputHandler.Controls.Player.ToggleFirstPerson.performed += OnToggleFirstPersonPerformed;
			PlayerInputHandler.Controls.Player.Cancel.performed += OnCancelPerformed;
			PlayerInputHandler.Controls.Player.CloseLastUI.performed += OnCloseLastUIPerformed;
			PlayerInputHandler.Controls.Player.Chat.performed += OnChatPerformed;

			// UI/Menu toggles
			PlayerInputHandler.Controls.Player.Inventory.performed += OnInventoryPerformed;
			PlayerInputHandler.Controls.Player.Abilities.performed += OnAbilitiesPerformed;
			PlayerInputHandler.Controls.Player.Equipment.performed += OnEquipmentPerformed;
			PlayerInputHandler.Controls.Player.Guild.performed += OnGuildPerformed;
			PlayerInputHandler.Controls.Player.Party.performed += OnPartyPerformed;
			PlayerInputHandler.Controls.Player.Friends.performed += OnFriendsPerformed;
			PlayerInputHandler.Controls.Player.Achievements.performed += OnAchievementsPerformed;
			PlayerInputHandler.Controls.Player.Factions.performed += OnFactionsPerformed;
			PlayerInputHandler.Controls.Player.Minimap.performed += OnMinimapPerformed;
			PlayerInputHandler.Controls.Player.Menu.performed += OnMenuPerformed;
		}

		/// <summary>
		/// Unsubscribes from all input actions to prevent memory leaks and unwanted input processing.
		/// </summary>
		private void UnsubscribeFromInputActions()
		{
			if (PlayerInputHandler.Controls == null) return;

			// Player continuous input
			PlayerInputHandler.Controls.Player.Move.performed -= OnMovePerformed;
			PlayerInputHandler.Controls.Player.Move.canceled -= OnMoveCanceled;

			PlayerInputHandler.Controls.Player.Look.performed -= OnLookPerformed;
			PlayerInputHandler.Controls.Player.Look.canceled -= OnLookCanceled;

			PlayerInputHandler.Controls.UI.ScrollWheel.performed -= OnScrollWheelPerformed;
			PlayerInputHandler.Controls.UI.ScrollWheel.canceled -= OnScrollWheelCanceled;

			PlayerInputHandler.Controls.Player.Jump.performed -= OnJumpPerformed;
			PlayerInputHandler.Controls.Player.Crouch.performed -= OnCrouchPerformed;
			PlayerInputHandler.Controls.Player.Crouch.canceled -= OnCrouchCanceled;
			PlayerInputHandler.Controls.Player.Sprint.performed -= OnSprintPerformed;
			PlayerInputHandler.Controls.Player.Sprint.canceled -= OnSprintCanceled;

			// Player action callbacks
			PlayerInputHandler.Controls.Player.Interact.performed -= OnInteractPerformed;
			PlayerInputHandler.Controls.Player.ToggleFirstPerson.performed -= OnToggleFirstPersonPerformed;
			PlayerInputHandler.Controls.Player.Cancel.performed -= OnCancelPerformed;
			PlayerInputHandler.Controls.Player.CloseLastUI.performed -= OnCloseLastUIPerformed;
			PlayerInputHandler.Controls.Player.Chat.performed -= OnChatPerformed;

			// UI/Menu toggles
			PlayerInputHandler.Controls.Player.Inventory.performed -= OnInventoryPerformed;
			PlayerInputHandler.Controls.Player.Abilities.performed -= OnAbilitiesPerformed;
			PlayerInputHandler.Controls.Player.Equipment.performed -= OnEquipmentPerformed;
			PlayerInputHandler.Controls.Player.Guild.performed -= OnGuildPerformed;
			PlayerInputHandler.Controls.Player.Party.performed -= OnPartyPerformed;
			PlayerInputHandler.Controls.Player.Friends.performed -= OnFriendsPerformed;
			PlayerInputHandler.Controls.Player.Achievements.performed -= OnAchievementsPerformed;
			PlayerInputHandler.Controls.Player.Factions.performed -= OnFactionsPerformed;
			PlayerInputHandler.Controls.Player.Minimap.performed -= OnMinimapPerformed;
			PlayerInputHandler.Controls.Player.Menu.performed -= OnMenuPerformed;
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
			return !PlayerInputHandler.MouseMode && !UIManager.InputControlHasFocus();
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
			if (!PlayerInputHandler.ForcedMouseMode && !UIManager.CloseNext(true))
			{
				if (PlayerInputHandler.MouseMode)
				{
					PlayerInputHandler.ToggleMouseMode();
				}
			}
		}

		/// <summary>
		/// Checks for right-click input while in MouseMode. If the current target is another player character
		/// within range, opens a context menu with interaction options.
		/// </summary>
		private void HandleRightClickContextMenu()
		{
			if (!PlayerInputHandler.MouseMode)
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

			if (!UIManager.TryGet("UIContextMenu", out UIContextMenu contextMenu))
			{
				return;
			}

			IPlayerCharacter capturedTarget = targetPlayer;

			List<(string label, Action callback)> entries = new List<(string label, Action callback)>()
			{
				("Inspect", () =>
				{
					if (UIManager.TryGet("UIInspect", out UIInspect uiInspect))
					{
						uiInspect.Inspect(capturedTarget);
					}
				}),
				("Add Friend", () =>
				{
					Client.Broadcast(new FriendAddNewBroadcast()
					{
						CharacterID = targetCharacterID,
					}, Channel.Reliable);
				}),
				("Invite to Party", () =>
				{
					Client.Broadcast(new PartyInviteBroadcast()
					{
						InviterCharacterID = Character.ID,
						TargetCharacterID = targetCharacterID,
					}, Channel.Reliable);
				}),
				("Trade", () =>
				{
					Log.Debug("PlayerInputController", "Trade is not yet implemented.");
				}),
			};

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
		/// Callback for closing the last UI element.
		/// If no UI can be closed and mouse mode is active, toggles mouse mode off.
		/// </summary>
		private void OnCloseLastUIPerformed(InputAction.CallbackContext context)
		{
			if (!UIManager.CloseNext())
			{
				if (PlayerInputHandler.MouseMode)
				{
					PlayerInputHandler.ToggleMouseMode();
				}
			}
		}

		/// <summary>
		/// Callback for chat input action.
		/// Activates the chat input field and enables mouse mode if not already active.
		/// </summary>
		private void OnChatPerformed(InputAction.CallbackContext context)
		{
			if (UIManager.TryGet("UIChat", out UIChat uiChat))
			{
				uiChat.EnableChatInput();
			}
		}

		/// <summary>
		/// Callback for equipment input action.
		/// Shows the equipment UI and sets the equipment view camera.
		/// </summary>
		private void OnEquipmentPerformed(InputAction.CallbackContext context)
		{
			if (UIManager.TryGet("UIEquipment", out UIEquipment uiEquipment))
			{
				uiEquipment.SetEquipmentViewCamera(Character.EquipmentViewCamera);
				uiEquipment.ToggleVisibility();
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
			UIManager.ToggleVisibility("UIMenu");
		}
#endif
	}
}