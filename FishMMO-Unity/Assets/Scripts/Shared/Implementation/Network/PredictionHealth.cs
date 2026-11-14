using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The client's own prediction health over the last minute: how often the server corrected its movement by more than a
	/// centimetre, and what share of the hits it predicted the server rejected. Shown in the network overlay; the numbers
	/// every prediction fix is measured by.
	/// </summary>
	public static class PredictionHealth
	{
		/// <summary>The window the figures cover, seconds.</summary>
		public const float WindowSeconds = 60f;
		/// <summary>A correction counts when the server's position for a tick differs from the prediction by more than this, m.</summary>
		public const float CorrectionThresholdMetres = 0.01f;

		private static readonly Queue<float> corrections = new Queue<float>();
		private static readonly Queue<(float At, bool Rejected)> hits = new Queue<(float, bool)>();
		private static bool subscribed;

		/// <summary>The largest correction in the window, m.</summary>
		public static float LargestCorrectionMetres { get; private set; }
		private static float largestAt = float.NegativeInfinity;

		/// <summary>Records one reconcile's correction: the distance between the server's position for a tick and the prediction.</summary>
		public static void NoteCorrection(float metres)
		{
			float now = Time.unscaledTime;
			if (metres > CorrectionThresholdMetres)
			{
				corrections.Enqueue(now);
			}
			if (metres >= LargestCorrectionMetres || now - largestAt > WindowSeconds)
			{
				LargestCorrectionMetres = metres;
				largestAt = now;
			}
		}

		/// <summary>Corrections over <see cref="CorrectionThresholdMetres"/> in the last minute.</summary>
		public static int CorrectionsPerMinute
		{
			get
			{
				Prune(Time.unscaledTime);
				return corrections.Count;
			}
		}

		/// <summary>Predicted hits the server answered in the last minute.</summary>
		public static int HitsAnswered
		{
			get
			{
				Prune(Time.unscaledTime);
				return hits.Count;
			}
		}

		/// <summary>The share of answered predicted hits the server rejected in the last minute; NaN with none.</summary>
		public static double RejectedHitShare
		{
			get
			{
				Prune(Time.unscaledTime);
				if (hits.Count == 0)
				{
					return double.NaN;
				}
				int rejected = 0;
				foreach ((float _, bool r) in hits)
				{
					rejected += r ? 1 : 0;
				}
				return (double)rejected / hits.Count;
			}
		}

		/// <summary>Starts counting predicted hits (idempotent; client only).</summary>
		public static void EnsureSubscribed()
		{
			if (subscribed)
			{
				return;
			}
			subscribed = true;
			PredictedCombatEvents.OnPredictionConfirmed += _ => hits.Enqueue((Time.unscaledTime, false));
			// An evade or immunity is the server answering the hit, not contradicting where it landed.
			PredictedCombatEvents.OnPredictionRefused += (_, _) => hits.Enqueue((Time.unscaledTime, false));
			PredictedCombatEvents.OnPredictionRejected += _ => hits.Enqueue((Time.unscaledTime, true));
		}

		private static void Prune(float now)
		{
			while (corrections.Count > 0 && now - corrections.Peek() > WindowSeconds)
			{
				corrections.Dequeue();
			}
			while (hits.Count > 0 && now - hits.Peek().At > WindowSeconds)
			{
				hits.Dequeue();
			}
		}
	}
}
