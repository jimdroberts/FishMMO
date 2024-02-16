using UnityEngine;
using FishNet.Transporting;
using FishMMO.Shared;
using KinematicCharacterController;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class LocalInputController : MonoBehaviour
	{
#if !UNITY_SERVER
		public Character Character { get; private set; }

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

		private Cached3DLabel targetLabel;

		public void Initialize(Character character)
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

			if (Character.TryGet(out TargetController targetController) &&
				UIManager.TryGet("UITarget", out UITarget uiTarget))
			{
				targetController.OnChangeTarget += uiTarget.OnChangeTarget;
				targetController.OnUpdateTarget += uiTarget.OnUpdateTarget;
				targetController.OnClearTarget += TargetController_OnClearTarget;
				targetController.OnNewTarget += TargetController_OnNewTarget;
			}

			if (Character.TryGet(out AbilityController abilityController))
			{
				abilityController.OnCanManipulate += () => { return CanUpdateInput(); };

				if (UIManager.TryGet("UICastBar", out UICastBar uiCastBar))
				{
					abilityController.OnUpdate += uiCastBar.OnUpdate;
					abilityController.OnCancel += uiCastBar.OnCancel;
				}

				abilityController.OnReset += AbilityController_OnReset;
				abilityController.OnAddAbility += AbilityController_OnAddAbility;
				abilityController.OnAddKnownAbility += AbilityController_OnAddKnownAbility;
			}

			if (Character.TryGet(out AchievementController achievementController))
			{
				if (LabelMaker.Instance != null)
				{
					achievementController.OnCompleteAchievement += LabelMaker.Display;
				}
			}

			if (Character.TryGet(out CharacterDamageController characterDamageController))
			{
				if (LabelMaker.Instance != null)
				{
					characterDamageController.OnDamageDisplay += LabelMaker.Display;
					characterDamageController.OnHealedDisplay += LabelMaker.Display;
				}
			}

			if (Character.TryGet(out FriendController friendController))
			{
				friendController.OnAddFriend += FriendController_OnAddFriend;
				friendController.OnRemoveFriend += FriendController_OnRemoveFriend;
			}

			if (Character.TryGet(out GuildController guildController))
			{
				guildController.OnReadPayload += GuildController_OnReadPayload;
				guildController.OnReceiveGuildInvite += GuildController_OnReceiveGuildInvite;
				guildController.OnAddGuildMember += GuildController_OnAddGuildMember;
				guildController.OnValidateGuildMembers += GuildController_OnValidateGuildMembers;
				guildController.OnRemoveGuildMember += GuildController_OnRemoveGuildMember;
				guildController.OnLeaveGuild += GuildController_OnLeaveGuild;
			}

			if (Character.TryGet(out PartyController partyController))
			{
				partyController.OnPartyCreated += PartyController_OnPartyCreated;
				partyController.OnReceivePartyInvite += PartyController_OnReceivePartyInvite;
				partyController.OnAddPartyMember += PartyController_OnAddPartyMember;
				partyController.OnValidatePartyMembers += PartyController_OnValidatePartyMembers;
				partyController.OnRemovePartyMember += PartyController_OnRemovePartyMember;
				partyController.OnLeaveParty += PartyController_OnLeaveParty;
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

			if (Character.TryGet(out TargetController targetController) &&
				UIManager.TryGet("UITarget", out UITarget uiTarget))
			{
				targetController.OnChangeTarget -= uiTarget.OnChangeTarget;
				targetController.OnUpdateTarget -= uiTarget.OnUpdateTarget;
				targetController.OnClearTarget -= TargetController_OnClearTarget;
				targetController.OnNewTarget -= TargetController_OnNewTarget;

				LabelMaker.Cache(targetLabel);
				targetLabel = null;
			}

			if (Character.TryGet(out AbilityController abilityController))
			{
				abilityController.OnCanManipulate -= () => { return !InputManager.MouseMode; };

				if (UIManager.TryGet("UICastBar", out UICastBar uiCastBar))
				{
					abilityController.OnUpdate -= uiCastBar.OnUpdate;
					abilityController.OnCancel -= uiCastBar.OnCancel;
				}

				abilityController.OnReset -= AbilityController_OnReset;
				abilityController.OnAddAbility -= AbilityController_OnAddAbility;
				abilityController.OnAddKnownAbility -= AbilityController_OnAddKnownAbility;
			}

			if (Character.TryGet(out AchievementController achievementController))
			{
				if (LabelMaker.Instance != null)
				{
					achievementController.OnCompleteAchievement -= LabelMaker.Display;
				}
			}

			if (Character.TryGet(out CharacterDamageController characterDamageController))
			{
				if (LabelMaker.Instance != null)
				{
					characterDamageController.OnDamageDisplay -= LabelMaker.Display;
					characterDamageController.OnHealedDisplay -= LabelMaker.Display;
				}
			}

			if (Character.TryGet(out FriendController friendController))
			{
				friendController.OnAddFriend -= FriendController_OnAddFriend;
				friendController.OnRemoveFriend -= FriendController_OnRemoveFriend;
			}

			if (Character.TryGet(out GuildController guildController))
			{
				guildController.OnReadPayload -= GuildController_OnReadPayload;
				guildController.OnReceiveGuildInvite -= GuildController_OnReceiveGuildInvite;
				guildController.OnAddGuildMember -= GuildController_OnAddGuildMember;
				guildController.OnValidateGuildMembers -= GuildController_OnValidateGuildMembers;
				guildController.OnRemoveGuildMember -= GuildController_OnRemoveGuildMember;
				guildController.OnLeaveGuild -= GuildController_OnLeaveGuild;
			}

			if (Character.TryGet(out PartyController partyController))
			{
				partyController.OnPartyCreated -= PartyController_OnPartyCreated;
				partyController.OnReceivePartyInvite -= PartyController_OnReceivePartyInvite;
				partyController.OnAddPartyMember -= PartyController_OnAddPartyMember;
				partyController.OnValidatePartyMembers -= PartyController_OnValidatePartyMembers;
				partyController.OnRemovePartyMember -= PartyController_OnRemovePartyMember;
				partyController.OnLeaveParty -= PartyController_OnLeaveParty;
			}
		}

		public void TargetController_OnClearTarget(Transform lastTarget)
		{
			Outline outline = lastTarget.GetComponent<Outline>();
			if (outline != null)
			{
				outline.enabled = false;
			}
			if (targetLabel != null)
			{
				LabelMaker.Cache(targetLabel);
			}
		}

		public void TargetController_OnNewTarget(Transform newTarget)
		{
			Vector3 newPos = newTarget.position;

			Collider collider = newTarget.GetComponent<Collider>();
			newPos.y += collider.bounds.extents.y + 0.15f;

			string label = newTarget.name;
			Color color = Color.grey;

			// apply merchant description
			Merchant merchant = newTarget.GetComponent<Merchant>();
			if (merchant != null &&
				merchant.Template != null)
			{
				label += "\r\n" + merchant.Template.Description;
				newPos.y += 0.15f;
				color = Color.white;
			}
			else
			{
				Banker banker = newTarget.GetComponent<Banker>();
				if (banker != null)
				{
					label += "\r\n<Banker>";
					newPos.y += 0.15f;
					color = Color.white;
				}
				else
				{
					AbilityCrafter abilityCrafter = newTarget.GetComponent<AbilityCrafter>();
					if (abilityCrafter != null)
					{
						label += "\r\n<Ability Crafter>";
						newPos.y += 0.15f;
						color = Color.white;
					}
				}

				targetLabel = LabelMaker.Display(label, newPos, color, 1.0f, 0.0f, true);

				Outline outline = newTarget.GetComponent<Outline>();
				if (outline != null)
				{
					outline.enabled = true;
				}
			}
		}

		public void AbilityController_OnReset()
		{
			if (UIManager.TryGet("UIAbilities", out UIAbilities uiAbilities) &&
				Character.IsOwner &&
				uiAbilities != null)
			{
				uiAbilities.ClearAllSlots();
			}
		}

		public void AbilityController_OnAddAbility(long abilityID, Ability ability)
		{
			if (UIManager.TryGet("UIAbilities", out UIAbilities uiAbilities) &&
				Character.IsOwner &&
				uiAbilities != null)
			{
				uiAbilities.AddAbility(ability.ID, ability);
			}
		}

		public void AbilityController_OnAddKnownAbility(long abilityID, BaseAbilityTemplate abilityTemplate)
		{
			if (UIManager.TryGet("UIAbilities", out UIAbilities uiAbilities) &&
				Character.IsOwner &&
				uiAbilities != null)
			{
				uiAbilities.AddKnownAbility(abilityID, abilityTemplate);
			}
		}

		public void FriendController_OnAddFriend(long friendID, bool online)
		{
			if (UIManager.TryGet("UIFriendList", out UIFriendList uiFriendList) &&
				Character.IsOwner &&
				uiFriendList != null)
			{
				uiFriendList.OnAddFriend(friendID, online);
			}
		}

		public void FriendController_OnRemoveFriend(long friendID)
		{
			if (UIManager.TryGet("UIFriendList", out UIFriendList uiFriendList))
			{
				uiFriendList.OnRemoveFriend(friendID);
			}
		}

		public void GuildController_OnReadPayload(long ID)
		{
			if (ID != 0)
			{
				// load the characters guild from disk or request it from the server
				ClientNamingSystem.SetName(NamingSystemType.GuildName, ID, (s) =>
				{
					Character.SetGuildName(s);
				});
			}
		}

		public void GuildController_OnReceiveGuildInvite(long inviterCharacterID)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s guild. Would you like to join?",
					() =>
					{
						Client.Broadcast(new GuildAcceptInviteBroadcast(), Channel.Reliable);
					},
					() =>
					{
						Client.Broadcast(new GuildDeclineInviteBroadcast(), Channel.Reliable);
					});
				}
			});
		}

		public void GuildController_OnAddGuildMember(long characterID, long guildID, GuildRank rank, string location)
		{
			if (UIManager.TryGet("UIGuild", out UIGuild uiGuild) &&
				Character.IsOwner &&
				uiGuild != null)
			{
				uiGuild.OnGuildAddMember(characterID, rank, location);

				ClientNamingSystem.SetName(NamingSystemType.GuildName, guildID, (s) =>
				{
					if (uiGuild.GuildLabel != null)
					{
						uiGuild.GuildLabel.text = s;
					}
				});
			}
		}

		public void GuildController_OnValidateGuildMembers(HashSet<long> newMembers)
		{
			if (UIManager.TryGet("UIGuild", out UIGuild uiGuild) &&
				Character.IsOwner &&
				uiGuild != null)
			{
				foreach (long id in new HashSet<long>(uiGuild.Members.Keys))
				{
					if (!newMembers.Contains(id))
					{
						GuildController_OnRemoveGuildMember(id);
					}
				}
			}
		}

		public void GuildController_OnRemoveGuildMember(long characterID)
		{
			if (UIManager.TryGet("UIGuild", out UIGuild uiGuild) &&
				Character.IsOwner &&
				uiGuild != null)
			{
				uiGuild.OnGuildRemoveMember(characterID);
			}
		}

		public void GuildController_OnLeaveGuild()
		{
			if (UIManager.TryGet("UIGuild", out UIGuild uiGuild) &&
				Character.IsOwner &&
				uiGuild != null)
			{
				uiGuild.OnLeaveGuild();
			}
		}

		public void PartyController_OnPartyCreated(string location)
		{
			if (UIManager.TryGet("UIParty", out UIParty uiParty) &&
				Character.IsOwner &&
				uiParty != null)
			{
				uiParty.OnPartyCreated(location);
			}
		}

		public void PartyController_OnReceivePartyInvite(long inviterCharacterID)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s party. Would you like to join?",
					() =>
					{
						Client.Broadcast(new PartyAcceptInviteBroadcast(), Channel.Reliable);
					},
					() =>
					{
						Client.Broadcast(new PartyDeclineInviteBroadcast(), Channel.Reliable);
					});
				}
			});
		}

		public void PartyController_OnAddPartyMember(long characterID, PartyRank rank, float healthPCT)
		{
			if (UIManager.TryGet("UIParty", out UIParty uiParty) &&
				Character.IsOwner &&
				uiParty != null)
			{
				uiParty.OnPartyAddMember(characterID, rank, healthPCT);
			}
		}

		public void PartyController_OnValidatePartyMembers(HashSet<long> newMembers)
		{
			if (UIManager.TryGet("UIParty", out UIParty uiParty) &&
				Character.IsOwner &&
				uiParty != null)
			{
				foreach (long id in new HashSet<long>(uiParty.Members.Keys))
				{
					if (!newMembers.Contains(id))
					{
						PartyController_OnRemovePartyMember(id);
					}
				}
			}
		}

		public void PartyController_OnRemovePartyMember(long characterID)
		{
			if (UIManager.TryGet("UIParty", out UIParty uiParty) &&
				Character.IsOwner &&
				uiParty != null)
			{
				uiParty.OnPartyRemoveMember(characterID);
			}
		}

		public void PartyController_OnLeaveParty()
		{
			if (UIManager.TryGet("UIParty", out UIParty uiParty) &&
				Character.IsOwner &&
				uiParty != null)
			{
				uiParty.OnLeaveParty();
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

		private bool CanUpdateInput()
		{
			return !InputManager.MouseMode;
		}

		public KCCInputReplicateData KCCPlayer_OnHandleCharacterInput()
		{
			// we can't change input if the UI is open or if the mouse cursor is enabled
			if (!CanUpdateInput())
			{
				return new KCCInputReplicateData(0.0f,
												 0.0f,
												 0,
												 Character.KCCPlayer.CharacterCamera.Transform.position,
												 Character.KCCPlayer.CharacterCamera.Transform.rotation);
			}

			int moveFlags = 0;
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
					Character.TryGet(out TargetController targetController))
				{
					Transform target = targetController.Current.Target;
					if (target != null)
					{
						IInteractable interactable = target.GetComponent<IInteractable>();
						if (interactable != null)
						{
							interactable.OnInteract(Character);
						}
					}
				}
				else if (CanUpdateInput())
				{
					if (InputManager.GetKeyDown(JumpInput) && !Character.CharacterController.IsJumping)
					{
						_jumpQueued = true;
					}

					_crouchInputActive = InputManager.GetKey(CrouchInput);

					_sprintInputActive = InputManager.GetKey(RunInput);
				}

				// UI windows should be able to open/close freely if the UI is not focused
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

			// Create the look input vector for the camera
			float mouseLookAxisUp = InputManager.GetAxis(MouseYInput);
			float mouseLookAxisRight = InputManager.GetAxis(MouseXInput);
			Vector3 lookInputVector = new Vector3(mouseLookAxisRight, mouseLookAxisUp, 0f);

			// Prevent moving the camera while the cursor isn't locked
			if (Cursor.lockState != CursorLockMode.Locked)
			{
				lookInputVector = Vector3.zero;
			}

			float scrollInput = 0.0f;
#if !UNITY_WEBGL
			if (CanUpdateInput())
			{
				// Input for zooming the camera (disabled in WebGL because it can cause problems)
				scrollInput = -InputManager.GetAxis(MouseScrollInput);
			}
#endif

			// Apply inputs to the camera
			Character.KCCPlayer.UpdateCamera(scrollInput, lookInputVector);

			// Handle toggling zoom level
			if (InputManager.GetKeyDown(ToggleFirstPersonInput))
			{
				Character.KCCPlayer.CharacterCamera.TargetDistance = (Character.KCCPlayer.CharacterCamera.TargetDistance == 0f) ? Character.KCCPlayer.CharacterCamera.DefaultDistance : 0f;
			}

			Character.KCCPlayer.SetOrientationMethod(Character.KCCPlayer.CharacterController.OrientationMethod);
		}
#endif
	}
}