using KinematicCharacterController;
using FishMMO.Auth.Core;
using UnityEngine;
using System;
using FishNet.Connection;
using FishNet.Serializing;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a player-controlled character in the game world, with inventory, abilities, and networked state.
	/// Implements IPlayerCharacter and extends BaseCharacter with player-specific logic, hotkeys, and event-driven behaviour.
	///
	/// <para><b>Coupling note:</b> This class uses 21 <see cref="RequireComponent"/> attributes, creating strong
	/// coupling between <see cref="PlayerCharacter"/> and its component dependencies. All required components
	/// are tightly bound to this class and cannot be removed/replaced without modifying this declaration block.
	/// If a more modular composition approach is desired in the future (e.g., optional components registered
	/// at runtime), this set of RequireComponent attributes would need to be refactored into a dynamic
	/// registration system.</para>
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
	[RequireComponent(typeof(WaypointController))]
	[RequireComponent(typeof(ArchetypeController))]
	[RequireComponent(typeof(CharacterPredictionController))]
	[RequireComponent(typeof(BodyVisibilityManager))]
	[RequireComponent(typeof(CharacterAppearanceManager))]
	[RequireComponent(typeof(EquipmentVisualController))]
	[RequireComponent(typeof(CharacterAnimationController))]
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

		/// <summary>
		/// The camera used for equipment view (e.g., inspecting gear).
		/// </summary>
		[SerializeField]
		private Camera equipmentViewCamera;
		/// <summary>
		/// Gets or sets the equipment view camera.
		/// </summary>
		public Camera EquipmentViewCamera { get { return this.equipmentViewCamera; } set { this.equipmentViewCamera = value; } }

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
		/// The current version of the character data, used for optimistic concurrency control when saving/loading from the database.
		/// </summary>
		public long Version { get; set; }
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
		/// <inheritdoc />
		/// <remarks>
		/// Two dictionary lookups in the template cache, so it is not worth a cached field — and a
		/// cached field would have to be invalidated whenever <see cref="RaceID"/> moved, which is
		/// exactly the kind of second copy this property exists to avoid.
		/// </remarks>
		public RaceTemplate RaceTemplate
		{
			// Fully qualified: inside this accessor the bare name resolves to the property itself.
			get { return RaceID == 0 ? null : FishMMO.Shared.RaceTemplate.Get<FishMMO.Shared.RaceTemplate>(RaceID); }
		}
		/// <inheritdoc />
		public string RaceName { get { return RaceTemplate?.Name; } }
		/// <summary>
		/// The model index for the character's race.
		/// </summary>
		public int ModelIndex { get; set; }
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
		/// <inheritdoc />
		public long SceneHandle { get; set; }
		/// <summary>
		/// The instance ID for instanced content.
		/// </summary>
		public long InstanceID { get; set; }
		/// <summary>
		/// The name of the instance scene.
		/// </summary>
		public string InstanceSceneName { get; set; }
		/// <inheritdoc />
		public long InstanceSceneHandle { get; set; }
		/// <summary>
		/// The position in the instance scene.
		/// </summary>
		public Vector3 InstancePosition { get; set; }
		/// <summary>
		/// The rotation in the instance scene.
		/// </summary>
		public Quaternion InstanceRotation { get; set; }
		/// <inheritdoc />
		public Vector3 LastWorldPosition { get; set; }
		/// <inheritdoc />
		public Quaternion LastWorldRotation { get; set; } = Quaternion.identity;
		/// <summary>
		/// Returns true if the character is in an instance and the instance scene name is set.
		/// </summary>
		public bool IsInInstance() { return Flags.IsFlagged(CharacterFlags.IsInInstance) && !string.IsNullOrWhiteSpace(InstanceSceneName); }
		/// <inheritdoc />
		/// <remarks>
		/// The instance name is tested for content as well as the flag, so a character flagged into
		/// an instance whose name has not arrived yet answers with the world scene rather than with
		/// an empty string. Every caller uses the answer to look a scene up, and an empty key finds
		/// nothing — which presents as a refusal rather than as the missing value it is.
		/// </remarks>
		public string CurrentSceneName()
		{
			return IsInInstance() && !string.IsNullOrEmpty(InstanceSceneName)
				? InstanceSceneName
				: SceneName;
		}
		/// <summary>
		/// Returns true if the character is currently loaded in the scene and active.
		/// </summary>
		public bool IsLoaded() { return Flags.IsFlagged(CharacterFlags.IsLoaded); }
		/// <summary>
		/// The last chat message sent by the character.
		/// </summary>
		public string LastChatMessage { get; set; }
		/// <summary>
		/// The next UTC ticks when the character can send a chat message.
		/// </summary>
		public long NextChatMessageTicks { get; set; }
		/// <summary>
		/// Current number of chat tokens available in the token bucket (double for precision on long-uptime servers).
		/// Server-side anti-spam: consumed per message, refilled at a configurable rate.
		/// </summary>
		public double ChatTokens { get; set; }
		/// <summary>
		/// Indicates whether ChatTokens is in its initial "filled to capacity" state (no refill applied yet).
		/// Used instead of <c>double.MaxValue</c> as a sentinel to avoid overflow to Infinity on first add.
		/// </summary>
		public bool IsChatTokensFull { get; set; }
		/// <summary>
		/// UTC ticks of the last token bucket refill.
		/// </summary>
		public long ChatTokenLastRefillTicks { get; set; }
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
			long nowTicks = DateTime.UtcNow.Ticks;
			NextChatMessageTicks = nowTicks;
			ChatTokens = 0;
			IsChatTokensFull = true; // Filled to max; server overrides with actual capacity on first message.
			ChatTokenLastRefillTicks = nowTicks;
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
					ReferenceID = HotkeyData.UnsetReferenceID,
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
			/* Unpacked. Template ids are a deterministic 32-bit hash
			 * (CachedScriptableObject.AddToCache), so they span the whole range and the
			 * signed-packed form spends FIVE bytes on one. See ObservedBuffEntry. */
			RaceID = reader.ReadInt32Unpacked();
			ModelIndex = reader.ReadInt32();
			Flags = reader.ReadInt32();

			/* Which shape follows — see WritePayload. Only the OWNER is told where it is standing
			 * and what it may do; an observer needs neither and used to receive both. */
			byte shape = reader.ReadUInt8Unpacked();
			if ((shape & PAYLOAD_SHAPE_OWNER) != 0)
			{
				SceneName = reader.ReadStringAllocated();
				/* Written empty when not in an instance. The client map keys everything by the scene
				 * the character is standing in (CurrentSceneName), which inside an instance is this
				 * and not SceneName; without it the map drew the open-world scene from inside a dungeon
				 * and every fast-travel request from there was refused as NotInScene. */
				string instanceSceneName = reader.ReadStringAllocated();
				InstanceSceneName = string.IsNullOrEmpty(instanceSceneName) ? null : instanceSceneName;
				AccessLevel = (AccessLevel)reader.ReadUInt8Unpacked();
			}

