using System;
using System.Text.RegularExpressions;
using UnityEngine;
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
		/// <summary>Resolves an ability template by its ID, from assets rather than the cache.</summary>
		/// <remarks>
		/// <c>AbilityTemplate.Get</c> reads the runtime cache, which nothing populates in EditMode —
		/// it returns null for every ID here, which is indistinguishable from the list being empty.
		/// Scanning the assets is what makes the global list checkable outside play mode.
		/// </remarks>
		private static AbilityTemplate FindAbilityByID(int id)
		{
			foreach (string guid in AssetDatabase.FindAssets("t:AbilityTemplate"))
			{
				AbilityTemplate candidate = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(
					AssetDatabase.GUIDToAssetPath(guid));

				if (candidate != null && candidate.ID == id)
				{
					return candidate;
				}
			}

			return null;
		}

		/// <summary>The template's runtime cache ID, recomputed when the cache has not run.</summary>
		private static int TemplateID(ScriptableObject template)
		{
			if (template is ICachedObject cached && cached.ID != 0)
			{
				return cached.ID;
			}

			return (template.GetType().Name + template.name).GetDeterministicHashCode();
		}

		/// <summary>The ability ids every new character is granted, from the login server config.</summary>
		private static IEnumerable<int> GlobalStartingAbilityIds()
		{
			string text = System.IO.File.ReadAllText(
				"Assets/Prefabs/Server/LoginServer/CharacterCreateSystem.asset");

			Match m = Regex.Match(text, @"startingAbilityIDs: ([0-9a-fA-F]*)");
			Assert.That(m.Success, Is.True,
				"startingAbilityIDs must be present in the login server config.");

			string hex = m.Groups[1].Value;
			byte[] bytes = new byte[hex.Length / 2];
			for (int i = 0; i < bytes.Length; ++i)
			{
				bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
			}

			for (int i = 0; i + 4 <= bytes.Length; i += 4)
			{
				yield return BitConverter.ToInt32(bytes, i);
			}
		}

		/// <summary>Every ability a new character is guaranteed, with where it comes from.</summary>
		/// <remarks>
		/// Two sources, because <c>CharacterCreateSystem</c> grants from two: the global
		/// <c>startingAbilityIDs</c> on its own asset, and the race template. Reading only the race
		/// templates made this test silently vacuous the moment the races were emptied — it kept
		/// passing over an empty set until the guard below caught it, and would have missed an inert
		/// ability granted globally to every character of every race.
		/// </remarks>
		private static IEnumerable<(string Source, AbilityTemplate Ability)> StartingAbilities()
		{
			/* Read from the asset text and matched by recomputed hash, mirroring
			 * ItemAttributeRollTests. Two EditMode facts force this: CharacterCreateSystem is a
			 * MonoBehaviour asset, which AssetDatabase's t: filter does not resolve; and ICachedObject.ID
			 * is assigned by AddToCache at runtime, so every asset loaded here reports ID 0 and matching
			 * on it silently finds nothing. */
			foreach (int id in GlobalStartingAbilityIds())
			{
				foreach (string guid in AssetDatabase.FindAssets("t:AbilityTemplate"))
				{
					AbilityTemplate ability = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(
						AssetDatabase.GUIDToAssetPath(guid));

					if (ability != null && TemplateID(ability) == id)
					{
						yield return ("every character", ability);
						break;
					}
				}
			}

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

			foreach ((string source, AbilityTemplate ability) in StartingAbilities())
			{
				++checkedCount;

				int hitEvents = ability.OnHitEvents?.Count ?? 0;
				int tickEvents = ability.OnTickEvents?.Count ?? 0;

				Assert.Greater(hitEvents + tickEvents, 0,
					$"'{ability.name}', granted to {source}, has no hit or tick events. " +
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
