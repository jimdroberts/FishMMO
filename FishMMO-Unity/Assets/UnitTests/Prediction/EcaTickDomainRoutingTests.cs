using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

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
			try
			{
				AuthTestTrace.LogTestStart(nameof(ApplyBuffAction_SameCharacterReplicateTick_UsesDirectPredictionApply),
					"When source and target are the same character, a replicate tick must be applied directly (no authoritative remap).")
					.GetAwaiter().GetResult();

				TestBuffController buffController = new TestBuffController();
				TestCharacter character = new TestCharacter(1, buffController, null);
				EventData eventData = new EventData(character, character);
				eventData.Add(new TickEventData(character, new PredictionTick(100u)));

				ApplyBuffAction action = CreateApplyBuffAction();
				action.Execute(character, eventData);
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "STEP",
					$"DirectApplyCalls={buffController.DirectApplyCalls} LastDirectApplyTick={buffController.LastDirectApplyTick} AuthoritativeApplyCalls={buffController.AuthoritativeApplyCalls}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(1, buffController.DirectApplyCalls, "Same-character replicate tick must use the direct apply path.");
				LogAssert.AreEqual(100u, buffController.LastDirectApplyTick, "Direct apply must use the original replicate tick.");
				LogAssert.AreEqual(0, buffController.AuthoritativeApplyCalls, "Same-character path must not invoke authoritative apply.");

				AuthTestTrace.Log("EcaTickDomainRoutingTests", "SUCCESS", nameof(ApplyBuffAction_SameCharacterReplicateTick_UsesDirectPredictionApply)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "FAILURE", $"{nameof(ApplyBuffAction_SameCharacterReplicateTick_UsesDirectPredictionApply)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ApplyBuffAction_SameCharacterReplicateTick_UsesDirectPredictionApply)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Same-character routing uses stable character ID when the runtime reference differs.
		/// </summary>
		[Test]
		public void ApplyBuffAction_SameCharacterIdReplicateTick_UsesDirectPredictionApply()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ApplyBuffAction_SameCharacterIdReplicateTick_UsesDirectPredictionApply),
					"When source and target share a stable character ID but differ by reference, routing must still use the direct apply path.")
					.GetAwaiter().GetResult();

				TestCharacter source = new TestCharacter(42, null, null);
				TestBuffController buffController = new TestBuffController();
				TestCharacter target = new TestCharacter(42, buffController, null);
				EventData eventData = new EventData(source, target);
				eventData.Add(new TickEventData(source, new PredictionTick(100u)));

				ApplyBuffAction action = CreateApplyBuffAction();
				action.Execute(source, eventData);
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "STEP",
					$"source.ID=42 target.ID=42 (distinct refs) -> DirectApplyCalls={buffController.DirectApplyCalls} LastDirectApplyTick={buffController.LastDirectApplyTick} AuthoritativeApplyCalls={buffController.AuthoritativeApplyCalls}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(1, buffController.DirectApplyCalls, "Matching stable IDs must use the direct apply path.");
				LogAssert.AreEqual(100u, buffController.LastDirectApplyTick, "Direct apply must use the original replicate tick.");
				LogAssert.AreEqual(0, buffController.AuthoritativeApplyCalls, "Matching stable IDs must not invoke authoritative apply.");

				AuthTestTrace.Log("EcaTickDomainRoutingTests", "SUCCESS", nameof(ApplyBuffAction_SameCharacterIdReplicateTick_UsesDirectPredictionApply)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "FAILURE", $"{nameof(ApplyBuffAction_SameCharacterIdReplicateTick_UsesDirectPredictionApply)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ApplyBuffAction_SameCharacterIdReplicateTick_UsesDirectPredictionApply)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// A caster's replicate tick must not be stamped directly onto another character's buff state.
		/// </summary>
		[Test]
		public void ApplyBuffAction_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ApplyBuffAction_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping),
					"A caster's replicate tick must never be stamped onto another character; it must be replaced by the target's current domain tick.")
					.GetAwaiter().GetResult();

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
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "STEP",
					$"caster replicate tick=100, target CurrentDomainTick=777 -> DirectApplyCalls={targetBuffController.DirectApplyCalls} AuthoritativeApplyCalls={targetBuffController.AuthoritativeApplyCalls} LastAuthoritativeApplyTick={targetBuffController.LastAuthoritativeApplyTick}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(0, targetBuffController.DirectApplyCalls, "Cross-character apply must not use the direct path.");
				LogAssert.AreEqual(1, targetBuffController.AuthoritativeApplyCalls, "Cross-character apply must use the authoritative path.");
				LogAssert.AreEqual(777u, targetBuffController.LastAuthoritativeApplyTick,
					"The caster replicate tick must be replaced with the target controller's current domain tick.");

				AuthTestTrace.Log("EcaTickDomainRoutingTests", "SUCCESS", nameof(ApplyBuffAction_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "FAILURE", $"{nameof(ApplyBuffAction_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ApplyBuffAction_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Raw authoritative ticks are passed through the authoritative path; the controller maps them into its own domain.
		/// </summary>
		[Test]
		public void ApplyBuffAction_RawAuthoritativeTick_UsesAuthoritativeMappingInput()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ApplyBuffAction_RawAuthoritativeTick_UsesAuthoritativeMappingInput),
					"A raw (non-prediction) authoritative tick must flow through the authoritative apply path unchanged at the controller boundary.")
					.GetAwaiter().GetResult();

				TestBuffController buffController = new TestBuffController();
				TestCharacter character = new TestCharacter(1, buffController, null);
				EventData eventData = new EventData(character, character);
				eventData.Add(new TickEventData(character, 205u));

				ApplyBuffAction action = CreateApplyBuffAction();
				action.Execute(character, eventData);
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "STEP",
					$"raw authoritative tick=205 -> DirectApplyCalls={buffController.DirectApplyCalls} AuthoritativeApplyCalls={buffController.AuthoritativeApplyCalls} LastAuthoritativeApplyTick={buffController.LastAuthoritativeApplyTick}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(0, buffController.DirectApplyCalls, "Raw authoritative tick must not use the direct apply path.");
				LogAssert.AreEqual(1, buffController.AuthoritativeApplyCalls, "Raw authoritative tick must use the authoritative apply path.");
				LogAssert.AreEqual(205u, buffController.LastAuthoritativeApplyTick, "Authoritative apply must receive the raw tick value.");

				AuthTestTrace.Log("EcaTickDomainRoutingTests", "SUCCESS", nameof(ApplyBuffAction_RawAuthoritativeTick_UsesAuthoritativeMappingInput)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "FAILURE", $"{nameof(ApplyBuffAction_RawAuthoritativeTick_UsesAuthoritativeMappingInput)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ApplyBuffAction_RawAuthoritativeTick_UsesAuthoritativeMappingInput)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Events without explicit tick payloads must still stamp buffs in the target controller's current domain.
		/// </summary>
		[Test]
		public void ApplyBuffAction_NoTickData_UsesTargetCurrentDomainTick()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(ApplyBuffAction_NoTickData_UsesTargetCurrentDomainTick),
					"An event with no tick payload must still stamp the buff using the target controller's current domain tick.")
					.GetAwaiter().GetResult();

				TestBuffController buffController = new TestBuffController
				{
					CurrentDomainTick = 333u,
				};
				TestCharacter character = new TestCharacter(1, buffController, null);
				EventData eventData = new EventData(character, character);

				ApplyBuffAction action = CreateApplyBuffAction();
				action.Execute(character, eventData);
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "STEP",
					$"no tick data, CurrentDomainTick=333 -> DirectApplyCalls={buffController.DirectApplyCalls} AuthoritativeApplyCalls={buffController.AuthoritativeApplyCalls} LastAuthoritativeApplyTick={buffController.LastAuthoritativeApplyTick}")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(0, buffController.DirectApplyCalls, "Missing tick data must not use the direct apply path.");
				LogAssert.AreEqual(1, buffController.AuthoritativeApplyCalls, "Missing tick data must use the authoritative apply path.");
				LogAssert.AreEqual(333u, buffController.LastAuthoritativeApplyTick, "Authoritative apply must use the target's current domain tick.");

				AuthTestTrace.Log("EcaTickDomainRoutingTests", "SUCCESS", nameof(ApplyBuffAction_NoTickData_UsesTargetCurrentDomainTick)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "FAILURE", $"{nameof(ApplyBuffAction_NoTickData_UsesTargetCurrentDomainTick)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(ApplyBuffAction_NoTickData_UsesTargetCurrentDomainTick)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Same-character replicate ticks can be used directly for cooldown conditions.
		/// </summary>
		[Test]
		public void HasCooldownCondition_SameCharacterReplicateTick_UsesDirectPredictionTick()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(HasCooldownCondition_SameCharacterReplicateTick_UsesDirectPredictionTick),
					"A same-character cooldown condition must evaluate using the replicate tick directly (no authoritative remap).")
					.GetAwaiter().GetResult();

				TestCooldownController cooldownController = new TestCooldownController();
				TestCharacter character = new TestCharacter(1, null, cooldownController);
				EventData eventData = new EventData(character, character);
				eventData.Add(new TickEventData(character, new PredictionTick(100u)));

				HasCooldownCondition condition = new HasCooldownCondition { AbilityID = 7 };
				bool result = condition.Evaluate(character, eventData);
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "STEP",
					$"Evaluate result={result} ResolveAuthoritativeTickCalls={cooldownController.ResolveAuthoritativeTickCalls} LastIsOnCooldownTick={cooldownController.LastIsOnCooldownTick}")
					.GetAwaiter().GetResult();

				LogAssert.IsTrue(result, "The cooldown condition must evaluate true for an active cooldown.");
				LogAssert.AreEqual(0, cooldownController.ResolveAuthoritativeTickCalls, "Same-character path must not remap through authoritative resolution.");
				LogAssert.AreEqual(100u, cooldownController.LastIsOnCooldownTick, "IsOnCooldown must be queried with the original replicate tick.");

				AuthTestTrace.Log("EcaTickDomainRoutingTests", "SUCCESS", nameof(HasCooldownCondition_SameCharacterReplicateTick_UsesDirectPredictionTick)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "FAILURE", $"{nameof(HasCooldownCondition_SameCharacterReplicateTick_UsesDirectPredictionTick)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(HasCooldownCondition_SameCharacterReplicateTick_UsesDirectPredictionTick)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Cross-character replicate ticks must be translated through the queried character's cooldown controller.
		/// </summary>
		[Test]
		public void HasCooldownCondition_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(HasCooldownCondition_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping),
					"A cross-character cooldown condition must translate the caster's replicate tick through the queried character's controller.")
					.GetAwaiter().GetResult();

				TestCharacter caster = new TestCharacter(1, null, null);
				TestCooldownController targetCooldownController = new TestCooldownController
				{
					MappedAuthoritativeTick = 777u,
				};
				TestCharacter target = new TestCharacter(2, null, targetCooldownController);
				EventData eventData = new EventData(caster, target);
				eventData.Add(new TickEventData(caster, new PredictionTick(100u)));

				HasCooldownCondition condition = new HasCooldownCondition { AbilityID = 7 };
				bool result = condition.Evaluate(caster, eventData);
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "STEP",
					$"Evaluate result={result} ResolveAuthoritativeTickCalls={targetCooldownController.ResolveAuthoritativeTickCalls} LastIsOnCooldownTick={targetCooldownController.LastIsOnCooldownTick}")
					.GetAwaiter().GetResult();

				LogAssert.IsTrue(result, "The cross-character cooldown condition must evaluate true.");
				LogAssert.AreEqual(1, targetCooldownController.ResolveAuthoritativeTickCalls, "Cross-character path must remap through authoritative resolution.");
				LogAssert.AreEqual(777u, targetCooldownController.LastIsOnCooldownTick, "IsOnCooldown must be queried with the target-mapped tick.");

				AuthTestTrace.Log("EcaTickDomainRoutingTests", "SUCCESS", nameof(HasCooldownCondition_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "FAILURE", $"{nameof(HasCooldownCondition_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(HasCooldownCondition_CrossCharacterReplicateTick_UsesTargetAuthoritativeMapping)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Tick ownership accepts the same runtime object or a stable non-zero character ID.
		/// </summary>
		[Test]
		public void TickEventData_IsForCharacter_UsesReferenceOrStableId()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(TickEventData_IsForCharacter_UsesReferenceOrStableId),
					"Tick ownership must match by runtime reference or a stable non-zero character ID, and reject mismatched/unsaved IDs.")
					.GetAwaiter().GetResult();

				TestCharacter source = new TestCharacter(42, null, null);
				TestCharacter sameId = new TestCharacter(42, null, null);
				TestCharacter other = new TestCharacter(43, null, null);
				TestCharacter unsaved = new TestCharacter(0, null, null);
				TickEventData tickData = new TickEventData(source, new PredictionTick(100u));
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "STEP",
					$"IsForCharacter: source={tickData.IsForCharacter(source)} sameId(42)={tickData.IsForCharacter(sameId)} other(43)={tickData.IsForCharacter(other)} unsaved(0)={tickData.IsForCharacter(unsaved)}")
					.GetAwaiter().GetResult();

				LogAssert.IsTrue(tickData.IsForCharacter(source), "The originating reference must match.");
				LogAssert.IsTrue(tickData.IsForCharacter(sameId), "A different reference with the same stable ID must match.");
				LogAssert.IsFalse(tickData.IsForCharacter(other), "A different stable ID must not match.");
				LogAssert.IsFalse(tickData.IsForCharacter(unsaved), "An unsaved (ID 0) character must not match by ID.");

				AuthTestTrace.Log("EcaTickDomainRoutingTests", "SUCCESS", nameof(TickEventData_IsForCharacter_UsesReferenceOrStableId)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("EcaTickDomainRoutingTests", "FAILURE", $"{nameof(TickEventData_IsForCharacter_UsesReferenceOrStableId)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(TickEventData_IsForCharacter_UsesReferenceOrStableId)).GetAwaiter().GetResult();
			}
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

		public WorldLabel CharacterNameLabel { get; set; }
		public WorldLabel CharacterGuildLabel { get; set; }

		public Transform MeshRoot => null;

#if !UNITY_SERVER
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

			/// <summary>The caster the most recent apply carried, or null when it carried none.</summary>
			public ICharacter LastCaster;

			public ICharacter Character => null;
			public bool Initialized => true;
			public List<Trigger> OnBuffApplyTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnBuffRemoveTriggers { get; } = new List<Trigger>();
			public SortedDictionary<int, Buff> Buffs { get; } = new SortedDictionary<int, Buff>();
			public IReadOnlyList<ObservedBuffEntry> ObservedBuffs { get; } = new List<ObservedBuffEntry>();
			public float ObservedBuffsReceivedTime => 0f;

			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
			public uint GetCurrentDomainTick() => CurrentDomainTick;
			public void Tick(uint currentTick) { }

			public void Apply(BaseBuffTemplate template, PredictionTick currentTick, ICharacter caster = null)
			{
				DirectApplyCalls++;
				LastDirectApplyTick = currentTick;
				LastCaster = caster;
			}

			public void ApplyAuthoritative(BaseBuffTemplate template, uint serverTick, ICharacter caster = null)
			{
				AuthoritativeApplyCalls++;
				LastAuthoritativeApplyTick = serverTick;
				LastCaster = caster;
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
			public void Write(Writer writer, bool includeEntries) { }
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

			public bool TryGetCooldownInstance(long id, out CooldownInstance instance)
			{
				instance = default;
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