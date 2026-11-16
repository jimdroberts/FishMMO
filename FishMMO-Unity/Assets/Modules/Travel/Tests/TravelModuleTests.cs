using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Travel module plugs into the game without the game naming it: a character part for players, a save participant,
	/// a scene server system adopted by label, and a trigger attached to every waypoint at run time.
	/// </summary>
	[TestFixture]
	public class TravelModuleTests
	{
		private const string ModuleFolder = "Assets/Modules/Travel";

		[Test]
		public void EveryPlayerGetsTheWaypointRecord_NoNpcDoes()
		{
			LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(WaypointController)),
				"a player character gets the waypoint record as a part");
			LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(WaypointController)), "an NPC does not");
			LogAssert.IsFalse(typeof(FishNet.Object.NetworkBehaviour).IsAssignableFrom(typeof(WaypointController)),
				"the part is not a NetworkBehaviour, so no prefab's behaviour list depends on it");
			LogAssert.AreEqual(CharacterParts.IdOf("travel.waypoints"), WaypointController.PayloadKey, "its payload id never changes");
		}

		[Test]
		public void NoPlayerPrefabCarriesTheRecord()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Runtime/WaypointController.cs"));
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), "the controller script exists");
			foreach (string race in new[] { "Human", "Elf", "Orc" })
			{
				string prefab = ProjectFiles.ReadAllText($"Assets/Prefabs/Shared/Entity/PlayableCharacters/{race}.prefab");
				LogAssert.IsFalse(prefab.Contains(guid), $"{race}.prefab does not author the waypoint record (it is added at wake)");
			}
		}

		[Test]
		public void TheSaveParticipantIsInstalled()
		{
			LogAssert.IsTrue(CharacterPersistenceParticipants.All.Any(p => p is WaypointPersistenceParticipant),
				"the character save includes discovered waypoints through the module's participant");
		}

		[Test]
		public void TheSceneServerAdoptsTheTravelSystem_AndItAttachesTheInteractTrigger()
		{
			string assetPath = ModuleFolder + "/Server/WaypointTravelSystem.asset";
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(assetPath));
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), $"{assetPath} exists");
			string group = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "it carries the SceneServer_Systems label");

			string triggerGuid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Templates/Waypoint Interact.asset"));
			LogAssert.IsTrue(ProjectFiles.ReadAllText(assetPath).Contains(triggerGuid), "the system holds the waypoint interact trigger");
			string waypointPrefab = ProjectFiles.ReadAllText("Assets/Prefabs/Shared/Entity/Interactables/Waypoints/Waypoint.prefab");
			LogAssert.IsFalse(waypointPrefab.Contains(triggerGuid), "the core waypoint prefab names nothing of the module");
			LogAssert.IsTrue(ProjectFiles.ReadAllText(ModuleFolder + "/Templates/Waypoint Interact.asset").Contains("class: UnlockWaypointAction, ns: FishMMO.Shared, asm: FishMMO.Travel"),
				"the trigger's unlock action resolves in the module's assembly");
		}

		[Test]
		public void AnAttachedTriggerRunsAfterTheAuthoredOnes_AndDetaches()
		{
			Trigger trigger = UnityEngine.ScriptableObject.CreateInstance<Trigger>();
			try
			{
				InteractableTriggers.Attach(typeof(Interactable), trigger);
				LogAssert.IsTrue(InteractableTriggers.For(typeof(Waypoint)).Contains(trigger), "a kind matches its subclasses");
				LogAssert.IsTrue(InteractableTriggers.Detach(typeof(Interactable), trigger), "detaches");
				LogAssert.IsFalse(InteractableTriggers.For(typeof(Waypoint)).Contains(trigger), "and is gone");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(trigger);
			}
		}
	}
}