#if !UNITY_SERVER
			ClientCharacters[ID] = this;

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
		/// <remarks>
		/// <para>
		/// Two shapes behind a shape byte, chosen by the receiver, in the same style as the ability,
		/// buff, attribute and waypoint controllers. Everything an observer needs to DRAW a peer is
		/// unconditional; the three fields that describe where that peer is and what it is allowed
		/// to do are the owner's alone.
		/// </para>
		/// <para>
		/// <c>SceneName</c> and <c>InstanceSceneName</c> are read only through
		/// <see cref="CurrentSceneName"/>, and the only client that calls it
		/// calls it on the LOCAL character (<c>ClientMapSystem</c>). Every observer of every peer
		/// was being sent two scene-name strings it had no reader for — and an observer is by
		/// definition already in that scene.
		/// </para>
		/// <para>
		/// <c>AccessLevel</c> has no client reader at all: the only thing that tests it is
		/// <c>ChatHelper.TryParseCommand</c>, whose sole caller is the server's chat system. Sending
		/// it told every observer which characters were game masters, for a byte nobody spent.
		/// </para>
		/// <para>
		/// <c>RaceName</c> used to travel here and is gone entirely. It was never assigned anywhere
		/// in the tree, so the wire value was always null, and a race name is derivable from
		/// <see cref="RaceID"/> through the template cache — which is exactly what the guild UI
		/// already does.
		/// </para>
		/// </remarks>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
			/* Unpacked — see ReadPayload. */
			writer.WriteInt32Unpacked(RaceID);
			writer.WriteInt32(ModelIndex);
			writer.WriteInt32(Flags);

			bool isOwner = PayloadVisibility.IsOwner(this, connection);
			writer.WriteUInt8Unpacked(isOwner ? PAYLOAD_SHAPE_OWNER : (byte)0);

			if (isOwner)
			{
				writer.WriteString(SceneName);
				writer.WriteString(InstanceSceneName ?? string.Empty);
				writer.WriteUInt8Unpacked((byte)AccessLevel);
			}
		}

		/// <summary>Shape flag: the payload carries the owner-only block.</summary>
		private const byte PAYLOAD_SHAPE_OWNER = 0x01;

