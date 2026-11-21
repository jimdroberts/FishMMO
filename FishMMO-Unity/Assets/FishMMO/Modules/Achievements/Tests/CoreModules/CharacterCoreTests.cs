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
	/// Characters' state on their Core entities: the modules that give players their chat and interaction state,
	/// reaching that state from a character, accomplishments, trigger dispatch, and the character save and load calling
	/// participants. The achievement and hotkey tests are those modules'.
	/// </summary>
	[TestFixture]
	public class CharacterCoreTests
	{
		private static (World World, ModuleInstall Install) InstallShared(SimulationSide side)
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(MessagingModule).Assembly }, problems);
			ModuleInstall install = ModuleCatalog.Resolve(found, problems).Install(world, scheduler, side);
			return (world, install);
		}

		// ── The modules ──────────────────────────────────────────────────────────

		[Test]
		public void TheSharedModulesAreDeclaredAndInstallCleanly()
		{
			(World _, ModuleInstall install) = InstallShared(SimulationSide.Server);
			string[] ids = install.Modules.Select(m => m.Id).ToArray();
			foreach (string id in new[] { CoreModule.ModuleId, MessagingModule.ModuleId, InteractionModule.ModuleId, NpcCorpseModule.ModuleId })
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
		public void NpcsGetNoPlayerState()
		{
			(World server, ModuleInstall _) = InstallShared(SimulationSide.Server);
			Entity npc = server.Spawn(Archetypes.Npc);
			LogAssert.IsFalse(server.Has<ChatState>(npc) || server.Has<InteractCooldown>(npc),
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

		[TearDown]
		public void TearDown()
		{
			Trigger.DispatchObserved = null;
		}

		// ── Accomplishments ──────────────────────────────────────────────────────

		[Test]
		public void AnAccomplishmentWithNoAchievementsModuleIsANoOp()
		{
			var world = new World();
			var character = new StubCharacter { ID = 1, CoreLink = new EntityLink(world, world.Create()) };
			var template = ScriptableObject.CreateInstance<AchievementTemplate>();
			template.name = "Test_NoModule";
			try
			{
				Assert.DoesNotThrow(() => Accomplishments.Record(character, template), "a game without the Achievements module counts nothing, and breaks nothing");
				Assert.DoesNotThrow(() => Accomplishments.Record(character, null));
				Assert.DoesNotThrow(() => Accomplishments.Record(null, template));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(template);
			}
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

		// ── Participant rows ─────────────────────────────────────────────────────

		[Test]
		public void RowsMergeAcrossCharacters_LeavingOutThoseUnderAnotherClaim()
		{
			var a = new CharacterRows<CharacterAchievementData>(row => row.CharacterID);
			var b = new CharacterRows<CharacterAchievementData>(row => row.CharacterID);
			b.Items.Add(new CharacterAchievementData(0, 1, 100, 1, 0, 1));
			b.Items.Add(new CharacterAchievementData(0, 1, 200, 1, 0, 1));
			a.AddFrom(b, new HashSet<long> { 200 });
			LogAssert.AreEqual(1, a.Count, "a conflicting claim's rows stay out of the merged flush");
			LogAssert.AreEqual(100L, a.Items[0].CharacterID);
			a.AddFrom(b, null);
			LogAssert.AreEqual(3, a.Count, "with no conflict every row is merged");
		}

		// ── The character save and load call participants ────────────────────────

		private const string SavingPath = "Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";
		private const string LoadingPath = "Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs";

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
			int apply = loading.IndexOf("participant.Apply(character, fetched);", StringComparison.Ordinal);
			int spawn = loading.IndexOf("ServerManager.Spawn(character.NetworkObject", StringComparison.Ordinal);
			LogAssert.IsTrue(link > 0 && mute > link && apply > link, "the entity exists before any loaded state is put on it");
			LogAssert.IsTrue(spawn > apply, "and everything is in place before the character spawns");
			LogAssert.IsTrue(loading.Contains("CharacterFetch fetched = await participant.FetchAsync(this, charData);"), "the participants fetch in the load");
			LogAssert.IsTrue(loading.Contains("await AbandonLoadAfterFetchFailure(participant.Name, fetched.ErrorCode, fetched.ErrorMessage);"),
				"and a failed fetch abandons the load, so nothing is saved over unloaded state");
		}

		[Test]
		public void TheDepartureCapturesFromTheEntity_BeforeTheDespawnEndsIt()
		{
			string saving = Read(SavingPath);
			string departure = Body(saving, "private void SaveAndDespawnCharacter(", "private CharacterData BuildCharacterData(");
			int capture = departure.IndexOf("AppendDepartureSubEntities(character, subEntities, sessionInfo);", StringComparison.Ordinal);
			int despawn = departure.IndexOf("PersistentPool.Despawn(", StringComparison.Ordinal);
			LogAssert.IsTrue(capture > 0 && despawn > capture, "the departure's rows (a module's live state among them) are read off the entity before the despawn releases it");
		}
	}
}
