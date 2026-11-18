using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared;
using FishNet.Object.Prediction;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A reconcile replay reaches the same stamina, and so the same movement, as the server.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reported as "running out of stamina and jumping or trying to sprint desynchronizes the client
	/// and it teleports a little". Stamina decides whether a tick sprints or jumps, and regeneration
	/// was skipped in replay. With the bar empty, a pulse the server had applied was missing from
	/// every replay that crossed it, so the replay refused sprint the server had allowed and pulled
	/// the character back, until a later reconcile carried the pulse and pushed it forward again.
	/// </para>
	/// <para>
	/// Real controllers and real resources on both sides. The step mirrors the order a replicate
	/// runs in: the motor spends first, then the attribute controller regenerates.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class StaminaReplayTests
	{
		private const int TickRate = 30;

		/// <summary>Ticks the owner runs ahead of the state it is reconciling to.</summary>
		private const int Latency = 6;

		/// <summary>Long enough for several pulse and lockout cycles.</summary>
		private const uint Ticks = TickRate * 12;

		private const float SprintCost = Constants.Character.SprintStaminaCost / TickRate;

		private const float Tolerance = 0.001f;

		private const ReplicateState Live = ReplicateState.Ticked | ReplicateState.Created;
		private const ReplicateState Replayed = ReplicateState.Replayed | ReplicateState.Created;

		private readonly List<GameObject> hosts = new List<GameObject>();
		private CharacterAttributeTemplate staminaTemplate;
		private CharacterAttributeTemplate staminaRegenTemplate;

		[SetUp]
		public void SetUp()
		{
			staminaRegenTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			staminaRegenTemplate.name = "StaminaReplayTestRegeneration";
			staminaRegenTemplate.InitialValue = 20;
			staminaRegenTemplate.AddToCache(staminaRegenTemplate.name);

			staminaTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			staminaTemplate.name = "StaminaReplayTestStamina";
			staminaTemplate.InitialValue = 100;
			staminaTemplate.AddToCache(staminaTemplate.name);
		}

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject host in hosts)
			{
				Object.DestroyImmediate(host);
			}
			hosts.Clear();

			foreach (CharacterAttributeTemplate template in new[] { staminaTemplate, staminaRegenTemplate })
			{
				template.RemoveFromCache();
				Object.DestroyImmediate(template);
			}
		}

		private sealed class Peer
		{
			public CharacterAttributeController Controller;
			public CharacterResourceAttribute Stamina;
		}

		private static void SetField(object target, string name, object value)
		{
			FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			LogAssert.IsNotNull(field, $"{target.GetType().Name} must declare '{name}'.");
			field.SetValue(target, value);
		}

		private static T Field<T>(object target, string name)
		{
			FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			LogAssert.IsNotNull(field, $"{target.GetType().Name} must declare '{name}'.");
			return (T)field.GetValue(target);
		}

		/// <summary>A controller with an emptied stamina bar, 4/s regen and a one second pulse and lockout.</summary>
		private Peer CreatePeer(string name)
		{
			GameObject host = new GameObject(name);
			hosts.Add(host);

			CharacterAttributeController controller = host.AddComponent<CharacterAttributeController>();
			typeof(CharacterBehaviour)
				.GetProperty("Character", BindingFlags.Instance | BindingFlags.Public)
				.SetValue(controller, new Harness.StubCharacter());

			CharacterResourceAttribute stamina = new CharacterResourceAttribute(
				controller, staminaTemplate.ID, initialValue: 100, currentValue: 100f, modifier: 0);
			CharacterAttribute regen = new CharacterAttribute(controller, staminaRegenTemplate.ID, 20, initialModifier: 0);

			controller.StaminaResourceTemplateID = staminaTemplate.ID;
			Field<Dictionary<int, CharacterResourceAttribute>>(controller, "resourceAttributes")[staminaTemplate.ID] = stamina;
			SetField(controller, "cachedStaminaRegen", regen);
			SetField(controller, "regenTickInterval", (uint)TickRate);
			SetField(controller, "regenConsumptionLockoutTicks", (uint)TickRate);

			stamina.Consume(100f);
			return new Peer() { Controller = controller, Stamina = stamina };
		}

		/// <summary>One replicate: the motor pays if it can, then the controller regenerates.</summary>
		/// <returns>Whether the movement was paid for.</returns>
		private static bool Step(Peer peer, uint tick, float cost, ReplicateState state)
		{
			bool moved = peer.Stamina.CurrentValue >= cost;
			if (moved)
			{
				peer.Stamina.Consume(cost);
			}
			peer.Controller.ProcessReplicateTick(tick, state);
			return moved;
		}

		/// <summary>
		/// Runs the server's timeline, then the owner's: a live tick, a reconcile to a state
		/// <see cref="Latency"/> ticks old, and the replay back up to the present.
		/// </summary>
		private void AssertReplaysMatchTheServer(float cost)
		{
			Peer server = CreatePeer("Server");
			Peer owner = CreatePeer("Owner");

			var moved = new bool[Ticks + 1];
			var states = new CharacterAttributeResourceState[Ticks + 1];
			int movedTicks = 0;
			for (uint tick = 1; tick <= Ticks; tick++)
			{
				moved[tick] = Step(server, tick, cost, Live);
				states[tick] = server.Controller.GetResourceState();
				movedTicks += moved[tick] ? 1 : 0;
			}
			LogAssert.IsTrue(movedTicks > 0, "the scenario must recover enough stamina to move at all");
			LogAssert.IsTrue(movedTicks < Ticks, "the scenario must run out of stamina");

			for (uint now = 1; now <= Ticks; now++)
			{
				Step(owner, now, cost, Live);
				if (now <= Latency)
				{
					continue;
				}

				uint reconciled = now - Latency;
				owner.Controller.ApplyResourceState(states[reconciled]);
				for (uint tick = reconciled + 1; tick <= now; tick++)
				{
					bool replayedMove = Step(owner, tick, cost, Replayed);
					LogAssert.IsTrue(replayedMove == moved[tick],
						$"replaying tick {tick} from a reconcile at {reconciled} {(replayedMove ? "moved" : "refused")} " +
						$"but the server {(moved[tick] ? "moved" : "refused")}; the owner is corrected every time this differs");
				}

				LogAssert.IsTrue(Mathf.Abs(owner.Stamina.CurrentValue - states[now].Stamina) < Tolerance,
					$"after replaying to tick {now} the owner has {owner.Stamina.CurrentValue} stamina and the server had " +
					$"{states[now].Stamina}");
			}
		}

		[Test]
		public void SprintingOnAnEmptyBar_ReplaysTheSameAsTheServer()
		{
			AssertReplaysMatchTheServer(SprintCost);
		}

		[Test]
		public void JumpingOnAnEmptyBar_ReplaysTheSameAsTheServer()
		{
			AssertReplaysMatchTheServer(Constants.Character.JumpStaminaCost);
		}

		[Test]
		public void AReconcileLoweringTheValue_IsNotASpend()
		{
			Peer owner = CreatePeer("Owner");
			owner.Stamina.Gain(4f);
			owner.Controller.ProcessReplicateTick(1u, Live);

			CharacterAttributeResourceState state = owner.Controller.GetResourceState();
			state.Stamina = 0f;
			owner.Controller.ApplyResourceState(state);

			owner.Controller.ProcessReplicateTick(2u, Live);

			LogAssert.IsFalse(Field<Dictionary<int, uint>>(owner.Controller, "lastConsumedResourceTicks").ContainsKey(staminaTemplate.ID),
				"a correction from the server must not open a lockout the server never opened");
		}

		[Test]
		public void TheLockoutTravelsWithTheReconcile()
		{
			Peer server = CreatePeer("Server");
			server.Controller.ProcessReplicateTick(1u, Live);
			server.Stamina.Gain(10f);
			server.Controller.ProcessReplicateTick(2u, Live);
			server.Stamina.Consume(1f);
			server.Controller.ProcessReplicateTick(3u, Live);

			CharacterAttributeResourceState state = server.Controller.GetResourceState();
			LogAssert.AreEqual(3u, state.StaminaConsumedTick, "the reconcile must name the tick stamina was spent");

			Peer owner = CreatePeer("Owner");
			owner.Controller.ApplyResourceState(state);

			LogAssert.AreEqual(3u, Field<Dictionary<int, uint>>(owner.Controller, "lastConsumedResourceTicks")[staminaTemplate.ID],
				"the owner must adopt the server's lockout");

			state.StaminaConsumedTick = 0u;
			owner.Controller.ApplyResourceState(state);

			LogAssert.IsFalse(Field<Dictionary<int, uint>>(owner.Controller, "lastConsumedResourceTicks").ContainsKey(staminaTemplate.ID),
				"zero means the server has no lockout, so the owner must drop a predicted one");
		}
	}
}
