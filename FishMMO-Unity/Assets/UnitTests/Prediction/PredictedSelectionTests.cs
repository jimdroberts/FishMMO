using System;
using System.Collections.Generic;
using System.IO;
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
	/// Guards the rule that made every ability shape predictable, not just projectiles:
	/// <b>selection is not authority</b>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Spatial selection used to be server-only. That made the <c>MaxHits</c> cap trivially agreed —
	/// a client computed nothing, so it could not compute something different — but it also meant an
	/// area, cone, line, chain or self-target ability produced NO client-side prediction at all: the
	/// selector yielded an empty set on the caster's own machine, so no action downstream ever ran,
	/// and the whole "hit what you see" path (predicted numbers, immediate resource movement, impact
	/// FX at the predicted tick) reached only projectiles, whose hits come from
	/// <c>AbilityObject</c>'s sweep rather than from a selector.
	/// </para>
	/// <para>
	/// Selection is now gated on <c>TargetSelector.ResolvesTargetsLocally</c> (the server, or the
	/// client owning the initiator). What keeps that safe is not the selector but the ACTIONS: each
	/// one re-asks what it is allowed to do. Feedback actions predict; authoritative ones stay
	/// server-only. If that split is ever eroded — the tempting "we predict everything now, so this
	/// can be MayPredict too" — a client starts inventing threat, revives, dispels and item grants.
	/// <see cref="AuthoritativeActions_StayServerOnly"/> is the test that catches it, and it is the
	/// most load-bearing assertion in this file.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PredictedSelectionTests
	{
		private const string TargetDir = "Assets/Scripts/Shared/Implementation/Entity/ECA/Target/";
		private const string ActionDir = "Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/";

		/// <summary>
		/// Every selector that resolves a SPATIAL query into candidate bodies, and therefore
		/// predicts. <c>AllCharactersTargetSelector</c> is deliberately absent — see
		/// <see cref="ZoneWideSelection_StaysServerOnly"/>.
		/// </summary>
		private static readonly string[] SpatialSelectors =
		{
			"AreaTargetSelector.cs",
			"ChainTargetSelector.cs",
			"ConeTargetSelector.cs",
			"FurthestTargetSelector.cs",
			"LineTargetSelector.cs",
			"NearestTargetSelector.cs",
			"RandomTargetSelector.cs",
			"TargetedEntitySelector.cs",
		};

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

		// ── The gate itself ─────────────────────────────────────────────────────────

		/// <summary>
		/// An OBSERVER resolves nothing. This is the half of the gate that did not move when
		/// selection was opened up, and the half that must never move: a third party holds every
		/// character interpolated against its own latency, has no input stream for the caster, and
		/// is going to be told what happened anyway.
		/// </summary>
		[Test]
		public void SpatialSelection_RefusesAnObserver()
		{
			GameObject context = MakeContext("observerContext", Vector3.zero);
			MakeLooseCollider("observerCandidate", new Vector3(2f, 0f, 0f));
			Physics.SyncTransforms();

			ICharacter observedCaster = MakeNetworkedCharacter("notOurCaster");
			LogAssert.IsFalse(EcaAuthority.MayPredict(observedCaster, null),
				"Precondition: a character whose NetworkObject we do not own must not be predictable.");

			AreaTargetSelector selector = new AreaTargetSelector { Radius = 20f, TargetLayer = ~0, MaxHits = 5 };
			List<GameObject> picked = Select(selector, context, observedCaster);

			LogAssert.AreEqual(0, picked.Count,
				"An observer must resolve nothing. Letting it pick its own targets for somebody else's " +
				"area ability is the failure the gate exists to prevent — it would apply effects for a " +
				"cast it cannot see the inputs of.");
		}

		/// <summary>
		/// A peer that MAY predict resolves normally. Without this the observer test above would
		/// pass just as well against a selector that is broken for everyone.
		/// </summary>
		[Test]
		public void SpatialSelection_RunsForAPredictingPeer()
		{
			GameObject context = MakeContext("predictContext", Vector3.zero);
			GameObject candidate = MakeLooseCollider("predictCandidate", new Vector3(2f, 0f, 0f));
			Physics.SyncTransforms();

			AreaTargetSelector selector = new AreaTargetSelector { Radius = 20f, TargetLayer = ~0, MaxHits = 5 };
			List<GameObject> picked = Select(selector, context, initiator: null);

			LogAssert.IsTrue(picked.Contains(candidate),
				"A peer allowed to predict must resolve the query — this is what gives an area ability " +
				"any client-side feedback at all.");
		}

		/// <summary>
		/// The gate the selectors call is the PREDICTING one, not a renamed server-only check.
		/// </summary>
		/// <remarks>
		/// This closes the middle link of the chain the two behavioural tests above cannot reach.
		/// Edit mode cannot produce an OWNED <see cref="NetworkObject"/> — <c>IsOwner</c> needs a
		/// live local client — so the one case that actually separates the two gates (a client that
		/// owns the caster: server-only says no, predicting says yes) is not constructible here.
		/// Both gates refuse an observer and both allow the un-networked case, which is why
		/// <see cref="SpatialSelection_RefusesAnObserver"/> would pass against either. The source
		/// chain is therefore what pins the behaviour: selectors call
		/// <c>ResolvesTargetsLocally</c> (asserted below), that is <c>MayPredict</c> (asserted
		/// here), and <c>MayPredict</c> is <c>IsServer</c> widened by an owner check (asserted in
		/// <c>EcaPredictionAuthorityTests</c>).
		/// </remarks>
		[Test]
		public void ResolvesTargetsLocally_IsThePredictingGate()
		{
			string source = ReadSource(TargetDir + "TargetSelector.cs");
			LogAssert.IsTrue(source.Contains("return EcaAuthority.MayPredict(eventData);"),
				"The selector gate must fall through to MayPredict. If it is ever narrowed back to " +
				"IsServer the whole family silently stops predicting while still passing the sweep below.");
			LogAssert.IsTrue(source.Contains("return abilityObject.ResolvesHitsLocally;"),
				"An ability-driven selection must be judged by the OBJECT, so a DETACHED object — " +
				"whose phantom caster has no NetworkObject and therefore passes the undecidable case " +
				"of both peer gates — cannot let a third-party client resolve selections for it.");
		}

		/// <summary>
		/// Every spatial selector uses the predicting gate, and none has been quietly returned to
		/// server-only. One selector left behind is invisible in play — that ability simply feels
		/// laggier than the rest — so it is asserted in source across the whole family.
		/// </summary>
		[Test]
		public void SpatialSelectors_AllGateOnResolvesTargetsLocally()
		{
			foreach (string selector in SpatialSelectors)
			{
				string source = ReadSource(TargetDir + selector);
				LogAssert.IsTrue(source.Contains("!ResolvesTargetsLocally(eventData)"),
					$"{selector} must gate selection on ResolvesTargetsLocally so the caster's own client predicts it.");
				LogAssert.IsFalse(source.Contains("!IsAuthoritativePeer(eventData)"),
					$"{selector} still gates on the server-only predicate; its ability would produce no " +
					"client-side feedback while every other shape does.");
			}
		}

		/// <summary>
		/// Zone-wide selection stays SERVER-ONLY, and that exception is deliberate.
		/// </summary>
		/// <remarks>
		/// Every other selector asks "which bodies are in this volume", which the caster's client can
		/// answer because its live world is the world the server rewinds to. This one asks "who is in
		/// the zone" — and a client holds only what observer streaming spawned for it, so predicting
		/// would be a systematic under-selection (every culled character quietly missing from an
		/// effect defined as hitting everyone), not a boundary mispick. It is also the only selector
		/// that pays a full-scene component scan, which does not belong on a player's machine.
		/// </remarks>
		[Test]
		public void ZoneWideSelection_StaysServerOnly()
		{
			string source = ReadSource(TargetDir + "AllCharactersTargetSelector.cs");
			LogAssert.IsTrue(source.Contains("!IsAuthoritativePeer(eventData)"),
				"A zone-wide fan-out must resolve only where the whole zone is known.");
			LogAssert.IsFalse(source.Contains("!ResolvesTargetsLocally(eventData)"),
				"Predicting this one would drop every streamed-out character from the effect.");
		}

		// ── The safety property the whole change rests on ───────────────────────────

		/// <summary>
		/// Actions whose effect is authoritative and not player-visible stay SERVER-ONLY.
		/// </summary>
		/// <remarks>
		/// This is the assertion that makes opening selection safe. A selector now hands candidates
		/// to these actions on the caster's client too, and the only thing stopping a client
		/// awarding itself threat, revives, dispels or equipment is each action's own
		/// <c>EcaAuthority.IsServer</c> gate. Predicting them buys no feel — none of them draws
		/// anything the player sees at the moment of the cast — and every one of them adds a way to
		/// be wrong.
		/// </remarks>
		[Test]
		public void AuthoritativeActions_StayServerOnly()
		{
			string[] serverOnly =
			{
				"ApplyThreatAction.cs",
				"ApplyTauntAction.cs",
				"ApplyReviveAction.cs",
				"ApplyDispelAction.cs",
				"EquipItemAction.cs",
				"UnequipItemAction.cs",
			};

			foreach (string action in serverOnly)
			{
				string source = ReadSource(ActionDir + action);
				LogAssert.IsTrue(source.Contains("EcaAuthority.IsServer"),
					$"{action} changes authoritative state that no client may invent, and must gate on IsServer.");
				LogAssert.IsFalse(source.Contains("EcaAuthority.MayPredict"),
					$"{action} must NOT predict. Selection now reaches it on the caster's client, so a " +
					"MayPredict gate here would let that client apply the effect locally for real.");
			}
		}

		/// <summary>
		/// The feedback actions do predict — the other half of the split, so this file states both
		/// halves rather than only the prohibition.
		/// </summary>
		[Test]
		public void FeedbackActions_Predict()
		{
			string[] feedback = { "ApplyDamageAction.cs", "ApplyHealAction.cs", "ApplyBuffAction.cs" };
			foreach (string action in feedback)
			{
				string source = ReadSource(ActionDir + action);
				LogAssert.IsTrue(source.Contains("EcaAuthority.MayPredict"),
					$"{action} is what the caster sees happen; it must predict on the owning client.");
			}
		}

		// ── The two non-selector paths that were opened with it ─────────────────────

		/// <summary>
		/// A self-target ability dispatches its effects on the caster's own client, not only on the
		/// server. This was the largest remaining hole: a self-buff or self-heal is the most
		/// immediate action a player takes and produced nothing on screen until the reconcile.
		/// </summary>
		[Test]
		public void SelfTargetAbilities_DispatchOnTheCastersClient()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs");
			int self = source.IndexOf("if (template.AbilitySpawnTarget == AbilitySpawnTarget.Self)", StringComparison.Ordinal);
			LogAssert.IsTrue(self > 0, "The self-target branch must exist.");
			string branch = source.Substring(self, 700);
			LogAssert.IsTrue(branch.Contains("ResolvesHitsOnThisPeer(isServer, casterIsOwner)"),
				"The self-target dispatch must use the same predicate a swept projectile hit uses, " +
				"so a self-buff lands on the caster's screen at the tick they cast it.");
		}

		/// <summary>
		/// The two physics actions resolve wherever hits resolve — server and casting client — rather
		/// than server-only. A hitscan shot suffers most from waiting: it has no projectile to watch,
		/// so nothing at all happened locally until the report arrived.
		/// </summary>
		[Test]
		public void PhysicsActions_ResolveWhereHitsResolve()
		{
			string[] actions =
			{
				"Ability/AbilityApplyAreaAction.cs",
				"Ability/AbilityApplyHitscanAction.cs",
				"Ability/AbilityApplyTargetAction.cs",
			};
			foreach (string action in actions)
			{
				string source = ReadSource(ActionDir + action);
				LogAssert.IsTrue(source.Contains("!abilityObject.ResolvesHitsLocally"),
					$"{action} must resolve on the server and the casting client.");
				LogAssert.IsFalse(source.Contains("if (!abilityObject.IsServer)"),
					$"{action} still gates server-only, so its ability produces no predicted feedback.");
			}
		}

		// ── Helpers ─────────────────────────────────────────────────────────────────

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		private static List<GameObject> Select(TargetSelector selector, GameObject context, ICharacter initiator)
		{
			EventData eventData = new EventData(initiator);
			eventData.SetTarget(context);
			return new List<GameObject>(selector.SelectTargets(eventData));
		}

		private GameObject MakeContext(string name, Vector3 position)
		{
			GameObject go = new GameObject(name);
			go.transform.position = position;
			spawned.Add(go);
			return go;
		}

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
		/// A character carrying a real, UNOWNED <see cref="NetworkObject"/> — the shape of a peer
		/// this client is merely observing, which is the case the gate must refuse.
		/// </summary>
		private ICharacter MakeNetworkedCharacter(string name)
		{
			GameObject go = new GameObject(name);
			spawned.Add(go);
			go.AddComponent<NetworkObject>();
			return go.AddComponent<ProbeSelectionCharacter>();
		}

		/// <summary>Minimal character exposing a real NetworkObject, which is all the gate reads.</summary>
		private sealed class ProbeSelectionCharacter : MonoBehaviour, ICharacter
		{
			public long ID { get; set; }
			public string Name => name;
			public Transform Transform => transform;
			public GameObject GameObject => gameObject;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => GetComponent<NetworkObject>();
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
