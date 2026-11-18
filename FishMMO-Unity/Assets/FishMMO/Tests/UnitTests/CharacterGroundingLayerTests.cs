using System.Collections.Generic;
using System.Linq;
using KinematicCharacterController;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards the grounding layer masks that issue #150 was about.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="KinematicCharacterMotor"/> keeps "what I collide with" and "what I can stand on"
	/// as separate masks and probes for ground with <c>CollidableLayers &amp; StableGroundLayers</c>.
	/// When the two disagree, a surface is solid enough to rest on but never reports
	/// <c>IsStableOnGround</c> — the character gets the unstable-surface path, loses ground friction
	/// and slides off. #150 was exactly that: <c>StableGroundLayers</c> named only Ground, while a
	/// quarter of the world's colliders — the moving platform among them — sit on Default.
	/// </para>
	/// <para>
	/// The failure is silent. Nothing logs, nothing throws; the character just slides, and the cause
	/// is two numbers in a prefab that no code refers to. So the invariant is asserted here rather
	/// than left to be rediscovered by walking around the world: every layer the world actually puts
	/// a walkable collider on must be standable by every character.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CharacterGroundingLayerTests
	{
		/// <summary>Prefabs carrying a character motor.</summary>
		private const string CharacterPrefabFolder = "Assets/Prefabs/Shared/Entity/PlayableCharacters";

		/// <summary>Scenes the player can walk around in.</summary>
		private const string WorldSceneFolder = "Assets/Scenes/WorldScene";

		private static List<GameObject> CharacterPrefabs()
		{
			List<GameObject> prefabs = AssetDatabase
				.FindAssets("t:Prefab", new[] { CharacterPrefabFolder })
				.Select(AssetDatabase.GUIDToAssetPath)
				.Select(AssetDatabase.LoadAssetAtPath<GameObject>)
				.Where(go => go != null && go.GetComponent<KinematicCharacterMotor>() != null)
				.ToList();

			LogAssert.IsTrue(prefabs.Count > 0,
				$"No character prefabs with a motor were found under {CharacterPrefabFolder}; "
				+ "this fixture would pass vacuously.");

			return prefabs;
		}

		private static string LayerLabel(int layer)
		{
			string name = LayerMask.LayerToName(layer);
			return string.IsNullOrEmpty(name) ? $"layer {layer}" : $"{name} ({layer})";
		}

		[Test]
		public void StableGroundLayers_AreAlsoCollidable_OrTheyDoNothing()
		{
			List<string> problems = new List<string>();

			foreach (GameObject prefab in CharacterPrefabs())
			{
				KinematicCharacterMotor motor = prefab.GetComponent<KinematicCharacterMotor>();
				int stableOnly = motor.StableGroundLayers.value & ~motor.CollidableLayers.value;

				for (int layer = 0; layer < 32; layer++)
				{
					if ((stableOnly & (1 << layer)) != 0)
					{
						problems.Add($"{prefab.name}: {LayerLabel(layer)} is in StableGroundLayers "
							+ "but not CollidableLayers, so the motor never probes it.");
					}
				}
			}

			LogAssert.IsTrue(problems.Count == 0, string.Join("\n  ", problems));
		}

		[Test]
		public void EveryWalkableWorldLayer_IsStandableByEveryCharacter()
		{
			HashSet<int> worldLayers = WalkableColliderLayers(out int colliderCount);

			LogAssert.IsTrue(colliderCount > 0,
				$"No solid colliders were found in {WorldSceneFolder}; this fixture would pass vacuously.");

			List<string> problems = new List<string>();

			foreach (GameObject prefab in CharacterPrefabs())
			{
				KinematicCharacterMotor motor = prefab.GetComponent<KinematicCharacterMotor>();

				/* The motor's real ground mask. Reproduced rather than referenced because it is
				 * computed inline at four call sites in KinematicCharacterMotor. */
				int groundMask = motor.CollidableLayers.value & motor.StableGroundLayers.value;

				foreach (int layer in worldLayers.OrderBy(l => l))
				{
					if ((groundMask & (1 << layer)) != 0)
					{
						continue;
					}

					bool collidable = (motor.CollidableLayers.value & (1 << layer)) != 0;
					problems.Add($"{prefab.name}: world geometry on {LayerLabel(layer)} is "
						+ (collidable
							? "collidable but NOT stable ground -- characters will slide off it (issue #150)."
							: "not even collidable -- characters will pass through it."));
				}
			}

			LogAssert.IsTrue(problems.Count == 0,
				"Character grounding masks do not cover the layers the world is built on:\n  "
				+ string.Join("\n  ", problems));
		}

		/// <summary>
		/// Layers used by solid (non-trigger) colliders across the world scenes.
		/// </summary>
		/// <remarks>
		/// Triggers are excluded deliberately — a trigger volume is never stood on, so requiring its
		/// layer to be stable ground would force unrelated layers into the mask and defeat the
		/// point of the check.
		/// </remarks>
		private static HashSet<int> WalkableColliderLayers(out int colliderCount)
		{
			HashSet<int> layers = new HashSet<int>();
			colliderCount = 0;

			string[] scenePaths = AssetDatabase
				.FindAssets("t:Scene", new[] { WorldSceneFolder })
				.Select(AssetDatabase.GUIDToAssetPath)
				.ToArray();

			foreach (string scenePath in scenePaths)
			{
				Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
				try
				{
					foreach (GameObject root in scene.GetRootGameObjects())
					{
						foreach (Collider collider in root.GetComponentsInChildren<Collider>(includeInactive: true))
						{
							if (collider.isTrigger)
							{
								continue;
							}

							layers.Add(collider.gameObject.layer);
							colliderCount++;
						}
					}
				}
				finally
				{
					EditorSceneManager.CloseScene(scene, removeScene: true);
				}
			}

			return layers;
		}
	}
}
