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
		public IPlayerCharacter Character { get; private set; }

		private const string MouseXInput = "Mouse X";
		private const string MouseYInput = "Mouse Y";
		private const string MouseScrollInput = "Mouse ScrollWheel";
		private const string HorizontalInput = "Horizontal";
		private const string VerticalInput = "Vertical";
		private const string JumpInput = "Jump";
		private const string CrouchInput = "Crouch";
		private const string RunInput = "Run";
		private const string ToggleFirstPersonInput = "ToggleFirstPerson";

		private bool _jumpQueued = false;
		private bool _crouchInputActive = false;
		private bool _sprintInputActive = false;

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

		private void OnEnable()
		{
			UIManager.Show("UIHealthBar");
			UIManager.Show("UIManaBar");
			UIManager.Show("UIStaminaBar");
			UIManager.Show("UIHotkeyBar");
			UIManager.Show("UIChat");
		}

		private void OnDisable()
		{
			UIManager.Hide("UIHealthBar");
			UIManager.Hide("UIManaBar");
			UIManager.Hide("UIStaminaBar");
			UIManager.Hide("UIHotkeyBar");
			UIManager.Hide("UIChat");
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool CanUpdateInput()
		{
			return !InputManager.MouseMode;
		}

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

		private void Update()
		{
			UpdateInput();
		}

		/// <summary>
		/// We handle UI input here because we completely disable UI elements when toggling visibility.
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
				InputManager.ToggleMouseMode();
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
								interactableID = interactable.ID,
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

			if (InputManager.GetKeyDown("Menu"))
			{
				UIManager.ToggleVisibility("UIMenu");
			}

			if (InputManager.GetKeyDown("Close Last UI") && !UIManager.CloseNext())
			{
				if (InputManager.MouseMode)
				{
					InputManager.MouseMode = false;
				}
			}
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