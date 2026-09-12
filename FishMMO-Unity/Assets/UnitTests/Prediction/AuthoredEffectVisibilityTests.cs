using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishNet.Object.Prediction;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the ways an authored ECA effect could be dispatched, resolved, spawned and still be seen
	/// by nobody — or by everybody, forever.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The first four share a shape: a guard or a payload test that is correct in isolation and, in the
	/// dispatch it actually receives, silently answers "do nothing". <c>PlayFXAction</c> demanded a
	/// payload type that no destroy, spawn or tick dispatch has; the selector fan-out ran an action
	/// once per selected target and so ran it zero times on the peer that is not allowed to select;
	/// <c>AbilityApplyTargetAction</c> demanded the one payload shape that makes it recurse; and
	/// <c>BaseAction.IsReplayTick</c> reads a flag no dispatch was in a position to set.
	/// </para>
	/// <para>
	/// The fifth is the same action and the opposite failure: the effect REACHED everyone, and then
	/// never left. The action spawned an instance it did not own, in a scene it did not choose, and
	/// left its ending to whatever the prefab happened to do — which for the fire FX the three fire
	/// abilities play was nothing at all (issue #258, and issue #269 for the same instances surviving
	/// a scene change).
	/// </para>
	/// <para>
	/// The pure functions below are the load-bearing assertions. Each replaced an inline condition
	/// precisely so its truth table could be asserted without a NetworkManager, a spawned object or
	/// a scene — the same reason <c>EcaAuthority.Allows</c> and
	/// <c>AbilityObject.ResolvesHitsOnThisPeer</c> exist.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AuthoredEffectVisibilityTests
	{
		private readonly List<Object> tracked = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < tracked.Count; ++i)
			{
				if (tracked[i] != null)
				{
					Object.DestroyImmediate(tracked[i]);
				}
			}
			tracked.Clear();
		}

		private T Track<T>(T obj) where T : Object
		{
			tracked.Add(obj);
			return obj;
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"Expected source at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>
		/// A bare ability object with no caster. That is the OBSERVER case: <c>isServer</c> is false
		/// and there is no caster to own, so <c>ResolvesHitsLocally</c> answers false and every
		/// selector built on it declines — which is exactly the peer defect 2 is about.
		/// </summary>
		private AbilityObject MakeAbilityObject(Vector3 position, bool isServer = false)
		{
			GameObject go = Track(new GameObject("ProbeAbilityObject"));
			go.transform.position = position;
			AbilityObject abilityObject = go.AddComponent<AbilityObject>();

			/* Prime the cached GameObject/Transform/Rigidbody references the way production does.
			 * AbilityObject caches them in CacheComponents, called from Awake — which Unity does not
			 * run for a component added in edit mode — and again from initialisation, because Spawn
			 * deactivates the instance before adding a missing component. Without this the probe
			 * object has a null Transform, so anything reading it resolves nothing and the test
			 * reports a defect the shipped path does not have. */
			typeof(AbilityObject)
				.GetMethod("CacheComponents", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(abilityObject, null);

			if (isServer)
			{
				typeof(AbilityObject)
					.GetField("isServer", BindingFlags.Instance | BindingFlags.NonPublic)
					.SetValue(abilityObject, true);
			}
			return abilityObject;
		}

		// ── Defect 1: PlayFXAction could never fire from a destroy, spawn or tick event ──────────

		/// <summary>
		/// The placement precedence, as a table.
		/// </summary>
		/// <remarks>
		/// The middle two rows are the ones that were wrong. The ability object row is the fix: the
		/// action used to require a <c>CollisionEventData</c>, and <c>AbilityCollisionEventData</c> is
		/// the only ability payload that is one — so the FX on <i>Fire Impact FX Event</i> (Lesser
		/// Fireball, Orc Firebolt, Scroll of Flame Impact) instantiated nothing on any peer, ever,
		/// and logged a warning per destroyed object per peer while doing it. The event-target row
		/// has to sit ABOVE it so a fan-out over a selector still places one effect per victim
		/// rather than stacking them all on the projectile.
		/// </remarks>
		[Test]
		public void PlayFX_ChoosesTheMostSpecificPositionAvailable()
		{
			LogAssert.AreEqual(PlayFXAction.FXOrigin.CollisionPoint,
				PlayFXAction.ChooseOrigin(true, true, true, true),
				"A resolved impact point is the only source that knows where on a body the hit landed.");
			LogAssert.AreEqual(PlayFXAction.FXOrigin.EventTarget,
				PlayFXAction.ChooseOrigin(false, true, true, true),
				"The event's own scope beats the ability object, so a fan-out places one effect per victim.");
			LogAssert.AreEqual(PlayFXAction.FXOrigin.AbilityObject,
				PlayFXAction.ChooseOrigin(false, false, true, true),
				"A destroy, spawn or tick dispatch resolves through the ability object — the branch that " +
				"did not exist, and the whole of defect 1.");
			LogAssert.AreEqual(PlayFXAction.FXOrigin.Initiator,
				PlayFXAction.ChooseOrigin(false, false, false, true),
				"The caster is the last resort for an event with no spatial content.");
			LogAssert.AreEqual(PlayFXAction.FXOrigin.None,
				PlayFXAction.ChooseOrigin(false, false, false, false),
				"And an event that can name no position plays nothing; the world origin is never a place " +
				"an authored effect belongs.");
		}

		/// <summary>
		/// The three ability dispatches that used to resolve nothing now resolve the object.
		/// </summary>
		/// <remarks>
		/// None of these payloads derives from <c>CollisionEventData</c>, which is why the old
		/// implementation took its else branch and warned. The destroy payload additionally sets
		/// <c>Target</c> to the dying object on purpose, so it resolves through the event-target row
		/// to the same position — the two agree, which is what makes the precedence safe.
		/// </remarks>
		[Test]
		public void PlayFX_ResolvesAPositionFromDestroySpawnAndTickDispatches()
		{
			Vector3 where = new Vector3(11f, 2f, -7f);
			AbilityObject abilityObject = MakeAbilityObject(where);

			LogAssert.IsTrue(
				PlayFXAction.TryResolveSpawnPosition(null, new AbilityDestroyEventData(null, abilityObject), out Vector3 destroyPosition),
				"An OnDestroy dispatch must place an effect; a projectile detonating at the end of its " +
				"lifetime is the shape three shipped abilities are authored as.");
			LogAssert.AreEqual(where, destroyPosition, "At the object, which is where the detonation happened.");

			AbilitySpawnEventData spawn = new AbilitySpawnEventData(null, null, null, default, 0, abilityObject, null, null);
			LogAssert.IsTrue(PlayFXAction.TryResolveSpawnPosition(null, spawn, out Vector3 spawnPosition),
				"An OnSpawn dispatch must place an effect at the object it just created.");
			LogAssert.AreEqual(where, spawnPosition, "At the object.");

			AbilityTickEventData tick = new AbilityTickEventData(null, 0.1f, abilityObject);
			LogAssert.IsTrue(PlayFXAction.TryResolveSpawnPosition(null, tick, out Vector3 tickPosition),
				"An OnTick dispatch must place an effect at the lingering object.");
			LogAssert.AreEqual(where, tickPosition, "At the object.");
		}

		/// <summary>An impact point still wins, and an area hit with no point still falls to its victim.</summary>
		/// <remarks>
		/// The behaviour the action already had, pinned so widening it to the object did not quietly
		/// move a projectile impact off the body it struck.
		/// </remarks>
		[Test]
		public void PlayFX_KeepsTheImpactPointForACollision()
		{
			AbilityObject abilityObject = MakeAbilityObject(new Vector3(0f, 0f, 0f));
			Vector3 impact = new Vector3(3f, 1f, 4f);

			AbilityCollisionEventData hit = new AbilityCollisionEventData(null, null, abilityObject, impact, Vector3.up);
			LogAssert.IsTrue(PlayFXAction.TryResolveSpawnPosition(null, hit, out Vector3 position),
				"A resolved hit always knows where it landed.");
			LogAssert.AreEqual(impact, position,
				"And the impact point outranks the object's own position, which is only the origin.");
		}

		// ── Defect 2: a selector fan-out ran the whole action list zero times on an observer ─────

		/// <summary>
		/// The fallback rule, as a table. Two of its three rows are the safety property.
		/// </summary>
		/// <remarks>
		/// <b>An observer must still not resolve targets, apply damage or draw a predicted number.</b>
		/// That is the <paramref name="actionIsPresentation"/> row: only an action that declares its
		/// whole effect to be rendered may run here, and only <c>PlayFXAction</c> does. And a peer
		/// that DID resolve and found nobody must run nothing, or a blast that hit no one plays an
		/// impact on the caster's screen — the <c>peerResolvesSelections</c> row.
		/// </remarks>
		[Test]
		public void PresentationFallback_AppliesOnlyToADeclinedSelectionOfARenderedAction()
		{
			LogAssert.IsTrue(TriggerExecution.PresentationFallbackApplies(false, false, true),
				"Declined selection, rendered action: the observer plays the authored picture once.");
			LogAssert.IsFalse(TriggerExecution.PresentationFallbackApplies(false, true, true),
				"A peer that resolved and found nobody hit nobody; nothing may play.");
			LogAssert.IsFalse(TriggerExecution.PresentationFallbackApplies(false, false, false),
				"An action that changes state must NEVER run for a peer that resolved no targets — " +
				"that is an observer applying somebody else's damage.");
			LogAssert.IsFalse(TriggerExecution.PresentationFallbackApplies(true, false, true),
				"A selection that produced targets is served by the fan-out; the fallback is not a second run.");
			LogAssert.IsFalse(TriggerExecution.PresentationFallbackApplies(true, true, true),
				"Nor on the resolving peer.");

			/* The list-level callers decide the third row per action inside ExecuteActions, so they
			 * ask only the two rows that do not depend on one action. */
			LogAssert.IsTrue(TriggerExecution.SelectionWasDeclined(false, false),
				"An empty selection on a peer that may not select was DECLINED.");
			LogAssert.IsFalse(TriggerExecution.SelectionWasDeclined(false, true),
				"An empty selection on a peer that may select means nobody was there.");
			LogAssert.IsFalse(TriggerExecution.SelectionWasDeclined(true, false),
				"And a selection that produced targets was not declined at all.");
		}

		/// <summary>
		/// A trigger whose selector declines still plays its presentation, and still applies nothing.
		/// </summary>
		/// <remarks>
		/// The end-to-end shape of <i>Mock Chain Damage Event</i> on a third party: the selector
		/// yields nothing because <c>ResolvesSelectionsLocally</c> is false for an ability object with
		/// no caster, and before this fix the action list ran zero times — arcs, impacts and sounds
		/// included. The non-presentational probe is the half that must stay at zero.
		/// </remarks>
		[Test]
		public void Trigger_WithADeclinedSelector_RunsPresentationOnly()
		{
			AbilityObject observed = MakeAbilityObject(Vector3.zero);
			LogAssert.IsFalse(TargetSelector.ResolvesSelectionsLocally(new AbilityDestroyEventData(null, observed)),
				"Sanity: an ability object with no caster is the observer case and must decline to select.");

			ProbeAction rendered = new ProbeAction { Presentation = true };
			ProbeAction stateful = new ProbeAction { Presentation = false };

			Trigger trigger = Track(ScriptableObject.CreateInstance<Trigger>());
			trigger.TargetSelector = new EmptySelector();
			trigger.OnConditionsMetActions = new List<BaseAction> { rendered, stateful };

			trigger.Execute(new AbilityDestroyEventData(null, observed));

			LogAssert.AreEqual(1, rendered.Runs,
				"The authored impact must reach a third party, or a chain ability is invisible to everyone " +
				"but the caster.");
			LogAssert.AreEqual(0, stateful.Runs,
				"And nothing that changes state may run there. An observer resolves nothing and predicts nothing.");
		}

		/// <summary>The resolving peer gets no fallback, so an honest miss stays a miss.</summary>
		[Test]
		public void Trigger_OnTheResolvingPeer_RunsNothingForAnEmptySelection()
		{
			AbilityObject resolving = MakeAbilityObject(Vector3.zero, isServer: true);
			LogAssert.IsTrue(TargetSelector.ResolvesSelectionsLocally(new AbilityDestroyEventData(null, resolving)),
				"Sanity: the server's copy resolves its own selections.");

			ProbeAction rendered = new ProbeAction { Presentation = true };

			Trigger trigger = Track(ScriptableObject.CreateInstance<Trigger>());
			trigger.TargetSelector = new EmptySelector();
			trigger.OnConditionsMetActions = new List<BaseAction> { rendered };

			trigger.Execute(new AbilityDestroyEventData(null, resolving));

			LogAssert.AreEqual(0, rendered.Runs,
				"A blast that hit nobody plays no impact. The fallback exists for a peer that was not " +
				"allowed to look, not for an empty answer.");
		}

		/// <summary>The same rule applies to a per-ACTION selector, not only a trigger-level one.</summary>
		[Test]
		public void ActionSelector_WithADeclinedSelection_RunsPresentationOnce()
		{
			AbilityObject observed = MakeAbilityObject(Vector3.zero);

			ProbeAction rendered = new ProbeAction { Presentation = true, TargetSelector = new EmptySelector() };
			ProbeAction stateful = new ProbeAction { Presentation = false, TargetSelector = new EmptySelector() };

			TriggerExecution.ExecuteActions(new List<BaseAction> { rendered, stateful },
				new AbilityDestroyEventData(null, observed));

			LogAssert.AreEqual(1, rendered.Runs, "Once, against the event's own scope.");
			LogAssert.AreEqual(0, stateful.Runs, "And never for an action that changes state.");
		}

		/// <summary>Only <c>PlayFXAction</c> claims to be pure presentation.</summary>
		/// <remarks>
		/// The classification is what keeps the fallback safe, so its membership is worth pinning:
		/// it is also the only action in the project that gates on <c>IsClientPeer</c>. Anything that
		/// moves a resource, installs a buff or draws a predicted number must stay false.
		/// </remarks>
		[Test]
		public void PresentationIsOptedIntoAndDefaultsToFalse()
		{
			LogAssert.IsTrue(new PlayFXAction().IsPresentation,
				"PlayFXAction is particles and nothing else.");
			LogAssert.IsFalse(new ApplyDamageAction().IsPresentation,
				"Damage is not presentation, whatever an observer would like to see.");
			LogAssert.IsFalse(new ApplyHealAction().IsPresentation, "Nor is healing.");
			LogAssert.IsFalse(new AbilityApplyAreaAction().IsPresentation, "Nor is resolving an area of hits.");
		}

		// ── Defect 3: AbilityApplyTargetAction was unusable in both documented wirings ───────────

		/// <summary>The recursion hazard is now a rule the action enforces, not a comment.</summary>
		/// <remarks>
		/// This action RUNS the ability's OnHit set. An OnHit dispatch is exactly the event shape that
		/// carries an <c>AbilityCollisionEventData</c> — on every producer — so refusing that shape
		/// both rejects the mis-wiring the old comment warned about and bounds the chain at one level:
		/// the event this action dispatches is itself a collision, so a nested instance declines.
		/// </remarks>
		[Test]
		public void ApplyTarget_RefusesToReenterTheHitChain()
		{
			LogAssert.IsFalse(AbilityApplyTargetAction.MayRunHitChain(eventCarriesCollision: true),
				"Wired to OnHit it re-enters the chain that invoked it and recurses until the stack gives out.");
			LogAssert.IsTrue(AbilityApplyTargetAction.MayRunHitChain(eventCarriesCollision: false),
				"OnSpawn, OnTick and OnDestroy carry no collision payload, and are the documented wirings.");
		}

		/// <summary>
		/// It resolves like its siblings and publishes like its siblings.
		/// </summary>
		/// <remarks>
		/// The payload test it opened with was <c>AbilityCollisionEventData</c>, which no OnSpawn or
		/// OnTick dispatch has — the identical defect corrected in <c>AbilityApplyAreaAction</c>. And
		/// it was the one apply-action that never published, so its impacts existed on the resolving
		/// peer alone even once it resolved.
		/// </remarks>
		[Test]
		public void ApplyTarget_ResolvesThroughTheSharedHelperAndPublishesItsImpact()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/Ability/AbilityApplyTargetAction.cs");

			LogAssert.IsTrue(source.Contains("AbilityObject.TryResolveFrom(eventData, out AbilityObject abilityObject)"),
				"It must resolve the object through the shared helper so OnSpawn / OnTick / OnDestroy all work.");
			LogAssert.IsTrue(source.Contains("abilityObject.PublishActionHit(target, point, normal)"),
				"And publish its impact, or third parties see the effect land on nobody.");
			LogAssert.IsTrue(source.Contains("abilityObject.ResolvesHitsLocally"),
				"The peer gate stays the object's, so a detached phantom does not predict on every client.");
			LogAssert.IsTrue(source.Contains("TryResolveTarget(eventData, out ICharacter target)"),
				"Strict target resolution: a misconfigured selector must be a no-op, never a self-hit.");
		}

		// ── Defect 4: the replay flag no production dispatch was in a position to set ────────────

		/// <summary>
		/// A dispatch declares its replay state from the <c>ReplicateState</c> it is running under.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The guard stays — it is the second line, and the first is that every production dispatch
		/// refuses to build an ECA payload at all on a replayed tick (<c>ResolveTargetAndSpawn</c>
		/// returns, <c>BuffController</c> tests <c>isReplayingTick</c>, the activation triggers test
		/// <c>state.ContainsReplayed()</c>, and the hit/tick/destroy dispatches run off
		/// <c>TimeManager.OnTick</c>, which is not replayed). Gating the whole dispatch is strictly
		/// better than gating one action inside it, because it also skips the RNG draws and the
		/// selector fan-out that a flag read by four actions cannot.
		/// </para>
		/// <para>
		/// What changed is how a site that CANNOT gate says so: it hands over the state it is
		/// executing under instead of remembering a bool. That is the difference between a fact and
		/// a claim.
		/// </para>
		/// </remarks>
		[Test]
		public void TickPayload_TakesItsReplayAnswerFromTheReplicateState()
		{
			PredictionTick tick = new PredictionTick(1234u);

			TickEventData ticked = new TickEventData(null, tick, ReplicateState.Ticked | ReplicateState.Created);
			LogAssert.IsFalse(ticked.IsReplay,
				"A first execution is not a replay, however the caller happens to feel about it.");
			LogAssert.IsTrue(ticked.IsReplicateTick,
				"and it is still replicate-domain — prediction-aware actions depend on that.");

			TickEventData replayed = new TickEventData(null, tick, ReplicateState.Replayed | ReplicateState.Created);
			LogAssert.IsTrue(replayed.IsReplay,
				"A dispatch running inside a reconcile says so from the state, not from memory.");
		}

		/// <summary>The guard reads the replay flag and not the tick DOMAIN.</summary>
		/// <remarks>
		/// Conflating the two was a real defect: every spawn and self-target dispatch carries a
		/// replicate-domain tick on every peer and none of them is ever a replay, so a guard reading
		/// the domain flag suppressed exactly the dispatches that cannot replay — <c>PlayFXAction</c>
		/// refused every self-buff and self-heal impact effect in the game, permanently.
		/// </remarks>
		[Test]
		public void ReplayGuard_ReadsTheReplayFlagAndNotTheTickDomain()
		{
			MethodInfo isReplayTick = typeof(BaseAction)
				.GetMethod("IsReplayTick", BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(isReplayTick, "The shared guard must still exist; four actions read it.");

			EventData spawnDispatch = new EventData(null);
			spawnDispatch.Add(new TickEventData(null, new PredictionTick(1234u), ReplicateState.Ticked | ReplicateState.Created));
			LogAssert.IsFalse((bool)isReplayTick.Invoke(null, new object[] { spawnDispatch }),
				"A replicate-domain first execution must not be mistaken for a replay.");

			EventData replayDispatch = new EventData(null);
			replayDispatch.Add(new TickEventData(null, new PredictionTick(1234u), ReplicateState.Replayed | ReplicateState.Created));
			LogAssert.IsTrue((bool)isReplayTick.Invoke(null, new object[] { replayDispatch }),
				"And a dispatch that declares itself a replay must be suppressed — the guard is live, " +
				"not decorative.");
		}

		// ── Defect 5: an effect was spawned, and nothing was ever going to end it ───────────────

		/// <summary>
		/// The FX the three fire abilities play as their impact, taken from the shipped asset.
		/// </summary>
		/// <remarks>
		/// Loaded rather than probed synthetically, because the whole defect is a property of THIS
		/// prefab: it is a looping, prewarmed ambient burn with <c>stopAction: None</c>, and a looping
		/// system with no stop action never reaches an end of its own. Nothing in this project calls
		/// <c>ParticleSystem.Stop</c> either, so the prefab had no way to end and no one to end it.
		/// </remarks>
		private const string FireFXPath = "Assets/Prefabs/Client/FX/Abilities/Fire.prefab";

		private static GameObject ShippedFireFX()
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FireFXPath);
			LogAssert.IsNotNull(prefab, $"Expected the shipped fire FX at {FireFXPath}.");
			return prefab;
		}

		/// <summary>
		/// An instance the action spawns is given a life, measured from the effect itself.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Issue #258. The action's body used to end at a bare <c>Instantiate</c>: no parent, no owner,
		/// no end. Anything that left the caller's cleanup to the prefab was therefore one authoring
		/// mistake from being permanent, and the shipped content made it — so each hit of Lesser
		/// Fireball, Orc Firebolt and Scroll of Flame Impact left a particle system in the world for
		/// the rest of the session, on every peer that saw the hit.
		/// </para>
		/// <para>
		/// What lingers is only the visuals, which is why walking into one triggers no ability event:
		/// no <c>AbilityObject</c> is involved in this path at all. That is the whole of the defect.
		/// </para>
		/// </remarks>
		[Test]
		public void PlayFX_SpawnedInstanceIsGivenALife()
		{
			Vector3 where = new Vector3(1f, 2f, 3f);
			GameObject instance = Track(PlayFXAction.SpawnFX(ShippedFireFX(), where, SceneManager.GetActiveScene()));
			LogAssert.IsNotNull(instance, "A prefab was supplied, so an instance must come back.");
			LogAssert.AreEqual(where, instance.transform.position, "At the position it was handed.");

			FXInstanceLifetime bound = instance.GetComponent<FXInstanceLifetime>();
			LogAssert.IsNotNull(bound,
				"Every instance this action spawns must be bounded, whatever its prefab does about despawn.");

			/* One period plus padding at the very least. Fire.prefab is a 0.5s system emitting 1s
			 * particles, so a bound taken from the system length alone would come out at a second and
			 * cut the effect off halfway through — generous is the safe direction here. */
			LogAssert.IsTrue(bound.Lifetime >= 1.5f,
				$"The bound ({bound.Lifetime}s) must outlast the effect it bounds, or the leak has been " +
				"traded for a truncation.");
			LogAssert.IsTrue(bound.Lifetime <= FXInstanceLifetime.MaximumLifetime,
				$"And it must be finite, which is the whole of the defect: {bound.Lifetime}s.");
		}

		/// <summary>That life actually runs out, and not before.</summary>
		/// <remarks>
		/// The bound is only half the fix; the other half is that reaching it destroys the instance.
		/// <c>Tick</c> is the seam that makes it assertable — <c>Time.deltaTime</c> is zero in edit
		/// mode, so a test driving <c>Update</c> through Unity would never reach the end of anything.
		/// </remarks>
		[Test]
		public void PlayFX_SpawnedInstanceEndsWhenItsLifeIsSpent()
		{
			GameObject instance = Track(PlayFXAction.SpawnFX(ShippedFireFX(), Vector3.zero, SceneManager.GetActiveScene()));
			FXInstanceLifetime bound = instance.GetComponent<FXInstanceLifetime>();

			LogAssert.IsFalse(bound.Tick(bound.Lifetime * 0.5f), "Half a life is not the end of one.");
			LogAssert.IsFalse(bound.Tick(bound.Lifetime * 0.4f), "Nor is nine tenths of one.");
			LogAssert.IsTrue(bound.Tick(bound.Lifetime * 0.2f),
				"The life runs out and the instance is destroyed. Nothing else was ever going to do it.");
		}

		/// <summary>
		/// The instance is placed in the scene the effect happened in, not the client's active scene.
		/// </summary>
		/// <remarks>
		/// Issue #269. World scenes are loaded additively and this project never calls
		/// <c>SetActiveScene</c> itself, so a bare <c>Instantiate</c> lands everything in whatever scene
		/// the client was started in. An effect played in a world scene then survives that scene being
		/// unloaded and is still on screen after a scene change — which is exactly the report.
		/// <c>AbilityObject.Spawn</c> moves its instances to the caster's scene for the same reason,
		/// and the ability object has therefore already made this decision.
		/// </remarks>
		[Test]
		public void PlayFX_PlacesTheInstanceInTheSceneTheEffectHappenedIn()
		{
			Scene active = SceneManager.GetActiveScene();

			/* A preview scene: fully isolated and legal in edit mode, where SceneManager.CreateScene is
			 * not, and it never touches the scene the test runner is standing in. */
			Scene world = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();

			try
			{
				LogAssert.IsFalse(world == active, "Sanity: the effect's scene must not be the active one.");

				AbilityObject abilityObject = MakeAbilityObject(Vector3.zero);
				SceneManager.MoveGameObjectToScene(abilityObject.GameObject, world);

				EventData destroy = new AbilityDestroyEventData(null, abilityObject);
				LogAssert.AreEqual(world, PlayFXAction.ResolveSpawnScene(null, destroy),
					"The ability object has already been placed in the caster's scene, so it is asked first.");

				GameObject instance = Track(PlayFXAction.SpawnFX(ShippedFireFX(), Vector3.zero,
					PlayFXAction.ResolveSpawnScene(null, destroy)));

				LogAssert.AreEqual(world, instance.scene,
					"The instance follows the effect into its scene — a bare Instantiate would have left it " +
					"in the active one, where it outlives the scene it belongs to.");
			}
			finally
			{
				UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(world);
			}
		}

		// ── Probes ──────────────────────────────────────────────────────────────────────────────

		/// <summary>A selector that resolves nothing, standing in for a spatial selector on an observer.</summary>
		private sealed class EmptySelector : TargetSelector
		{
			public override IEnumerable<GameObject> SelectTargets(EventData eventData)
			{
				yield break;
			}
		}

		/// <summary>Counts executions and declares whichever classification the test needs.</summary>
		private sealed class ProbeAction : BaseAction
		{
			public bool Presentation;
			public int Runs;

			public override bool IsPresentation => Presentation;

			public override void Execute(ICharacter initiator, EventData eventData)
			{
				++Runs;
			}
		}
	}
}
