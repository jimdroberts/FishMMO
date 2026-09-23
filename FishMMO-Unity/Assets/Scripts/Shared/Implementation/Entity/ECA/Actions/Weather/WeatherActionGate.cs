using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// The one authority check every weather action makes, and the one place a weather action finds
	/// the scene it is talking about.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Weather is authoritative, not predicted.</b> Everything else in this pass is predicted —
	/// exposure, recipes, region buffs — because all of it is derived from a timeline both peers
	/// already hold. Changing that timeline is the opposite: it is an edit, it is broadcast, and a
	/// client that made one locally would be predicting a future the server never decided on and
	/// would be corrected out of it a moment later. So these actions run on the server or nowhere.
	/// </para>
	/// <para>
	/// <b>And never during a replay.</b> A reconcile replays whatever ticks it must, so an action
	/// that ran inside one would apply its preset once per replayed tick. Weather edits are already
	/// scheduled ahead and broadcast; doing that thirty times for one trigger would flood every
	/// client in the scene.
	/// </para>
	/// </remarks>
	public static class WeatherActionGate
	{
		/// <summary>
		/// The weather service to act through, or null when this peer must not act.
		/// </summary>
		/// <remarks>
		/// Deliberately answers with the service rather than with a bool: there is exactly one way
		/// to pass the gate and it hands back the only thing a caller could do afterwards, so there
		/// is no arrangement of these two lines that checks authority and then acts through
		/// something else.
		/// </remarks>
		public static IWeatherService Resolve(ICharacter initiator, EventData eventData, out Scene scene)
		{
			scene = default;
			if (!RegionActionGate.ShouldExecuteGameplay(initiator, eventData))
			{
				return null;
			}
			if (initiator.GameObject == null)
			{
				return null;
			}
			scene = initiator.GameObject.scene;
			return WeatherQuery.Commands;
		}
	}
}
