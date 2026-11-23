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
	/// <summary>The Quests module: the quest log is a part, loaded and sent by the module.</summary>
	[TestFixture]
	public class QuestsModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Quests";

		[Test]
		public void EveryPlayerGetsAQuestLog_LoadedAndSentByTheModule()
		{
			LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(QuestController)), "a player gets the quest log as a part");
			LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(QuestController)), "an NPC does not");
			LogAssert.IsTrue(CharacterPersistenceParticipants.All.Any(p => p is QuestPersistenceParticipant), "quests load through the module");

			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Runtime/QuestController.cs"));
			foreach (string race in new[] { "Human", "Elf", "Orc" })
			{
				LogAssert.IsFalse(ProjectFiles.ReadAllText($"Assets/Prefabs/Shared/Entity/PlayableCharacters/{race}.prefab").Contains(guid),
					$"{race}.prefab does not author the quest controller");
			}

			string loading = ProjectFiles.ReadAllText("Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs");
			string social = ProjectFiles.ReadAllText("Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Social.cs");
			LogAssert.IsFalse(loading.Contains("CharacterQuestData") || social.Contains("QuestUpdateBroadcast"), "the core load and login send name nothing of quests");
			LogAssert.IsTrue(ProjectFiles.ReadAllText(ModuleFolder + "/Server/QuestSystem.cs").Contains("characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;"),
				"the quest system sends a spawned player its quests");
		}

		[Test]
		public void TheSceneServerAdoptsTheQuestSystem()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/QuestSystem.asset"));
			string group = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "it carries the SceneServer_Systems label");
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Server/SceneServer.unity").Contains(guid), "the scene server's own list does not name it");
		}

		[Test]
		public void ARestoredQuestKeepsItsObjectiveValues()
		{
			LogAssert.AreEqual("3,0,12", string.Join(",", QuestPersistenceParticipant.ParseObjectiveValues("3,0,12")));
			LogAssert.AreEqual(0, QuestPersistenceParticipant.ParseObjectiveValues(null).Length, "no values, none restored");
			LogAssert.AreEqual("5,0", string.Join(",", QuestPersistenceParticipant.ParseObjectiveValues("5,x")), "an unreadable value restores as zero");
		}
	}
}
