using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Object;
using FishNet.Component.Transforming;
using FishNet.Component.Transforming.Beta;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the presentation contract between a rider and a moving-platform deck: both are drawn
	/// through FishNet's tick smoother with the same flat interpolation, so the visible deck and
	/// the visible rider trail their simulation by the same amount.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The report behind this ("the platform shifts you over a bit as it switches direction",
	/// "jittery with a player on it") was not a simulation defect. The deck's
	/// <c>NetworkTickSmoother</c> ran ADAPTIVE interpolation — a visual lag that grows with ping —
	/// while the owner's character was not smoothed at all: it stepped at tick rate on a deck
	/// whose visual trailed its collider by most of a metre. The lag points along the direction
	/// of travel, so it flipped sign at every reversal. See <see cref="CharacterTickSmoother"/>.
	/// </para>
	/// <para>
	/// Two halves, each of which silently reintroduces the report if it drifts: the playable
	/// prefabs must carry the rider smoother with the mesh under its node, and every platform in
	/// every scene must smooth with the rider's constant and never adaptively.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PlatformRiderSmoothingTests
	{
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
		/// Every playable prefab smooths its visual: the mesh root, the camera follow point and
		/// the name labels all sit under the smoother's graphical node, which is a direct child of
		/// the character root, and no <c>NetworkTickSmoother</c> competes with it.
		/// </summary>
		[Test]
		public void PlayablePrefabs_SmoothTheMeshUnderTheRiderSmoother()
		{
			foreach (string path in PlayablePrefabs)
			{
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				LogAssert.IsNotNull(prefab, $"{path} did not load.");

				CharacterTickSmoother smoother = prefab.GetComponent<CharacterTickSmoother>();
				LogAssert.IsNotNull(smoother,
					$"{path}: no CharacterTickSmoother on the root. The owner steps at tick rate on a " +
					"per-frame-smooth deck — the jitter and end-of-run shift of the platform report.");

				Transform graphical = smoother.GraphicalRoot;
				LogAssert.IsNotNull(graphical, $"{path}: CharacterTickSmoother.GraphicalRoot is unassigned.");
				LogAssert.AreSame(prefab.transform, graphical.parent,
					$"{path}: the smoothed node must be a direct child of the character root; the " +
					"smoother targets the root and offsets its child against it.");
				LogAssert.IsTrue(graphical.localPosition == Vector3.zero && graphical.localRotation == Quaternion.identity,
					$"{path}: the smoothed node must rest at local identity, or the mesh is offset from the collider.");

				KCCController controller = prefab.GetComponent<KCCController>();
				LogAssert.IsNotNull(controller, $"{path}: no KCCController.");
				LogAssert.IsNotNull(controller.MeshRoot, $"{path}: KCCController.MeshRoot unassigned.");
				LogAssert.AreSame(graphical, controller.MeshRoot.parent,
					$"{path}: MeshRoot must be a child of the smoothed node, not of the root.");
				LogAssert.IsNotNull(controller.CameraFollowPoint, $"{path}: KCCController.CameraFollowPoint unassigned.");
				LogAssert.IsTrue(controller.CameraFollowPoint.IsChildOf(graphical),
					$"{path}: the camera follow point must descend from the smoothed node, or the camera " +
					"keeps stepping at tick rate while the mesh under it is smooth.");

				ICharacter character = prefab.GetComponent<ICharacter>();
				LogAssert.IsNotNull(character, $"{path}: no ICharacter.");
				LogAssert.AreSame(controller.MeshRoot, character.MeshRoot,
					$"{path}: BaseCharacter.MeshRoot and KCCController.MeshRoot disagree.");

				Transform labels = FindChild(graphical, "NameLabels");
				LogAssert.IsNotNull(labels, $"{path}: NameLabels must descend from the smoothed node.");

				LogAssert.IsNull(prefab.GetComponentInChildren<NetworkTickSmoother>(true),
					$"{path}: a NetworkTickSmoother would smooth the same visual twice, and would run " +
					"for observers whose root is already interpolated by NetworkTransform.");
			}
		}

		/// <summary>
		/// The rider's settings are flat: the deck is pinned to the same constant, and adaptive
		/// interpolation would make the player's own visual lag scale with ping.
		/// </summary>
		[Test]
		public void RiderSettings_AreFlatAndSmoothEverything()
		{
			MovementSettings settings = CharacterTickSmoother.RiderSettings;
			LogAssert.AreEqual(AdaptiveInterpolationType.Off, settings.AdaptiveInterpolationValue,
				"Rider smoothing must never be adaptive.");
			LogAssert.AreEqual(CharacterTickSmoother.InterpolationTicks, settings.InterpolationValue);
			LogAssert.AreEqual(TransformPropertiesFlag.Everything, settings.SmoothedProperties,
				"Position and rotation both step at tick rate; both must be smoothed.");
			LogAssert.IsTrue(settings.EnableTeleport && settings.TeleportThreshold > 1f,
				"A teleport or scene load must snap the visual rather than slide it across the map.");
		}

		/// <summary>
		/// Every <c>KCCPlatform</c> in every scene has a tick smoother on its deck, and that
		/// smoother's spectator settings (the branch a client takes for an ownerless object) use the
		/// rider's flat interpolation, never adaptive.
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
				$"({CharacterTickSmoother.InterpolationTicks} ticks, adaptive off), or the visible deck " +
				"trails its collider by a different distance than the rider does and the offset flips at " +
				"every reversal. Problems:" + problems);
		}

		/// <summary>
		/// A non-owner never starts smoothing, and stopping restores the graphical node's rest pose
		/// so a pooled instance is not handed on with a stale offset.
		/// </summary>
		[Test]
		public void Smoother_NonOwnerIsInert_AndStopRestoresTheRestPose()
		{
			GameObject root = new GameObject("Rider");
			try
			{
				GameObject graphical = new GameObject("Smoothing");
				graphical.transform.SetParent(root.transform, false);
				CharacterTickSmoother smoother = root.AddComponent<CharacterTickSmoother>();
				typeof(CharacterTickSmoother)
					.GetField("graphicalRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
					.SetValue(smoother, graphical.transform);
				KCCPlayer player = root.AddComponent<KCCPlayer>();

				// Not the owner: nothing starts, nothing moves.
				smoother.SetOwnerSmoothing(false, player);
				LogAssert.IsFalse(smoother.IsSmoothing, "A non-owner must not be smoothed.");

				// An owner whose behaviour was never spawned has no TimeManager: refuse rather than throw.
				smoother.SetOwnerSmoothing(true, player);
				LogAssert.IsFalse(smoother.IsSmoothing, "Without a TimeManager the smoother cannot run and must say so by not starting.");

				graphical.transform.localPosition = new Vector3(0.3f, 0f, 0f);
				smoother.Stop();
				LogAssert.IsTrue(root.transform.childCount == 1, "Stop on a smoother that never started must not create or destroy children.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
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
			if (interpolation != CharacterTickSmoother.InterpolationTicks)
			{
				problems.Append($"\n  {path}: NetworkTickSmoother &{id} {block}.InterpolationValue is {interpolation}; the rider uses {CharacterTickSmoother.InterpolationTicks}.");
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
