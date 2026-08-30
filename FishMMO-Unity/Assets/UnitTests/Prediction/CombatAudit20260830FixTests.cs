using System;
using System.Reflection;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using FishNet.Serializing;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regressions for the fixes applied after the 2026-08-30 combat/prediction audit.
	/// </summary>
	/// <remarks>
	/// Every defect pinned here was silent: an action that ran nowhere, a cap spent on the shooter
	/// itself, an observer applying bonuses it had already been sent, an order that held only while
	/// a sort happened to be stable. None produced an error or a log line, and three of them were
	/// inert only because of an unrelated accident elsewhere — which is the state a fix has to be
	/// pinned out of, because the accident is what moves next.
	/// </remarks>
	[TestFixture]
	public class CombatAudit20260830FixTests
	{
		private const BindingFlags Any =
			BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		#region F1 — an area effect resolves its object from every ability event shape.

		/// <summary>
		/// The shared resolver reads all three ability event payloads, not only the collision one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>AbilityApplyAreaAction</c> tested <see cref="AbilityCollisionEventData"/> and nothing
		/// else, so an area blast authored on <c>OnSpawn</c> or <c>OnTick</c> returned having done
		/// nothing, on every peer. That is precisely the failure an earlier fix to the same action
		/// believed it had removed — it corrected the PEER gate (a tick-domain test that suppressed
		/// the server too) and left the PAYLOAD gate that was the other half of the same bug, with a
		/// comment asserting the case now worked.
		/// </para>
		/// <para>
		/// Latent today: the asset census finds zero authored <c>Ability*Action</c> assets. It goes
		/// live the moment anyone authors one.
		/// </para>
		/// </remarks>
		[Test]
		public void AbilityObjectResolution_AcceptsSpawnTickAndCollisionPayloads()
		{
			GameObject host = new GameObject("ResolveFrom_Object");
			try
			{
				AbilityObject abilityObject = host.AddComponent<AbilityObject>();

				LogAssert.IsFalse(AbilityObject.TryResolveFrom(null, out _),
					"A null event resolves nothing rather than throwing.");

				LogAssert.IsFalse(AbilityObject.TryResolveFrom(new EventData(null), out _),
					"An event carrying no ability payload resolves nothing.");

				EventData spawn = new EventData(null);
				spawn.Add(new AbilitySpawnEventData(
					initiator: null, ability: null, abilitySpawner: null, targetInfo: default,
					seed: 0, initialAbilityObject: abilityObject,
					currentAbilityObjectID: null, spawnedAbilityObjects: null));
				LogAssert.IsTrue(AbilityObject.TryResolveFrom(spawn, out AbilityObject fromSpawn),
					"OnSpawn must resolve. An area effect wired here used to run on no peer at all.");
				LogAssert.AreSame(abilityObject, fromSpawn, "...to the object that spawned.");

				EventData tick = new EventData(null);
				tick.Add(new AbilityTickEventData(null, 1f / 30f, abilityObject));
				LogAssert.IsTrue(AbilityObject.TryResolveFrom(tick, out AbilityObject fromTick),
					"OnTick must resolve, so a lingering field can reapply its effect each tick.");
				LogAssert.AreSame(abilityObject, fromTick, "...to the ticking object.");

				EventData hit = new EventData(null);
				hit.Add(new AbilityCollisionEventData(null, null, abilityObject, Vector3.zero, Vector3.up, null));
				LogAssert.IsTrue(AbilityObject.TryResolveFrom(hit, out AbilityObject fromHit),
					"OnHit must resolve — the one shape that always worked.");
				LogAssert.AreSame(abilityObject, fromHit, "...to the object that hit.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
		}

		/// <summary>
		/// Both physics actions resolve through the one helper, so they cannot disagree again.
		/// </summary>
		/// <remarks>
		/// The two actions answer the same question — "which ability object am I acting for?" — and
		/// having answered it separately is how one of them ended up accepting three payloads while
		/// the other accepted one. Asserted structurally because the divergence is what matters, not
		/// either implementation.
		/// </remarks>
		[Test]
		public void BothPhysicsActions_ShareOneAbilityObjectResolver()
		{
			foreach (Type action in new[] { typeof(AbilityApplyAreaAction), typeof(AbilityApplyHitscanAction) })
			{
				LogAssert.IsNull(action.GetMethod("TryGetAbilityObject", Any),
					$"{action.Name} must not carry a private copy of the resolver. Two copies is how " +
					"the two actions came to accept different sets of event payloads.");
			}

			LogAssert.IsNotNull(typeof(AbilityObject).GetMethod("TryResolveFrom", Any),
				"AbilityObject.TryResolveFrom is the shared implementation both actions use.");
		}

		#endregion

		#region F2 — the observer spawn payload equips without applying bonuses.

		/// <summary>
		/// Reading an OBSERVER-shaped equipment payload must not run the item's attribute apply;
		/// reading an OWNER-shaped one must.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>ReadPayload</c> called <c>ItemEquippable.Equip</c> for BOTH shapes.
		/// <c>Equip</c> raises <c>OnEquip</c>, which runs <c>ItemGenerator.ApplyAttributes</c> — and
		/// the observer's attribute payload from the same spawn already carries the server's TOTAL
		/// <c>ExternalModifier</c>, which by construction contains every equipped item. That is the
		/// identical double-apply <c>ApplyObservedSlot</c> was rewritten to remove for the broadcast
		/// path, and the rule <c>SimulatesEquipmentEffects</c> states; only the payload path was
		/// never brought into line.
		/// </para>
		/// <para>
		/// <b>It was inert, not harmless.</b> The observer shape omits the item id, so
		/// <c>ItemGenerator.TryResolveLedgerSource</c> declined for a zero id and wrote no ledger
		/// entry. An accident of the wire format was standing in for a rule, and it would have become
		/// a live double-apply the moment the observer payload carried an id — so the difference
		/// cannot be measured on the attribute value, and is measured here on whether the apply path
		/// RAN at all.
		/// </para>
		/// <para>
		/// The probe is <c>ICharacter.TryGet</c>, which <c>ApplyAttributes</c> calls unconditionally
		/// before it looks at anything else. Both receivers read a payload written from the same item
		/// in the same slot, so the only difference between them is the shape byte and the one call
		/// the equip handler makes.
		/// </para>
		/// </remarks>
		[Test]
		public void ObserverEquipmentPayload_PointsTheItemAtTheCharacter_WithoutApplyingItsBonuses()
		{
			GameObject senderHost = new GameObject("F2_Sender");
			GameObject ownerHost = new GameObject("F2_OwnerReceiver");
			GameObject observerHost = new GameObject("F2_ObserverReceiver");
			FixArmorTemplate template = ScriptableObject.CreateInstance<FixArmorTemplate>();

			try
			{
				template.name = "F2_GeneratedArmor";
				template.Slot = ItemSlot.Chest;
				// Generated, so the item wires ItemEquippable_OnEquip -> Generator.ApplyAttributes.
				// That handler is the entire difference between the two paths.
				template.Generate = true;
				/* Explicitly empty rather than left null. Unity serialises an authored asset's list
				 * as empty, never null, so ItemGenerator.AddAdditionalTemplateAttributes iterates it
				 * unguarded — a CreateInstance template would otherwise NRE inside the constructor
				 * for a reason that has nothing to do with what is being tested. */
				template.Attributes = new System.Collections.Generic.List<ItemAttributeTemplate>();
				template.RandomAttributeDatabases = new ItemAttributeTemplateDatabase[0];
				template.AddToCache(template.name);

				EquipmentController sender = senderHost.AddComponent<EquipmentController>();
				// OnAwake is what allocates the slot array; AddComponent alone does not run it here.
				sender.OnAwake();
				sender.InitializeOnce(new CountingCharacter());

				// Id zero on purpose: the observer shape does not carry one, so writing zero keeps
				// the two payloads byte-identical apart from the shape flag.
				Item item = new Item(0L, seed: 12345, template, 1u);
				LogAssert.IsTrue(item.IsGenerated, "The probe needs a generated item — that is what wires OnEquip.");
				LogAssert.IsTrue(item.IsEquippable, "...and an equippable one.");
				LogAssert.IsTrue(sender.SetItemSlot(item, (int)ItemSlot.Chest),
					"The sender must actually hold the item, or both payloads are empty and the test proves nothing.");

				int ownerCalls = ReadShapeAndCountAttributeLookups(sender, ownerHost, ownerShape: true, out Item ownerItem);
				int observerCalls = ReadShapeAndCountAttributeLookups(sender, observerHost, ownerShape: false, out Item observerItem);

				LogAssert.IsNotNull(ownerItem, "The owner must end up holding the item.");
				LogAssert.IsNotNull(observerItem, "The observer must end up holding the item — it needs the mesh.");

				LogAssert.IsNotNull(observerItem.Equippable.Character,
					"The observer's item must still be POINTED at the character. ItemGenerator.SetAttribute " +
					"reads Equippable.Character to decide whether a later change has anywhere to go, so " +
					"suppressing the bonuses must not also drop the owner link.");

				LogAssert.AreEqual(ownerCalls - 1, observerCalls,
					$"The owner ran the attribute apply and the observer must not: owner made {ownerCalls} " +
					$"controller lookups, observer {observerCalls}. Equal counts mean the observer is " +
					"calling Equip for real and applying item bonuses on top of the server total it was " +
					"just sent in the same spawn.");
			}
			finally
			{
				template.RemoveFromCache();
				UnityEngine.Object.DestroyImmediate(template);
				UnityEngine.Object.DestroyImmediate(senderHost);
				UnityEngine.Object.DestroyImmediate(ownerHost);
				UnityEngine.Object.DestroyImmediate(observerHost);
			}
		}

		/// <summary>
		/// Writes <paramref name="sender"/>'s equipment in one shape, reads it into a fresh
		/// controller, and reports how many times the receiving character was asked for its attribute
		/// controller.
		/// </summary>
		private static int ReadShapeAndCountAttributeLookups(
			EquipmentController sender, GameObject host, bool ownerShape, out Item received)
		{
			Writer writer = new Writer();
			sender.WritePayload(writer, ownerShape);

			CountingCharacter character = new CountingCharacter();
			EquipmentController receiver = host.AddComponent<EquipmentController>();
			receiver.OnAwake();
			receiver.InitializeOnce(character);

			character.AttributeControllerLookups = 0;
			Reader reader = new Reader(writer.GetArraySegment(), null);
			receiver.ReadPayload(null, reader);

			LogAssert.AreEqual(0, reader.Remaining,
				"The framed block must be consumed exactly, or every behaviour after this one desyncs.");

			received = receiver.Items[(int)ItemSlot.Chest];
			return character.AttributeControllerLookups;
		}

		/// <summary>An equippable, generatable template with nothing else on it.</summary>
		private sealed class FixArmorTemplate : EquippableItemTemplate { }

		/// <summary>
		/// Counts requests for the attribute controller, which is the first thing
		/// <c>ItemGenerator.ApplyAttributes</c> does and therefore the cheapest evidence that it ran.
		/// </summary>
		private sealed class CountingCharacter : ICharacter
		{
			public int AttributeControllerLookups;

			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				if (typeof(T) == typeof(ICharacterAttributeController))
				{
					++AttributeControllerLookups;
				}
				control = null;
				return false;
			}

			public long ID { get; set; } = 1L;
			public string Name => "F2Character";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => null;
			public FishNet.Managing.Predicting.PredictionManager PredictionManager => null;
			public System.Collections.Generic.HashSet<FishNet.Connection.NetworkConnection> Observers { get; }
				= new System.Collections.Generic.HashSet<FishNet.Connection.NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
			public Transform MeshRoot => null;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void Invoke(System.Collections.Generic.List<Trigger> triggers, EventData eventData) { }
		}

		#endregion

		#region F3 — the shooter is excluded before the cap is charged, not after.

		/// <summary>
		/// <c>RaycastNearest</c> takes the transform to exclude, so a projectile's own collider
		/// cannot consume a slot of <c>MaxHits</c>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The hitscan action skipped its own collider AFTER the query returned, by which point the
		/// cap had already been spent on it — so <c>MaxHits = 1</c> with <c>BlockedByScenery</c> on
		/// produced a shot that hit nothing at all. The query has always applied
		/// <c>charactersOnly</c> before the cap for exactly this reason; the exclusion now sits
		/// beside it.
		/// </para>
		/// <para>
		/// Masked in the shipped content by two unrelated accidents — the authored ability prefabs
		/// centre a convex collider on the transform origin, and <c>m_QueriesHitBackfaces</c> is off,
		/// so a ray starting inside does not report it. An offset collider or a collider on a child
		/// visual removes both. Pinned on the signature because a physics scene is not available in
		/// edit mode.
		/// </para>
		/// </remarks>
		[Test]
		public void RaycastNearest_TakesAnExclusionThatIsAppliedInsideTheCap()
		{
			MethodInfo raycast = typeof(LagCompensatedQuery).GetMethod("RaycastNearest", Any);
			LogAssert.IsNotNull(raycast, "RaycastNearest must exist.");

			ParameterInfo[] parameters = raycast.GetParameters();
			ParameterInfo ignoreRoot = Array.Find(parameters, p => p.Name == "ignoreRoot");
			LogAssert.IsNotNull(ignoreRoot,
				"RaycastNearest must accept the transform to exclude. Filtering it out in the CALLER " +
				"leaves the cap already charged for it, which is a shot that hits nothing at MaxHits 1.");
			LogAssert.AreEqual(typeof(Transform), ignoreRoot.ParameterType, "...as a Transform.");
			LogAssert.IsTrue(ignoreRoot.IsOptional,
				"Optional, so the callers that have nothing to exclude are unchanged.");

			MethodInfo gather = typeof(LagCompensatedQuery).GetMethod("GatherAlongRay", Any);
			LogAssert.IsNotNull(gather, "GatherAlongRay must exist.");
			LogAssert.IsNotNull(Array.Find(gather.GetParameters(), p => p.Name == "ignoreRoot"),
				"...and the exclusion must reach the loop that charges the cap, not stop at the " +
				"public entry point.");
		}

		#endregion

		#region F4 — the ray comparator is a total order.

		/// <summary>
		/// <c>SortRaycastHits</c> orders by distance, then identity, then buffer position — total,
		/// rather than relying on the sort being stable.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The residual case is two colliders on ONE body sharing a name and a millimetre, which has
		/// no cross-peer agreed key and cannot be given one — buffer position is the broadphase's
		/// order and differs between peers. What the tiebreak buys is that the outcome no longer
		/// depends on the insertion sort staying an insertion sort: swapping in <c>Array.Sort</c>
		/// would otherwise have made an already-arbitrary case vary run to run as well.
		/// </para>
		/// <para>
		/// Exercised through the public entry point on a hit set the comparator can actually
		/// separate, so the ordering property is asserted rather than the comparator's shape.
		/// </para>
		/// </remarks>
		[Test]
		public void SortRaycastHits_OrdersByDistance_AndIsTotal()
		{
			MethodInfo compare = typeof(TargetOrdering).GetMethod("CompareKeys", Any);
			LogAssert.IsNotNull(compare, "CompareKeys must exist.");
			LogAssert.AreEqual(10, compare.GetParameters().Length,
				"CompareKeys must carry a buffer-position key for each side (5 keys x 2) so the order " +
				"is total. Without it two entries the identity keys cannot separate compare EQUAL, " +
				"and the result is whatever order the sort happened to leave them in.");

			// Distances are the part a physics-free test can drive: RaycastHit.distance is settable.
			RaycastHit[] hits = new RaycastHit[4];
			hits[0].distance = 9f;
			hits[1].distance = 1f;
			hits[2].distance = 5f;
			hits[3].distance = 3f;

			TargetOrdering.SortRaycastHits(hits, 4);

			LogAssert.AreEqual(1f, hits[0].distance, "Nearest first — a ray is a sequence.");
			LogAssert.AreEqual(3f, hits[1].distance, "...then the next along it.");
			LogAssert.AreEqual(5f, hits[2].distance, "...and the next.");
			LogAssert.AreEqual(9f, hits[3].distance, "...furthest last.");
		}

		/// <summary>
		/// Sorting an already-sorted set changes nothing, at any length the key buffers have to grow
		/// through.
		/// </summary>
		/// <remarks>
		/// The parallel key arrays are grown by doubling and are shared statics, so a mismatch
		/// between the arrays and the hit count would corrupt an unrelated later query rather than
		/// this one. Walking past the 32-entry starting size is what exercises that.
		/// </remarks>
		[Test]
		public void SortRaycastHits_IsIdempotent_AcrossABufferGrowth()
		{
			const int count = 70;
			RaycastHit[] hits = new RaycastHit[count];
			for (int i = 0; i < count; ++i)
			{
				hits[i].distance = count - i;
			}

			TargetOrdering.SortRaycastHits(hits, count);
			for (int i = 1; i < count; ++i)
			{
				LogAssert.IsTrue(hits[i - 1].distance <= hits[i].distance,
					$"Entry {i} is out of order after the first sort; the key buffers did not grow " +
					"in step with the hit count.");
			}

			TargetOrdering.SortRaycastHits(hits, count);
			for (int i = 1; i < count; ++i)
			{
				LogAssert.IsTrue(hits[i - 1].distance <= hits[i].distance,
					$"Entry {i} moved on a second sort of an already-sorted set — the comparator is " +
					"not a consistent order.");
			}
		}

		#endregion

		#region F5 — one character's delta-chain break cannot silence another's.

		/// <summary>
		/// The chain-break report is throttled by a count, not latched and cleared by any character's
		/// next good packet.
		/// </summary>
		/// <remarks>
		/// The delta reader is a static registered against the type: it sees every character's
		/// reconciles and <c>ReadDelta</c> receives only a reader and the previous state, so it has
		/// no identity to attribute a gap to. The boolean latch therefore coupled unrelated objects —
		/// one character's break claimed the single report and ANY character's next good delta
		/// cleared it, so in a busy scene a real gap was usually swallowed by a neighbour.
		/// </remarks>
		[Test]
		public void ChainBreakReporting_IsNotLatchedAcrossCharacters()
		{
			Type serializer = typeof(CharacterReconcileDataDeltaSerializer);

			LogAssert.IsNull(serializer.GetField("chainBreakLogged", Any),
				"The boolean latch must be gone: it let one character's recovery decide whether " +
				"another character's lost state update was ever reported.");

			FieldInfo counter = serializer.GetField("chainBreaksSinceReport", Any);
			LogAssert.IsNotNull(counter,
				"A counting throttle replaces it, so the first gap always reports and a storm is " +
				"still bounded.");
			LogAssert.AreEqual(typeof(int), counter.FieldType, "...counted, not latched.");

			FieldInfo interval = serializer.GetField("CHAIN_BREAK_REPORT_INTERVAL", Any);
			LogAssert.IsNotNull(interval, "The throttle interval must be named rather than inline.");
			LogAssert.IsTrue((int)interval.GetValue(null) > 1,
				"An interval of one is no throttle at all on a channel that can drop many packets " +
				"in a row.");
		}

		#endregion

		#region F6 — one grow-on-full helper, so a saturated sweep is reported.

		/// <summary>
		/// <c>AbilityObjectSweep</c> grows its buffers through <c>TargetOrdering</c> rather than a
		/// private copy of the same loop.
		/// </summary>
		/// <remarks>
		/// The copy was behaviourally identical — the same doubling against the same 256 ceiling —
		/// and that was the problem: it was identical except for the one thing the shared helper also
		/// does, which is report a saturated query once per session. A sweep that filled its buffer
		/// truncated in broadphase order and said nothing, while every other spatial query in the
		/// project said so.
		/// </remarks>
		[Test]
		public void AbilityObjectSweep_UsesTheSharedBufferGrowth()
		{
			LogAssert.IsNull(typeof(AbilityObjectSweep).GetField("MaximumBufferSize", Any),
				"The private ceiling must be gone; TargetOrdering.MaximumQueryBufferSize is the one " +
				"the shared helper enforces, and a second constant is a second thing to keep in step.");

			// The shared helper is grow-only and stops at the shared ceiling — the properties the
			// sweep now inherits rather than reimplements.
			Collider[] buffer = null;
			LogAssert.IsTrue(TargetOrdering.TryGrowQueryBuffer(ref buffer, 0),
				"A null buffer is allocated on first use.");
			int allocated = buffer.Length;

			LogAssert.IsFalse(TargetOrdering.TryGrowQueryBuffer(ref buffer, allocated - 1),
				"A query that did not fill the buffer needs no growth and no re-query.");
			LogAssert.AreEqual(allocated, buffer.Length,
				"...and the buffer must not shrink back: growth is one-way, or every cast undoes the " +
				"previous one's growth.");

			LogAssert.IsTrue(TargetOrdering.TryGrowQueryBuffer(ref buffer, allocated),
				"A full buffer says nothing about what it discarded, so it must grow and re-query.");
			LogAssert.IsTrue(buffer.Length > allocated, "...by actually growing.");
		}

		#endregion

		#region F9 — the AI heal choice is reproducible.

		/// <summary>
		/// A healer picking between equally hurt allies breaks the tie on network identity.
		/// </summary>
		/// <remarks>
		/// A group at full health is the common case for a healer scan, not a corner one, so a strict
		/// health comparison alone made the choice depend on overlap order — which is not
		/// reproducible between two runs of the same fight. Server-side, so this buys repeatability
		/// rather than cross-peer agreement; a heal that lands on a different ally each time a
		/// scenario is replayed is not debuggable.
		/// </remarks>
		[Test]
		public void HealTargetSelection_BreaksTiesOnIdentity()
		{
			MethodInfo identityKey = typeof(HealerAttackingState).GetMethod("IdentityKey", Any);
			LogAssert.IsNotNull(identityKey,
				"The healer must have a stable key to break a health tie with, or equally hurt " +
				"allies are chosen between by broadphase order.");

			LogAssert.AreEqual(int.MaxValue, identityKey.Invoke(null, new object[] { null }),
				"An unspawned or absent candidate must sort LAST, so it can never win a tie against " +
				"a real ally.");
		}

		#endregion
	}
}
