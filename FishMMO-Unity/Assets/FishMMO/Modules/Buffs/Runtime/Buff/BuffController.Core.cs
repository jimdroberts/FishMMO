using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;
using FishNet.Managing.Timing;

namespace FishMMO.Shared
{
	/// <summary>
	/// The controller's side of the core seams (when buffs became the Buffs module's): what weather exposure,
	/// the target frame and the server's dismiss handler do with buffs (<see cref="ICharacterBuffs"/>), and the buffs in the
	/// character save (<see cref="IBuffPersistence"/>).
	/// </summary>
	public partial class BuffController
	{
		/// <inheritdoc />
		public bool HasBuff(int templateID) => Buffs != null && Buffs.ContainsKey(templateID);

		/// <inheritdoc />
		public void ApplyTemplate(IBuffTemplateInfo template, PredictionTick currentTick, ICharacter caster = null)
		{
			if (template is BaseBuffTemplate buffTemplate)
			{
				Apply(buffTemplate, currentTick, caster);
			}
		}

		/// <inheritdoc />
		public void CaptureObserved(List<ObservedBuffEntry> into)
		{
			/* Every peer holds the same entries (an observer materialises them from the server's message and counts them
			 * down on its own clock), so the remaining seconds are this peer's own. */
			if (into == null || Buffs == null)
			{
				return;
			}
			uint currentTick = GetCurrentDomainTick();
			foreach (Buff buff in Buffs.Values)
			{
				BaseBuffTemplate template = buff?.Template;
				if (template == null)
				{
					continue;
				}
				into.Add(new ObservedBuffEntry()
				{
					TemplateID = template.ID,
					Stacks = buff.Stacks,
					RemainingSeconds = buff.RemainingSeconds(currentTick),
					TotalSeconds = template.Duration,
				});
			}
		}

		/// <inheritdoc />
		public bool TryDismissByPlayer(int templateID)
		{
			if (Buffs == null || !Buffs.TryGetValue(templateID, out Buff buff) ||
				buff == null || buff.Template == null || !buff.Template.CanBeDismissedByPlayer)
			{
				return false;
			}
			// Marks the snapshot dirty: the buff's absence reaches the owner on the reconcile it already receives.
			Remove(templateID);
			return true;
		}

		/// <summary>
		/// Whether a buff is written to the database with its owner. Permanent buffs are not: they are the environment's
		/// (weather exposure, a buff volume), re-applied within a tick of a login from where the character stands, and a
		/// saved one could only come back as a buff that expires on its first tick. Pure.
		/// </summary>
		public static bool IsPersistedBuff(BaseBuffTemplate template)
		{
			return template != null && !template.IsPermanent;
		}

		/// <inheritdoc />
		public void CaptureBuffs(uint localTick, float tickDelta, Action<int, float, float, int, int> emit)
		{
			if (emit == null || Buffs == null || Buffs.Count == 0)
			{
				return;
			}
			uint currentTick = ResolveAuthoritativeTick(localTick);
			foreach (KeyValuePair<int, Buff> kvp in Buffs)
			{
				Buff buff = kvp.Value;
				if (buff == null || !IsPersistedBuff(buff.Template))
				{
					continue;
				}
				// Absolute ticks to remaining seconds; negatives clamp to 0 (about to expire).
				int remainingTicks = (int)(buff.ExpiryTick - currentTick);
				int nextTickTicks = (int)(buff.NextTickTick - currentTick);
				emit(buff.Template.ID,
					remainingTicks > 0 ? remainingTicks * tickDelta : 0f,
					nextTickTicks > 0 ? nextTickTicks * tickDelta : 0f,
					buff.Stacks,
					buff.TickCount);
			}
		}

		/// <inheritdoc />
		public bool RestoreBuff(int templateID, float remainingSeconds, float tickSeconds, int stacks, int tickCount, uint localTick, float tickDelta)
		{
			/* A row for a permanent buff predates the rule that leaves them out of the save. Restored, it would come back as
			 * a buff that expires on its first tick; skipped, the environment applies the real one and the next save
			 * deletes the row. A template this peer cannot resolve is restored as before. */
			BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			if (template != null && !IsPersistedBuff(template))
			{
				return false;
			}
			// A buff that lasts until removed (a mount) comes back without an end: its saved remaining time is meaningless.
			uint expiryTick = template != null && template.LastsUntilRemoved
				? TimeManager.UNSET_TICK
				: localTick + (uint)Math.Max(1.0, Math.Ceiling(remainingSeconds / tickDelta));
			uint nextTickTick = localTick + (uint)Math.Max(1.0, Math.Ceiling(tickSeconds / tickDelta));
			Apply(new Buff(templateID, expiryTick, nextTickTick, tickDelta, stacks, tickCount));
			return true;
		}
	}
}
