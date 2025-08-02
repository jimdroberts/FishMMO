using KinematicCharacterController;
using UnityEngine;
using System;
using FishNet.Connection;
using FishNet.Serializing;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a player-controlled character in the game world, with inventory, abilities, and networked state.
	/// Implements IPlayerCharacter and extends BaseCharacter with player-specific logic, hotkeys, and event-driven behaviour.
	/// </summary>
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
		/// <summary>
		/// The kinematic character motor for movement.
		/// </summary>
		public KinematicCharacterMotor Motor { get; private set; }
		/// <summary>
		/// The KCC controller for character movement logic.
		/// </summary>
		public KCCController CharacterController { get; private set; }
		/// <summary>
		/// The KCC player component for input and control.
		/// </summary>
		public KCCPlayer KCCPlayer { get; private set; }
		#endregion

#if !UNITY_SERVER
		/// <summary>
		/// The camera used for equipment view (e.g., inspecting gear).
		/// </summary>
		[SerializeField]
		private Camera equipmentViewCamera;
		/// <summary>
		/// Gets or sets the equipment view camera.
		/// </summary>
		public Camera EquipmentViewCamera { get { return this.equipmentViewCamera; } set { this.equipmentViewCamera = value; } }
#endif
		/// <summary>
		/// The character's display name (use this instead of GameObject.name).
		/// </summary>
		public new string Name { get { return CharacterName; } }
		/// <summary>
		/// The character's real name. Use this for referencing by name.
		/// </summary>
		public string CharacterName { get; set; }
		/// <summary>
		/// The lowercase version of the character's name, for case-insensitive operations.
		/// </summary>
		public string CharacterNameLower { get; set; }
		/// <summary>
		/// The account name associated with this character.
		/// </summary>
		public string Account { get; set; }
		/// <summary>
		/// The access level of the character (e.g., player, admin).
		/// </summary>
		public AccessLevel AccessLevel { get; set; }
		/// <summary>
		/// The UTC time when the character was created.
		/// </summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>
		/// The world server ID this character belongs to.
		/// </summary>
		public long WorldServerID { get; set; }
		/// <summary>
		/// The name of the teleporter the character is currently using.
		/// </summary>
		public string TeleporterName { get; set; }
		/// <summary>
		/// Whether the character is currently teleporting (true if TeleporterName is set).
		/// </summary>
		public override bool IsTeleporting { get { return !string.IsNullOrWhiteSpace(TeleporterName); } }
		/// <summary>
		/// The Race Template ID for the character object.
		/// </summary>
		public int RaceID { get; set; }
		/// <summary>
		/// The model index for the character's race.
		/// </summary>
		public int ModelIndex { get; set; }
		/// <summary>
		/// The name of the character's race.
		/// </summary>
		public string RaceName { get; set; }
		/// <summary>
		/// The name of the bind scene for respawn.
		/// </summary>
		public string BindScene { get; set; }
		/// <summary>
		/// The position in the bind scene for respawn.
		/// </summary>
		public Vector3 BindPosition { get; set; }
		/// <summary>
		/// The name of the current scene.
		/// </summary>
		public string SceneName { get; set; }
		/// <summary>
		/// The handle of the current scene.
		/// </summary>
		public int SceneHandle { get; set; }
		/// <summary>
		/// The instance ID for instanced content.
		/// </summary>
		public long InstanceID { get; set; }
		/// <summary>
		/// The name of the instance scene.
		/// </summary>
		public string InstanceSceneName { get; set; }
		/// <summary>
		/// The handle of the instance scene.
		/// </summary>
		public int InstanceSceneHandle { get; set; }
		/// <summary>
		/// The position in the instance scene.
		/// </summary>
		public Vector3 InstancePosition { get; set; }
		/// <summary>
		/// The rotation in the instance scene.
		/// </summary>
		public Quaternion InstanceRotation { get; set; }
		/// <summary>
		/// Returns true if the character is in an instance and the instance scene name is set.
		/// </summary>
		public bool IsInInstance() { return Flags.IsFlagged(CharacterFlags.IsInInstance) && !string.IsNullOrWhiteSpace(InstanceSceneName); }
		/// <summary>
		/// The last chat message sent by the character.
		/// </summary>
		public string LastChatMessage { get; set; }
		/// <summary>
		/// The next UTC time the character can send a chat message.
		/// </summary>
		public DateTime NextChatMessageTime { get; set; }
		/// <summary>
		/// The next UTC time the character can interact.
		/// </summary>
		public DateTime NextInteractTime { get; set; }
		/// <summary>
		/// The list of hotkey data for the character.
		/// </summary>
		public List<HotkeyData> Hotkeys { get; set; }

		/// <summary>
		/// Initializes the player character and its hotkeys. Sets up KCC movement components and resets chat/interact timers.
		/// </summary>
		public override void OnAwake()
		{
			Hotkeys = new List<HotkeyData>();
			ResetHotkeys();

			#region KCC
			Motor = gameObject.GetComponent<KinematicCharacterMotor>();
			CharacterController = gameObject.GetComponent<KCCController>();
			if (CharacterController != null)
			{
				// Link the KCC controller to this character instance.
				CharacterController.Character = this;
			}
			KCCPlayer = gameObject.GetComponent<KCCPlayer>();
			#endregion

			// Initialize chat and interaction timers to current UTC time.
			NextChatMessageTime = DateTime.UtcNow;
			NextInteractTime = DateTime.UtcNow;
		}

		/// <summary>
		/// Resets the hotkeys to the default configuration. Initializes each hotkey slot.
		/// </summary>
		public void ResetHotkeys()
		{
			Hotkeys.Clear();
			for (int i = 0; i < Constants.Configuration.MaximumPlayerHotkeys; ++i)
			{
				HotkeyData data = new HotkeyData()
				{
					Slot = i,
				};
				Hotkeys.Add(data);
			}
		}

		/// <summary>
		/// Reads the character's payload from the network connection and updates core properties.
		/// Invokes OnReadPayload event and instantiates the race model on the client.
		/// </summary>
		/// <param name="connection">The network connection to read from.</param>
		/// <param name="reader">The reader containing serialized data.</param>
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

		/// <summary>
		/// Writes the character's payload to the network connection for serialization.
		/// </summary>
		/// <param name="connection">The network connection to write to.</param>
		/// <param name="writer">The writer to serialize data into.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
			writer.WriteInt32(RaceID);
			writer.WriteInt32(ModelIndex);
			writer.WriteString(RaceName);
			writer.WriteString(SceneName);
		}

#if !UNITY_SERVER

		/// <summary>
		/// Called when the client starts. Invokes OnStartLocalClient and starts all character behaviours for the local player.
		/// </summary>
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

		/// <summary>
		/// Called when the client stops. Invokes OnStopLocalClient and stops all character behaviours for the local player.
		/// </summary>
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

		/// <summary>
		/// Resets the character's state, including teleportation, chat/interact timers, instance data, and hotkeys.
		/// </summary>
		/// <param name="asServer">True if called on the server, false if on the client.</param>
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

		/// <summary>
		/// Teleports the character to the specified teleporter name and invokes the teleport event.
		/// </summary>
		/// <param name="teleporterName">The name of the teleporter to use.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Teleport(string teleporterName)
		{
			TeleporterName = teleporterName;

			IPlayerCharacter.OnTeleport?.Invoke(this);
		}

		/// <summary>
		/// Sets the character's guild name label (client only). Updates the label text if available.
		/// </summary>
		/// <param name="guildName">The name of the guild to set.</param>
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