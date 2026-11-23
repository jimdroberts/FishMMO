using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Core;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Achievements module: accomplishments counted toward achievements, the save participant, dirty
	/// tracking, and the wiring that lets the folder be removed (a part, a label-adopted system, a label-placed window).
	/// Moved from CharacterCoreTests and WaypointTests with the module.
	/// </summary>
	[TestFixture]
	public class AchievementsModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Achievements";

		private static (World World, ModuleInstall Install) InstallWithAchievements()
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(MessagingModule).Assembly, typeof(AchievementModule).Assembly }, problems);
			ModuleInstall install = ModuleCatalog.Resolve(found, problems).Install(world, scheduler, SimulationSide.Server);
			return (world, install);
		}

		// ── Achievements from accomplishments ────────────────────────────────────

		private sealed class CountingAchievements : IAchievementController
		{
			public readonly List<(AchievementTemplate Template, uint Amount)> Increments = new List<(AchievementTemplate, uint)>();
			public ICharacter Character { get; set; }
			public bool Initialized => true;
			public Dictionary<int, Achievement> Achievements { get; } = new Dictionary<int, Achievement>();
			public void InitializeOnce(ICharacter character) => Character = character;
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
			public void SetAchievement(int templateID, byte tier, uint value, bool skipEvent = false) =>
				Achievements[templateID] = new Achievement(templateID, tier, value);
			public bool TryGetAchievement(int templateID, out Achievement achievement) => Achievements.TryGetValue(templateID, out achievement);
			public void Increment(AchievementTemplate template, uint amount) => Increments.Add((template, amount));
			public void Increment(int templateID, uint amount) { }
		}

		private readonly List<UnityEngine.Object> temporaries = new List<UnityEngine.Object>();

		[TearDown]
		public void TearDown()
		{
			Trigger.DispatchObserved = null;
			foreach (UnityEngine.Object o in temporaries)
			{
				if (o is ICachedObject cached)
				{
					cached.RemoveFromCache();
				}
				if (o != null)
				{
					UnityEngine.Object.DestroyImmediate(o);
				}
			}
			temporaries.Clear();
		}

		private AchievementTemplate NewTemplate(string name)
		{
			var template = ScriptableObject.CreateInstance<AchievementTemplate>();
			template.name = name;
			temporaries.Add(template);
			return template;
		}

		[Test]
		public void AnAccomplishmentCountsAtOnce_TowardTheAchievementContentNamed()
		{
			(World world, ModuleInstall _) = InstallWithAchievements();
			var achievements = new CountingAchievements();
			var character = new StubCharacter { ID = 1, Behaviour = achievements, CoreLink = new EntityLink(world, world.Spawn(Archetypes.Player)) };
			AchievementTemplate guildFounder = NewTemplate("Test_GuildFounder");

			Accomplishments.Record(character, guildFounder);
			Accomplishments.Record(character, guildFounder, 3);

			LogAssert.AreEqual(2, achievements.Increments.Count, "each accomplishment is counted before Record returns: no tick in between, so a save straight after carries it");
			LogAssert.IsTrue(achievements.Increments[0].Template == guildFounder && achievements.Increments[0].Amount == 1, "the default is one");
			LogAssert.AreEqual(3u, achievements.Increments[1].Amount);
		}

		[Test]
		public void AnAccomplishmentWithNothingToCountIsANoOp()
		{
			(World world, ModuleInstall _) = InstallWithAchievements();
			var achievements = new CountingAchievements();
			var character = new StubCharacter { Behaviour = achievements, CoreLink = new EntityLink(world, world.Spawn(Archetypes.Player)) };
			AchievementTemplate template = NewTemplate("Test_NoOp");

			Accomplishments.Record(character, null);
			Accomplishments.Record(character, template, 0);
			Accomplishments.Record(null, template);
			LogAssert.AreEqual(0, achievements.Increments.Count, "a feature whose content names no achievement counts nothing");

			var noModule = new World();
			var elsewhere = new StubCharacter { Behaviour = achievements, CoreLink = new EntityLink(noModule, noModule.Create()) };
			Assert.DoesNotThrow(() => Accomplishments.Record(elsewhere, template));
			LogAssert.AreEqual(0, achievements.Increments.Count, "a game without the achievements module counts nothing, and breaks nothing");
		}

		// ── The achievement save participant ─────────────────────────────────────

		[Test]
		public void TheAchievementParticipantCapturesOnlyChangedProgress_AndBumpsItsVersion()
		{
			var achievements = new CountingAchievements();
			var character = new StubCharacter { ID = 42, Behaviour = achievements };
			var changed = new Achievement(11, 1, 5) { Version = 3 };
			changed.MarkChanged();
			var untouched = new Achievement(12, 0, 2) { Version = 8 };
			achievements.Achievements[11] = changed;
			achievements.Achievements[12] = untouched;

			var participant = new AchievementPersistence();
			CharacterRows rows = participant.CreateRows();
			participant.Capture(character, rows);

			List<CharacterAchievementData> captured = ((CharacterRows<CharacterAchievementData>)rows).Items;
			LogAssert.AreEqual(1, captured.Count, "only dirty progress is captured");
			LogAssert.AreEqual(11, captured[0].TemplateID);
			LogAssert.AreEqual(42L, captured[0].CharacterID);
			LogAssert.AreEqual(changed.Version, captured[0].Version, "the row carries the bumped version the write is checked against");
			LogAssert.AreEqual(5u, captured[0].Value);
		}

		[Test]
		public void TheAchievementParticipantRestoresWhatWasSaved_AsPersisted()
		{
			var achievements = new CountingAchievements();
			var character = new StubCharacter { ID = 42, Behaviour = achievements };
			var saved = new List<CharacterAchievementData>
			{
				new CharacterAchievementData(id: 1, version: 17, characterID: 42, templateID: 21, tier: 2, value: 40),
			};
			new AchievementPersistence().Apply(character, saved);

			LogAssert.IsTrue(achievements.Achievements.TryGetValue(21, out Achievement restored), "the saved achievement is back");
			LogAssert.AreEqual((byte)2, restored.CurrentTier);
			LogAssert.AreEqual(40u, restored.CurrentValue);
			LogAssert.AreEqual(17L, restored.Version, "its version is restored, so the next write is newer than the saved row");
			Assert.DoesNotThrow(() => new AchievementPersistence().Apply(character, null), "a character with no saved progress loads");
		}

		[Test]
		public void TheAchievementParticipantIsInstalled()
		{
			LogAssert.IsTrue(CharacterPersistenceParticipants.All.Any(p => p is AchievementPersistence),
				"achievements load and save through their participant; without it progress would neither load nor save");
		}

		// ── Dirty tracking ───────────────────────────────────────────────────────

		[Test]
		public void Achievement_MarkChanged_AdvancesVersion_AndAStaleConfirmationDoesNotClear()
		{
			Achievement achievement = new Achievement(12345, 0, 0);
			Assert.IsFalse(achievement.PersistenceDirty, "A restored row is clean until something changes it.");

			achievement.MarkChanged();
			Assert.IsTrue(achievement.PersistenceDirty);
			long snapshot = ++achievement.Version; // the save snapshot bumps once more

			achievement.MarkChanged(); // changed while the write was in flight
			achievement.MarkPersisted(snapshot);
			Assert.IsTrue(achievement.PersistenceDirty, "The confirmation is for an older state.");

			achievement.MarkPersisted(achievement.Version);
			Assert.IsFalse(achievement.PersistenceDirty);
		}

		// ── Wiring ───────────────────────────────────────────────────────────────

		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(AchievementModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == AchievementModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void EveryPlayerGetsAnAchievementController_LoadedAndSentByTheModule()
		{
			LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(AchievementController)), "a player gets the controller as a part");
			LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(AchievementController)), "an NPC does not");
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Runtime/AchievementController.cs"));
			foreach (string race in new[] { "Human", "Elf", "Orc" })
			{
				LogAssert.IsFalse(ProjectFiles.ReadAllText($"Assets/Prefabs/Shared/Entity/PlayableCharacters/{race}.prefab").Contains(guid),
					$"{race}.prefab does not author the achievement controller");
			}
			string social = ProjectFiles.ReadAllText("Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Social.cs");
			LogAssert.IsFalse(social.Contains("AchievementUpdate"), "the core login send names nothing of achievements");
			LogAssert.IsTrue(ProjectFiles.ReadAllText(ModuleFolder + "/Server/AchievementSystem.cs").Contains("characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;"),
				"the achievement system sends a spawned player its achievements");
		}

		[Test]
		public void TheSceneServerAdoptsTheAchievementSystem_AndTheWorldGuiPlacesTheWindow()
		{
			string system = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/AchievementSystem.asset"));
			string server = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(server, $@"- m_GUID: {system}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "the system carries the SceneServer_Systems label");
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Server/SceneServer.unity").Contains(system), "the scene server's own list does not name it");

			string window = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UIAchievements.prefab"));
			string client = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {window}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"), "the window carries the ClientWorldGUI_Panels label");
			string script = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UITKAchievements.cs"));
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Client/ClientWorldGUI.unity").Contains(script), "ClientWorldGUI.unity no longer carries the window");
		}

		[Test]
		public void CoreContentRecordsAccomplishments_NeverNamingTheModule()
		{
			foreach (string path in new[]
			{
				"Assets/FishMMO/Shared/Implementation/Entity/ECA/Actions/Character/Achievement/AchievementIncrementAction.cs",
				"Assets/FishMMO/Modules/WorldInteractables/Runtime/Actions/ShrineAction.cs",
				"Assets/FishMMO/Modules/WorldInteractables/Runtime/Actions/LoreObjectAction.cs",
			})
			{
				if (InstalledModules.IsAbsent(path))
				{
					continue; // the World Interactables module's shrine and lore actions, when it is removed
				}
				string source = ProjectFiles.ReadAllText(path);
				LogAssert.IsTrue(source.Contains("Accomplishments.Record("), $"{path} records the accomplishment");
				LogAssert.IsFalse(source.Contains("IAchievementController"), $"{path} no longer reaches for the achievement controller");
			}
		}

		[Test]
		public void AchievementsAreWrittenThroughTheOwnershipGate_AndClearTheirMarksByTheOneRule()
		{
			string system = ProjectFiles.ReadAllText(ModuleFolder + "/Server/AchievementSystem.cs");
			LogAssert.IsTrue(system.Contains("service.PersistOwnedAsync("), "the system's reward writes go through the ownership gate");
			LogAssert.IsFalse(system.Contains("service.PersistAsync("), "and never through the ungated write");
			string save = ProjectFiles.ReadAllText(ModuleFolder + "/Server/AchievementPersistence.cs");
			LogAssert.IsTrue(save.Contains("await achievementService.PersistOwnedAsync("), "the character save writes progress through the ownership gate");
			LogAssert.IsFalse(save.Contains("await achievementService.PersistAsync("), "and never through the ungated write");
			LogAssert.IsTrue(save.Contains("BulkWriteReporting.MayClearDirtyMarks(result)"), "its marks clear only by the one rule");
		}

	}
}
