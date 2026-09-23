using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>One exposure state a recipe needs, and how far into it the character has to be.</summary>
	[Serializable]
	public class WeatherExposureIngredient
	{
		[Tooltip("The exposure state this ingredient reads.")]
		public WeatherExposureTemplate State;

		[Tooltip("How deep into that state the character has to be for this ingredient to count.")]
		[Range(0f, 1f)] public float AtLeast = 0.6f;
	}

	/// <summary>
	/// A state that exists only where two or more others do at once: soaked AND chilled is frozen,
	/// which is not what either one alone means.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>It reads levels, never other recipes.</b> A recipe's ingredients are always
	/// <see cref="WeatherExposureTemplate"/> levels, so the whole set can be evaluated in one pass
	/// with no ordering to get right and no way to write a cycle. Two recipes can name the same
	/// ingredient, and both hold.
	/// </para>
	/// <para>
	/// <b>Nothing of it rides the wire.</b> A recipe is a pure function of the levels, and the levels
	/// already reconcile — so both peers reach the same verdict from the same numbers, and the
	/// reconcile payload does not grow by a byte for any number of recipes. What is NOT derivable is
	/// which side of the hysteresis band a recipe was on, and that is re-derived after a reconcile
	/// exactly the way a single state's hold is: see <see cref="WeatherExposureController"/>.
	/// </para>
	/// <para>
	/// <b>Static content only.</b> Like every template, this holds no runtime state; which recipes a
	/// given character currently satisfies lives on that character's controller.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Weather Exposure Recipe", menuName = "FishMMO/Weather/Exposure Recipe", order = 7)]
	public class WeatherExposureRecipe : CachedScriptableObject<WeatherExposureRecipe>, ICachedObject
	{
		[Tooltip("Shown in tooltips and logs. The asset's name is used when this is empty.")]
		public string Description;

		[Header("What it takes")]
		[Tooltip("Every one of these states must be at or above its level at the same time.")]
		public List<WeatherExposureIngredient> Ingredients = new List<WeatherExposureIngredient>();

		[Tooltip("How far an ingredient has to fall back below its level before the recipe lets go. Keeps a character sitting exactly on a threshold from flickering in and out of the combined state.")]
		[Range(0f, 0.5f)] public float ReleaseMargin = 0.1f;

		[Header("What it does")]
		[Tooltip("The buff applied while the recipe holds. Optional — a recipe with no buff is still tracked and still suppresses, which is how a combined state can simply replace its parts.")]
		public BaseBuffTemplate Buff;

		[Tooltip("Take the ingredients' own buffs off while this one holds, so the combined state replaces its parts instead of stacking three icons on top of each other.")]
		public bool SuppressIngredients = true;

		public string DisplayName => string.IsNullOrWhiteSpace(Description) ? name : Description;

		/// <summary>
		/// Whether the recipe is satisfied, given a way to read each state's level.
		/// </summary>
		/// <remarks>
		/// Two thresholds, as everywhere else in exposure: a recipe that is not currently held needs
		/// every ingredient at its <see cref="WeatherExposureIngredient.AtLeast"/>, while one that IS
		/// held only lets go once an ingredient drops <see cref="ReleaseMargin"/> below it. Without
		/// the gap, a character standing in weather that parks an ingredient exactly on its threshold
		/// would gain and lose the combined buff on alternate ticks.
		/// </remarks>
		/// <param name="holding">Whether this recipe is currently held, which decides which threshold applies.</param>
		/// <param name="levelOf">Reads a state's current level, 0..1.</param>
		public bool IsSatisfied(bool holding, Func<WeatherExposureTemplate, float> levelOf)
		{
			if (Ingredients == null || Ingredients.Count == 0 || levelOf == null)
			{
				// A recipe with nothing in it is not "always true": it is unauthored, and an
				// unauthored recipe that applied its buff to everybody in the world would be a very
				// confusing thing to debug.
				return false;
			}

			for (int i = 0; i < Ingredients.Count; i++)
			{
				WeatherExposureIngredient ingredient = Ingredients[i];
				if (ingredient == null || ingredient.State == null)
				{
					return false;
				}

				float threshold = holding ? ingredient.AtLeast - ReleaseMargin : ingredient.AtLeast;
				if (levelOf(ingredient.State) < threshold)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>True when this recipe names that state as one of its ingredients.</summary>
		public bool Uses(WeatherExposureTemplate state)
		{
			if (state == null || Ingredients == null)
			{
				return false;
			}
			for (int i = 0; i < Ingredients.Count; i++)
			{
				if (Ingredients[i] != null && Ingredients[i].State == state)
				{
					return true;
				}
			}
			return false;
		}
	}
}
