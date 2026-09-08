using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests.Prediction
{
	/// <summary>
	/// A Forward-spawned melee volume (Punch, Orc Slam) must sit where the AI's reach says it
	/// does. Both are derived from the PREFAB ASSET's collider, and a collider that has never
	/// been simulated reports an empty <see cref="Collider.bounds"/> — so the spawn offset used to
	/// read as zero while the reach (shape-based) did not, and an orc that had stopped "in reach"
	/// punched the air in front of the player (reported 2026-09-07). These tests pin that both
	/// read the authored shape, and that they agree.
	/// </summary>
	[TestFixture]
	public class ForwardSpawnGeometryTests
	{
		private readonly List<Object> created = new List<Object>();

		[SetUp]
		public void SetUp()
		{
			AbilityPrefabColliderCache.Clear();
		}

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
			AbilityPrefabColliderCache.Clear();
		}

		[Test]
		public void Forward_OffsetsByThePrefabShape_NotItsEmptyBounds()
		{
			// Punch: a 2 m box at half scale, so 0.5 m half extents.
			GameObject prefab = NewPrefab("PunchLike", new Vector3(2f, 2f, 2f), 0.5f);
			Ability ability = NewForwardAbility("ForwardSpawn_Punch", prefab);
			SceneCaster caster = NewCaster("Orc", 0.5f, 1.2f);
			caster.Transform.SetPositionAndRotation(new Vector3(10f, 0f, -4f), Quaternion.Euler(0f, 90f, 0f));
			Physics.SyncTransforms();
			Vector3 casterExtents = caster.Collider.bounds.extents;

			AbilitySpawnPose pose = AbilityObject.ResolveSpawnPose(caster, ability, null, default, Vector3.zero, Vector3.forward);

			Vector3 expected = caster.Transform.position
				+ caster.Transform.forward * (casterExtents.z + 0.5f)
				+ Vector3.up * (casterExtents.y + 0.5f);
			Assert.That(Vector3.Distance(pose.Position, expected), Is.LessThan(1e-4f),
				$"Forward spawn must offset by the prefab's authored half extents; got {pose.Position}, expected {expected}.");

			// The near face starts at the caster's surface: none of the volume is wasted inside the caster.
			float nearFace = Vector3.Dot(pose.Position - caster.Transform.position, caster.Transform.forward) - 0.5f;
			Assert.That(nearFace, Is.EqualTo(casterExtents.z).Within(1e-4f),
				"The volume's near face must sit on the caster's surface.");
		}

		[Test]
		public void Forward_FarFace_IsTheReachTheAIPlansWith()
		{
			// Orc Slam: a 3×2×3 box at half scale. The NPC attacks once the target is within
			// AIAbilityReach.Resolve(...); that promise is only good if the volume really extends
			// that far, so the two must be derived from the same shape.
			GameObject prefab = NewPrefab("SlamLike", new Vector3(3f, 2f, 3f), 0.5f);
			Ability ability = NewForwardAbility("ForwardSpawn_Slam", prefab);
			SceneCaster caster = NewCaster("Orc", 0.5f, 1.2f);
			Physics.SyncTransforms();
			float casterRadius = caster.Collider.bounds.extents.z;

			AbilitySpawnPose pose = AbilityObject.ResolveSpawnPose(caster, ability, null, default, Vector3.zero, Vector3.forward);

			float halfDepth = AbilityPrefabColliderCache.ResolveShapeHalfExtents(prefab.GetComponent<Collider>()).z;
			Assert.That(halfDepth, Is.EqualTo(0.75f).Within(1e-5f));
			float farFace = Vector3.Dot(pose.Position - caster.Transform.position, caster.Transform.forward) + halfDepth;
			Assert.That(farFace, Is.EqualTo(casterRadius + 2f * halfDepth).Within(1e-4f),
				"The far face must be the caster's radius plus the volume's full depth.");

			float reach = AIAbilityReach.Resolve(ability, casterRadius);
			Assert.That(reach - AIAbilityReach.REACH_SLACK, Is.EqualTo(farFace).Within(1e-4f),
				"The reach the AI plans with is the far face plus its slack; spawn and reach must read the same shape.");
		}

		[TestCase("Assets/Templates/Entity/Abilities/Types/Punch.asset")]
		[TestCase("Assets/Templates/Entity/Abilities/Types/Orc/Orc Slam.asset")]
		public void ShippedMeleeAbility_SpawnsItsFullDepthInFrontOfAnOrc(string templatePath)
		{
			// The production input: a real prefab ASSET, whose collider has never been simulated.
			AbilityTemplate template = UnityEditor.AssetDatabase.LoadAssetAtPath<AbilityTemplate>(templatePath);
			Assert.That(template, Is.Not.Null, $"'{templatePath}' must exist.");
			Assert.That(template.AbilitySpawnTarget, Is.EqualTo(AbilitySpawnTarget.Forward));
			Collider prefabCollider = AbilityPrefabColliderCache.GetPrefabCollider(template);
			Assert.That(prefabCollider, Is.Not.Null, "A Forward ability needs a collider to sweep with.");
			Ability ability = new Ability(1L, template);

			// An orc: capsule radius 0.5, the same figure AIController hands AIAbilityReach as Agent.radius.
			SceneCaster caster = NewCaster("Orc", 0.5f, 1.2f);
			Physics.SyncTransforms();
			float casterRadius = caster.Collider.bounds.extents.z;

			AbilitySpawnPose pose = AbilityObject.ResolveSpawnPose(caster, ability, null, default, Vector3.zero, Vector3.forward);

			float halfDepth = AbilityPrefabColliderCache.ResolveShapeHalfExtents(prefabCollider).z;
			Assert.That(halfDepth, Is.GreaterThan(0.25f), "The shipped melee volumes are at least half a metre deep.");
			float nearFace = Vector3.Dot(pose.Position - caster.Transform.position, caster.Transform.forward) - halfDepth;
			float farFace = nearFace + 2f * halfDepth;
			Assert.That(nearFace, Is.EqualTo(casterRadius).Within(1e-4f),
				$"'{template.name}' must start at the orc's surface, not half inside it.");
			Assert.That(farFace, Is.EqualTo(AIAbilityReach.Resolve(ability, casterRadius) - AIAbilityReach.REACH_SLACK).Within(1e-4f),
				$"'{template.name}' must reach exactly as far as the AI believes it does.");
		}

		[Test]
		public void Forward_WithoutAPrefabCollider_SpawnsAtTheCaster()
		{
			GameObject prefab = new GameObject("NoCollider");
			created.Add(prefab);
			prefab.SetActive(false);
			Ability ability = NewForwardAbility("ForwardSpawn_NoCollider", prefab);
			SceneCaster caster = NewCaster("Orc", 0.5f, 1.2f);
			caster.Transform.position = new Vector3(3f, 1f, 2f);
			Physics.SyncTransforms();

			AbilitySpawnPose pose = AbilityObject.ResolveSpawnPose(caster, ability, null, default, Vector3.zero, Vector3.forward);

			Assert.That(Vector3.Distance(pose.Position, caster.Transform.position), Is.LessThan(1e-5f),
				"A prefab with nothing to sweep keeps the historical pose: the caster's own position.");
		}

		[Test]
		public void ShapeHalfExtents_ReadTheAuthoredShape_ForEveryPrimitive()
		{
			GameObject go = new GameObject("shape-probe");
			created.Add(go);
			go.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
			// Inactive, like a prefab asset: bounds are empty here, the shape is not.
			go.SetActive(false);

			BoxCollider box = go.AddComponent<BoxCollider>();
			box.size = new Vector3(2f, 2f, 4f);
			Assert.That(AbilityPrefabColliderCache.ResolveShapeHalfExtents(box), Is.EqualTo(new Vector3(0.5f, 2f, 1f)),
				"Box: half size per axis, scaled per axis.");

			SphereCollider sphere = go.AddComponent<SphereCollider>();
			sphere.radius = 1f;
			Assert.That(AbilityPrefabColliderCache.ResolveShapeHalfExtents(sphere), Is.EqualTo(new Vector3(2f, 2f, 2f)),
				"Sphere: the largest axis scale on every axis, as physics does.");

			CapsuleCollider upright = go.AddComponent<CapsuleCollider>();
			upright.direction = 1;
			upright.radius = 0.5f;
			upright.height = 2f;
			Assert.That(AbilityPrefabColliderCache.ResolveShapeHalfExtents(upright), Is.EqualTo(new Vector3(0.25f, 2f, 0.25f)),
				"Upright capsule: radius on the cross axes, half height along the axis.");

			CapsuleCollider lying = go.AddComponent<CapsuleCollider>();
			lying.direction = 2;
			lying.radius = 0.5f;
			lying.height = 4f;
			Assert.That(AbilityPrefabColliderCache.ResolveShapeHalfExtents(lying), Is.EqualTo(new Vector3(1f, 1f, 1f)),
				"Capsule along Z: radius takes the larger of the X/Y scales, and the axis is at least the radius.");

			Assert.That(AbilityPrefabColliderCache.ResolveShapeHalfExtents(null), Is.EqualTo(Vector3.zero));

			// And the AI's horizontal reading is the same shape, so the reach cannot drift from the spawn.
			Assert.That(AIAbilityReach.ResolvePrefabHalfExtent(box), Is.EqualTo(1f).Within(1e-5f));
		}

		private GameObject NewPrefab(string name, Vector3 boxSize, float scale)
		{
			GameObject go = new GameObject(name);
			created.Add(go);
			go.transform.localScale = Vector3.one * scale;
			go.AddComponent<BoxCollider>().size = boxSize;
			// A prefab asset is never active in a scene. An inactive object is the closest an
			// edit-mode test gets to one, and its collider reports the same empty bounds.
			go.SetActive(false);
			return go;
		}

		private Ability NewForwardAbility(string name, GameObject prefab)
		{
			AbilityTemplate template = ScriptableObject.CreateInstance<AbilityTemplate>();
			template.name = name;
			created.Add(template);
			template.AbilitySpawnTarget = AbilitySpawnTarget.Forward;
			template.AbilityObjectPrefab = prefab;
			return new Ability(1L, template);
		}

		private SceneCaster NewCaster(string name, float radius, float height)
		{
			GameObject go = new GameObject(name);
			created.Add(go);
			CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
			capsule.radius = radius;
			capsule.height = height;
			capsule.center = new Vector3(0f, height * 0.5f, 0f);
			return new SceneCaster(go, capsule);
		}

		/// <summary>
		/// An <see cref="ICharacter"/> with a real transform and collider, which is all
		/// <see cref="AbilityObject.ResolveSpawnPose"/> reads for a non-player caster.
		/// </summary>
		private sealed class SceneCaster : ICharacter
		{
			public SceneCaster(GameObject gameObject, Collider collider)
			{
				GameObject = gameObject;
				Transform = gameObject.transform;
				Collider = collider;
			}

			public long ID { get; set; }
			public string Name => GameObject.name;
			public Transform Transform { get; }
			public GameObject GameObject { get; }
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers => null;
			public bool IsTeleporting => false;
			public bool IsSpawned => false;
			public int Flags { get; set; }
			public void EnableFlags(CharacterFlags flags) { }
			public void DisableFlags(CharacterFlags flags) { }
			public bool IsFlagged(CharacterFlags flags) => false;
			public Nameplate CharacterNameplate { get; set; }
			public Transform MeshRoot => Transform;
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				control = null;
				return false;
			}
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
