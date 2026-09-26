using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that forgetting an ability removes the row it actually names, tells the people who can
	/// see the character, and leaves no hotkey pointing at something that no longer resolves.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ability knowledge was permanent with no way to end it. <c>RemoveAbility</c> existed on the
	/// controller with an event and a client row handler and had ZERO server-side callers — no
	/// client-to-server message, no handler, no control — while two places in the craft panel told
	/// the player to forget one: the craft list filters already-known templates out, and its hint
	/// says "You already have that ability; forget it before crafting it again". The advertised flow
	/// had no mechanism behind it.
	/// </para>
	/// <para>
	/// The identity fix is a PREREQUISITE rather than a sibling: the service's delete matches
	/// <c>WHERE id = ?</c> and not the template id, so a freshly crafted ability — whose id was
	/// <c>-1</c> — deleted nothing and reported success for it.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityForgetTests
	{
		private const string SystemPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Ability/AbilitySystem.cs";

		private const string HotkeySystemPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Hotkey/HotkeySystem.cs";

		private const string HotkeyInterfacePath =
			"Assets/Scripts/Server/Core/World/SceneServer/Hotkey/IHotkeySystem.cs";

		private const string BroadcastPath =
			"Assets/Scripts/Shared/Implementation/Network/Character/AbilityBroadcasts.cs";

		private const string ObserverBroadcastPath =
			"Assets/Scripts/Shared/Implementation/Network/Character/Prediction/PredictionObserverBroadcasts.cs";

		private const string ControllerNetworkingPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Networking.cs";

		private const string ControllerActivationPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs";

		/// <summary>The ability delete, which decides whether a forgotten row can be re-crafted.</summary>
		private const string AbilityServicePath =
			"../FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/Character/CharacterAbilityService.cs";

		/// <summary>Source text with line endings normalised, so bounds do not depend on checkout.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The body of a named method, bounded by the next member's signature.</summary>
		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		/// <summary>Non-overlapping occurrences of a needle.</summary>
		private static int Occurrences(string text, string needle)
		{
			int count = 0;
			int at = 0;
			while (true)
			{
				at = text.IndexOf(needle, at, StringComparison.Ordinal);
				if (at < 0)
				{
					return count;
				}
				++count;
				at += needle.Length;
			}
		}

		[Test]
		public void ForgetNamesTheRowAndNotTheTemplate()
		{
			/* The delete matches on the row key. A template id would match nothing — reporting
			 * success for a delete that removed no row — or, if the table ever held several rows per
			 * template, remove the wrong one. */
			string body = MethodBody(ReadSource(SystemPath),
				"public void OnServerAbilityForgetBroadcastReceived(",
				"private async Task ForgetAbilityAsync(long characterID");

			LogAssert.IsTrue(body.Contains("abilityController.KnownAbilities.TryGetValue(msg.AbilityID"),
				"the request names a row identity, and it is the ability that identity resolves to that is removed");

			LogAssert.IsFalse(body.Contains("msg.TemplateID"),
				"the template is never what is deleted; a request that names one must not be able to reach the row");

			LogAssert.IsTrue(body.Contains("long version = long.MaxValue;"),
				"the delete must quote the version ceiling: a forget is authoritative for a row resolved by identity, and quoting the in-memory version refused it whenever the row had been bumped by a late save");

			LogAssert.IsTrue(body.Contains("ForgetAbilityAsync(characterID, abilityID, version, claim, guardKey)"),
				"and the delete must be handed the resolved identity rather than the request, with the session claim it was asked under (O27)");
		}

		[Test]
		public void AForgetThatLandedIsAppliedAndAnsweredOnce()
		{
			/* An ability the server's controller still holds, with no hotkey cleanup and no answer,
			 * is a row deleted behind a panel that goes on showing it. */
			string body = MethodBody(ReadSource(SystemPath),
				"private void CompleteForget(long characterID, long abilityID, AbilityForgetFailure failure)",
				"private void SendForgetResult(NetworkConnection conn");

			LogAssert.IsTrue(body.Contains("abilityController.RemoveAbility(abilityID)"),
				"the landed forget must drop the ability from the controller the panel is built from");

			LogAssert.IsTrue(body.Contains("hotkeySystem.ForgetAbilityBindings(character, abilityID)"),
				"and clear the bindings that name it, or the bar shows a dead slot until the next login");

			LogAssert.IsTrue(body.Contains("SendForgetResult(character.Owner, abilityID, AbilityForgetFailure.None)"),
				"and answer, which is what removes the row from the panel");

			LogAssert.AreEqual(2, Occurrences(body, "SendForgetResult("),
				"exactly two answers — the refusal and the success — so a second path cannot report the same forget twice");

			int refused = body.IndexOf("if (failure != AbilityForgetFailure.None)", StringComparison.Ordinal);
			int removed = body.IndexOf("RemoveAbility(abilityID)", StringComparison.Ordinal);
			LogAssert.IsTrue(refused >= 0 && refused < removed,
				"a refused forget must be answered BEFORE anything is dropped: nothing changed on the server");
		}

		[Test]
		public void AForgetForSomethingTheServerNoLongerHoldsIsRefused()
		{
			/* Answered with success it would hide a disagreement rather than resolve one — the panel
			 * would drop a row the next login hands straight back. */
			string body = MethodBody(ReadSource(SystemPath),
				"public void OnServerAbilityForgetBroadcastReceived(",
				"private async Task ForgetAbilityAsync(long characterID");

			int lookup = body.IndexOf("KnownAbilities.TryGetValue(msg.AbilityID", StringComparison.Ordinal);
			int refusal = body.IndexOf("AbilityForgetFailure.Unknown", StringComparison.Ordinal);
			LogAssert.IsTrue(lookup >= 0 && refusal > lookup,
				"an ability the character does not hold must be refused as unknown, not answered as forgotten");

			/* Resolved and refused in the same branch. A miss that fell through to the delete would
			 * hand the service a default Ability and report whatever came back as the answer. */
			LogAssert.IsTrue(body.Substring(lookup, refusal - lookup).Contains("ability == null"),
				"and a null resolved ability must be folded into that same refusal");

			string enumSource = ReadSource(BroadcastPath);
			LogAssert.IsTrue(enumSource.Contains("enum AbilityForgetFailure"),
				"the refusal needs a reason on the wire, or the panel can only say something went wrong");
			LogAssert.IsTrue(enumSource.Contains("Unknown = 1"),
				"and unknown must be its own value; None is success");
		}

		[Test]
		public void TheForgetHoldsItsGuardUntilTheDeleteLands()
		{
			/* Released when the handler returns instead, a second forget in the window reads the same
			 * still-live ability and deletes the same row twice, and a re-craft sees a character who
			 * still knows the ability it is about to create again. */
			string handler = MethodBody(ReadSource(SystemPath),
				"public void OnServerAbilityForgetBroadcastReceived(",
				"private async Task ForgetAbilityAsync(long characterID");

			LogAssert.IsTrue(handler.Contains("TryEnqueueAsyncWork("),
				"the handler must hand the guard over with the work");

			LogAssert.IsTrue(handler.Contains("if (!guardTransferred)"),
				"and release only what was never handed over, so a refused enqueue does not leak it");

			string worker = MethodBody(ReadSource(SystemPath),
				"private async Task ForgetAbilityAsync(long characterID",
				"private void QueueForgetCompletion(long characterID");

			LogAssert.IsTrue(worker.Contains("EndIngressGuard(guardKey)"),
				"the guard is released where the delete finishes, which is the whole point of holding it");
		}

		[Test]
		public void ForgettingPrunesAndEchoesHotkeys()
		{
			/* Bindings are validated when they are MADE and in the login prune and nowhere in between,
			 * so without this the server's bar and the database row keep naming a dead ability until
			 * the character relogs. */
			string source = ReadSource(HotkeySystemPath);
			string body = MethodBody(source,
				"public bool ForgetAbilityBindings(IPlayerCharacter playerCharacter, long abilityID)",
				"private static bool IsHotkeyReferenceValid(IPlayerCharacter playerCharacter, HotkeyData hotkey)");

			LogAssert.IsTrue(body.Contains("hotkey.Type != HotkeyTypeAbility"),
				"only ability bindings may be cleared: the id spaces are not disjoint, so an id alone is not a match");

			LogAssert.IsTrue(body.Contains("hotkey.ReferenceID != abilityID"),
				"and only the bindings that actually name the forgotten ability");

			LogAssert.IsTrue(body.Contains("EmptyHotkey(hotkey.Slot)"),
				"a clear must be the same clear the login prune writes, or the client cannot recognise it as empty");

			LogAssert.IsTrue(body.Contains("StageHotkeyPersist(playerCharacter)"),
				"the correction has to reach the database row, not just the session");

			LogAssert.IsTrue(body.Contains("AcknowledgeAllHotkeys(conn, playerCharacter)"),
				"and the client's bar has to be told, or it draws the dead binding for the rest of the session");

			LogAssert.IsTrue(body.Contains("if (!changed)"),
				"a forget that cleared nothing must not write the bar back");

			LogAssert.IsTrue(ReadSource(HotkeyInterfacePath).Contains("bool ForgetAbilityBindings("),
				"the cleanup is reached through the interface, not by casting the concrete system");
		}

		[Test]
		public void TheForgetReachesTheOwnerAndTheObservers()
		{
			/* An observer's copy of a peer's abilities is written once, by the spawn payload, and kept
			 * current by these messages alone. Nothing renders a stale entry — the server will not
			 * activate the ability, so the cast that would resolve it is never sent — but Inspect,
			 * CanActivate and faction evaluation all read the observed character's real state. */
			string broadcasts = ReadSource(BroadcastPath);
			LogAssert.IsTrue(broadcasts.Contains("struct AbilityForgetBroadcast"),
				"the client needs a way to ask");
			LogAssert.IsTrue(broadcasts.Contains("struct AbilityForgetResultBroadcast"),
				"and a way to be answered, since the server owns the row and may refuse");

			LogAssert.IsTrue(ReadSource(ObserverBroadcastPath).Contains("struct AbilityForgottenObserverBroadcast"),
				"the learn path tells observers, so the forget must too");

			string networking = ReadSource(ControllerNetworkingPath);
			LogAssert.IsTrue(networking.Contains("RegisterBroadcast<AbilityForgetResultBroadcast>(OnClientAbilityForgetResultBroadcastReceived)"),
				"the owner's controller applies the removal, which is what raises the event the panel listens to");
			LogAssert.IsTrue(networking.Contains("UnregisterBroadcast<AbilityForgetResultBroadcast>"),
				"and it must be unregistered, or a character change leaves the handler on a dead controller");

			string activation = ReadSource(ControllerActivationPath);
			LogAssert.IsTrue(activation.Contains("RegisterBroadcast<AbilityForgottenObserverBroadcast>(OnAbilityForgottenObserverBroadcast)"),
				"observers apply it through the shared activation registration, beside the learn message");

			string system = ReadSource(SystemPath);
			LogAssert.IsTrue(system.Contains("ObserverBroadcastScope.BroadcastToObserversExceptOwner(character.NetworkObject, new AbilityForgottenObserverBroadcast()"),
				"and the server must actually send it to the observers and not to the owner");
		}

		[Test]
		public void TheDeleteIsHardAndOwnedByOneCharacter()
		{
			/* A soft delete would leave a tombstone stamped with the caller's version, and the upsert
			 * only writes when the incoming version beats the surviving row's — so no later craft
			 * could ever outrank it and forget-then-craft, the flow the craft panel advertises, would
			 * be permanently impossible. CharacterItemService removes a vacated row for the same
			 * reason.
			 *
			 * The character is part of the predicate so one character's forget can never reach
			 * another's row, even if a caller passes an id it does not own. */
			string path = Path.Combine(Directory.GetCurrentDirectory(), AbilityServicePath);
			if (!File.Exists(path))
			{
				Assert.Ignore($"the database project is not checked out beside the Unity project ({AbilityServicePath}).");
			}

			string source = File.ReadAllText(path).Replace("\r\n", "\n");
			/* The ungated and the ownership-gated delete (O27) share one body, which sits between the
			 * two entry points and FetchAsync, so the slice reads all three. */
			string body = MethodBody(source,
				"public Task<DatabaseResult> DeleteAbilityAsync(",
				"public async Task<DatabaseResult<IReadOnlyList<CharacterAbilityData>>> FetchAsync(");

			LogAssert.IsTrue(body.Contains("DELETE FROM {TableName}"),
				"a forgotten row is removed outright rather than tombstoned");

			LogAssert.IsFalse(body.Contains("deleted = TRUE"),
				"a tombstone is what stands in the way of re-crafting the same template");

			LogAssert.IsTrue(body.Contains("AND character_id = {{2}}"),
				"and the delete is scoped to the owning character as well as the row");

			LogAssert.IsTrue(body.Contains("abilityId <= 0"),
				"an ability with no database identity must be refused rather than answered as forgotten");
		}
	}
}
