using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Coverage for how <see cref="CharacterDamageController"/> behaves when the health resource
	/// attribute cannot be resolved — issue #157.
	/// </summary>
	/// <remarks>
	/// <c>ResourceInstance</c> memoized success but not failure, so a failed lookup repeated the
	/// lookup <em>and</em> the error on every access. <c>IsAlive</c> reads it from AI target
	/// selection, inventory checks and input handling, all per tick, so three misconfigured NPCs
	/// wrote 153 MB of log in five and a half minutes.
	/// <para>
	/// The fix reports once while still retrying the lookup, and those are separate on purpose:
	/// health can arrive after this component does, so latching the failure would strand a
	/// character as not-alive over a gap that resolves itself. Both halves are asserted here.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MissingHealthResourceTests
	{
		private GameObject gameObject;
		private CharacterDamageController controller;
		private StubCharacter character;

		[SetUp]
		public void SetUp()
		{
			gameObject = new GameObject("MissingHealthResourceTest");
			controller = gameObject.AddComponent<CharacterDamageController>();
			character = new StubCharacter();
			controller.InitializeOnce(character);
		}

		[TearDown]
		public void TearDown()
		{
			if (gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>Reads the private gate that decides whether the failure is reported.</summary>
		private bool ReportedFlag()
		{
			FieldInfo field = typeof(CharacterDamageController).GetField("loggedMissingResource",
				BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(field, "CharacterDamageController must gate the missing-health report.");
			return (bool)field.GetValue(controller);
		}

		[Test]
		public void AMissingHealthResource_IsReportedOnce_NotOnEveryAccess()
		{
			LogAssert.IsFalse(ReportedFlag(), "nothing has been reported before the first access");

			CharacterResourceAttribute first = controller.ResourceInstance;

			LogAssert.IsNull(first, "the lookup fails, so the property is null");
			LogAssert.IsTrue(ReportedFlag(), "the first failure is reported");

			// A tick's worth of reads from AI, inventory and input.
			for (int i = 0; i < 500; i++)
			{
				LogAssert.IsNull(controller.ResourceInstance, "still unresolved");
			}

			LogAssert.IsTrue(ReportedFlag(),
				"the report stays suppressed for every later access -- this is what #157 was");
		}

		[Test]
		public void TheLookup_KeepsRetrying_EvenWhileTheReportIsSuppressed()
		{
			/* Reporting and resolving are separate. Health arrives late on clients, populated by
			 * reconcile, so a latched failure would leave the character permanently not-alive. */
			const int reads = 50;
			for (int i = 0; i < reads; i++)
			{
				_ = controller.ResourceInstance;
			}

			LogAssert.AreEqual(reads, character.TryGetCalls,
				"every access must retry the lookup, even though only the first is reported");
		}

		[Test]
		public void WhenHealthArrivesLate_TheGateReArms_SoALaterFailureIsStillReported()
		{
			_ = controller.ResourceInstance;
			LogAssert.IsTrue(ReportedFlag(), "the early failure was reported");

			// Reconcile populates the attribute controller.
			character.AttributeController = new StubAttributeController();

			// Compared as a reference: LogAssert renders the value into its message, and the stub
			// instance has no template to render.
			LogAssert.IsTrue(controller.ResourceInstance != null, "health now resolves");
			LogAssert.IsFalse(ReportedFlag(),
				"resolving re-arms the gate, so a genuinely new failure later is not swallowed");
		}

		// ── Stubs ─────────────────────────────────────────────────────────────

		private sealed class StubAttributeController : ICharacterAttributeController
		{
			private readonly CharacterResourceAttribute health;

			public StubAttributeController()
			{
				/* Only its non-nullness matters here — the assertion is about the report gate, and
				 * nothing reads the attribute. Built without running the constructor because that
				 * resolves a cached template and computes a final value, none of which this
				 * fixture is testing and all of which would have to be stood up to get past it. */
				health = (CharacterResourceAttribute)FormatterServices
					.GetUninitializedObject(typeof(CharacterResourceAttribute));
			}

			public bool TryGetHealthAttribute(out CharacterResourceAttribute value)
			{
				value = health;
				return true;
			}

			public ICharacter Character => null;
			public bool Initialized => true;
			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }

			public Dictionary<int, CharacterAttribute> Attributes => throw new NotImplementedException();
			public Dictionary<int, CharacterResourceAttribute> ResourceAttributes => throw new NotImplementedException();
			public bool IsPropagating => throw new NotImplementedException();
			public void SetAttribute(int id, int value, int? modifier = null) => throw new NotImplementedException();
			public void SetResourceAttribute(int id, int value, float currentValue, int? modifier = null) => throw new NotImplementedException();
			public bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute) => throw new NotImplementedException();
			public bool TryGetAttribute(int id, out CharacterAttribute attribute) => throw new NotImplementedException();
			public bool TryGetManaAttribute(out CharacterResourceAttribute mana) => throw new NotImplementedException();
			public bool TryGetStaminaAttribute(out CharacterResourceAttribute stamina) => throw new NotImplementedException();
			public bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute) => throw new NotImplementedException();
			public bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute) => throw new NotImplementedException();
			public void AddAttribute(CharacterAttribute instance) => throw new NotImplementedException();

			/// <summary>No ledger on a stub; nothing to release.</summary>
			public void ClearModifierSource(ModifierSource source) { }
			public void Regenerate(uint tick) => throw new NotImplementedException();
			public void ApplyResourceState(CharacterAttributeResourceState resourceState) => throw new NotImplementedException();
			public CharacterAttributeResourceState GetResourceState() => throw new NotImplementedException();
			public void BeginPropagation() => throw new NotImplementedException();
			public void EndPropagation() => throw new NotImplementedException();
			public void EnqueueNotification(CharacterAttribute attribute) => throw new NotImplementedException();
			public void BeginNotificationSuppression() => throw new NotImplementedException();
			public void EndNotificationSuppression() => throw new NotImplementedException();
		}

		private sealed class StubCharacter : ICharacter
		{
			/// <summary>Attribute controller to hand out, or null to fail the lookup.</summary>
			public ICharacterAttributeController AttributeController;

			/// <summary>How many times the behaviour lookup was attempted.</summary>
			public int TryGetCalls;

			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				TryGetCalls++;
				control = AttributeController as T;
				return control != null;
			}

			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }

			public long ID { get; set; }
			public int Flags { get; set; }
			public Collider Collider { get; set; }
			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }

			public string Name => "Stub";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers => null;
			public bool IsTeleporting => false;
			public bool IsSpawned => false;
			public Transform MeshRoot => null;

			public void EnableFlags(CharacterFlags flags) { }
			public void DisableFlags(CharacterFlags flags) { }
			public bool IsFlagged(CharacterFlags flags) => false;
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) => throw new NotImplementedException();
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) => throw new NotImplementedException();
			public void Invoke(List<Trigger> triggers, EventData eventData) => throw new NotImplementedException();
		}
	}
}
