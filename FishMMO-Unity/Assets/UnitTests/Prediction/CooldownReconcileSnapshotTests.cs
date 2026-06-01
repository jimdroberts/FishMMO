using System;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regression tests for cooldown reconcile snapshot caching.
	/// </summary>
	[TestFixture]
	public class CooldownReconcileSnapshotTests
	{
		private const BindingFlags PrivateInstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
		private const float TickDelta30 = 1.0f / 30f;

		private GameObject gameObject;
		private CooldownController controller;

		/// <summary>
		/// Creates a real cooldown controller with a deterministic tick delta for reconcile rebuilds.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			gameObject = new GameObject("CooldownReconcileSnapshotTest");
			controller = gameObject.AddComponent<CooldownController>();
			typeof(CooldownController)
				.GetField("cachedTickDelta", PrivateInstanceFlags)
				.SetValue(controller, TickDelta30);
		}

		/// <summary>
		/// Destroys Unity objects created by <see cref="SetUp"/>.
		/// </summary>
		[TearDown]
		public void TearDown()
		{
			if (gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Verifies a no-op cooldown reconcile preserves the cached snapshot reference.
		/// </summary>
		[Test]
		public void RestoreFromReconcile_UnchangedValues_PreservesCachedSnapshotReference()
		{
			try
			{
				AuthTestTrace.LogTestStart(
					nameof(RestoreFromReconcile_UnchangedValues_PreservesCachedSnapshotReference),
					"A no-op cooldown reconcile (identical entries) must reuse the cached snapshot " +
					"reference, while a changed StartTick must dirty it and allocate a fresh array.")
					.GetAwaiter().GetResult();

				// Seed the controller through the reconcile path rather than AddCooldown.
				// AddCooldown reads base.IsOwner to gate owner-only UI events, and IsOwner
				// dereferences the FishNet NetworkObject cache which is null on an unspawned
				// component instantiated via AddComponent in an EditMode test. RestoreFrom
				// Reconcile populates the same cooldown table without touching IsOwner, so the
				// snapshot-caching behaviour under test is exercised identically.
				AuthTestTrace.Log("CooldownReconcileSnapshotTests", "STEP",
					"Seeding controller via RestoreFromReconcile(ability=101, start=200, duration=30).")
					.GetAwaiter().GetResult();
				controller.RestoreFromReconcile(new[]
				{
					new CooldownReconcileEntry
					{
						AbilityID = 101L,
						StartTick = 200u,
						DurationTicks = 30u,
					},
				});
				CooldownReconcileEntry[] firstSnapshot = controller.CreateReconcileSnapshot();
				AuthTestTrace.Log("CooldownReconcileSnapshotTests", "STEP",
					$"Captured first snapshot (entries={firstSnapshot.Length}).")
					.GetAwaiter().GetResult();

				AuthTestTrace.Log("CooldownReconcileSnapshotTests", "STEP",
					"Re-applying identical reconcile entry — expecting cache reuse.")
					.GetAwaiter().GetResult();
				controller.RestoreFromReconcile(new[]
				{
					new CooldownReconcileEntry
					{
						AbilityID = 101L,
						StartTick = 200u,
						DurationTicks = 30u,
					},
				});
				CooldownReconcileEntry[] secondSnapshot = controller.CreateReconcileSnapshot();

				LogAssert.AreSame(firstSnapshot, secondSnapshot,
					"No-op cooldown reconcile must not dirty the cached snapshot or allocate a fresh array.");

				AuthTestTrace.Log("CooldownReconcileSnapshotTests", "STEP",
					"Applying changed reconcile entry (start=201) — expecting cache invalidation.")
					.GetAwaiter().GetResult();
				controller.RestoreFromReconcile(new[]
				{
					new CooldownReconcileEntry
					{
						AbilityID = 101L,
						StartTick = 201u,
						DurationTicks = 30u,
					},
				});
				CooldownReconcileEntry[] changedSnapshot = controller.CreateReconcileSnapshot();

				LogAssert.AreNotSame(secondSnapshot, changedSnapshot,
					"Changed cooldown reconcile data must dirty the cached snapshot.");
				LogAssert.AreEqual(201u, changedSnapshot[0].StartTick,
					"The rebuilt snapshot must reflect the changed StartTick.");

				AuthTestTrace.Log("CooldownReconcileSnapshotTests", "SUCCESS",
					nameof(RestoreFromReconcile_UnchangedValues_PreservesCachedSnapshotReference))
					.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownReconcileSnapshotTests", "FAILURE",
					$"{nameof(RestoreFromReconcile_UnchangedValues_PreservesCachedSnapshotReference)}: {ex.Message}\n{ex.StackTrace}")
					.GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(
					nameof(RestoreFromReconcile_UnchangedValues_PreservesCachedSnapshotReference))
					.GetAwaiter().GetResult();
			}
		}
	}
}