using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards the NPC interaction chain end to end, prompted by a live report of "can't interact
	/// with NPCs" whose cause was invisible in every log.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The chain crosses two machines and half a dozen silent early-outs: hover raycast →
	/// <c>InteractableResolver</c> → <c>CanInteract</c> (corpse + range) → rate limit → broadcast →
	/// server scene-object resolution → <c>CharacterStateValidation.CanAct</c> → server-side
	/// resolve/CanInteract/rate-limit again. Any link failing produced "pressing E does nothing"
	/// with not one line to bisect with. These tests pin the offline-verifiable links against the
	/// REAL prefabs, and pin in source that every remaining link now says why it refused.
	/// </para>
	/// <para>
	/// The prefab tests deliberately run against the shipped assets rather than synthetic
	/// GameObjects: the failure being guarded is a CONTRACT drift between prefabs (the player's
	/// targeting mask versus the layer NPCs actually sit on), which synthetic fixtures cannot see.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class InteractionRegressionTests
	{
		private const string HumanPrefabPath = "Assets/Prefabs/Shared/Entity/PlayableCharacters/Human.prefab";
		private const string BankerPrefabPath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab";

		private readonly List<GameObject> spawned = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < spawned.Count; ++i)
			{
				if (spawned[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(spawned[i]);
				}
			}
			spawned.Clear();
		}

		// ── The cross-prefab raycast contract ────────────────────────────────────────

		/// <summary>
		/// The player's hover raycast can actually HIT an interactable NPC: the layer the NPC
		/// prefab sits on is inside the player's authored <c>TargetController.LayerMask</c>, and
		/// the NPC carries a collider for the ray to strike.
		/// </summary>
		/// <remarks>
		/// This is the contract that fails silently and completely: a mask edit on the player
		/// prefab, or an NPC prefab moved to a new layer, kills hovering — and with it targeting,
		/// interaction and inspection — with zero errors anywhere, because a raycast that hits
		/// nothing is not a fault to any code on the path.
		/// </remarks>
		[Test]
		public void PlayerTargetMask_SeesTheInteractableNpcLayer()
		{
#if UNITY_EDITOR
			GameObject humanPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(HumanPrefabPath);
			LogAssert.IsNotNull(humanPrefab, $"{HumanPrefabPath} must exist.");
			GameObject bankerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BankerPrefabPath);
			LogAssert.IsNotNull(bankerPrefab, $"{BankerPrefabPath} must exist.");

			TargetController targetController = humanPrefab.GetComponent<TargetController>();
			LogAssert.IsNotNull(targetController, "The player prefab must carry a TargetController.");

			int npcLayer = bankerPrefab.layer;
			LogAssert.IsTrue((targetController.LayerMask.value & (1 << npcLayer)) != 0,
				$"The player's targeting mask must include layer {npcLayer} ('{LayerMask.LayerToName(npcLayer)}'), " +
				"or hovering an interactable NPC resolves nothing and every interaction dies silently.");

			LogAssert.IsNotNull(bankerPrefab.GetComponentInChildren<Collider>(true),
				"The NPC must carry a collider for the hover ray to strike.");
