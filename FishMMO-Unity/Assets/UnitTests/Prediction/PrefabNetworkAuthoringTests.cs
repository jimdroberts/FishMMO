using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Asserts the on-disk network authoring of every prefab under <c>Assets/Prefabs</c>, read as
	/// YAML rather than through the asset database so the check sees exactly what is committed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>State forwarding is off everywhere.</b> With forwarding on, the server relays the
	/// owner's replicate stream and a reconcile to every observer every tick — around 850 B/s per
	/// observer per object even when the object is idle, and for an ownerless NPC that is pure
	/// waste because there is no input to relay. Observers are fed by broadcasts and the
	/// NetworkTransform instead. The runtime switch that used to flip this was removed, so the
	/// serialized field is the only thing that decides it and it must read 0.
	/// </para>
	/// <para>
	/// <b>NetworkTransforms are server-authoritative and do not send to their owner.</b> The
	/// owner predicts itself; a transform update to it is discarded on receipt. Previously only a
	/// one-shot <c>ConfigureForPrediction</c> at spawn flipped these two fields, and only on the
	/// transform assigned to the NetworkObject's prediction slot — any other NetworkTransform on
	/// the prefab (a platform, a mount) kept whatever was authored. Authoring them on disk makes
	/// the configuration true before any runtime path runs, and lets the send-side owner exclusion
	/// (<c>NetworkBehaviour.ExcludeOwnerFromUnbufferedObserversRpcs</c>) see it.
	/// </para>
	/// <para>
	/// Regex rather than a YAML parser: Unity's prefab YAML is line oriented and the three keys
	/// are unique to the components they belong to (<c>_enableStateForwarding</c> to
	/// NetworkObject, <c>_clientAuthoritative</c> / <c>_sendToOwner</c> to NetworkTransform), so
	/// a key-per-line scan is exact and needs no schema.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PrefabNetworkAuthoringTests
	{
		private static readonly Regex StateForwarding = new Regex(@"^\s*_enableStateForwarding:\s*(\d+)\s*$", RegexOptions.Compiled);
		private static readonly Regex ClientAuthoritative = new Regex(@"^\s*_clientAuthoritative:\s*(\d+)\s*$", RegexOptions.Compiled);
		private static readonly Regex SendToOwner = new Regex(@"^\s*_sendToOwner:\s*(\d+)\s*$", RegexOptions.Compiled);

		private static string PrefabRoot => Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Prefabs");

		private static IEnumerable<string> PrefabFiles()
		{
			LogAssert.IsTrue(Directory.Exists(PrefabRoot), $"Prefab root not found at {PrefabRoot}.");
			return Directory.EnumerateFiles(PrefabRoot, "*.prefab", SearchOption.AllDirectories);
		}

		[Test]
		public void EveryNetworkObjectPrefab_HasStateForwardingOff()
		{
			int networkObjects = 0;
			List<string> offenders = new List<string>();

			foreach (string file in PrefabFiles())
			{
				foreach (string line in File.ReadLines(file))
				{
					Match m = StateForwarding.Match(line);
					if (!m.Success)
					{
						continue;
					}
					networkObjects++;
					if (m.Groups[1].Value != "0")
					{
						offenders.Add(Path.GetRelativePath(PrefabRoot, file));
					}
				}
			}

			TestContext.WriteLine($"MEASURE NetworkObject components scanned across prefabs: {networkObjects}");
			LogAssert.IsTrue(networkObjects > 0, "No NetworkObject was found in any prefab; this guard is checking nothing.");
			LogAssert.AreEqual(0, offenders.Count,
				"These prefabs still forward state, which relays a replicate and a reconcile to every " +
				"observer every tick (and for an ownerless NPC relays nothing useful at all): " +
				string.Join(", ", offenders));
		}

		[Test]
		public void EveryNetworkTransformOnANetworkObjectPrefab_IsServerAuthoritative_AndSkipsTheOwner()
		{
			int transforms = 0;
			List<string> clientAuthoritative = new List<string>();
			List<string> sendToOwner = new List<string>();

			foreach (string file in PrefabFiles())
			{
				string[] lines = File.ReadAllLines(file);
				bool hasNetworkObject = false;
				foreach (string line in lines)
				{
					if (StateForwarding.IsMatch(line))
					{
						hasNetworkObject = true;
						break;
					}
				}
				if (!hasNetworkObject)
				{
					continue;
				}

				string name = Path.GetRelativePath(PrefabRoot, file);
				foreach (string line in lines)
				{
					Match ca = ClientAuthoritative.Match(line);
					if (ca.Success)
					{
						transforms++;
						if (ca.Groups[1].Value != "0")
						{
							clientAuthoritative.Add(name);
						}
						continue;
					}
					Match so = SendToOwner.Match(line);
					if (so.Success && so.Groups[1].Value != "0")
					{
						sendToOwner.Add(name);
					}
				}
			}

			TestContext.WriteLine($"MEASURE NetworkTransform components scanned on NetworkObject prefabs: {transforms}");
			LogAssert.IsTrue(transforms > 0, "No NetworkTransform was found on any NetworkObject prefab; this guard is checking nothing.");
			LogAssert.AreEqual(0, clientAuthoritative.Count,
				"These prefabs author a client-authoritative NetworkTransform; the server is authoritative " +
				"for every predicted object and a client-authoritative transform lets the client dictate " +
				"position: " + string.Join(", ", clientAuthoritative));
			LogAssert.AreEqual(0, sendToOwner.Count,
				"These prefabs author a NetworkTransform that sends to its owner; the owner predicts itself " +
				"and discards the update, so every one of those packets is waste: " +
				string.Join(", ", sendToOwner));
		}

		[Test]
		public void PredictionModeController_IsGone()
		{
			/* The runtime interpolated/forwarded switch was deleted: it lived on no prefab, it
			 * could not actually stop a NetworkTransform (the component sends from a TimeManager
			 * subscription and receives through an RPC, neither of which checks `enabled`), and the
			 * project policy is that forwarding stays off. Anything reviving it should do so
			 * deliberately, not by leaving the old file around. */
			string path = Path.Combine(Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/PredictionModeController.cs");
			LogAssert.IsTrue(!File.Exists(path),
				"PredictionModeController.cs is back. State forwarding stays off everywhere; see " +
				"NetworkObject.SetStateForwarding's FISHMMO EDIT comment before reintroducing a switch.");
		}
	}
}
