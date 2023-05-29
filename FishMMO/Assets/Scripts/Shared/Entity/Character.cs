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
[RequireComponent(typeof(CharacterDeathController))]
[RequireComponent(typeof(GuildController))]
[RequireComponent(typeof(PartyController))]
public class Character : NetworkBehaviour, IPooledResettable
{
	public static Character localCharacter;

	public CharacterAttributeController AttributeController;
	public CharacterDamageController DamageController;
	public CharacterDeathController DeathController;
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
	public Transform AbilitySpawnPoint;
#if !UNITY_SERVER || UNITY_EDITOR
	public LocalInputController LocalInputController;

	//temporary
	public UILabel3D CharacterNameLabel;
#endif

	/// <summary>
	/// The characters real name. Use this if you are referencing a character by name. Avoid character.name unless you want the name of the game object.
	/// </summary>
	[SyncVar(Channel = Channel.Unreliable, OnChange = nameof(OnCharacterNameChanged))]
	public string characterName;
	private void OnCharacterNameChanged(string prev, string next, bool asServer)
	{
#if !UNITY_SERVER || UNITY_EDITOR
		gameObject.name = next;

		if (CharacterNameLabel == null)
		{
			float calcHeight = 100.0f;
			if (Motor != null && Motor.Capsule != null)
			{
				calcHeight *= Motor.Capsule.height * 0.75f;
			}
			CharacterNameLabel = UILabel3D.Create(next, 12, transform, new Vector2(0.0f, calcHeight));
		}
		else
		{
			CharacterNameLabel.Text = next;
		}
#endif
	}

	// accountName for reference
	[SyncVar(Channel = Channel.Reliable)]
	public long id;
	public string account;
	public bool isGameMaster = false;
	public bool isTeleporting = false;
	public int raceID;
	public string raceName;
	public string sceneName;
	public int sceneHandle;
	public string lastChatMessage = "";
	public DateTime nextChatMessageTime = DateTime.UtcNow;

	void Awake()
	{
		AttributeController = gameObject.GetComponent<CharacterAttributeController>();
		DamageController = gameObject.GetComponent<CharacterDamageController>();
		DamageController.character = this;
		DeathController = gameObject.GetComponent<CharacterDeathController>();
		DeathController.character = this;
		TargetController = gameObject.GetComponent<TargetController>();
		TargetController.character = this;
		CooldownController = gameObject.GetComponent<CooldownController>();
		InventoryController = gameObject.GetComponent<InventoryController>();
		InventoryController.character = this;
		EquipmentController = gameObject.GetComponent<EquipmentController>();
		EquipmentController.character = this;
		AbilityController = gameObject.GetComponent<AbilityController>();
		AbilityController.character = this;
		AchievementController = gameObject.GetComponent<AchievementController>();
		BuffController = gameObject.GetComponent<BuffController>();
		BuffController.character = this;
		QuestController = gameObject.GetComponent<QuestController>();
		QuestController.character = this;
		GuildController = gameObject.GetComponent<GuildController>();
		GuildController.character = this;
		PartyController = gameObject.GetComponent<PartyController>();
		PartyController.character = this;
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
#endif
		}
	}

	/// <summary>
	/// Resets the Character values to default for pooling.
	/// </summary>
	public void OnPooledReset()
	{
		id = -1;
		characterName = "";
		account = "";
		isGameMaster = false;
		isTeleporting = false;
		raceID = 0;
		raceName = "";
		sceneName = "";
		sceneHandle = 0;
		lastChatMessage = "";
		nextChatMessageTime = DateTime.UtcNow;
		if (Motor != null)
		{
			Motor.SetPositionAndRotationAndVelocity(Vector3.zero, Quaternion.identity, Vector3.zero);
		}
	}
}