using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that a crafted ability carries the identity its database row has before anything learns
	/// it, and that nothing grants one into a session that cannot record it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reported as "AbilityCraft destroys ability knowledge when crafting". <c>Ability(template,
	/// events)</c> sets <c>ID = -1</c> by design — its own doc says the database assigns the real id —
	/// and nothing put that id back: the persist read <c>IsSuccess</c> and discarded the
	/// <c>long</c> the upsert returned, so <c>LearnAbility</c> filed every crafted ability under
	/// <c>-1</c>. A second craft from a different template replaced the first, and because
	/// <c>HotkeyData.UnsetReferenceID</c> is also <c>-1</c>, a hotkey bound to either of them
	/// validated against the "no binding" sentinel.
	/// </para>
	/// <para>
	/// The identity is now applied on the async continuation, before the learn, which is why these
	/// pins are about ORDER inside <c>AbilitySystem</c> rather than about a corrected value. The item
	/// layer corrects an identity after the fact because an item lives in a slot that can be re-sent;
	/// an ability has no slot, so the window is removed instead of repaired.
	/// </para>
	/// <para>
	/// Source-scanning, like its neighbours, with the limits that implies: it reads what was WRITTEN.
	/// What it is guarding against is a fourth hand-rolled "construct, learn, persist" growing back in
	/// the next panel somebody writes, which no behavioural test can see.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityGrantIdentityTests
	{
		private const string SystemPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Ability/AbilitySystem.cs";

		private const string CraftPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.AbilityCraft.cs";

		private const string MerchantPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.Merchant.cs";

		private const string SavingPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";

		/// <summary>The ability upsert, which decides whether a save of a gone row can come back.</summary>
		private const string AbilityServicePath =
			"../FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/Character/CharacterAbilityService.cs";

		/// <summary>Source text with line endings normalised, so bounds do not depend on checkout.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>
		/// Comment lines stripped, so prose ABOUT a construct does not trip a scan for the construct.
		/// </summary>
		/// <remarks>
		/// Every one of these files explains in a comment what it stopped doing and why — that
		/// explanation is the point of it. Scanning raw text would make the refactor fail its own test
		/// and teach the next person to delete the explanation to get green.
		/// </remarks>
		private static string CodeOnly(string source)
		{
			string[] lines = source.Split('\n');
			StringBuilder code = new StringBuilder(source.Length);

			for (int i = 0; i < lines.Length; ++i)
			{
				string trimmed = lines[i].TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}

				code.Append(lines[i]).Append('\n');
			}

			return code.ToString();
		}

		/// <summary>
		/// The body of a named method, bounded by the next member's signature.
		/// </summary>
		/// <remarks>
		/// Two anchors rather than brace matching: these methods contain interpolated strings and
		/// lambdas, and the anchors name the neighbour, so a reordering that moves the boundary fails
		/// the assertion instead of quietly widening the scan.
		/// </remarks>
		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		[Test]
		public void TheDatabaseIdentityReachesTheAbilityBeforeItIsLearned()
		{
			/* The regression, in one ordering. The upsert mints the row key and returns it; the
			 * ability must carry it before the completion that learns it is queued. */
			string body = MethodBody(ReadSource(SystemPath),
				"private async Task PersistGrantedAbilityAsync(GrantRequest request)",
				"private bool QueueGrantCompletion(GrantRequest request, bool persisted)");

			int applied = body.IndexOf("request.Ability.ID = result.Data;", StringComparison.Ordinal);
			LogAssert.IsTrue(applied >= 0,
				"the identity the database returned must be put on the ability; discarding it is the whole defect");

			int completed = body.IndexOf("QueueGrantCompletion(request, persisted: true)", StringComparison.Ordinal);
			LogAssert.IsTrue(completed > applied,
				"it must be applied BEFORE the completion is queued, or the learn still files the ability under -1");
		}

		[Test]
		public void NothingIsLearnedBeforeItsRowExists()
		{
			/* One learn site on this path, and it is the continuation. Two would mean one of them
			 * still offers the ability to a session that has not written it. */
			string complete = MethodBody(ReadSource(SystemPath),
				"private void CompleteGrant(GrantRequest request)",
				"private void FailGrant(GrantRequest request)");

			LogAssert.IsTrue(complete.Contains("abilityController.LearnAbility(request.Ability)"),
				"the continuation is what learns the ability, and it is what has the identity");

			string craft = CodeOnly(ReadSource(CraftPath));
			LogAssert.IsFalse(craft.Contains("LearnAbility("),
				"the craft handler must not learn anything itself — it cannot know the row key yet");

			string merchant = CodeOnly(ReadSource(MerchantPath));
			LogAssert.IsFalse(merchant.Contains("LearnAbility(character"),
				"and neither may the merchant's premade-ability purchase, which used the same helper");
		}

		[Test]
		public void TheGrantIsRefusedWhenItCannotBeRecorded()
		{
			/* An ability granted into a process that cannot write it back is keyed on the
			 * constructor's -1 for the rest of the session and replaces whichever crafted ability
			 * preceded it. The item layer refuses on exactly this reasoning. */
			string body = MethodBody(ReadSource(SystemPath),
				"public bool TryGrantAbility(",
				"private async Task PersistGrantedAbilityAsync(GrantRequest request)");

			LogAssert.IsTrue(body.Contains("TryGetDbService<ICharacterAbilityService>(out _)"),
				"a server with no ability service must refuse the grant rather than grant without a write-back");

			string craft = ReadSource(CraftPath);
			LogAssert.IsTrue(craft.Contains("Server.BehaviourRegistry.TryGet(out IAbilitySystem abilitySystem)"),
				"and the craft must resolve the system before it charges the player, not after");
			LogAssert.IsTrue(craft.Contains("AbilityCraftFailure.Unavailable"),
				"refusing to charge for a craft that cannot be recorded needs its own answer");
		}

		[Test]
		public void TheGrantReleasesItsGuardOnEveryPathThatDoesNotTakeIt()
		{
			/* The contract is that TryGrantAbility takes ownership of releaseGuard: it is invoked
			 * exactly once on whichever path finishes the request. Every early refusal finishes it
			 * here, so every early refusal must release it — a leaked guard locks that connection out
			 * of crafting until the stale-entry backstop expires. */
			string body = MethodBody(ReadSource(SystemPath),
				"public bool TryGrantAbility(",
				"private async Task PersistGrantedAbilityAsync(GrantRequest request)");

			LogAssert.AreEqual(4, Occurrences(body, "return false;"),
				"the grant must still refuse for its four documented reasons: an unusable request, no ability service, no session claim to write under (O27), and a refused enqueue");
			LogAssert.AreEqual(Occurrences(body, "return false;"), Occurrences(body, "releaseGuard?.Invoke();"),
				"every refusal must release the guard it is about to stop owning");
		}

		[Test]
		public void TheCraftHandsItsGuardToTheGrantSoItIsReleasedOnce()
		{
			/* Two owners would be worse than none: the handler's finally would release a guard the
			 * grant is still using, letting a second craft past while the first is in flight. */
			string source = ReadSource(CraftPath);

			LogAssert.IsTrue(source.Contains("releaseGuard: () => EndIngressGuard(guardKey)"),
				"the craft must hand its guard to the grant rather than release it afterwards");

			int handedOff = source.IndexOf("guardHandedOff = true;", StringComparison.Ordinal);
			int call = source.IndexOf("bool accepted = abilitySystem.TryGrantAbility(", StringComparison.Ordinal);
			LogAssert.IsTrue(call >= 0 && handedOff > call,
				"ownership transfers only AFTER the call returns, so a throw still releases in the finally");

			LogAssert.IsTrue(source.Contains("if (!guardHandedOff)"),
				"and the finally must release only what was never handed over");
		}

		[Test]
		public void ASavedAbilityCarriesTheRowIdentityThatMakesItAnUpdate()
		{
			/* This is what makes a forgotten ability stay forgotten.
			 *
			 * The save is an upsert keyed on (character_id, template_id), so a row with no identity
			 * would be INSERTed — and a save batch captured just before a forget would then put the
			 * ability straight back. The batch's rows carry their real ids, so the write is an UPDATE
			 * of a row that is gone, which the service drops. */
			string body = MethodBody(ReadSource(SavingPath),
				"private void AppendAbilityData(IPlayerCharacter character, List<CharacterAbilityData> abilities)",
				"private void AppendPetData(");

			LogAssert.IsTrue(body.Contains("id: ability.ID"),
				"a saved ability must carry the identity its row has, not a placeholder");

			LogAssert.IsTrue(body.Contains("if (!ability.PersistenceDirty)"),
				"and it must still be skipped when nothing about it changed");
		}

		[Test]
		public void ASaveCannotResurrectARowItNoLongerHas()
		{
			/* The other half of the pin above, and the reason the forget can delete the row outright
			 * instead of leaving a tombstone.
			 *
			 * The bulk save splits by whether a row already has a key and re-reads which of those
			 * keys still exist, so a batch built before a forget is dropped rather than re-inserted.
			 * A tombstone was rejected as the alternative because it would hold a version no later
			 * craft could outrank, which makes forget-then-craft permanently impossible. */
			string path = Path.Combine(Directory.GetCurrentDirectory(), AbilityServicePath);
			if (!File.Exists(path))
			{
				Assert.Ignore($"the database project is not checked out beside the Unity project ({AbilityServicePath}).");
			}

			/* The batch body lives behind both the ungated and the ownership-gated entry points
			 * (PersistAsync and PersistOwnedAsync, 2026-09-25), so the pin reads the shared body. */
			string source = File.ReadAllText(path).Replace("\r\n", "\n");
			string body = MethodBody(source,
				"private async Task<DatabaseResult<BulkWriteResult>> PersistBatchAsync(IEnumerable<CharacterAbilityData> abilities",
				"/// <inheritdoc/>");

			LogAssert.IsTrue(body.Contains("var existingItems = list.Where(a => a.ID > 0).ToList();"),
				"a batch must split on whether the row already has a key, or every save is an INSERT");

			LogAssert.IsTrue(body.Contains("activeExistingItems = activeExistingItems.Where(a => existingIdSet.Contains(a.ID)).ToList();"),
				"and a keyed row whose row is gone must be dropped rather than inserted");
		}

		/// <summary>Non-overlapping occurrences of a needle, for the release-parity pin.</summary>
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
	}
}
