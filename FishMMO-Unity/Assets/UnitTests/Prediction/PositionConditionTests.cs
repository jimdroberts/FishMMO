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
	/// Covers the position-sensitive ECA conditions and the rule that makes them safe.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Until these existed, no shipped condition read a transform position, so a condition could
	/// never disagree with the query that produced its candidate. These are the first that can, and
	/// they are only correct because every spatial selector evaluates its conditions inside the same
	/// lag-compensation scope as its query — see <c>TargetSelector.GatherRewound</c>.
	/// <see cref="AllCharactersSelector_GathersUnderARewindScope"/> pins the selector that had to be
	/// changed to hold that line.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PositionConditionTests
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

		// ── WithinRangeCondition ─────────────────────────────────────────────────────

		[Test]
		public void WithinRange_PassesInsideAndFailsOutside()
		{
			ICharacter caster = MakeCharacter("RangeCaster", Vector3.zero);
			ICharacter near = MakeCharacter("RangeNear", new Vector3(5f, 0f, 0f));
			ICharacter far = MakeCharacter("RangeFar", new Vector3(15f, 0f, 0f));

			WithinRangeCondition condition = new WithinRangeCondition { MaximumRange = 10f };

			LogAssert.IsTrue(condition.Evaluate(caster, EventFor(caster, near)),
				"5m is inside a 10m range and must pass.");
			LogAssert.IsFalse(condition.Evaluate(caster, EventFor(caster, far)),
				"15m is outside a 10m range and must fail.");
		}

		/// <summary>A minimum range must exclude a target that is too close.</summary>
		[Test]
		public void WithinRange_MinimumExcludesPointBlank()
		{
			ICharacter caster = MakeCharacter("MinCaster", Vector3.zero);
			ICharacter adjacent = MakeCharacter("MinAdjacent", new Vector3(1f, 0f, 0f));

			WithinRangeCondition condition = new WithinRangeCondition { MaximumRange = 30f, MinimumRange = 5f };

			LogAssert.IsFalse(condition.Evaluate(caster, EventFor(caster, adjacent)),
				"1m is inside the 5m dead zone of a ranged ability and must fail.");
		}

		/// <summary>
		/// Height must not eat into the horizontal reach by default.
		/// </summary>
		/// <remarks>
		/// A sphere centred on the caster reaches less far along the ground as height difference
		/// grows, so a target on a ledge is refused at a horizontal distance that would pass on
		/// flat ground. Authors mean the cylinder.
		/// </remarks>
		[Test]
		public void WithinRange_IgnoresHeightByDefault()
		{
			ICharacter caster = MakeCharacter("HeightCaster", Vector3.zero);
			ICharacter onLedge = MakeCharacter("HeightLedge", new Vector3(9.5f, 8f, 0f));

			WithinRangeCondition cylinder = new WithinRangeCondition { MaximumRange = 10f };
			LogAssert.IsTrue(cylinder.Evaluate(caster, EventFor(caster, onLedge)),
				"9.5m horizontally is within 10m; a target on a ledge must not be excluded by its height.");

			WithinRangeCondition sphere = new WithinRangeCondition { MaximumRange = 10f, IgnoreVerticalDistance = false };
			LogAssert.IsFalse(sphere.Evaluate(caster, EventFor(caster, onLedge)),
				"With vertical distance counted the same target is 12.4m away and must fail.");
		}

		/// <summary>A range check against oneself must pass, or self-targeted abilities gate themselves out.</summary>
		[Test]
		public void WithinRange_SelfPasses()
		{
			ICharacter caster = MakeCharacter("SelfRange", Vector3.zero);

			LogAssert.IsTrue(new WithinRangeCondition { MaximumRange = 10f }.Evaluate(caster, EventFor(caster, caster)),
				"A character is always within range of itself; failing here breaks every self-targeted ability.");
		}

		/// <summary>Invert must negate the result, like every other condition.</summary>
		[Test]
		public void WithinRange_HonoursInvertThroughCheck()
		{
			ICharacter caster = MakeCharacter("InvCaster", Vector3.zero);
			ICharacter far = MakeCharacter("InvFar", new Vector3(50f, 0f, 0f));

			WithinRangeCondition condition = new WithinRangeCondition { MaximumRange = 10f, Invert = true };

			LogAssert.IsTrue(condition.Check(caster, EventFor(caster, far)),
				"Inverted, an out-of-range target must PASS — this is how 'only at long range' is authored. " +
				"Invert is applied by the framework's Check, never by Evaluate.");
		}

		// ── IsWithinFacingAngleCondition ─────────────────────────────────────────────

		/// <summary>
		/// The authored angle is the FULL arc, not the half angle.
		/// </summary>
		/// <remarks>
		/// A 90 degree cone allows 45 degrees to each side. Reading it as a half angle would make
		/// every authored cone twice as wide as intended, which is exactly the kind of error that
		/// looks like "the ability feels too forgiving" rather than like a bug.
		/// </remarks>
		[Test]
		public void FacingAngle_ArcIsTheFullWidthNotTheHalfAngle()
		{
			ICharacter caster = MakeCharacter("ArcCaster", Vector3.zero);
			ICharacter atForty = MakeCharacter("ArcInside", DirectionAtDegrees(40f) * 5f);
			ICharacter atFifty = MakeCharacter("ArcOutside", DirectionAtDegrees(50f) * 5f);

			IsWithinFacingAngleCondition condition = new IsWithinFacingAngleCondition { ArcDegrees = 90f };

			LogAssert.IsTrue(condition.Evaluate(caster, EventFor(caster, atForty)),
				"40 degrees off forward is inside a 90 degree arc (45 per side).");
			LogAssert.IsFalse(condition.Evaluate(caster, EventFor(caster, atFifty)),
				"50 degrees off forward is outside a 90 degree arc. If this passes, the arc is being " +
				"read as a half angle and every authored cone is twice as wide as intended.");
		}

		/// <summary>Behind must fail, and Invert must turn it into a backstab gate.</summary>
		[Test]
		public void FacingAngle_BehindFailsAndInvertsIntoABackstab()
		{
			ICharacter caster = MakeCharacter("BackCaster", Vector3.zero);
			ICharacter behind = MakeCharacter("BackTarget", new Vector3(0f, 0f, -5f));

			LogAssert.IsFalse(new IsWithinFacingAngleCondition { ArcDegrees = 90f }.Evaluate(caster, EventFor(caster, behind)),
				"A target directly behind must be outside a forward arc.");
			LogAssert.IsTrue(new IsWithinFacingAngleCondition { ArcDegrees = 90f, Invert = true }.Check(caster, EventFor(caster, behind)),
				"Inverted, the same test is how a backstab requirement is authored.");
		}

		/// <summary>A full circle always passes; a zero arc never does.</summary>
		[Test]
		public void FacingAngle_DegenerateArcsAreDefinite()
		{
			ICharacter caster = MakeCharacter("DegCaster", Vector3.zero);
			ICharacter behind = MakeCharacter("DegTarget", new Vector3(0f, 0f, -5f));

			LogAssert.IsTrue(new IsWithinFacingAngleCondition { ArcDegrees = 360f }.Evaluate(caster, EventFor(caster, behind)),
				"A 360 degree arc covers every direction.");
			LogAssert.IsFalse(new IsWithinFacingAngleCondition { ArcDegrees = 0f }.Evaluate(caster, EventFor(caster, behind)),
				"A zero-width arc cannot contain anything.");
		}

		/// <summary>
		/// A target standing exactly on the caster must not be excluded by the arc.
		/// </summary>
		/// <remarks>
		/// There is no direction to measure, so the arc cannot rule them out. Failing here would
		/// make a melee cone miss a target that had walked precisely into the caster — a rare case
		/// that reads as a bug when it happens.
		/// </remarks>
		[Test]
		public void FacingAngle_CoincidentTargetPasses()
		{
			ICharacter caster = MakeCharacter("CoCaster", Vector3.zero);
			ICharacter onTop = MakeCharacter("CoTarget", new Vector3(0f, 0f, 0f));

			LogAssert.IsTrue(new IsWithinFacingAngleCondition { ArcDegrees = 45f }.Evaluate(caster, EventFor(caster, onTop)),
				"A coincident target has no bearing to test, so the arc must not exclude it.");
		}

		// ── HasLineOfSightCondition ──────────────────────────────────────────────────

		/// <summary>
		/// An unconfigured blocker mask must pass rather than refuse everything.
		/// </summary>
		/// <remarks>
		/// A condition whose layer mask was left unset would otherwise fail every cast silently and
		/// totally. Passing is the absence of a restriction the author has not finished expressing.
		/// </remarks>
		[Test]
		public void LineOfSight_EmptyBlockerMaskPasses()
		{
			ICharacter caster = MakeCharacter("LosCaster", Vector3.zero);
			ICharacter target = MakeCharacter("LosTarget", new Vector3(5f, 0f, 0f));

			LogAssert.IsTrue(new HasLineOfSightCondition { Blockers = 0 }.Evaluate(caster, EventFor(caster, target)),
				"With nothing configured as a blocker there is nothing to block, so this must pass.");
		}

		/// <summary>Line of sight to oneself is never blocked.</summary>
		[Test]
		public void LineOfSight_SelfPasses()
		{
			ICharacter caster = MakeCharacter("LosSelf", Vector3.zero);

			LogAssert.IsTrue(new HasLineOfSightCondition { Blockers = ~0 }.Evaluate(caster, EventFor(caster, caster)),
				"A character always has sight of itself, whatever the blocker mask.");
		}

		// ── The rule that makes the above safe ───────────────────────────────────────

		/// <summary>
		/// <c>AllCharactersTargetSelector</c> must evaluate its conditions inside a rewind scope.
		/// </summary>
		/// <remarks>
		/// It is the one selector whose own selection is position-independent, which is why its
		/// conditions were originally evaluated outside the scope. That was harmless only while no
		/// condition read a position. Now that the conditions in this file exist, a candidate from
		/// this selector would be filtered against live server positions while every other
		/// selector filters against the caster's rewound view — the same authored range meaning two
		/// different things depending on which selector produced the candidate.
		/// <para>
		/// Asserted on the source because the alternative needs a spawned network object, an owning
		/// connection and a populated position history, none of which an EditMode test can build.
		/// </para>
		/// </remarks>
		[Test]
		public void AllCharactersSelector_GathersUnderARewindScope()
		{
			string path = Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/AllCharactersTargetSelector.cs");
			LogAssert.IsTrue(File.Exists(path), $"AllCharactersTargetSelector.cs not found at {path}.");

			string source = File.ReadAllText(path);

			LogAssert.IsTrue(source.Contains("GatherRewound"),
				"AllCharactersTargetSelector must gather through TargetSelector.GatherRewound so its " +
				"conditions see the same world every other selector's conditions see. Position-sensitive " +
				"conditions (WithinRangeCondition, HasLineOfSightCondition, IsWithinFacingAngleCondition) " +
				"are what make this observable.");
			LogAssert.IsFalse(source.Contains("RewoundOverlapSphere") || source.Contains("RewoundRaycast"),
				"Those helpers were deleted because they close the rewind before ranking and filtering. " +
				"See the note in TargetSelector.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		/// <summary>Unit vector that many degrees clockwise from +Z on the horizontal plane.</summary>
		private static Vector3 DirectionAtDegrees(float degrees)
		{
			float radians = degrees * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
		}

		private static EventData EventFor(ICharacter initiator, ICharacter target)
		{
			EventData data = new EventData(initiator);
			data.SetTarget(target.GameObject);
			return data;
		}

		/// <summary>
		/// Creates a character stand-in at a position, facing +Z.
		/// </summary>
		private ICharacter MakeCharacter(string name, Vector3 position)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			go.transform.position = position;
			go.transform.rotation = Quaternion.identity;
			return go.AddComponent<ProbeCharacter>();
		}

		/// <summary>
		/// A character stand-in with a real Transform, which is all a positional condition reads.
		/// </summary>
		/// <remarks>
		/// A MonoBehaviour rather than a plain object because these conditions measure
		/// <see cref="ICharacter.Transform"/> and query <see cref="ICharacter.GameObject"/>'s
		/// physics scene — neither of which a detached stub can provide.
		/// </remarks>
		private sealed class ProbeCharacter : MonoBehaviour, ICharacter
		{
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
