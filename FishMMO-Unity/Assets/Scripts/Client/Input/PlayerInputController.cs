using FishNet.Transporting;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;
using KinematicCharacterController;
using UnityEngine.InputSystem;

namespace FishMMO.Client
{
	public class PlayerInputController : MonoBehaviour
	{
#if !UNITY_SERVER
		public IPlayerCharacter Character { get; private set; }

		private bool _jumpQueued = false;
		private bool _crouchInputActive = false;
		private bool _sprintInputActive = false;

		// Current input values from the new Input System
		private Vector2 _moveInput;
		private Vector2 _lookInput;
		private float _mouseScrollInput;

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

			// Subscribe to Input Actions events
			SubscribeToInputActions();
		}

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

			// Unsubscribe from Input Actions events
			UnsubscribeFromInputActions();
		}

		private void SubscribeToInputActions()
		{
			if (PlayerInputHandler.Controls == null)
			{
				Log.Error("PlayerInputController", "PlayerControls not initialized. Ensure PlayerInputHandler is active in the scene.");
				return;
			}

			// Player Actions
			PlayerInputHandler.Controls.Player.Move.performed += ctx => _moveInput = ctx.ReadValue<Vector2>();
			PlayerInputHandler.Controls.Player.Move.canceled += ctx => _moveInput = Vector2.zero;

			PlayerInputHandler.Controls.Player.Look.performed += ctx => _lookInput = ctx.ReadValue<Vector2>();
			PlayerInputHandler.Controls.Player.Look.canceled += ctx => _lookInput = Vector2.zero;

			// Mouse Scroll is handled in PlayerControls.UI (or custom action if preferred)
			// For camera scroll, we'll listen to UI.ScrollWheel
			PlayerInputHandler.Controls.UI.ScrollWheel.performed += ctx => _mouseScrollInput = ctx.ReadValue<Vector2>().y;
			PlayerInputHandler.Controls.UI.ScrollWheel.canceled += ctx => _mouseScrollInput = 0f;

			PlayerInputHandler.Controls.Player.Jump.performed += ctx => _jumpQueued = true;
			PlayerInputHandler.Controls.Player.Crouch.performed += ctx => _crouchInputActive = true;
			PlayerInputHandler.Controls.Player.Crouch.canceled += ctx => _crouchInputActive = false;
			PlayerInputHandler.Controls.Player.Sprint.performed += ctx => _sprintInputActive = true;
			PlayerInputHandler.Controls.Player.Sprint.canceled += ctx => _sprintInputActive = false;

			PlayerInputHandler.Controls.Player.Interact.performed += OnInteractPerformed;
			PlayerInputHandler.Controls.Player.ToggleFirstPerson.performed += OnToggleFirstPersonPerformed;
			PlayerInputHandler.Controls.Player.Cancel.performed += OnCancelPerformed;
			PlayerInputHandler.Controls.Player.CloseLastUI.performed += OnCloseLastUIPerformed;
			PlayerInputHandler.Controls.Player.Chat.performed += OnChatPerformed;

			// UI/Menu Toggles
			PlayerInputHandler.Controls.Player.Inventory.performed += ctx => UIManager.ToggleVisibility("UIInventory");
			PlayerInputHandler.Controls.Player.Abilities.performed += ctx => UIManager.ToggleVisibility("UIAbilities");
			PlayerInputHandler.Controls.Player.Equipment.performed += OnEquipmentPerformed;
			PlayerInputHandler.Controls.Player.Guild.performed += ctx => UIManager.ToggleVisibility("UIGuild");
			PlayerInputHandler.Controls.Player.Party.performed += ctx => UIManager.ToggleVisibility("UIParty");
			PlayerInputHandler.Controls.Player.Friends.performed += ctx => UIManager.ToggleVisibility("UIFriendList");
			PlayerInputHandler.Controls.Player.Achievements.performed += ctx => UIManager.ToggleVisibility("UIAchievements");
			PlayerInputHandler.Controls.Player.Factions.performed += ctx => UIManager.ToggleVisibility("UIFactions");
			PlayerInputHandler.Controls.Player.Minimap.performed += ctx => UIManager.ToggleVisibility("UIMinimap");
			PlayerInputHandler.Controls.Player.Menu.performed += ctx => UIManager.ToggleVisibility("UIMenu");

			// Hotkeys
			PlayerInputHandler.Controls.Player.Hotkey1.performed += ctx => HandleHotkeyInput(1);
			PlayerInputHandler.Controls.Player.Hotkey2.performed += ctx => HandleHotkeyInput(2);
			PlayerInputHandler.Controls.Player.Hotkey3.performed += ctx => HandleHotkeyInput(3);
			PlayerInputHandler.Controls.Player.Hotkey4.performed += ctx => HandleHotkeyInput(4);
			PlayerInputHandler.Controls.Player.Hotkey5.performed += ctx => HandleHotkeyInput(5);
			PlayerInputHandler.Controls.Player.Hotkey6.performed += ctx => HandleHotkeyInput(6);
			PlayerInputHandler.Controls.Player.Hotkey7.performed += ctx => HandleHotkeyInput(7);
			PlayerInputHandler.Controls.Player.Hotkey8.performed += ctx => HandleHotkeyInput(8);
			PlayerInputHandler.Controls.Player.Hotkey9.performed += ctx => HandleHotkeyInput(9);
			PlayerInputHandler.Controls.Player.Hotkey0.performed += ctx => HandleHotkeyInput(0);
		}

		private void UnsubscribeFromInputActions()
		{
			if (PlayerInputHandler.Controls == null) return;

			PlayerInputHandler.Controls.Player.Move.performed -= ctx => _moveInput = ctx.ReadValue<Vector2>();
			PlayerInputHandler.Controls.Player.Move.canceled -= ctx => _moveInput = Vector2.zero;

			PlayerInputHandler.Controls.Player.Look.performed -= ctx => _lookInput = ctx.ReadValue<Vector2>();
			PlayerInputHandler.Controls.Player.Look.canceled -= ctx => _lookInput = Vector2.zero;

			PlayerInputHandler.Controls.UI.ScrollWheel.performed -= ctx => _mouseScrollInput = ctx.ReadValue<Vector2>().y;
			PlayerInputHandler.Controls.UI.ScrollWheel.canceled -= ctx => _mouseScrollInput = 0f;

			PlayerInputHandler.Controls.Player.Jump.performed -= ctx => _jumpQueued = true;
			PlayerInputHandler.Controls.Player.Crouch.performed -= ctx => _crouchInputActive = true;
			PlayerInputHandler.Controls.Player.Crouch.canceled -= ctx => _crouchInputActive = false;
			PlayerInputHandler.Controls.Player.Sprint.performed -= ctx => _sprintInputActive = true;
			PlayerInputHandler.Controls.Player.Sprint.canceled -= ctx => _sprintInputActive = false;

			PlayerInputHandler.Controls.Player.Interact.performed -= OnInteractPerformed;
			PlayerInputHandler.Controls.Player.ToggleFirstPerson.performed -= OnToggleFirstPersonPerformed;
			PlayerInputHandler.Controls.Player.Cancel.performed -= OnCancelPerformed;
			PlayerInputHandler.Controls.Player.CloseLastUI.performed -= OnCloseLastUIPerformed;
			PlayerInputHandler.Controls.Player.Chat.performed -= OnChatPerformed;

			PlayerInputHandler.Controls.Player.Inventory.performed -= ctx => UIManager.ToggleVisibility("UIInventory");
			PlayerInputHandler.Controls.Player.Abilities.performed -= ctx => UIManager.ToggleVisibility("UIAbilities");
			PlayerInputHandler.Controls.Player.Equipment.performed -= OnEquipmentPerformed;
			PlayerInputHandler.Controls.Player.Guild.performed -= ctx => UIManager.ToggleVisibility("UIGuild");
			PlayerInputHandler.Controls.Player.Party.performed -= ctx => UIManager.ToggleVisibility("UIParty");
			PlayerInputHandler.Controls.Player.Friends.performed -= ctx => UIManager.ToggleVisibility("UIFriendList");
			PlayerInputHandler.Controls.Player.Achievements.performed -= ctx => UIManager.ToggleVisibility("UIAchievements");
			PlayerInputHandler.Controls.Player.Factions.performed -= ctx => UIManager.ToggleVisibility("UIFactions");
			PlayerInputHandler.Controls.Player.Minimap.performed -= ctx => UIManager.ToggleVisibility("UIMinimap");
			PlayerInputHandler.Controls.Player.Menu.performed -= ctx => UIManager.ToggleVisibility("UIMenu");

			PlayerInputHandler.Controls.Player.Hotkey1.performed -= ctx => HandleHotkeyInput(1);
			PlayerInputHandler.Controls.Player.Hotkey2.performed -= ctx => HandleHotkeyInput(2);
			PlayerInputHandler.Controls.Player.Hotkey3.performed -= ctx => HandleHotkeyInput(3);
			PlayerInputHandler.Controls.Player.Hotkey4.performed -= ctx => HandleHotkeyInput(4);
			PlayerInputHandler.Controls.Player.Hotkey5.performed -= ctx => HandleHotkeyInput(5);
			PlayerInputHandler.Controls.Player.Hotkey6.performed -= ctx => HandleHotkeyInput(6);
			PlayerInputHandler.Controls.Player.Hotkey7.performed -= ctx => HandleHotkeyInput(7);
			PlayerInputHandler.Controls.Player.Hotkey8.performed -= ctx => HandleHotkeyInput(8);
			PlayerInputHandler.Controls.Player.Hotkey9.performed -= ctx => HandleHotkeyInput(9);
			PlayerInputHandler.Controls.Player.Hotkey0.performed -= ctx => HandleHotkeyInput(0);
		}

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
			// Input for player actions should only be processed if the game is in "MouseMode == false" (cursor hidden/locked)
			// and no UI input field has focus.
			return !PlayerInputHandler.MouseMode && !UIManager.InputControlHasFocus();
		}

		public KCCInputReplicateData KCCPlayer_OnHandleCharacterInput()
		{
			int moveFlags = 0;
			moveFlags.EnableBit(KCCMoveFlags.IsActualData);

			if (!CanUpdateInput())
			{
				// Reset input states if input is not allowed
				_jumpQueued = false;
				_crouchInputActive = false;
				_sprintInputActive = false;
				_moveInput = Vector2.zero;
				_lookInput = Vector2.zero;

				return new KCCInputReplicateData(0.0f,
												 0.0f,
												 moveFlags,
												 Character.KCCPlayer.CharacterCamera.Transform.position,
												 Character.KCCPlayer.CharacterCamera.Transform.rotation);
			}

			if (_jumpQueued)
			{
				moveFlags.EnableBit(KCCMoveFlags.Jump);
				_jumpQueued = false; // Consume the jump input
			}
			if (_crouchInputActive)
			{
				moveFlags.EnableBit(KCCMoveFlags.Crouch);
			}
			if (_sprintInputActive)
			{
				moveFlags.EnableBit(KCCMoveFlags.Sprint);
			}

			// KCCInputReplicateData expects vertical and horizontal, which align with Move.y and Move.x
			return new KCCInputReplicateData(_moveInput.y,
											 _moveInput.x,
											 moveFlags,
											 Character.KCCPlayer.CharacterCamera.Transform.position,
											 Character.KCCPlayer.CharacterCamera.Transform.rotation);
		}

		private void LateUpdate()
		{
			if (Character.KCCPlayer.CharacterCamera == null)
			{
				return;
			}

			HandleCameraInput();
		}

		private void HandleCameraInput()
		{
			// Handle rotating the camera along with physics movers
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
				// Create the look input vector for the camera from _lookInput
				Vector3 lookInputVector = new Vector3(_lookInput.x, _lookInput.y, 0f);

				// Apply inputs to the camera
				// Note: MouseScrollWheel is now a Vector2, so we take the y component for zoom
				Character.KCCPlayer.UpdateCamera(-_mouseScrollInput, lookInputVector);

				Character.KCCPlayer.SetOrientationMethod(Character.KCCPlayer.CharacterController.OrientationMethod);
			}
			else
			{
				// Reset camera input if not allowed
				_lookInput = Vector2.zero;
				_mouseScrollInput = 0f;
				Character.KCCPlayer.UpdateCamera(0.0f, Vector3.zero);
			}
		}

		// --- Action Callbacks ---
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

		private void OnToggleFirstPersonPerformed(InputAction.CallbackContext context)
		{
			if (!CanUpdateInput()) return;
			Character.KCCPlayer.CharacterCamera.TargetDistance = (Character.KCCPlayer.CharacterCamera.TargetDistance == 0f) ? Character.KCCPlayer.CharacterCamera.DefaultDistance : 0f;
		}

		private void OnCancelPerformed(InputAction.CallbackContext context)
		{
			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.Interrupt(Character);
			}
		}

		private void OnCloseLastUIPerformed(InputAction.CallbackContext context)
		{
			if (!UIManager.CloseNext())
			{
				if (PlayerInputHandler.MouseMode)
				{
					// If no UI can be closed and mouse mode is active, toggle out of mouse mode
					PlayerInputHandler.ToggleMouseMode();
				}
			}
		}

		private void OnChatPerformed(InputAction.CallbackContext context)
		{
			// You might want to explicitly activate the chat UI here
			// This is just an example, the specific UIManager method might vary.
			// UIManager.ActivateChatInput();
		}

		private void OnEquipmentPerformed(InputAction.CallbackContext context)
		{
			if (UIManager.TryGet("UIEquipment", out UIEquipment uiEquipment))
			{
				uiEquipment.SetEquipmentViewCamera(Character.EquipmentViewCamera);
				uiEquipment.ToggleVisibility();
			}
		}

		private void HandleHotkeyInput(int hotkeyNumber)
		{
			// Implement your hotkey logic here, e.g., using an ability, consuming an item.
			// Example: Character.UseAbility(hotkeyNumber);
			Log.Debug("PlayerInputController", $"Hotkey {hotkeyNumber} pressed!");
		}
#endif
	}
}