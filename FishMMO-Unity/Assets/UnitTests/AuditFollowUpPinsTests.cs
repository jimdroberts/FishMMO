using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Source pins for the defects the 2026-09-13 audit of the request-preamble, ability-system and
	/// character-sheet commits closed — each one an ORDERING or a SUBSCRIPTION that compiles either
	/// way round and that no behavioural test can see without a server.
	/// </summary>
	/// <remarks>
	/// Deliberately text scans, in the style of <c>SlotPanelSharingTests</c>: what each exists to
	/// prevent is the old order quietly coming back in a refactor. Where a behaviour could be
	/// exercised cheaply it is, in <c>AbilityKnowledgeReverseIndexTests</c> and
	/// <c>HotkeyBarLiftTests</c>; this fixture holds what could not.
	/// </remarks>
	[TestFixture]
	public class AuditFollowUpPinsTests
	{
		private const string GuildSystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Guild/GuildSystem.cs";
		private const string ChatPanelPath = "Assets/Scripts/Client/GUI/World/Chat/UITKChat.cs";
		private const string AbilitySystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Ability/AbilitySystem.cs";
		private const string AbilityCraftPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.AbilityCraft.cs";
		private const string MerchantPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.Merchant.cs";
		private const string ContainerSystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.Container.cs";
		private const string HotkeySystemPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Hotkey/HotkeySystem.cs";
		private const string SavingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";
		private const string InspectPath = "Assets/Scripts/Client/GUI/World/Inspect/UITKInspect.cs";
		private const string SheetViewPath = "Assets/Scripts/Client/GUI/World/CharacterSheet/CharacterSheetView.cs";
		private const string LootPath = "Assets/Scripts/Client/GUI/World/Loot/UITKLoot.cs";
		private const string ContainerPanelPath = "Assets/Scripts/Client/GUI/World/Container/UITKContainer.cs";
		private const string ControlPath = "Assets/Scripts/Client/GUI/UITKControl.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		private static int Occurrences(string source, string needle)
		{
			int count = 0;
			for (int i = source.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = source.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}

		private static void AssertOrdered(string body, string first, string second, string why)
		{
			int a = body.IndexOf(first, StringComparison.Ordinal);
			int b = body.IndexOf(second, StringComparison.Ordinal);
			LogAssert.IsTrue(a >= 0, $"{first} must still be present");
			LogAssert.IsTrue(b >= 0, $"{second} must still be present");
			LogAssert.IsTrue(a < b, why);
		}

		#region Guild invite claims nothing for a target it cannot reach

		[Test]
		public void GuildInvite_ResolvesTheTarget_BeforeClaimingCooldownOrPendingSlot()
		{
			/* The old order — cooldown, pending slot, THEN target lookup — left an unanswerable
			 * invitation blocking every real invite to an offline or cross-server target until the
			 * TTL, and a spent cooldown, in silence. The party path had already been fixed; this
			 * keeps the guild path level with it. */
			string body = MethodBody(ReadSource(GuildSystemPath),
				"private async Task InviteToGuildAsync(",
				"private void ClearPendingInvitation(long characterID)");

			AssertOrdered(body, "CharactersByID.TryGetValue(targetCharacterID", "TryBeginInviteCooldown(",
				"the target must be resolved before the inviter's cooldown is spent on them");
			AssertOrdered(body, "CharactersByID.TryGetValue(targetCharacterID", "TryAddPendingInvitation(",
				"the target must be resolved before a pending slot is claimed on their behalf");
			LogAssert.IsTrue(body.Contains("SendGuildChatCode(conn, targetCharacterID, ChatHelper.TARGET_OFFLINE)"),
				"a target this server cannot reach is answered, not dropped");
		}

		[Test]
		public void GuildChat_RendersEveryCodeInTheTable()
		{
			/* TARGET_OFFLINE now arrives on the guild channel; a handler matching one hard-coded
			 * code printed it as the raw FISHMMO_ token. */
			string body = MethodBody(ReadSource(ChatPanelPath),
				"public bool OnGuildChat(",
				"public bool OnTellChat(");

			LogAssert.IsTrue(body.Contains("ErrorCodes.TryGetValue(cmd,"),
				"guild chat must look the code up in the table, as party chat does");
		}

		#endregion

		#region Grant payment is settled on completion

		[Test]
		public void CompleteGrant_SettlesBeforeLearning_AndRevokesWhenItCannot()
		{
			/* Charging before the write left a real loss: a persist that failed after the player
			 * had transferred could not be refunded. Now nothing moves until both the row and the
			 * payer are there, and a row that cannot be settled is taken back. */
			string body = MethodBody(ReadSource(AbilitySystemPath),
				"private void CompleteGrant(GrantRequest request)",
				"private void FailGrant(GrantRequest request)");

			AssertOrdered(body, "request.Settle(character)", "LearnAbility(request.Ability)",
				"the charge must be taken before the ability is learned, or a refused charge learns it anyway");
			AssertOrdered(body, "request.Settle(character)", "AbilityAddBroadcast",
				"...and before the player is shown it");
			LogAssert.AreEqual(2, Occurrences(body, "RevokeUnsettledGrant(request"),
				"both a departed character and a refused settlement revoke the row");
		}

		[Test]
		public void CraftAndPurchase_ChargeOnlyInsideTheirSettleCallback()
		{
			string craft = MethodBody(ReadSource(AbilityCraftPath),
				"public void OnServerAbilityCraftBroadcastReceived(",
				"\n\t}\n}");
			string sale = MethodBody(ReadSource(MerchantPath),
				"private bool TryPurchasePremadeAbility(",
				"private MerchantPurchaseFailure LearnAbilityGeneric<");

			foreach (var (name, body, settle, answer) in new[]
			{
				("craft", craft, "bool SettleCraft(", "void AnswerRejectedCraft("),
				("sale", sale, "bool SettleSale(", "void AnswerRejectedSale("),
			})
			{
				LogAssert.AreEqual(1, Occurrences(body, "CharacterCurrency.TrySpend("),
					$"the {name} spends exactly once");
				string settleBody = MethodBody(body, settle, answer);
				LogAssert.IsTrue(settleBody.Contains("CharacterCurrency.TrySpend("),
					$"...and that once is inside the settle callback, which AbilitySystem runs on completion");
				LogAssert.IsTrue(body.Contains("settle: Settle"),
					$"the {name} hands its charge to TryGrantAbility as settle:");
				LogAssert.IsFalse(body.Contains("CharacterCurrency.TryAdd("),
					$"the {name} has no refund: nothing was charged before the grant landed");
			}
		}

		#endregion

		#region Refusals that must be answered are answered, and throttled

		[Test]
		public void ContainerTake_TakesTheThrottle_BeforeResolvingTheCharacter()
		{
			/* A state-gate reply sent before the throttle is a reply the throttle does not cover:
			 * a dead client spamming a chest slot got one answer per packet. */
			string body = MethodBody(ReadSource(ContainerSystemPath),
				"private void OnServerContainerTakeItemBroadcastReceived(",
				"private void SendContainerResult(");

			AssertOrdered(body, "TryBeginIngressGuard(", "GetComponent<IPlayerCharacter>()",
				"the ingress guard must be taken before the character is resolved, so every answer is throttled");
		}

		[Test]
		public void HotkeyHandlers_EchoTheAuthoritativeSlot_WhenTheStateGateRefuses()
		{
			/* The client applies a binding the instant the icon is dropped, so a silent refusal
			 * leaves it holding a binding the server never took until the next login. */
			string source = ReadSource(HotkeySystemPath);

			string single = MethodBody(source,
				"public void OnServerHotkeySetBroadcastReceived(",
				"public void OnServerHotkeySetMultipleBroadcastReceived(");
			LogAssert.IsTrue(single.Contains("PlayerRequestGate.SkipCanAct"),
				"the single-slot handler applies the state gate itself so it can answer");
			AssertOrdered(single, "CharacterStateValidation.CanAct(playerCharacter)", "AcknowledgeHotkey(conn, playerCharacter, msg.HotkeyData.Slot)",
				"...and answers the refusal with the authoritative slot");

			/* Ended at the acknowledgement helpers, which are declared directly after the bulk handler.
			 * It used to end at TryApplyHotkey, which now sits earlier in the file, so the slice could no
			 * longer find its end and the pin failed against a handler that still did the right thing. */
			string multiple = MethodBody(source,
				"public void OnServerHotkeySetMultipleBroadcastReceived(",
				"private void AcknowledgeHotkey(");
			LogAssert.IsTrue(multiple.Contains("PlayerRequestGate.SkipCanAct"),
				"the bulk handler applies the state gate itself so it can answer");
			AssertOrdered(multiple, "CharacterStateValidation.CanAct(playerCharacter)", "AcknowledgeAllHotkeys(conn, playerCharacter)",
				"...and answers the refusal with the whole bar");
		}

		#endregion

		#region The despawn save groups abilities as the periodic save does

		[Test]
		public void DespawnSave_GroupsAbilityRowsByCharacter()
		{
			/* The ability upsert validates a batch whole, so a pet whose character row is gone took
			 * its owner's rows down with it on the very save the next server reads under. */
			string body = MethodBody(ReadSource(SavingPath),
				"private async Task<bool> SaveSubEntitiesSequentiallyAsync(SubEntitySnapshot s, long characterID)",
				"private void SaveAndDespawnCharacter(");

			LogAssert.IsTrue(body.Contains("s.Abilities.GroupBy(a => a.CharacterID)"),
				"the despawn path groups ability rows per character, as EnqueueSubEntitySaves does");
		}

		#endregion

		#region The inspect window stays live

		[Test]
		public void InspectWindow_FollowsTheSubjectsEquipment_AndLetsGoOfIt()
		{
			/* Observers receive slot changes, and the socket tooltip reads the live container, so a
			 * sheet painted once at open showed a sword while hovering it described an axe. */
			string source = ReadSource(InspectPath);

			string populate = MethodBody(source, "private void Populate(IPlayerCharacter target)", "private void FollowEquipment(");
			LogAssert.IsTrue(populate.Contains("FollowEquipment(target)"),
				"binding a subject follows their equipment container");

			LogAssert.IsTrue(MethodBody(source, "public override void Hide(bool overrideIsAlwaysOpen)", "public override void OnDestroying()").Contains("FollowEquipment(null)"),
				"hiding lets go of it");
			LogAssert.IsTrue(MethodBody(source, "public override void OnDestroying()", "\n\t}\n}").Contains("FollowEquipment(null)"),
				"...and so does destruction");

			string follow = MethodBody(source, "private void FollowEquipment(IPlayerCharacter target)", "private void OnInspectedSlotUpdated(");
			AssertOrdered(follow, "OnSlotUpdated -= OnInspectedSlotUpdated", "OnSlotUpdated += OnInspectedSlotUpdated",
				"the previous subject is released before the next is followed");
		}

		[Test]
		public void InspectWindow_ClosesOnPooledReuse()
		{
			/* IsSpawned alone cannot see a despawn and a spawn processed in the same frame; the
			 * character id is what changes. */
			string body = MethodBody(ReadSource(InspectPath), "protected override void OnTick()", "private static bool IsSubjectAlive(");

			LogAssert.IsTrue(body.Contains("inspected.ID != inspectedID"),
				"the panel compares the subject's id against the one it was opened on");
		}

		[Test]
		public void CharacterSheet_KeepsTheVitalsLive_WithoutTheAttributeRows()
		{
			/* The chips were kept live only as a side effect of the attribute rows' subscription,
			 * so a sheet that shows the bar without the rows — an inspected character — froze them
			 * at bind time. */
			string source = ReadSource(SheetViewPath);

			string bind = MethodBody(source, "public void SetSubject(IPlayerCharacter subject)", "public void Refresh()");
			LogAssert.IsTrue(bind.Contains("if (!options.ShowAttributes)") && bind.Contains("SubscribeVitals(attributeController)"),
				"a sheet without attribute rows subscribes the vitals itself");

			string release = MethodBody(source, "private void UnsubscribeAttributes()", "private void SubscribeVitals(");
			LogAssert.IsTrue(release.Contains("OnAttributeUpdated -= OnVitalUpdated"),
				"...and the same release call lets go of them");
		}

		#endregion

		#region A press on a take-only row puts the carried thing down

		[Test]
		public void LootAndContainerRows_PutDownACarriedDrag_InsteadOfActing()
		{
			/* Both rows wear the slot marker, so the root's cancel rule leaves a drag to them; a
			 * handler that took the item under it let the carried thing survive an unrelated
			 * action. One press, one thing. */
			string loot = MethodBody(ReadSource(LootPath), "private void OnRowPointerDown(PointerDownEvent evt, int slot)", "private void OnCurrencyPointerDown(");
			AssertOrdered(loot, "TryPutDownCarriedDrag()", "RequestTakeItem(slot)",
				"a loot row puts the drag down before it would take");

			string container = ReadSource(ContainerPanelPath);
			AssertOrdered(container, "TryPutDownCarriedDrag()", "OnRowPointerDown(capturedSlot)",
				"a container row puts the drag down before it would take");

			LogAssert.AreEqual(1, Occurrences(ReadSource(ControlPath), "protected static bool TryPutDownCarriedDrag()"),
				"the put-down lives once, on the shared control base");
		}

		#endregion
	}
}
