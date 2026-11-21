using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FishMMO.TestHarness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.PlayMode
{
	/// <summary>
	/// The sims again, recorded: each run is captured to a JPEG sequence with every collider drawn as a wireframe
	/// (character capsules, attack shapes, triggers), plus each sim's own debug shapes (lag-compensation rewound poses,
	/// server sweep records, hits, rollback snapshots and replay paths), and checked with the same assertions as its
	/// plain test. A <c>summary.json</c> beside the frames holds the run's numbers.
	/// </summary>
	/// <remarks>
	/// Skipped unless <c>FISHMMO_SIM_CAPTURE</c> names an output directory, so the ordinary suite pays nothing. Must run
	/// with graphics (no <c>-nographics</c>; xvfb is fine). Frames become animated WebP with ffmpeg afterwards.
	/// </remarks>
	public class SimCapturePlayModeTests
	{
		private GameObject host;
		private SimCapture capture;

		[UnityTearDown]
		public IEnumerator TearDownHost()
		{
			if (capture != null)
			{
				capture.StopRecording();
				capture.Flush();
				Object.DestroyImmediate(capture.gameObject);
				capture = null;
			}
			if (host != null)
			{
				Object.DestroyImmediate(host);
				host = null;
			}
			yield return null;
		}

		/// <summary>Characters already standing in the scene when this test began: what earlier sims left behind.</summary>
		private int leftoverCharactersAtStart;

		private void RequireCapture()
		{
			if (SimCapture.OutputRoot == null)
			{
				Assert.Ignore($"set {SimCapture.OutputVariable} to an output directory to record the sims");
			}
			leftoverCharactersAtStart = Object.FindObjectsByType<FishMMO.Shared.BaseCharacter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
		}

		private static ushort FreeUdpPort()
		{
			using (System.Net.Sockets.UdpClient probe = new System.Net.Sockets.UdpClient(0))
			{
				return (ushort)((System.Net.IPEndPoint)probe.Client.LocalEndPoint).Port;
			}
		}

		private static IEnumerator WaitSeconds(float seconds, Func<bool> until = null)
		{
			float deadline = Time.realtimeSinceStartup + seconds;
			while (Time.realtimeSinceStartup < deadline && (until == null || !until()))
			{
				yield return null;
			}
		}

		private void WriteSummary(SimCapture capture, Dictionary<string, object> values)
		{
			values["leftoverCharactersAtStart"] = leftoverCharactersAtStart;
			values["frames"] = capture.FramesWritten;
			values["fps"] = capture.FramesPerSecond;
			values["width"] = capture.Width;
			values["height"] = capture.Height;
			StringBuilder sb = new StringBuilder("{\n");
			int i = 0;
			foreach (KeyValuePair<string, object> pair in values)
			{
				string value = pair.Value switch
				{
					null => "null",
					bool b => b ? "true" : "false",
					string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"",
					float f => f.ToString("R", CultureInfo.InvariantCulture),
					double d => d.ToString("R", CultureInfo.InvariantCulture),
					IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
					_ => "\"" + pair.Value + "\"",
				};
				sb.Append("  \"").Append(pair.Key).Append("\": ").Append(value).Append(++i < values.Count ? ",\n" : "\n");
			}
			sb.Append("}\n");
			File.WriteAllText(Path.Combine(capture.Directory, "summary.json"), sb.ToString());
		}

		// ── Combat: server-authoritative casts, lag-compensated sweeps ────────────────────────────────────────────

		[UnityTest]
		public IEnumerator Capture_Combat_LagCompensatedSweeps()
		{
			RequireCapture();
			host = new GameObject("CombatCapture");
			CombatSimBootstrap sim = host.AddComponent<CombatSimBootstrap>();
			sim.AutoSweep = false;
			sim.ClaimMs = 250;
			sim.TeamSize = 2;
			sim.Port = FreeUdpPort();

			yield return WaitSeconds(180f, () => sim.Ready);
			Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");

			ColliderOverlay overlay = host.AddComponent<ColliderOverlay>();
			sim.AttachCaptureOverlay(overlay);
			capture = SimCapture.Create("combat", new Vector3(0f, 1f, 0f), new Vector3(0f, 10.5f, -12.5f));
			capture.Caption = () => sim.CaptureCaption("Combat sim — 2v2 NPCs, server-authoritative, lag-compensated sweeps");
			capture.StartRecording();

			Dictionary<string, object> summary = new Dictionary<string, object>();
			int[] claims = { 250, 500, 0 };
			float[] spans = { 12f, 10f, 8f };
			for (int phase = 0; phase < claims.Length; ++phase)
			{
				sim.ClaimMs = claims[phase];
				long rewindsBefore = sim.RewindsResolved;
				long claimsBefore = sim.ClaimsConsulted;
				yield return WaitSeconds(spans[phase]);
				summary[$"phase{phase}_claimMs"] = claims[phase];
				summary[$"phase{phase}_rewinds"] = sim.RewindsResolved - rewindsBefore;
				summary[$"phase{phase}_claimsConsulted"] = sim.ClaimsConsulted - claimsBefore;
			}
			capture.StopRecording();
			capture.Flush();

			summary["castsStarted"] = sim.CastsStarted;
			summary["damageEvents"] = sim.DamageEvents;
			summary["totalDamage"] = sim.TotalDamage;
			summary["healEvents"] = sim.HealEvents;
			summary["buffAdds"] = sim.BuffAdds;
			summary["buffRemoves"] = sim.BuffRemoves;
			summary["kills"] = sim.Kills;
			summary["claimsConsulted"] = sim.ClaimsConsulted;
			summary["rewindsResolved"] = sim.RewindsResolved;
			summary["aliveFighters"] = sim.AliveFighters;
			summary["maxLiveAbilityObjects"] = sim.CaptureMaxAbilityObjects;
			summary["overlayLines"] = overlay.LastLineCount;
			WriteSummary(capture, summary);

			Assert.Greater(sim.CastsStarted, 0, "no fighter ever started a cast");
			Assert.Greater(sim.DamageEvents, 0, "no damage was ever dealt");
			Assert.Greater(sim.HealEvents, 0, "no heal ever landed");
			Assert.Greater(sim.BuffAdds, 0, "no buff was ever applied");
			Assert.Greater(sim.ClaimsConsulted, 0, "the latency claim was never consulted — the rewind path never ran");
			Assert.Greater(sim.RewindsResolved, 0, "no rewind resolved against the history ring");
			Assert.Greater(capture.FramesWritten, 100, "the capture wrote almost nothing");
		}

		// ── Prediction probe: blank ticks, hit claims, observed copies ─────────────────────────────────────────

		[UnityTest]
		public IEnumerator Capture_PredictionProbe()
		{
			RequireCapture();
			host = new GameObject("ProbeCapture");
			CombatSimBootstrap sim = host.AddComponent<CombatSimBootstrap>();
			sim.AutoSweep = false;
			sim.ClaimMs = 0;
			sim.TeamSize = 1;
			sim.Port = FreeUdpPort();
			sim.DirectorPaused = true;

			yield return WaitSeconds(180f, () => sim.Ready);
			Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");

			ColliderOverlay overlay = host.AddComponent<ColliderOverlay>();
			sim.AttachCaptureOverlay(overlay);
			capture = SimCapture.Create("prediction_probe", new Vector3(0f, 1f, 0f), new Vector3(0f, 7.5f, -9.5f));
			capture.Caption = () => sim.CaptureCaption("Prediction probe — blank ticks, claimed hits, observed copies");
			capture.FocusProvider = sim.CaptureFocus;
			capture.StartRecording();
			yield return WaitSeconds(1.5f);

			PredictionProbeResult result = new PredictionProbeResult();
			yield return sim.RunPredictionProbe(result);
			yield return WaitSeconds(2f);
			capture.StopRecording();
			capture.Flush();

			Dictionary<string, object> summary = new Dictionary<string, object>
			{
				["error"] = result.Error,
				["inputsToFinishSteady"] = result.InputsToFinishSteady,
				["inputsToFinishWithBlanks"] = result.InputsToFinishWithBlanks,
				["blankTicksRun"] = result.BlankTicksRun,
				["castSurvivedEveryBlankTick"] = result.CastSurvivedEveryBlankTick,
				["spawnedOnABlankTick"] = result.SpawnedOnABlankTick,
				["spawnedOnFinish"] = result.SpawnedOnFinish,
				["cancelledAfterStall"] = result.CancelledAfterStall,
				["spawnedWhenCancelled"] = result.SpawnedWhenCancelled,
				["claimGrantedWithinTolerance"] = result.ClaimGrantedWithinTolerance,
				["claimRefusedOutsideTolerance"] = result.ClaimRefusedOutsideTolerance,
				["claimNotGrantedTwice"] = result.ClaimNotGrantedTwice,
				["observedBegan"] = result.ObservedBegan,
				["observedTicksBeforeClock"] = result.ObservedTicksBeforeClock,
				["observedTicksAtThreeAndAHalf"] = result.ObservedTicksAtThreeAndAHalf,
				["observedEndWaitedForClock"] = result.ObservedEndWaitedForClock,
				["observedEndedOnClock"] = result.ObservedEndedOnClock,
			};
			WriteSummary(capture, summary);

			Assert.IsNull(result.Error, "the probe could not run: " + result.Error);
			Assert.Greater(result.BlankTicksRun, 0);
			Assert.IsTrue(result.CastSurvivedEveryBlankTick);
			Assert.IsFalse(result.SpawnedOnABlankTick);
			Assert.AreEqual(result.InputsToFinishSteady, result.InputsToFinishWithBlanks);
			Assert.IsTrue(result.ClaimGrantedWithinTolerance);
			Assert.IsTrue(result.ClaimNotGrantedTwice);
			Assert.IsTrue(result.ClaimRefusedOutsideTolerance);
			Assert.IsTrue(result.ObservedBegan);
			Assert.AreEqual(3, result.ObservedTicksAtThreeAndAHalf);
			Assert.IsTrue(result.ObservedEndedOnClock);
		}

		// ── Punch + projectile: hits that land only through the rewind ──────────────────────────────────────────

		[UnityTest]
		public IEnumerator Capture_LagComp_PunchAndProjectile()
		{
			RequireCapture();
			host = new GameObject("LagCompCapture");
			CombatSimBootstrap sim = host.AddComponent<CombatSimBootstrap>();
			sim.AutoSweep = false;
			sim.ClaimMs = 0;
			sim.TeamSize = 1;
			sim.Port = FreeUdpPort();
			sim.DirectorPaused = true;

			yield return WaitSeconds(180f, () => sim.Ready);
			Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");

			ColliderOverlay overlay = host.AddComponent<ColliderOverlay>();
			sim.AttachCaptureOverlay(overlay);
			capture = SimCapture.Create("lagcomp_punch_projectile", new Vector3(4.6f, 0.5f, 0.3f), new Vector3(0f, 15f, -3.5f));
			capture.FramesPerSecond = 30f; // encoded at 15 fps: half speed, so a one-tick punch is visible
			capture.Caption = () => sim.CaptureCaption("Lag compensation — shipped Punch and Lesser Fireball vs a strafing victim (7 m/s)")
				+ "\n<b>" + sim.LagCompStatus + "</b>";
			capture.StartRecording();
			yield return WaitSeconds(1f);

			LagCompProbeResult result = new LagCompProbeResult();
			yield return sim.RunLagCompProbe(result, LagCompProbePlayModeTests.Load(LagCompProbePlayModeTests.PunchPath),
				LagCompProbePlayModeTests.Load(LagCompProbePlayModeTests.ProjectilePath), LagCompProbePlayModeTests.ClaimMs);
			yield return WaitSeconds(1f);
			capture.StopRecording();
			capture.Flush();

			StringBuilder shots = new StringBuilder("[\n");
			for (int i = 0; i < result.Shots.Count; ++i)
			{
				LagCompShot s = result.Shots[i];
				shots.Append(string.Format(CultureInfo.InvariantCulture,
					"  {{\"ability\": \"{0}\", \"mode\": \"{1}\", \"claimMs\": {2}, \"rewindTicks\": {3:F3}, \"sweepTick\": {4}, " +
					"\"liveZ\": {5:F3}, \"rewoundZ\": {6:F3}, \"hit\": {7}, \"expectHit\": {8}, \"sweeps\": {9}, \"note\": \"{10}\"}}{11}\n",
					s.Ability, s.Mode, s.ClaimMs, s.RewindTicks, s.SweepTick, s.LiveOffset, s.RewoundOffset,
					s.Hit ? "true" : "false", s.ExpectHit ? "true" : "false", s.SweepsRecorded, s.Note ?? "", i + 1 < result.Shots.Count ? "," : ""));
			}
			shots.Append("]\n");
			File.WriteAllText(Path.Combine(capture.Directory, "shots.json"), shots.ToString());
			WriteSummary(capture, new Dictionary<string, object>
			{
				["error"] = result.Error,
				["victimSpeed"] = result.VictimSpeed,
				["claimMs"] = LagCompProbePlayModeTests.ClaimMs,
				["shots"] = result.Shots.Count,
			});
			LagCompProbePlayModeTests.AssertProbe(result);
		}

		// ── Platform: predict → snapshot → rollback → replay on moving decks ─────────────────────────────────────

		[UnityTest]
		public IEnumerator Capture_Platform_Rollback_250ms() => CapturePlatform(250, "platform_rtt250");

		[UnityTest]
		public IEnumerator Capture_Platform_Rollback_500ms() => CapturePlatform(500, "platform_rtt500");

		private IEnumerator CapturePlatform(int rtt, string clip)
		{
			RequireCapture();
			host = new GameObject("PlatformCapture");
			PlatformSimHarness harness = host.AddComponent<PlatformSimHarness>();
			harness.TimeScale = 3f;
			harness.AlwaysReconcile = true;
			harness.RttMs = rtt;
			yield return null;
			harness.ResetSimulation();

			int clientLayer = PlatformSimHarness.ClientWorldLayer;
			int serverLayer = PlatformSimHarness.ServerWorldLayer;
			Color clientColor = new Color(0.35f, 0.65f, 1f, 0.9f);
			Color serverColor = ColliderOverlay.CharacterColor;
			ColliderOverlay overlay = host.AddComponent<ColliderOverlay>();
			overlay.MaxColliderExtent = 14f;
			overlay.Classify = collider =>
				collider.gameObject.layer == clientLayer ? clientColor
				: collider.gameObject.layer == serverLayer ? serverColor
				: (Color?)null;
			overlay.ExtraShapes += o =>
			{
				List<Vector3> path = harness.LastReplayPath;
				for (int i = 1; i < path.Count; ++i)
				{
					o.AddLine(path[i - 1] + Vector3.up * 0.9f, path[i] + Vector3.up * 0.9f, ColliderOverlay.SweepColor);
				}
				if (path.Count > 0)
				{
					Vector3 snap = harness.LastRollbackSnapshot;
					o.AddCapsule(snap + Vector3.up * 0.35f, snap + Vector3.up * 1.45f, 0.35f, ColliderOverlay.RewindColor);
					o.AddLine(snap + Vector3.up * 0.9f, harness.LastRollbackLive + Vector3.up * 0.9f, ColliderOverlay.HitColor);
				}
			};

			capture = SimCapture.Create(clip, new Vector3(0f, 0.5f, 2f), new Vector3(0f, 9f, -12.5f));
			capture.Caption = () =>
				$"<b>Platform sim — client predicts, server twin authoritative, RTT {rtt} ms (×{harness.TimeScale} speed)</b>\n" +
				$"tick {harness.ClientTick}  snapshots {harness.SnapshotsChecked}  rollbacks {harness.Rollbacks}  mispredictions {harness.Mispredictions}  " +
				$"max error {harness.MaxPredictionError:F4} m\n" +
				$"crossings {harness.CompletedCrossings}  fall-throughs {harness.TotalFallThroughs}  identity fails {harness.IdentityFailures}  " +
				$"carry {harness.CarryTicks}/{harness.CarrySlips} slips  phase err {harness.MaxPlatformPhaseError:F4}\n" +
				$"last rollback: tick {harness.LastRollbackTick}, replayed {Mathf.Max(0, harness.LastReplayPath.Count - 1)} ticks\n" +
				"<color=#59a6ff>blue</color> client colliders  <color=#33ff4d>green</color> server colliders  " +
				"<color=#ffee26>yellow</color> snapshot pose  <color=#ff40e6>magenta</color> replay path  <color=#ff2626>red</color> live-vs-snapshot";
			capture.StartRecording();
			yield return WaitSeconds(16f);
			capture.StopRecording();
			capture.Flush();

			WriteSummary(capture, new Dictionary<string, object>
			{
				["rttMs"] = rtt,
				["timeScale"] = harness.TimeScale,
				["ticks"] = harness.ClientTick,
				["snapshots"] = harness.SnapshotsChecked,
				["rollbacks"] = harness.Rollbacks,
				["mispredictions"] = harness.Mispredictions,
				["maxPredictionError"] = harness.MaxPredictionError,
				["identityChecks"] = harness.IdentityChecks,
				["identityFailures"] = harness.IdentityFailures,
				["edgeReplayDivergences"] = harness.EdgeReplayDivergences,
				["crossings"] = harness.CompletedCrossings,
				["fallThroughs"] = harness.TotalFallThroughs,
				["recoveredDips"] = harness.RecoveredDips,
				["carryTicks"] = harness.CarryTicks,
				["carrySlips"] = harness.CarrySlips,
				["maxPlatformPhaseError"] = harness.MaxPlatformPhaseError,
				["waterEntries"] = harness.WaterEntries,
			});

			Assert.Greater((int)harness.ClientTick, 600, "the simulation must actually have run");
			Assert.GreaterOrEqual(harness.CompletedCrossings, 1, "the rider never completed a ferry crossing");
			Assert.AreEqual(0, harness.TotalFallThroughs, "a rider fell through a deck");
		}

		// ── Regions: trigger volumes, nested handoff, attribute ledger ────────────────────────────────────────────

		[UnityTest]
		public IEnumerator Capture_Regions()
		{
			RequireCapture();
			host = new GameObject("RegionCapture");
			RegionSimHarness sim = host.AddComponent<RegionSimHarness>();
			sim.WalkSpeed = 9f;
			yield return WaitSeconds(180f, () => sim.Ready);
			Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");

			ColliderOverlay overlay = host.AddComponent<ColliderOverlay>();
			overlay.MaxColliderExtent = 20f;
			capture = SimCapture.Create("regions", new Vector3(3f, 0f, 0f), new Vector3(0f, 21f, -24f));
			capture.Caption = () =>
				"<b>Region sim — server trigger volumes, nested ownership handoff, attribute ledger</b>\n" +
				$"laps {sim.Laps}  enters {sim.TotalEnters}  pairing sound {sim.PairingIsSound}  nesting violations {sim.NestingViolations}  " +
				$"ledger violations {sim.LedgerViolations}\nattribute base {sim.BaseAttributeValue}, max observed {sim.MaxObservedAttribute}\n" +
				"<color=#33d9ff>cyan</color> region triggers  <color=#33ff4d>green</color> walker collider  <color=#bfbfcc>grey</color> static";
			capture.StartRecording();
			yield return WaitSeconds(50f, () => sim.Laps >= 2);
			yield return WaitSeconds(1f);
			capture.StopRecording();
			capture.Flush();

			WriteSummary(capture, new Dictionary<string, object>
			{
				["laps"] = sim.Laps,
				["totalEnters"] = sim.TotalEnters,
				["pairingIsSound"] = sim.PairingIsSound,
				["nestingViolations"] = sim.NestingViolations,
				["ledgerViolations"] = sim.LedgerViolations,
				["baseAttribute"] = sim.BaseAttributeValue,
				["maxObservedAttribute"] = sim.MaxObservedAttribute,
			});

			Assert.GreaterOrEqual(sim.Laps, 2);
			Assert.IsTrue(sim.PairingIsSound);
			Assert.AreEqual(0, sim.NestingViolations);
			Assert.AreEqual(0, sim.LedgerViolations);
			Assert.Greater(sim.MaxObservedAttribute, sim.BaseAttributeValue);
		}

		// ── Interactables: the server interact chain ─────────────────────────────────────────────────────────────

		[UnityTest]
		public IEnumerator Capture_Interactables()
		{
			RequireCapture();
			host = new GameObject("InteractableCapture");
			InteractableSimHarness sim = host.AddComponent<InteractableSimHarness>();
			sim.StepInterval = 0.3f;
			yield return WaitSeconds(180f, () => sim.Ready);
			Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");

			ColliderOverlay overlay = host.AddComponent<ColliderOverlay>();
			capture = SimCapture.Create("interactables", new Vector3(0f, 0.5f, 0f), new Vector3(0f, 4.5f, -7f));
			capture.FocusProvider = () => sim.InteractorTransform != null ? sim.InteractorTransform.position + Vector3.up * 0.8f : (Vector3?)null;
			capture.Caption = () =>
				"<b>Interactable sim — CanAct / registry / resolve / range / corpse / rate limit</b>\n" +
				$"attempts {sim.Attempts}  cases covered {sim.CasesCovered}/{sim.CaseCount}  wrong answers {sim.TotalWrong}\n" +
				"<color=#33ff4d>green</color> character colliders  <color=#33d9ff>cyan</color> triggers  <color=#bfbfcc>grey</color> static";
			capture.StartRecording();
			yield return WaitSeconds(14f);
			capture.StopRecording();
			capture.Flush();

			WriteSummary(capture, new Dictionary<string, object>
			{
				["attempts"] = sim.Attempts,
				["casesCovered"] = sim.CasesCovered,
				["caseCount"] = sim.CaseCount,
				["totalWrong"] = sim.TotalWrong,
			});

			Assert.GreaterOrEqual(sim.CasesCovered, sim.CaseCount);
			Assert.AreEqual(0, sim.TotalWrong);
		}
	}
}
