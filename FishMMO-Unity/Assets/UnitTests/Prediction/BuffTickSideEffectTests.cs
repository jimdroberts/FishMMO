using System;
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
	/// Pins which peer is allowed to have CONSEQUENCES when a damage-over-time or heal-over-time
	/// buff ticks.
	/// </summary>
	/// <remarks>
	/// <para>
	/// With state forwarding off, exactly two peers tick a player's buffs: the server, and the owning
	/// client predicting its own character. Both must run the resource mutation — that is what keeps
	/// predicted health in step with the server's — but only ONE may run what hangs off it: ECA tick
	/// triggers, combat entry, threat, kill credit, achievement progress. Those are not idempotent.
	/// </para>
	/// <para>
	/// The flag used to be <c>buff.IsReplaying</c> alone, which is not the same statement: the
	/// owner's FIRST pass over a tick is not a replay, and it fired every one of those consequences
	/// a second time alongside the server's. The rule is now "the server's first execution, and
	/// nothing else", spelled <see cref="Buff.IsAuthoritative"/> plus <see cref="Buff.IsReplaying"/>.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BuffTickSideEffectTests
	{
		private const float TickDelta30 = 1f / 30f;
		private const uint ApplyTick = 100u;

		private ProbeTickTemplate template;
		private CharacterAttributeTemplate healthTemplate;
		private ProbeTickTrigger trigger;

		[SetUp]
		public void SetUp()
		{
			healthTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			healthTemplate.name = "BuffTickSideEffect_Health";
			healthTemplate.AddToCache(healthTemplate.name);

			trigger = ScriptableObject.CreateInstance<ProbeTickTrigger>();
			trigger.name = "BuffTickSideEffect_Trigger";
			trigger.AddToCache(trigger.name);

			template = ScriptableObject.CreateInstance<ProbeTickTemplate>();
			template.name = "BuffTickSideEffect_Dot";
			template.Duration = 10f;
			template.TickRate = 1f;
			template.OnTickEvents = new List<BuffTickEvent>() { trigger };
			template.AddToCache(template.name);
		}

		[TearDown]
		public void TearDown()
		{
			template.RemoveFromCache();
			trigger.RemoveFromCache();
			healthTemplate.RemoveFromCache();
			UnityEngine.Object.DestroyImmediate(template);
			UnityEngine.Object.DestroyImmediate(trigger);
			UnityEngine.Object.DestroyImmediate(healthTemplate);
		}

		// ── Damage / heal suppression ────────────────────────────────────────────────

		/// <summary>
		/// The damage a DoT tick deals must be flagged "no side effects" on every client pass, and
		/// only on the server's first pass may it carry them.
		/// </summary>
		/// <remarks>
		/// The argument asserted here is the one <c>Damage</c> forwards to achievements, combat
		/// entry and the OnDamaged triggers. Getting it wrong does not desynchronise anything
		/// visible — it double-counts.
		/// </remarks>
		[Test]
		public void ResourceTick_Damage_PassesSuppressTriggers_OnEveryPassButTheServersFirst()
		{
			AssertDamageSuppression(isAuthoritative: true, isReplaying: false, expectedSuppressed: false,
				because: "The server's first execution of a tick is the one pass that is allowed to have consequences.");
			AssertDamageSuppression(isAuthoritative: true, isReplaying: true, expectedSuppressed: true,
				because: "A reconcile replay re-runs every tick since the last authoritative state; a single " +
					"tick of poison must not count a dozen times toward an achievement.");
			AssertDamageSuppression(isAuthoritative: false, isReplaying: false, expectedSuppressed: true,
				because: "The owning client predicts the same tick the server executes. Its first pass is not a " +
					"replay, and this is exactly the case IsReplaying alone could not express — it fired every " +
					"consequence a second time.");
			AssertDamageSuppression(isAuthoritative: false, isReplaying: true, expectedSuppressed: true,
				because: "A client replay is a client pass twice over.");
		}

		/// <summary>The healing half of the same rule.</summary>
		[Test]
		public void ResourceTick_Heal_PassesSuppressTriggers_OnEveryPassButTheServersFirst()
		{
			AssertHealSuppression(isAuthoritative: true, isReplaying: false, expectedSuppressed: false);
			AssertHealSuppression(isAuthoritative: false, isReplaying: false, expectedSuppressed: true);
			AssertHealSuppression(isAuthoritative: true, isReplaying: true, expectedSuppressed: true);
			AssertHealSuppression(isAuthoritative: false, isReplaying: true, expectedSuppressed: true);
		}

		/// <summary>
		/// The tick's resource mutation still runs on the client — only the consequences are gated.
		/// </summary>
		/// <remarks>
		/// If the client skipped the mutation entirely, predicted health would visibly jump on every
		/// reconcile for the whole duration of any DoT.
		/// </remarks>
		[Test]
		public void ResourceTick_StillMutatesTheResource_OnTheOwningClient()
		{
			Harness harness = new Harness(this);
			Buff buff = harness.MakeTickedBuff(isReplaying: false, isAuthoritative: false);

			template.RunResourceTick(buff, harness.Character, harness.DamageAttributes(-10), null);

			LogAssert.AreEqual(1, harness.Damage.DamageCalls,
				"The owning client must still apply the tick to its predicted health, or every DoT would " +
				"snap the health bar back on each reconcile.");
			LogAssert.AreEqual(10, harness.Damage.LastAmount, "The predicted amount must match the server's.");
		}

		// ── ECA trigger suppression ──────────────────────────────────────────────────

		/// <summary>
		/// A buff's authored tick events must fire on the server's first pass and nowhere else.
		/// </summary>
		/// <remarks>
		/// This is where a DoT's damage action, its threat, its chained debuffs and its achievement
		/// progress all live, so "fires once" is the entire correctness condition.
		/// </remarks>
		[Test]
		public void TickEvents_FireOnlyOnTheServersFirstPass()
		{
			AssertTickEventsFired(isAuthoritative: true, isReplaying: false, expectedExecutions: 1);
			AssertTickEventsFired(isAuthoritative: true, isReplaying: true, expectedExecutions: 0);
			AssertTickEventsFired(isAuthoritative: false, isReplaying: false, expectedExecutions: 0);
			AssertTickEventsFired(isAuthoritative: false, isReplaying: true, expectedExecutions: 0);
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private void AssertDamageSuppression(bool isAuthoritative, bool isReplaying, bool expectedSuppressed, string because)
		{
			Harness harness = new Harness(this);
			Buff buff = harness.MakeTickedBuff(isReplaying, isAuthoritative);

			template.RunResourceTick(buff, harness.Character, harness.DamageAttributes(-10), null);

			LogAssert.AreEqual(1, harness.Damage.DamageCalls,
				"A negative tick on the health resource must route through the damage pipeline, " +
				"or nothing can die of a DoT.");
			LogAssert.AreEqual(expectedSuppressed, harness.Damage.LastSuppressTriggers,
				$"authoritative={isAuthoritative}, replaying={isReplaying}: {because}");
		}

		private void AssertHealSuppression(bool isAuthoritative, bool isReplaying, bool expectedSuppressed)
		{
			Harness harness = new Harness(this);
			Buff buff = harness.MakeTickedBuff(isReplaying, isAuthoritative);

			template.RunResourceTick(buff, harness.Character, harness.DamageAttributes(8), null);

			LogAssert.AreEqual(1, harness.Damage.HealCalls, "A positive tick on health must route through Heal.");
			LogAssert.AreEqual(expectedSuppressed, harness.Damage.LastSuppressTriggers,
				$"Heal: authoritative={isAuthoritative}, replaying={isReplaying}.");
		}

		private void AssertTickEventsFired(bool isAuthoritative, bool isReplaying, int expectedExecutions)
		{
			Harness harness = new Harness(this);
			Buff buff = harness.MakeTickedBuff(isReplaying, isAuthoritative);

			trigger.Executions = 0;
			template.RunTickEvents(buff, harness.Character);

			LogAssert.AreEqual(expectedExecutions, trigger.Executions,
				$"authoritative={isAuthoritative}, replaying={isReplaying}: an ECA tick trigger is a side " +
				"effect and must run exactly once, on the server's first execution of the tick.");
		}

		/// <summary>Wires a character with the two controllers <c>ApplyResourceTick</c> resolves.</summary>
		private sealed class Harness
		{
			public readonly MockAttributeController Attributes;
			public readonly MockDamageController Damage;
			public readonly MockCharacter Character;

			private readonly BuffTickSideEffectTests owner;
			private readonly CharacterResourceAttribute health;

			public Harness(BuffTickSideEffectTests owner)
			{
				this.owner = owner;

				Attributes = new MockAttributeController();
				Damage = new MockDamageController();
				Character = new MockCharacter(1L, Attributes, Damage);

				/* The character has to exist before the resource does: CharacterResourceAttribute
				 * clamps through characterAttributeController.Character.Flags on construction, to
				 * decide whether a value above the maximum is a half-loaded character or an error. */
				Attributes.OwningCharacter = Character;
				Damage.OwningCharacter = Character;

				health = new CharacterResourceAttribute(Attributes, owner.healthTemplate.ID, 100, 100f, 0);
				Attributes.Resource = health;
				Damage.Resource = health;
			}

			/// <summary>
			/// Produces a buff whose replay and authority flags are set the way a real tick sets
			/// them — through <see cref="Buff.TryTick"/>, not by poking the fields.
			/// </summary>
			public Buff MakeTickedBuff(bool isReplaying, bool isAuthoritative)
			{
				Buff buff = new Buff(owner.template.ID, ApplyTick, TickDelta30);
				buff.TryTick(Character, ApplyTick, TickDelta30, isReplaying, isAuthoritative);

				LogAssert.AreEqual(isReplaying, buff.IsReplaying, "TryTick must record the replay state it was given.");
				LogAssert.AreEqual(isAuthoritative, buff.IsAuthoritative, "TryTick must record the authority it was given.");
				return buff;
			}

			public List<BuffAttributeTemplate> DamageAttributes(int value)
			{
				return new List<BuffAttributeTemplate>()
				{
					new BuffAttributeTemplate() { Value = value, Template = owner.healthTemplate },
				};
			}
		}

		/// <summary>Exposes the protected tick helpers so a test can call them directly.</summary>
		private sealed class ProbeTickTemplate : BaseBuffTemplate
		{
			public override void OnApply(Buff buff, ICharacter target) { }
			public override void OnRemove(Buff buff, ICharacter target) { }

			public void RunResourceTick(Buff buff, ICharacter target, List<BuffAttributeTemplate> tickAttributes, DamageAttributeTemplate damageAttribute)
			{
				ApplyResourceTick(buff, target, tickAttributes, damageAttribute);
			}

			public void RunTickEvents(Buff buff, ICharacter target)
			{
				InvokeTickEvents(buff, target);
			}
		}

		/// <summary>A tick event that counts how many times it was actually executed.</summary>
		private sealed class ProbeTickTrigger : BuffTickEvent
		{
			public int Executions;

			public override void Execute(EventData eventData)
			{
				++Executions;
			}
		}

		/// <summary>Records the arguments the damage pipeline was called with.</summary>
		private sealed class MockDamageController : ICharacterDamageController
		{
			public CharacterResourceAttribute Resource;

			public int DamageCalls;
			public int HealCalls;
			public int LastAmount;
			public bool LastSuppressTriggers;

			public void Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute, bool ignoreAchievements = false)
			{
				++DamageCalls;
				LastAmount = amount;
				LastSuppressTriggers = ignoreAchievements;
			}

			public void Heal(ICharacter healer, int amount, bool ignoreAchievements = false)
			{
				++HealCalls;
				LastAmount = amount;
				LastSuppressTriggers = ignoreAchievements;
			}

			public bool Immortal { get; set; }
			public bool IsAlive => true;
			public CharacterResourceAttribute ResourceInstance => Resource;
			public List<Trigger> OnDamageTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnDamagedTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnHealTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnHealedTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnKillTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnKilledTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnResurrectTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnResurrectedTriggers { get; } = new List<Trigger>();
			public void Kill(ICharacter killer) { }
			public void CompleteHeal() { }
			public void Revive(ICharacter resurrector, int amount) { }
			public bool IsInCombat => false;
			public uint LastCombatTick => 0u;
			public uint CombatDurationTicks => 0u;
			public void EnterCombat() { }
			public void RecordCombatContribution(ICharacter contributor, CombatContributionKind kind) { }
			public void PropagateCombatContribution(ICharacter supporter) { }
			public bool TryConsumeContributors(out List<long> contributors) { contributors = null; return false; }
			public bool HasCombatContributor(long characterID) => false;
			public void ClearCombatContributions() { }

			public ICharacter OwningCharacter;
			public ICharacter Character => OwningCharacter;
			public bool Initialized => true;
			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
		}

		/// <summary>Serves exactly one resource attribute — the health the tick lands on.</summary>
		private sealed class MockAttributeController : ICharacterAttributeController
		{
			public CharacterResourceAttribute Resource;

			public Dictionary<int, CharacterAttribute> Attributes { get; } = new Dictionary<int, CharacterAttribute>();
			public Dictionary<int, CharacterResourceAttribute> ResourceAttributes { get; } = new Dictionary<int, CharacterResourceAttribute>();

			public bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute)
			{
				attribute = Resource;
				return attribute != null;
			}

			public bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute)
			{
				attribute = Resource;
				return attribute != null;
			}

			public void SetAttribute(int id, int value, int? modifier = null) { }
			public void SetResourceAttribute(int id, int value, float currentValue, int? modifier = null) { }
			public bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute) { attribute = null; return false; }
			public bool TryGetAttribute(int id, out CharacterAttribute attribute) { attribute = null; return false; }
			public bool TryGetHealthAttribute(out CharacterResourceAttribute health) { health = Resource; return health != null; }
			public bool TryGetManaAttribute(out CharacterResourceAttribute mana) { mana = null; return false; }
			public bool TryGetStaminaAttribute(out CharacterResourceAttribute stamina) { stamina = null; return false; }
			public void AddAttribute(CharacterAttribute instance) { }

			/// <summary>No ledger on a stub; nothing to release.</summary>
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

		/// <summary>Character stand-in that resolves the two controllers the tick path asks for.</summary>
		private sealed class MockCharacter : ICharacter
		{
			private readonly ICharacterAttributeController attributes;
			private readonly ICharacterDamageController damage;

			public MockCharacter(long id, ICharacterAttributeController attributes, ICharacterDamageController damage)
			{
				ID = id;
				this.attributes = attributes;
				this.damage = damage;
			}

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

			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				if (typeof(T) == typeof(ICharacterAttributeController))
				{
					control = attributes as T;
					return control != null;
				}
				if (typeof(T) == typeof(ICharacterDamageController))
				{
					control = damage as T;
					return control != null;
				}
				control = null;
				return false;
			}

			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
