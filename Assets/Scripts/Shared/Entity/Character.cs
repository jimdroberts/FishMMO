using Client;
using FishNet.Component.Prediction;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using KinematicCharacterController;
using KinematicCharacterController.Examples;
using UnityEngine;

/// <summary>
/// Character contains references to all the controllers associated with the character.
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
public class Character : NetworkBehaviour
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
	public PlayerInputController PlayerInputController;

	[SyncVar(Channel = Channel.Unreliable, OnChange = nameof(OnCharacterNameChanged))]
	public string characterName;
	private void OnCharacterNameChanged(string prev, string next, bool asServer)
	{
		if (!asServer)
		{
			gameObject.name = next;
		}
	}

	// accountName for reference
	public string account;
	public bool isGameMaster;
	public string raceName;
	public string sceneName;
	public int sceneHandle;

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
		//CharacterMovementController = gameObject.GetComponent<CharacterMovementController>();
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
			PlayerInputController = gameObject.AddComponent<PlayerInputController>();
		}
	}

	public override void OnStopClient()
	{
		base.OnStopClient();
		if (base.IsOwner)
		{
			localCharacter = null;

			if (PlayerInputController != null)
			{
				PlayerInputController.enabled = false;
			}
		}
	}
}