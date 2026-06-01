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
	/// Regression tests for replay-safe attribute notification suppression.
	/// </summary>
	[TestFixture]
	public class CharacterAttributeNotificationSuppressionTests
	{
		private const BindingFlags PrivateInstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

		private CharacterAttributeTemplate template;
		private GameObject gameObject;
		private CharacterAttributeController controller;

		/// <summary>
		/// Creates a cached attribute template and a real attribute controller for each test.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			template.name = "SuppressionTestAttribute";
			template.InitialValue = 10;
			template.AddToCache(template.name);

			gameObject = new GameObject("AttributeNotificationSuppressionTest");
			controller = gameObject.AddComponent<CharacterAttributeController>();
		}

		/// <summary>
		/// Removes cached test assets and Unity objects created by <see cref="SetUp"/>.
		/// </summary>
		[TearDown]
		public void TearDown()
		{
			if (template != null)
			{
				template.RemoveFromCache();
				UnityEngine.Object.DestroyImmediate(template);
			}

			if (gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Verifies replay suppression discards update callbacks without blocking state changes.
		/// </summary>
		[Test]
		public void NotificationSuppression_AllowsStateMutationWithoutListenerDispatch()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(NotificationSuppression_AllowsStateMutationWithoutListenerDispatch),
					"During replay suppression, attribute mutations must still update state but must not dispatch OnAttributeUpdated listeners.")
					.GetAwaiter().GetResult();

				CharacterAttribute attribute = new CharacterAttribute(controller, template.ID, 10, 0);
				int notificationCount = 0;
				attribute.OnAttributeUpdated += _ => notificationCount++;

				attribute.AddModifier(1);
				AuthTestTrace.Log("CharacterAttributeNotificationSuppressionTests", "STEP",
					$"Baseline AddModifier(1): notificationCount={notificationCount} FinalValue={attribute.FinalValue}")
					.GetAwaiter().GetResult();
				LogAssert.AreEqual(1, notificationCount, "Baseline mutation should notify outside suppression.");
				LogAssert.AreEqual(11, attribute.FinalValue, "Baseline mutation should still update state.");

				controller.BeginNotificationSuppression();
				try
				{
					attribute.AddModifier(1);
				}
				finally
				{
					controller.EndNotificationSuppression();
				}
				AuthTestTrace.Log("CharacterAttributeNotificationSuppressionTests", "STEP",
					$"Suppressed AddModifier(1): notificationCount={notificationCount} FinalValue={attribute.FinalValue}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(1, notificationCount,
					"Suppressed mutation must not dispatch OnAttributeUpdated listeners during replay.");
				LogAssert.AreEqual(12, attribute.FinalValue,
					"Suppression must not block the deterministic state mutation itself.");

				attribute.AddModifier(1);
				AuthTestTrace.Log("CharacterAttributeNotificationSuppressionTests", "STEP",
					$"Post-suppression AddModifier(1): notificationCount={notificationCount} FinalValue={attribute.FinalValue}")
					.GetAwaiter().GetResult();
				LogAssert.AreEqual(2, notificationCount,
					"Listener dispatch must resume after suppression ends.");
				LogAssert.AreEqual(13, attribute.FinalValue, "State must continue mutating after suppression ends.");

				AuthTestTrace.Log("CharacterAttributeNotificationSuppressionTests", "SUCCESS", nameof(NotificationSuppression_AllowsStateMutationWithoutListenerDispatch)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterAttributeNotificationSuppressionTests", "FAILURE", $"{nameof(NotificationSuppression_AllowsStateMutationWithoutListenerDispatch)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(NotificationSuppression_AllowsStateMutationWithoutListenerDispatch)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Verifies a no-op attribute reconcile preserves the cached snapshot reference.
		/// </summary>
		[Test]
		public void ApplyAttributeSnapshot_UnchangedValues_PreservesCachedSnapshotReference()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ApplyAttributeSnapshot_UnchangedValues_PreservesCachedSnapshotReference),
					"A no-op attribute reconcile must reuse the cached snapshot reference; a changed value must invalidate it.")
					.GetAwaiter().GetResult();

				controller.AddAttribute(new CharacterAttribute(controller, template.ID, 10, 0));

				AttributeReconcileEntry[] firstSnapshot = CreateAttributeSnapshot(controller);
				AuthTestTrace.Log("CharacterAttributeNotificationSuppressionTests", "STEP",
					$"Captured first snapshot (entries={firstSnapshot.Length}). Applying identical reconcile (value=10).")
					.GetAwaiter().GetResult();
				ApplyAttributeSnapshot(controller, new[]
				{
					new AttributeReconcileEntry
					{
						TemplateID = template.ID,
						Value = 10,
						ExternalModifier = 0,
					},
				});
				AttributeReconcileEntry[] secondSnapshot = CreateAttributeSnapshot(controller);

				LogAssert.AreSame(firstSnapshot, secondSnapshot,
					"No-op reconcile must not dirty the cached attribute snapshot or allocate a fresh array.");

				AuthTestTrace.Log("CharacterAttributeNotificationSuppressionTests", "STEP",
					"Applying changed reconcile (value=11) — expecting cache invalidation.")
					.GetAwaiter().GetResult();
				ApplyAttributeSnapshot(controller, new[]
				{
					new AttributeReconcileEntry
					{
						TemplateID = template.ID,
						Value = 11,
						ExternalModifier = 0,
					},
				});
				AttributeReconcileEntry[] changedSnapshot = CreateAttributeSnapshot(controller);

				LogAssert.AreNotSame(secondSnapshot, changedSnapshot,
					"Changed reconcile data must dirty the cached attribute snapshot.");
				LogAssert.AreEqual(11, changedSnapshot[0].Value, "The rebuilt snapshot must reflect the changed value.");

				AuthTestTrace.Log("CharacterAttributeNotificationSuppressionTests", "SUCCESS", nameof(ApplyAttributeSnapshot_UnchangedValues_PreservesCachedSnapshotReference)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterAttributeNotificationSuppressionTests", "FAILURE", $"{nameof(ApplyAttributeSnapshot_UnchangedValues_PreservesCachedSnapshotReference)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ApplyAttributeSnapshot_UnchangedValues_PreservesCachedSnapshotReference)).GetAwaiter().GetResult();
			}
		}

		private static AttributeReconcileEntry[] CreateAttributeSnapshot(CharacterAttributeController attributeController)
		{
			return (AttributeReconcileEntry[])typeof(CharacterAttributeController)
				.GetMethod("CreateAttributeSnapshot", PrivateInstanceFlags)
				.Invoke(attributeController, null);
		}

		private static void ApplyAttributeSnapshot(CharacterAttributeController attributeController, AttributeReconcileEntry[] snapshot)
		{
			typeof(CharacterAttributeController)
				.GetMethod("ApplyAttributeSnapshot", PrivateInstanceFlags)
				.Invoke(attributeController, new object[] { snapshot });
		}
	}
}