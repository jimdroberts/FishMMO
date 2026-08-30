using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the rule that every consumer of a raw overlap buffer resolves the character it found
	/// through <see cref="TargetOrdering"/> rather than with a bare <c>GetComponent</c> on the
	/// collider.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a bare <c>GetComponent</c> is wrong in two directions at once.</b> The collider a
	/// physics query returns is frequently not the object anything cares about — a prefab is free to
	/// hang its hitbox off a child transform. A <c>GetComponent&lt;ICharacter&gt;()</c> on that child
	/// finds nothing, so the character is dropped from the result entirely; and a character rigged
	/// with several colliders on one body is counted once per collider, so whatever the loop is
	/// measuring scales with how the target happens to be rigged.
	/// <see cref="TargetOrdering.ResolveHitKey"/> walks to the attached rigidbody and then to the
	/// parents, and <see cref="TargetOrdering.ContainsBody"/> collapses the duplicates.
	/// </para>
	/// <para>
	/// The 2026-08-29 combat audit established that resolution as the single shared implementation
	/// and converted the ability and selector paths to it. Three overlap consumers were missed:
	/// <c>ApplyThreatAction</c>, <c>BaseAIState.SweepForEnemies</c> and
	/// <c>HealerAttackingState</c>'s ally scan. The behavioural tests below pin the resolution
	/// itself; the source guard at the bottom pins the call sites, because those three sit behind an
	/// <c>AIController</c>, a faction table and a live <c>PhysicsScene</c> and cannot be reached from
	/// an EditMode fixture without standing all three up.
	/// </para>
	/// <para>
	/// Every prefab in the project carries exactly one collider on its root today, so none of this is
	/// reachable from shipped content — it becomes live the moment a character is rigged with a child
	/// hitbox, which is the normal way to rig one. See <see cref="TargetSelectorBodyIdentityTests"/>,
	/// which pins the same identity rule for the target selectors.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class OverlapHitRootResolutionTests
	{
		private readonly List<GameObject> spawned = new List<GameObject>();

		[TearDown]
		public void DestroySpawned()
		{
			for (int i = 0; i < spawned.Count; ++i)
			{
				if (spawned[i] != null)
				{
					Object.DestroyImmediate(spawned[i]);
				}
			}
			spawned.Clear();
		}

		// ── The resolution these call sites depend on ───────────────────────────────

		/// <summary>
		/// A hit on a child hitbox resolves to the character on the root.
		/// </summary>
		/// <remarks>
		/// This is the half a bare <c>GetComponent</c> gets wrong by dropping the candidate: such an
		/// NPC never noticed a cast that applied threat, and was invisible to every other NPC's enemy
		/// sweep, while remaining perfectly able to attack them.
		/// </remarks>
		[Test]
		public void ResolveHitKey_FromChildHitbox_FindsTheCharacterOnTheRoot()
		{
			GameObject root = MakeCharacterBody("childHitboxNpc", Vector3.zero, new Vector3(0f, 1f, 0f));
			Collider hitbox = root.transform.GetChild(0).GetComponent<Collider>();

			GameObject key = TargetOrdering.ResolveHitKey(hitbox, out ICharacter character);

			LogAssert.IsTrue(character != null,
				"A collider on a child transform must still resolve to the ICharacter on the body. " +
				"GetComponent on the collider returns null here, which is how such a character " +
				"became invisible to the threat and AI sweeps.");
			LogAssert.AreEqual(root, key,
				"The dedupe key for a character's hitbox is the character's own GameObject.");
		}

		/// <summary>
		/// Two hitboxes on one body produce one key, so a per-body loop credits the body once.
		/// </summary>
		/// <remarks>
		/// The other half. <c>ApplyThreatAction</c> called <c>AddPoints</c> once per collider, so a
		/// two-hitbox NPC gained double threat from the same cast — a threat table that depends on
		/// rigging rather than on what the players did.
		/// </remarks>
		[Test]
		public void ContainsBody_CollapsesTwoHitboxesOnOneCharacter()
		{
			GameObject root = MakeCharacterBody("twoHitboxNpc", Vector3.zero,
				new Vector3(0f, 1f, 0f), new Vector3(0f, 0.2f, 0f));

			Collider first = root.transform.GetChild(0).GetComponent<Collider>();
			Collider second = root.transform.GetChild(1).GetComponent<Collider>();

			List<GameObject> kept = new List<GameObject>();

			GameObject firstKey = TargetOrdering.ResolveHitKey(first, out ICharacter _);
			LogAssert.IsFalse(TargetOrdering.ContainsBody(kept, firstKey),
				"The first hitbox of a body is not a duplicate.");
			kept.Add(firstKey);

			GameObject secondKey = TargetOrdering.ResolveHitKey(second, out ICharacter _);
			LogAssert.IsTrue(TargetOrdering.ContainsBody(kept, secondKey),
				"The second hitbox of the SAME body must be recognised as a duplicate, or every " +
				"per-body count scales with how the target is rigged.");
		}

		/// <summary>
		/// Scenery keys to itself, so a wall's many colliders stay many candidates.
		/// </summary>
		/// <remarks>
		/// The dedupe must not be a blanket one. A wall panel has neither a rigidbody nor a
		/// character, so each of its colliders is its own body — which is what a beam or a blast
		/// should see, and what stops the rule above from silently narrowing an unrelated query.
		/// </remarks>
		[Test]
		public void ContainsBody_DoesNotCollapseUnrelatedScenery()
		{
			GameObject first = MakeLooseCollider("panelA", Vector3.zero);
			GameObject second = MakeLooseCollider("panelB", new Vector3(1f, 0f, 0f));

			List<GameObject> kept = new List<GameObject>();
			kept.Add(TargetOrdering.ResolveHitKey(first.GetComponent<Collider>(), out ICharacter _));

			GameObject secondKey = TargetOrdering.ResolveHitKey(second.GetComponent<Collider>(), out ICharacter _);
			LogAssert.IsFalse(TargetOrdering.ContainsBody(kept, secondKey),
				"Two unrelated scene colliders are two separate bodies and must both survive.");
		}

		// ── The call sites ──────────────────────────────────────────────────────────

		/// <summary>
		/// The three overlap consumers outside the selector and ability paths resolve through
		/// <see cref="TargetOrdering"/>.
		/// </summary>
		/// <remarks>
		/// A source guard rather than a behavioural one, and deliberately so: all three run inside a
		/// live <see cref="PhysicsScene"/> behind an <c>AIController</c> and a faction table, none of
		/// which an EditMode fixture can stand up, while the defect itself is a single expression at
		/// each site. Matching on the expression is what makes reverting any one of them fail here.
		/// </remarks>
		[Test]
		public void OverlapConsumers_ResolveHitsThroughTargetOrdering()
		{
			string[] sources =
			{
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/ApplyThreatAction.cs",
				"Assets/Scripts/Shared/Implementation/Entity/NPC/AI/BaseAIState.cs",
				"Assets/Scripts/Shared/Implementation/Entity/NPC/AI/States/HealerAttackingState.cs",
			};

			/* Any GetComponent for an ICharacter reached off a collider. The three sites spelled it
			 * `collider.GetComponent<ICharacter>()`, `col.GetComponent<ICharacter>()` and
			 * `hits[i].gameObject.GetComponent<ICharacter>()`, so the receiver is matched loosely and
			 * only the call itself is pinned. */
			Regex bareResolve = new Regex(@"\.GetComponent<\s*ICharacter\s*>\s*\(\s*\)");

			for (int i = 0; i < sources.Length; ++i)
			{
				string text = ReadSource(sources[i]);

				LogAssert.IsFalse(bareResolve.IsMatch(text),
					$"{sources[i]} resolves a character with a bare GetComponent on a query result. " +
					"That drops a character whose hitbox is a child transform and counts a " +
					"multi-collider character once per collider. Use TargetOrdering.ResolveHitKey.");

				LogAssert.IsTrue(text.Contains("TargetOrdering.ResolveHitKey"),
					$"{sources[i]} consumes a raw overlap buffer and must resolve its hits through " +
					"TargetOrdering.ResolveHitKey, the shared implementation the 2026-08-29 combat " +
					"audit established.");
			}
		}

		/// <summary>
		/// The two sites that grant something per body deduplicate; the one that keeps a single best
		/// candidate does not need to.
		/// </summary>
		/// <remarks>
		/// Stated separately from the resolution above because they are different failures.
		/// Resolution decides WHO is a candidate; the dedupe decides how many times each of them is
		/// counted. <c>ApplyThreatAction</c> adds points per entry and <c>SweepForEnemies</c> appends
		/// to a list something else later picks from, so both must collapse duplicates.
		/// <c>HealerAttackingState</c> keeps one running best and a body's duplicate colliders all
		/// report the same health, so it is exempt.
		/// </remarks>
		[Test]
		public void PerBodyGrants_DeduplicateDuplicateColliders()
		{
			string[] mustDedupe =
			{
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/ApplyThreatAction.cs",
				"Assets/Scripts/Shared/Implementation/Entity/NPC/AI/BaseAIState.cs",
			};

			for (int i = 0; i < mustDedupe.Length; ++i)
			{
				string text = ReadSource(mustDedupe[i]);
				LogAssert.IsTrue(text.Contains("TargetOrdering.ContainsBody"),
					$"{mustDedupe[i]} grants once per entry it accepts, so it must collapse the " +
					"several colliders one body can return — otherwise the grant scales with rigging.");
			}
		}

		// ── Helpers ─────────────────────────────────────────────────────────────────

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"Expected source at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>A collider that belongs to nothing — scenery, as far as keying goes.</summary>
		private GameObject MakeLooseCollider(string name, Vector3 position)
		{
			GameObject go = new GameObject(name);
			go.transform.position = position;
			SphereCollider collider = go.AddComponent<SphereCollider>();
			collider.radius = 0.3f;
			spawned.Add(go);
			return go;
		}

		/// <summary>
		/// A character root with its colliders on CHILD transforms — the rig no shipped prefab uses
		/// yet and every one of these defects needs.
		/// </summary>
		private GameObject MakeCharacterBody(string name, Vector3 position, params Vector3[] hitboxOffsets)
		{
			GameObject root = new GameObject(name);
			root.transform.position = position;
			root.AddComponent<StubOverlapCharacter>();
			spawned.Add(root);

			for (int i = 0; i < hitboxOffsets.Length; ++i)
			{
				GameObject child = new GameObject($"{name}Hitbox{i}");
				child.transform.SetParent(root.transform, false);
				child.transform.localPosition = hitboxOffsets[i];
				SphereCollider collider = child.AddComponent<SphereCollider>();
				collider.radius = 0.3f;
			}
			return root;
		}

		/// <summary>
		/// The smallest <see cref="ICharacter"/> a parent walk can find.
		/// </summary>
		/// <remarks>
		/// A MonoBehaviour rather than a plain-object mock, because the whole point is the WALK, and
		/// a walk needs a real component on a real transform to find.
		/// </remarks>
		private sealed class StubOverlapCharacter : MonoBehaviour, ICharacter
		{
			public long ID { get; set; }
			public string Name => name;
			public Transform Transform => transform;
			public GameObject GameObject => gameObject;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
			public Transform MeshRoot => transform;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
