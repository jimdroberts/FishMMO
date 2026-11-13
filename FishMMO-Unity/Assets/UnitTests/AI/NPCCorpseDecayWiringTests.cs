using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins for corpse decay running in the server's Core world instead of on every NPC's own tick.
	/// </summary>
	/// <remarks>
	/// Every NPC used to subscribe to <c>TimeManager.OnTick</c> at spawn for a timer that only a corpse
	/// runs, so each living NPC cost a call per server tick, and every spawn and despawn copied the
	/// tick's whole subscriber list. Decay now starts at death, as a Core entity.
	/// </remarks>
	[TestFixture]
	public class NPCCorpseDecayWiringTests
	{
		private const string NpcSource = "Assets/Scripts/Shared/Implementation/Entity/NPC/NPC.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found at {path}.");
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"NPC.cs must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");
			return source.Substring(start, end - start);
		}

		[Test]
		public void ALivingNPC_DoesNotTick()
		{
			string spawn = MethodBody(ReadSource(NpcSource), "public override void OnStartServer()", "public override void OnStopServer()");
			LogAssert.IsFalse(spawn.Contains("OnTick +="),
				"OnStartServer must not subscribe to the network tick: only a corpse decays");
		}

		[Test]
		public void DeathStartsDecay_InTheCoreWorldFirst()
		{
			string source = ReadSource(NpcSource);
			string despawn = MethodBody(source, "public virtual void Despawn()", "private void BuildCorpseLoot(");
			LogAssert.IsTrue(despawn.Contains("StartCorpseDecay();"), "Despawn must start the corpse's decay");

			string start = MethodBody(source, "private void StartCorpseDecay()", "private void StopCorpseDecay()");
			int core = start.IndexOf("CoreWorldHost.TryGetOrStart(", StringComparison.Ordinal);
			int fallback = start.IndexOf("OnTick += CorpseDecayTick", StringComparison.Ordinal);
			LogAssert.IsTrue(core >= 0, "decay goes to the server's Core world when one can run");
			LogAssert.IsTrue(fallback > core, "the per-NPC tick is only the fallback, after the Core world is tried");
		}

		[Test]
		public void TheCorpseModule_IsInstalled()
		{
			string module = ReadSource("Assets/Scripts/Shared/Implementation/Entity/NPC/NPCCorpseDecay.cs");
			LogAssert.IsTrue(module.Contains("[assembly: GameModule(typeof(FishMMO.Shared.NpcCorpseModule))]"),
				"without its declaration the corpse systems never reach the Core world and every corpse falls back to its own tick");
			string start = MethodBody(ReadSource(NpcSource), "private void StartCorpseDecay()", "private void StopCorpseDecay()");
			LogAssert.IsTrue(start.Contains("Get<CorpseExpirySystem>() != null"),
				"a Core world without the corpse module must not be handed corpses it will never end");
		}

		[Test]
		public void EveryWayOutStopsTheDecay()
		{
			string source = ReadSource(NpcSource);
			LogAssert.IsTrue(MethodBody(source, "public void ReturnToPool()", "private void NotifyCorpseExpired()").Contains("StopCorpseDecay();"),
				"ReturnToPool must end the decay entity before the NPC goes back to its pool");
			LogAssert.IsTrue(MethodBody(source, "public override void ResetState(bool asServer)", "public override void ReadPayload(").Contains("StopCorpseDecay();"),
				"ResetState must end any decay, so a pooled NPC carries no timer into its next life");
		}
	}
}
