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
	public interface IPlayerCharacter : ICharacter
	{
		static Action<IPlayerCharacter> OnReadPayload;
		static Action<IPlayerCharacter> OnStartLocalClient;
		static Action<IPlayerCharacter> OnStopLocalClient;
		static Action<IPlayerCharacter> OnTeleport;

		string CharacterName { get; set; }
		string CharacterNameLower { get; set; }
		long WorldServerID { get; set; }
		string Account { get; set; }
		DateTime TimeCreated { get; set; }
		AccessLevel AccessLevel { get; set; }
		string TeleporterName { get; set; }
		NetworkConnection Owner { get; }
		NetworkObject NetworkObject { get; }
		PredictionManager PredictionManager { get; }
		HashSet<NetworkConnection> Observers { get; }
		int RaceID { get; set; }
		string RaceName { get; set; }
		string BindScene { get; set; }
		Vector3 BindPosition { get; set; }
		string SceneName { get; set; }
		int SceneHandle { get; set; }
		long InstanceID { get; set; }
		string InstanceSceneName { get; set; }
		int InstanceSceneHandle { get; set; }
		Vector3 InstancePosition { get; set; }
		Quaternion InstanceRotation { get; set; }

		KinematicCharacterMotor Motor { get; }
		KCCController CharacterController { get; }
		KCCPlayer KCCPlayer { get; }

#if !UNITY_SERVER
		Camera EquipmentViewCamera { get; set; }
#endif

		string LastChatMessage { get; set; }
		DateTime NextChatMessageTime { get; set; }
		DateTime NextInteractTime { get; set; }

		List<HotkeyData> Hotkeys { get; set; }
		void ResetHotkeys();

		void SetGuildName(string guildName);
		void Teleport(string teleporterName);
	}
}