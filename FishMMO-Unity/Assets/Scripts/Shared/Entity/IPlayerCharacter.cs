using System;
using System.Collections.Generic;
using KinematicCharacterController;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Managing.Predicting;
using UnityEngine;
#if !UNITY_SERVER
using TMPro;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for player-controlled character entities, extending ICharacter with player-specific properties and methods.
	/// Includes networking, instance, race, chat, hotkey, and teleportation features.
	/// </summary>
	public interface IPlayerCharacter : ICharacter
	{
		/// <summary>
		/// Event triggered when a payload is read for this player character.
		/// </summary>
		static Action<IPlayerCharacter> OnReadPayload;
		/// <summary>
		/// Event triggered when the local client starts for this player character.
		/// </summary>
		static Action<IPlayerCharacter> OnStartLocalClient;
		/// <summary>
		/// Event triggered when the local client stops for this player character.
		/// </summary>
		static Action<IPlayerCharacter> OnStopLocalClient;
		/// <summary>
		/// Event triggered when this player character teleports.
		/// </summary>
		static Action<IPlayerCharacter> OnTeleport;

		/// <summary>
		/// The display name of the character.
		/// </summary>
		string CharacterName { get; set; }
		/// <summary>
		/// Lowercase version of the character's name for case-insensitive comparisons.
		/// </summary>
		string CharacterNameLower { get; set; }
		/// <summary>
		/// Unique identifier for the world server this character belongs to.
		/// </summary>
		long WorldServerID { get; set; }
		/// <summary>
		/// The account name associated with this character.
		/// </summary>
		string Account { get; set; }
		/// <summary>
		/// The date and time when this character was created.
		/// </summary>
		DateTime TimeCreated { get; set; }
		/// <summary>
		/// The access level of the player (e.g., admin, moderator, player).
		/// </summary>
		AccessLevel AccessLevel { get; set; }
		/// <summary>
		/// The name of the teleporter used for the last teleport action.
		/// </summary>
		string TeleporterName { get; set; }
		/// <summary>
		/// The network connection that owns this character.
		/// </summary>
		NetworkConnection Owner { get; }
		/// <summary>
		/// The network object representing this character in FishNet networking.
		/// </summary>
		NetworkObject NetworkObject { get; }
		/// <summary>
		/// The prediction manager for client-side prediction and reconciliation.
		/// </summary>
		PredictionManager PredictionManager { get; }
		/// <summary>
		/// The set of network connections observing this character.
		/// </summary>
		HashSet<NetworkConnection> Observers { get; }
		/// <summary>
		/// The race ID of the character.
		/// </summary>
		int RaceID { get; set; }
		/// <summary>
		/// The model index for the character's race appearance.
		/// </summary>
		int ModelIndex { get; set; }
		/// <summary>
		/// The name of the character's race.
		/// </summary>
		string RaceName { get; set; }
		/// <summary>
		/// The name of the scene where the character is bound (e.g., respawn location).
		/// </summary>
		string BindScene { get; set; }
		/// <summary>
		/// The position in the bind scene where the character will respawn.
		/// </summary>
		Vector3 BindPosition { get; set; }
		/// <summary>
		/// The name of the current scene the character is in.
		/// </summary>
		string SceneName { get; set; }
		/// <summary>
		/// The handle of the current scene.
		/// </summary>
		int SceneHandle { get; set; }
		/// <summary>
		/// Unique identifier for the instance the character is in.
		/// </summary>
		long InstanceID { get; set; }
		/// <summary>
		/// The name of the instance scene.
		/// </summary>
		string InstanceSceneName { get; set; }
		/// <summary>
		/// The handle of the instance scene.
		/// </summary>
		int InstanceSceneHandle { get; set; }
		/// <summary>
		/// The position in the instance scene.
		/// </summary>
		Vector3 InstancePosition { get; set; }
		/// <summary>
		/// The rotation in the instance scene.
		/// </summary>
		Quaternion InstanceRotation { get; set; }
		/// <summary>
		/// Returns true if the character is currently inside an instance.
		/// </summary>
		bool IsInInstance();

		/// <summary>
		/// The motor for kinematic character movement.
		/// </summary>
		KinematicCharacterMotor Motor { get; }
		/// <summary>
		/// The controller for kinematic character movement.
		/// </summary>
		KCCController CharacterController { get; }
		/// <summary>
		/// The player controller for kinematic character movement.
		/// </summary>
		KCCPlayer KCCPlayer { get; }

#if !UNITY_SERVER
		/// <summary>
		/// The camera used for equipment view (e.g., inspecting gear).
		/// </summary>
		Camera EquipmentViewCamera { get; set; }
#endif

		/// <summary>
		/// The last chat message sent by the character.
		/// </summary>
		string LastChatMessage { get; set; }
		/// <summary>
		/// The next time the character is allowed to send a chat message.
		/// </summary>
		DateTime NextChatMessageTime { get; set; }
		/// <summary>
		/// The next time the character is allowed to interact with objects.
		/// </summary>
		DateTime NextInteractTime { get; set; }

		/// <summary>
		/// The list of hotkey data for this character.
		/// </summary>
		List<HotkeyData> Hotkeys { get; set; }
		/// <summary>
		/// Resets all hotkeys to their default state.
		/// </summary>
		void ResetHotkeys();

		/// <summary>
		/// Sets the guild name for this character.
		/// </summary>
		/// <param name="guildName">The name of the guild to set.</param>
		void SetGuildName(string guildName);
		/// <summary>
		/// Teleports the character using the specified teleporter name.
		/// </summary>
		/// <param name="teleporterName">The name of the teleporter to use.</param>
		void Teleport(string teleporterName);
	}
}