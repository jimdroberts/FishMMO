using System.Collections.Generic;
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
	/// Guards the identity a physics target selector reasons about: a candidate is still the
	/// COLLIDER the query returned, but every identity test a selector performs on it — the
	/// <c>MaxHits</c> cap, the self-exclusion, the chain's visited set — is keyed on the BODY that
	/// collider belongs to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The two halves are deliberately decided differently, and the tests pin both.
	/// </para>
	/// <para>
	/// <b>Resolution stays at the collider.</b> Every framework consumer of a selector forks through
	/// <see cref="EventData.SetTarget"/>, which already walks the parents to find the
	/// <see cref="ICharacter"/> — so <c>TargetCharacter</c>, which is all any action reads, is
	/// character-correct however the target is rigged. Returning the body instead would change what
	/// a NON-character candidate is, and the selectors legitimately return scenery: a hit on a door
	/// panel would come back as the whole building. It also matches
	/// <c>LagCompensatedQuery.CompensatedHit</c>, which carries the collider and the resolved
	/// character side by side rather than collapsing one into the other.
	/// </para>
	/// <para>
	/// <b>Identity is keyed on the body.</b> A prefab may hang several colliders off one character,
	/// and every count a selector performs was measuring colliders: the cap decided how many
	/// HITBOXES an ability affected rather than how many victims, the self-exclusion compared
	/// against the caster's root transform and so never recognised the caster's own hitbox, and the
	/// chain's visited set let a walk arc back onto a victim it had already struck. Static scenery
	/// is untouched — a wall has neither a rigidbody nor a character, so each of its colliders keys
	/// to itself and stays a separate candidate, which is what a beam or a blast should see.
	/// </para>
	/// <para>
	/// Every prefab in the project carries exactly one collider on its root today, so none of this
	/// is reachable from shipped content; these build the rig by hand.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class TargetSelectorBodyIdentityTests
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

		// ── Area: the cap counts victims, not hitboxes ──

		/// <summary>
		/// <c>MaxHits</c> on an area selector bounds the number of BODIES affected.
		/// </summary>
		/// <remarks>
		/// The cap was applied to the raw hits, so a character rigged with two hitboxes filled two of
		/// its slots and the bystander behind it was dropped — the same ability affecting a different
		/// NUMBER of characters depending on how its targets happen to be rigged.
		/// </remarks>
		[Test]
		public void AreaSelector_MaxHits_CountsBodiesNotColliders()
		{
			GameObject context = MakeContext("areaBodyContext", Vector3.zero);
			GameObject body = MakeCharacterBody("areaTwoHitbox", new Vector3(2f, 0f, 0f),
				new Vector3(0f, 0f, 0f), new Vector3(0.4f, 0f, 0f));
			GameObject bystander = MakeLooseCollider("areaBystander", new Vector3(5f, 0f, 0f));
			Physics.SyncTransforms();

			AreaTargetSelector selector = new AreaTargetSelector { Radius = 20f, TargetLayer = ~0, MaxHits = 2 };
			List<GameObject> picked = Select(selector, context);

			LogAssert.AreEqual(2, picked.Count, "Two slots, two bodies.");
			LogAssert.AreEqual(1, CountUnder(picked, body),
				"A body with two hitboxes occupies ONE slot of MaxHits, represented by its nearest collider.");
			LogAssert.IsTrue(picked.Contains(bystander),
				"And the slot the second hitbox used to consume goes to the next body out. Before the " +
				"dedupe both hitboxes were kept and this bystander was never selected at all.");
		}

		/// <summary>
		/// Scenery is NOT collapsed: colliders with no shared body stay separate candidates.
		/// </summary>
		/// <remarks>
		/// The counterpart to the test above, and the reason resolution and dedupe were decided
		/// separately. A wall, a building or a terrain chunk carries many colliders and no rigidbody
		/// and no character, so each keys to itself. A dedupe that collapsed them would silently make
		/// a blast in a corridor select one wall panel instead of the several it overlaps.
		/// </remarks>
		[Test]
		public void AreaSelector_UnrelatedColliders_AreNotCollapsed()
		{
			GameObject context = MakeContext("sceneryContext", Vector3.zero);
			GameObject panelParent = new GameObject("wall");
			spawned.Add(panelParent);
			panelParent.transform.position = new Vector3(2f, 0f, 0f);
			GameObject panelA = AddChildCollider(panelParent, "panelA", new Vector3(0f, 0f, 0f));
			GameObject panelB = AddChildCollider(panelParent, "panelB", new Vector3(0.4f, 0f, 0f));
			Physics.SyncTransforms();

			AreaTargetSelector selector = new AreaTargetSelector { Radius = 20f, TargetLayer = ~0, MaxHits = 8 };
			List<GameObject> picked = Select(selector, context);

			LogAssert.IsTrue(picked.Contains(panelA), "A wall panel is its own body.");
			LogAssert.IsTrue(picked.Contains(panelB),
				"And so is the one next to it. Sharing a parent transform is not sharing a body — only " +
				"a rigidbody or a character makes two colliders one target.");
		}

		// ── Cone: same cap, same rule ──

		/// <summary>A cone's <c>MaxHits</c> counts bodies for the same reason an area's does.</summary>
		[Test]
		public void ConeSelector_MaxHits_CountsBodiesNotColliders()
		{
			GameObject context = MakeContext("coneBodyContext", Vector3.zero);
			context.transform.forward = Vector3.forward;
			GameObject body = MakeCharacterBody("coneTwoHitbox", new Vector3(0f, 0f, 2f),
				new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0.4f));
			GameObject bystander = MakeLooseCollider("coneBystander", new Vector3(0f, 0f, 5f));
			Physics.SyncTransforms();

			ConeTargetSelector selector = new ConeTargetSelector { Radius = 20f, Angle = 90f, TargetLayer = ~0, MaxHits = 2 };
			List<GameObject> picked = Select(selector, context);

			LogAssert.AreEqual(2, picked.Count, "Two slots, two bodies.");
			LogAssert.AreEqual(1, CountUnder(picked, body), "One body, one slot.");
			LogAssert.IsTrue(picked.Contains(bystander), "The bystander gets the slot the duplicate used to eat.");
		}

		// ── Line: a pierce counts bodies, not the faces it passes through ──

		/// <summary>
		/// A beam's <c>MaxHits</c> counts the bodies it pierces.
		/// </summary>
		/// <remarks>
		/// A ray reports every collider it passes through, so a target with two hitboxes consumed two
		/// of a two-pierce beam's slots and the beam stopped short of the victim standing behind it.
		/// </remarks>
		[Test]
		public void LineSelector_MaxHits_CountsBodiesNotColliders()
		{
			GameObject context = MakeContext("lineBodyContext", Vector3.zero);
			context.transform.forward = Vector3.forward;
			GameObject body = MakeCharacterBody("lineTwoHitbox", new Vector3(0f, 0f, 2f),
				new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f));
			GameObject bystander = MakeLooseCollider("lineBystander", new Vector3(0f, 0f, 6f));
			Physics.SyncTransforms();

			LineTargetSelector selector = new LineTargetSelector { Length = 20f, TargetLayer = ~0, MaxHits = 2 };
			List<GameObject> picked = Select(selector, context);

			LogAssert.AreEqual(2, picked.Count, "Two pierce slots, two bodies.");
			LogAssert.AreEqual(1, CountUnder(picked, body),
				"A body is pierced once, at the first collider the ray meets — its entry face.");
			LogAssert.IsTrue(picked.Contains(bystander),
				"The beam reaches the target behind it. Before the dedupe it stopped on the second hitbox.");
		}

		// ── Nearest / Furthest / Random: the caster is excluded by BODY ──

		/// <summary>
		/// A caster whose hitbox is a child is still excluded from its own nearest-target selection.
		/// </summary>
		/// <remarks>
		/// The guard read <c>hit.gameObject == context</c>. The context is the character root and the
		/// hit is the hitbox, so it never matched and the selector returned the caster's own collider
		/// — an outward-effecting ability resolving onto the caster, which is precisely what
		/// <c>BaseAction.TryResolveTarget</c>'s strictness exists to prevent upstream.
		/// </remarks>
		[Test]
		public void NearestSelector_ExcludesTheCasterByBody()
		{
			GameObject caster = MakeCharacterBody("nearestCaster", Vector3.zero, new Vector3(0f, 0f, 0f));
			GameObject victim = MakeLooseCollider("nearestVictim", new Vector3(3f, 0f, 0f));
			Physics.SyncTransforms();

			NearestTargetSelector selector = new NearestTargetSelector { Radius = 20f, TargetLayer = ~0 };
			List<GameObject> picked = Select(selector, caster);

			LogAssert.AreEqual(1, picked.Count, "One nearest target.");
			LogAssert.AreEqual(0, CountUnder(picked, caster),
				"Never the caster's own hitbox, which sits at distance zero and wins every ranking.");
			LogAssert.IsTrue(picked.Contains(victim), "The nearest OTHER body.");
		}

		/// <summary>The mirror of the nearest case, with the caster's hitbox offset outward.</summary>
		/// <remarks>
		/// The offset matters: a hitbox on the caster's origin is at distance zero and could never win
		/// a furthest-target ranking, so this defect only shows on a rig where the hitbox is displaced
		/// from the root — which is the rig the whole resolution question is about.
		/// </remarks>
		[Test]
		public void FurthestSelector_ExcludesTheCasterByBody()
		{
			GameObject caster = MakeCharacterBody("furthestCaster", Vector3.zero, new Vector3(8f, 0f, 0f));
			GameObject victim = MakeLooseCollider("furthestVictim", new Vector3(3f, 0f, 0f));
			Physics.SyncTransforms();

			FurthestTargetSelector selector = new FurthestTargetSelector { Radius = 20f, TargetLayer = ~0 };
			List<GameObject> picked = Select(selector, caster);

			LogAssert.AreEqual(1, picked.Count, "One furthest target.");
			LogAssert.AreEqual(0, CountUnder(picked, caster),
				"The caster's own displaced hitbox is the furthest thing in range and must still not win.");
			LogAssert.IsTrue(picked.Contains(victim), "The furthest OTHER body.");
		}

		/// <summary>
		/// A random selection draws from bodies, so the caster cannot be drawn and no body is weighted
		/// by its collider count.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two defects in one roll. The caster's own hitbox was an eligible candidate, and a body with
		/// two hitboxes occupied two entries and so came up twice as often as a single-collider one —
		/// a loaded die that no amount of determinism downstream can correct.
		/// </para>
		/// <para>
		/// <b>The roll space is explored by varying the TICK, not by seeding <c>EventData.RNG</c>.</b>
		/// This selector is server-only and deliberately does not draw from the event's shared
		/// generator — doing so advanced it on the server alone and desynchronised every later
		/// ungated draw in the same chain (see <c>RandomTargetSelector.ResolveRNG</c>). Its stream
		/// comes from <c>EventData.IndependentRNG</c>, seeded from the initiator, the event's tick
		/// and the selector's salt, so the tick is the input that moves it. Assigning
		/// <c>eventData.RNG</c> here would leave every iteration drawing the same index — which is
		/// exactly how this test caught the first, wrong version of that fix.
		/// </para>
		/// </remarks>
		[Test]
		public void RandomSelector_DrawsFromBodies()
		{
			GameObject caster = MakeCharacterBody("randomCaster", Vector3.zero, new Vector3(0f, 0f, 0f));
			GameObject twoHitbox = MakeCharacterBody("randomTwoHitbox", new Vector3(2f, 0f, 0f),
				new Vector3(0f, 0f, 0f), new Vector3(0.4f, 0f, 0f));
			GameObject single = MakeLooseCollider("randomSingle", new Vector3(4f, 0f, 0f));
			Physics.SyncTransforms();

			// QueryBufferHint sizes the buffer; MaxHits is the pool the roll draws from. Two bodies are
			// in range besides the caster, so a pool of two must hold exactly one entry each; before the
			// dedupe it held both of the two-hitbox body's colliders and the single-collider body could
			// never be drawn at all.
			RandomTargetSelector selector = new RandomTargetSelector { Radius = 20f, TargetLayer = ~0, MaxHits = 2 };

			bool everPickedSingle = false;
			for (uint tick = 0; tick < 16; ++tick)
			{
				EventData eventData = new EventData(null);
				eventData.SetTarget(caster);
				eventData.Add(new TickEventData(null, new PredictionTick(tick)));

				List<GameObject> picked = new List<GameObject>(selector.SelectTargets(eventData));
				LogAssert.AreEqual(1, picked.Count, "A random selection yields exactly one target.");
				LogAssert.AreEqual(0, CountUnder(picked, caster),
					"The caster is never drawn out of its own selection, hitbox or not.");
				everPickedSingle |= picked.Contains(single);
			}

			LogAssert.IsTrue(everPickedSingle,
				"The single-collider body is reachable. While the two-hitbox body held two entries of a " +
				"two-entry pool this was unreachable for every tick.");
		}

		// ── Chain: a link is a body, and a body is visited once ──

		/// <summary>
		/// A chain never arcs back onto a body it has already struck through another collider.
		/// </summary>
		/// <remarks>
		/// The visited set held <c>hit.gameObject</c>, so marking one hitbox left the other unmarked:
		/// the walk chained from a victim straight back onto that same victim, burning a link of
		/// <c>ChainLength</c> and never reaching the target it was authored to arc to.
		/// </remarks>
		[Test]
		public void ChainSelector_VisitsEachBodyOnce()
		{
			GameObject context = MakeContext("chainContext", Vector3.zero);
			GameObject body = MakeCharacterBody("chainTwoHitbox", new Vector3(2f, 0f, 0f),
				new Vector3(0f, 0f, 0f), new Vector3(0.4f, 0f, 0f));
			GameObject far = MakeLooseCollider("chainFar", new Vector3(4f, 0f, 0f));
			Physics.SyncTransforms();

			ChainTargetSelector selector = new ChainTargetSelector
			{
				ChainLength = 3,
				ChainRadius = 20f,
				TargetLayer = ~0,
				QueryBufferHint = 16,
			};
			List<GameObject> chain = Select(selector, context);

			LogAssert.AreEqual(3, chain.Count, "Context plus two links.");
			LogAssert.AreSame(context, chain[0], "The chain starts at its context.");
			LogAssert.AreEqual(1, CountUnder(chain, body),
				"The two-hitbox body is one link, not two.");
			LogAssert.IsTrue(chain.Contains(far),
				"So the third link reaches the far target. Before the fix the walk spent it returning " +
				"to the body it had just struck.");
		}

		// ── Resolution: what a selector RETURNS is still the collider ──

		/// <summary>
		/// The candidate a selector yields is the collider's GameObject, not the body's.
		/// </summary>
		/// <remarks>
		/// The other half of the decision, pinned so a later change cannot quietly convert the dedupe
		/// into a resolution. <see cref="EventData.SetTarget"/> is where a character is resolved, and
		/// it walks the parents — so a consumer reading <c>TargetCharacter</c> gets the character
		/// while a consumer that wants the hitbox (an impact point, a hit-zone multiplier, a scene
		/// object's specific panel) still has it.
		/// </remarks>
		[Test]
		public void Selector_YieldsTheCollider_WhileEventDataResolvesTheCharacter()
		{
			GameObject context = MakeContext("resolutionContext", Vector3.zero);
			GameObject body = MakeCharacterBody("resolutionBody", new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 0f));
			GameObject hitbox = body.transform.GetChild(0).gameObject;
			Physics.SyncTransforms();

			AreaTargetSelector selector = new AreaTargetSelector { Radius = 20f, TargetLayer = ~0, MaxHits = 8 };
			List<GameObject> picked = Select(selector, context);

			LogAssert.IsTrue(picked.Contains(hitbox),
				"The candidate is the collider that was hit, not the body it hangs off.");
			LogAssert.IsFalse(picked.Contains(body),
				"Resolving here would change what a NON-character candidate is — a door panel would " +
				"come back as the building.");

			EventData scoped = new EventData(null);
			scoped.SetTarget(hitbox);
			LogAssert.IsNotNull(scoped.TargetCharacter,
				"And the character is resolved at the consumer boundary, where every framework path " +
				"already forks through SetTarget.");
			LogAssert.AreSame(body, scoped.TargetCharacter.GameObject, "Resolved to the body, by the parent walk.");
		}

		// ── Helpers ──

		private List<GameObject> Select(TargetSelector selector, GameObject context)
		{
			EventData eventData = new EventData(null);
			eventData.SetTarget(context);
			return new List<GameObject>(selector.SelectTargets(eventData));
		}

		/// <summary>Counts how many picked candidates belong to <paramref name="root"/>'s hierarchy.</summary>
		private static int CountUnder(List<GameObject> picked, GameObject root)
		{
			int count = 0;
			for (int i = 0; i < picked.Count; ++i)
			{
				if (picked[i] != null && picked[i].transform.IsChildOf(root.transform))
				{
					++count;
				}
			}
			return count;
		}

		/// <summary>A spatial origin with no collider of its own, so it is never its own candidate.</summary>
		private GameObject MakeContext(string name, Vector3 position)
		{
			GameObject go = new GameObject(name);
			go.transform.position = position;
			spawned.Add(go);
			return go;
		}

		/// <summary>A single collider that belongs to nothing — scenery, as far as keying goes.</summary>
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
		/// <remarks>
		/// Keyed through the <see cref="ICharacter"/> on the root rather than through a
		/// <see cref="Rigidbody"/>: both are resolution paths in
		/// <c>TargetOrdering.ResolveHitRoot</c>, and the character one is the half that matters here
		/// and the half that needs no physics simulation to hold.
		/// </remarks>
		private GameObject MakeCharacterBody(string name, Vector3 position, params Vector3[] hitboxOffsets)
		{
			GameObject root = new GameObject(name);
			root.transform.position = position;
			root.AddComponent<StubBodyCharacter>();
			spawned.Add(root);

			for (int i = 0; i < hitboxOffsets.Length; ++i)
			{
				AddChildCollider(root, $"{name}Hitbox{i}", hitboxOffsets[i]);
			}
			return root;
		}

		private static GameObject AddChildCollider(GameObject root, string name, Vector3 localOffset)
		{
			GameObject child = new GameObject(name);
			child.transform.SetParent(root.transform, false);
			child.transform.localPosition = localOffset;
			SphereCollider collider = child.AddComponent<SphereCollider>();
			collider.radius = 0.3f;
			return child;
		}

		/// <summary>
		/// The smallest <see cref="ICharacter"/> that <c>GetComponentInParent</c> can find.
		/// </summary>
		/// <remarks>
		/// A MonoBehaviour rather than one of the plain-object mocks the other fixtures use, because
		/// the whole point of these tests is the parent WALK, and a walk needs a real component on a
		/// real transform to find.
		/// </remarks>
		private sealed class StubBodyCharacter : MonoBehaviour, ICharacter
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
