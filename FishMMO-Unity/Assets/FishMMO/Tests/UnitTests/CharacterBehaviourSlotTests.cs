using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <c>BaseCharacter.TryGet</c> reads a slot array, kept beside the <c>Behaviours</c> dictionary, instead of
	/// probing the dictionary on every call.
	/// </summary>
	/// <remarks>
	/// The two must never disagree: registering fills both, unregistering empties both, and an entry seeded straight
	/// into the dictionary (the editor render rigs do it by reflection) must still be found.
	/// </remarks>
	[TestFixture]
	public class CharacterBehaviourSlotTests
	{
		public interface ISlotProbeA : ICharacterBehaviour { }
		public interface ISlotProbeB : ICharacterBehaviour { }
		public interface ISlotProbeSeeded : ICharacterBehaviour { }

		private class Probe : ISlotProbeA, ISlotProbeB, ISlotProbeSeeded
		{
			public ICharacter Character => null;
			public bool Initialized => true;
			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
		}

		private class OnlyA : ISlotProbeA
		{
			public ICharacter Character => null;
			public bool Initialized => true;
			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
		}

		private GameObject host;
		private PlayerCharacter character;

		[SetUp]
		public void SetUp()
		{
			/* A PlayerCharacter stood up by hand brings its RequireComponents, and FishNet logs a duplicate
			 * NetworkObject for them in edit mode: an artefact of the setup, not of the lookup under test. */
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			host = new GameObject("SlotProbeCharacter");
			host.SetActive(false);
			character = host.AddComponent<PlayerCharacter>();
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		[Test]
		public void Registered_IsFoundUnderEveryInterface()
		{
			Probe probe = new Probe();
			character.RegisterCharacterBehaviour(probe);

			LogAssert.IsTrue(character.TryGet(out ISlotProbeA a) && ReferenceEquals(a, probe), "found under its first interface");
			LogAssert.IsTrue(ReferenceEquals(character.Get<ISlotProbeB>(), probe), "and under its second");
		}

		[Test]
		public void Unregistered_IsGone_AndAnotherCanTakeTheSlot()
		{
			Probe probe = new Probe();
			character.RegisterCharacterBehaviour(probe);
			character.UnregisterCharacterBehaviour(probe);

			LogAssert.IsFalse(character.TryGet(out ISlotProbeA _), "an unregistered behaviour is not found");
			LogAssert.IsNull(character.Get<ISlotProbeB>(), "under any interface");

			OnlyA next = new OnlyA();
			character.RegisterCharacterBehaviour(next);
			LogAssert.IsTrue(character.TryGet(out ISlotProbeA a) && ReferenceEquals(a, next), "the next registration owns the slot");
			LogAssert.IsNull(character.Get<ISlotProbeB>(), "and fills only its own interfaces");
		}

		[Test]
		public void FirstRegistrationWins_AsTheDictionaryDid()
		{
			OnlyA first = new OnlyA();
			OnlyA second = new OnlyA();
			character.RegisterCharacterBehaviour(first);
			character.RegisterCharacterBehaviour(second);

			LogAssert.IsTrue(ReferenceEquals(character.Get<ISlotProbeA>(), first), "a second registration does not displace the first");

			character.UnregisterCharacterBehaviour(second);
			LogAssert.IsTrue(ReferenceEquals(character.Get<ISlotProbeA>(), first), "and removing the loser leaves the winner");
		}

		[Test]
		public void SeededStraightIntoTheDictionary_IsStillFound()
		{
			FieldInfo field = typeof(BaseCharacter).GetField("Behaviours", BindingFlags.NonPublic | BindingFlags.Instance);
			LogAssert.IsNotNull(field, "the editor rigs seed this field by name");
			var behaviours = (Dictionary<Type, ICharacterBehaviour>)field.GetValue(character);

			Probe seeded = new Probe();
			behaviours[typeof(ISlotProbeSeeded)] = seeded;

			LogAssert.IsTrue(character.TryGet(out ISlotProbeSeeded found) && ReferenceEquals(found, seeded),
				"a rig's seeded fake resolves exactly as at run time");
		}

		[Test]
		public void AClassType_StillThrows()
		{
			Assert.Throws<UnityException>(() => character.TryGet(out Probe _),
				"a class is never a behaviour key; the call is a bug, as before");
		}
	}
}
