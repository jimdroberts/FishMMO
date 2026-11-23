using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// What the core does with a character's buffs (buffs are the Buffs module's, whose controller implements
	/// this): weather exposure applies and removes its buffs, the target frame reads the visible strip, and the server
	/// honours a player's dismiss.
	/// </summary>
	public interface ICharacterBuffs : ICharacterBehaviour
	{
		/// <summary>Raised on every peer when a character's visible buffs change; the target frame repaints.</summary>
		static Action<ICharacterBuffs> OnObservedBuffsChanged;

		/// <summary>True when a buff of the template is on the character.</summary>
		bool HasBuff(int templateID);

		/// <summary>Applies a buff of the template (predicted on the owner, authoritative on the server).</summary>
		void ApplyTemplate(IBuffTemplateInfo template, PredictionTick currentTick, ICharacter caster = null);

		/// <summary>Removes the buff of the template, if it is on.</summary>
		void Remove(int buffID);

		/// <summary>The tick the buffs count in on this peer.</summary>
		uint GetCurrentDomainTick();

		/// <summary>Adds the character's buffs as the strip shows them (template, stacks, remaining and total seconds).</summary>
		void CaptureObserved(List<ObservedBuffEntry> into);

		/// <summary>Removes the buff of the template if a player may dismiss it; false when it is not on or may not be.</summary>
		bool TryDismissByPlayer(int templateID);

		/// <summary>
		/// True when this peer runs this character's buff EFFECTS, rather than merely tracking the
		/// strip for display.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The server always does; the owning client does too, because it predicts its own
		/// simulation. A third-party observer holds real <see cref="Buff"/> instances — Inspect, the
		/// target frame and aggro all read them — but has never run <c>Buff.Apply</c>, so it must
		/// not run <c>Buff.Remove</c> either, and must not spend anything a buff is holding.
		/// </para>
		/// <para>
		/// On the interface because mitigation asks it from outside the controller:
		/// <c>DamageMitigation</c> is allowed to COMPUTE how much a shield would block on any peer —
		/// which is what lets a caster predict a blocked hit honestly — but may only spend the pool
		/// where this is true.
		/// </para>
		/// </remarks>
		bool SimulatesBuffEffects { get; }

		/// <summary>
		/// Removes all non-permanent buffs from the character, optionally suppressing removal events.
		/// </summary>
		/// <param name="ignoreInvokeRemove">If true, does not invoke OnRemoveBuff/OnRemoveDebuff events.</param>
		/// <param name="includePermanent">
		/// True to remove permanent buffs as well. Default false so gameplay dispels leave them
		/// alone; the lifecycle callers pass true, because a pooled object inheriting the previous
		/// occupant's permanent buff also inherits its attribute modifiers.
		/// </param>
		/// <param name="preserveFX">
		/// True to leave buff effects showing. For a REFRESH — the spawn payload clears before it
		/// re-reads — where tearing effects down first makes every surviving buff's FX restart.
		/// Whatever is genuinely gone is despawned by the diff that follows.
		/// </param>
		void RemoveAll(bool ignoreInvokeRemove = false, bool includePermanent = false, bool preserveFX = false);
	}

	/// <summary>
	/// The character's buffs in the save. The core writes and reads the rows with the character's own under
	/// its claims and version; the module's controller converts between them and live buffs.
	/// </summary>
	public interface IBuffPersistence : ICharacterBehaviour
	{
		/// <summary>
		/// Emits each buff saved with its owner (not the permanent, environmental ones): template ID, remaining seconds,
		/// seconds to the next tick, stacks, ticks so far. Remaining time is measured from <paramref name="localTick"/>.
		/// </summary>
		void CaptureBuffs(uint localTick, float tickDelta, Action<int, float, float, int, int> emit);

		/// <summary>
		/// Restores one saved buff, measured from <paramref name="localTick"/>. A permanent buff's stale row is skipped (the
		/// environment re-applies it); false when it was skipped.
		/// </summary>
		bool RestoreBuff(int templateID, float remainingSeconds, float tickSeconds, int stacks, int tickCount, uint localTick, float tickDelta);
	}
}
