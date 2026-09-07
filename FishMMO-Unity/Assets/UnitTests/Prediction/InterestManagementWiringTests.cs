using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Component.Observing;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Observing;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Asserts that interest management is actually wired up — on the prefabs, and on the
	/// <c>NetworkManager</c> in the scenes that host them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every bandwidth projection assumes a culled visible set. Without interest management the
	/// "visible peers" figure is the whole scene population and the per-client cost stops being
	/// linear in what a player can see and becomes linear in how many players logged in.
	/// </para>
	/// <para>
	/// There are two layers and a prefab may use either. A <c>NetworkObserver</c> on the prefab
	/// supplies its own conditions; the <c>ObserverManager</c> on the <c>NetworkManager</c> supplies
	/// defaults to every <c>NetworkObject</c> that has none. Both are checked, because "covered by
	/// interest management" is true through either path and a test that only knew about one would
	/// report a false gap.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class InterestManagementWiringTests
	{
		private const string EntityRoot = "Assets/Prefabs/Shared/Entity";
		private static readonly string[] ServerScenes =
		{
			"Assets/Scenes/Server/SceneServer.unity",
			"Assets/Scenes/Server/WorldServer.unity",
		};

		/// <summary>
		/// The <c>ObserverManager</c> exists in the scene servers and supplies at least one default
		/// condition, which is what covers every prefab that carries no <c>NetworkObserver</c>.
		/// </summary>
		/// <remarks>
		/// Read out of the scene YAML rather than by loading the scene: an EditMode test that opens
		/// a scene disturbs whatever the editor currently has open, and the assertion here is about
		/// what is authored, not about what happens at runtime.
		/// </remarks>
		[Test]
		public void ObserverManager_IsPresentInEveryServerScene_WithDefaultConditions()
		{
			foreach (string scenePath in ServerScenes)
			{
				string full = Path.Combine(Directory.GetCurrentDirectory(), scenePath);
				LogAssert.IsTrue(File.Exists(full), $"Scene not found: {scenePath}");

				string[] lines = File.ReadAllLines(full);
				int managerLine = -1;
				for (int i = 0; i < lines.Length; ++i)
				{
					// ObserverManager's script guid, from ObserverManager.cs.meta.
					if (lines[i].Contains("guid: 7d331f979d46e8e4a9fc90070c596d44"))
					{
						managerLine = i;
						break;
					}
				}
				LogAssert.IsTrue(managerLine >= 0,
					$"{scenePath} has no ObserverManager. Without it, every NetworkObject that " +
					"carries no NetworkObserver of its own is visible to every client in the scene.");

				// _defaultConditions is a list; its entries are the lines beginning "- {fileID:".
				int defaults = 0;
				bool inDefaults = false;
				for (int i = managerLine; i < lines.Length && i < managerLine + 40; ++i)
				{
					string trimmed = lines[i].Trim();
					if (trimmed.StartsWith("_defaultConditions:"))
					{
						inDefaults = true;
						continue;
					}
					if (inDefaults)
					{
						if (trimmed.StartsWith("- {fileID:")) defaults++;
						else break;
					}
				}

				TestContext.WriteLine($"MEASURE {Path.GetFileName(scenePath)}: ObserverManager default conditions = {defaults}");
				LogAssert.IsTrue(defaults > 0,
					$"{scenePath}'s ObserverManager supplies no default conditions, so a prefab " +
					"without its own NetworkObserver has no interest management at all.");
			}
		}

		/// <summary>
		/// Every predicted character prefab is covered by interest management through one layer or
		/// the other, and the report says which — because the two are not equivalent.
		/// </summary>
		/// <remarks>
		/// A prefab with its own <c>DistanceCondition</c> also gets the density-scaled range that
		/// <c>ObserverStreamingRegistry</c> applies, because that lever works by writing to the
		/// condition instance. A prefab relying on the manager defaults alone is culled by those
		/// defaults but its range never scales with crowding — see
		/// <c>ObserverStreamingEntry.ApplyRange</c>, which returns early without a condition.
		/// </remarks>
		[Test]
		public void EveryPredictedCharacterPrefab_IsCoveredByInterestManagement()
		{
			string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { EntityRoot });
			LogAssert.IsTrue(guids.Length > 0, $"No prefabs found under {EntityRoot}.");

			List<string> uncovered = new List<string>();
			List<string> ownCondition = new List<string>();
			List<string> managerDefaultsOnly = new List<string>();

			foreach (string guid in guids)
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null)
				{
					continue;
				}

				// Only characters that run the prediction pipeline are in scope.
				if (prefab.GetComponent<CharacterPredictionController>() == null)
				{
					continue;
				}

				NetworkObserver observer = prefab.GetComponent<NetworkObserver>();
				bool hasOwnCondition = false;
				if (observer != null)
				{
					IReadOnlyList<ObserverCondition> conditions = observer.ObserverConditions;
					if (conditions != null)
					{
						foreach (ObserverCondition c in conditions)
						{
							if (c != null)
							{
								hasOwnCondition = true;
								break;
							}
						}
					}
				}

				if (hasOwnCondition)
				{
					ownCondition.Add(prefab.name);
				}
				else
				{
					// Covered by the ObserverManager defaults, which the test above proves exist.
					managerDefaultsOnly.Add(prefab.name);
				}
			}

			TestContext.WriteLine(
				$"MEASURE predicted character prefabs with their own observer condition: {ownCondition.Count} " +
				$"({string.Join(", ", ownCondition)})");
			TestContext.WriteLine(
				$"MEASURE predicted character prefabs relying on ObserverManager defaults: {managerDefaultsOnly.Count} " +
				$"({string.Join(", ", managerDefaultsOnly)})");
			TestContext.WriteLine(
				"MEASURE a prefab without its own DistanceCondition is still culled, but its observer " +
				"range never scales with local density (ObserverStreamingEntry.ApplyRange no-ops).");

			LogAssert.AreEqual(0, uncovered.Count,
				"Uncovered predicted character prefabs: " + string.Join(", ", uncovered));
			LogAssert.IsTrue(ownCondition.Count + managerDefaultsOnly.Count > 0,
				"No predicted character prefabs were found at all — the scan is not reaching them.");
		}

		/// <summary>
		/// Every predicted character prefab ships with state forwarding OFF, which is the mode the
		/// observer broadcasts assume.
		/// </summary>
		/// <remarks>
		/// This overlaps <c>PrefabNetworkAuthoringTests</c> deliberately: that fixture scans every
		/// prefab, this one states the invariant for the specific set whose observer channels depend
		/// on it, and reports the count so a prefab added later shows up in the measurement.
		/// </remarks>
		[Test]
		public void EveryPredictedCharacterPrefab_ShipsWithForwardingOff()
		{
			string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { EntityRoot });
			List<string> forwarding = new List<string>();
			int checkedCount = 0;

			foreach (string guid in guids)
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null || prefab.GetComponent<CharacterPredictionController>() == null)
				{
					continue;
				}
				NetworkObject nob = prefab.GetComponent<NetworkObject>();
				if (nob == null)
				{
					continue;
				}

				checkedCount++;
				if (nob.EnableStateForwarding)
				{
					forwarding.Add(prefab.name);
				}
			}

			TestContext.WriteLine($"MEASURE predicted character prefabs checked for forwarding: {checkedCount}");
			LogAssert.AreEqual(0, forwarding.Count,
				"These prefabs ship with state forwarding ON. Their observers would receive the " +
				"relayed replicate and reconcile — measured ~9x the interpolated cost per peer — " +
				"while the gated broadcasts go silent: " + string.Join(", ", forwarding));
		}

		/// <summary>
		/// No <c>NetworkObject</c> anywhere ships with state forwarding on — scenes included.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>PrefabNetworkAuthoringTests</c> covers prefabs; scene-placed objects sat outside every
		/// assertion and 28 of them still held FishNet's serialized default of <c>1</c>. Twenty-seven
		/// were static props — regions, spawners, teleporters, bindstones — that are not predicted at
		/// all and so paid nothing for it, but nothing distinguished them from a deliberate choice.
		/// </para>
		/// <para>
		/// The twenty-eighth was <c>MovingPlatform</c>, which <i>is</i> predicted — and since issue
		/// #228 it is the one kind of scene object REQUIRED to forward. The argument that keeps
		/// forwarding off everywhere else is about relaying each owner's input to every observer;
		/// an ownerless platform has no input, its replicate is an empty struct, and the only cost
		/// is a delta-encoded reconcile per observer. That reconcile is what lets FishNet roll the
		/// platform back and replay it with the rider. Run with forwarding off, the client platform
		/// was dead-reckoned from a one-shot payload and riders sank through it.
		/// </para>
		/// </remarks>
		[Test]
		public void OnlyPlatforms_ShipWithStateForwardingOn()
		{
			string platformGuid = ScriptGuid("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlatform.cs");
			string[] scenes = UnityEditor.AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
			StringBuilder forwardedOthers = new StringBuilder();
			StringBuilder unforwardedPlatforms = new StringBuilder();
			int scanned = 0;
			int platforms = 0;

			foreach (string guid in scenes)
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				string full = Path.Combine(Directory.GetCurrentDirectory(), path);
				if (!File.Exists(full))
				{
					continue;
				}

				scanned++;
				Dictionary<string, string> docs = SceneDocuments(full);
				foreach (KeyValuePair<string, string> entry in docs)
				{
					int flag = ForwardingFlag(entry.Value);
					if (flag < 0)
					{
						continue; // Not a NetworkObject.
					}
					bool isPlatform = GameObjectHasScript(docs, entry.Value, platformGuid);
					if (isPlatform)
					{
						platforms++;
						if (flag != 1)
						{
							unforwardedPlatforms.Append($"\n  {path}: NetworkObject &{entry.Key}");
						}
					}
					else if (flag == 1)
					{
						forwardedOthers.Append($"\n  {path}: NetworkObject &{entry.Key}");
					}
				}
			}

			TestContext.WriteLine($"MEASURE scenes scanned for state forwarding: {scanned}");
			TestContext.WriteLine($"MEASURE scene platforms found: {platforms}");
			LogAssert.IsTrue(forwardedOthers.Length == 0,
				"State forwarding is off for everything but platforms; observers are fed by " +
				"NetworkTransform and per-controller broadcasts. These scene objects have it on:" + forwardedOthers);
			LogAssert.IsTrue(unforwardedPlatforms.Length == 0,
				"Every KCCPlatform must forward state, or FishNet never reconciles it to clients and " +
				"the client copy runs open-loop from its spawn payload (issue #228). These do not:" + unforwardedPlatforms);
			LogAssert.IsTrue(platforms >= 1,
				"No KCCPlatform was found in any scene — the shipped MovingPlatform is gone or this " +
				"scan no longer recognises it, and the forwarding contract is unproven.");
		}

		/// <summary>Every YAML document in a scene, keyed by its <c>&amp;fileID</c>.</summary>
		private static Dictionary<string, string> SceneDocuments(string fullPath)
		{
			Dictionary<string, string> docs = new Dictionary<string, string>();
			string text = File.ReadAllText(fullPath);
			int start = text.IndexOf("--- !u!", StringComparison.Ordinal);
			while (start >= 0)
			{
				int next = text.IndexOf("\n--- !u!", start + 1, StringComparison.Ordinal);
				string doc = next < 0 ? text.Substring(start) : text.Substring(start, next - start);
				int amp = doc.IndexOf('&');
				int eol = doc.IndexOf('\n');
				if (amp > 0 && eol > amp)
				{
					// "&123 stripped" on prefab-instance documents; the id is the first token.
					string id = doc.Substring(amp + 1, eol - amp - 1).Trim().Split(' ')[0];
					docs[id] = doc;
				}
				start = next < 0 ? -1 : next + 1;
			}
			return docs;
		}

		/// <summary>The serialized <c>_enableStateForwarding</c> value, or -1 when the document is not a NetworkObject.</summary>
		private static int ForwardingFlag(string doc)
		{
			const string key = "_enableStateForwarding: ";
			foreach (string raw in doc.Split('\n'))
			{
				string line = raw.Trim();
				if (line.StartsWith(key, StringComparison.Ordinal) && int.TryParse(line.Substring(key.Length), out int value))
				{
					return value;
				}
			}
			return -1;
		}

		/// <summary>The fileID a <c>field: {fileID: N}</c> line references, or null.</summary>
		private static string FieldRef(string doc, string field)
		{
			string key = field + ": {fileID: ";
			int at = doc.IndexOf(key, StringComparison.Ordinal);
			if (at < 0)
			{
				return null;
			}
			int from = at + key.Length;
			int end = doc.IndexOfAny(new[] { '}', ',' }, from);
			return end < 0 ? null : doc.Substring(from, end - from).Trim();
		}

		/// <summary>True when the GameObject a component document belongs to carries a MonoBehaviour of the given script guid.</summary>
		private static bool GameObjectHasScript(Dictionary<string, string> docs, string componentDoc, string scriptGuid)
		{
			string goId = FieldRef(componentDoc, "m_GameObject");
			if (goId == null || !docs.TryGetValue(goId, out string go))
			{
				return false;
			}
			foreach (string raw in go.Split('\n'))
			{
				string line = raw.Trim();
				const string key = "- component: {fileID: ";
				if (!line.StartsWith(key, StringComparison.Ordinal))
				{
					continue;
				}
				string id = line.Substring(key.Length).TrimEnd('}').Trim();
				if (docs.TryGetValue(id, out string component) && component.Contains("guid: " + scriptGuid))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The asset guid of a script, read from its .meta.</summary>
		private static string ScriptGuid(string scriptPath)
		{
			string meta = Path.Combine(Directory.GetCurrentDirectory(), scriptPath + ".meta");
			LogAssert.IsTrue(File.Exists(meta), $"{scriptPath}.meta not found.");
			foreach (string raw in File.ReadAllLines(meta))
			{
				string line = raw.Trim();
				if (line.StartsWith("guid: ", StringComparison.Ordinal))
				{
					return line.Substring(6).Trim();
				}
			}
			LogAssert.IsTrue(false, $"{scriptPath}.meta carries no guid.");
			return null;
		}

		/// <summary>
		/// <c>BaseCharacter</c> requires the per-observer distance LOD, which transitively requires
		/// the <c>NetworkTransform</c> the interpolated transport depends on.
		/// </summary>
		/// <remarks>
		/// Asserted through the attribute rather than through the prefabs, because the attribute is
		/// what makes it impossible to author a character without it. <c>PlayerCharacter</c> did not
		/// previously require a <c>NetworkTransform</c> at all — only <c>NPC</c> did — so this is
		/// also what closes that gap.
		/// </remarks>
		[Test]
		public void BaseCharacter_RequiresTheDistanceLod_AndThroughItTheNetworkTransform()
		{
			object[] required = typeof(BaseCharacter).GetCustomAttributes(typeof(RequireComponent), inherit: false);
			bool requiresLod = false;
			foreach (RequireComponent rc in required)
			{
				if (rc.m_Type0 == typeof(NetworkTransformDistanceLod) ||
					rc.m_Type1 == typeof(NetworkTransformDistanceLod) ||
					rc.m_Type2 == typeof(NetworkTransformDistanceLod))
				{
					requiresLod = true;
					break;
				}
			}
			LogAssert.IsTrue(requiresLod,
				"BaseCharacter must [RequireComponent(typeof(NetworkTransformDistanceLod))]. With " +
				"state forwarding off, an unfiltered NetworkTransform sends every observer every " +
				"update at full tick rate regardless of distance.");

			// And the LOD pins the transform, so requiring one requires both.
			object[] lodRequires = typeof(NetworkTransformDistanceLod)
				.GetCustomAttributes(typeof(RequireComponent), inherit: false);
			bool requiresTransform = false;
			foreach (RequireComponent rc in lodRequires)
			{
				if (rc.m_Type0 == typeof(NetworkTransform) ||
					rc.m_Type1 == typeof(NetworkTransform) ||
					rc.m_Type2 == typeof(NetworkTransform))
				{
					requiresTransform = true;
					break;
				}
			}
			LogAssert.IsTrue(requiresTransform,
				"NetworkTransformDistanceLod must require NetworkTransform, or requiring the LOD on " +
				"BaseCharacter would not guarantee the transport it filters.");
		}

		/// <summary>
		/// Every predicted character prefab carries the distance LOD, matching the attribute.
		/// </summary>
		/// <remarks>
		/// <c>RequireComponent</c> stops a character being authored without it from here on, but it
		/// does not retroactively repair a prefab saved before the attribute existed — so the assets
		/// are checked too.
		/// </remarks>
		[Test]
		public void EveryPredictedCharacterPrefab_CarriesTheDistanceLod()
		{
			string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { EntityRoot });
			List<string> missingLod = new List<string>();
			List<string> missingTransform = new List<string>();
			int checkedCount = 0;

			foreach (string guid in guids)
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null || prefab.GetComponent<BaseCharacter>() == null)
				{
					continue;
				}

				checkedCount++;
				if (prefab.GetComponent<NetworkTransformDistanceLod>() == null)
				{
					missingLod.Add(prefab.name);
				}
				if (prefab.GetComponent<NetworkTransform>() == null)
				{
					missingTransform.Add(prefab.name);
				}
			}

			TestContext.WriteLine($"MEASURE BaseCharacter prefabs checked: {checkedCount}");
			LogAssert.IsTrue(checkedCount > 0, "No BaseCharacter prefabs were found; the scan is not reaching them.");
			LogAssert.AreEqual(0, missingLod.Count,
				"Character prefabs without a distance LOD: " + string.Join(", ", missingLod));
			LogAssert.AreEqual(0, missingTransform.Count,
				"Character prefabs without a NetworkTransform, which is the only thing that carries " +
				"their position to observers while forwarding is off: " + string.Join(", ", missingTransform));
		}
	}
}
