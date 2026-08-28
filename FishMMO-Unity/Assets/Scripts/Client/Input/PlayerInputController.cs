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

		/// <summary>
		/// The last orientation method sent to the server, so the per-frame input pass only sends the
		/// ServerRpc when the value actually changes. Null until the first send.
		/// </summary>
		private OrientationMethod? lastSentOrientationMethod;
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

		/// <summary>
		/// Creates the shared <see cref="PlayerControls"/> asset and applies the player's saved
		/// keybinding overrides, without enabling any action map.
		/// </summary>
		/// <remarks>
		/// Split out of <see cref="InitializeControls"/> so the settings panel can list and rebind
		/// keys before the player has entered the world. The asset on its own is inert data: no
		/// action fires until a map is enabled, so creating it early does not make the world's
		/// input live on the login screen.
		/// <para>
		/// Called from the client's boot phase. Before that existed, the asset was created only by
		/// <see cref="Initialize"/> on world entry — so the Key Bindings tab had nothing to show
		/// until then, and a saved override was not read until then either.
		/// </para>
		/// </remarks>
		public static void EnsureControlsCreated()
		{
			if (Controls != null)
			{
				return;
			}

			Controls = new PlayerControls();
			LoadBindingOverrides();
		}

		/// <summary>Ensures the static Controls instance is created and its maps are enabled.</summary>
		public static void InitializeControls()
		{
			EnsureControlsCreated();

			/* Enabling is idempotent and deliberately NOT skipped when the asset already exists:
			 * the boot phase creates it inert, so on the normal path this is the call that
			 * actually turns input on. An early-out on "already created" is how the player ends
			 * up in the world unable to move. */
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
		/// <remarks>
		/// Through <see cref="ClientSettings.SetString"/>, which schedules the debounced write.
		/// Writing straight into the store left the new binding in memory only: it survived until
		/// something else happened to save the file, and was lost outright if nothing did.
		/// </remarks>
		public static void SaveBindingOverrides()
		{
			if (Controls == null)
			{
				return;
			}
			ClientSettings.SetString(ClientSettings.InputBindingOverridesKey, Controls.SaveBindingOverridesAsJson());
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

			/* Ensures the shared PlayerControls exists and its maps are enabled. Saved keybinds
			 * are applied by EnsureControlsCreated when the asset is built — normally during the
			 * client's boot phase, long before this runs — so there is no second load here. */
			InitializeControls();

			// Start with cursor visible (login/loading state).
			MouseMode = true;

			SubscribeToInputActions();
		}

		/// <summary>
		/// Deinitializes the input controller, unsubscribing from input events and character input handling.
		/// </summary>
		public void Deinitialize()
		{
			/* Unsubscribing is NOT conditional on Character.
			 *
			 * PlayerControls is static and outlives this component, but the handlers registered
			 * against it are instance methods — so a teardown that skips the unsubscribe leaves
			 * the static action holding a delegate over a component that is destroyed moments
			 * later. Returning early on a null Character, which a despawn or scene transfer can
			 * produce, therefore leaks one dead subscriber per character for the session.
			 *
			 * The failure this produces is easy to misread: movement is polled through
			 * ReadValue and keeps working, while everything routed through a "performed"
			 * callback — interact, jump, crouch, sprint — is the half that suffers. */
			if (Character != null && Character.KCCPlayer != null)
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
			Controls.Player.WorldMap.performed += OnWorldMapPerformed;
			Controls.Player.Lore.performed += OnLorePerformed;
			Controls.Player.Pet.performed += OnPetPerformed;
			Controls.Player.Options.performed += OnOptionsPerformed;
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
			Controls.Player.WorldMap.performed -= OnWorldMapPerformed;
			Controls.Player.Lore.performed -= OnLorePerformed;
			Controls.Player.Pet.performed -= OnPetPerformed;
			Controls.Player.Options.performed -= OnOptionsPerformed;
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
			UIManager.Hide("UIMap");
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

				/* Only when it actually changes.
				 *
				 * This is a ServerRpc, and it sat in LateUpdate re-sending the value it had just read
				 * back off the controller — 120 messages per second per player at 120 FPS, every one
				 * of them carrying an enum the server already had. The orientation method changes
				 * when the player toggles a camera mode, which is not a per-frame event. */
				OrientationMethod orientationMethod = Character.KCCPlayer.CharacterController.OrientationMethod;
				if (!lastSentOrientationMethod.HasValue ||
					lastSentOrientationMethod.Value != orientationMethod)
				{
					lastSentOrientationMethod = orientationMethod;
					Character.KCCPlayer.SetOrientationMethod(orientationMethod);
				}
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
			if (TypingIntoField) return;
			if (!CanUpdateInput() || UIManager.ControlHasFocus()) return;

			if (Character.TryGet(out ITargetController targetController))
			{
				Transform target = targetController.Current.Target;
				if (target != null)
				{
					IInteractable interactable = InteractableResolver.Resolve(target.gameObject);
					/* The client keeps its own copy of the limiter so holding the key does not
					 * spam the server. It is spent here and again server-side against that peer's
					 * own clock; the two never see each other's value. */
					if (interactable != null &&
						interactable.CanInteract(Character) &&
						interactable.TryConsumeInteractRateLimit(Character))
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
			if (TypingIntoField) return;
			if (UIManager.TryGetTK("UIEquipment", out UITKEquipment equipment))
			{
				equipment.SetEquipmentViewCamera(Character.EquipmentViewCamera);
				equipment.ToggleVisibility();
			}
		}

		// --- UI Toggle Callbacks ---

		/// <summary>
		/// True when a panel toggle bound to a letter key must be ignored because the player is
		/// typing into a text field.
		/// </summary>
		/// <remarks>
		/// Movement already gates on this (see the CanReadInput path) and so does the hotkey bar,
		/// but the window toggles did not — so typing an ordinary sentence into chat opened the
		/// inventory on "i", the guild panel on "g", and so on. The panel that opened then took
		/// focus off the chat field, which is why the rest of the sentence went nowhere: the two
		/// symptoms are one bug.
		/// </remarks>
		private static bool TypingIntoField => UIManager.InputControlHasFocus();


		private void OnInventoryPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIInventory");
		}

		private void OnAbilitiesPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIAbilities");
		}

		private void OnGuildPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIGuild");
		}

		private void OnPartyPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIParty");
		}

		private void OnFriendsPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIFriendList");
		}

		private void OnAchievementsPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIAchievements");
		}

		private void OnFactionsPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIFactions");
		}

		private void OnMinimapPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIMinimap");
		}

		/// <summary>
		/// Callback for the world map shortcut.
		/// </summary>
		/// <remarks>
		/// A key of its own rather than sharing the minimap's. The two are different things — one
		/// hides a permanent HUD element, the other opens a window — and binding both to M would
		/// make the common action (open the map) also perform the rare and confusing one (make the
		/// minimap disappear). The minimap's own MAP button opens the same panel for players who
		/// never learn the key.
		/// </remarks>
		private void OnWorldMapPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIMap");
		}

		/*
		 * The panels below are opened by the player, so each has a key. The ones that are NOT
		 * here are deliberate.
		 *
		 * A merchant, bank, loot window, NPC dialogue, shrine, gathering node, trade container,
		 * MAILBOX or DUNGEON FINDER is opened by the SERVER, in a reply sent after it has
		 * validated an interaction with something in the world. There is nothing for a key to do
		 * in those cases: pressing it would either open a window the server never populated, or
		 * claim to open a mailbox the character is not standing in front of — and a player who
		 * found the empty window would reasonably report it as broken.
		 *
		 * The scene channel picker and the instance panel are reached from the menu for the
		 * reason the menu documents: they are rare, deliberate acts rather than windows a player
		 * flicks in and out of.
		 */

		private void OnLorePerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UILore");
		}

		private void OnPetPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIPetControl");
		}

		/// <summary>
		/// Callback for the settings shortcut.
		/// </summary>
		/// <remarks>
		/// A key of its own as well as the route through the menu. Options is the panel a player
		/// reaches for when something is wrong — the sound is too loud, the frame rate is wrong,
		/// a key does not do what they expect — and making that a two-step trip through a menu
		/// that pauses nothing is a poor trade for one keyboard letter.
		/// </remarks>
		private void OnOptionsPerformed(InputAction.CallbackContext context)
		{
			if (TypingIntoField) return;
			UIManager.ToggleVisibility("UIOptions");
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
		/// <remarks>
		/// Public so the boot phase can apply saved bindings as soon as the configuration is
		/// loaded, rather than leaving them until world entry.
		/// </remarks>
		public static void LoadBindingOverrides()
		{
			if (Configuration.GlobalSettings == null || Controls == null) return;
			if (!Configuration.GlobalSettings.TryGetString(ClientSettings.InputBindingOverridesKey, out string json)
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
				ClientSettings.SetString(ClientSettings.InputBindingOverridesKey, string.Empty);
			}
		}
	}
}