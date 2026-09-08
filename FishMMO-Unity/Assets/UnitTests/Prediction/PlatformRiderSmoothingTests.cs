using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Component.Transforming;
using FishNet.Component.Transforming.Beta;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the presentation contract between a rider and a moving-platform deck: both are drawn
	/// through a FishNet tick smoother with the same flat interpolation, so the visible deck and
	/// the visible rider trail their simulation by the same amount.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The report behind this ("the platform shifts you over a bit as it switches direction",
	/// "jittery with a player on it") was not a simulation defect. The server consumes one rider
	/// input per tick and pairs it with an exact tick, and the client replays the platform in
	/// lockstep, so a reversal is predicted correctly. What differed was the PRESENTATION: the
	/// deck's <c>NetworkTickSmoother</c> ran ADAPTIVE interpolation — a visual lag that grows with
	/// ping, most of a metre at 100 ms — while the owner's character was not smoothed at all. The
	/// playable prefabs shipped with the NetworkObject's <c>GraphicalObject</c> unassigned, so
	/// FishNet logged "GraphicalObject is null" and created no <c>PredictionSmoother</c>; the root
	/// stepped at tick rate with the camera locked to it. The deck's lag points along the direction
	/// of travel, so the gap between the visible deck and the rider it carried flipped sign at
	/// every reversal, and a 30 Hz rider on a per-frame-smooth deck sawtoothed one tick of travel
	/// every tick.
	/// </para>
	/// <para>
	/// The fix is parity. Each playable prefab has a <c>Smoothing</c> node between the root and
	/// <c>MeshRoot</c>, assigned as the NetworkObject's <c>GraphicalObject</c>, and its owner
	/// interpolation equals <see cref="InterpolationTicks"/>; every platform deck's smoother is
	/// held to the same flat value. FishNet's <c>TransformTickSmoother</c> smooths only the
	/// controller of a non-forwarded object, so observed players stay on <c>NetworkTransform</c>.
	/// Two halves, each of which silently reintroduces the report if it drifts.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PlatformRiderSmoothingTests
	{
		/// <summary>
		/// Ticks the visual trails the simulation by, for the rider AND for every platform deck.
		/// FishNet's default; two ticks tolerate a tick landing a frame late without the visual
		/// stalling, at the cost of ~66 ms of visual latency on the player's own movement. Lower
		/// it here and both the prefabs and the scene must follow.
		/// </summary>
		public const byte InterpolationTicks = 2;

		private static readonly string[] PlayablePrefabs =
		{
			"Assets/Prefabs/Shared/Entity/PlayableCharacters/Human.prefab",
			"Assets/Prefabs/Shared/Entity/PlayableCharacters/Elf.prefab",
			"Assets/Prefabs/Shared/Entity/PlayableCharacters/Orc.prefab",
		};

		private const string PlatformScript = "Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlatform.cs";
		private const string NetworkObjectScript = "Assets/Plugins/FishNet/Runtime/Object/NetworkObject/NetworkObject.cs";
		private const string TickSmootherScript = "Assets/Plugins/FishNet/Runtime/Generated/Component/TickSmoothing/NetworkTickSmoother.cs";

		/// <summary>
		/// Every playable prefab hands FishNet a graphical object: a direct child of the root at
		/// local identity, under which the mesh root, the camera follow point and the name labels
		/// all live, and no <c>NetworkTickSmoother</c> competes with it.
		/// </summary>
		[Test]
		public void PlayablePrefabs_SmoothTheMeshUnderTheGraphicalObject()
		{
			foreach (string path in PlayablePrefabs)
			{
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				LogAssert.IsNotNull(prefab, $"{path} did not load.");

				NetworkObject nob = prefab.GetComponent<NetworkObject>();
				LogAssert.IsNotNull(nob, $"{path}: no NetworkObject.");
				Transform graphical = nob.GetGraphicalObject();
				LogAssert.IsNotNull(graphical,
					$"{path}: NetworkObject.GraphicalObject is unassigned. FishNet then creates no " +
					"PredictionSmoother and the owner steps at tick rate on a per-frame-smooth deck — " +
					"the jitter and end-of-run shift of the platform report.");
				LogAssert.AreSame(prefab.transform, graphical.parent,
					$"{path}: the graphical object must be a direct child of the character root.");
				LogAssert.IsTrue(graphical.localPosition == Vector3.zero && graphical.localRotation == Quaternion.identity,
					$"{path}: the graphical object must rest at local identity, or the mesh is offset from the collider.");

				KCCController controller = prefab.GetComponent<KCCController>();
				LogAssert.IsNotNull(controller, $"{path}: no KCCController.");
				LogAssert.IsNotNull(controller.MeshRoot, $"{path}: KCCController.MeshRoot unassigned.");
				LogAssert.AreSame(graphical, controller.MeshRoot.parent,
					$"{path}: MeshRoot must be a child of the graphical object, not of the root.");
				LogAssert.IsNotNull(controller.CameraFollowPoint, $"{path}: KCCController.CameraFollowPoint unassigned.");
				LogAssert.IsTrue(controller.CameraFollowPoint.IsChildOf(graphical),
					$"{path}: the camera follow point must descend from the graphical object, or the camera " +
					"keeps stepping at tick rate while the mesh under it is smooth.");

				ICharacter character = prefab.GetComponent<ICharacter>();
				LogAssert.IsNotNull(character, $"{path}: no ICharacter.");
				LogAssert.AreSame(controller.MeshRoot, character.MeshRoot,
					$"{path}: BaseCharacter.MeshRoot and KCCController.MeshRoot disagree.");

				LogAssert.IsNotNull(FindChild(graphical, "NameLabels"),
					$"{path}: NameLabels must descend from the graphical object.");

				LogAssert.IsNull(prefab.GetComponentInChildren<NetworkTickSmoother>(true),
					$"{path}: a NetworkTickSmoother would smooth the same visual twice.");
			}
		}

		/// <summary>
		/// The owner's smoothing is flat and equals the deck's: FishNet's owner path never uses
		/// adaptive interpolation, so <c>_ownerInterpolation</c> is the whole story. Position and
		/// rotation both step at tick rate and both must be smoothed; a teleport must snap.
		/// </summary>
		[Test]
		public void PlayablePrefabs_OwnerInterpolationMatchesTheDeck()
		{
			foreach (string path in PlayablePrefabs)
			{
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				NetworkObject nob = prefab.GetComponent<NetworkObject>();
				UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(nob);

				LogAssert.AreEqual((int)InterpolationTicks, so.FindProperty("_ownerInterpolation").intValue,
					$"{path}: _ownerInterpolation must equal the deck's interpolation ({InterpolationTicks}), " +
					"or the rider and the visible deck trail the simulation by different distances and the " +
					"gap flips at every reversal.");

				int smoothed = so.FindProperty("_ownerSmoothedProperties").intValue;
				const int positionAndRotation = (int)(TransformPropertiesFlag.Position | TransformPropertiesFlag.Rotation);
				LogAssert.IsTrue((smoothed & positionAndRotation) == positionAndRotation,
					$"{path}: owner smoothing must cover position and rotation (has {smoothed}).");

				LogAssert.IsTrue(so.FindProperty("_enableTeleport").boolValue &&
					so.FindProperty("_teleportThreshold").floatValue > 1f,
					$"{path}: a teleport or scene load must snap the visual rather than slide it across the map.");
				LogAssert.IsFalse(so.FindProperty("_detachGraphicalObject").boolValue,
					$"{path}: the graphical object stays nested; the hit reaction and labels rely on its local space.");
			}
		}

		/// <summary>
		/// Every <c>KCCPlatform</c> in every scene has a tick smoother on its deck whose settings
		/// (spectator is the branch a client takes for an ownerless object; controller is checked
		/// too so nothing depends on which branch FishNet picks) use the rider's flat interpolation,
		/// never adaptive.
		/// </summary>
		[Test]
		public void Platforms_SmoothWithTheRidersFlatInterpolation()
		{
			string platformGuid = ScriptGuid(PlatformScript);
			string networkObjectGuid = ScriptGuid(NetworkObjectScript);
			string smootherGuid = ScriptGuid(TickSmootherScript);
			string[] scenes = UnityEditor.AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
			StringBuilder problems = new StringBuilder();
			int platforms = 0;

			foreach (string guid in scenes)
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				string full = Path.Combine(Directory.GetCurrentDirectory(), path);
				if (!File.Exists(full))
				{
					continue;
				}

				Dictionary<string, string> docs = SceneDocuments(full);
				foreach (KeyValuePair<string, string> entry in docs)
				{
					if (!entry.Value.Contains("guid: " + platformGuid))
					{
						continue;
					}
					platforms++;
					string goId = FieldRef(entry.Value, "m_GameObject");
					string nobId = ComponentOfGameObject(docs, goId, networkObjectGuid);
					if (nobId == null)
					{
						problems.Append($"\n  {path}: KCCPlatform &{entry.Key} has no NetworkObject.");
						continue;
					}

					int smoothers = 0;
					foreach (KeyValuePair<string, string> candidate in docs)
					{
						if (!candidate.Value.Contains("guid: " + smootherGuid) ||
							FieldRef(candidate.Value, "_addedNetworkObject") != nobId)
						{
							continue;
						}
						smoothers++;
						CheckSettingsBlock(candidate.Value, "_spectatorMovementSettings", path, candidate.Key, problems);
						CheckSettingsBlock(candidate.Value, "_controllerMovementSettings", path, candidate.Key, problems);
					}
					if (smoothers == 0)
					{
						problems.Append($"\n  {path}: platform NetworkObject &{nobId} has no NetworkTickSmoother; its deck would step at tick rate.");
					}
				}
			}

			TestContext.WriteLine($"MEASURE scene platforms checked for smoothing parity: {platforms}");
			LogAssert.IsTrue(platforms >= 1, "No KCCPlatform was found in any scene; the parity contract is unproven.");
			LogAssert.IsTrue(problems.Length == 0,
				$"Every platform deck must smooth with the rider's flat interpolation " +
				$"({InterpolationTicks} ticks, adaptive off), or the visible deck trails its collider by a " +
				"different distance than the rider does and the offset flips at every reversal. Problems:" + problems);
		}

		private static void CheckSettingsBlock(string doc, string block, string path, string id, StringBuilder problems)
		{
			int at = doc.IndexOf(block + ":", StringComparison.Ordinal);
			if (at < 0)
			{
				problems.Append($"\n  {path}: NetworkTickSmoother &{id} has no {block}.");
				return;
			}
			string tail = doc.Substring(at);
			int adaptive = IntField(tail, "AdaptiveInterpolationValue");
			int interpolation = IntField(tail, "InterpolationValue");
			if (adaptive != (int)AdaptiveInterpolationType.Off)
			{
				problems.Append($"\n  {path}: NetworkTickSmoother &{id} {block}.AdaptiveInterpolationValue is {adaptive}; must be Off (0).");
			}
			if (interpolation != InterpolationTicks)
			{
				problems.Append($"\n  {path}: NetworkTickSmoother &{id} {block}.InterpolationValue is {interpolation}; the rider uses {InterpolationTicks}.");
			}
		}

		private static int IntField(string text, string key)
		{
			Match m = Regex.Match(text, @"(?m)^\s*" + Regex.Escape(key) + @":\s*(-?\d+)\s*$");
			return m.Success ? int.Parse(m.Groups[1].Value) : int.MinValue;
		}

		private static Transform FindChild(Transform parent, string name)
		{
			foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
			{
				if (child != parent && child.name == name)
				{
					return child;
				}
			}
			return null;
		}

		/// <summary>The fileID of the component with the given script guid on a GameObject document, or null.</summary>
		private static string ComponentOfGameObject(Dictionary<string, string> docs, string goId, string scriptGuid)
		{
			if (goId == null || !docs.TryGetValue(goId, out string go))
			{
				return null;
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
					return id;
				}
			}
			return null;
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
					string id = doc.Substring(amp + 1, eol - amp - 1).Trim().Split(' ')[0];
					docs[id] = doc;
				}
				start = next < 0 ? -1 : next + 1;
			}
			return docs;
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
			LogAssert.Fail($"{scriptPath}.meta carries no guid.");
			return null;
		}
	}
}
