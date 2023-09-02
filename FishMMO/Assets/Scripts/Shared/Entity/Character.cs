#if !UNITY_SERVER || UNITY_EDITOR
using FishMMO.Client;
#endif
using FishNet.Component.Prediction;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using KinematicCharacterController;
using KinematicCharacterController.Examples;
using UnityEngine;
using TMPro;
using System;
using Shared;

/// <summary>
/// Character contains references to all of the controllers associated with the character.
/// </summary>
#region KCC
[RequireComponent(typeof(PredictedObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(ExamplePlayer))]
[RequireComponent(typeof(ExampleCharacterController))]
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
public class Character : NetworkBehaviour, IPooledResettable
{
	public static Character localCharacter;

	public Transform Transform { get; private set; }

	public ExampleCharacterController CharacterController;
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
	public KinematicCharacterMotor Motor;
#if !UNITY_SERVER || UNITY_EDITOR
	public LocalInputController LocalInputController;
	public TextMeshPro CharacterNameLabel;
#endif
	// accountID for reference
	[SyncVar(Channel = Channel.Reliable)]
	public long ID;
	/// <summary>
	/// The characters real name. Use this if you are referencing a character by name. Avoid character.name unless you want the name of the game object.
	/// </summary>
	[SyncVar(Channel = Channel.Unreliable, OnChange = nameof(OnCharacterNameChanged))]
	public string CharacterName;
	private void OnCharacterNameChanged(string prev, string next, bool asServer)
	{
#if !UNITY_SERVER || UNITY_EDITOR
		gameObject.name = next;

		if (CharacterNameLabel != null)
			CharacterNameLabel.text = next;
#endif
	}
	public string Account;
	public bool IsGameMaster = false;
	public bool IsTeleporting = false;
	public int RaceID;
	public string RaceName;
	public string SceneName;
	public int SceneHandle;
	public string LastChatMessage = "";
	public DateTime NextChatMessageTime = DateTime.UtcNow;

	void Awake()
	{
		Transform = transform;

		CharacterController = gameObject.GetComponent<ExampleCharacterController>();
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
		BuffController = gameObject.GetComponent<BuffController>();
		BuffController.Character = this;
		QuestController = gameObject.GetComponent<QuestController>();
		QuestController.Character = this;
		GuildController = gameObject.GetComponent<GuildController>();
		GuildController.Character = this;
		PartyController = gameObject.GetComponent<PartyController>();
		PartyController.Character = this;
		Motor = gameObject.GetComponent<KinematicCharacterMotor>();
	}

	public override void OnStartClient()
	{
		base.OnStartClient();
		if (base.IsOwner)
		{
			localCharacter = this;

#if !UNITY_SERVER || UNITY_EDITOR
			LocalInputController = gameObject.AddComponent<LocalInputController>();
			if (UIManager.TryGet("UICastBar", out UICastBar uiCastBar))
			{
				AbilityController.OnUpdate += uiCastBar.OnUpdate;
				AbilityController.OnCancel += uiCastBar.OnCancel;
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
#if !UNITY_SERVER || UNITY_EDITOR
			if (LocalInputController != null)
			{
				Destroy(LocalInputController);
			}
			if (UIManager.TryGet("UICastBar", out UICastBar uiCastBar))
			{
				AbilityController.OnUpdate -= uiCastBar.OnUpdate;
				AbilityController.OnCancel -= uiCastBar.OnCancel;
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
		IsGameMaster = false;
		IsTeleporting = false;
		RaceID = 0;
		RaceName = "";
		SceneName = "";
		SceneHandle = 0;
		LastChatMessage = "";
		NextChatMessageTime = DateTime.UtcNow;
		if (Motor != null)
		{
			Motor.SetPositionAndRotationAndVelocity(Vector3.zero, Quaternion.identity, Vector3.zero);
		}
	}
}