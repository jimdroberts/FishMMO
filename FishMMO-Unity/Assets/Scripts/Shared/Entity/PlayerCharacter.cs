#if !UNITY_SERVER
using TMPro;
#endif
using KinematicCharacterController;
using UnityEngine;
using System;
using FishNet.Connection;
using FishNet.Object.Synchronizing;
using FishNet.Serializing;

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
		private TextMeshPro characterNameLabel;
		public TextMeshPro CharacterNameLabel { get { return this.characterNameLabel; } set { this.characterNameLabel = value; } }
		[SerializeField]
		private TextMeshPro characterGuildLabel;
		public TextMeshPro CharacterGuildLabel { get { return this.characterGuildLabel; } set { this.characterGuildLabel = value; } }
		[SerializeField]
		private Camera equipmentViewCamera;
		public Camera EquipmentViewCamera { get { return this.equipmentViewCamera; } set { this.equipmentViewCamera = value; } }
#endif

		/// <summary>
		/// The characters real name. Use this if you are referencing a character by name. Avoid character.name unless you want the name of the game object.
		/// </summary>
		public string CharacterName { get; set; }
		public string CharacterNameLower { get; set; }
		public long WorldServerID { get; set; }
		public string Account { get; set; }
		public AccessLevel AccessLevel { get; set; }
		public string TeleporterName { get; set; }
		public override bool IsTeleporting { get { return !string.IsNullOrWhiteSpace(TeleporterName); } }
		/// <summary>
		/// The prefab ID for the character object.
		/// </summary>
		public int RaceID { get; set; }
		public string RaceName { get; set; }
		public string SceneName { get; set; }
		public int SceneHandle { get; set; }
		public string LastChatMessage { get; set; }
		public DateTime NextChatMessageTime { get; set; }
		public DateTime NextInteractTime { get; set; }

		public override void OnAwake()
		{
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

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			RaceID = reader.ReadInt32();
			RaceName = reader.ReadString();
			SceneName = reader.ReadString();

#if !UNITY_SERVER
			IPlayerCharacter.OnReadPayload?.Invoke(this);
#endif
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
			writer.WriteInt32(RaceID);
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

				gameObject.layer = Constants.Layers.LocalEntity;

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

				gameObject.layer = Constants.Layers.Default;
			}
		}
#endif

		internal void SetSyncVarDatabaseValue<T>(SyncVar<T> syncVar, T value)
		{
			syncVar.Value = value;
			syncVar.SetInitialValues(value);
		}

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			TeleporterName = "";
			LastChatMessage = "";
			NextChatMessageTime = DateTime.UtcNow;
			NextInteractTime = DateTime.UtcNow;
		}

		public void Teleport(string teleporterName)
		{
			TeleporterName = teleporterName;

#if UNITY_SERVER
			// just disconnect
			Owner.Disconnect(false);
#endif
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