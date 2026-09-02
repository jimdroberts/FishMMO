using System;
using System.IO;
using System.Reflection;
using FishMMO.Shared;
using FishNet.Object.Prediction;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regeneration stopped for good on every login after the first one a server process handled.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FishNet runs a character's replicate every tick, with default data when no input is queued,
	/// and it seeded the tick of those replicates from <c>TimeManager.LastPacketTick</c> — fed by
	/// every connection's packets and keeping only the highest tick it has ever seen. A client's
	/// tick counter restarts at zero when it connects, so after a relog, a character switch, a
	/// scene transfer back, or with a second player online, the freshly spawned character (whose
	/// owner's input had not arrived yet) was stamped with an earlier session's clock, tens of
	/// thousands of ticks ahead. The regeneration high-water mark then rejected every real input
	/// for the rest of the session, and the owner's client inherited the same far-future schedule
	/// through the reconcile. A freshly started server with one client never shows it.
	/// </para>
	/// <para>
	/// Two things pin the fix: the vendored FishNet edit that seeds from the OWNER's per-connection
	/// tick, and the controller rule that a replicate without real input cannot anchor the schedule.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class RegenReplicateTickSeedTests
	{
		private const int TickRate = 60;

		private GameObject gameObject;
		private CharacterAttributeController controller;
		private CharacterAttributeTemplate staminaTemplate;
		private CharacterAttributeTemplate staminaRegenTemplate;

		[SetUp]
		public void SetUp()
		{
			staminaRegenTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			staminaRegenTemplate.name = "TickSeedTestStaminaRegeneration";
			staminaRegenTemplate.InitialValue = 20;
			staminaRegenTemplate.AddToCache(staminaRegenTemplate.name);

			staminaTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			staminaTemplate.name = "TickSeedTestStamina";
			staminaTemplate.InitialValue = 100;
			staminaTemplate.AddToCache(staminaTemplate.name);

			gameObject = new GameObject("RegenReplicateTickSeedTest");
			controller = gameObject.AddComponent<CharacterAttributeController>();
		}

		[TearDown]
		public void TearDown()
		{
			if (gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}

			foreach (CharacterAttributeTemplate template in new[] { staminaTemplate, staminaRegenTemplate })
			{
				if (template != null)
				{
					template.RemoveFromCache();
					UnityEngine.Object.DestroyImmediate(template);
				}
			}
		}

		private static void SetField(object target, string name, object value)
		{
			FieldInfo field = target.GetType().GetField(name,
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			LogAssert.IsNotNull(field, $"{target.GetType().Name} must declare '{name}'.");
			field.SetValue(target, value);
		}

		private static T Field<T>(object target, string name)
		{
			FieldInfo field = target.GetType().GetField(name,
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			LogAssert.IsNotNull(field, $"{target.GetType().Name} must declare '{name}'.");
			return (T)field.GetValue(target);
		}

		/// <summary>Same arrangement as ResourceRegenerationTests: a real stamina resource, 4/s regen, 60 Hz.</summary>
		private CharacterResourceAttribute ArmRealStamina()
		{
			typeof(CharacterBehaviour)
				.GetProperty("Character", BindingFlags.Instance | BindingFlags.Public)
				.SetValue(controller, new Harness.StubCharacter());

			CharacterResourceAttribute stamina = new CharacterResourceAttribute(
				controller, staminaTemplate.ID, initialValue: 100, currentValue: 100f, modifier: 0);
			CharacterAttribute regen = new CharacterAttribute(
				controller, staminaRegenTemplate.ID, 20, initialModifier: 0);

			controller.StaminaResourceTemplateID = staminaTemplate.ID;
			Field<System.Collections.Generic.Dictionary<int, CharacterResourceAttribute>>(
				controller, "resourceAttributes")[staminaTemplate.ID] = stamina;

			SetField(controller, "cachedStaminaRegen", regen);
			SetField(controller, "regenTickInterval", (uint)TickRate);
			SetField(controller, "regenConsumptionLockoutTicks", (uint)TickRate);

			return stamina;
		}

		[Test]
		public void AReplicateWithoutInput_CannotAnchorTheSchedule_UntilARealOneHasRun()
		{
			LogAssert.IsFalse(controller.AcceptReplicateTick(ReplicateState.Ticked),
				"a default-data replicate before any real input must be ignored: its tick is an estimate, possibly of the wrong clock");
			LogAssert.IsTrue(controller.AcceptReplicateTick(ReplicateState.Ticked | ReplicateState.Created),
				"the first replicate carrying real input is accepted");
			LogAssert.IsTrue(controller.AcceptReplicateTick(ReplicateState.Ticked),
				"once real input has been seen, default-data ticks continue from it in the same domain and are accepted — a starved queue must not stop regen");
			LogAssert.IsTrue(controller.AcceptReplicateTick(ReplicateState.Replayed | ReplicateState.Ticked | ReplicateState.Created),
				"a replay of real input is real input");
		}

		[Test]
		public void AnotherClientsTick_OnTheSpawnReplicates_NoLongerStopsRegenForTheSession()
		{
			CharacterResourceAttribute stamina = ArmRealStamina();
			stamina.Consume(100f);
			LogAssert.IsTrue(stamina.CurrentValue <= 0f, "the bar starts empty");

			/* What the server used to do: a handful of default-data replicates before the owner's
			 * first input, stamped from the highest tick any client ever sent it — here the previous
			 * session, which ran for an hour at 60 Hz before the player relogged. */
			const uint someoneElsesClock = 60u * 60u * 60u;
			for (uint i = 0; i < 4; i++)
			{
				controller.ProcessReplicateTick(someoneElsesClock + i, ReplicateState.Ticked);
			}

			/* Then the owner's real inputs, on a clock that started at zero when it connected. */
			for (uint tick = 1; tick <= TickRate * 3; tick++)
			{
				controller.ProcessReplicateTick(tick, ReplicateState.Ticked | ReplicateState.Created);
			}

			LogAssert.IsTrue(stamina.CurrentValue > 0f,
				$"three seconds of the owner's own ticks must regenerate stamina, but it is still {stamina.CurrentValue}: " +
				"the spawn replicates' foreign tick was allowed to become the regen high-water mark");
			LogAssert.IsTrue(Mathf.Abs(stamina.CurrentValue - 8f) < 0.01f,
				$"expected two pulses of 4 (schedule anchored on the owner's first real tick), got {stamina.CurrentValue}");
		}

		[Test]
		public void AStarvedQueue_StillRegenerates_OnceRealInputHasBeenSeen()
		{
			CharacterResourceAttribute stamina = ArmRealStamina();
			stamina.Consume(100f);

			controller.ProcessReplicateTick(1u, ReplicateState.Ticked | ReplicateState.Created);
			// Packet loss: the server keeps ticking with default data in the owner's domain.
			for (uint tick = 2; tick <= TickRate * 2; tick++)
			{
				controller.ProcessReplicateTick(tick, ReplicateState.Ticked);
			}

			LogAssert.IsTrue(Mathf.Abs(stamina.CurrentValue - 4f) < 0.01f,
				$"default-data ticks after the first real input must keep the schedule moving; got {stamina.CurrentValue}");
		}

		[Test]
		public void FishNet_SeedsDefaultReplicates_FromTheOwnersPacketTick()
		{
			string path = Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Plugins/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs");
			LogAssert.IsTrue(File.Exists(path),
				$"Vendored FishNet file not found at {path}; the default-data replicate tick is seeded there.");

			string source = File.ReadAllText(path);
			int seedIndex = source.IndexOf("uint GetDefaultedLastReplicateTick()", StringComparison.Ordinal);
			LogAssert.IsTrue(seedIndex >= 0, "GetDefaultedLastReplicateTick is missing from the vendored FishNet.");

			int returnIndex = source.IndexOf("return _lastOrderedReplicatedTick;", seedIndex, StringComparison.Ordinal);
			LogAssert.IsTrue(returnIndex >= 0, "GetDefaultedLastReplicateTick no longer returns _lastOrderedReplicatedTick.");

			string body = source.Substring(seedIndex, returnIndex - seedIndex);
			LogAssert.IsTrue(body.Contains("Owner.PacketTick.Value("),
				"GetDefaultedLastReplicateTick (FISHMMO EDIT) no longer seeds from the owner's per-connection " +
				"PacketTick. TimeManager.LastPacketTick keeps the highest tick any connection ever sent, so every " +
				"login after a server's first (relog, character switch, scene transfer, second player) is stamped " +
				"with an earlier session's clock: regeneration stops for the session and pre-replicate " +
				"buff/cooldown ticks are translated by a garbage offset. " +
				"Re-apply the edit after a FishNet upgrade.");
		}
	}
}
