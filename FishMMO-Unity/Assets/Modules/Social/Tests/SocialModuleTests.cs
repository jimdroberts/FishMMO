using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Social module (Phase 3, 2026-11-16), friends first: everything the friend list is lives in
	/// <c>Assets/Modules/Social</c>, and the core names none of it.
	/// </summary>
	[TestFixture]
	public class SocialModuleTests
	{
		private const string ModuleFolder = "Assets/Modules/Social";

		[Test]
		public void EveryPlayerGetsAFriendList_NoNpcDoes()
		{
			LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(FriendController)),
				"a player character gets its friend list as a part");
			LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(FriendController)), "an NPC does not");
			LogAssert.IsFalse(typeof(FishNet.Object.NetworkBehaviour).IsAssignableFrom(typeof(FriendController)),
				"the part is not a NetworkBehaviour, so no prefab's behaviour list depends on it");
		}

		[Test]
		public void NoPlayerPrefabCarriesTheFriendList()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Runtime/FriendController.cs"));
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), "the controller script exists");
			foreach (string race in new[] { "Human", "Elf", "Orc" })
			{
				string prefab = ProjectFiles.ReadAllText($"Assets/Prefabs/Shared/Entity/PlayableCharacters/{race}.prefab");
				LogAssert.IsFalse(prefab.Contains(guid), $"{race}.prefab does not author the friend list (it is added at wake)");
			}
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scripts/Shared/Implementation/Entity/PlayerCharacter.cs").Contains("FriendController"),
				"PlayerCharacter does not require it");
		}

		[Test]
		public void TheFriendListLoadsThroughTheModulesParticipant()
		{
			LogAssert.IsTrue(CharacterPersistenceParticipants.All.Any(p => p is FriendPersistenceParticipant),
				"the character load reads the friend list through the module's participant");
			string loading = ProjectFiles.ReadAllText("Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs");
			string social = ProjectFiles.ReadAllText("Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Social.cs");
			LogAssert.IsFalse(loading.Contains("ICharacterFriendService") || loading.Contains("IFriendController"),
				"the core load names nothing of friends");
			LogAssert.IsFalse(social.Contains("FriendAddBroadcast"), "nor does the core's social payload: the module sends the list");
			LogAssert.IsTrue(ProjectFiles.ReadAllText(ModuleFolder + "/Server/FriendSystem.cs").Contains("characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;"),
				"the friend system sends a spawned player its list");
		}

		[Test]
		public void TheSceneServerAdoptsTheFriendSystem()
		{
			string assetPath = ModuleFolder + "/Server/FriendSystem.asset";
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(assetPath));
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), $"{assetPath} exists");
			string group = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "it carries the SceneServer_Systems label");
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Server/SceneServer.unity").Contains(guid),
				"the scene server's own list does not name it");
		}

		[Test]
		public void TheFriendListWindowIsPlacedIntoTheWorldGui()
		{
			string prefabPath = ModuleFolder + "/Client/UIFriendList.prefab";
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(prefabPath));
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), $"{prefabPath} exists");
			string group = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(group, $@"- m_GUID: {guid}\n    m_Address: UIFriendList\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
				"it is labelled for ClientWorldGUI and keeps the UIManager key UIFriendList");
			string script = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UITKFriendList.cs"));
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Client/ClientWorldGUI.unity").Contains(script),
				"the world GUI scene no longer holds the window");
			LogAssert.IsTrue(ProjectFiles.ReadAllText(prefabPath).Contains(script), "the prefab does");
		}

		[Test]
		public void AddFriendIsOfferedOnEveryCharacterMenu()
		{
			SocialClient.Install(); // [RuntimeInitializeOnLoadMethod] does not run in EditMode.
			LogAssert.IsTrue(CharacterMenuEntries.All.Any(e => e.Id == SocialClient.AddFriendEntryId && e.Label == "Add Friend"),
				"the module registers Add Friend");
			foreach (string path in new[]
			{
				"Assets/Scripts/Client/Input/PlayerInputController.cs",
				"Assets/Scripts/Client/GUI/World/Guild/UITKGuild.cs",
				"Assets/Modules/Social/Client/UITKParty.cs",
			})
			{
				string code = ProjectFiles.ReadAllText(path);
				LogAssert.IsTrue(code.Contains("CharacterMenuEntries.AppendTo(entries, new CharacterMenuTarget("), $"{path} takes the module entries");
				LogAssert.IsFalse(code.Contains("FriendAddNewBroadcast"), $"{path} names nothing of friends");
			}
		}

		[Test]
		public void CharacterMenuEntries_OrderApplyAndReplace()
		{
			const string first = "test.first";
			const string second = "test.second";
			var clicked = new List<long>();
			try
			{
				CharacterMenuEntries.Register(second, "Second", 9001, t => clicked.Add(-t.CharacterID));
				CharacterMenuEntries.Register(first, "First", 9000, t => clicked.Add(t.CharacterID),
					t => t.Source != CharacterMenuSource.Party);
				CharacterMenuEntries.Register(second, "Second again", 9001, t => clicked.Add(t.CharacterID * 10));

				var world = new List<(string label, Action callback)>();
				CharacterMenuEntries.AppendTo(world, new CharacterMenuTarget(7, "Bob", CharacterMenuSource.World));
				List<string> labels = world.Select(e => e.label).Where(l => l == "First" || l.StartsWith("Second", StringComparison.Ordinal)).ToList();
				LogAssert.AreEqual("First,Second again", string.Join(",", labels), "in order, and a re-register replaces by id");

				foreach ((string label, Action callback) entry in world)
				{
					if (entry.label == "First" || entry.label == "Second again")
					{
						entry.callback();
					}
				}
				LogAssert.AreEqual("7,70", string.Join(",", clicked), "each entry acts on the target it was built for");

				var party = new List<(string label, Action callback)>();
				CharacterMenuEntries.AppendTo(party, new CharacterMenuTarget(7, "Bob", CharacterMenuSource.Party));
				LogAssert.IsFalse(party.Any(e => e.label == "First"), "an entry that does not apply is left out");
			}
			finally
			{
				CharacterMenuEntries.Unregister(first);
				CharacterMenuEntries.Unregister(second);
			}
		}
	}
}
