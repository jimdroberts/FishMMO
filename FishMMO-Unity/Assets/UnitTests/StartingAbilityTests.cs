using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that the abilities a character is born with can actually do something.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Punch shipped with <c>OnHitEvents: []</c>. <see cref="AbilityObject"/> resolves a hit by
	/// iterating that collection and executing each event — <c>ApplyDamageAction</c> lives inside
	/// one — so an empty set means the loop body never runs. The hitbox spawned, the overlap was
	/// detected, block and deflect mitigation ran, the hit was broadcast, and nothing was applied.
	/// No exception, no warning, no log line: the basic attack of every character in the game
	/// silently did no damage.
	/// </para>
	/// <para>
	/// It could not be repaired in game either. <c>AdditionalEventSlots</c> is zero on Punch, so a
	/// player cannot craft an event onto it — the ability was inert by construction rather than
	/// merely unfinished.
	/// </para>
	/// <para>
	/// The general test is the one that matters. A starting ability is the only ability a player is
	/// guaranteed to have, so one that cannot act is not a content gap somebody notices later — it
	/// is a character who cannot fight. Asserting over every race's list catches the next one
	/// without anybody remembering to look.
	/// </para>
	/// </remarks>
	public class StartingAbilityTests
	{
		/// <summary>Every ability granted by a race template, with the race that grants it.</summary>
		private static IEnumerable<(string Race, AbilityTemplate Ability)> StartingAbilities()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:RaceTemplate"))
			{
				RaceTemplate race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(
					AssetDatabase.GUIDToAssetPath(guid));

				if (race?.StartingAbilities == null)
				{
					continue;
				}

				for (int i = 0; i < race.StartingAbilities.Count; ++i)
				{
					AbilityTemplate ability = race.StartingAbilities[i];
					if (ability != null)
					{
						yield return (race.name, ability);
					}
				}
			}
		}

		/// <summary>
		/// A race that grants no abilities at all would make the test below vacuous.
		/// </summary>
		[Test]
		public void EveryRaceGrantsAtLeastOneAbility()
		{
			Dictionary<string, int> byRace = new Dictionary<string, int>();

			foreach (string guid in AssetDatabase.FindAssets("t:RaceTemplate"))
			{
				RaceTemplate race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(
					AssetDatabase.GUIDToAssetPath(guid));
				if (race != null)
				{
					byRace[race.name] = race.StartingAbilities?.Count ?? 0;
				}
			}

			Assert.IsNotEmpty(byRace, "No race templates were found; this suite would prove nothing.");

			foreach (KeyValuePair<string, int> pair in byRace)
			{
				Assert.Greater(pair.Value, 0, $"Race '{pair.Key}' grants no starting abilities.");
			}
		}

		/// <summary>
		/// The regression: a starting ability with no events resolves hits and applies nothing.
		/// </summary>
		/// <remarks>
		/// Hit and tick events are both accepted. A damage-over-time shape does its work from
		/// <c>OnTickEvents</c> and legitimately has no hit event, so requiring the hit list
		/// specifically would fail an ability that works.
		/// </remarks>
		[Test]
		public void EveryStartingAbilityCanActOnSomething()
		{
			int checkedCount = 0;

			foreach ((string race, AbilityTemplate ability) in StartingAbilities())
			{
				++checkedCount;

				int hitEvents = ability.OnHitEvents?.Count ?? 0;
				int tickEvents = ability.OnTickEvents?.Count ?? 0;

				Assert.Greater(hitEvents + tickEvents, 0,
					$"'{ability.name}', granted by race '{race}', has no hit or tick events. " +
					"It will spawn, detect its target and apply nothing.");
			}

			Assert.Greater(checkedCount, 0, "No starting abilities were found; this test would prove nothing.");
		}

		/// <summary>
		/// An ability nobody can add an event to must ship with the events it needs.
		/// </summary>
		/// <remarks>
		/// <c>AdditionalEventSlots</c> is what lets a player craft onto an ability. At zero, whatever
		/// the template authors is all the ability will ever have — so the gap cannot be closed by a
		/// player, and the assertion above is the only thing standing between a shipped template and
		/// an unusable attack.
		/// </remarks>
		[Test]
		public void AStartingAbilityWithNoCraftingSlotsShipsItsOwnEvents()
		{
			foreach ((string race, AbilityTemplate ability) in StartingAbilities())
			{
				if (ability.AdditionalEventSlots > 0)
				{
					continue;
				}

				int events = (ability.OnHitEvents?.Count ?? 0) + (ability.OnTickEvents?.Count ?? 0);

				Assert.Greater(events, 0,
					$"'{ability.name}' (race '{race}') has no event slots for a player to craft into " +
					"and authors no events of its own, so nothing can ever make it act.");
			}
		}
	}
}
