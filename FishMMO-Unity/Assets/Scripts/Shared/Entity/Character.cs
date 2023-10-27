#if !UNITY_SERVER
using FishMMO.Client;
using TMPro;
#endif
using FishNet.Component.Prediction;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using KinematicCharacterController;
using UnityEngine;
using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character contains references to all of the controllers associated with the character.
	/// </summary>
	#region KCC
	[RequireComponent(typeof(PredictedObject))]
	[RequireComponent(typeof(NetworkTransform))]
	[RequireComponent(typeof(Rigidbody))]
	[RequireComponent(typeof(KCCPlayer))]
	[RequireComponent(typeof(KCCController))]
	[RequireComponent(typeof(KinematicCharacterMotor))]
	#endregion
	[RequireComponent(typeof(CharacterAttributeController))]
	[RequireComponent(typeof(TargetController))]
	[RequireComponent(typeof(CooldownController))]
	[RequireComponent(typeof(InventoryController))]
	[RequireComponent(typeof(EquipmentController))]
	[RequireComponent(typeof(AbilityController))]
	[RequireComponent(typeof(AchievementController))]
	[RequireComponent(typeof(BuffController))]
	[RequireComponent(typeof(QuestController))]
	[RequireComponent(typeof(CharacterDamageController))]
	[RequireComponent(typeof(GuildController))]
	[RequireComponent(typeof(PartyController))]
	[RequireComponent(typeof(FriendController))]
	public class Character : NetworkBehaviour, IPooledResettable
	{
		public static Character localCharacter;

		public Transform Transform { get; private set; }

		public KCCController CharacterController;
		public CharacterAttributeController AttributeController;
		public CharacterDamageController DamageController;
		public TargetController TargetController;
		public CooldownController CooldownController;
		public InventoryController InventoryController;
		public EquipmentController EquipmentController;
		public AbilityController AbilityController;
		public AchievementController AchievementController;
		public BuffController BuffController;
		public QuestController QuestController;
		public GuildController GuildController;
		public PartyController PartyController;
		public FriendController FriendController;
		public KinematicCharacterMotor Motor;
#if !UNITY_SERVER
		public LocalInputController LocalInputController;
		public TextMeshPro CharacterNameLabel;
		public LabelMaker LabelMaker;
#endif
		// accountID for reference
		[SyncVar(Channel = Channel.Reliable, ReadPermissions = ReadPermission.Observers, WritePermissions = WritePermission.ServerOnly)]
		public long ID;
		/// <summary>
		/// The characters real name. Use this if you are referencing a character by name. Avoid character.name unless you want the name of the game object.
		/// </summary>
		[SyncVar(Channel = Channel.Unreliable, ReadPermissions = ReadPermission.Observers, WritePermissions = WritePermission.ServerOnly, OnChange = nameof(OnCharacterNameChanged))]
		public string CharacterName;
		public string CharacterNameLower;
		private void OnCharacterNameChanged(string prev, string next, bool asServer)
		{
			gameObject.name = next;
			CharacterNameLower = next.ToLower();

#if !UNITY_SERVER
			if (CharacterNameLabel != null)
				CharacterNameLabel.text = next;
#endif
		}
		public string Account;
		public long WorldServerID;
		public AccessLevel AccessLevel = AccessLevel.Player;
		public bool IsTeleporting = false;
		public int RaceID;
		[SyncVar(Channel = Channel.Unreliable, ReadPermissions = ReadPermission.OwnerOnly, WritePermissions = WritePermission.ServerOnly)]
		public string RaceName;
		[SyncVar(Channel = Channel.Unreliable, ReadPermissions = ReadPermission.OwnerOnly, WritePermissions = WritePermission.ServerOnly)]
		public string SceneName;
		public int SceneHandle;
		public string LastChatMessage = "";
		public DateTime NextChatMessageTime = DateTime.UtcNow;

		void Awake()
		{
			Transform = transform;

			CharacterController = gameObject.GetComponent<KCCController>();
			AttributeController = gameObject.GetComponent<CharacterAttributeController>();
			DamageController = gameObject.GetComponent<CharacterDamageController>();
			DamageController.Character = this;
			TargetController = gameObject.GetComponent<TargetController>();
			TargetController.Character = this;
			CooldownController = gameObject.GetComponent<CooldownController>();
			InventoryController = gameObject.GetComponent<InventoryController>();
			InventoryController.Character = this;
			EquipmentController = gameObject.GetComponent<EquipmentController>();
			EquipmentController.Character = this;
			AbilityController = gameObject.GetComponent<AbilityController>();
			AbilityController.Character = this;
			AchievementController = gameObject.GetComponent<AchievementController>();
			AchievementController.Character = this;
			BuffController = gameObject.GetComponent<BuffController>();
			BuffController.Character = this;
			QuestController = gameObject.GetComponent<QuestController>();
			QuestController.Character = this;
			GuildController = gameObject.GetComponent<GuildController>();
			GuildController.Character = this;
			PartyController = gameObject.GetComponent<PartyController>();
			PartyController.Character = this;
			FriendController = gameObject.GetComponent<FriendController>();
			FriendController.Character = this;
			Motor = gameObject.GetComponent<KinematicCharacterMotor>();
		}

		public override void OnStartClient()
		{
			base.OnStartClient();
			if (base.IsOwner)
			{
				localCharacter = this;

#if !UNITY_SERVER
				InputManager.MouseMode = false;

				LocalInputController = gameObject.GetComponent<LocalInputController>();
				if (LocalInputController == null)
				{
					LocalInputController = gameObject.AddComponent<LocalInputController>();
				}
				LabelMaker = gameObject.GetComponent<LabelMaker>();

				if (TargetController != null &&
					UIManager.TryGet("UITarget", out UITarget uiTarget))
				{
					TargetController.OnChangeTarget += uiTarget.OnChangeTarget;
					TargetController.OnUpdateTarget += uiTarget.OnUpdateTarget;
				}
#endif
			}
		}

		public override void OnStopClient()
		{
			base.OnStopClient();
			if (base.IsOwner)
			{
				localCharacter = null;

#if !UNITY_SERVER
				if (LocalInputController != null)
				{
					Destroy(LocalInputController);
				}

				if (TargetController != null &&
					UIManager.TryGet("UITarget", out UITarget uiTarget))
				{
					TargetController.OnChangeTarget -= uiTarget.OnChangeTarget;
					TargetController.OnUpdateTarget += uiTarget.OnUpdateTarget;
				}
#endif
			}
		}

		/// <summary>
		/// Resets the Character values to default for pooling.
		/// </summary>
		public void OnPooledReset()
		{
			ID = -1;
			CharacterName = "";
			Account = "";
			WorldServerID = 0;
			AccessLevel = AccessLevel.Player;
			IsTeleporting = false;
			RaceID = 0;
			RaceName = "";
			SceneName = "";
			SceneHandle = 0;
			LastChatMessage = "";
			NextChatMessageTime = DateTime.UtcNow;
			Motor.SetPositionAndRotationAndVelocity(Vector3.zero, Quaternion.identity, Vector3.zero);
		}
	}
}