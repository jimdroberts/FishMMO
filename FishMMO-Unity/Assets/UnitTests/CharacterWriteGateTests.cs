using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Server.Core;
using FishMMO.Server.Implementation;
using FishMMO.Server.Implementation.World.SceneServer;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that every per-character write lands only while the writer still holds that
	/// character's session claim (O27), that a pet's unrestorable rows are pruned by its pet-row write
	/// (O37), and that permanent buffs are neither saved nor restored (O11).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The defect O27 closes: the character row's write carried the ownership triple, but its
	/// sub-entity tables were guarded only by their version, which does not say who is writing. A
	/// write captured by a session released a moment later could land after the release — over an
	/// offline tax debit, over a trade's last settlement, or over the next owner's first save, whose
	/// versions restart from the rows it loaded. The item layer's own assertion accepted an unclaimed
	/// row from any writer, which is exactly what a just-released character is.
	/// </para>
	/// <para>
	/// The gate's SQL — the share lock, the re-check against a release that commits while the write
	/// waits, the release that waits behind a write in flight, the refused late write after an
	/// offline debit, the same for items and a pet table, and a stress run for deadlocks — was
	/// validated against a throwaway PostgreSQL through the real services; it cannot run here. What
	/// is pinned here is the pure decisions and the shape that carries a claim to every write.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CharacterWriteGateTests
	{
		private const string SavingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";
		private const string LoadingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs";
		private const string InventoryPath = "Assets/Scripts/Server/Implementation/World/SceneServer/CharacterInventory/CharacterInventorySystem.cs";
		private const string ServicesPath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/Character/";
		private const string CharacterSystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.cs";
		private const string CombatLogoutPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.CombatLogout.cs";
		private const string SceneServerRoot = "Assets/Scripts/Server/Implementation/World/SceneServer/";

		#region The whole-write decision

		[Test]
		public void AWriteWithAnyWritableCharacter_Writes()
		{
			LogAssert.AreEqual(CharacterWriteGateDecision.Write, CharacterWriteGate.Decide(3, 3, 3), "all owned");
			LogAssert.AreEqual(CharacterWriteGateDecision.Write, CharacterWriteGate.Decide(3, 2, 1),
				"one owned among a missing and an unowned one: its rows are written, the rest filtered");
		}

		[Test]
		public void AWriteWhoseCharactersExistButAreNotOurs_IsForbidden()
		{
			LogAssert.AreEqual(CharacterWriteGateDecision.NotOwned, CharacterWriteGate.Decide(2, 2, 0),
				"a released or reclaimed character refuses the whole write — the code a refused row save uses");
			LogAssert.AreEqual(CharacterWriteGateDecision.NotOwned, CharacterWriteGate.Decide(2, 1, 0),
				"even when some are also gone: ownership is the more specific answer");
		}

		[Test]
		public void AWriteNamingOnlyMissingCharacters_IsNotFound()
		{
			LogAssert.AreEqual(CharacterWriteGateDecision.NotFound, CharacterWriteGate.Decide(2, 0, 0),
				"the answer every service gave before the gate, kept");
		}

		[Test]
		public void AnEmptyWrite_HasNothingToRefuse()
		{
			LogAssert.AreEqual(CharacterWriteGateDecision.Write, CharacterWriteGate.Decide(0, 0, 0), "nothing named");
		}

		[Test]
		public void AnOwnedWriteWithoutClaims_IsRejectedBeforeItStarts()
		{
			LogAssert.IsNotNull(CharacterWriteGate.ValidateClaims(null), "no claims at all");
			LogAssert.IsNotNull(CharacterWriteGate.ValidateClaims(Array.Empty<CharacterSessionLeaseData>()), "an empty set of claims");
			LogAssert.IsNull(CharacterWriteGate.ValidateClaims(new[] { new CharacterSessionLeaseData(1, 2, Guid.NewGuid()) }), "a claim");
		}

		#endregion

		#region Refused rows never clear dirty marks

		private static DatabaseResult<BulkWriteResult> Ok(int supplied, int attempted, int applied, int unowned = 0)
		{
			return DatabaseResult<BulkWriteResult>.Success(new BulkWriteResult(supplied, attempted, applied, unowned));
		}

		[Test]
		public void UnownedRows_AreFiltered()
		{
			var write = new BulkWriteResult(5, 3, 3, 2);
			LogAssert.AreEqual(2, write.Filtered, "a refused row was never attempted");
			LogAssert.AreEqual(2, write.Unowned, "and says why");
			LogAssert.AreEqual(0, write.Superseded, "it is not a lost version race");

			BulkWriteResult sum = write + new BulkWriteResult(1, 0, 0, 1);
			LogAssert.AreEqual(3, sum.Unowned, "a two-statement service adds them up");
			LogAssert.AreEqual(3, sum.Filtered, "and they stay part of the filtered count");
			LogAssert.AreEqual(0, new BulkWriteResult(4, 4, 4).Unowned, "the three-count constructor refuses nothing");
		}

		[Test]
		public void OnlyAWriteThatAttemptedEveryRow_ClearsDirtyMarks()
		{
			LogAssert.IsTrue(BulkWriteReporting.MayClearDirtyMarks(Ok(4, 4, 4)), "all written");
			LogAssert.IsTrue(BulkWriteReporting.MayClearDirtyMarks(Ok(4, 4, 1)),
				"superseded rows are safe: the database holds something newer");
			LogAssert.IsFalse(BulkWriteReporting.MayClearDirtyMarks(Ok(4, 2, 2, 2)),
				"rows the ownership gate refused were never stored");
			LogAssert.IsFalse(BulkWriteReporting.MayClearDirtyMarks(Ok(4, 3, 3)),
				"nor were rows filtered for any other reason");
			LogAssert.IsFalse(BulkWriteReporting.MayClearDirtyMarks(
				DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.Forbidden, "claim lost")),
				"a write refused whole clears nothing");
		}

		[Test]
		public void TheCharacterSave_ClearsItsMarksThroughTheOneRule()
		{
			string source = CodeOnly(ReadSource(SavingPath));
			LogAssert.IsFalse(source.Contains("Filtered == 0"),
				"every mark clear goes through BulkWriteReporting.MayClearDirtyMarks, not a hand-written test");
			LogAssert.AreEqual(6, Occurrences(source, "BulkWriteReporting.MayClearDirtyMarks(result)"),
				"attributes, abilities, achievements, factions, known abilities and archetypes");
		}

		#endregion

		#region Every character-system write quotes a claim

		[Test]
		public void TheSubEntitySnapshot_CarriesTheClaimItsRowsWereCapturedUnder()
		{
			string source = ReadSource(SavingPath);
			LogAssert.IsTrue(source.Contains("public readonly Dictionary<long, CharacterSessionLeaseData> Claims;"),
				"the snapshot carries a claim per character");

			string capture = CodeOnly(MethodBody(source,
				"private void AppendSubEntities(IPlayerCharacter character, SubEntitySnapshot snapshot, CharacterSessionInfo? claim)",
				"private void EnqueueSubEntitySaves("));
			AssertOrdered(capture, "if (!claim.HasValue)", "return;", "a character with no claim is not captured at all");
			AssertOrdered(capture, "snapshot.Claims[character.ID] =", "AppendAttributeData(", "and a captured one records its claim first");
		}

		[Test]
		public void TheCharacterSave_WritesEverySubEntityTableThroughTheGate()
		{
			string source = CodeOnly(ReadSource(SavingPath));
			foreach (string service in new[]
			{
				"attrService", "abilityService", "achievementService", "factionService", "knownAbilityService",
				"archetypeService", "petService", "petAttributeService", "petBuffService", "hotkeyService",
			})
			{
				LogAssert.IsTrue(source.Contains($"await {service}.PersistOwnedAsync("), $"{service} writes through the ownership gate");
				LogAssert.IsFalse(source.Contains($"await {service}.PersistAsync("), $"and never through the ungated write");
			}
		}

		[Test]
		public void ADepartureThatCannotBeEnqueued_CarriesItsSubEntitiesInThePendingRelease()
		{
			/* Refused, not merely late, if they landed after the release: so they travel with it. */
			string body = CodeOnly(MethodBody(ReadSource(SavingPath),
				"private void SaveAndDespawnCharacter(", "private CharacterData BuildCharacterData("));
			LogAssert.IsTrue(body.Contains("QueuePendingFlush(charData.ID, charData, sessionInfo, itemFlush, subEntities);"),
				"the pending release carries the sub-entity rows");
			LogAssert.IsFalse(body.Contains("EnqueueSubEntitySaves("),
				"which no longer go out on their own lane, where they could land after the release");
		}

		[Test]
		public void ThePendingRelease_WaitsForItsSubEntityRows()
		{
			string body = MethodBody(ReadSource(SavingPath), "private async Task RunPendingFlushAsync(", "#endregion");
			AssertOrdered(body, "SaveSubEntitiesSequentiallyAsync(subEntities, characterID)", "ReleaseCharacterSessionAsync(",
				"the rows are written before the claim is handed back");
			LogAssert.IsTrue(body.Contains("session.HasValue && flushDone && subEntitiesDone"),
				"and a table still failing keeps the claim for the next attempt");
		}

		[Test]
		public void AppendingSnapshots_NeverWritesOneSessionsRowsUnderAnothersClaim()
		{
			/* The shutdown flush appends every resident's snapshot, then whatever the retry queue still
			 * holds. A queued row from an OLDER session of the same character, appended under the
			 * current claim, could beat the current session's row on version. The first claim recorded
			 * for a character wins and rows captured under any other are left out. */
			Type snapshotType = typeof(CharacterSystem).GetNestedType("SubEntitySnapshot", BindingFlags.NonPublic);
			LogAssert.IsNotNull(snapshotType, "CharacterSystem.SubEntitySnapshot must still exist");
			MethodInfo addFrom = snapshotType.GetMethod("AddFrom");

			Guid current = Guid.NewGuid();
			Guid older = Guid.NewGuid();
			object merged = Snapshot(snapshotType);
			addFrom.Invoke(merged, new[] { Snapshot(snapshotType, (18, current, 1), (19, current, 1)) });
			addFrom.Invoke(merged, new[] { Snapshot(snapshotType, (18, older, 2)) });
			addFrom.Invoke(merged, new[] { Snapshot(snapshotType, (18, current, 3)) });

			var attributes = (List<CharacterAttributeData>)snapshotType.GetField("Attributes").GetValue(merged);
			var claims = (Dictionary<long, CharacterSessionLeaseData>)snapshotType.GetField("Claims").GetValue(merged);
			LogAssert.AreEqual(3, attributes.Count, "the current session's rows, from both appends, and character 19's");
			LogAssert.IsFalse(attributes.Exists(a => a.TemplateID == 2), "the older session's row is left out");
			LogAssert.AreEqual(current, claims[18].OwnerToken, "and the claim stays the one recorded first");
		}

		[Test]
		public void ThePendingQueue_ReplacesRowsFromAnEarlierSession_RatherThanMergingThem()
		{
			/* The opposite order to the shutdown flush: a newer departure's rows replace an older
			 * session's still queued, and are never merged under one claim. */
			string body = CodeOnly(MethodBody(ReadSource(SavingPath),
				"private void QueuePendingFlush(", "private static bool SameClaims("));
			AssertOrdered(body, "SameClaims(entry.SubEntities, subEntities)", "merged.AddFrom(subEntities);",
				"only snapshots under the same claims are merged");
			LogAssert.IsTrue(body.Contains("entry.SubEntities = subEntities;"), "anything else is replaced by the newer session's");
		}

		private static object Snapshot(Type snapshotType, params (long Character, Guid Token, int Template)[] rows)
		{
			object snapshot = Activator.CreateInstance(snapshotType, new object[] { 1 });
			var attributes = (List<CharacterAttributeData>)snapshotType.GetField("Attributes").GetValue(snapshot);
			var claims = (Dictionary<long, CharacterSessionLeaseData>)snapshotType.GetField("Claims").GetValue(snapshot);
			foreach (var row in rows)
			{
				claims[row.Character] = new CharacterSessionLeaseData(row.Character, 3, row.Token);
				attributes.Add(new CharacterAttributeData(0, 5, row.Character, row.Template, 1, 0f));
			}
			return snapshot;
		}

		#endregion

		#region Every other system's per-character write quotes a claim too

		[Test]
		public void EveryRequestTimeWrite_QuotesTheClaimItWasMadeUnder()
		{
			/* O27, finished: the writes other systems make for a resident character used the ungated
			 * path, so a lapsed lease let them land over the owner's state until the eviction. Each
			 * now captures the claim from SessionTokens with the request and quotes it. */
			AssertOwned("Ability/AbilitySystem.cs",
				new[]
				{
					"abilityService.PersistOwnedAsync(request.AbilityData, request.Claim)",
					"abilityService.DeleteAbilityOwnedAsync(characterID, abilityID, version, claim)",
					"abilityService.DeleteAbilityOwnedAsync(characterID, abilityID, long.MaxValue, claim, admitReleased: true)",
				},
				new[] { "abilityService.PersistAsync(", "abilityService.DeleteAbilityAsync(" });
			AssertOwned("Quest/QuestSystem.cs",
				new[] { "service.PersistOwnedAsync(new[] { dto }, claims)", "service.DeleteQuestOwnedAsync(characterID, templateID, version, claim)" },
				new[] { "service.PersistAsync(", "service.DeleteQuestAsync(" });
			AssertOwned("Hotkey/HotkeySystem.cs",
				new[] { "hotkeyService.PersistOwnedAsync(hotkeys, ClaimsOf(claim))" },
				new[] { "hotkeyService.PersistAsync(" });
			AssertOwned("Interactable/InteractableSystem.Merchant.cs",
				new[] { "knownAbilityService.PersistOwnedAsync(", "service.PersistOwnedAsync(dtos, ClaimsOf(claim))" },
				new[] { "knownAbilityService.PersistAsync(", "service.PersistAsync(dtos)" });
			AssertOwned("Achievement/AchievementSystem.cs",
				new[] { "service.PersistOwnedAsync(" },
				new[] { "service.PersistAsync(" });
			AssertOwned("SceneServer/SceneServerSystem.AdminCommands.Economy.cs",
				new[] { "attributeService.PersistOwnedAsync(dtos, ClaimsOf(claim))" },
				new[] { "attributeService.PersistAsync(" });
			AssertOwned("Guild/GuildSystem.cs",
				new[] { "attributeService.PersistOwnedAsync(dtos, ClaimsOf(claim))" },
				new[] { "attributeService.PersistAsync(" });
			AssertOwned("Housing/HousingSystem.Plots.cs",
				new[] { "attributeService.PersistOwnedAsync(dtos, ClaimsOf(claim))" },
				new[] { "attributeService.PersistAsync(" });
			AssertOwned("Pet/PetSystem.cs",
				new[] { "charPetService.PersistOwnedAsync(new[] { petData }, ClaimsOf(claim))" },
				new[] { "charPetService.PersistAsync(" });
		}

		[Test]
		public void TheOfflineTaxDebit_StaysUngated_BecauseNoServerHoldsTheCharacter()
		{
			/* The one per-character write left ungated on purpose: it bills an owner who has logged
			 * out, from the stored row, and the ownership assertion before it proves — under the row
			 * lock, for the whole transaction — that NO server holds a claim. There is none to quote. */
			string body = CodeOnly(MethodBody(ReadSource(SceneServerRoot + "Housing/HousingSystem.Tax.cs"),
				"private async Task<OfflineChargeOutcome> TryChargeWonPeriodFromRowAsync(",
				"private enum OnlineChargeOutcome"));
			AssertOrdered(body, "ownershipService.AssertOwnershipAsync(characterID, default, allowUnclaimed: true)",
				"attributeService.PersistAsync(",
				"the stored-row debit runs only after proving the character unclaimed, inside the same unit of work");
		}

		[Test]
		public void ARequestWithNoClaim_IsRefusedBeforeAnythingChanges()
		{
			string ability = ReadSource(SceneServerRoot + "Ability/AbilitySystem.cs");
			string grant = CodeOnly(MethodBody(ability, "public bool TryGrantAbility(", "private async Task PersistGrantedAbilityAsync("));
			AssertOrdered(grant, "TryCaptureSessionClaim(characterID, out CharacterSessionLeaseData claim)", "new Ability(template, craftedEvents)",
				"a grant with nothing to write under is refused before it is built");
			string forget = CodeOnly(MethodBody(ability, "public void OnServerAbilityForgetBroadcastReceived(", "private async Task ForgetAbilityAsync("));
			AssertOrdered(forget, "TryCaptureSessionClaim(characterID, out CharacterSessionLeaseData claim)", "TryEnqueueAsyncWork(",
				"and a forget, answered as the write failing");

			string merchant = CodeOnly(MethodBody(ReadSource(SceneServerRoot + "Interactable/InteractableSystem.Merchant.cs"),
				"private MerchantPurchaseFailure LearnAbilityGeneric<", "private async Task PersistKnownAbilityAsync("));
			AssertOrdered(merchant, "TryCaptureSessionClaim(charID, out CharacterSessionLeaseData claim)", "learnFunc(abilityController,",
				"a purchase is refused before anything is learned or charged");

			string economy = CodeOnly(MethodBody(ReadSource(SceneServerRoot + "SceneServer/SceneServerSystem.AdminCommands.Economy.cs"),
				"private void ChangeCurrency(", "private void GiveItem("));
			AssertOrdered(economy, "TryCaptureSessionClaim(target.ID, out _)", "currency.SetValue(",
				"an operator's balance change is refused before the balance moves");
		}

		[Test]
		public void TheDepartingHotkeyBar_TravelsWithTheSaveAndRelease()
		{
			/* Flushed from OnDisconnect it was a write of its own, kept ahead of the release only by the
			 * lane order — and at shutdown the hotkey system tears down, and flushed, BEFORE the
			 * character system releases every claim. Gated, a late bar is refused, not late. */
			string saving = ReadSource(SavingPath);
			string despawn = CodeOnly(MethodBody(saving, "private void SaveAndDespawnCharacter(", "private CharacterData BuildCharacterData("));
			LogAssert.IsTrue(despawn.Contains("AppendDepartureSubEntities(character, subEntities, sessionInfo);"),
				"the logout captures the departure's rows, the bar among them");
			string departure = CodeOnly(MethodBody(saving, "private void AppendDepartureSubEntities(", "private void EnqueueSubEntitySaves("));
			AssertOrdered(departure, "AppendSubEntities(character, snapshot, claim);", "TakeDepartingBar(character.ID, character.Hotkeys)",
				"which is the ordinary capture plus the live bar, only with a claim");
			string sequential = CodeOnly(MethodBody(saving, "private async Task<bool> SaveSubEntitiesSequentiallyAsync(", "private const int MaxSubEntityWriteAttempts"));
			LogAssert.IsTrue(sequential.Contains("SaveHotkeysAsync(s.Hotkeys, claims)"), "and writes it before the release, under the claim");

			LogAssert.AreEqual(2, Occurrences(CodeOnly(ReadSource(CombatLogoutPath)), "AppendDepartureSubEntities("),
				"the reattach and the linger's own snapshot capture it too");
			string shutdown = CodeOnly(MethodBody(ReadSource(CharacterSystemPath), "private bool TryCaptureShutdownEntry(", "/// <summary>"));
			LogAssert.IsTrue(shutdown.Contains("AppendDepartureSubEntities("), "and so does the shutdown flush");

			string hotkeys = ReadSource(SceneServerRoot + "Hotkey/HotkeySystem.cs");
			LogAssert.IsFalse(CodeOnly(hotkeys).Contains("OnDisconnect +="), "the hotkey system no longer writes on disconnect");
			string teardown = CodeOnly(MethodBody(hotkeys, "public override void OnDeinitialize()", "protected override void OnUpdate("));
			LogAssert.IsFalse(teardown.Contains("FlushPendingHotkeyWrites()"), "nor from its own teardown, which runs before the claims are released");
		}

		[Test]
		public void NoReleaseOvertakesTheCharactersQueuedWrites()
		{
			/* Every write another system queued for a character sits on its ordered lane and quotes the
			 * claim; one still queued when the claim is handed back is refused. The save-and-release is
			 * on that lane; these are the releases that were not. */
			string saving = CodeOnly(ReadSource(SavingPath));
			LogAssert.IsTrue(saving.Contains("EnqueueAsyncWork(() => ReleaseSessionWithRetryAsync(characterID, sessionInfo), characterID)"),
				"a plain release queues behind the character's lane");
			LogAssert.IsTrue(saving.Contains("EnqueueAsyncWork(() => RunPendingFlushAsync(characterID, pending), characterID)"),
				"and so does every retry of a pending save-and-release");

			string lane = CodeOnly(MethodBody(ReadSource(CharacterSystemPath), "private async Task FlushAndReleaseForShutdownAsync(", "private const int ShutdownLaneDrainTimeoutMs"));
			AssertOrdered(lane, "DrainCharacterLaneAsync(asyncWorker, entry.CharacterID", "ReleaseCharacterSessionAsync(",
				"the shutdown flush, which runs off the lanes, waits for the character's before its release");
		}

		[Test]
		public void AHotkeyBarRefusedByTheGate_IsNotRestaged()
		{
			LogAssert.IsFalse(HotkeySystem.RestagesAfter(Ok(4, 4, 4)), "a complete bar is done");
			LogAssert.IsTrue(HotkeySystem.RestagesAfter(Ok(4, 4, 2)), "a short one is written again from the live bar");
			LogAssert.IsTrue(HotkeySystem.RestagesAfter(Ok(4, 3, 3)), "as is one the service filtered");
			LogAssert.IsFalse(HotkeySystem.RestagesAfter(Ok(4, 0, 0, 4)), "but not one refused row by row for a lost claim");
			LogAssert.IsFalse(HotkeySystem.RestagesAfter(
				DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.Forbidden, "claim lost")), "nor one refused whole");
			LogAssert.IsTrue(HotkeySystem.RestagesAfter(
				DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.DatabaseError, "down", isTransient: true)), "a database fault is retried");
		}

		[Test]
		public void AClaimRefusal_IsForbiddenAndNothingElse()
		{
			LogAssert.IsTrue(ServerBehaviour.IsClaimRefusal(DatabaseResult.Failure(DatabaseErrorCodes.Forbidden, "claim lost")), "the gate's refusal");
			LogAssert.IsFalse(ServerBehaviour.IsClaimRefusal(DatabaseResult.Failure(DatabaseErrorCodes.StaleState, "stale")), "a lost version race is not one");
			LogAssert.IsFalse(ServerBehaviour.IsClaimRefusal(DatabaseResult.Success()), "a success is not one");
			LogAssert.IsTrue(ServerBehaviour.IsClaimRefusal(DatabaseResult<long>.Failure(DatabaseErrorCodes.Forbidden, "claim lost")), "for a typed result too");
		}

		[Test]
		public void ASingleRowOwnedWrite_NeedsAClaimForItsOwnCharacter()
		{
			LogAssert.IsNotNull(CharacterWriteGate.ValidateClaim(default, 18), "no claim");
			LogAssert.IsNotNull(CharacterWriteGate.ValidateClaim(new CharacterSessionLeaseData(19, 3, Guid.NewGuid()), 18), "another character's claim");
			LogAssert.IsNull(CharacterWriteGate.ValidateClaim(new CharacterSessionLeaseData(18, 3, Guid.NewGuid()), 18), "its own");
		}

		[Test]
		public void ADeadOwnersPet_IsNotOut()
		{
			/* Death dismisses the pet, but the kill handler that does it runs after the character
			 * system's, which finalises a killed combat-logout body — capturing the pet as still out —
			 * before the dismissal can. The rule is stated where the row is built. */
			LogAssert.IsTrue(CharacterSystem.IsPetOut(40f, ownerDead: false), "a live pet of a live owner");
			LogAssert.IsFalse(CharacterSystem.IsPetOut(0f, ownerDead: false), "a dead pet");
			LogAssert.IsFalse(CharacterSystem.IsPetOut(40f, ownerDead: true), "a live pet of a dead owner");

			string capture = CodeOnly(MethodBody(ReadSource(SavingPath), "private void AppendPetData(", "public static bool IsPetOut("));
			LogAssert.IsTrue(capture.Contains("spawned: IsPetOut(currentHealth, character.IsFlagged(CharacterFlags.IsDead))"),
				"the pet row the save captures applies the rule");
		}

		[Test]
		public void TheSingleRowOwnedWrites_AreAdmittedByTheGate()
		{
			string ability = ReadServiceOrIgnore("CharacterAbilityService");
			string persist = CodeOnly(MethodBody(ability, "private async Task<DatabaseResult<long>> PersistOneAsync(", "public Task<DatabaseResult<BulkWriteResult>> PersistAsync("));
			AssertOrdered(persist, "CharacterWriteGate.AdmitOneAsync(dbContext, abilityData.CharacterID, claim,", "INSERT INTO {TableName}",
				"the grant is admitted before its row is written, in the same transaction");
			string delete = CodeOnly(MethodBody(ability, "private async Task<DatabaseResult> DeleteAbilityCoreAsync(", "public async Task<DatabaseResult<IReadOnlyList<CharacterAbilityData>>> FetchAsync("));
			AssertOrdered(delete, "CharacterWriteGate.AdmitOwnOrReleasedAsync(dbContext, held,", "await delete(dbContext)",
				"the revoke may also undo its row on a released character");
			AssertOrdered(delete, "CharacterWriteGate.AdmitOneAsync(dbContext, characterId, held,", "await delete(dbContext)",
				"and the forget only under the claim");

			string quest = CodeOnly(MethodBody(ReadServiceOrIgnore("CharacterQuestService"),
				"private async Task<DatabaseResult> DeleteQuestCoreAsync(", "public async Task<DatabaseResult<IReadOnlyList<CharacterQuestData>>> FetchAsync("));
			AssertOrdered(quest, "CharacterWriteGate.AdmitOneAsync(dbContext, characterId, held,", "await delete(dbContext)",
				"the quest delete is admitted before its tombstone is written");

			string gate = MethodBody(ReadServiceOrIgnore("CharacterWriteGate"),
				"internal static async Task AdmitOwnOrReleasedAsync(", "public static string? ValidateClaim(");
			LogAssert.IsTrue(gate.Contains("FOR SHARE OF c"), "the revoke's admission holds the same share lock");
			LogAssert.IsTrue(gate.Contains("c.session_state <> @p3") && gate.Contains("c.session_owner_server_id = 0"),
				"and admits an unclaimed character by the ownership assertion's own definition, nothing wider");
		}

		private static void AssertOwned(string relativeToSceneServer, string[] owned, string[] ungated)
		{
			string source = CodeOnly(ReadSource(SceneServerRoot + relativeToSceneServer));
			foreach (string call in owned)
			{
				LogAssert.IsTrue(source.Contains(call), $"{relativeToSceneServer} writes through the ownership gate: {call}");
			}
			foreach (string call in ungated)
			{
				LogAssert.IsFalse(source.Contains(call), $"{relativeToSceneServer} no longer writes through the ungated {call}");
			}
		}

		#endregion

		#region The item gate

		[Test]
		public void AnItemWriteThatCarriesAClaim_MustFindThatClaim()
		{
			LogAssert.IsFalse(CharacterInventorySystem.AllowsUnclaimedItemWrite(new CharacterSessionLeaseData(18, 3, Guid.NewGuid())),
				"a released character's unclaimed row does not accept a write captured under its old claim");
			LogAssert.IsTrue(CharacterInventorySystem.AllowsUnclaimedItemWrite(default),
				"a write with no triple at all keeps the old fallback");
		}

		[Test]
		public void EveryItemAssertion_UsesTheRule()
		{
			string source = CodeOnly(ReadSource(InventoryPath));
			LogAssert.IsFalse(source.Contains("allowUnclaimed: true"), "no assertion accepts an unclaimed row unconditionally");
			LogAssert.AreEqual(3, Occurrences(source, "allowUnclaimed: AllowsUnclaimedItemWrite("),
				"the batch and both legs of an exchange");
		}

		#endregion

		#region The database services

		[Test]
		public void EverySubEntityBatch_IsAdmittedByTheGate()
		{
			foreach (string service in new[]
			{
				"CharacterAttributeService", "CharacterAbilityService", "CharacterAchievementService", "CharacterArchetypeService",
				"CharacterFactionService", "CharacterHotkeyService", "CharacterItemCooldownService", "CharacterKnownAbilityService",
				"CharacterPetService", "CharacterPetAttributeService", "CharacterPetBuffService", "CharacterQuestService",
				"CharacterSkillService",
			})
			{
				string source = ReadServiceOrIgnore(service);
				LogAssert.IsTrue(source.Contains("public Task<DatabaseResult<BulkWriteResult>> PersistOwnedAsync("), $"{service} offers the owned write");
				LogAssert.IsTrue(source.Contains("CharacterWriteGate.AdmitAsync(dbContext,"), $"{service} admits its characters through the gate");
				LogAssert.IsFalse(CodeOnly(source).Contains("activeCharacterIdSet"), $"{service} has no hand-rolled existence check left");
			}
		}

		[Test]
		public void APetRowWrite_PrunesTheRowsItsPetCannotRestore()
		{
			string source = ReadServiceOrIgnore("CharacterPetService");
			string batch = CodeOnly(MethodBody(source,
				"private async Task<DatabaseResult<BulkWriteResult>> PersistBatchAsync(IEnumerable<CharacterPetData> pets",
				"private static async Task PruneUnrestorableRowsAsync("));
			AssertOrdered(batch, "ExecuteBulkUpsertAsync(", "PruneUnrestorableRowsAsync(dbContext, admittedCharacterIds",
				"the prune runs after the pet rows, against what the transaction leaves stored, for admitted characters only");

			string prune = MethodBody(source, "private static async Task PruneUnrestorableRowsAsync(", "/// <inheritdoc/>");
			LogAssert.IsTrue(prune.Contains("AND p.version = d.version"),
				"a row survives only if it matches its pet row's version — the restore's own test");
			LogAssert.IsTrue(prune.Contains("GetTableName<CharacterPetBuffEntity>()") && prune.Contains("GetTableName<CharacterPetAttributeEntity>()"),
				"both dependent tables");
		}

		#endregion

		#region Permanent buffs (O11)

		[Test]
		public void ABuffWithNoTemplate_IsNotPersisted()
		{
			LogAssert.IsFalse(CharacterSystem.IsPersistedBuff(null), "nothing to restore it as");
		}

		[Test]
		public void PermanentBuffs_AreNeitherSavedNorRestored()
		{
			string rule = CodeOnly(MethodBody(ReadSource(SavingPath),
				"public static bool IsPersistedBuff(BaseBuffTemplate template)", "private void AppendAttributeData("));
			LogAssert.IsTrue(rule.Contains("template != null && !template.IsPermanent"), "a permanent template is not persisted");

			string capture = CodeOnly(MethodBody(ReadSource(SavingPath),
				"private List<CharacterBuffData> CaptureBuffSet(IPlayerCharacter character, long version)",
				"public static bool IsPersistedBuff("));
			LogAssert.IsTrue(capture.Contains("!IsPersistedBuff(buff.Template)"),
				"the set leaves them out, which deletes their rows");

			string pet = CodeOnly(MethodBody(ReadSource(SavingPath),
				"private void AppendPetBuffData(", "private async Task<bool> SavePetsAsync("));
			LogAssert.IsTrue(pet.Contains("!IsPersistedBuff(buff.Template)"), "and so does a pet's");

			string load = CodeOnly(ReadSource(LoadingPath));
			AssertOrdered(load, "!IsPersistedBuff(buffTemplate)", "Buff newBuff = new Buff(buff.TemplateID, expiryTick",
				"an old row is skipped before it is restored as a buff that expires on its first tick");
		}

		#endregion

		#region Helpers

		private static string ReadServiceOrIgnore(string service)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), ServicesPath + service + ".cs");
			if (!File.Exists(path))
			{
				Assert.Ignore($"the database project is not checked out beside the Unity project ({path}).");
			}
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>Comment lines stripped, so prose about a construct does not trip a scan for it.</summary>
		private static string CodeOnly(string source)
		{
			var kept = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string t = line.TrimStart();
				if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				kept.Append(line).Append('\n');
			}
			return kept.ToString();
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");
			return source.Substring(start, end - start);
		}

		private static void AssertOrdered(string text, string first, string second, string message)
		{
			int a = text.IndexOf(first, StringComparison.Ordinal);
			int b = a >= 0 ? text.IndexOf(second, a + first.Length, StringComparison.Ordinal) : -1;
			LogAssert.IsTrue(a >= 0 && b > a, $"{message} ('{first}' before '{second}')");
		}

		private static int Occurrences(string text, string needle)
		{
			int count = 0;
			for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}

		#endregion
	}
}
