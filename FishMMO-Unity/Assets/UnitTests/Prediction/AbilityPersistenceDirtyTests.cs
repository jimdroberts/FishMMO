using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the mark that decides whether an ability is written by the periodic save.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Known abilities are the purest case of the periodic save writing what it already stored.
	/// What is persisted is the template and the set of event IDs; cooldowns are deliberately not,
	/// so an ability in constant use produces exactly the row it produced an hour ago. Measured
	/// against the real schema, 500 characters at eight abilities each generated 34.3 MB of
	/// write-ahead log and 9.9 MB of table growth every ten minutes, none of it a change.
	/// </para>
	/// <para>
	/// Marking starts true and is only ever cleared by a confirmed write, so the failure direction
	/// is a redundant write rather than a lost one. These tests pin that direction: every path that
	/// changes what gets stored marks the ability, and clearing happens only when the write landed
	/// and the ability has not moved since.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityPersistenceDirtyTests
	{
		private AbilityTemplate template;

		[SetUp]
		public void SetUp()
		{
			template = ScriptableObject.CreateInstance<AbilityTemplate>();
			template.name = "PersistenceDirtyAbility";
			template.AddToCache(template.name);
		}

		[TearDown]
		public void TearDown()
		{
			if (template != null)
			{
				template.RemoveFromCache();
				Object.DestroyImmediate(template);
			}
		}

		private Ability NewAbility()
		{
			return new Ability(1, template, new List<int>());
		}

		[Test]
		public void ANewAbility_StartsDirty()
		{
			/* Deliberately optimistic in the safe direction. An ability that has just been built --
			 * whether crafted this second or loaded from the database a moment ago -- is written
			 * once and then never again until it changes. The alternative, starting clean, would
			 * depend on knowing which constructor the load path used, and being wrong there loses
			 * a crafted ability silently. */
			Ability ability = NewAbility();

			Assert.IsTrue(ability.PersistenceDirty,
				"An ability nobody has confirmed stored must be written at least once.");
		}

		[Test]
		public void MarkPersisted_ClearsWhenTheWrittenVersionIsStillCurrent()
		{
			Ability ability = NewAbility();
			ability.Version++;

			ability.MarkPersisted(ability.Version);

			Assert.IsFalse(ability.PersistenceDirty,
				"The write landed and nothing has changed since.");
		}

		[Test]
		public void AnAbilityThatHasNotChanged_IsNotWrittenAgain()
		{
			/* The whole saving: after one confirmed write, an untouched ability contributes nothing
			 * to any later save. */
			Ability ability = NewAbility();
			ability.Version++;
			ability.MarkPersisted(ability.Version);

			Assert.IsFalse(ability.PersistenceDirty);
		}

		[Test]
		public void AddingAnEvent_MarksTheAbility()
		{
			/* The event set is half of what is stored, so changing it has to be stored. */
			Ability ability = NewAbility();
			ability.Version++;
			ability.MarkPersisted(ability.Version);
			Assume.That(ability.PersistenceDirty, Is.False);

			AbilityOnHitEvent abilityEvent = ScriptableObject.CreateInstance<AbilityOnHitEvent>();
			try
			{
				abilityEvent.name = "PersistenceDirtyEvent";
				abilityEvent.AddToCache(abilityEvent.name);

				ability.AddEvent(abilityEvent);

				Assert.IsTrue(ability.PersistenceDirty,
					"A new event is a new row.");
			}
			finally
			{
				abilityEvent.RemoveFromCache();
				Object.DestroyImmediate(abilityEvent);
			}
		}

		[Test]
		public void RemovingAnEvent_MarksTheAbility()
		{
			Ability ability = NewAbility();

			AbilityOnHitEvent abilityEvent = ScriptableObject.CreateInstance<AbilityOnHitEvent>();
			try
			{
				abilityEvent.name = "PersistenceDirtyRemovedEvent";
				abilityEvent.AddToCache(abilityEvent.name);
				ability.AddEvent(abilityEvent);

				ability.Version++;
				ability.MarkPersisted(ability.Version);
				Assume.That(ability.PersistenceDirty, Is.False);

				ability.RemoveAbilityEvent(abilityEvent.ID);

				Assert.IsTrue(ability.PersistenceDirty,
					"Forgetting an event changes the stored row just as learning one does.");
			}
			finally
			{
				abilityEvent.RemoveFromCache();
				Object.DestroyImmediate(abilityEvent);
			}
		}

		[Test]
		public void MarkPersisted_LeavesItDirtyWhenItChangedWhileTheSaveWasInFlight()
		{
			/* A save snapshots on the main thread and completes later. A change in that window is
			 * not in the write about to be confirmed, and clearing on it would drop that change
			 * until the ability happened to change again. */
			Ability ability = NewAbility();
			ability.Version++;
			long writtenVersion = ability.Version;

			// The save is in flight; the ability changes again.
			ability.Version++;

			ability.MarkPersisted(writtenVersion);

			Assert.IsTrue(ability.PersistenceDirty,
				"The in-flight change was not in the write that just landed.");
		}

		[Test]
		public void AFailedSave_LeavesTheAbilityDirty()
		{
			/* MarkPersisted is only reached when the write reports success, so a failure is modelled
			 * by not calling it. The periodic save has no retry of its own -- writing everything
			 * every time WAS the retry -- so this had to be kept deliberately. */
			Ability ability = NewAbility();
			ability.Version++;

			// No MarkPersisted: the write failed.

			Assert.IsTrue(ability.PersistenceDirty,
				"A row the database refused must be offered again on the next pass.");
		}
	}
}
