using System;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Mode = FishMMO.Shared.CharacterPredictionController.TickMode;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Prediction is the owning client's feature: an NPC is stepped by the server alone, without FishNet's replicate or
	/// reconcile, and a client runs nothing for a character it does not own.
	/// </summary>
	/// <remarks>
	/// NPCs ran the whole per-object prediction pipeline on the server, 30 times a second, though nobody predicts them:
	/// input queue, replicate history and a reconcile nobody received. Two big scenes loaded on one scene server (about
	/// 2,500 NPCs) took its main thread to 85-90%, and past that it fell behind, which every player felt as desync.
	/// </remarks>
	[TestFixture]
	public class NpcServerStepTests
	{
		private const string ControllerPath = "Assets/FishMMO/Shared/Implementation/Entity/Prediction/CharacterPredictionController.cs";
		private const string AttributesPath = "Assets/FishMMO/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterAttributeController.cs";
		private const string BuffsPath = "Assets/FishMMO/Modules/Buffs/Runtime/Buff/BuffController.cs";

		[TestCase(true, true, false, false, Mode.ServerSimulated, TestName = "An NPC on the server is simulated by the server alone")]
		[TestCase(true, true, false, true, Mode.ServerSimulated, TestName = "An NPC on the server is simulated alone even with forwarding on")]
		[TestCase(false, true, false, false, Mode.Observed, TestName = "An NPC on a client is only observed")]
		[TestCase(false, true, true, true, Mode.Observed, TestName = "An NPC is only observed on a client whatever its ownership")]
		[TestCase(true, false, false, false, Mode.Predicted, TestName = "A player on the server runs its owner's input")]
		[TestCase(false, false, true, false, Mode.Predicted, TestName = "A player predicts itself")]
		[TestCase(false, false, false, false, Mode.Observed, TestName = "Another player is only observed")]
		[TestCase(false, false, false, true, Mode.Predicted, TestName = "Another player is predicted only while forwarding is on")]
		public void TickMode_TruthTable(bool isServerStarted, bool serverDriven, bool isOwner, bool forwarding, Mode expected)
		{
			LogAssert.AreEqual(expected, CharacterPredictionController.ResolveTickMode(isServerStarted, serverDriven, isOwner, forwarding));
		}

		[TestCase(false, true, false, false, true, TestName = "An idle NPC nobody sees skips its step")]
		[TestCase(true, true, false, false, false, TestName = "An observed NPC always steps")]
		[TestCase(false, false, false, false, false, TestName = "An unseen NPC whose brain asked for something steps")]
		[TestCase(false, true, true, false, false, TestName = "An unseen NPC mid-cast steps")]
		[TestCase(false, true, false, true, false, TestName = "An unseen NPC in combat steps")]
		public void UnobservedSkip_OnlyWhenIdle(bool observed, bool inert, bool castActive, bool inCombat, bool expectedSkip)
		{
			LogAssert.AreEqual(expectedSkip, CharacterPredictionController.CanSkipServerStep(observed, inert, castActive, inCombat));
		}

		[Test]
		public void TheNpcStep_RunsTheControllers_WithoutReplicateOrReconcile()
		{
			string body = SourceScanPins.Body(SourceScanPins.ReadCode(ControllerPath), "private void SimulateOnServer()");
			LogAssert.IsNotNull(body, "SimulateOnServer must exist.");
			LogAssert.IsTrue(body.Contains(".OnReplicate(ref input, ServerStepState"), "each controller steps with the server's input");
			LogAssert.IsTrue(body.Contains("RunServerTickHooks();"), "then pushes to observers");
			LogAssert.IsTrue(body.Contains("bool observed = base.NetworkObject.Observers.Count > 0;") && body.Contains("CanSkipServerStep(observed"),
				"an idle NPC nobody observes is not stepped");
			int populate = body.IndexOf("PopulateInput", System.StringComparison.Ordinal);
			int skip = body.IndexOf("CanSkipServerStep(", System.StringComparison.Ordinal);
			LogAssert.IsTrue(populate >= 0 && skip > populate, "the brain's input is read before deciding, so a queued cast is never dropped");
			LogAssert.IsFalse(Regex.IsMatch(body, @"\bReplicate\("), "no FishNet replicate for an NPC");
			LogAssert.IsFalse(body.Contains("CreateReconcile") || body.Contains("Reconcile("), "no reconcile for an NPC");
			LogAssert.IsTrue(body.Contains("input.SetTick(tick)"), "its tick is the server's own, as FishNet's ownerless replicate gave it");
		}

		[Test]
		public void ObserverPushes_RunFromTheServerTickHook_NotFromTheReconcile()
		{
			string attributes = SourceScanPins.ReadCode(AttributesPath);
			string attributesReconcile = SourceScanPins.Body(attributes, "public void OnCreateReconcile(ref CharacterReconcileData reconcileData)");
			string attributesHook = SourceScanPins.Body(attributes, "public void OnServerTick()");
			LogAssert.IsFalse(attributesReconcile.Contains("MaybePushObserved"), "an NPC builds no reconcile, so no push may hang on it");
			LogAssert.IsTrue(attributesHook.Contains("MaybePushObservedResources();") && attributesHook.Contains("MaybePushObservedAttributes();"),
				"health bars and attributes reach observers from the hook");

			string buffs = SourceScanPins.ReadCode(BuffsPath);
			LogAssert.IsFalse(SourceScanPins.Body(buffs, "public void OnCreateReconcile(ref CharacterReconcileData reconcileData)").Contains("PushObservedBuffs"),
				"buff pushes do not hang on the reconcile");
			LogAssert.IsTrue(SourceScanPins.Body(buffs, "public void OnServerTick()").Contains("PushObservedBuffs();"), "buff pushes run from the hook");

			string controller = SourceScanPins.ReadCode(ControllerPath);
			string reconcile = SourceScanPins.Body(controller, "public override void CreateReconcile()");
			LogAssert.IsTrue(reconcile.Contains("RunServerTickHooks();"), "players push from the same hook");
		}

		[Test]
		public void AnNpc_HasStateForwardingSwitchedOff()
		{
			string onStart = SourceScanPins.Body(SourceScanPins.ReadCode(ControllerPath), "public override void OnStartNetwork()");
			int off = onStart.IndexOf("SetStateForwarding(false)", StringComparison.Ordinal);
			int transport = onStart.IndexOf("ApplyObserverTransportMode();", StringComparison.Ordinal);
			LogAssert.IsTrue(off >= 0 && onStart.Contains("serverDrivenInput"), "an NPC's observers take its state from broadcasts, never a reconcile");
			LogAssert.IsTrue(transport > off, "before the transport mode is chosen from it");
		}
	}
}
