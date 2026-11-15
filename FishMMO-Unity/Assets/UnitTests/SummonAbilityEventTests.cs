using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for summon effects: which effects go on which abilities, and what a summoned pet is taught.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A summon spell spawns no ability object, so the tick, hit, spawn and destroy effects never
	/// run on one. They used to be accepted anyway, and all they did was shift the spell's cast
	/// time, cooldown and cost. <see cref="AbilityEvent.IsCompatibleWith"/> is now the single rule:
	/// ordinary effects on ordinary abilities, <see cref="AbilityOnSummonEvent"/> on summons only.
	/// </para>
	/// <para>
	/// The summon effect's grants reach the pet through
	/// <see cref="PetAbilityTemplate.BuildPetAbilityIDs"/>, which merges the persisted list, the
	/// spell's own pet abilities and every summon effect's, before the pet spawns.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class SummonAbilityEventTests
	{
		private readonly List<ScriptableObject> created = new List<ScriptableObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (ScriptableObject asset in created)
			{
				if (asset == null)
				{
					continue;
				}
				if (asset is BaseAbilityTemplate template)
				{
					template.RemoveFromCache();
				}
				else if (asset is AbilityEvent abilityEvent)
				{
					abilityEvent.RemoveFromCache();
				}
				Object.DestroyImmediate(asset);
			}
			created.Clear();
		}

		private T NewTemplate<T>(string name) where T : AbilityTemplate
		{
			T template = ScriptableObject.CreateInstance<T>();
			template.name = name;
			template.AddToCache(name);
			created.Add(template);
			return template;
		}

		private T NewEvent<T>(string name) where T : AbilityEvent
		{
			T abilityEvent = ScriptableObject.CreateInstance<T>();
			abilityEvent.name = name;
			abilityEvent.AddToCache(name);
			created.Add(abilityEvent);
			return abilityEvent;
		}

		[Test]
		public void AnOrdinaryEffect_DoesNotWorkOnASummonSpell()
		{
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonCompat_Summon");
			AbilityTemplate bolt = NewTemplate<AbilityTemplate>("SummonCompat_Bolt");
			AbilityOnHitEvent hit = NewEvent<AbilityOnHitEvent>("SummonCompat_Hit");

			Assert.IsFalse(hit.IsCompatibleWith(summon), "A summon spawns no object for a hit effect to run on.");
			Assert.IsTrue(hit.IsCompatibleWith(bolt));
		}

		[Test]
		public void ASummonEffect_WorksOnlyOnASummonSpell()
		{
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonOnly_Summon");
			AbilityTemplate bolt = NewTemplate<AbilityTemplate>("SummonOnly_Bolt");
			AbilityOnSummonEvent onSummon = NewEvent<AbilityOnSummonEvent>("SummonOnly_Event");

			Assert.IsTrue(onSummon.IsCompatibleWith(summon));
			Assert.IsFalse(onSummon.IsCompatibleWith(bolt), "There is no pet for it to act on.");
			Assert.IsFalse(onSummon.IsCompatibleWith(null));
		}

		[Test]
		public void ACraftedOrdinaryEffect_IsLeftOffASummon_AndDoesNotChangeItsNumbers()
		{
			/* The exploit this closes: a negative cast-time effect crafted onto a summon made a
			 * faster summon, though the effect itself never ran. */
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonNumbers_Summon");
			summon.ActivationTime = 10.0f;
			AbilityOnHitEvent haste = NewEvent<AbilityOnHitEvent>("SummonNumbers_Haste");
			haste.ActivationTime = -5.0f;

			Ability ability = new Ability(1L, summon, new List<int> { haste.ID });

			Assert.IsFalse(ability.HasAbilityEvent(haste.ID));
			Assert.AreEqual(10.0f, ability.ActivationTime, 0.0001f);
		}

		[Test]
		public void ACraftedSummonEffect_IsLeftOffAnOrdinaryAbility()
		{
			AbilityTemplate bolt = NewTemplate<AbilityTemplate>("SummonOnBolt_Bolt");
			AbilityOnSummonEvent onSummon = NewEvent<AbilityOnSummonEvent>("SummonOnBolt_Event");
			onSummon.Cooldown = 3.0f;

			Ability ability = new Ability(1L, bolt, new List<int> { onSummon.ID });

			Assert.IsFalse(ability.HasAbilityEvent(onSummon.ID));
			Assert.AreEqual(0.0f, ability.Cooldown, 0.0001f);
		}

		[Test]
		public void AddEvent_RefusesAnIncompatibleEffect()
		{
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonAddEvent_Summon");
			AbilityOnSpawnEvent spawn = NewEvent<AbilityOnSpawnEvent>("SummonAddEvent_Spawn");
			Ability ability = new Ability(1L, summon);

			Assert.IsFalse(ability.AddEvent(spawn));
			Assert.AreEqual(0, ability.AbilityEvents.Count);
			Assert.AreEqual(0, ability.OnSpawnEvents.Count);
		}

		[Test]
		public void ACraftedSummonEffect_IsRoutedToOnSummonEvents_AndCanBeRemoved()
		{
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonRoute_Summon");
			AbilityOnSummonEvent onSummon = NewEvent<AbilityOnSummonEvent>("SummonRoute_Event");

			Ability ability = new Ability(1L, summon, new List<int> { onSummon.ID });

			Assert.IsTrue(ability.HasAbilityEvent(onSummon.ID));
			Assert.IsTrue(ability.OnSummonEvents.ContainsKey(onSummon.ID));

			Assert.IsTrue(ability.RemoveAbilityEvent(onSummon.ID));
			Assert.IsFalse(ability.OnSummonEvents.ContainsKey(onSummon.ID));
		}

		[Test]
		public void ASummonSpellsBundledSummonEffects_AreOnEveryAbilityBuiltFromIt()
		{
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonBundled_Summon");
			AbilityOnSummonEvent bundled = NewEvent<AbilityOnSummonEvent>("SummonBundled_Event");
			summon.OnSummonEvents.Add(bundled);

			Ability ability = new Ability(1L, summon);

			Assert.IsTrue(ability.OnSummonEvents.ContainsKey(bundled.ID));
			CollectionAssert.Contains(summon.GetAllAbilityEventIDs(), bundled.ID,
				"Starting abilities are stored with every event the template ships with.");
		}

		[Test]
		public void BuildPetAbilityIDs_MergesPersistedSpellAndCraftedGrants_WithoutDuplicates()
		{
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonGrants_Summon");
			AbilityTemplate claw = NewTemplate<AbilityTemplate>("SummonGrants_Claw");
			AbilityTemplate firebolt = NewTemplate<AbilityTemplate>("SummonGrants_Firebolt");
			AbilityTemplate ember = NewTemplate<AbilityTemplate>("SummonGrants_Ember");
			summon.PetAbilities.Add(claw);

			AbilityOnSummonEvent crafted = NewEvent<AbilityOnSummonEvent>("SummonGrants_Event");
			crafted.PetAbilities.Add(firebolt);
			crafted.PetAbilities.Add(claw);

			Ability cast = new Ability(1L, summon, new List<int> { crafted.ID });
			List<int> ids = summon.BuildPetAbilityIDs(cast, new List<int> { ember.ID });

			CollectionAssert.AreEqual(new List<int> { ember.ID, claw.ID, firebolt.ID }, ids,
				"Persisted first, then the spell's own, then what its summon effects teach; each once.");
		}

		[Test]
		public void BuildPetAbilityIDs_NeverTeachesAPetToSummon()
		{
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonNoSummon_Summon");
			PetAbilityTemplate otherSummon = NewTemplate<PetAbilityTemplate>("SummonNoSummon_Other");
			summon.PetAbilities.Add(otherSummon);
			summon.PetAbilities.Add(null);

			AbilityOnSummonEvent crafted = NewEvent<AbilityOnSummonEvent>("SummonNoSummon_Event");
			crafted.PetAbilities.Add(otherSummon);

			Ability cast = new Ability(1L, summon, new List<int> { crafted.ID });

			Assert.IsEmpty(summon.BuildPetAbilityIDs(cast, null));
		}

		[Test]
		public void BuildPetAbilityIDs_OnARestore_ReadsTheBundledEffects_AndKeepsWhatWasSaved()
		{
			/* A restore has no cast. A crafted grant is already in the persisted list, because the
			 * pet's known abilities are captured on save, so the pet keeps it until it is dismissed
			 * or summoned fresh. */
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonRestore_Summon");
			AbilityTemplate bundledGrant = NewTemplate<AbilityTemplate>("SummonRestore_Bundled");
			AbilityTemplate craftedGrant = NewTemplate<AbilityTemplate>("SummonRestore_Crafted");
			AbilityOnSummonEvent bundled = NewEvent<AbilityOnSummonEvent>("SummonRestore_Event");
			bundled.PetAbilities.Add(bundledGrant);
			summon.OnSummonEvents.Add(bundled);

			List<int> ids = summon.BuildPetAbilityIDs(null, new List<int> { craftedGrant.ID });

			CollectionAssert.AreEqual(new List<int> { craftedGrant.ID, bundledGrant.ID }, ids);
		}

		[Test]
		public void AFreshSummon_IsBuiltFromTheSpellAsItStands()
		{
			/* No persisted list on a fresh summon: a grant from an effect no longer on the spell
			 * does not come back. */
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonFresh_Summon");
			Ability cast = new Ability(1L, summon);

			Assert.IsEmpty(summon.BuildPetAbilityIDs(cast, null));
		}

		[Test]
		public void APremadeOffer_WithAnIncompatibleEffect_IsNotSellable()
		{
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonPremade_Summon");
			summon.AdditionalEventSlots = 2;
			AbilityOnHitEvent hit = NewEvent<AbilityOnHitEvent>("SummonPremade_Hit");
			AbilityOnSummonEvent onSummon = NewEvent<AbilityOnSummonEvent>("SummonPremade_Event");

			PremadeAbilityTemplate offer = ScriptableObject.CreateInstance<PremadeAbilityTemplate>();
			created.Add(offer);
			offer.Ability = summon;

			offer.Events.Add(onSummon);
			Assert.IsTrue(offer.Validate(out string accepted), accepted);

			offer.Events.Add(hit);
			Assert.IsFalse(offer.Validate(out string reason));
			StringAssert.Contains(hit.Name, reason);
		}

		[Test]
		public void TheCraftPreview_IgnoresAnIncompatibleEffect_AndNamesWhatThePetLearns()
		{
			PetAbilityTemplate summon = NewTemplate<PetAbilityTemplate>("SummonPreview_Summon");
			AbilityTemplate firebolt = NewTemplate<AbilityTemplate>("SummonPreview_Firebolt");
			AbilityOnHitEvent haste = NewEvent<AbilityOnHitEvent>("SummonPreview_Haste");
			haste.ActivationTime = -5.0f;
			haste.Price = 50;
			AbilityOnSummonEvent onSummon = NewEvent<AbilityOnSummonEvent>("SummonPreview_Event");
			onSummon.PetAbilities.Add(firebolt);

			AbilitySummary summary = AbilitySummary.Compose(summon, new List<ITooltip> { haste, onSummon });

			Assert.AreEqual(0.0f, summary.Stats.ActivationTime, 0.0001f);
			Assert.AreEqual(0, summary.CraftPrice);
			CollectionAssert.AreEqual(new List<AbilityEvent> { onSummon }, summary.CraftedEvents);
			CollectionAssert.Contains(summary.Effects, $"Your pet learns {firebolt.Name}");
			Assert.AreEqual("on summon", AbilitySummary.EventCategory(onSummon));
		}
	}
}
