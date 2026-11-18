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
	/// Reported 2026-11-14: "Stamina is still stuck at 99/100 with regen." A sprint, then standing still: both the server and
	/// the owner must come back to exactly full, with the owner reconciled the way it is live (every second tick, a few ticks
	/// behind) and the server running ticks with no input in them (an empty queue).
	/// </summary>
	[TestFixture]
	public class StaminaFullRecoveryTests
	{
		private const int TickRate = 30;
		private const int Latency = 6;
		private const float SprintCost = Constants.Character.SprintStaminaCost / TickRate;

		private const ReplicateState Live = ReplicateState.Ticked | ReplicateState.Created;
		private const ReplicateState Replayed = ReplicateState.Replayed | ReplicateState.Created;
		private const ReplicateState Default = ReplicateState.Ticked;

		private readonly List<GameObject> hosts = new List<GameObject>();
		private CharacterAttributeTemplate staminaTemplate;
		private CharacterAttributeTemplate staminaRegenTemplate;

		[SetUp]
		public void SetUp()
		{
			staminaRegenTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			staminaRegenTemplate.name = "StaminaFullRecoveryRegeneration";
			staminaRegenTemplate.InitialValue = 20;
			staminaRegenTemplate.AddToCache(staminaRegenTemplate.name);

			staminaTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			staminaTemplate.name = "StaminaFullRecoveryStamina";
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

		/// <summary>A loaded character (so the bar clamps to its maximum) with full stamina, 4/s regen, live pulse and lockout lengths.</summary>
		private Peer CreatePeer(string name)
		{
			GameObject host = new GameObject(name);
			hosts.Add(host);
			CharacterAttributeController controller = host.AddComponent<CharacterAttributeController>();
			var character = new Harness.StubCharacter { Flags = 1 << (int)CharacterFlags.IsLoaded };
			typeof(CharacterBehaviour).GetProperty("Character", BindingFlags.Instance | BindingFlags.Public).SetValue(controller, character);

			CharacterResourceAttribute stamina = new CharacterResourceAttribute(controller, staminaTemplate.ID, initialValue: 100, currentValue: 100f, modifier: 0);
			CharacterAttribute regen = new CharacterAttribute(controller, staminaRegenTemplate.ID, 20, initialModifier: 0);
			controller.StaminaResourceTemplateID = staminaTemplate.ID;
			Field<Dictionary<int, CharacterResourceAttribute>>(controller, "resourceAttributes")[staminaTemplate.ID] = stamina;
			SetField(controller, "cachedStaminaRegen", regen);
			// What EnsureRegenIntervalCurrent derives at 30 Hz: Ceiling(1 / (1/30)) in doubles.
			uint interval = (uint)System.Math.Max(1, (int)System.Math.Ceiling(1.0 / (1.0 / TickRate)));
			SetField(controller, "regenTickInterval", interval);
			SetField(controller, "regenConsumptionLockoutTicks", interval);
			return new Peer { Controller = controller, Stamina = stamina };
		}

		/// <summary>Sprinting for the first <paramref name="sprintTicks"/> ticks, then standing still.</summary>
		private static float CostAt(uint tick, uint sprintTicks) => tick <= sprintTicks ? SprintCost : 0f;

		private static void Step(Peer peer, uint tick, float cost, ReplicateState state)
		{
			if (cost > 0f && peer.Stamina.CurrentValue >= cost)
			{
				peer.Stamina.Consume(cost);
			}
			peer.Controller.ProcessReplicateTick(tick, state);
		}

		[TestCase(37u)]
		[TestCase(61u)]
		[TestCase(100u)]
		public void TheServer_ComesBackToFull(uint sprintTicks)
		{
			Peer server = CreatePeer("Server");
			for (uint tick = 1; tick <= sprintTicks + TickRate * 40; tick++)
			{
				Step(server, tick, CostAt(tick, sprintTicks), Live);
			}
			LogAssert.AreEqual(100f, server.Stamina.CurrentValue, "Forty seconds after the sprint the server must be full.");
		}

		/// <summary>The server also runs ticks with no input (an empty queue): numbered by its estimate, and real inputs for ticks it already ran are refused.</summary>
		[TestCase(37u)]
		[TestCase(61u)]
		public void TheServer_ComesBackToFull_WithEmptyQueueTicks(uint sprintTicks)
		{
			Peer server = CreatePeer("Server");
			uint lastRun = 0;
			for (uint step = 1; step <= sprintTicks + TickRate * 40; step++)
			{
				// Every fifth server tick the queue is empty: default data on the next estimated tick, and the input for that
				// tick arrives later and runs as a duplicate number.
				if (step % 5 == 0)
				{
					Step(server, lastRun + 1, 0f, Default);
				}
				Step(server, step, CostAt(step, sprintTicks), Live);
				lastRun = step;
			}
			LogAssert.AreEqual(100f, server.Stamina.CurrentValue, "Empty-queue ticks must not leave the server short of full.");
		}

		/// <summary>The owner, reconciled every second tick to a state <see cref="Latency"/> ticks old, ends exactly where the server does.</summary>
		[TestCase(37u)]
		[TestCase(61u)]
		[TestCase(100u)]
		public void TheOwner_ComesBackToFull(uint sprintTicks)
		{
			Peer server = CreatePeer("Server");
			Peer owner = CreatePeer("Owner");
			uint total = sprintTicks + TickRate * 40;
			var states = new CharacterAttributeResourceState[total + 1];
			for (uint tick = 1; tick <= total; tick++)
			{
				Step(server, tick, CostAt(tick, sprintTicks), Live);
				states[tick] = server.Controller.GetResourceState();
			}
			for (uint now = 1; now <= total; now++)
			{
				Step(owner, now, CostAt(now, sprintTicks), Live);
				if (now <= Latency || now % 2 != 0)
				{
					continue;
				}
				uint reconciled = now - Latency;
				owner.Controller.ApplyResourceState(states[reconciled]);
				for (uint tick = reconciled + 1; tick <= now; tick++)
				{
					Step(owner, tick, CostAt(tick, sprintTicks), Replayed);
				}
			}
			LogAssert.AreEqual(100f, server.Stamina.CurrentValue, "The server is full.");
			LogAssert.AreEqual(100f, owner.Stamina.CurrentValue, "The owner is full too.");
		}
	}
}