#endif
		}

		// ── Resolver rules against the real prefab ───────────────────────────────────

		/// <summary>
		/// The banker prefab resolves to a usable interactable while alive, and refuses while its
		/// client-side dead flag is set — the corpse rule the resolver and <c>CanInteract</c> share.
		/// </summary>
		/// <remarks>
		/// Instantiated under an INACTIVE parent so no Awake runs: the assertions are about
		/// component composition and flag logic, not about a live character's lifecycle.
		/// </remarks>
		[Test]
		public void BankerPrefab_ResolvesAliveAndRefusesAsCorpse()
		{
#if UNITY_EDITOR
			GameObject bankerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BankerPrefabPath);
			LogAssert.IsNotNull(bankerPrefab, $"{BankerPrefabPath} must exist.");

			GameObject inactiveParent = new GameObject("inactiveHost");
			spawned.Add(inactiveParent);
			inactiveParent.SetActive(false);
			GameObject banker = UnityEngine.Object.Instantiate(bankerPrefab, inactiveParent.transform);

			IInteractable resolved = InteractableResolver.Resolve(banker);
			LogAssert.IsNotNull(resolved,
				"A living interactable NPC must resolve to an interactable — this is the client's whole entry point.");
			LogAssert.IsFalse(resolved is ILootableCorpse,
				"And it must resolve to the SERVICE (banker/dialogue), never to its own corpse component, while alive.");

			ICharacter character = banker.GetComponent<ICharacter>();
			LogAssert.IsNotNull(character, "The NPC prefab must carry a character.");
			LogAssert.IsFalse(InteractableResolver.IsCorpse(banker),
				"A fresh instance presents alive.");

			/* The client derives corpse state from the replicated dead flag (see NPC.IsCorpse).
			 * A stuck IsDead — a mispatched death broadcast, or pooling that failed to clear
			 * flags — turns every affected NPC into an untouchable corpse, which is exactly the
			 * "can't interact with npcs" symptom. */
			character.EnableFlags(CharacterFlags.IsDead);
			LogAssert.IsTrue(InteractableResolver.IsCorpse(banker),
				"The dead flag must present as a corpse on the client.");
			IInteractable resolvedDead = InteractableResolver.Resolve(banker);
			LogAssert.IsTrue(resolvedDead is ILootableCorpse,
				"A dead NPC resolves to its corpse — the loot, not the shop.");

			character.DisableFlags(CharacterFlags.IsDead);
			LogAssert.IsFalse(InteractableResolver.IsCorpse(banker),
				"Clearing the flag restores the living NPC — pooling relies on exactly this reset.");
