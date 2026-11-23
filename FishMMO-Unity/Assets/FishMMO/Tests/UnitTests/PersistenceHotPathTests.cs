using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation;
using FishMMO.Server.Implementation.World.SceneServer;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the character persistence fixes of the server hot-path audit
	/// (H2, H4, M20–M23, L24): the rules that decide when a claim may be handed back and what a batch's
	/// outcome means. The item half (what an item snapshot writes, the item write journal) moved with
	/// the inventory system to the Items module's <c>ItemPersistenceHotPathTests</c>.
	/// </summary>
	/// <remarks>
	/// The rules that could be written as pure functions are tested as such; the orderings that only a
	/// running server exercises — a release after the writes it covers, a save gate released on every
	/// exit — are source pins, in the style of <c>AuditFollowUpPinsTests</c>.
	/// </remarks>
	[TestFixture]
	public class PersistenceHotPathTests
	{
		private const string SavingPath = "Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";
		private const string CharacterSystemPath = "Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.cs";
		private const string ServerBehaviourPath = "Assets/FishMMO/Server/Implementation/ServerBehaviour.cs";

		#region Outcome rules (M20, M22, H4)

		private static CharacterPersistRequest Request(long version, bool owned)
		{
			var data = new CharacterData(
				id: 18, name: "n", nameLowercase: "n", account: "a", selected: false, worldServerID: 1,
				sceneName: "s", sceneHandle: 1, bindScene: "b", bindX: 0, bindY: 0, bindZ: 0,
				instanceID: 0, instanceX: 0, instanceY: 0, instanceZ: 0,
				instanceRotX: 0, instanceRotY: 0, instanceRotZ: 0, instanceRotW: 1,
				raceID: 1, modelIndex: 0, x: 0, y: 0, z: 0, rotX: 0, rotY: 0, rotZ: 0, rotW: 1,
				accessLevel: 1, online: true, flags: 0, version: version,
				timeCreated: DateTime.UtcNow, lastSaved: DateTime.UtcNow);
			return new CharacterPersistRequest(data, owned ? new CharacterSessionLeaseData(18, 7, Mine) : (CharacterSessionLeaseData?)null);
		}

		private static readonly Guid Mine = new Guid("11111111-1111-1111-1111-111111111111");
		private static readonly Guid Theirs = new Guid("22222222-2222-2222-2222-222222222222");

		[Test]
		public void ABatchedSaveRow_ThatWasNotWritten_IsClassifiedAsTheSingleRowSaveWouldReportIt()
		{
			CharacterSessionState online = CharacterSessionState.Online;

			LogAssert.AreEqual(CharacterPersistOutcome.NotFound,
				CharacterService.ClassifyUnwritten(Request(11, true), false, false, 0, online, 0, Guid.Empty), "missing");
			LogAssert.AreEqual(CharacterPersistOutcome.NotFound,
				CharacterService.ClassifyUnwritten(Request(11, true), true, true, 10, online, 7, Mine), "deleted");
			LogAssert.AreEqual(CharacterPersistOutcome.OwnershipLost,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 10, online, 9, Theirs), "claimed by another server");
			LogAssert.AreEqual(CharacterPersistOutcome.OwnershipLost,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 10, CharacterSessionState.Offline, 0, Guid.Empty), "released");
			LogAssert.AreEqual(CharacterPersistOutcome.OwnershipLost,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 50, online, 9, Theirs),
				"a lost claim is reported ahead of the stale version the new owner's saves also cause");
			LogAssert.AreEqual(CharacterPersistOutcome.Stale,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 50, online, 7, Mine), "newer stored");
			LogAssert.AreEqual(CharacterPersistOutcome.Replayed,
				CharacterService.ClassifyUnwritten(Request(11, true), true, false, 11, online, 7, Mine),
				"the same version under our own claim is our own write, replayed");
			LogAssert.AreEqual(CharacterPersistOutcome.Stale,
				CharacterService.ClassifyUnwritten(Request(11, false), true, false, 11, online, 7, Mine),
				"without a claim an equal version proves nothing about who wrote it");
		}

		#endregion

		#region Async worker admission (M23)

		[Test]
		public void RequiredWork_IsAdmittedPastTheCap_ButStillWaitsForASlot_InItsLane()
		{
			var worker = new AsyncWorkerData();
			typeof(AsyncWorkerData).GetField("maxConcurrency", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(worker, 1);
			typeof(AsyncWorkerData).GetField("maxOutstandingItems", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(worker, 1);
			worker.InitializeOnce();

			var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var order = new List<int>();
			var otherLaneRan = new ManualResetEventSlim(false);
			var firstHoldsTheSlot = new ManualResetEventSlim(false);
			var allDone = new CountdownEvent(3);

			LogAssert.IsTrue(worker.Enqueue(async () =>
			{
				firstHoldsTheSlot.Set();
				await release.Task;
				lock (order) { order.Add(1); }
				allDone.Signal();
			}, 42), "the first item fills the only slot and the whole allowance");
			// Lane work is handed to the thread pool, so wait until the first item has actually taken
			// the slot; otherwise the lane-99 item below can reach the free slot first and run legally.
			LogAssert.IsTrue(firstHoldsTheSlot.Wait(5000), "the first item starts and holds the only slot");

			LogAssert.IsFalse(worker.Enqueue(() => Task.CompletedTask, 43), "ordinary work is refused over the cap");

			LogAssert.AreEqual(AsyncWorkAdmission.AdmittedOverCapacity, worker.EnqueueRequired(() =>
			{
				lock (order) { order.Add(2); }
				allDone.Signal();
				return Task.CompletedTask;
			}, 42), "required work is admitted over the cap");

			worker.EnqueueRequired(() =>
			{
				otherLaneRan.Set();
				allDone.Signal();
				return Task.CompletedTask;
			}, 99);

			LogAssert.IsFalse(otherLaneRan.Wait(150),
				"over-cap work still waits for a concurrency slot — the old fallback ran it at once, outside the cap");

			release.SetResult(true);
			LogAssert.IsTrue(allDone.Wait(5000), "everything admitted runs once the slot frees");
			LogAssert.AreEqual(2, order.Count, "both lane items ran");
			LogAssert.AreEqual(1, order[0], "and in the order they were enqueued");
			LogAssert.AreEqual(2, order[1], "over-cap work keeps its place in its lane");

			worker.Clear();
			LogAssert.AreEqual(AsyncWorkAdmission.Refused, worker.EnqueueRequired(() => Task.CompletedTask, 1),
				"a pool that has stopped accepting refuses, so the caller falls back");
		}

		#endregion

		#region Source pins (H2, H4, M21, M22, M23)

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found at {path}.");
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");
			return source.Substring(start, end - start);
		}

		private static void AssertOrdered(string body, string first, string second, string why)
		{
			int a = body.IndexOf(first, StringComparison.Ordinal);
			int b = body.IndexOf(second, StringComparison.Ordinal);
			LogAssert.IsTrue(a >= 0, $"{first} must still be present");
			LogAssert.IsTrue(b >= 0, $"{second} must still be present");
			LogAssert.IsTrue(a < b, why);
		}

		[Test]
		public void PeriodicSave_ReleasesItsGateOnEveryExitThatDidNotHandItOn()
		{
			/* H2: one exception in the capture loop latched the gate and stopped every save until
			 * restart. The gate is now released in a finally unless the async save took it. */
			string body = MethodBody(ReadSource(SavingPath), "private void OnPeriodicSave(float deltaTime)", "private bool TryCapturePeriodicSnapshot(");

			AssertOrdered(body, "runtimeData.TryBeginSave()", "finally", "the gate is taken before the guarded block");
			AssertOrdered(body, "finally", "if (!handedOff)", "and released in the finally");
			AssertOrdered(body, "if (!handedOff)", "runtimeData.EndSave();", "unless the save that owns it was enqueued");
			LogAssert.IsTrue(body.Contains("TryCapturePeriodicSnapshot("), "each character is captured through the per-character catch");
		}

		[Test]
		public void PeriodicSave_WritesRowsInBatches_NotOneTransactionEach()
		{
			string body = MethodBody(ReadSource(SavingPath),
				"private async Task SaveAllCharactersAsync(", "private static CharacterPersistRequest ToPersistRequest(");

			LogAssert.IsTrue(body.Contains("PersistManyAsync("), "H4: the periodic rows go through the batched save");
			LogAssert.IsFalse(body.Contains("SaveCharacterAsync("), "and not through the single-row save in a loop");
		}

		[Test]
		public void Logout_KeepsTheClaimWhileTheItemFlushHasNotLanded()
		{
			/* M22: the flush used to be awaited, its failure swallowed, and the claim released anyway. */
			string body = MethodBody(ReadSource(SavingPath),
				"private async Task SaveAndReleaseCharacterAsync(", "/// Saves all characters asynchronously");

			AssertOrdered(body, "RunItemFlushWithRetryAsync(", "ReleaseCharacterSessionAsync(", "the flush is run before the release");
			AssertOrdered(body, "itemOutcome == ItemWriteOutcome.Retry", "ReleaseCharacterSessionAsync(",
				"and a flush that has not landed is handled before the release is reached");
			string retryBranch = MethodBody(body, "itemOutcome == ItemWriteOutcome.Retry", "return;");
			LogAssert.IsTrue(retryBranch.Contains("QueuePendingFlush("), "which hands the flush and the claim to the retry queue");
		}

		[Test]
		public void PendingFlush_ReleasesOnlyOnceItsFlushIsDone()
		{
			string body = MethodBody(ReadSource(SavingPath),
				"private async Task RunPendingFlushAsync(", "#endregion");

			LogAssert.IsTrue(body.Contains("session.HasValue && flushDone"), "the retry's release waits for the flush");
			LogAssert.IsTrue(body.Contains("lock (pending.Gate)"), "L24: the entry is only read and cleared under its lock");
		}

		[Test]
		public void ShutdownFlush_ReleasesEachCharacterOnlyAfterItsOwnWrites()
		{
			/* M21: every release used to wait for every save, so a slow database released nobody. */
			string source = ReadSource(CharacterSystemPath);
			string flush = MethodBody(source, "private async Task FlushForShutdownAsync(", "private async Task FlushAndReleaseForShutdownAsync(");
			AssertOrdered(flush, "PersistManyAsync(", "SaveSubEntitiesSequentiallyAsync(", "rows, then sub-entities");
			AssertOrdered(flush, "SaveSubEntitiesSequentiallyAsync(", "FlushAndReleaseForShutdownAsync(", "then each character's items and release");

			string lane = MethodBody(source, "private async Task FlushAndReleaseForShutdownAsync(", "#endregion");
			AssertOrdered(lane, "RunItemFlushWithRetryAsync(", "ReleaseCharacterSessionAsync(", "a character's release follows its own flush");
		}

		[Test]
		public void Persistence_OverTheWorkersCap_DoesNotRunOutsideIt()
		{
			string body = MethodBody(ReadSource(ServerBehaviourPath),
				"protected bool EnqueuePersistence(", "private static readonly SemaphoreSlim PersistenceFallbackGate");

			LogAssert.IsTrue(body.Contains("EnqueueRequired("), "M23: over-cap persistence is admitted by the worker, under its cap");
			LogAssert.IsTrue(body.Contains("PersistenceFallbackGate.WaitAsync("), "and the remaining fallback is bounded");
		}

		#endregion
	}
}
