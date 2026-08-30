using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The two questions the attributed-modifier ledger leaves open once its contributor keying is
	/// settled: whether a STACKING buff's apply and remove hooks are exact inverses, and which
	/// direction of the install/apply race the residual leaves wrong and for how long.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why stacks are the hard case.</b> <see cref="Buff.AddStack"/> raises the hook BEFORE it
	/// increments <c>Stacks</c>, and <see cref="Buff.RemoveStack"/> raises it BEFORE it decrements —
	/// so <c>AttributeBuffTemplate</c> writes <c>2 + Stacks</c> on the way up and <c>Stacks</c> on
	/// the way down, and the two expressions are only inverses because of that off-by-one. Nothing
	/// in the type system holds that relationship, and both hooks state an ABSOLUTE contribution
	/// through <c>SetSource</c>, so a mistake is not a drift that accumulates visibly — it is a
	/// permanently wrong stat at one particular stack count, which is exactly the sort of thing a
	/// player reports as "this buff feels wrong at 3 stacks".
	/// </para>
	/// <para>
	/// <b>Why the race direction matters.</b> The owner both applies a contributor locally and
	/// receives an authoritative total that already contains it — that is structural, not a bug, and
	/// the residual is what reconciles the two. But the residual is derived at INSTALL time from
	/// whatever the peer had attributed THEN, so if the total lands before the local contributor
	/// does, the residual absorbs the contributor's value and the local apply adds it a second time.
	/// The fixture below pins that this overshoot exists, that it is bounded by the next install,
	/// and that the controller ordering is what keeps the window at zero for the reconcile-driven
	/// path.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AttributeStackLedgerTests
	{
		private CharacterAttributeTemplate attributeTemplate;
		private AttributeBuffTemplate buffTemplate;
		private LedgerAttributeController controller;
		private LedgerCharacter character;

		/// <summary>Per-stack bonus. Deliberately not 1, so an off-by-one multiplier is unmistakable.</summary>
		private const int BonusPerStack = 7;

		[SetUp]
		public void SetUp()
		{
			attributeTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			attributeTemplate.name = "StackLedger_Attribute";
			attributeTemplate.AddToCache(attributeTemplate.name);

			buffTemplate = ScriptableObject.CreateInstance<AttributeBuffTemplate>();
			buffTemplate.name = "StackLedger_Buff";
			buffTemplate.BonusAttributes = new List<BuffAttributeTemplate>()
			{
				new BuffAttributeTemplate() { Value = BonusPerStack, Template = attributeTemplate },
			};
			buffTemplate.AddToCache(buffTemplate.name);

			controller = new LedgerAttributeController();
			character = new LedgerCharacter(controller);
			controller.OwningCharacter = character;

			controller.Attribute = new CharacterAttribute(controller, attributeTemplate.ID, 100, 0);
		}

		[TearDown]
		public void TearDown()
		{
			buffTemplate.RemoveFromCache();
			attributeTemplate.RemoveFromCache();
			UnityEngine.Object.DestroyImmediate(buffTemplate);
			UnityEngine.Object.DestroyImmediate(attributeTemplate);
		}

		private CharacterAttribute Attribute => controller.Attribute;

		/// <summary>A buff instance for <see cref="buffTemplate"/>, permanent, at zero stacks.</summary>
		private Buff MakeBuff() => new Buff(buffTemplate.ID, FishNet.Managing.Timing.TimeManager.UNSET_TICK, 1f / 30f);

		// ── stacks ─────────────────────────────────────────────────────────────

		/// <summary>
		/// The contribution is <c>(1 + Stacks) × Value</c> at every stack count reached by climbing,
		/// and the same at every count reached by coming back down.
		/// </summary>
		/// <remarks>
		/// Walked rather than spot-checked: the hooks are asymmetric expressions (<c>2 + Stacks</c>
		/// against <c>Stacks</c>) that only agree because of where the increment sits relative to the
		/// call, so a single stack count proves nothing about the next one.
		/// </remarks>
		[Test]
		public void StackMultipliers_AreExactAtEveryCount_ClimbingAndDescending()
		{
			Buff buff = MakeBuff();
			const int maxStacks = 6;

			buff.Apply(character);
			LogAssert.AreEqual(BonusPerStack, Attribute.ExternalModifier,
				"The base application is one multiple, not zero and not two.");

			for (int expected = 1; expected <= maxStacks; ++expected)
			{
				buff.AddStack(character);
				LogAssert.AreEqual(expected, buff.Stacks,
					"AddStack must increment after the hook, or every multiplier below is off by one.");
				LogAssert.AreEqual((1 + expected) * BonusPerStack, Attribute.ExternalModifier,
					$"At {expected} stack(s) the contribution must be (1 + {expected}) x {BonusPerStack}. " +
					"OnApplyStack writes 2 + Stacks precisely because Stacks has not been incremented " +
					"yet; a 1 + Stacks there would under-apply every stack.");
			}

			for (int remaining = maxStacks - 1; remaining >= 0; --remaining)
			{
				buff.RemoveStack(character);
				LogAssert.AreEqual(remaining, buff.Stacks, "RemoveStack must decrement after the hook.");
				LogAssert.AreEqual((1 + remaining) * BonusPerStack, Attribute.ExternalModifier,
					$"Coming back down through {remaining} stack(s) must land on exactly the value the " +
					"climb passed through. OnRemoveStack writes Stacks — the count AFTER this removal " +
					"plus the base application — and any other expression makes the descent a " +
					"different curve from the climb.");
			}

			LogAssert.AreEqual(BonusPerStack, Attribute.ExternalModifier,
				"Back at zero stacks the buff is still applied, so its base multiple stands.");

			buff.Remove(character);
			LogAssert.AreEqual(0, Attribute.ExternalModifier,
				"Removing the buff releases the contribution entirely.");
			LogAssert.AreEqual(0, Attribute.ModifierSourceCount,
				"...and leaves no entry behind, or the next buff of this template inherits it.");
		}

		/// <summary>
		/// A stack cycle is a round trip: any number of ups and downs ending where it started leaves
		/// the sheet where it started.
		/// </summary>
		/// <remarks>
		/// The property a refresh-on-reapply buff exercises hundreds of times in a fight. It holds
		/// only because both hooks state an absolute contribution — under an additive
		/// <c>AddModifier(±V)</c> shape a single mismatched pair drifted permanently, with nothing
		/// able to notice.
		/// </remarks>
		[Test]
		public void StackCycles_ReturnTheSheetToWhereItStarted()
		{
			Buff buff = MakeBuff();
			buff.Apply(character);

			int start = Attribute.ExternalModifier;

			for (int cycle = 0; cycle < 25; ++cycle)
			{
				buff.AddStack(character);
				buff.AddStack(character);
				buff.AddStack(character);
				buff.RemoveStack(character);
				buff.RemoveStack(character);
				buff.RemoveStack(character);
			}

			LogAssert.AreEqual(0, buff.Stacks, "Twenty-five balanced cycles end at zero stacks.");
			LogAssert.AreEqual(start, Attribute.ExternalModifier,
				$"...and at the value they started from. Ended at {Attribute.ExternalModifier} " +
				$"against {start}: the apply and remove hooks are not exact inverses.");
			LogAssert.AreEqual(1, Attribute.ModifierSourceCount,
				"One contributor, restated — not one entry per stack operation.");
		}

		/// <summary>
		/// A stacking buff and an equipped item raising the same attribute stay independent.
		/// </summary>
		/// <remarks>
		/// Both write through <c>SetSource</c> under their own <see cref="ModifierSource"/>, so the
		/// buff restating its contribution on every stack change must not disturb the item's. This is
		/// the case a single key per attribute got wrong, and it is silent: the sheet simply reads
		/// low, with no event and no log line.
		/// </remarks>
		[Test]
		public void AStackingBuff_DoesNotDisturbAnItemOnTheSameAttribute()
		{
			const int itemBonus = 40;
			Attribute.SetSource(ModifierSource.Item(9001), itemBonus);

			Buff buff = MakeBuff();
			buff.Apply(character);
			buff.AddStack(character);
			buff.AddStack(character);

			LogAssert.AreEqual(itemBonus + (3 * BonusPerStack), Attribute.ExternalModifier,
				"The item and the three multiples of the buff must sum.");

			buff.Remove(character);
			LogAssert.AreEqual(itemBonus, Attribute.ExternalModifier,
				"Removing the buff leaves the item exactly where it was — the buff releases by " +
				"contributor, so it cannot take the item's entry with it.");
		}

		// ── the install/apply race ─────────────────────────────────────────────

		/// <summary>
		/// The race direction the residual gets WRONG: an authoritative total that lands before the
		/// local contributor does is over-applied until the next install.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The mirror of <c>AttributeLedgerContractTests.ALocalContribution_AfterAnAuthoritativeInstall_IsAddedThenSubsumed</c>,
		/// which covers the ordering that behaves. This one exists because the failure is invisible:
		/// the number is too high rather than obviously broken, and the residual repairs it on the
		/// next install, so it only ever appears as a flicker.
		/// </para>
		/// <para>
		/// Pinned as a BOUND rather than as desirable behaviour. The claim being made is that the
		/// error survives exactly one install and cannot compound — an implementation whose residual
		/// failed to re-derive would leave the overshoot permanent, and that is the regression this
		/// catches.
		/// </para>
		/// </remarks>
		[Test]
		public void AnAuthoritativeInstall_BeforeItsContributor_OvershootsForExactlyOneInstall()
		{
			const int buffBonus = 25;

			// The server has applied the buff and reports a total that contains it. This peer has
			// not applied it yet, so the whole total lands in the authoritative residual.
			Attribute.SetModifier(buffBonus);
			LogAssert.AreEqual(buffBonus, Attribute.ExternalModifier, "The total is the total.");
			LogAssert.AreEqual(buffBonus, Attribute.GetSourceValue(ModifierSource.Authoritative),
				"With nothing attributed, the residual IS the total.");

			// Now this peer applies the same buff locally.
			Attribute.SetSource(ModifierSource.Buff(4242), buffBonus);
			LogAssert.AreEqual(buffBonus * 2, Attribute.ExternalModifier,
				"This is the overshoot, and it is structural: the residual was derived when nothing " +
				"was attributed, so the contributor is now counted twice.");

			// The next authoritative install re-derives the residual and closes it.
			Attribute.SetModifier(buffBonus);
			LogAssert.AreEqual(buffBonus, Attribute.ExternalModifier,
				"One install repairs it. If this reads 50 the residual is not being re-derived and " +
				"the overshoot is permanent rather than a flicker.");
			LogAssert.AreEqual(0, Attribute.GetSourceValue(ModifierSource.Authoritative),
				"...by closing the residual to nothing, because the peer's ledger now matches the " +
				"server's.");

			// And it does not oscillate: repeating the same total changes nothing.
			for (int i = 0; i < 10; ++i)
			{
				Attribute.SetModifier(buffBonus);
			}
			LogAssert.AreEqual(buffBonus, Attribute.ExternalModifier,
				"An unchanged authoritative total restated ten times is still one contribution.");
		}

		/// <summary>
		/// Installing the same total twice never accumulates, whatever is attributed underneath.
		/// </summary>
		/// <remarks>
		/// The owner reconciles every tick and most ticks carry an unchanged total, so this is the
		/// path the ledger spends nearly all of its time on. An install that added rather than
		/// stated would run away at thirty times a second.
		/// </remarks>
		[Test]
		public void RepeatedInstalls_AreIdempotent_UnderAnyAttributedMix()
		{
			Attribute.SetSource(ModifierSource.Item(1), 10);
			Attribute.SetSource(ModifierSource.Buff(2), 15);
			Attribute.SetSource(ModifierSource.Region(3), 5);

			for (int tick = 0; tick < 60; ++tick)
			{
				Attribute.SetModifier(30);
			}

			LogAssert.AreEqual(30, Attribute.ExternalModifier,
				"Sixty reconciles carrying the same total are one total.");
			LogAssert.AreEqual(0, Attribute.GetSourceValue(ModifierSource.Authoritative),
				"The residual is the difference between the server's total and what this peer has " +
				"attributed: 30 - 30 = 0.");

			// Releasing one attributed contributor must move the sheet by exactly that contributor.
			Attribute.ClearSourceGroup(ModifierSourceKind.Buff, 2);
			LogAssert.AreEqual(15, Attribute.ExternalModifier,
				"Dropping the buff drops its 15 and nothing else. A residual that had drifted would " +
				"show up here as a different number, which is why this assert follows the loop.");
		}

		/// <summary>
		/// The residual can go NEGATIVE, and must, when this peer has attributed more than the
		/// server's total.
		/// </summary>
		/// <remarks>
		/// The owner predicts an equip the server has not processed yet: the local ledger holds the
		/// bonus and the authoritative total does not. Clamping the residual at zero here would leave
		/// the predicted bonus standing against a server that had refused it — a client showing stats
		/// it does not have.
		/// </remarks>
		[Test]
		public void TheResidualGoesNegative_WhenThePeerHasAttributedMoreThanTheServerAgreesTo()
		{
			Attribute.SetSource(ModifierSource.Item(7), 60);
			LogAssert.AreEqual(60, Attribute.ExternalModifier, "The predicted equip shows at once.");

			// The server has not processed the equip, so its total is still zero.
			Attribute.SetModifier(0);
			LogAssert.AreEqual(0, Attribute.ExternalModifier,
				"The authoritative total wins outright: the peer predicted a bonus the server has " +
				"not granted.");
			LogAssert.AreEqual(-60, Attribute.GetSourceValue(ModifierSource.Authoritative),
				"...and it wins by carrying a NEGATIVE residual. Clamping this at zero would leave " +
				"the predicted 60 standing on a client whose server had refused it.");

			// The server catches up.
			Attribute.SetModifier(60);
			LogAssert.AreEqual(60, Attribute.ExternalModifier, "And the equip lands for real.");
			LogAssert.AreEqual(0, Attribute.GetSourceValue(ModifierSource.Authoritative),
				"The residual closes once the two ledgers agree.");
		}

		// ── stubs ──────────────────────────────────────────────────────────────

		/// <summary>Serves the one attribute the buff template writes to.</summary>
		private sealed class LedgerAttributeController : ICharacterAttributeController
		{
			public CharacterAttribute Attribute;

			public Dictionary<int, CharacterAttribute> Attributes { get; } = new Dictionary<int, CharacterAttribute>();
			public Dictionary<int, CharacterResourceAttribute> ResourceAttributes { get; } = new Dictionary<int, CharacterResourceAttribute>();

			public bool TryGetAttribute(int id, out CharacterAttribute attribute)
			{
				attribute = Attribute;
				return attribute != null;
			}

			public bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute)
			{
				attribute = Attribute;
				return attribute != null;
			}

			public bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute) { attribute = null; return false; }
			public bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute) { attribute = null; return false; }
			public bool TryGetHealthAttribute(out CharacterResourceAttribute health) { health = null; return false; }
			public bool TryGetManaAttribute(out CharacterResourceAttribute mana) { mana = null; return false; }
			public bool TryGetStaminaAttribute(out CharacterResourceAttribute stamina) { stamina = null; return false; }

			public void SetAttribute(int id, int value, int? modifier = null) { }
			public void SetResourceAttribute(int id, int value, float currentValue, int? modifier = null) { }
			public void AddAttribute(CharacterAttribute instance) { }
			public void ClearModifierSource(ModifierSource source) { }
			public void Regenerate(uint tick) { }
			public void ApplyResourceState(CharacterAttributeResourceState resourceState) { }
			public CharacterAttributeResourceState GetResourceState() => default;
			public bool IsPropagating => false;
			public void BeginPropagation() { }
			public void EndPropagation() { }
			public void EnqueueNotification(CharacterAttribute attribute) { }
			public void BeginNotificationSuppression() { }
			public void EndNotificationSuppression() { }

			public ICharacter OwningCharacter;
			public ICharacter Character => OwningCharacter;
			public bool Initialized => true;
			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
		}

		/// <summary>Resolves exactly the one controller the buff hooks ask for.</summary>
		private sealed class LedgerCharacter : ICharacter
		{
			private readonly ICharacterAttributeController attributes;

			public LedgerCharacter(ICharacterAttributeController attributes) => this.attributes = attributes;

			public long ID { get; set; } = 1L;
			public string Name => "LedgerCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
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

			/// <summary>No authored triggers on this stand-in; the attribute hooks raise none.</summary>
			public void Invoke(List<Trigger> triggers, EventData eventData) { }

			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				control = attributes as T;
				return control != null;
			}
		}
	}
}
