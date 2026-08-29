using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Tracks combat numbers a client has drawn from its OWN predicted hits, and reconciles them
	/// against the server's combat report.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the client draws first.</b> Damage numbers used to come only from
	/// <c>CombatEventBroadcast</c>, so a player's own hit showed nothing until the report came back
	/// — half a round trip of nothing happening, on the one action combat is built around. The
	/// caster predicted the cast and owns the input that produced it, so it can draw the number the
	/// moment its predicted projectile connects.
	/// </para>
	/// <para>
	/// <b>Why the amounts agree.</b> Damage variance is drawn from the ability object's
	/// <c>DeterministicRNG</c> (<c>RandomRangeValue</c> reads <c>EventData.RNG</c>), whose state is
	/// carried in the reconcile and corrected every tick. Client and server draw the same number
	/// from the same state. A hit-count divergence advances the two generators differently until
	/// the next reconcile realigns them, so a mismatch is possible, bounded, and self-healing —
	/// which is why <see cref="TryConfirm"/> matches on source, target and kind rather than on the
	/// amount.
	/// </para>
	/// <para>
	/// <b>Why there is no server-sent rejection.</b> The server cannot tell a client "that hit did
	/// not land", because it never knew the client predicted one — it simply resolves its own
	/// simulation and reports what happened. Absence is the only signal available, so a prediction
	/// that goes unconfirmed for <see cref="ConfirmationWindowSeconds"/> is treated as rejected.
	/// That costs no bandwidth and needs no new message.
	/// </para>
	/// <para>
	/// Pure bookkeeping with no Unity or rendering dependency, so the policy is unit tested. The
	/// client's display layer subscribes to the events below; nothing here draws anything.
	/// </para>
	/// </remarks>
	public static class PredictedCombatEvents
	{
		/// <summary>
		/// How long a predicted number waits for the server to confirm it before being treated as
		/// rejected.
		/// </summary>
		/// <remarks>
		/// Must comfortably exceed a round trip plus the server's report cadence (combat events
		/// flush every tick, so the cadence term is one tick). Too short invalidates good hits on a
		/// laggy connection — far worse than the occasional wrong number lingering, because it makes
		/// a working hit look broken.
		/// </remarks>
		public static float ConfirmationWindowSeconds { get; set; } = 1.0f;

		/// <summary>What a predicted entry describes.</summary>
		public enum Kind : byte
		{
			/// <summary>Damage dealt to the target.</summary>
			Damage = 0,
			/// <summary>Healing applied to the target.</summary>
			Heal = 1,
		}

		/// <summary>One number this client drew before the server agreed to it.</summary>
		private struct Pending
		{
			public long Id;
			public int SourceObjectId;
			public int TargetObjectId;
			public Kind Kind;
			public int Amount;
			public float PredictedAt;
		}

		private static readonly List<Pending> pending = new List<Pending>();
		private static long nextId = 1;

		/// <summary>
		/// Raised when this client predicts a combat number. The display draws it immediately.
		/// </summary>
		/// <remarks>
		/// The <c>long</c> is a handle the display keeps, so a later
		/// <see cref="OnPredictionRejected"/> can name the number to grey out.
		/// </remarks>
		public static event Action<long, ICharacter, int, Kind, DamageAttributeTemplate> OnPredicted;

		/// <summary>
		/// Raised when a predicted number went unconfirmed past the window. The display should mark
		/// it invalid rather than deleting it — a number that vanishes reads as a rendering glitch,
		/// one that greys out reads as "that did not land".
		/// </summary>
		public static event Action<long> OnPredictionRejected;

		/// <summary>
		/// Raised when the server's report confirmed a predicted number. Nothing needs redrawing — the
		/// number on screen was already right — but the display holds a handle per prediction so it can
		/// grey one out later, and without this it would only ever release the handles for predictions
		/// that turned out WRONG. A session's worth of correct hits leaked one entry each.
		/// </summary>
		public static event Action<long> OnPredictionConfirmed;

		/// <summary>Predicted entries still waiting on the server.</summary>
		public static int PendingCount => pending.Count;

		/// <summary>
		/// Records and announces a number this client predicted.
		/// </summary>
		/// <param name="source">The character that dealt it — this client's own, since only an owner predicts.</param>
		/// <param name="target">The character the number belongs to.</param>
		/// <param name="amount">The predicted amount.</param>
		/// <param name="kind">Damage or heal.</param>
		/// <param name="damageAttribute">Damage type, for colouring. Null for heals and typeless damage.</param>
		/// <param name="now">Current unscaled time, in seconds.</param>
		public static void Predict(ICharacter source, ICharacter target, int amount, Kind kind, DamageAttributeTemplate damageAttribute, float now)
		{
			if (target == null || amount <= 0)
			{
				return;
			}

			int targetObjectId = ResolveObjectId(target);
			int sourceObjectId = ResolveObjectId(source);
			if (targetObjectId == 0 || sourceObjectId == 0)
			{
				/* No network identity to match a report against, so this could never be confirmed
				 * and would always end up greyed out. Better to draw nothing than to draw a number
				 * that is guaranteed to be marked wrong. The SOURCE is required for the same reason
				 * now that confirmation matches on it. */
				return;
			}

			long id = nextId++;
			pending.Add(new Pending
			{
				Id = id,
				SourceObjectId = sourceObjectId,
				TargetObjectId = targetObjectId,
				Kind = kind,
				Amount = amount,
				PredictedAt = now,
			});

			OnPredicted?.Invoke(id, target, amount, kind, damageAttribute);
		}

		/// <summary>
		/// Consumes a pending prediction matching an arriving server report.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Matched on SOURCE, target and kind</b>, oldest first. The source is what makes this a
		/// pairing rather than a guess: a combat report is broadcast to everyone observing the victim,
		/// so with source ignored any other player's hit on the same target consumed this client's
		/// pending entry. Two players on one mob was enough — the other player's real number was
		/// swallowed (the caller draws nothing on a match) and this client's own report, arriving to
		/// find no pending entry left, was then drawn a second time.
		/// </para>
		/// <para>
		/// Deliberately NOT matched on the amount. The two numbers agree whenever the RNG states
		/// agree, but a transient divergence would otherwise leave the prediction unmatched and
		/// produce the worst outcome available: the predicted number greyed out AND the server's
		/// number drawn beside it, for a hit that landed. Trusting the pairing and letting the amount
		/// differ is the lesser error.
		/// </para>
		/// <para>
		/// The caller draws the server's number only when this returns false.
		/// </para>
		/// </remarks>
		/// <param name="source">The attacker named by the report. A report with none matches nothing.</param>
		/// <param name="target">The character named by the report.</param>
		/// <param name="kind">Damage or heal.</param>
		/// <returns>True when this report was already drawn as a prediction.</returns>
		public static bool TryConfirm(ICharacter source, ICharacter target, Kind kind)
		{
			if (source == null || target == null || pending.Count == 0)
			{
				return false;
			}

			int targetObjectId = ResolveObjectId(target);
			int sourceObjectId = ResolveObjectId(source);
			if (targetObjectId == 0 || sourceObjectId == 0)
			{
				return false;
			}

			for (int i = 0; i < pending.Count; ++i)
			{
				if (pending[i].SourceObjectId == sourceObjectId &&
					pending[i].TargetObjectId == targetObjectId &&
					pending[i].Kind == kind)
				{
					long id = pending[i].Id;
					pending.RemoveAt(i);
					OnPredictionConfirmed?.Invoke(id);
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Rejects predictions the server never confirmed. Call once per frame or tick.
		/// </summary>
		/// <param name="now">Current unscaled time, in seconds.</param>
		public static void Sweep(float now)
		{
			for (int i = pending.Count - 1; i >= 0; --i)
			{
				if (now - pending[i].PredictedAt < ConfirmationWindowSeconds)
				{
					continue;
				}

				long id = pending[i].Id;
				pending.RemoveAt(i);
				OnPredictionRejected?.Invoke(id);
			}
		}

		/// <summary>
		/// Drops every pending prediction without raising rejections.
		/// </summary>
		/// <remarks>
		/// For a scene change or disconnect, where the numbers and the characters they were drawn
		/// over are both gone. Raising rejections here would ask the display to grey out labels that
		/// no longer exist.
		/// </remarks>
		public static void Clear()
		{
			pending.Clear();
		}

		/// <summary>Network object id for a character, or 0 when it has no network identity.</summary>
		private static int ResolveObjectId(ICharacter character)
		{
			FishNet.Object.NetworkObject networkObject = character?.NetworkObject;
			return networkObject != null ? networkObject.ObjectId : 0;
		}
	}
}
