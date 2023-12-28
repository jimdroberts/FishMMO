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
	[RequireComponent(typeof(KinematicCharacterMotor))]
	[RequireComponent(typeof(KCCController))]
	[RequireComponent(typeof(KCCPlayer))]
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
		public Transform Transform { get; private set; }

		#region KCC
		public KinematicCharacterMotor Motor { get; private set; }
		public KCCController CharacterController { get; private set; }
		public KCCPlayer KCCPlayer { get; private set; }
		#endregion

		public CharacterAttributeController AttributeController { get; private set; }
		public CharacterDamageController DamageController { get; private set; }
		public TargetController TargetController { get; private set; }
		public CooldownController CooldownController { get; private set; }
		public InventoryController InventoryController { get; private set; }
		public EquipmentController EquipmentController { get; private set; }
		public AbilityController AbilityController { get; private set; }
		public AchievementController AchievementController { get; private set; }
		public BuffController BuffController { get; private set; }
		public QuestController QuestController { get; private set; }
		public GuildController GuildController { get; private set; }
		public PartyController PartyController { get; private set; }
		public FriendController FriendController { get; private set; }
#if !UNITY_SERVER
		public LocalInputController LocalInputController { get; private set; }
		public TextMeshPro CharacterNameLabel;
		public TextMeshPro CharacterGuildLabel;
#endif
		// accountID for reference
		[SyncVar(SendRate = 0.0f, Channel = Channel.Reliable, ReadPermissions = ReadPermission.Observers, WritePermissions = WritePermission.ServerOnly, OnChange = nameof(OnCharacterIDChanged))]
		public long ID;
		private void OnCharacterIDChanged(long prev, long next, bool asServer)
		{
#if !UNITY_SERVER
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, next, (n) =>
			{
				gameObject.name = n;
				CharacterName = n;
				CharacterNameLower = n.ToLower();

				if (CharacterNameLabel != null)
					CharacterNameLabel.text = n;
			});
#endif
		}

		/// <summary>
		/// The characters real name. Use this if you are referencing a character by name. Avoid character.name unless you want the name of the game object.
		/// </summary>
		public string CharacterName;
		public string CharacterNameLower;
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
		public DateTime NextInteractTime = DateTime.UtcNow;

		void Awake()
		{
			Transform = transform;

			#region KCC
			Motor = gameObject.GetComponent<KinematicCharacterMotor>();

			CharacterController = gameObject.GetComponent<KCCController>();
			CharacterController.Motor = Motor;
			Motor.CharacterController = CharacterController;

			KCCPlayer = gameObject.GetComponent<KCCPlayer>();
			KCCPlayer.CharacterController = CharacterController;
			KCCPlayer.Motor = Motor;
			#endregion

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
		}

#if !UNITY_SERVER
		public override void OnStartClient()
		{
			base.OnStartClient();
			if (base.IsOwner)
			{
				InitializeLocal(true);
			}
		}

		public override void OnStopClient()
		{
			base.OnStopClient();
			if (base.IsOwner)
			{
				InitializeLocal(false);
			}
		}

		private void InitializeLocal(bool initializing)
		{
			InputManager.MouseMode = false;

			LocalInputController = gameObject.GetComponent<LocalInputController>();
			if (LocalInputController == null)
			{
				LocalInputController = gameObject.AddComponent<LocalInputController>();
			}
			LocalInputController.Initialize(this);

			InitializeUI(initializing);
		}

		private void InitializeUI(bool initializing)
		{
			if (initializing)
			{
				UIManager.SetCharacter(this);

				if (TargetController != null &&
					UIManager.TryGet("UITarget", out UITarget uiTarget))
				{
					TargetController.OnChangeTarget += uiTarget.OnChangeTarget;
					TargetController.OnUpdateTarget += uiTarget.OnUpdateTarget;
				}
			}
			else
			{
				UIManager.SetCharacter(null);

				if (TargetController != null &&
					UIManager.TryGet("UITarget", out UITarget uiTarget))
				{
					TargetController.OnChangeTarget -= uiTarget.OnChangeTarget;
					TargetController.OnUpdateTarget -= uiTarget.OnUpdateTarget;
				}
			}
		}
#endif


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
			NextInteractTime = DateTime.UtcNow;
			Motor.SetPositionAndRotationAndVelocity(Vector3.zero, Quaternion.identity, Vector3.zero);
		}

		public void SetGuildName(string guildName)
		{
#if !UNITY_SERVER
			if (CharacterGuildLabel != null)
			{
				CharacterGuildLabel.text = !string.IsNullOrWhiteSpace(guildName) ? "[" + guildName + "]" : "";
			}
#endif
		}
	}
}