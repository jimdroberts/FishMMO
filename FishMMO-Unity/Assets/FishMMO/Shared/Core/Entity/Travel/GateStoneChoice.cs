using System;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// The client's side of choosing a Gate Stone for a Gate spell (issue #241): a seam between the Travel module, which hears
	/// from the server that a Gate waits for a destination, and the map, which shows the choice. Neither names the other.
	/// </summary>
	/// <remarks>
	/// The Travel module calls <see cref="Begin"/> and <see cref="End"/> and installs <see cref="Submitter"/> and
	/// <see cref="Canceller"/>; the map listens to <see cref="OnBegan"/> / <see cref="OnEnded"/> and calls <see cref="Choose"/> and
	/// <see cref="Cancel"/>. Client only; nothing here is trusted by the server, which checks every choice.
	/// </remarks>
	public static class GateStoneChoice
	{
		/// <summary>Whether a Gate waits for this client's choice.</summary>
		public static bool IsActive { get; private set; }

		/// <summary>When the choice times out, in <see cref="Time.realtimeSinceStartupAsDouble"/> seconds.</summary>
		public static double EndsAt { get; private set; }

		/// <summary>Seconds left to choose; 0 when none waits.</summary>
		public static float RemainingSeconds => IsActive ? Mathf.Max(0.0f, (float)(EndsAt - Time.realtimeSinceStartupAsDouble)) : 0.0f;

		/// <summary>A Gate began waiting for a choice.</summary>
		public static event Action OnBegan;

		/// <summary>The wait ended (chosen, cancelled, timed out, broken by damage).</summary>
		public static event Action OnEnded;

		/// <summary>Sends a choice to the server (scene name, waypoint index). Installed by the Travel module.</summary>
		public static Action<string, int> Submitter;

		/// <summary>Tells the server the choice was given up. Installed by the Travel module.</summary>
		public static Action Canceller;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			IsActive = false;
			EndsAt = 0.0;
			OnBegan = null;
			OnEnded = null;
			Submitter = null;
			Canceller = null;
		}

		/// <summary>A Gate waits for a choice for <paramref name="seconds"/>.</summary>
		public static void Begin(float seconds)
		{
			IsActive = true;
			EndsAt = Time.realtimeSinceStartupAsDouble + Mathf.Max(0.0f, seconds);
			OnBegan?.Invoke();
		}

		/// <summary>The wait is over. Safe to call when none is active.</summary>
		public static void End()
		{
			if (!IsActive)
			{
				return;
			}
			IsActive = false;
			EndsAt = 0.0;
			OnEnded?.Invoke();
		}

		/// <summary>Chooses the Gate Stone at (<paramref name="sceneName"/>, <paramref name="waypointIndex"/>). The server answers.</summary>
		public static void Choose(string sceneName, int waypointIndex)
		{
			if (IsActive && !string.IsNullOrEmpty(sceneName))
			{
				Submitter?.Invoke(sceneName, waypointIndex);
			}
		}

		/// <summary>Gives up the choice: the server ends the wait, and the local state ends at once.</summary>
		public static void Cancel()
		{
			if (!IsActive)
			{
				return;
			}
			Canceller?.Invoke();
			End();
		}
	}
}
