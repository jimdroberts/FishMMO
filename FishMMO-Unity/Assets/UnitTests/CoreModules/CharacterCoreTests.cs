using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Core;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Characters' state on their Core entities: the modules that give players their chat, interaction and
	/// hotkey state, reaching that state from a character, achievements counted from accomplishments, trigger
	/// dispatch, and the achievement save participant.
	/// </summary>
	[TestFixture]
	public class CharacterCoreTests
	{
		private static (World World, ModuleInstall Install) InstallShared(SimulationSide side)
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(ChatModule).Assembly }, problems);
			ModuleInstall install = ModuleCatalog.Resolve(found, problems).Install(world, scheduler, side);
			return (world, install);
		}

		// ── The modules ──────────────────────────────────────────────────────────

		[Test]
		public void TheSharedModulesAreDeclaredAndInstallCleanly()
		{
			(World _, ModuleInstall install) = InstallShared(SimulationSide.Server);
			string[] ids = install.Modules.Select(m => m.Id).ToArray();
			foreach (string id in new[] { CoreModule.ModuleId, ChatModule.ModuleId, InteractionModule.ModuleId, HotkeyModule.ModuleId, AchievementModule.ModuleId, NpcCorpseModule.ModuleId })
			{
				LogAssert.IsTrue(ids.Contains(id), $"module '{id}' must be installed on the server; installed: {string.Join(", ", ids)}");
			}
			LogAssert.IsTrue(install.Problems.Count == 0, "no module may be left out: " + string.Join("; ", install.Problems));
			LogAssert.IsTrue(install.SystemsOf(NpcCorpseModule.ModuleId).Contains(nameof(CorpseExpirySystem)), "the corpse module brings its systems");
		}

		[Test]
		public void APlayerStartsWithFreshChatState_TheBucketFullAndBothClocksAtNever()
		{
			(World world, ModuleInstall _) = InstallShared(SimulationSide.Server);
			Entity player = world.Spawn(Archetypes.Player);
			ChatState chat = world.Get<ChatState>(player);
			LogAssert.IsTrue(chat.IsFull, "the anti-spam bucket starts full");
			LogAssert.AreEqual(0L, chat.NextMessageTicks, "no gap pending: zero is 'never' on either side's clock");
			LogAssert.AreEqual(0L, chat.LastRefillTicks, "no refill yet");
			LogAssert.AreEqual(0L, chat.MutedUntilTicks, "a new spawn carries no mute; the load sets the stored one");
			LogAssert.IsNull(chat.MuteReason, "and no mute reason");
			LogAssert.AreEqual(string.Empty, chat.LastMessage, "no previous line to compare a repeat against");
		}

		[Test]
		public void APlayerStartsReadyToInteract()
		{
			(World world, ModuleInstall _) = InstallShared(SimulationSide.Client);
			Entity player = world.Spawn(Archetypes.Player);
			LogAssert.AreEqual(0d, world.Get<InteractCooldown>(player).NextAt, "zero is 'now'");
			LogAssert.IsTrue(world.Has<ChatState>(player), "the client keeps its own chat state for its own repeat filter and gap");
		}

		[Test]
		public void APlayerOnTheServerHasEveryHotkeySlot_InAListOfItsOwn()
		{
			(World world, ModuleInstall _) = InstallShared(SimulationSide.Server);
			Entity a = world.Spawn(Archetypes.Player);
			Entity b = world.Spawn(Archetypes.Player);
			List<HotkeyData> slots = world.Get<HotkeyBar>(a).Slots;
			LogAssert.AreEqual(Constants.Configuration.MaximumPlayerHotkeys, slots.Count, "every slot exists, empty");
			for (int i = 0; i < slots.Count; i++)
			{
				LogAssert.AreEqual(i, slots[i].Slot, "slots are in slot order");
				LogAssert.AreEqual(HotkeyData.UnsetReferenceID, slots[i].ReferenceID, "and unbound");
			}
			LogAssert.IsFalse(ReferenceEquals(slots, world.Get<HotkeyBar>(b).Slots), "two players never share one bar");
		}

		[Test]
		public void TheClientKeepsNoHotkeyBar_AndNpcsGetNoPlayerState()
		{
			(World client, ModuleInstall _) = InstallShared(SimulationSide.Client);
			LogAssert.IsFalse(client.Has<HotkeyBar>(client.Spawn(Archetypes.Player)), "the bar is server state; the client is sent it");

			(World server, ModuleInstall _) = InstallShared(SimulationSide.Server);
			Entity npc = server.Spawn(Archetypes.Npc);
			LogAssert.IsFalse(server.Has<ChatState>(npc) || server.Has<InteractCooldown>(npc) || server.Has<HotkeyBar>(npc),
				"NPCs get nothing the modules give players");
		}

		// ── Reaching it from a character ─────────────────────────────────────────

		[Test]
		public void ACharactersComponentIsItsEntitys_ByReference()
		{
			(World world, ModuleInstall _) = InstallShared(SimulationSide.Server);
			var character = new StubCharacter { ID = 7, CoreLink = new EntityLink(world, world.Spawn(Archetypes.Player)) };
			LogAssert.IsTrue(character.HasCoreEntity(), "a linked character has an entity");
			character.Component<ChatState>().MutedUntilTicks = 99;
			LogAssert.AreEqual(99L, world.Get<ChatState>(character.CoreLink.Entity).MutedUntilTicks, "writes go to the entity, not a copy");
			LogAssert.IsTrue(character.CoreLink().Equals(character.CoreLink), "the extension reads the character's own link");
		}

		[Test]
		public void AComponentNoModuleGaveIsAddedEmpty()
		{
			var world = new World();
			var character = new StubCharacter { CoreLink = new EntityLink(world, world.Create()) };
			character.Component<InteractCooldown>().NextAt = 3;
			LogAssert.AreEqual(3d, world.Get<InteractCooldown>(character.CoreLink.Entity).NextAt);
		}

		[Test]
		public void ACharacterWithNoEntitySaysSo()
		{
			var unlinked = new StubCharacter();
			LogAssert.IsFalse(unlinked.HasCoreEntity());
			Assert.Throws<InvalidOperationException>(() => unlinked.Component<ChatState>());

			var world = new World();
			Entity gone = world.Create();
			var stale = new StubCharacter { CoreLink = new EntityLink(world, gone) };
			world.Destroy(gone);
			LogAssert.IsFalse(stale.HasCoreEntity(), "a character whose entity ended (despawned, pooled) has none");
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
			(World world, ModuleInstall _) = InstallShared(SimulationSide.Server);
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
			(World world, ModuleInstall _) = InstallShared(SimulationSide.Server);
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

		// ── Trigger dispatch ─────────────────────────────────────────────────────

		[Test]
		public void InvokeAllIsNullSafe_AndEveryDispatchIsObservable()
		{
			int observed = 0;
			Trigger.DispatchObserved = (triggers, data) => observed++;
			var character = new StubCharacter();
			Assert.DoesNotThrow(() => Trigger.InvokeAll(null, new EventData(character)));
			Assert.DoesNotThrow(() => Trigger.InvokeAll(new List<Trigger> { null }, new EventData(character)));
			Assert.DoesNotThrow(() => Trigger.InvokeAll(new List<Trigger>(), null));
			LogAssert.AreEqual(3, observed, "the test hook hears every dispatch, empty ones included");
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
		public void RowsMergeAcrossCharacters_LeavingOutThoseUnderAnotherClaim()
		{
			var participant = new AchievementPersistence();
			var a = (CharacterRows<CharacterAchievementData>)participant.CreateRows();
			var b = (CharacterRows<CharacterAchievementData>)participant.CreateRows();
			b.Items.Add(new CharacterAchievementData(0, 1, 100, 1, 0, 1));
			b.Items.Add(new CharacterAchievementData(0, 1, 200, 1, 0, 1));
			a.AddFrom(b, new HashSet<long> { 200 });
			LogAssert.AreEqual(1, a.Count, "a conflicting claim's rows stay out of the merged flush");
			LogAssert.AreEqual(100L, a.Items[0].CharacterID);
			a.AddFrom(b, null);
			LogAssert.AreEqual(3, a.Count, "with no conflict every row is merged");
		}

		[Test]
		public void TheAchievementParticipantIsInstalled()
		{
			LogAssert.IsTrue(CharacterPersistenceParticipants.All.Any(p => p is AchievementPersistence),
				"achievements load and save through their participant; without it progress would neither load nor save");
		}

		// ── The character save and load call participants ────────────────────────

		private const string SavingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";
		private const string LoadingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs";

		private static string Read(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found");
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string Body(string source, string signature, string next)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"must still declare {signature}");
			int end = source.IndexOf(next, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");
			return source.Substring(start, end - start);
		}

		[Test]
		public void EverySaveRouteCallsTheParticipants()
		{
			string saving = Read(SavingPath);
			LogAssert.IsTrue(Body(saving, "private void AppendSubEntities(", "private void AppendDepartureSubEntities(").Contains(".Capture(character, snapshot.RowsFor("),
				"capture: every participant captures with the rest, under the same claim");
			LogAssert.IsTrue(Body(saving, "public void AddFrom(SubEntitySnapshot other)", "/// <summary>").Contains("RowsFor(participant.Key).AddFrom(participant.Value, conflicting)"),
				"merge: participants' rows are merged with the same conflicting-claim rule");
			LogAssert.IsTrue(Body(saving, "private void EnqueueSubEntitySaves(", "private async Task<bool> SaveSubEntitiesSequentiallyAsync(").Contains("writer.SaveAsync(this, rows, claims)"),
				"the periodic path writes them");
			LogAssert.IsTrue(Body(saving, "private async Task<bool> SaveSubEntitiesSequentiallyAsync(", "private const int MaxSubEntityWriteAttempts").Contains("SaveWithRetryAsync(() => writer.SaveAsync(this, rows, claims), writer.Name, characterID)"),
				"the hand-off path writes them with bounded retry before the release");
		}

		[Test]
		public void TheLoadMakesTheEntityFirst_ThenFetchesAndAppliesTheParticipants()
		{
			string loading = Read(LoadingPath);
			int link = loading.IndexOf("linkable.AttachCoreLink(coreLink);", StringComparison.Ordinal);
			int mute = loading.IndexOf("character.Component<ChatState>().MutedUntilTicks = ChatMutePolicy.ResolveUntilTicks(", StringComparison.Ordinal);
			int hotkeys = loading.IndexOf("character.Component<HotkeyBar>().Slots[hotkey.Slot]", StringComparison.Ordinal);
			int apply = loading.IndexOf("participant.Apply(character, fetched);", StringComparison.Ordinal);
			int spawn = loading.IndexOf("ServerManager.Spawn(character.NetworkObject", StringComparison.Ordinal);
			LogAssert.IsTrue(link > 0 && mute > link && hotkeys > link && apply > link, "the entity exists before any loaded state is put on it");
			LogAssert.IsTrue(spawn > apply, "and everything is in place before the character spawns");
			LogAssert.IsTrue(loading.Contains("CharacterFetch fetched = await participant.FetchAsync(this, characterID);"), "the participants fetch in the load");
			LogAssert.IsTrue(loading.Contains("await AbandonLoadAfterFetchFailure(participant.Name, fetched.ErrorCode, fetched.ErrorMessage);"),
				"and a failed fetch abandons the load, so nothing is saved over unloaded state");
		}

		[Test]
		public void TheDepartureSavesTheHotkeyBarFromTheEntity_BeforeTheDespawnEndsIt()
		{
			string saving = Read(SavingPath);
			string departure = Body(saving, "private void SaveAndDespawnCharacter(", "private CharacterData BuildCharacterData(");
			int capture = departure.IndexOf("AppendDepartureSubEntities(character, subEntities, sessionInfo);", StringComparison.Ordinal);
			int despawn = departure.IndexOf("PersistentPool.Despawn(", StringComparison.Ordinal);
			LogAssert.IsTrue(capture > 0 && despawn > capture, "the bar is read off the entity before the despawn releases the entity");
		}
	}
}
