using FishNet.Transporting;
using UnityEngine;
using FishMMO.Shared;
using KinematicCharacterController;
using System.Runtime.CompilerServices;

namespace FishMMO.Client
{
	public class LocalInputController : MonoBehaviour
	{
#if !UNITY_SERVER
		/// <summary>
		/// The player character associated with this input controller.
		/// </summary>
		public IPlayerCharacter Character { get; private set; }

		/// <summary>Input axis name for mouse X movement.</summary>
		private const string MouseXInput = "Mouse X";
		/// <summary>Input axis name for mouse Y movement.</summary>
		private const string MouseYInput = "Mouse Y";
		/// <summary>Input axis name for mouse scroll wheel.</summary>
		private const string MouseScrollInput = "Mouse ScrollWheel";
		/// <summary>Input axis name for horizontal movement.</summary>
		private const string HorizontalInput = "Horizontal";
		/// <summary>Input axis name for vertical movement.</summary>
		private const string VerticalInput = "Vertical";
		/// <summary>Input name for jump action.</summary>
		private const string JumpInput = "Jump";
		/// <summary>Input name for crouch action.</summary>
		private const string CrouchInput = "Crouch";
		/// <summary>Input name for run/sprint action.</summary>
		private const string RunInput = "Run";
		/// <summary>Input name for toggling first-person camera.</summary>
		private const string ToggleFirstPersonInput = "ToggleFirstPerson";

		/// <summary>
		/// Indicates if a jump input has been queued for processing.
		/// </summary>
		private bool _jumpQueued = false;
		/// <summary>
		/// Indicates if crouch input is currently active.
		/// </summary>
		private bool _crouchInputActive = false;
		/// <summary>
		/// Indicates if sprint input is currently active.
		/// </summary>
		private bool _sprintInputActive = false;

		/// <summary>
		/// Initializes the input controller for the specified player character.
		/// Subscribes to character input handling.
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
		}

		/// <summary>
		/// Deinitializes the input controller, unsubscribing from character input handling.
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
		/// Input is only processed if the character is alive and mouse mode is off.
		/// </summary>
		/// <returns>True if input can be processed; otherwise, false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool CanUpdateInput()
		{
			if (Character.TryGet(out ICharacterDamageController damageController))
			{
				if (!damageController.IsAlive)
				{
					return false;
				}
			}
			return !InputManager.MouseMode;
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

			// we can't change input if the UI is open or if the mouse cursor is enabled
			if (!CanUpdateInput())
			{
				return new KCCInputReplicateData(0.0f,
												 0.0f,
												 moveFlags,
												 Character.KCCPlayer.CharacterCamera.Transform.position,
												 Character.KCCPlayer.CharacterCamera.Transform.rotation);
			}

			if (_jumpQueued)
			{
				moveFlags.EnableBit(KCCMoveFlags.Jump);
				_jumpQueued = false;
			}
			if (_crouchInputActive)
			{
				moveFlags.EnableBit(KCCMoveFlags.Crouch);
			}
			if (_sprintInputActive)
			{
				moveFlags.EnableBit(KCCMoveFlags.Sprint);
			}

			return new KCCInputReplicateData(InputManager.GetAxis(VerticalInput),
											 InputManager.GetAxis(HorizontalInput),
											 moveFlags,
											 Character.KCCPlayer.CharacterCamera.Transform.position,
											 Character.KCCPlayer.CharacterCamera.Transform.rotation);
		}

		/// <summary>
		/// Unity event called every frame. Updates input states.
		/// </summary>
		private void Update()
		{
			UpdateInput();
		}

