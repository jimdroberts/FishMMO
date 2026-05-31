using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
#if !UNITY_SERVER
using TMPro;
#endif

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regression tests for ECA tick-domain routing between caster-sourced events and target-owned prediction state.
	/// </summary>
	[TestFixture]
	public class EcaTickDomainRoutingTests
	{
		/// <summary>
		/// Same-character replicate ticks can be consumed directly by the target buff controller.
		/// </summary>
		[Test]
		public void ApplyBuffAction_SameCharacterReplicateTick_UsesDirectPredictionApply()
		{
			TestBuffController buffController = new TestBuffController();
			TestCharacter character = new TestCharacter(1, buffController, null);
			EventData eventData = new EventData(character, character);
			eventData.Add(new TickEventData(character, new PredictionTick(100u)));

			ApplyBuffAction action = CreateApplyBuffAction();

			action.Execute(character, eventData);

			Assert.AreEqual(1, buffController.DirectApplyCalls);
			Assert.AreEqual(100u, buffController.LastDirectApplyTick);
			Assert.AreEqual(0, buffController.AuthoritativeApplyCalls);
		}

		/// <summary>
		/// Same-character routing uses stable character ID when the runtime reference differs.
		/// </summary>
		[Test]
		public void ApplyBuffAction_SameCharacterIdReplicateTick_UsesDirectPredictionApply()
		{
			TestCharacter source = new TestCharacter(42, null, null);
			TestBuffController buffController = new TestBuffController();
			TestCharacter target = new TestCharacter(42, buffController, null);
			EventData eventData = new EventData(source, target);
			eventData.Add(new TickEventData(source, new PredictionTick(100u)));

			ApplyBuffAction action = CreateApplyBuffAction();

			action.Execute(source, eventData);

			Assert.AreEqual(1, buffController.DirectApplyCalls);
			Assert.AreEqual(100u, buffController.LastDirectApplyTick);
			Assert.AreEqual(0, buffController.AuthoritativeApplyCalls);
		}

		/// <summary>
		/// A caster's replicate tick must not be stamped directly onto another character's buff state.
		/// </summary>
		[Test]
		public void ApplyBuffAction_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping()
		{
			TestCharacter caster = new TestCharacter(1, null, null);
			TestBuffController targetBuffController = new TestBuffController
			{
				CurrentDomainTick = 777u,
			};
			TestCharacter target = new TestCharacter(2, targetBuffController, null);
			EventData eventData = new EventData(caster, target);
			eventData.Add(new TickEventData(caster, new PredictionTick(100u)));

			ApplyBuffAction action = CreateApplyBuffAction();

			action.Execute(caster, eventData);

			Assert.AreEqual(0, targetBuffController.DirectApplyCalls);
			Assert.AreEqual(1, targetBuffController.AuthoritativeApplyCalls);
			Assert.AreEqual(777u, targetBuffController.LastAuthoritativeApplyTick,
				"The caster replicate tick must be replaced with the target controller's current domain tick.");
		}

		/// <summary>
		/// Raw authoritative ticks are passed through the authoritative path; the controller maps them into its own domain.
		/// </summary>
		[Test]
		public void ApplyBuffAction_RawAuthoritativeTick_UsesAuthoritativeMappingInput()
		{
			TestBuffController buffController = new TestBuffController();
			TestCharacter character = new TestCharacter(1, buffController, null);
			EventData eventData = new EventData(character, character);
			eventData.Add(new TickEventData(character, 205u));

			ApplyBuffAction action = CreateApplyBuffAction();

			action.Execute(character, eventData);

			Assert.AreEqual(0, buffController.DirectApplyCalls);
			Assert.AreEqual(1, buffController.AuthoritativeApplyCalls);
			Assert.AreEqual(205u, buffController.LastAuthoritativeApplyTick);
		}

		/// <summary>
		/// Events without explicit tick payloads must still stamp buffs in the target controller's current domain.
		/// </summary>
		[Test]
		public void ApplyBuffAction_NoTickData_UsesTargetCurrentDomainTick()
		{
			TestBuffController buffController = new TestBuffController
			{
				CurrentDomainTick = 333u,
			};
			TestCharacter character = new TestCharacter(1, buffController, null);
			EventData eventData = new EventData(character, character);

			ApplyBuffAction action = CreateApplyBuffAction();

			action.Execute(character, eventData);

			Assert.AreEqual(0, buffController.DirectApplyCalls);
			Assert.AreEqual(1, buffController.AuthoritativeApplyCalls);
			Assert.AreEqual(333u, buffController.LastAuthoritativeApplyTick);
		}

		/// <summary>
		/// Same-character replicate ticks can be used directly for cooldown conditions.
		/// </summary>
		[Test]
		public void HasCooldownCondition_SameCharacterReplicateTick_UsesDirectPredictionTick()
		{
			TestCooldownController cooldownController = new TestCooldownController();
			TestCharacter character = new TestCharacter(1, null, cooldownController);
			EventData eventData = new EventData(character, character);
			eventData.Add(new TickEventData(character, new PredictionTick(100u)));

			HasCooldownCondition condition = new HasCooldownCondition { AbilityID = 7 };

			Assert.IsTrue(condition.Evaluate(character, eventData));
			Assert.AreEqual(0, cooldownController.ResolveAuthoritativeTickCalls);
			Assert.AreEqual(100u, cooldownController.LastIsOnCooldownTick);
		}

		/// <summary>
		/// Cross-character replicate ticks must be translated through the queried character's cooldown controller.
		/// </summary>
		[Test]
		public void HasCooldownCondition_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping()
		{
			TestCharacter caster = new TestCharacter(1, null, null);
			TestCooldownController targetCooldownController = new TestCooldownController
			{
				MappedAuthoritativeTick = 777u,
			};
			TestCharacter target = new TestCharacter(2, null, targetCooldownController);
			EventData eventData = new EventData(caster, target);
			eventData.Add(new TickEventData(caster, new PredictionTick(100u)));

			HasCooldownCondition condition = new HasCooldownCondition { AbilityID = 7 };

			Assert.IsTrue(condition.Evaluate(caster, eventData));
			Assert.AreEqual(1, targetCooldownController.ResolveAuthoritativeTickCalls);
			Assert.AreEqual(777u, targetCooldownController.LastIsOnCooldownTick);
		}

		/// <summary>
		/// Tick ownership accepts the same runtime object or a stable non-zero character ID.
		/// </summary>
		[Test]
		public void TickEventData_IsForCharacter_UsesReferenceOrStableId()
		{
			TestCharacter source = new TestCharacter(42, null, null);
			TestCharacter sameId = new TestCharacter(42, null, null);
			TestCharacter other = new TestCharacter(43, null, null);
			TestCharacter unsaved = new TestCharacter(0, null, null);
			TickEventData tickData = new TickEventData(source, new PredictionTick(100u));

			Assert.IsTrue(tickData.IsForCharacter(source));
			Assert.IsTrue(tickData.IsForCharacter(sameId));
			Assert.IsFalse(tickData.IsForCharacter(other));
			Assert.IsFalse(tickData.IsForCharacter(unsaved));
		}

		private static ApplyBuffAction CreateApplyBuffAction()
		{
			return new ApplyBuffAction
			{
				StacksValue = new ConstantValue { Amount = 1 },
				BuffTemplate = ScriptableObject.CreateInstance<TestBuffTemplate>(),
			};
		}

		private sealed class TestBuffTemplate : BaseBuffTemplate
		{
			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }
		}

		private sealed class TestCharacter : ICharacter
		{
			private readonly IBuffController buffController;
			private readonly ICooldownController cooldownController;

			public TestCharacter(long id, IBuffController buffController, ICooldownController cooldownController)
			{
				ID = id;
				this.buffController = buffController;
				this.cooldownController = cooldownController;
			}

			public long ID { get; set; }
			public string Name => "TestCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }

#if !UNITY_SERVER
			public Transform MeshRoot => null;
			public TextMeshPro CharacterNameLabel { get; set; }
			public TextMeshPro CharacterGuildLabel { get; set; }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif

			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }

			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				if (buffController is T buff)
				{
					control = buff;
					return true;
				}
				if (cooldownController is T cooldown)
				{
					control = cooldown;
					return true;
				}

				control = null;
				return false;
			}

			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}

		private sealed class TestBuffController : IBuffController
		{
			public int DirectApplyCalls;
			public uint LastDirectApplyTick;
			public int AuthoritativeApplyCalls;
			public uint LastAuthoritativeApplyTick;
			public uint CurrentDomainTick = 500u;

			public ICharacter Character => null;
			public bool Initialized => true;
			public List<Trigger> OnBuffApplyTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnBuffRemoveTriggers { get; } = new List<Trigger>();
			public SortedDictionary<int, Buff> Buffs { get; } = new SortedDictionary<int, Buff>();

			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
			public uint GetCurrentDomainTick() => CurrentDomainTick;
			public void Tick(uint currentTick) { }

			public void Apply(BaseBuffTemplate template, PredictionTick currentTick)
			{
				DirectApplyCalls++;
				LastDirectApplyTick = currentTick;
			}

			public void ApplyAuthoritative(BaseBuffTemplate template, uint serverTick)
			{
				AuthoritativeApplyCalls++;
				LastAuthoritativeApplyTick = serverTick;
			}

			public uint ResolveAuthoritativeTick(uint serverTick) => serverTick;
			public void Apply(Buff buff, bool suppressFX = false) { }
			public void Remove(int buffID) { }
			public void RemoveRandom(DeterministicRNG rng, bool includeBuffs = false, bool includeDebuffs = false) { }
			public void RemoveAll(bool ignoreInvokeRemove = false) { }
			public BuffReconcileEntry[] CreateReconcileSnapshot() => null;
			public void RestoreFromReconcile(BuffReconcileEntry[] entries, uint reconcileTick) { }
		}

		private sealed class TestCooldownController : ICooldownController
		{
			public uint MappedAuthoritativeTick = 777u;
			public int ResolveAuthoritativeTickCalls;
			public uint LastResolveAuthoritativeInput;
			public uint LastIsOnCooldownTick;

			public ICharacter Character => null;
			public bool Initialized => true;

			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
			public void Read(Reader reader, uint currentTick) { }
			public void Write(Writer writer) { }
			public void ExpireElapsed(uint currentTick) { }

			public bool IsOnCooldown(long id, uint currentTick)
			{
				LastIsOnCooldownTick = currentTick;
				return true;
			}

			public uint ResolveAuthoritativeTick(uint serverTick)
			{
				ResolveAuthoritativeTickCalls++;
				LastResolveAuthoritativeInput = serverTick;
				return MappedAuthoritativeTick;
			}

			public bool TryGetCooldown(long id, uint currentTick, out float cooldown)
			{
				cooldown = 0f;
				return false;
			}

			public void AddCooldown(long id, CooldownInstance cooldown) { }
			public void RemoveCooldown(long id) { }
			public void Clear() { }
			public CooldownReconcileEntry[] CreateReconcileSnapshot() => null;
			public void RestoreFromReconcile(CooldownReconcileEntry[] entries) { }
		}
	}
}