#if !UNITY_SERVER

		/// <summary>True once local client initialization has completed (idempotent).</summary>
		private bool localClientInitialized;

		/// <summary>
		/// Called when the client starts or gains ownership. Idempotent — only
		/// fires OnStartLocalClient and behaviour start once, even if ownership
		/// is applied after OnStartClient (race condition on fast spawns).
		/// </summary>
		private void TryInitializeLocalClient()
		{
			if (localClientInitialized || !base.IsOwner) return;
			localClientInitialized = true;

			IPlayerCharacter.OnStartLocalClient?.Invoke(this);

			foreach (ICharacterBehaviour behaviour in this.Behaviours.Values)
			{
				behaviour.OnStartCharacter();
			}
		}

		/// <inheritdoc/>
		public override void OnStartClient()
		{
			base.OnStartClient();
			TryInitializeLocalClient();
		}

		/// <inheritdoc/>
		public override void OnOwnershipClient(NetworkConnection prevOwner)
		{
			base.OnOwnershipClient(prevOwner);
			TryInitializeLocalClient();
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
		/// This is called when the character is despawned or removed from the world.
		/// </summary>
		/// <param name="asServer">True if called on the server, false if on the client.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			/* Safe to clear, unlike an NPC's — see BaseCharacter.ResetState. A player's ID is a
			 * database key written fresh by the load path on every spawn, and the base has
			 * already used it to prune the client character dictionary. */
			ID = 0;

#if !UNITY_SERVER
			/* Unlatch local-client initialization.
			 *
			 * TryInitializeLocalClient is deliberately once-only, but "once" meant once per
			 * pooled INSTANCE rather than once per spawn: the latch is a plain field and nothing
			 * cleared it. A character object that came back out of the pool for a second spawn —
			 * an instance entry and its return are the common pair — therefore skipped
			 * OnStartCharacter entirely, so no behaviour re-registered its client broadcast
			 * handlers and the whole owner-side UI came up inert with no error anywhere. */
			localClientInitialized = false;
#endif

			TeleporterName = "";
			LastChatMessage = "";
			long nowTicks = DateTime.UtcNow.Ticks;
			NextChatMessageTicks = nowTicks;
			ChatTokens = 0;
			IsChatTokensFull = true;
			ChatTokenLastRefillTicks = nowTicks;
			NextInteractTime = DateTime.UtcNow;
			InstanceSceneName = null;
			InstanceSceneHandle = 0;
			InstancePosition = Vector3.zero;
			InstanceRotation = Quaternion.identity;
			LastWorldPosition = Vector3.zero;
			LastWorldRotation = Quaternion.identity;

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
		/// Sets the guild row of the character's overhead nameplate (client only).
		/// </summary>
		/// <param name="guildName">The guild's name, or blank when the character has left one.</param>
		/// <remarks>
		/// A blank name clears the row rather than leaving an empty one: an empty row would still
		/// take a line of height on the plate, so every character in the world who is not in a
		/// guild would carry a visible gap under their name.
		/// </remarks>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetGuildName(string guildName)
		{
#if !UNITY_SERVER
			if (CharacterNameplate != null)
			{
				CharacterNameplate.SetLine(NameplateSlot.GuildName,
					!string.IsNullOrWhiteSpace(guildName) ? "[" + guildName + "]" : null);
			}
#endif
		}
	}
}