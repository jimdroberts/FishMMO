using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The runtime contract every shipped ability template must meet to do anything at all.
	/// </summary>
	/// <remarks>
	/// Written after the 2026-09-03 audit found "Lesser Fireball" with Speed 0 and no move event
	/// (a stationary ball), a hit event whose mana condition tested the VICTIM, and "Lesser
	/// Flame" with no events whatsoever. Each rule below is one of those failures generalised.
	/// Mock content under <c>Abilities/Mock</c> is excluded; it exercises the runtime, not players.
	/// </remarks>
	[TestFixture]
	public class AbilityAuthoringContractTests
	{
		private const string TypesRoot = "Assets/Templates/Entity/Abilities/Types";

		private static List<AbilityTemplate> ShippedAbilities()
		{
			List<AbilityTemplate> results = new List<AbilityTemplate>();
			foreach (string guid in AssetDatabase.FindAssets("t:AbilityTemplate", new[] { TypesRoot }))
			{
				AbilityTemplate template = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (template != null)
				{
					results.Add(template);
				}
			}
			return results;
		}

		private static bool HasMoveAction(AbilityTemplate template)
		{
			if (template.OnTickEvents == null) return false;
			foreach (AbilityOnTickEvent tick in template.OnTickEvents)
			{
				if (tick == null || tick.OnConditionsMetActions == null) continue;
				foreach (BaseAction action in tick.OnConditionsMetActions)
				{
					if (action is AbilityMoveTransformAction move && move.MoveDirection.sqrMagnitude > 0f)
					{
						return true;
					}
				}
			}
			return false;
		}

		private static int EventCount(AbilityTemplate t)
		{
			return (t.OnHitEvents?.Count ?? 0) + (t.OnTickEvents?.Count ?? 0) + (t.OnSpawnEvents?.Count ?? 0);
		}

		[Test]
		public void ShippedAbilitiesExist()
		{
			Assert.That(ShippedAbilities(), Is.Not.Empty);
		}

		[Test]
		public void EveryAbility_DoesSomething()
		{
			List<string> offenders = new List<string>();
			foreach (AbilityTemplate t in ShippedAbilities())
			{
				// A summon acts through the pet system on activation; it needs no events.
				if (t is PetAbilityTemplate)
				{
					continue;
				}
				if (EventCount(t) == 0 && t.AdditionalEventSlots == 0)
				{
					offenders.Add(t.name);
				}
			}
			Assert.That(offenders, Is.Empty, "no hit, tick or spawn events and no crafting slots: the ability can never act");
		}

		[Test]
		public void SpeedAndMoveEvent_AgreeOnWhetherTheObjectTravels()
		{
			List<string> offenders = new List<string>();
			foreach (AbilityTemplate t in ShippedAbilities())
			{
				bool travels = t.Speed > 0f && t.AbilityObjectPrefab != null;
				bool moves = HasMoveAction(t);
				if (travels != moves)
				{
					offenders.Add($"{t.name} (Speed {t.Speed}, prefab {(t.AbilityObjectPrefab ? "set" : "none")}, move event {moves})");
				}
			}
			Assert.That(offenders, Is.Empty,
				"Speed only sets Range; the object travels only if a tick event carries AbilityMoveTransformAction. A mismatch is a fireball that never leaves the hand, or a punch that flies away.");
		}

		[Test]
		public void EverySpawningAbility_HasAUsableObjectPrefab()
		{
			List<string> offenders = new List<string>();
			foreach (AbilityTemplate t in ShippedAbilities())
			{
				if (t.AbilityObjectPrefab == null)
				{
					continue;
				}
				GameObject prefab = t.AbilityObjectPrefab;
				if (prefab.GetComponent<AbilityObject>() == null) offenders.Add($"{t.name}: prefab lacks AbilityObject");
				if (prefab.GetComponent<Collider>() == null) offenders.Add($"{t.name}: prefab lacks a Collider (nothing to sweep with)");
				Rigidbody body = prefab.GetComponent<Rigidbody>();
				if (body == null || !body.isKinematic || body.useGravity) offenders.Add($"{t.name}: prefab needs a kinematic, gravity-free Rigidbody");
				if (t.LifeTime <= 0f) offenders.Add($"{t.name}: LifeTime 0 with a prefab — the object is destroyed the tick it spawns");
				if (t.HitCount < 1) offenders.Add($"{t.name}: HitCount < 1 with a prefab — it can never register a hit");
			}
			Assert.That(offenders, Is.Empty);
		}

		[Test]
		public void HitEvents_NeverGateOnTheVictimsResources()
		{
			/* HasResourceCondition checks the event's target; on a hit that is the victim. With no
			 * InitiatorTargetSelector the condition asks whether the VICTIM can afford the cast,
			 * and everyone below the threshold is immune. Costs belong in ActivationConditions. */
			List<string> offenders = new List<string>();
			foreach (AbilityTemplate t in ShippedAbilities())
			{
				if (t.OnHitEvents == null) continue;
				foreach (AbilityOnHitEvent hit in t.OnHitEvents)
				{
					if (hit == null || hit.Conditions == null) continue;
					foreach (BaseCondition c in hit.Conditions)
					{
						if (c is HasResourceCondition && !(c.TargetSelector is InitiatorTargetSelector))
						{
							offenders.Add($"{t.name} → {hit.name}");
						}
					}
				}
			}
			Assert.That(offenders, Is.Empty);
		}

		[Test]
		public void FxActions_NeverInstantiateAnAbilityObject()
		{
			// Playing an FX by instantiating an ability prefab spawns an orphan AbilityObject per hit.
			List<string> offenders = new List<string>();
			foreach (AbilityTemplate t in ShippedAbilities())
			{
				foreach (AbilityEvent e in AllEvents(t))
				{
					foreach (BaseAction a in e.OnConditionsMetActions)
					{
						if (a is PlayFXAction fx && fx.FXPrefab != null && fx.FXPrefab.GetComponent<AbilityObject>() != null)
						{
							offenders.Add($"{t.name} → {e.name}");
						}
					}
				}
			}
			Assert.That(offenders, Is.Empty);
		}

		private static IEnumerable<AbilityEvent> AllEvents(AbilityTemplate t)
		{
			if (t.OnHitEvents != null) foreach (AbilityEvent e in t.OnHitEvents) if (e != null) yield return e;
			if (t.OnTickEvents != null) foreach (AbilityEvent e in t.OnTickEvents) if (e != null) yield return e;
			if (t.OnSpawnEvents != null) foreach (AbilityEvent e in t.OnSpawnEvents) if (e != null) yield return e;
			if (t.OnDestroyEvents != null) foreach (AbilityEvent e in t.OnDestroyEvents) if (e != null) yield return e;
			if (t.OnPreSpawnEvents != null) foreach (AbilityEvent e in t.OnPreSpawnEvents) if (e != null) yield return e;
		}

		// --- The orc kits ------------------------------------------------------------------------

		private static readonly (string path, string archetypeGuid)[] Orcs =
		{
			("Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab", ""),
			("Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc warrior.prefab", ""),
			("Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc mage.prefab", ""),
		};

		[Test]
		public void EveryOrc_KnowsOnlyShippedAbilitiesThatAct()
		{
			foreach ((string path, _) in Orcs)
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				Assert.That(prefab, Is.Not.Null, path);
				NPC npc = prefab.GetComponent<NPC>();
				Assert.That(npc.Abilities, Is.Not.Empty, $"{npc.name} knows nothing");
				foreach (AbilityTemplate t in npc.Abilities)
				{
					Assert.That(t, Is.Not.Null, $"{npc.name} has a null ability slot");
					Assert.That(EventCount(t), Is.GreaterThan(0), $"{npc.name}: {t.name} does nothing");
				}
			}
		}

		[Test]
		public void TheOrcMage_CanAttackFromItsCasterComfortDistance()
		{
			/* The Caster archetype backs away inside 10 m. A kit whose longest reach is under that
			 * gets its comfort distance dropped by AICombatDecision.ResolveSpacing and fights in
			 * melee instead — correct, but not what a mage is for. */
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Orcs[2].path);
			NPC npc = prefab.GetComponent<NPC>();
			AIController ai = prefab.GetComponent<AIController>();
			BaseAttackingState attacking = ai.AttackingState as BaseAttackingState;
			Assert.That(attacking, Is.Not.Null);

			float longest = 0f;
			foreach (AbilityTemplate t in npc.Abilities)
			{
				Ability ability = new Ability(t.ID, t);
				longest = Mathf.Max(longest, AIAbilityReach.Resolve(ability, 0.5f));
			}
			Assert.That(longest, Is.GreaterThan(attacking.MinComfortDistance),
				$"the mage's longest reach ({longest} m) must exceed its comfort distance ({attacking.MinComfortDistance} m) or it cannot kite");
			Assert.That(longest, Is.GreaterThanOrEqualTo(attacking.PreferredDistance),
				"its preferred distance would otherwise be capped down by ResolveSpacing");
		}
	}
}
