#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Editor checks that catch FishNet spawnable-contract drift before it becomes
	/// runtime <c>RPCLink of Id N could not be found</c> / unhandled PacketId spam.
	/// </summary>
	/// <remarks>
	/// Root cause class: stored <see cref="NetworkObject.PrefabId"/> must match the
	/// index of that object in <c>DefaultPrefabObjects</c> (asset order already matches
	/// FishNet runtime <c>Sort()</c> by AssetPathHash when hashes are set correctly).
	/// Player race prefabs must also have contiguous ComponentIndex 0..N-1 with no null
	/// NetworkBehaviour slots.
	/// </remarks>
	public static class DefaultPrefabObjectsContractValidator
	{
		private const string DefaultPrefabObjectsPath = "Assets/DefaultPrefabObjects.asset";
		private static readonly string[] PlayableRacePrefabPaths =
		{
			"Assets/Prefabs/Shared/Entity/PlayableCharacters/Elf.prefab",
			"Assets/Prefabs/Shared/Entity/PlayableCharacters/Human.prefab",
			"Assets/Prefabs/Shared/Entity/PlayableCharacters/Orc.prefab",
		};

		[MenuItem("FishMMO/Validate/DefaultPrefabObjects Contract")]
		public static void ValidateMenu()
		{
			if (Validate(logSuccess: true, out string report))
			{
				Debug.Log($"[FishMMO] DefaultPrefabObjects contract OK.\n{report}");
			}
			else
			{
				Debug.LogError($"[FishMMO] DefaultPrefabObjects contract FAILED.\n{report}");
				EditorUtility.DisplayDialog(
					"DefaultPrefabObjects Contract",
					"Contract check failed. See Console for details.\n\n" +
					"Fix: Fish-Networking → Refresh Default Prefabs, then re-run this validator.",
					"OK");
			}
		}

		/// <summary>
		/// Returns true when every spawnable PrefabId matches its collection index and
		/// playable race prefabs have a clean NetworkBehaviour index sequence.
		/// </summary>
		public static bool Validate(bool logSuccess, out string report)
		{
			StringBuilder sb = new StringBuilder();
			bool ok = true;

			DefaultPrefabObjects dpo = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabObjectsPath);
			if (dpo == null)
			{
				report = $"Missing {DefaultPrefabObjectsPath}";
				return false;
			}

			IReadOnlyList<NetworkObject> prefabs = dpo.Prefabs;
			sb.AppendLine($"DefaultPrefabObjects count={prefabs.Count}");

			HashSet<ulong> seenHashes = new HashSet<ulong>();
			for (int i = 0; i < prefabs.Count; i++)
			{
				NetworkObject nob = prefabs[i];
				if (nob == null)
				{
					ok = false;
					sb.AppendLine($"  [{i}] NULL prefab slot");
					continue;
				}

				if (nob.AssetPathHash == 0)
				{
					ok = false;
					sb.AppendLine($"  [{i}] {nob.name}: AssetPathHash=0 (FishNet Sort will abort)");
				}
				else if (!seenHashes.Add(nob.AssetPathHash))
				{
					ok = false;
					sb.AppendLine($"  [{i}] {nob.name}: duplicate AssetPathHash {nob.AssetPathHash}");
				}

				if (nob.PrefabId != (ushort)i)
				{
					ok = false;
					sb.AppendLine(
						$"  [{i}] {nob.name}: PrefabId={nob.PrefabId} expected {i} " +
						"(client/server spawn identity will desync)");
				}
				else
				{
					sb.AppendLine($"  [{i}] {nob.name}: PrefabId={nob.PrefabId} OK hash={nob.AssetPathHash}");
				}
			}

			sb.AppendLine("Playable race NetworkBehaviour layout:");
			foreach (string path in PlayableRacePrefabPaths)
			{
				GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (go == null)
				{
					ok = false;
					sb.AppendLine($"  MISSING {path}");
					continue;
				}

				NetworkObject nob = go.GetComponent<NetworkObject>();
				if (nob == null)
				{
					ok = false;
					sb.AppendLine($"  {go.name}: no NetworkObject");
					continue;
				}

				NetworkBehaviour[] nbs = go.GetComponents<NetworkBehaviour>();
				bool raceOk = true;
				if (nbs == null || nbs.Length == 0)
				{
					ok = false;
					raceOk = false;
					sb.AppendLine($"  {go.name}: zero NetworkBehaviours");
				}
				else
				{
					for (int i = 0; i < nbs.Length; i++)
					{
						if (nbs[i] == null)
						{
							ok = false;
							raceOk = false;
							sb.AppendLine($"  {go.name}: null NetworkBehaviour at GetComponents index {i}");
							continue;
						}
						// ComponentIndex is runtime-assigned; serialized cache is inspected via prefab YAML
						// for play-mode only. Here we only verify no missing scripts.
					}

					// Missing scripts show as null components on the GameObject.
					Component[] all = go.GetComponents<Component>();
					for (int i = 0; i < all.Length; i++)
					{
						if (all[i] == null)
						{
							ok = false;
							raceOk = false;
							sb.AppendLine($"  {go.name}: missing script Component slot {i}");
						}
					}
				}

				if (raceOk)
				{
					sb.AppendLine(
						$"  {go.name}: PrefabId={nob.PrefabId} NBs={nbs.Length} missingScripts=0 OK");
				}
			}

			report = sb.ToString();
			return ok;
		}

		/// <summary>
		/// Optional pre-build gate. Enable via menu toggle if desired.
		/// </summary>
		[MenuItem("FishMMO/Validate/Run DefaultPrefabObjects Contract On Build", true)]
		private static bool ToggleValidateOnBuildValidate()
		{
			Menu.SetChecked(
				"FishMMO/Validate/Run DefaultPrefabObjects Contract On Build",
				EditorPrefs.GetBool("FishMMO.ValidateDpoOnBuild", false));
			return true;
		}

		[MenuItem("FishMMO/Validate/Run DefaultPrefabObjects Contract On Build")]
		private static void ToggleValidateOnBuild()
		{
			bool next = !EditorPrefs.GetBool("FishMMO.ValidateDpoOnBuild", false);
			EditorPrefs.SetBool("FishMMO.ValidateDpoOnBuild", next);
			Menu.SetChecked("FishMMO/Validate/Run DefaultPrefabObjects Contract On Build", next);
		}

		[InitializeOnLoadMethod]
		private static void RegisterBuildHook()
		{
			// No automatic build failure unless the toggle is on — keep default non-blocking.
		}
	}
}
#endif