#endif
		}

		// ── The lifecycle invariant behind the pooled-respawn unlatch ────────────────

		/// <summary>
		/// Every client broadcast a behaviour registers in <c>OnStartCharacter</c> is unregistered
		/// in <c>OnStopCharacter</c> in the same file.
		/// </summary>
		/// <remarks>
		/// <c>PlayerCharacter.ResetState</c> now unlatches local-client initialization so a pooled
		/// character re-runs <c>OnStartCharacter</c> on its next spawn — the fix for the owner's UI
		/// coming back inert. That re-run is only sound if the previous spawn's registrations were
		/// torn down; an asymmetric behaviour double-registers its handlers on every respawn, and
		/// each stale delegate fires alongside the live one forever after. This sweep pins the
		/// symmetry across every behaviour that registers anything.
		/// </remarks>
		[Test]
		public void CharacterBehaviours_RegisterAndUnregisterSymmetrically()
		{
			string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Shared/Implementation/Entity");
			LogAssert.IsTrue(Directory.Exists(root), "Entity implementation folder must exist.");

			/* Scoped to the OnStartCharacter METHOD BODY, not the file. The same files also hold
			 * the process-wide once-only static registrations (RegisterObservedSlotBroadcast and
			 * friends), which are latch-guarded and deliberately never unregistered — a file-level
			 * sweep flags those as leaks. Only what OnStartCharacter itself registers re-runs on a
			 * pooled respawn, so only that is held to the symmetry rule. */
			Regex registerPattern = new Regex(@"ClientManager\.RegisterBroadcast<(\w+)>", RegexOptions.Compiled);

			int checkedFiles = 0;
			foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
			{
				string source = File.ReadAllText(file);
				if (!source.Contains("OnStartCharacter"))
				{
					continue;
				}

				string startBody = ExtractMethodBody(source, "void OnStartCharacter()");
				if (startBody == null || !startBody.Contains("ClientManager.RegisterBroadcast<"))
				{
					continue;
				}

				checkedFiles++;
				foreach (Match match in registerPattern.Matches(startBody))
				{
					string broadcast = match.Groups[1].Value;
					LogAssert.IsTrue(source.Contains($"ClientManager.UnregisterBroadcast<{broadcast}>"),
						$"{Path.GetFileName(file)}'s OnStartCharacter registers {broadcast} but the file never " +
						"unregisters it. A pooled character re-runs OnStartCharacter on its next spawn, so this " +
						"handler stacks a new registration per respawn.");
				}
			}

			LogAssert.IsTrue(checkedFiles > 0,
				"The sweep must find at least one registering behaviour, or the pattern has drifted and this test is vacuous.");
		}

		// ── The instrumentation that makes the next report diagnosable ───────────────

		/// <summary>
		/// Every silent refusal on the interaction path now says why, on both machines.
		/// </summary>
		/// <remarks>
		/// SOURCE assertions — the refusals themselves need live network state. The value being
		/// pinned is diagnosability: "can't interact with NPCs" has at least ten distinct causes
		/// across two machines, and with these lines a single repro at Debug log level names the
		/// failing link outright.
		/// </remarks>
		[Test]
		public void InteractionRefusals_AreAllLogged()
		{
			string client = ReadSource("Assets/Scripts/Client/Input/PlayerInputController.cs");
			LogAssert.IsTrue(client.Contains("Refused: no hover target under the cursor."),
				"The client must say when there was nothing under the cursor.");
			LogAssert.IsTrue(client.Contains("has no interactable."),
				"The client must say when the hovered object resolves to nothing.");
			LogAssert.IsTrue(client.Contains("Refused: CanInteract false"),
				"The client must say when interaction was refused, with the corpse/range detail.");
			LogAssert.IsTrue(client.Contains("its scene-object ID was never assigned client-side"),
				"Sending ID 0 must be named for what it is: a client-side registration fault.");
			LogAssert.IsTrue(client.Contains("Sent interact"),
				"A successful send must be visible, so 'client sent / server never answered' is distinguishable.");

			string validation = ReadSource("Assets/Scripts/Shared/Core/Entity/CharacterStateValidation.cs");
			foreach (string refusal in new[] { "is dead.", "is teleporting.", "is incapacitated", "is not loaded" })
			{
				LogAssert.IsTrue(validation.Contains(refusal),
					$"CanAct must log the '{refusal}' refusal — it fronts every state-mutating broadcast handler " +
					"and a stuck flag here silently disables interaction, trading and crafting at once.");
			}

			string server = ReadSource("Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.cs");
			LogAssert.IsTrue(server.Contains("carries no interactable."),
				"The server must say when a resolved scene object has no interactable.");
			LogAssert.IsTrue(server.Contains("CanInteract refused"),
				"The server must say when it refused, with the corpse/range detail.");
			LogAssert.IsTrue(server.Contains("rate limited."),
				"The server must say when the limiter refused.");
		}

		/// <summary>
		/// A reconcile naming a platform this peer cannot resolve is logged — the
		/// falling-through-platforms signature.
		/// </summary>
		[Test]
		public void UnresolvablePlatformOnReconcile_IsLogged()
		{
			string source = ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs");
			LogAssert.IsTrue(source.Contains("but it is not registered on this peer"),
				"A rider whose platform cannot resolve simulates with zero platform velocity while the server " +
				"uses the real one — the classic fall-off/fall-through divergence, and it must not be silent.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Returns the brace-matched body of the first method whose signature contains
		/// <paramref name="signatureFragment"/>, or null when absent.
		/// </summary>
		private static string ExtractMethodBody(string source, string signatureFragment)
		{
			int signature = source.IndexOf(signatureFragment, StringComparison.Ordinal);
			if (signature < 0)
			{
				return null;
			}
			int open = source.IndexOf('{', signature);
			if (open < 0)
			{
				return null;
			}
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					depth++;
				}
				else if (source[i] == '}')
				{
					depth--;
					if (depth == 0)
					{
						return source.Substring(open, i - open + 1);
					}
				}
			}
			return null;
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}
	}
}
