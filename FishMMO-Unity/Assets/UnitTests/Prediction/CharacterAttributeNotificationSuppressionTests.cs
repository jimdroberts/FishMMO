using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

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
				Object.DestroyImmediate(template);
			}

			if (gameObject != null)
			{
				Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Verifies replay suppression discards update callbacks without blocking state changes.
		/// </summary>
		[Test]
		public void NotificationSuppression_AllowsStateMutationWithoutListenerDispatch()
		{
			CharacterAttribute attribute = new CharacterAttribute(controller, template.ID, 10, 0);
			int notificationCount = 0;
			attribute.OnAttributeUpdated += _ => notificationCount++;

			attribute.AddModifier(1);
			Assert.AreEqual(1, notificationCount, "Baseline mutation should notify outside suppression.");
			Assert.AreEqual(11, attribute.FinalValue, "Baseline mutation should still update state.");

			controller.BeginNotificationSuppression();
			try
			{
				attribute.AddModifier(1);
			}
			finally
			{
				controller.EndNotificationSuppression();
			}

			Assert.AreEqual(1, notificationCount,
				"Suppressed mutation must not dispatch OnAttributeUpdated listeners during replay.");
			Assert.AreEqual(12, attribute.FinalValue,
				"Suppression must not block the deterministic state mutation itself.");

			attribute.AddModifier(1);
			Assert.AreEqual(2, notificationCount,
				"Listener dispatch must resume after suppression ends.");
			Assert.AreEqual(13, attribute.FinalValue);
		}

		/// <summary>
		/// Verifies a no-op attribute reconcile preserves the cached snapshot reference.
		/// </summary>
		[Test]
		public void ApplyAttributeSnapshot_UnchangedValues_PreservesCachedSnapshotReference()
		{
			controller.AddAttribute(new CharacterAttribute(controller, template.ID, 10, 0));

			AttributeReconcileEntry[] firstSnapshot = CreateAttributeSnapshot(controller);
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

			Assert.AreSame(firstSnapshot, secondSnapshot,
				"No-op reconcile must not dirty the cached attribute snapshot or allocate a fresh array.");

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

			Assert.AreNotSame(secondSnapshot, changedSnapshot,
				"Changed reconcile data must dirty the cached attribute snapshot.");
			Assert.AreEqual(11, changedSnapshot[0].Value);
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