		/// <summary>
		/// Handles UI and gameplay input, including movement, interaction, and UI toggling.
		/// UI input is handled here because UI elements are disabled when toggling visibility.
		/// </summary>
		private void UpdateInput()
		{
			// if an input has focus we should skip input otherwise things will happen while we are typing!
			if (Character == null ||
				UIManager.InputControlHasFocus())
			{
				return;
			}

			// mouse mode can toggle at any time other than input focus
			if (InputManager.GetKeyDown("Mouse Mode"))
			{
				InputManager.ToggleMouseMode(true);
			}

			// we can interact with things as long as the UI doesn't have focus
			if (!UIManager.ControlHasFocus())
			{
				// interact overrides movement inputs
				if (InputManager.GetKeyDown("Interact") &&
					Character.TryGet(out ITargetController targetController))
				{
					Transform target = targetController.Current.Target;
					if (target != null)
					{
						IInteractable interactable = target.GetComponent<IInteractable>();
						if (interactable != null &&
							interactable.CanInteract(Character))
						{
							Client.Broadcast(new InteractableBroadcast()
							{
								InteractableID = interactable.ID,
							}, Channel.Reliable);
						}
					}
				}
				else if (CanUpdateInput())
				{
					if (InputManager.GetKeyDown(JumpInput) &&
						!Character.CharacterController.IsJumping)
					{
						_jumpQueued = true;
					}

					_crouchInputActive = InputManager.GetKey(CrouchInput);

					_sprintInputActive = InputManager.GetKey(RunInput);
				}
			}

			// UI windows should be able to open/close freely if an InputControl is not focused
			if (InputManager.GetKeyDown("Inventory"))
			{
				UIManager.ToggleVisibility("UIInventory");
			}

			if (InputManager.GetKeyDown("Abilities"))
			{
				UIManager.ToggleVisibility("UIAbilities");
			}

			if (InputManager.GetKeyDown("Equipment") &&
				UIManager.TryGet("UIEquipment", out UIEquipment uiEquipment))
			{
				uiEquipment.SetEquipmentViewCamera(Character.EquipmentViewCamera);
				uiEquipment.ToggleVisibility();
			}

			if (InputManager.GetKeyDown("Guild"))
			{
				UIManager.ToggleVisibility("UIGuild");
			}

			if (InputManager.GetKeyDown("Party"))
			{
				UIManager.ToggleVisibility("UIParty");
			}

			if (InputManager.GetKeyDown("Friends"))
			{
				UIManager.ToggleVisibility("UIFriendList");
			}

			if (InputManager.GetKeyDown("Achievements"))
			{
				UIManager.ToggleVisibility("UIAchievements");
			}

			if (InputManager.GetKeyDown("Factions"))
			{
				UIManager.ToggleVisibility("UIFactions");
			}

			if (InputManager.GetKeyDown("Minimap"))
			{
				UIManager.ToggleVisibility("UIMinimap");
			}

			if (InputManager.GetKeyDown("Menu"))
			{
				UIManager.ToggleVisibility("UIMenu");
			}

			if (InputManager.GetKeyDown("Close Last UI") && !UIManager.CloseNext())
			{
				if (InputManager.MouseMode)
				{
					InputManager.ToggleMouseMode();
				}
			}

			if (InputManager.GetKeyDown("Cancel") &&
				Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.Interrupt(Character);
			}

			if (!InputManager.ForcedMouseMode && !UIManager.CloseNext(true))
			{
				if (InputManager.MouseMode)
				{
					InputManager.ToggleMouseMode();
				}
			}
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
				// Create the look input vector for the camera
				float mouseLookAxisUp = InputManager.GetAxis(MouseYInput);
				float mouseLookAxisRight = InputManager.GetAxis(MouseXInput);
				Vector3 lookInputVector = new Vector3(mouseLookAxisRight, mouseLookAxisUp, 0f);

				float scrollInput = 0.0f;

				// Input for zooming the camera (disabled in WebGL because it can cause problems)
				scrollInput = -InputManager.GetAxis(MouseScrollInput);

				// Apply inputs to the camera
				Character.KCCPlayer.UpdateCamera(scrollInput, lookInputVector);

				// Handle toggling zoom level
				if (InputManager.GetKeyDown(ToggleFirstPersonInput))
				{
					Character.KCCPlayer.CharacterCamera.TargetDistance = (Character.KCCPlayer.CharacterCamera.TargetDistance == 0f) ? Character.KCCPlayer.CharacterCamera.DefaultDistance : 0f;
				}
				Character.KCCPlayer.SetOrientationMethod(Character.KCCPlayer.CharacterController.OrientationMethod);
			}
			else
			{
				Character.KCCPlayer.UpdateCamera(0.0f, Vector3.zero);
			}
		}
#endif
	}
}