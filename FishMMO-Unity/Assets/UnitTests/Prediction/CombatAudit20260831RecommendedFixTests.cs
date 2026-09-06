using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using UnityLogAssert = UnityEngine.TestTools.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regressions for the ten items the 2026-08-31 round-3 audit recommended and the follow-up
	/// session implemented.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The batch: a denied predicted consumable now corrects the owner's inventory; the observer
	/// streaming target pin reads a server-verified, client-reported target frame (bounded by the
	/// engagement ceiling at use); a cull/uncull cycle reclaims detached projectile phantoms
	/// instead of doubling them; a predicted cross-character buff is provisional until the server
	/// names it; a fork's post-chain heading travels on the wire so an unresolvable victim no
	/// longer strands an observer's copy on the old line; the caster's own combat reports go
	/// reliable; periodic (DoT/HoT) reports are their own kinds and never touch the direct-hit
	/// prediction pairing; both ability collider lookups reach child hitboxes; and independent
	/// RNG streams fold the chain root's target into their seed so sibling roots decorrelate.
	/// </para>
	/// <para>
	/// What can be exercised for real is. What needs a spawned NetworkObject or a connected peer
	/// is asserted on the SOURCE, the idiom of the earlier audit fixtures, and says so.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CombatAudit20260831RecommendedFixTests
	{
		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<ScriptableObject> templates = new List<ScriptableObject>();
		private readonly List<Action> cacheCleanups = new List<Action>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < cacheCleanups.Count; ++i)
			{
				cacheCleanups[i]();
			}
			cacheCleanups.Clear();

			for (int i = 0; i < gameObjects.Count; ++i)
			{
				if (gameObjects[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(gameObjects[i]);
				}
			}
			gameObjects.Clear();

			for (int i = 0; i < templates.Count; ++i)
			{
				if (templates[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(templates[i]);
				}
			}
			templates.Clear();

			AbilityPrefabColliderCache.Clear();
		}

		// ── R3-24: periodic damage is its own merge and pairing key ──────────────────

		/// <summary>
		/// A DoT tick and a direct hit of the same type from the same source must never merge:
		/// the kind is the coalescer's key and the prediction pairing's key, and folding them
		/// together is exactly what let a DoT report consume a projectile's pending prediction.
		/// </summary>
		[Test]
		public void Coalescer_PeriodicDamage_IsItsOwnKind()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(5, CombatEventKind.Damage, 7, 10);
			coalescer.Add(5, CombatEventKind.PeriodicDamage, 7, 4);

			LogAssert.AreEqual(2, coalescer.Count,
				"Same source, same type — but a direct hit and a DoT tick are different kinds and must not merge.");

			List<CombatEventCoalescer.Entry> entries = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(entries);
			foreach (CombatEventCoalescer.Entry entry in entries)
			{
				if (entry.Kind == CombatEventKind.PeriodicDamage)
				{
					LogAssert.AreEqual(7, entry.DamageTemplateID,
						"Periodic damage keeps its damage type — the client colours DoT numbers by it.");
				}
			}
		}

		/// <summary>
		/// SOURCE assertions for the periodic plumbing that needs a networked controller: the
		/// damage/heal paths report periodic ticks under the periodic kinds, the buff DoT/HoT site
		/// passes the flag, and the display routes periodic reports straight to drawing — never
		/// into the prediction pairing, which this client only ever feeds with direct hits.
		/// </summary>
		[Test]
		public void PeriodicReports_BypassThePredictionPairing()
		{
			string damage = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");
			LogAssert.IsTrue(damage.Contains("periodic ? CombatEventKind.PeriodicDamage : CombatEventKind.Damage"),
				"Damage must report a DoT tick under its own kind.");
			LogAssert.IsTrue(damage.Contains("periodic ? CombatEventKind.PeriodicHeal : CombatEventKind.Heal"),
				"Heal must report a HoT tick under its own kind.");

			string buffTemplate = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Buff/Template/BaseBuffTemplate.cs");
			LogAssert.IsTrue(buffTemplate.Contains("periodic: true"),
				"The buff tick is the periodic caller and must say so.");

			string display = ReadSource("Assets/Scripts/Client/World/ClientCombatDisplay.cs");
			LogAssert.IsTrue(display.Contains("if (kind == CombatEventKind.PeriodicDamage)"),
				"The display must route periodic damage around TryConfirm — there is no prediction for it to settle.");
			LogAssert.IsTrue(display.Contains("if (kind == CombatEventKind.PeriodicHeal)"),
				"Same for periodic heals.");
		}

		// ── R3-19: a predicted cross-character buff is provisional ───────────────────

		/// <summary>
		/// The caster's client tracks a cross-character buff the moment its ECA predicts it. When
		/// the server refuses the apply, no correcting message ever arrives on its own — so an
		/// entry the server has not named by the confirmation deadline must be removed, exactly
		/// as an unconfirmed combat number is.
		/// </summary>
		[Test]
		public void PredictedCrossCharacterBuff_ExpiresUnconfirmed()
		{
			BuffController controller = MakeObserverBuffController(out BaseBuffTemplate template);

			controller.Apply(template, new PredictionTick(1000u));
			LogAssert.AreEqual(1, controller.Buffs.Count, "The prediction tracks immediately — that part is right.");

			SetPrivateField(controller, "lastReplicateTick", 1050u);
			InvokeObserverTick(controller);
			LogAssert.AreEqual(1, controller.Buffs.Count,
				"Inside the confirmation window the entry waits for the server.");

			SetPrivateField(controller, "lastReplicateTick", 1200u);
			InvokeObserverTick(controller);
			LogAssert.AreEqual(0, controller.Buffs.Count,
				"Past the window with no server push naming it, the phantom must go — a refused " +
				"apply produces no correction of its own, and a permanent template's phantom " +
				"otherwise survived forever.");
		}

		/// <summary>A server push naming the template confirms the prediction; the deadline is lifted.</summary>
		[Test]
		public void PredictedCrossCharacterBuff_SurvivesWhenTheServerNamesIt()
		{
			BuffController controller = MakeObserverBuffController(out BaseBuffTemplate template);

			controller.Apply(template, new PredictionTick(1000u));

			// The observed-strip materialise is what a server push lands as.
			MethodInfo materialize = typeof(BuffController).GetMethod("ApplyObservedBuffs",
				BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(materialize, "ApplyObservedBuffs must exist.");
			materialize.Invoke(controller, new object[]
			{
				new ObservedBuffEntry[]
				{
					new ObservedBuffEntry() { TemplateID = template.ID, Stacks = 0, RemainingSeconds = 30f },
				},
			});

			SetPrivateField(controller, "lastReplicateTick", 1200u);
			InvokeObserverTick(controller);
			LogAssert.AreEqual(1, controller.Buffs.Count,
				"A confirmed entry must never be swept by the prediction deadline.");
		}

		// ── R3-18: detached phantoms are reclaimed on rematerialisation ──────────────

		/// <summary>
		/// A cull detaches in-flight projectiles as phantoms; re-observation rematerialises the
		/// same cast from the payload. Reclamation must destroy exactly the matching phantoms —
		/// quietly, because the fresh copy IS the same projectile — and leave every other cast's
		/// phantoms alone.
		/// </summary>
		[Test]
		public void DetachedPhantoms_AreReclaimedByContainer()
		{
			AbilityObject reclaimed = NewAbilityObject("PhantomReclaimed");
			AbilityObject unrelated = NewAbilityObject("PhantomUnrelated");
			reclaimed.RegisterDetached(1L, 42);
			unrelated.RegisterDetached(1L, 43);

			UnityLogAssert.ignoreFailingMessages = true;
			try
			{
				AbilityObject.ReclaimDetached(1L, 42);

				LogAssert.IsTrue(reclaimed.IsDestroyed,
					"The rematerialised cast's phantom must be destroyed, or the observer renders the projectile twice.");
				LogAssert.IsFalse(unrelated.IsDestroyed,
					"A different cast's phantom is untouched.");

				// The destroyed phantom unregistered itself; a second reclaim is a no-op.
				AbilityObject.ReclaimDetached(1L, 42);

				// A phantom destroyed by its own lifetime also leaves the registry.
				unrelated.DestroyAbilityObjectInternal(dispatchDestroyEvents: false);
				AbilityObject.ReclaimDetached(1L, 43);
			}
			finally
			{
				UnityLogAssert.ignoreFailingMessages = false;
			}
		}

		// ── R3-20: an observed redirect turns only on a real change ──────────────────

		/// <summary>
		/// The redirect message reaches every peer, including the ones that already ran the fork
		/// themselves. Applying it blindly would call Redirect a second time and reset the
		/// trajectory leg — a pose jump — so a matching heading must be treated as confirmation.
		/// </summary>
		[Test]
		public void ObservedRedirect_TurnsOnlyOnARealChange()
		{
			AbilityObject abilityObject = NewAbilityObject("ObservedRedirect");
			Vector3 heading = AimDirectionCompression.Quantize(new Vector3(0.3f, 0.1f, 0.9f).normalized);
			abilityObject.SpawnRotation = AimDirectionCompression.ToRotation(heading);
			abilityObject.ElapsedTicks = 5u;

			abilityObject.ApplyObservedRedirect(heading);
			LogAssert.AreEqual(5u, abilityObject.ElapsedTicks,
				"The heading already matches — a peer that ran the fork itself must not have its leg clock reset.");

			Vector3 newHeading = AimDirectionCompression.Quantize(new Vector3(-0.7f, 0.0f, 0.7f).normalized);
			abilityObject.ApplyObservedRedirect(newHeading);
			LogAssert.AreEqual(0u, abilityObject.ElapsedTicks, "A genuine turn starts a new leg.");
			LogAssert.AreEqual(5u, abilityObject.PreRedirectElapsedTicks,
				"And the finished leg moves to the lifetime counter, as any Redirect does.");
			LogAssert.IsTrue(
				Vector3.Dot((abilityObject.SpawnRotation * Vector3.forward).normalized, newHeading.normalized) > 0.9999f,
				"The applied heading is the wire's, bit-identical to what the deciding peers simulate.");
		}

		/// <summary>
		/// SOURCE assertions for the redirect wiring that needs a server: the post-chain heading
		/// change is detected and broadcast, and the client registers the handler.
		/// </summary>
		[Test]
		public void ForkRedirects_TravelOnTheWire()
		{
			string abilityObject = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs");
			LogAssert.IsTrue(abilityObject.Contains("if (isServer && SpawnRotation != preEventHeading)"),
				"The server must detect a redirect the OnHit chain applied.");
			LogAssert.IsTrue(abilityObject.Contains("BroadcastRedirectToObservers();"),
				"And publish it — the receiver that dropped the hit skipped the fork's RNG draw and cannot re-derive it.");

			string activation = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.Activation.cs");
			LogAssert.IsTrue(activation.Contains("RegisterBroadcast<AbilityObjectRedirectBroadcast>(OnAbilityObjectRedirectBroadcast)"),
				"The client must be listening for it.");
		}

		// ── R3-23: independent streams decorrelate by chain target ───────────────────

		/// <summary>
		/// A hit or tick dispatch builds one chain root per victim; seeding from (initiator,
		/// tick, salt) alone made every sibling root walk a byte-identical sequence — a beam
		/// hitting three victims stripped correlated dispel slots. The target identity is
		/// peer-agreed, so the augmented seed is exactly as reproducible as the plain one.
		/// </summary>
		[Test]
		public void IndependentRNG_DecorrelatesSiblingRootsByTarget()
		{
			const int salt = 0x54455354; // "TEST"
			MockCharacter initiator = new MockCharacter(3);
			MockCharacter victimA = new MockCharacter(7);
			MockCharacter victimB = new MockCharacter(8);

			EventData rootA = new EventData(initiator, victimA);
			EventData rootB = new EventData(initiator, victimB);
			EventData rootA2 = new EventData(initiator, victimA);

			int drawA = rootA.IndependentRNG(salt).Next();
			int drawB = rootB.IndependentRNG(salt).Next();
			int drawA2 = rootA2.IndependentRNG(salt).Next();

			LogAssert.IsTrue(drawA != drawB,
				"Two sibling roots differing only by victim must walk different sequences.");
			LogAssert.AreEqual(drawA, drawA2,
				"And the augmented seed is still deterministic: the same (initiator, target, salt) reproduces exactly.");
		}

		// ── R3-22: both collider lookups reach child hitboxes ────────────────────────

		/// <summary>
		/// A prefab authored with its hitbox on a child used to be found by NEITHER lookup, and
		/// the sweep silently degraded to a ray on every peer. Both lookups now search children,
		/// with the root still winning when both exist, and the cache's identity self-heal judges
		/// by the collider's root so a child entry is not evicted as \"stale\" on every call.
		/// </summary>
		[Test]
		public void ColliderLookups_ReachChildHitboxes()
		{
			GameObject prefabRoot = new GameObject("ChildHitboxRoot");
			gameObjects.Add(prefabRoot);
			GameObject child = new GameObject("Hitbox");
			child.transform.SetParent(prefabRoot.transform);
			BoxCollider childCollider = child.AddComponent<BoxCollider>();

			AbilityTemplate template = NewTemplate<AbilityTemplate>("Audit0831Rec_ChildHitbox");
			template.AddToCache(template.name);
			cacheCleanups.Add(() => template.RemoveFromCache());
			template.AbilityObjectPrefab = prefabRoot;

			Collider first = AbilityPrefabColliderCache.GetPrefabCollider(template);
			LogAssert.IsTrue(ReferenceEquals(first, childCollider),
				"The prefab cache must find a child hitbox.");
			Collider second = AbilityPrefabColliderCache.GetPrefabCollider(template);
			LogAssert.IsTrue(ReferenceEquals(second, childCollider),
				"And serve it from the cache without the identity self-heal evicting it as stale.");

			// The instance lookup must agree with the prefab lookup, or peers sweep different
			// shapes. Awake does not run for a plain MonoBehaviour in edit mode, so the test
			// drives CacheComponents the way initialisation does.
			AbilityObject abilityObject = prefabRoot.AddComponent<AbilityObject>();
			MethodInfo cacheComponents = typeof(AbilityObject).GetMethod("CacheComponents",
				BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(cacheComponents, "CacheComponents must exist.");
			cacheComponents.Invoke(abilityObject, null);
			FieldInfo sweepShape = typeof(AbilityObject).GetField("sweepShape", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(sweepShape, "The sweep shape field must exist.");
			LogAssert.IsTrue(ReferenceEquals(sweepShape.GetValue(abilityObject), childCollider),
				"CacheComponents must resolve the same child hitbox on the instance.");
		}

		// ── R3-16 / R3-21 / R3-17: server-path source assertions ─────────────────────

		/// <summary>
		/// SOURCE — the consumable denial correction needs a connected owner. The owner predicted
		/// the whole use including the inventory mutation; a denial fires no OnConsumableUsed, so
		/// the correction must hang off the denial itself.
		/// </summary>
		[Test]
		public void DeniedConsumables_CorrectTheOwnersInventory()
		{
			string controller = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityController.cs");
			LogAssert.IsTrue(controller.Contains("SendConsumableDenialCorrection(activationData.QueuedAbilityID);"),
				"The denial site must trigger the correction.");
			LogAssert.IsTrue(controller.Contains("private void SendConsumableDenialCorrection(long queuedAbilityID)"),
				"The correction must exist.");
			LogAssert.IsTrue(controller.Contains("new InventorySetItemBroadcast()"),
				"And it sends the slot's authoritative state through the inventory channel the client already handles.");
		}

		/// <summary>
		/// SOURCE — the caster's own combat report goes reliable, because absence of a report is
		/// the prediction system's only rejection signal and a lost packet greyed out a landed hit.
		/// Observers stay unreliable; a lost cosmetic number is still not worth a resend.
		/// </summary>
		[Test]
		public void CombatReports_AreReliableToTheSourceOwner()
		{
			string damage = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");
			LogAssert.IsTrue(damage.Contains("Channel channel = sourceOwner != null && conn == sourceOwner"),
				"The per-connection channel split must key on owning the entry's source.");
			LogAssert.IsTrue(damage.Contains("? Channel.Reliable"),
				"Reliable for the caster whose predictions this report settles.");
			LogAssert.IsTrue(damage.Contains(": Channel.Unreliable"),
				"Unreliable for everyone else, as before.");
		}

		/// <summary>
		/// SOURCE — the target-selection pipeline needs two connected peers. The client reports
		/// its target frame on change (rate-limited), the server verifies the claim resolves to a
		/// live character in the sender's own scene, the streaming entry prefers the verified
		/// report over the cast-scoped acquisition target, and the pin is bounded by the
		/// engagement ceiling at use so a forged id can hold nothing distant.
		/// </summary>
		[Test]
		public void TargetSelection_IsReportedVerifiedAndBounded()
		{
			string targetController = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Target/TargetController.cs");
			LogAssert.IsTrue(targetController.Contains("MaybeReportTargetSelection(!ReferenceEquals(pinnedTarget, null) ? pinnedTarget : resolvedTarget);"),
				"The owner's trace tick must report frame changes, preferring a pinned target over the hovered one.");
			LogAssert.IsTrue(targetController.Contains("TARGET_SELECTION_SEND_INTERVAL"),
				"Rate-limited — a mouse sweeping a crowd changes targets faster than the server has any use for.");

			string connection = ReadSource(
				"Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Connection.cs");
			LogAssert.IsTrue(connection.Contains("targetNob.gameObject.scene == player.GameObject.scene"),
				"The server must verify the claimed target lives in the sender's own scene.");
			LogAssert.IsTrue(connection.Contains("targetController.ServerSetClientSelectedTarget(verifiedTargetId);"),
				"And install only the verified value — a failed claim stores no-target, never the raw id.");

			string entry = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingEntry.cs");
			LogAssert.IsTrue(entry.Contains("targetController.HasClientSelectedTarget"),
				"The streaming entry must prefer the reported frame — the cast-scoped Current does not exist before the first landed cast.");

			string registry = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingRegistry.cs");
			int targetClause = registry.IndexOf("observed.NetworkObject.ObjectId == viewerTargetId &&", StringComparison.Ordinal);
			LogAssert.IsTrue(targetClause >= 0, "The target pin clause must exist.");
			int bound = registry.IndexOf("distance <= ObserverStreamingPolicy.EngagementRangeCeiling", targetClause, StringComparison.Ordinal);
			LogAssert.IsTrue(bound >= 0 && bound - targetClause < 200,
				"And it must be distance-bounded at use, now that the id can be client-reported.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		private static void SetPrivateField<T>(object instance, string fieldName, T value)
		{
			FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(field, $"Private field '{fieldName}' not found on {instance.GetType().Name}.");
			field.SetValue(instance, value);
		}

		private static void InvokeObserverTick(BuffController controller)
		{
			MethodInfo tick = typeof(BuffController).GetMethod("ObserverTimeManager_OnTick",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(tick, "ObserverTimeManager_OnTick must exist.");
			tick.Invoke(controller, null);
		}

		/// <summary>
		/// A buff controller shaped like the caster's client view of SOMEBODY ELSE: unspawned, so
		/// it neither simulates nor is the owner, with a replicate-domain clock the test can move.
		/// </summary>
		private BuffController MakeObserverBuffController(out BaseBuffTemplate template)
		{
			ProvisionalBuffTemplate created = ScriptableObject.CreateInstance<ProvisionalBuffTemplate>();
			created.name = "Audit0831Rec_ProvisionalBuff";
			created.Duration = 60f;
			created.AddToCache(created.name);
			templates.Add(created);
			cacheCleanups.Add(() => created.RemoveFromCache());
			template = created;

			GameObject go = new GameObject("ObserverBuffController");
			gameObjects.Add(go);
			BuffController controller = go.AddComponent<BuffController>();
			SetPrivateField(controller, "tickDelta", 1f / 30f);
			SetPrivateField(controller, "lastReplicateTick", 1000u);
			SetPrivateField(controller, "hasSeenFirstReplicate", true);
			controller.InitializeOnce(new MockCharacter(11));
			LogAssert.IsFalse(controller.SimulatesBuffEffects,
				"Precondition: this controller must be the tracking-only peer the TTL exists for.");
			return controller;
		}

		private T NewTemplate<T>(string name) where T : ScriptableObject
		{
			T template = ScriptableObject.CreateInstance<T>();
			template.name = name;
			templates.Add(template);
			return template;
		}

		private AbilityObject NewAbilityObject(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			return go.AddComponent<AbilityObject>();
		}

		private sealed class ProvisionalBuffTemplate : BaseBuffTemplate
		{
			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }
		}

		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id) => ID = id;
			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
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
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
