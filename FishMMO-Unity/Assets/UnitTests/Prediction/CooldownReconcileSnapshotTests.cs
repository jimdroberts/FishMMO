using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

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
				Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Verifies a no-op cooldown reconcile preserves the cached snapshot reference.
		/// </summary>
		[Test]
		public void RestoreFromReconcile_UnchangedValues_PreservesCachedSnapshotReference()
		{
			// Seed the controller through the reconcile path rather than AddCooldown.
			// AddCooldown reads base.IsOwner to gate owner-only UI events, and IsOwner
			// dereferences the FishNet NetworkObject cache which is null on an unspawned
			// component instantiated via AddComponent in an EditMode test. RestoreFrom
			// Reconcile populates the same cooldown table without touching IsOwner, so the
			// snapshot-caching behaviour under test is exercised identically.
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

			Assert.AreSame(firstSnapshot, secondSnapshot,
				"No-op cooldown reconcile must not dirty the cached snapshot or allocate a fresh array.");

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

			Assert.AreNotSame(secondSnapshot, changedSnapshot,
				"Changed cooldown reconcile data must dirty the cached snapshot.");
			Assert.AreEqual(201u, changedSnapshot[0].StartTick);
		}
	}
}