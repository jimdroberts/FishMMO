using KinematicCharacterController;
using UnityEngine;
using System;
using FishNet.Connection;
using FishNet.Serializing;
using System.Runtime.CompilerServices;
using System.Collections.Generic;


namespace FishMMO.Shared
{
	[RequireComponent(typeof(CharacterAttributeController))]
	[RequireComponent(typeof(TargetController))]
	[RequireComponent(typeof(CooldownController))]
	[RequireComponent(typeof(InventoryController))]
	[RequireComponent(typeof(EquipmentController))]
	[RequireComponent(typeof(BankController))]
	[RequireComponent(typeof(AbilityController))]
	[RequireComponent(typeof(AchievementController))]
	[RequireComponent(typeof(BuffController))]
	[RequireComponent(typeof(QuestController))]
	[RequireComponent(typeof(CharacterDamageController))]
	[RequireComponent(typeof(GuildController))]
	[RequireComponent(typeof(PartyController))]
	[RequireComponent(typeof(FriendController))]
	[RequireComponent(typeof(FactionController))]
	public class PlayerCharacter : BaseCharacter, IPlayerCharacter
	{
		#region KCC
		public KinematicCharacterMotor Motor { get; private set; }
		public KCCController CharacterController { get; private set; }
		public KCCPlayer KCCPlayer { get; private set; }
		#endregion

#if !UNITY_SERVER
		[SerializeField]
		private Camera equipmentViewCamera;
		public Camera EquipmentViewCamera { get { return this.equipmentViewCamera; } set { this.equipmentViewCamera = value; } }
#endif

		/// <summary>
		/// The characters real name. Use this if you are referencing a character by name. Avoid character.name unless you want the name of the game object.
		/// </summary>
		public string CharacterName { get; set; }
		public string CharacterNameLower { get; set; }
		public string Account { get; set; }
		public AccessLevel AccessLevel { get; set; }
		public DateTime TimeCreated { get; set; }
		public long WorldServerID { get; set; }
		public string TeleporterName { get; set; }
		public override bool IsTeleporting { get { return !string.IsNullOrWhiteSpace(TeleporterName); } }
		/// <summary>
		/// The Race Template ID for the character object.
		/// </summary>
		public int RaceID { get; set; }
		public int ModelIndex { get; set; }
		public string RaceName { get; set; }
		public int ModelIndex { get; set; }
		public string BindScene { get; set; }
		public Vector3 BindPosition { get; set; }
		public string SceneName { get; set; }
		public int SceneHandle { get; set; }
		public long InstanceID { get; set; }
		public string InstanceSceneName { get; set; }
		public int InstanceSceneHandle { get; set; }
		public Vector3 InstancePosition { get; set; }
		public Quaternion InstanceRotation { get; set; }
		public bool IsInInstance() { return Flags.IsFlagged(CharacterFlags.IsInInstance) && !string.IsNullOrWhiteSpace(InstanceSceneName); }
		public string LastChatMessage { get; set; }
		public DateTime NextChatMessageTime { get; set; }
		public DateTime NextInteractTime { get; set; }
		public List<HotkeyData> Hotkeys { get; set; }

		public override void OnAwake()
		{
			Hotkeys = new List<HotkeyData>();
			ResetHotkeys();

			#region KCC
			Motor = gameObject.GetComponent<KinematicCharacterMotor>();
			CharacterController = gameObject.GetComponent<KCCController>();
			if (CharacterController != null)
			{
				CharacterController.Character = this;
			}
			KCCPlayer = gameObject.GetComponent<KCCPlayer>();
			#endregion

			NextChatMessageTime = DateTime.UtcNow;
			NextInteractTime = DateTime.UtcNow;
		}

		public void ResetHotkeys()
		{
			Hotkeys.Clear();
			for (int i = 0; i < Constants.Configuration.MaximumPlayerHotkeys; ++i)
			{
				Hotkeys.Add(new HotkeyData()
				{
					Slot = i,
				});
			}
		}

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			RaceID = reader.ReadInt32();
			ModelIndex = reader.ReadInt32();
			RaceName = reader.ReadStringAllocated();
			SceneName = reader.ReadStringAllocated();

#if !UNITY_SERVER
			IPlayerCharacter.OnReadPayload?.Invoke(this);

			RaceTemplate raceTemplate = RaceTemplate.Get<RaceTemplate>(RaceID);
			if (raceTemplate != null)
			{
				InstantiateRaceModelFromIndex(raceTemplate, ModelIndex);
			}
#endif
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
			writer.WriteInt32(RaceID);
			writer.WriteInt32(ModelIndex);
			writer.WriteString(RaceName);
			writer.WriteString(SceneName);
		}

#if !UNITY_SERVER

		public override void OnStartClient()
		{
			base.OnStartClient();

			if (base.IsOwner)
			{
				IPlayerCharacter.OnStartLocalClient?.Invoke(this);

				foreach (ICharacterBehaviour behaviour in this.Behaviours.Values)
				{
					behaviour.OnStartCharacter();
				}
			}
		}

		public override void OnStopClient()
		{
			base.OnStopClient();
			if (base.IsOwner)
			{
				foreach (ICharacterBehaviour behaviour in this.Behaviours.Values)
				{
					behaviour.OnStopCharacter();
				}

				IPlayerCharacter.OnStopLocalClient?.Invoke(this);
			}
		}
#endif

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			TeleporterName = "";
			LastChatMessage = "";
			NextChatMessageTime = DateTime.UtcNow;
			NextInteractTime = DateTime.UtcNow;
			InstanceSceneName = null;
			InstanceSceneHandle = 0;
			InstancePosition = Vector3.zero;
			InstanceRotation = Quaternion.identity;

			ResetHotkeys();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Teleport(string teleporterName)
		{
			TeleporterName = teleporterName;

			IPlayerCharacter.OnTeleport?.Invoke(this);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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