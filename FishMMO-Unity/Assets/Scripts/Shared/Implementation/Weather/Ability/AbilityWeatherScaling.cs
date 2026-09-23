using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The weather multiplier for one ability, one target, at the moment of a cast (Q16).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Gathered from the template AND its events</b>, the way every other ability number already
	/// is: an ability's activation time is its template's plus its events', so a rule attached to a
	/// Channeled event has to count as much as one on the ability itself.
	/// </para>
	/// <para>
	/// <b>Free unless something asked for it.</b> Almost no ability has a rule, so the cheap check —
	/// does anything name this target at all — happens first, and a weather sample is only taken
	/// once something does. That matters because this sits on the cast path, which runs in the
	/// replicate and is replayed on every reconcile.
	/// </para>
	/// </remarks>
	public static class AbilityWeatherScaling
	{
		/// <summary>
		/// The multiplier to apply to <paramref name="target"/> for this ability, cast now by this
		/// character. Exactly 1 when nothing says otherwise, and the caller pays nothing for it.
		/// </summary>
		/// <param name="ability">The ability being cast.</param>
		/// <param name="behaviour">The predicted behaviour doing the casting; supplies the clocks.</param>
		/// <param name="character">Whose position the weather is read at.</param>
		/// <param name="inputTick">The replicate's tick, in the owning client's domain.</param>
		/// <param name="target">Which of the ability's numbers is being scaled.</param>
		public static float Multiplier(Ability ability, NetworkBehaviour behaviour, ICharacter character, uint inputTick, WeatherAbilityTarget target)
		{
			if (ability == null || character?.GameObject == null || character.Transform == null)
			{
				return 1f;
			}

			// The cheap question first: is there any rule for this target anywhere on this ability?
			bool any = WeatherAbilityModifiers.Any(ability.Template?.WeatherModifiers, target);
			if (!any && ability.AbilityEvents != null)
			{
				foreach (KeyValuePair<int, AbilityEvent> entry in ability.AbilityEvents)
				{
					if (WeatherAbilityModifiers.Any(entry.Value?.WeatherModifiers, target))
					{
						any = true;
						break;
					}
				}
			}
			if (!any)
			{
				return 1f;
			}

			/* One sample, at the tick the weather timeline is anchored in — NOT the replicate's own
			 * tick, which is the owning client's private counter. See WeatherExposureTick: read at
			 * the wrong clock this would give the owner one hour's weather and the server another,
			 * and the cast bar would disagree with the cast for as long as the ability existed. */
			uint weatherTick = WeatherExposureTick.Resolve(behaviour, inputTick);
			WeatherSample sample = WeatherQuery.Sample(character.GameObject.scene, character.Transform.position, weatherTick);

			float product = WeatherAbilityModifiers.Multiplier(ability.Template?.WeatherModifiers, target, sample);
			if (ability.AbilityEvents != null)
			{
				foreach (KeyValuePair<int, AbilityEvent> entry in ability.AbilityEvents)
				{
					product *= WeatherAbilityModifiers.Multiplier(entry.Value?.WeatherModifiers, target, sample);
				}
			}
			return Mathf.Max(0f, product);
		}
	}
}
