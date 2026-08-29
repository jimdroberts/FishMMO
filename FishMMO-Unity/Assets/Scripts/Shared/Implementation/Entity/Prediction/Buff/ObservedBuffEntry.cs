using System;
using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// One buff on another character, as the SERVER has chosen to show it to observers.
	/// Display-only: nothing on the client applies an effect from this.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is deliberately not <see cref="Buff"/>. A <see cref="Buff"/> is simulation state — it
	/// carries expiry in the owner's replicate-tick domain, a stack count that drives attribute
	/// modifiers, and a template whose <c>OnApply</c>/<c>OnRemove</c> mutate the character. Handing
	/// that to an observer would either desynchronise the observer's own prediction domain (ticks
	/// mean different things on different clients) or, worse, let a client run buff effects on a
	/// character it does not own.
	/// </para>
	/// <para>
	/// Duration travels as SECONDS remaining at the moment the server sent it, not as an absolute
	/// tick, precisely because the receiving client's tick domain is its own. The observer counts
	/// down locally from receipt. That drifts by the one-way latency, which for a bar a few pixels
	/// tall on someone else's nameplate is not worth a tick-domain translation.
	/// </para>
	/// <para>
	/// <b>The struct is wider than the wire.</b> <see cref="WriteTo"/> sends seven bytes; the
	/// in-memory fields stay at their natural widths because every consumer — the FX diff, the
	/// target frame, the party frame — indexes templates by <c>int</c> and draws bars from
	/// <c>float</c>. Narrowing the fields themselves would ripple through all of that and save
	/// nothing, since the wire form is where the bytes are spent.
	/// </para>
	/// </remarks>
	public struct ObservedBuffEntry : IEquatable<ObservedBuffEntry>
	{
		/// <summary>The buff template's cached ID.</summary>
		public int TemplateID;

		/// <summary>
		/// Stack count above the base application: 0 means one application, 2 means three.
		/// </summary>
		/// <remarks>
		/// True again as of the 2026-08-28 audit. A freshly applied stacking buff used to run both
		/// the new-buff branch and the stack branch, so it arrived here reporting 1 while having
		/// applied its modifier twice; <c>MaxStacks</c> is the total number of applications.
		/// </remarks>
		public int Stacks;

		/// <summary>Seconds remaining when the server sent this, or 0 for a permanent buff.</summary>
		public float RemainingSeconds;

		/// <summary>
		/// The buff's full authored duration in seconds, straight from the template — including for a
		/// permanent buff, whose authored Duration is reported as-is. Only <c>RemainingSeconds</c> is
		/// 0 for a permanent buff.
		/// </summary>
		/// <remarks>
		/// <b>Not sent.</b> This is <c>BaseBuffTemplate.Duration</c> — authored content the receiver
		/// already holds, not runtime state — so <see cref="ReadFrom"/> resolves it from the
		/// template rather than paying for it once per entry per observer. A template the receiver
		/// cannot resolve yields 0, which every consumer already treats as "no bar to fill".
		/// </remarks>
		public float TotalSeconds;

		/// <summary>Largest remaining/total duration the wire form can carry, in seconds.</summary>
		/// <remarks>
		/// <see cref="RemainingSeconds"/> travels as deciseconds in a <c>ushort</c>. Just over 109
		/// minutes, against authored buff durations measured in seconds to minutes; anything longer
		/// clamps and displays as a bar that starts full and stays there slightly too long, which is
		/// the same thing a permanent buff already does.
		/// </remarks>
		public const float MaxEncodableSeconds = 6553.5f;

		/// <summary>Largest stack count the wire form can carry.</summary>
		/// <remarks>
		/// <c>BaseBuffTemplate.MaxStacks</c> is authored well below this; the clamp exists so a
		/// runaway stack cannot corrupt the stream, not because 255 stacks is expected.
		/// </remarks>
		public const int MaxEncodableStacks = byte.MaxValue;

		/// <summary>
		/// Structural equality: the fields that make this a DIFFERENT buff rather than the same
		/// buff a moment later.
		/// </summary>
		/// <remarks>
		/// <see cref="RemainingSeconds"/> is deliberately excluded. It moves every tick on every
		/// buff, so including it would mark the whole list changed on every tick and collapse the
		/// delta back into a full resend. Timing drift is a separate concern with its own gate —
		/// see <c>BuffController.ObservedTimingDriftExceedsTolerance</c>, which answers it with a
		/// periodic full set instead.
		/// </remarks>
		public bool StructurallyEquals(ObservedBuffEntry other)
		{
			return TemplateID == other.TemplateID && Stacks == other.Stacks;
		}

		/// <inheritdoc/>
		public bool Equals(ObservedBuffEntry other)
		{
			return TemplateID == other.TemplateID &&
				   Stacks == other.Stacks &&
				   RemainingSeconds.Equals(other.RemainingSeconds) &&
				   TotalSeconds.Equals(other.TotalSeconds);
		}

		/// <inheritdoc/>
		public override bool Equals(object obj)
		{
			return obj is ObservedBuffEntry other && Equals(other);
		}

		/// <inheritdoc/>
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = TemplateID;
				hash = (hash * 397) ^ Stacks;
				hash = (hash * 397) ^ RemainingSeconds.GetHashCode();
				hash = (hash * 397) ^ TotalSeconds.GetHashCode();
				return hash;
			}
		}

		/// <summary>Writes one entry — seven bytes.</summary>
		/// <remarks>
		/// <c>WriteInt32Unpacked</c> rather than <c>WriteInt32</c>: template ids are a deterministic
		/// 32-bit hash (<c>CachedScriptableObject.AddToCache</c>), so they occupy the whole range
		/// and FishNet's signed-packed form would spend FIVE bytes on one. Unpacked is exactly four.
		/// </remarks>
		public void WriteTo(Writer writer)
		{
			writer.WriteInt32Unpacked(TemplateID);
			writer.WriteUInt8Unpacked((byte)Mathf.Clamp(Stacks, 0, MaxEncodableStacks));
			writer.WriteUInt16Unpacked(EncodeSeconds(RemainingSeconds));
		}

		/// <summary>Reads one entry, resolving <see cref="TotalSeconds"/> from the template.</summary>
		public static ObservedBuffEntry ReadFrom(Reader reader)
		{
			ObservedBuffEntry entry = new ObservedBuffEntry
			{
				TemplateID = reader.ReadInt32Unpacked(),
				Stacks = reader.ReadUInt8Unpacked(),
				RemainingSeconds = DecodeSeconds(reader.ReadUInt16Unpacked()),
			};

			/* Authored data, not runtime state: the receiver holds the same template database the
			 * sender does. A miss leaves 0, which every bar treats as "nothing to fill" rather than
			 * dividing by it. */
			BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(entry.TemplateID);
			entry.TotalSeconds = template != null ? template.Duration : 0f;
			return entry;
		}

		/// <summary>Seconds to deciseconds, clamped to the encodable range.</summary>
		public static ushort EncodeSeconds(float seconds)
		{
			if (seconds <= 0f)
			{
				return 0;
			}
			if (seconds >= MaxEncodableSeconds)
			{
				return ushort.MaxValue;
			}
			return (ushort)Mathf.RoundToInt(seconds * 10f);
		}

		/// <summary>Deciseconds back to seconds.</summary>
		public static float DecodeSeconds(ushort deciseconds)
		{
			return deciseconds * 0.1f;
		}
	}
}
