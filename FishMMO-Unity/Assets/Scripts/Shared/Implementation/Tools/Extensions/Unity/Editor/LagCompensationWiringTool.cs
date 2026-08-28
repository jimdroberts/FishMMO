#if UNITY_EDITOR
using System.Collections.Generic;
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Attaches <see cref="CharacterPositionHistory"/> to every predicted character prefab.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Uses <see cref="PrefabUtility"/> rather than editing prefab YAML. Hand-editing that YAML is
	/// how this component first went in, and it went onto the wrong GameObject on every prefab: a
	/// prefab's root is the object whose Transform has no parent, which is frequently <b>not</b> the
	/// first <c>GameObject</c> block in the file. The component imported fine and simply hung off a
	/// child, where nothing looks for it. Going through the API removes the entire class of mistake.
	/// </para>
	/// <para>
	/// Idempotent — prefabs that already carry the component are skipped, so this is safe to re-run
	/// after adding new character prefabs.
	/// </para>
	/// </remarks>
	public static class LagCompensationWiringTool
	{
		private static readonly string[] SearchRoots =
		{
			"Assets/Prefabs/Shared/Entity/PlayableCharacters",
			"Assets/Prefabs/Shared/Entity/NPCs",
		};

		/// <summary>
		/// Adds the history component to every predicted character prefab that lacks one.
		/// </summary>
		[MenuItem("FishMMO/Prediction/Attach Position History To Characters")]
		public static void AttachToCharacters()
		{
			List<string> added = new List<string>();
			List<string> skipped = new List<string>();

			foreach (string guid in AssetDatabase.FindAssets("t:Prefab", SearchRoots))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				GameObject root = PrefabUtility.LoadPrefabContents(path);
				if (root == null)
				{
					continue;
				}

				try
				{
					// Only characters are rewound. A prefab without a prediction controller has no
					// authoritative per-tick position worth recording.
					if (root.GetComponent<NetworkObject>() == null ||
						root.GetComponent<CharacterPredictionController>() == null)
					{
						continue;
					}

					if (root.GetComponent<CharacterPositionHistory>() != null)
					{
						skipped.Add(root.name);
						continue;
					}

					root.AddComponent<CharacterPositionHistory>();
					PrefabUtility.SaveAsPrefabAsset(root, path);
					added.Add(root.name);
				}
				finally
				{
					PrefabUtility.UnloadPrefabContents(root);
				}
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log($"[LagCompensationWiringTool] added={added.Count} ({string.Join(", ", added)}) " +
				$"alreadyPresent={skipped.Count} ({string.Join(", ", skipped)})");
		}
		/// <summary>
		/// Attaches a <see cref="FishNet.Observing.NetworkObserver"/> with the matching distance
		/// condition to every playable-character and world-item prefab that lacks one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the interest-management half of the migration. Every scene-budget projection in
		/// the plan assumes a culled visible set; without a <c>NetworkObserver</c> on the player
		/// prefabs the "visible peers" figure is the whole population and every client pays the
		/// all-visible row. NPC prefabs already carry observers (30 m interactable, 50 m monster);
		/// this brings players (100 m) and world items (15 m) in line, using the condition assets
		/// under <c>Assets/Settings/ObserverConditions</c>.
		/// </para>
		/// <para>
		/// Mirrors the NPC wiring exactly — <c>_overrideType</c> and <c>_updateHostVisibility</c> are
		/// copied from what the interactable prefabs already use, and the condition is appended to
		/// <c>_observerConditions</c> through <see cref="SerializedObject"/> so Unity's own dirtying
		/// applies. Idempotent: a prefab that already has an observer keeps it, and a condition
		/// already in the list is not added twice.
		/// </para>
		/// </remarks>
		[MenuItem("FishMMO/Prediction/Attach Observers To Players And World Items")]
		public static void AttachObserversToPlayersAndWorldItems()
		{
			(string root, string conditionPath)[] targets =
			{
				("Assets/Prefabs/Shared/Entity/PlayableCharacters",
				 "Assets/Settings/ObserverConditions/PlayerDistanceCondition.asset"),
				("Assets/Prefabs/Shared/Entity/Interactables/World Items",
				 "Assets/Settings/ObserverConditions/WorldItemDistanceCondition.asset"),
			};

			List<string> report = new List<string>();

			foreach ((string root, string conditionPath) in targets)
			{
				FishNet.Observing.ObserverCondition condition =
					AssetDatabase.LoadAssetAtPath<FishNet.Observing.ObserverCondition>(conditionPath);
				if (condition == null)
				{
					Debug.LogError($"[LagCompensationWiringTool] condition asset missing: {conditionPath}");
					continue;
				}

				foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
				{
					string path = AssetDatabase.GUIDToAssetPath(guid);
					GameObject prefab = PrefabUtility.LoadPrefabContents(path);
					if (prefab == null)
					{
						continue;
					}

					try
					{
						if (prefab.GetComponent<NetworkObject>() == null)
						{
							continue;
						}

						FishNet.Observing.NetworkObserver observer = prefab.GetComponent<FishNet.Observing.NetworkObserver>();
						bool added = false;
						if (observer == null)
						{
							observer = prefab.AddComponent<FishNet.Observing.NetworkObserver>();
							added = true;
						}

						SerializedObject so = new SerializedObject(observer);
						SerializedProperty overrideType = so.FindProperty("_overrideType");
						SerializedProperty updateHost = so.FindProperty("_updateHostVisibility");
						SerializedProperty conditions = so.FindProperty("_observerConditions");

						// Same values the NPC prefabs carry (see HumanBanker.prefab).
						if (added)
						{
							if (overrideType != null) overrideType.intValue = (int)FishNet.Observing.NetworkObserver.ConditionOverrideType.AddMissing;
							if (updateHost != null) updateHost.boolValue = true;
						}

						bool conditionAdded = false;
						if (conditions != null)
						{
							bool present = false;
							for (int i = 0; i < conditions.arraySize; i++)
							{
								if (conditions.GetArrayElementAtIndex(i).objectReferenceValue == condition)
								{
									present = true;
									break;
								}
							}
							if (!present)
							{
								conditions.InsertArrayElementAtIndex(conditions.arraySize);
								conditions.GetArrayElementAtIndex(conditions.arraySize - 1).objectReferenceValue = condition;
								conditionAdded = true;
							}
						}

						so.ApplyModifiedPropertiesWithoutUndo();
						if (added || conditionAdded)
						{
							PrefabUtility.SaveAsPrefabAsset(prefab, path);
						}

						report.Add($"{prefab.name}: observerAdded={added} conditionAdded={conditionAdded} ({condition.name})");
					}
					finally
					{
						PrefabUtility.UnloadPrefabContents(prefab);
					}
				}
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[LagCompensationWiringTool] " + string.Join(" | ", report));
		}

		/// <summary>
		/// Prepares the playable character prefabs for interpolated spectating.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Three things, in the order they must happen. A <see cref="NetworkTransform"/> is added,
		/// because with state forwarding off it becomes the <i>only</i> thing replicating a
		/// character's position to anyone but its owner — the playable prefabs have none today, so
		/// flipping the flag first would freeze every player in place for every observer while
		/// server-resolved damage kept landing. It is then assigned to
		/// <c>NetworkObject._networkTransform</c>, which is what lets
		/// <c>InitializePredictionEarly</c> call <c>ConfigureForPrediction</c> on it. Finally
		/// <c>_enableStateForwarding</c> is cleared.
		/// </para>
		/// <para>
		/// Both NetworkObject fields are private and serialized, so they are written through
		/// <see cref="SerializedObject"/> rather than reflection — that keeps Unity's own dirtying
		/// and undo bookkeeping correct instead of writing a field behind the editor's back.
		/// </para>
		/// </remarks>
		[MenuItem("FishMMO/Prediction/Switch Playable Characters To Interpolated")]
		public static void SwitchPlayableCharactersToInterpolated()
		{
			const string root = "Assets/Prefabs/Shared/Entity/PlayableCharacters";
			List<string> report = new List<string>();

			foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = PrefabUtility.LoadPrefabContents(path);
				if (prefab == null)
				{
					continue;
				}

				try
				{
					NetworkObject nob = prefab.GetComponent<NetworkObject>();
					if (nob == null)
					{
						continue;
					}

					NetworkTransform nt = prefab.GetComponent<NetworkTransform>();
					bool addedTransform = false;
					if (nt == null)
					{
						nt = prefab.AddComponent<NetworkTransform>();
						addedTransform = true;
					}

					bool addedLod = false;
					if (prefab.GetComponent<NetworkTransformDistanceLod>() == null)
					{
						prefab.AddComponent<NetworkTransformDistanceLod>();
						addedLod = true;
					}

					SerializedObject so = new SerializedObject(nob);
					SerializedProperty forwarding = so.FindProperty("_enableStateForwarding");
					SerializedProperty transformRef = so.FindProperty("_networkTransform");

					bool changedForwarding = false;
					if (forwarding != null && forwarding.boolValue)
					{
						forwarding.boolValue = false;
						changedForwarding = true;
					}

					bool assignedTransform = false;
					if (transformRef != null && transformRef.objectReferenceValue == null)
					{
						transformRef.objectReferenceValue = nt;
						assignedTransform = true;
					}

					so.ApplyModifiedPropertiesWithoutUndo();
					PrefabUtility.SaveAsPrefabAsset(prefab, path);

					report.Add($"{prefab.name}: nt={addedTransform} lod={addedLod} " +
						$"assigned={assignedTransform} forwardingOff={changedForwarding}");
				}
				finally
				{
					PrefabUtility.UnloadPrefabContents(prefab);
				}
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[LagCompensationWiringTool] " + string.Join(" | ", report));
		}
	}
}
#endif
