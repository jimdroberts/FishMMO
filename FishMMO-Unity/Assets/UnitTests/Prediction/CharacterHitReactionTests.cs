using System.Collections.Generic;
using System.IO;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the cosmetic hit reaction: the attacker sees an impact on the frame it lands, without
	/// anything predicting a networked position.
	/// </summary>
	/// <remarks>
	/// Knockback is the one feedback action that did not move to <c>MayPredict</c>. Its displacement
	/// moves the target, and on the attacker's client that target is driven by NetworkTransform — a
	/// locally applied impulse is overwritten one to three ticks later and the character snaps back,
	/// which reads worse than the delay. The flinch goes on a child transform instead, so the two
	/// compose rather than fight.
	/// </remarks>
	[TestFixture]
	public class CharacterHitReactionTests
	{
		private readonly List<GameObject> gameObjects = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < gameObjects.Count; ++i)
			{
				if (gameObjects[i] != null)
				{
					Object.DestroyImmediate(gameObjects[i]);
				}
			}
			gameObjects.Clear();
		}

		/// <summary>A reaction offsets the model and leaves the networked root alone.</summary>
		[Test]
		public void Play_OffsetsTheModelAndNotTheRoot()
		{
			Rig rig = MakeRig();
			Vector3 rootBefore = rig.Root.transform.position;

			rig.Reaction.Play(Vector3.forward);
			Pump(rig, 0.02f);

			LogAssert.AreEqual(rootBefore, rig.Root.transform.position,
				"The networked root must never move. NetworkTransform owns it, and displacing it " +
				"locally is overwritten within a few ticks — the snap-back this design exists to avoid.");
			LogAssert.IsTrue(rig.Mesh.localPosition.sqrMagnitude > 0f,
				"The model must lean, or there is no feedback at all.");
		}

		/// <summary>The lean decays back to rest and stays there.</summary>
		[Test]
		public void Play_DecaysBackToRest()
		{
			Rig rig = MakeRig();

			rig.Reaction.Play(Vector3.forward);
			Pump(rig, 0.02f);
			LogAssert.IsTrue(rig.Reaction.IsPlaying, "Still leaning immediately after the hit.");

			Pump(rig, rig.Reaction.ReactionSeconds + 0.05f);

			LogAssert.IsFalse(rig.Reaction.IsPlaying, "The reaction must finish on its own.");
			LogAssert.AreEqual(Vector3.zero, rig.Mesh.localPosition,
				"The model must return exactly to rest. Drift here accumulates over a fight and " +
				"leaves the mesh permanently off its own character.");
		}

		/// <summary>
		/// A burst of hits restarts the lean rather than stacking it.
		/// </summary>
		/// <remarks>
		/// Accumulating would let a multi-hit ability walk the model arbitrarily far from the root
		/// it belongs to.
		/// </remarks>
		[Test]
		public void Play_RestartsRatherThanAccumulating()
		{
			Rig rig = MakeRig();

			rig.Reaction.Play(Vector3.forward);
			Pump(rig, 0.01f);
			float first = rig.Mesh.localPosition.magnitude;

			for (int i = 0; i < 5; ++i)
			{
				rig.Reaction.Play(Vector3.forward);
				Pump(rig, 0.01f);
			}

			LogAssert.IsTrue(rig.Mesh.localPosition.magnitude <= first + 0.001f,
				$"Five stacked hits must not lean further than one ({rig.Mesh.localPosition.magnitude} vs {first}).");
			LogAssert.IsTrue(rig.Mesh.localPosition.magnitude <= rig.Reaction.MaximumOffset + 0.001f,
				"The lean must never exceed MaximumOffset, whatever arrives.");
		}

		/// <summary>A zero direction is not an impact and must be ignored.</summary>
		[Test]
		public void Play_IgnoresAZeroDirection()
		{
			Rig rig = MakeRig();

			rig.Reaction.Play(Vector3.zero);

			LogAssert.IsFalse(rig.Reaction.IsPlaying,
				"With no direction there is nothing to lean along; leaning an arbitrary way would be worse.");
		}

		/// <summary>Vertical-only impacts are ignored — the lean is horizontal.</summary>
		[Test]
		public void Play_IgnoresAPurelyVerticalDirection()
		{
			Rig rig = MakeRig();

			rig.Reaction.Play(Vector3.up);

			LogAssert.IsFalse(rig.Reaction.IsPlaying,
				"A straight-up impact flattens to zero horizontally, so there is no lean to play.");
		}

		/// <summary>Resetting returns the model to rest, so a pooled character cannot inherit a lean.</summary>
		[Test]
		public void ResetToRest_ReturnsTheModelToRest()
		{
			Rig rig = MakeRig();

			rig.Reaction.Play(Vector3.forward);
			Pump(rig, 0.02f);
			LogAssert.IsTrue(rig.Mesh.localPosition.sqrMagnitude > 0f, "Leaning before the despawn.");

			/* ResetToRest rather than enabled = false: EditMode does not invoke OnDisable, and the
			 * behaviour under test is the reset itself — which pooling calls directly anyway. */
			rig.Reaction.ResetToRest();

			LogAssert.AreEqual(Vector3.zero, rig.Mesh.localPosition,
				"A pooled character keeps its transforms, so a reaction left mid-decay would be " +
				"inherited by whoever the object is reused as — a model visibly off-centre for no reason.");
		}

		/// <summary>A character with no mesh root simply does not flinch.</summary>
		[Test]
		public void Play_WithoutAMeshRoot_DoesNothing()
		{
			GameObject go = new GameObject("NoMesh");
			gameObjects.Add(go);
			ReactionProbeCharacter character = go.AddComponent<ReactionProbeCharacter>();
			character.Mesh = null;
			CharacterHitReaction reaction = go.AddComponent<CharacterHitReaction>();

			reaction.Play(Vector3.forward);

			LogAssert.IsFalse(reaction.IsPlaying,
				"An NPC or a character whose model has not loaded has nothing to lean; that must " +
				"degrade silently rather than throw on a hit.");
		}

		/// <summary>Knockback's displacement must stay server-only.</summary>
		/// <remarks>
		/// The flinch is the predicted half; the movement is not, and widening it to MayPredict
		/// would reintroduce exactly the NetworkTransform fight this component exists to avoid.
		/// </remarks>
		[Test]
		public void KnockbackAction_KeepsItsDisplacementServerOnly()
		{
			string source = File.ReadAllText(Path.Combine(Application.dataPath,
				"Scripts/Shared/Implementation/Entity/ECA/Actions/Character/KnockbackHitAction.cs"));

			LogAssert.IsTrue(source.Contains("if (!EcaAuthority.IsServer(initiator, eventData))"),
				"The displacement must remain gated on IsServer.");
			LogAssert.IsTrue(source.Contains("CharacterHitReaction.PlayOn"),
				"The attacker must still get immediate feedback through the cosmetic reaction.");
			LogAssert.IsTrue(source.Contains("IsReplayTick(eventData)"),
				"The reaction must be suppressed on replayed ticks, or a reconcile makes the model " +
				"jitter once per replayed tick instead of leaning once.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private struct Rig
		{
			public GameObject Root;
			public Transform Mesh;
			public CharacterHitReaction Reaction;
		}

		/// <summary>Root with a child mesh transform, matching the real character hierarchy.</summary>
		private Rig MakeRig()
		{
			GameObject root = new GameObject("HitReactionRoot");
			gameObjects.Add(root);
			root.transform.position = new Vector3(5f, 0f, 5f);

			GameObject mesh = new GameObject("MeshRoot");
			mesh.transform.SetParent(root.transform);
			mesh.transform.localPosition = Vector3.zero;

			ReactionProbeCharacter character = root.AddComponent<ReactionProbeCharacter>();
			character.Mesh = mesh.transform;

			CharacterHitReaction reaction = root.AddComponent<CharacterHitReaction>();
			typeof(CharacterHitReaction)
				.GetField("leanTarget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(reaction, mesh.transform);

			return new Rig { Root = root, Mesh = mesh.transform, Reaction = reaction };
		}

		/// <summary>
		/// Drives the decay with an explicit delta.
		/// </summary>
		/// <remarks>
		/// EditMode runs no player loop, and Unity's delta in batch mode can exceed a whole
		/// reaction — a component reading the clock directly would finish before the first
		/// assertion. Step exists so the schedule is driven rather than observed.
		/// </remarks>
		private static void Pump(Rig rig, float seconds)
		{
			const float Step = 0.01f;
			for (float t = 0f; t < seconds; t += Step)
			{
				rig.Reaction.Step(Step);
			}
		}

		/// <summary>Character stand-in exposing a settable mesh root.</summary>
		private sealed class ReactionProbeCharacter : MonoBehaviour, ICharacter
		{
			public Transform Mesh;

			public long ID { get; set; }
			public string Name => name;
			public Transform Transform => transform;
			public GameObject GameObject => gameObject;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => null;
			public FishNet.Managing.Predicting.PredictionManager PredictionManager => null;
			public HashSet<FishNet.Connection.NetworkConnection> Observers { get; } = new HashSet<FishNet.Connection.NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
			public Transform MeshRoot => Mesh;
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
