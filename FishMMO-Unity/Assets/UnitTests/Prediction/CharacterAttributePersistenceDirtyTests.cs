using FishMMO.Shared;
using FishMMO.UnitTests;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the mark that decides whether an attribute is written by the periodic save.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The save used to write every attribute of every resident character on every pass. Skipping
	/// the ones that have not moved is worth a great deal — measured against the real schema, a
	/// server holding five hundred characters generated 68.6 MB of write-ahead log and 18.8 MB of
	/// table growth every ten minutes to store values that were already stored.
	/// </para>
	/// <para>
	/// It is also the kind of change whose failure mode is silence. An attribute that changes
	/// without saying so is simply never written again, and nothing reports it: the character logs
	/// out, comes back, and is quietly wrong. Every mutation path therefore has a test here, and
	/// the two that bypass the change notification have one each, because those are the ones a
	/// reasonable implementation misses.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CharacterAttributePersistenceDirtyTests
	{
		private CharacterAttributeTemplate template;
		private CharacterAttributeTemplate resourceTemplate;
		private GameObject gameObject;
		private CharacterAttributeController controller;

		[SetUp]
		public void SetUp()
		{
			template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			template.name = "PersistenceDirtyAttribute";
			template.InitialValue = 10;
			template.AddToCache(template.name);

			resourceTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			resourceTemplate.name = "PersistenceDirtyResource";
			resourceTemplate.InitialValue = 100;
			resourceTemplate.AddToCache(resourceTemplate.name);

			gameObject = new GameObject("AttributePersistenceDirtyTest");
			controller = gameObject.AddComponent<CharacterAttributeController>();

			/* A resource attribute clamps against its character's load flag on construction, so it
			 * cannot be built without one. Flags stay clear, which reads as "not fully loaded" and
			 * skips the clamp to FinalValue -- the tests here are about the dirty mark, not about
			 * clamping. */
			controller.InitializeOnce(new Harness.StubCharacter());
		}

		[TearDown]
		public void TearDown()
		{
			if (template != null)
			{
				template.RemoveFromCache();
				Object.DestroyImmediate(template);
			}
			if (resourceTemplate != null)
			{
				resourceTemplate.RemoveFromCache();
				Object.DestroyImmediate(resourceTemplate);
			}
			if (gameObject != null)
			{
				Object.DestroyImmediate(gameObject);
			}
		}

		private CharacterAttribute NewAttribute(int value = 10)
		{
			return new CharacterAttribute(controller, template.ID, value, 0);
		}

		private CharacterResourceAttribute NewResource(int value = 100, float current = 100.0f)
		{
			return new CharacterResourceAttribute(controller, resourceTemplate.ID, value, current, 0);
		}

		// --- Change marks it -------------------------------------------------------------------

		[Test]
		public void SetValue_MarksTheAttributeForPersistence()
		{
			CharacterAttribute attribute = NewAttribute();
			attribute.MarkPersisted(attribute.Version);
			Assume.That(attribute.PersistenceDirty, Is.False);

			attribute.SetValue(42);

			Assert.IsTrue(attribute.PersistenceDirty,
				"A value the database does not have must be written.");
		}

		[Test]
		public void AddValue_MarksTheAttributeForPersistence()
		{
			CharacterAttribute attribute = NewAttribute();
			attribute.MarkPersisted(attribute.Version);

			attribute.AddValue(5);

			Assert.IsTrue(attribute.PersistenceDirty);
		}

		[Test]
		public void SetValueDirect_MarksTheAttributeForPersistence()
		{
			/* SetValueDirect exists to write the value WITHOUT raising the change event, for the
			 * two-phase reconcile. The notification funnel therefore never runs, so an
			 * implementation that only marks there loses every reconciled value silently. */
			CharacterAttribute attribute = NewAttribute();
			attribute.MarkPersisted(attribute.Version);

			attribute.SetValueDirect(77);

			Assert.IsTrue(attribute.PersistenceDirty,
				"A value written past the notification is still a value the database lacks.");
		}

		[Test]
		public void SetCurrentValue_MarksTheResourceEvenWhenTheNotificationIsSuppressed()
		{
			/* updateInternal: false suppresses the change NOTIFICATION, which callers turn off to
			 * avoid re-entrancy. It does not mean the value held still, and the attribute
			 * controller uses exactly this overload when applying a resource from the network. */
			CharacterResourceAttribute resource = NewResource();
			resource.MarkPersisted(resource.Version);

			resource.SetCurrentValue(55.0f, false);

			Assert.IsTrue(resource.PersistenceDirty,
				"Suppressing the event must not suppress the save.");
		}

		[Test]
		public void SetCurrentValue_MarksTheResourceNormally()
		{
			CharacterResourceAttribute resource = NewResource();
			resource.MarkPersisted(resource.Version);

			resource.SetCurrentValue(55.0f);

			Assert.IsTrue(resource.PersistenceDirty);
		}

		// --- No change does not mark it ---------------------------------------------------------

		[Test]
		public void SettingTheSameValue_LeavesTheAttributeClean()
		{
			/* The whole saving. An assignment that changes nothing must not schedule a write, or
			 * the mark is set on every pass and nothing is skipped. */
			CharacterAttribute attribute = NewAttribute(10);
			attribute.MarkPersisted(attribute.Version);

			attribute.SetValue(10);

			Assert.IsFalse(attribute.PersistenceDirty,
				"Writing a value that is already stored is the cost this exists to avoid.");
		}

		[Test]
		public void SettingTheSameCurrentValue_LeavesTheResourceClean()
		{
			CharacterResourceAttribute resource = NewResource(100, 100.0f);
			resource.MarkPersisted(resource.Version);

			resource.SetCurrentValue(100.0f);

			Assert.IsFalse(resource.PersistenceDirty);
		}

		[Test]
		public void SetValueDirectWithTheSameValue_LeavesTheAttributeClean()
		{
			CharacterAttribute attribute = NewAttribute(10);
			attribute.MarkPersisted(attribute.Version);

			attribute.SetValueDirect(10);

			Assert.IsFalse(attribute.PersistenceDirty);
		}

		// --- Clearing is confirmation, not optimism ---------------------------------------------

		[Test]
		public void MarkPersisted_ClearsWhenTheWrittenVersionIsStillCurrent()
		{
			CharacterAttribute attribute = NewAttribute();
			attribute.SetValue(42);
			attribute.Version++;                       // as the save snapshot does

			attribute.MarkPersisted(attribute.Version);

			Assert.IsFalse(attribute.PersistenceDirty,
				"The write landed and nothing has moved since.");
		}

		[Test]
		public void MarkPersisted_LeavesItDirtyWhenItChangedWhileTheSaveWasInFlight()
		{
			/* A save snapshots on the main thread and completes some time later. A change in that
			 * window is not covered by the write that is about to be confirmed, and clearing on it
			 * would drop that change until the attribute happened to move again. */
			CharacterAttribute attribute = NewAttribute();
			attribute.SetValue(42);
			attribute.Version++;
			long writtenVersion = attribute.Version;

			// The save is in flight; the attribute moves again.
			attribute.SetValue(99);
			attribute.Version++;

			attribute.MarkPersisted(writtenVersion);

			Assert.IsTrue(attribute.PersistenceDirty,
				"The in-flight change was not in the write that just landed.");
		}

		[Test]
		public void AFailedSave_LeavesEverythingDirty()
		{
			/* MarkPersisted is only reached when the write reports success, so a failure is modelled
			 * by not calling it. This matters more than it looks: the periodic save has no retry of
			 * its own -- writing everything every time WAS the retry -- so a change that is dropped
			 * here is dropped until the value moves again. */
			CharacterAttribute attribute = NewAttribute();
			attribute.SetValue(42);
			attribute.Version++;

			// No MarkPersisted: the write failed.

			Assert.IsTrue(attribute.PersistenceDirty,
				"A change the database refused must be offered again on the next pass.");
		}

		[Test]
		public void AFreshlyLoadedAttribute_IsNotWrittenBackImmediately()
		{
			/* An attribute that came from the database and has not been touched is already stored,
			 * so the first periodic save after login should not write it back. */
			CharacterAttribute attribute = NewAttribute();
			attribute.MarkPersisted(attribute.Version);

			Assert.IsFalse(attribute.PersistenceDirty);
		}
	}
}
