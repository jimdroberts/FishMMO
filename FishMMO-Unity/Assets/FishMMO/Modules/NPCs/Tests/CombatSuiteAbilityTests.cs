using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the crowd-control, over-time, projectile-behaviour and defence abilities: the held guard's rule,
	/// the fork's ids, who a charm may take, and the content TestAbilitySuiteGenerator.CombatSuite writes for Stunning Blow,
	/// Freezing Bolt, Mesmerize, Ignite, Rejuvenate, Ricochet Shot, Forked Bolt, Chain Lightning, Shield Block and Deflect.
	/// Also that the old Mock ability set is gone.
	/// </summary>
	[TestFixture]
	public class CombatSuiteAbilityTests
	{
		private const string Types = "Assets/FishMMO/Modules/Abilities/Templates/Types/";
		private const string Buffs = "Assets/FishMMO/Modules/Buffs/Templates/";
		private const string Merchant = "Assets/FishMMO/Modules/Commerce/Templates/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";
		private const string Premades = "Assets/FishMMO/Modules/Commerce/Templates/Premade/";
		private const string Stamina = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Resource/Stamina.asset";
		private const string AnimationSet = "Assets/Prefabs/Client/Models/TestHuman/TestHuman Animations.asset";

		private static readonly string[] AbilityPaths =
		{
			"Control/Stunning Blow", "Control/Freezing Bolt", "Control/Mesmerize", "Over Time/Ignite", "Over Time/Rejuvenate",
			"Over Time/Rejuvenate Self", "Projectile/Ricochet Shot", "Projectile/Forked Bolt", "Projectile/Chain Lightning",
			"Defense/Shield Block", "Defense/Deflect",
			"Stealth/Invisibility", "Stealth/See Invisible", "Melee/Stab", "Melee/Swipe", "Melee/Disrupting Strike", "Melee/Hamstring",
			"Utility/Provoke", "Utility/Arcane Ward", "Projectile/Piercing Arrow", "Projectile/Crossbow Shot", "Projectile/Drain Life",
			"Beam/Searing Beam",
		};

		private const string Events = "Assets/FishMMO/Modules/Abilities/Templates/Events/";

		private static AbilityEvent Effect(string path) => LoadAsset<AbilityEvent>(Events + path + ".asset");

		private static readonly string[] BuffNames = { "Stunned", "Frozen", "Mesmerized", "Burning", "Rejuvenation", "Deflecting",
			"Invisible", "See Invisible", "Hamstrung", "Arcane Ward", "Pet Regeneration" };

		private static T LoadAsset<T>(string path) where T : Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(ProjectFiles.Path(path));
			if (asset == null)
			{
				Assert.Inconclusive($"'{path}' has not been generated (TestAbilitySuiteGenerator.GenerateCombatSuiteOnly).");
			}
			return asset;
		}

		private static AbilityTemplate Ability(string name) => LoadAsset<AbilityTemplate>(Types + name + ".asset");

		private static T Buff<T>(string name) where T : BaseBuffTemplate => LoadAsset<T>(Buffs + name + ".asset");

		// ── The held guard ───────────────────────────────────────────────────────────────────

		[Test]
		public void Guard_ReducesByPercent_RoundingDown()
		{
			Assert.That(AbilityGuard.ResolveBlocked(20, 75), Is.EqualTo(15));
			Assert.That(AbilityGuard.ResolveBlocked(1, 75), Is.EqualTo(0), "a 1-point blow lands whole: chip damage stays");
			Assert.That(AbilityGuard.ResolveBlocked(10, 150), Is.EqualTo(10), "never more than arrived");
			Assert.That(AbilityGuard.ResolveBlocked(10, 0), Is.EqualTo(0));
			Assert.That(AbilityGuard.ResolveBlocked(-4, 75), Is.EqualTo(0));
		}

		[Test]
		public void Guard_CostsFlatPlusPerPointBlocked()
		{
			AbilityGuard guard = new AbilityGuard { CostPerHit = 5, CostPerDamage = 0.5f };
			Assert.That(guard.ResolveCost(15), Is.EqualTo(5 + 8), "half a point per point blocked, rounded up");
			Assert.That(guard.ResolveCost(0), Is.EqualTo(0), "nothing blocked, nothing spent");
			Assert.That(new AbilityGuard { CostPerHit = 0, CostPerDamage = 0f }.ResolveCost(30), Is.EqualTo(0));
		}

		[Test]
		public void Guard_HoldsOnlyAgainstTheFront()
		{
			Vector3 forward = Vector3.forward;
			Assert.That(HeldGuardMitigation.IsInFront(forward, new Vector3(0f, 0f, 5f), 150f), Is.True);
			Assert.That(HeldGuardMitigation.IsInFront(forward, new Vector3(3f, 0f, 4f), 150f), Is.True, "37 degrees off");
			Assert.That(HeldGuardMitigation.IsInFront(forward, new Vector3(5f, 0f, 0f), 150f), Is.False, "beside: 90 > 75");
			Assert.That(HeldGuardMitigation.IsInFront(forward, new Vector3(0f, 0f, -5f), 150f), Is.False, "behind");
			Assert.That(HeldGuardMitigation.IsInFront(forward, new Vector3(0f, 8f, 1f), 150f), Is.True, "height does not matter");
			Assert.That(HeldGuardMitigation.IsInFront(forward, Vector3.zero, 150f), Is.False, "no direction, no block");
			Assert.That(HeldGuardMitigation.IsInFront(forward, new Vector3(0f, 0f, -5f), 360f), Is.True);
		}

		[Test]
		public void Guard_IsInstalledAsTheCoreHook()
		{
			// RuntimeInitializeOnLoad / InitializeOnLoad install it; the editor has loaded by the time tests run.
			Assert.That(DamageMitigation.GuardNegator, Is.Not.Null, "the Abilities module installs the held guard");
		}

		// ── The fork ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void ForkIds_AreAPureFunction_UniqueAndClearOfSpawnIds()
		{
			HashSet<int> seen = new HashSet<int>();
			for (int parent = 0; parent < 16; ++parent)
			{
				for (int index = 0; index < AbilityObject.MaxForks; ++index)
				{
					int id = AbilityObject.ForkChildID(parent, index);
					Assert.That(id, Is.EqualTo(AbilityObject.ForkChildID(parent, index)), "same hit, same fork, every peer");
					Assert.That(id, Is.GreaterThanOrEqualTo(AbilityObject.ForkIdBase), "never a root's or a multiply's id");
					Assert.That(seen.Add(id), Is.True, $"fork id {id} repeated");
				}
			}
		}

		[Test]
		public void ForkSplit_DoesNothingWithoutAnObject()
		{
			AbilityForkSplitAction split = new AbilityForkSplitAction();
			Assert.DoesNotThrow(() => split.Execute(null, null));
			Assert.That(split.OnlyOnCharacters, Is.True, "a bolt that strikes a wall just ends by default");
		}

		// ── The charm ────────────────────────────────────────────────────────────────────────

		[Test]
		public void Charm_TakesOnlyCharmableNpcsNobodyElseHolds()
		{
			List<GameObject> made = new List<GameObject>();
			try
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
				NPC Make(string name, bool charmable)
				{
					GameObject go = new GameObject(name);
					made.Add(go);
					NPC npc = go.AddComponent<NPC>();
					npc.IsCharmable = charmable;
					return npc;
				}
				NPC orc = Make("Orc", true);
				NPC townsfolk = Make("Townsfolk", false);
				NPC caster = Make("Caster", false);
				NPC rival = Make("Rival", false);
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

				Assert.That(AIController.MayCharm(orc, caster, alive: true), Is.True);
				Assert.That(AIController.MayCharm(orc, caster, alive: false), Is.False, "not a corpse");
				Assert.That(AIController.MayCharm(townsfolk, caster, alive: true), Is.False, "not authored charmable");
				Assert.That(AIController.MayCharm(orc, orc, alive: true), Is.False, "not itself");
				Assert.That(AIController.MayCharm(null, caster, alive: true), Is.False);

				orc.CharmedBy = rival;
				Assert.That(orc.CompanionOwner, Is.SameAs(rival), "a charmed NPC is its charmer's to the harm rule");
				Assert.That(AIController.MayCharm(orc, caster, alive: true), Is.False, "held by another charmer");
				Assert.That(CanBeCharmedCondition.MayCharm(orc, caster), Is.False, "the selector says the same");
				Assert.That(AIController.MayCharm(orc, rival, alive: true), Is.True, "its own charmer may refresh it");
				orc.CharmedBy = null;
				Assert.That(orc.CompanionOwner, Is.Null);
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
				foreach (GameObject go in made)
				{
					Object.DestroyImmediate(go);
				}
			}
		}

		[Test]
		public void Charm_IsNotTheIncapacitatingMesmerizeFlag()
		{
			/* CharacterFlags.IsMesmerized means "cannot act" (CharacterIncapacitation). A charmed NPC must act — for its
			 * charmer — so the charm sets no state flag at all. */
			Assert.That(typeof(StateBuffTemplate).IsAssignableFrom(typeof(CharmBuffTemplate)), Is.False);
			Assert.That(typeof(FishMMO.Shared.Core.INPCCharm).IsAssignableFrom(typeof(AIController)), Is.True);
		}

		// ── The content ──────────────────────────────────────────────────────────────────────

		[Test]
		public void Buffs_CarryTheirStatesAndTicks()
		{
			StateBuffTemplate stunned = Buff<StateBuffTemplate>("Stunned");
			Assert.That(stunned.Flag, Is.EqualTo(CharacterFlags.IsStunned));
			Assert.That(stunned.IsDebuff, Is.True);

			StateBuffTemplate frozen = Buff<StateBuffTemplate>("Frozen");
			Assert.That(frozen.Flag, Is.EqualTo(CharacterFlags.IsFrozen));
			GameObject ice = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(frozen.FXPrefabReference.AssetGUID));
			Assert.That(ice, Is.Not.Null, "the freeze wears its ice");
			Assert.That(ice.GetComponent<FXFreezePose>(), Is.Not.Null, "and the ice holds the body's pose");

			Assert.That(Buff<BaseBuffTemplate>("Mesmerized"), Is.InstanceOf<CharmBuffTemplate>());

			ResourceTickBuffTemplate burning = Buff<ResourceTickBuffTemplate>("Burning");
			Assert.That(burning.IsDebuff, Is.True);
			Assert.That(burning.DamageAttribute, Is.Not.Null, "a burn is fire damage, so resistance applies");
			Assert.That(burning.TickAttributes.Single().Value, Is.LessThan(0));

			ResourceTickBuffTemplate rejuvenation = Buff<ResourceTickBuffTemplate>("Rejuvenation");
			Assert.That(rejuvenation.IsDebuff, Is.False);
			Assert.That(rejuvenation.TickAttributes.Single().Value, Is.GreaterThan(0));

			DeflectBuffTemplate deflecting = Buff<DeflectBuffTemplate>("Deflecting");
			Assert.That(deflecting.Duration, Is.LessThanOrEqualTo(0.5f), "a short window, not a stance");
			Assert.That(deflecting.MaxDeflections, Is.EqualTo(0), "everything inside the window");
		}

		[Test]
		public void ShieldBlock_IsAHeldGuardThatReducesAndCostsStamina()
		{
			AbilityTemplate block = Ability("Defense/Shield Block");
			CharacterAttributeTemplate stamina = LoadAsset<CharacterAttributeTemplate>(Stamina);
			Assert.That(block.Type, Is.EqualTo(AbilityType.Block), "a Block ability is held (AbilityController.RequiresHeld)");
			Assert.That(block.Guard.ReducePercent, Is.InRange(1, 99), "a guard reduces, it does not negate");
			Assert.That(block.Guard.CostResource, Is.SameAs(stamina), "each blocked blow costs stamina");
			Assert.That(block.Guard.CostPerHit, Is.GreaterThan(0));
			Assert.That(block.AbilityObjectPrefab, Is.Null, "the guard spawns nothing");
		}

		[Test]
		public void Deflect_IsAQuickStaminaParry()
		{
			AbilityTemplate deflect = Ability("Defense/Deflect");
			Assert.That(deflect.Type, Is.EqualTo(AbilityType.Deflect), "plays the parry animation");
			Assert.That(deflect.ActivationTime, Is.EqualTo(0f));
			Assert.That(deflect.ActivationConditions.OfType<HasResourceCondition>().Single().Template.name, Is.EqualTo("Stamina"));
			Assert.That(deflect.OnHitEvents.Single().OnConditionsMetActions.OfType<ApplyBuffAction>().Single().BuffTemplate,
				Is.InstanceOf<DeflectBuffTemplate>());
		}

		[Test]
		public void ProjectileBehaviours_AreWired()
		{
			AbilityTemplate ricochet = Ability("Projectile/Ricochet Shot");
			Assert.That(ricochet.HitCount, Is.GreaterThan(1), "a ricochet survives the hit that turns it");
			AbilityRicochetHitAction bounce = ricochet.OnHitEvents.SelectMany(e => e.OnConditionsMetActions).OfType<AbilityRicochetHitAction>().Single();
			Assert.That(bounce.ReflectOffScenery, Is.True);

			AbilityTemplate fork = Ability("Projectile/Forked Bolt");
			Assert.That(fork.OnHitEvents.SelectMany(e => e.OnConditionsMetActions).OfType<AbilityForkSplitAction>().Count(), Is.EqualTo(1));

			AbilityTemplate chain = Ability("Projectile/Chain Lightning");
			Assert.That(chain.OnHitEvents.Any(e => e.TargetSelector is ChainTargetSelector), Is.True);
			Assert.That(chain.OnHitEvents.SelectMany(e => e.OnConditionsMetActions).OfType<PlayChainArcFXAction>().Single().ArcPrefab
				.GetComponent<LightningArc>(), Is.Not.Null, "observers draw the leaps");

			AbilityTemplate mesmerize = Ability("Control/Mesmerize");
			Assert.That(mesmerize.OnHitEvents.Single().TargetSelector.Conditions.OfType<CanBeCharmedCondition>().Count(), Is.EqualTo(1),
				"never a player, pet or wild animal");
		}

		[Test]
		public void EverySuiteAbilityAndBuff_HasAnIcon_IsSold_AndHasAPremade()
		{
			MerchantTemplate merchant = LoadAsset<MerchantTemplate>(Merchant);
			foreach (string path in AbilityPaths)
			{
				AbilityTemplate ability = Ability(path);
				Assert.That(merchant.Abilities, Does.Contain(ability), $"{ability.name} on General Goods");
				Assert.That(AssetDatabase.LoadAssetAtPath<PremadeAbilityTemplate>(ProjectFiles.Path($"{Premades}Premade {ability.name}.asset")),
					Is.Not.Null, $"Premade {ability.name}");
				Assert.That(new SerializedObject(ability).FindProperty("icon").FindPropertyRelative("m_AssetGUID").stringValue,
					Is.Not.Empty, $"{ability.name} icon (FishMMO-Art/Icons/generate_ability_icons.py --assign)");
			}
			foreach (string name in BuffNames)
			{
				BaseBuffTemplate buff = Buff<BaseBuffTemplate>(name);
				Assert.That(new SerializedObject(buff).FindProperty("icon").FindPropertyRelative("m_AssetGUID").stringValue,
					Is.Not.Empty, $"{name} icon");
			}
		}

		[Test]
		public void TheParryClip_IsInTheAnimationSet()
		{
			CharacterAnimationSet set = LoadAsset<CharacterAnimationSet>(AnimationSet);
			Assert.That(set.Deflect, Is.Not.Null, "TestHuman.fbx's Deflect take (memory tools autorig/anim.py)");
			Assert.That(set.Block, Is.Not.Null);
		}

		// ── Stealth, skirmish, bases and effects (second round) ───────────────────────────────

		[Test]
		public void Behind_IsMeasuredAgainstTheTargetsBack()
		{
			Vector3 facing = Vector3.forward; // the target looks along +Z
			Assert.That(IsBehindTargetCondition.IsBehind(facing, new Vector3(0f, 0f, -2f), 120f), Is.True, "straight behind");
			Assert.That(IsBehindTargetCondition.IsBehind(facing, new Vector3(-1f, 0f, -1.5f), 120f), Is.True, "behind and to one side");
			Assert.That(IsBehindTargetCondition.IsBehind(facing, new Vector3(2f, 0f, 0f), 120f), Is.False, "beside: 90 > 60");
			Assert.That(IsBehindTargetCondition.IsBehind(facing, new Vector3(0f, 0f, 2f), 120f), Is.False, "in front");
			Assert.That(IsBehindTargetCondition.IsBehind(facing, new Vector3(0f, 3f, -1f), 120f), Is.True, "height does not matter");
			Assert.That(IsBehindTargetCondition.IsBehind(facing, Vector3.zero, 120f), Is.False, "on top of it: no back to stab");
		}

		[Test]
		public void Invisibility_IsAStateTheAiReads_EndsOnAction_AndHidesOnEveryClient()
		{
			StateBuffTemplate invisible = Buff<StateBuffTemplate>("Invisible");
			Assert.That(invisible.Flag, Is.EqualTo(CharacterFlags.IsInvisible), "the NPC sweep skips it");
			Assert.That(invisible.BreaksOnAction, Is.True, "an ability or a consumable ends it");
			Assert.That(invisible.Duration, Is.EqualTo(60f));
			GameObject fx = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(invisible.FXPrefabReference.AssetGUID));
			Assert.That(fx.GetComponent<FXConcealment>(), Is.Not.Null, "model faded, nameplate hidden");
			Assert.That(CharacterAnimationController.ConcealedOpacity, Is.EqualTo(0.2f).Within(1e-4f), "80% invisible");

			StateBuffTemplate sees = Buff<StateBuffTemplate>("See Invisible");
			Assert.That(sees.Flag, Is.EqualTo(CharacterFlags.SeesInvisible));
			Assert.That(sees.BreaksOnAction, Is.False);
			GameObject sight = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(sees.FXPrefabReference.AssetGUID));
			Assert.That(sight.GetComponent<FXSeeInvisible>(), Is.Not.Null);
		}

		[Test]
		public void SeeInvisible_IsAClientViewToggle()
		{
			ConcealmentView.Reset();
			int changes = 0;
			void Count() => changes++;
			ConcealmentView.Changed += Count;
			try
			{
				ConcealmentView.HoldSeeInvisible(true);
				ConcealmentView.HoldSeeInvisible(true);
				Assert.That(ConcealmentView.SeesInvisible, Is.True);
				ConcealmentView.HoldSeeInvisible(false);
				Assert.That(ConcealmentView.SeesInvisible, Is.True, "counted: one hold is still up");
				ConcealmentView.HoldSeeInvisible(false);
				Assert.That(ConcealmentView.SeesInvisible, Is.False);
				Assert.That(changes, Is.EqualTo(2), "told once when it turned on and once when it turned off");
			}
			finally
			{
				ConcealmentView.Changed -= Count;
				ConcealmentView.Reset();
			}
		}

		[Test]
		public void PetRegeneration_IsASummonEffect_ThatLastsForever()
		{
			AbilityEvent effect = Effect("Summon/Pet Regeneration");
			Assert.That(effect, Is.InstanceOf<AbilityOnSummonEvent>(), "crafted onto a summon, nothing else");
			AttributeBuffTemplate regen = (AttributeBuffTemplate)effect.OnConditionsMetActions.OfType<ApplyBuffAction>().Single().BuffTemplate;
			Assert.That(regen.IsPermanent, Is.True);
			Assert.That(regen.BonusAttributes.Single().Template.name, Is.EqualTo("Health Regeneration"));
			Assert.That(regen.BonusAttributes.Single().Value, Is.EqualTo(10), "regeneration is authored per five seconds: 10 is 2 a second");
		}

		[Test]
		public void Backstab_IsAPositionalEffect_OnAStabBase()
		{
			AbilityEvent backstab = Effect("Hit/Stealth/Backstab");
			Assert.That(backstab.TargetSelector.Conditions.OfType<IsBehindTargetCondition>().Count(), Is.EqualTo(1));
			Assert.That(backstab.OnConditionsMetActions.OfType<ApplyDamageAction>().Count(), Is.EqualTo(1), "extra damage from behind");

			AbilityTemplate stab = Ability("Melee/Stab");
			Assert.That(stab.AdditionalEventSlots, Is.GreaterThanOrEqualTo(1), "room for Backstab or any other effect");
			Assert.That(stab.OnHitEvents, Has.No.Member(backstab), "the base itself does not care where you stand");

			PremadeAbilityTemplate premade = LoadAsset<PremadeAbilityTemplate>(Premades + "Premade Backstab.asset");
			Assert.That(premade.Ability, Is.SameAs(stab));
			Assert.That(premade.Events, Does.Contain(backstab));
			Assert.That(premade.Validate(out string reason), Is.True, reason);
		}

		[Test]
		public void Riders_AreEffects_BundledOnTheirBases()
		{
			(string ability, string effect)[] pairs =
			{
				("Control/Stunning Blow", "Control/Stun"), ("Control/Freezing Bolt", "Control/Freeze"), ("Over Time/Ignite", "Hit/Over Time/Burn"),
				("Melee/Disrupting Strike", "Hit/Melee/Interrupt"), ("Melee/Hamstring", "Hit/Melee/Hamstring"),
				("Projectile/Drain Life", "Hit/Projectile/Lifesteal"),
			};
			MerchantTemplate merchant = LoadAsset<MerchantTemplate>(Merchant);
			foreach ((string ability, string effect) in pairs)
			{
				AbilityTemplate template = Ability(ability);
				AbilityEvent rider = Effect(effect);
				Assert.That(template.OnHitEvents, Does.Contain(rider), $"{template.name} carries {rider.name}");
				Assert.That(rider.OnConditionsMetActions.OfType<ApplyDamageAction>().Any(), Is.False, $"{rider.name} adds its rider, not the base's damage");
				Assert.That(merchant.AbilityEvents, Does.Contain(rider), $"{rider.name} is sold on its own");
				Assert.That(template.AdditionalEventSlots, Is.GreaterThanOrEqualTo(1), $"{template.name} is a base with a slot");
			}
			// Lifesteal heals the caster, on a hostile hit only.
			AbilityEvent lifesteal = Effect("Hit/Projectile/Lifesteal");
			Assert.That(lifesteal.OnConditionsMetActions.OfType<ApplyHealAction>().Single().TargetSelector, Is.InstanceOf<InitiatorTargetSelector>());
		}

		[Test]
		public void Swipe_IsAFrontalArcOnAPointBlankArea()
		{
			AbilityTemplate swipe = Ability("Melee/Swipe");
			Assert.That(swipe.AbilitySpawnTarget, Is.EqualTo(AbilitySpawnTarget.PointBlank));
			AbilityApplyAreaAction area = swipe.OnSpawnEvents.SelectMany(e => e.OnConditionsMetActions).OfType<AbilityApplyAreaAction>().Single();
			Assert.That(area.ArcDegrees, Is.LessThan(360f), "a cone in front, not a ring");
			Assert.That(new AbilityApplyAreaAction().ArcDegrees, Is.EqualTo(360f), "every other area keeps its full ring");
		}

		[Test]
		public void SearingBeam_IsHeldPulsedAndPaysEveryPulse()
		{
			AbilityTemplate beam = Ability("Beam/Searing Beam");
			Assert.That(beam.PulseSeconds, Is.GreaterThan(0f), "held and pulsed (AbilityController.RequiresHeld)");
			Assert.That(beam.ActivationTime, Is.GreaterThan(beam.PulseSeconds), "the longest hold holds many pulses");
			Assert.That(beam.ActivationConditions.OfType<HasResourceCondition>().Single().Template.name, Is.EqualTo("Mana"),
				"a magic beam burns mana, paid every pulse");
			Assert.That(beam.Speed, Is.EqualTo(0f), "the beam is a hitscan; the point object does not travel");
			List<BaseAction> pulse = beam.OnSpawnEvents.SelectMany(e => e.OnConditionsMetActions).ToList();
			Assert.That(pulse.OfType<AbilityApplyHitscanAction>().Count(), Is.EqualTo(1), "each pulse is a hitscan");
			Assert.That(pulse.OfType<PlayBeamFXAction>().Single().BeamPrefab.GetComponent<LightningArc>(), Is.Not.Null, "and draws the beam");
		}

		[Test]
		public void TheMockSet_IsGone()
		{
			string[] found = AssetDatabase.FindAssets("Mock t:ScriptableObject", new[] { "Assets/FishMMO/Modules/Abilities/Templates", "Assets/FishMMO/Modules/Buffs/Templates" })
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(p => System.IO.Path.GetFileName(p).StartsWith("Mock "))
				.ToArray();
			Assert.That(found, Is.Empty, "the Mock abilities, events and buffs were deleted");
		}
	}
}
