using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Converts party vitals between the values the game holds and the quantised wire form.
	/// </summary>
	/// <remarks>
	/// Pure functions, unit tested for round-trip error. A fraction survives the byte with at
	/// most half a step (1/510) of error, which is below a pixel on any party bar; a rate is
	/// rounded to the unit and clamped, which is exactly what the meter readout does anyway.
	/// </remarks>
	public static class PartyVitalsQuantiser
	{
		/// <summary>Quantises a 0..1 fraction to 0..255. Out-of-range input is clamped.</summary>
		public static byte FractionToByte(float fraction)
		{
			if (float.IsNaN(fraction) || fraction <= 0.0f)
			{
				return 0;
			}
			if (fraction >= 1.0f)
			{
				return 255;
			}
			return (byte)Mathf.RoundToInt(fraction * 255.0f);
		}

		/// <summary>Restores a fraction from its byte form.</summary>
		public static float ByteToFraction(byte value)
		{
			return value / 255.0f;
		}

		/// <summary>Rounds a per-second rate to the unit and clamps it to what a ushort holds.</summary>
		public static ushort RateToUInt16(float rate)
		{
			if (float.IsNaN(rate) || rate <= 0.0f)
			{
				return 0;
			}
			if (rate >= ushort.MaxValue)
			{
				return ushort.MaxValue;
			}
			return (ushort)Mathf.RoundToInt(rate);
		}
	}

	/// <summary>
	/// Custom serializers for the party vitals payload.
	/// </summary>
	/// <remarks>
	/// Hand written so the buff array is written only when <see cref="PartyMemberVitalsEntry.BuffsChanged"/>
	/// is set — a generated writer would put a length prefix on the wire for every member every
	/// time — and so the EditMode tests can round-trip the payload without the IL weaver.
	/// <see cref="ObservedBuffEntry"/> is written field by field here for the same reason; its
	/// layout is four fields and is not expected to change without this file changing with it.
	/// </remarks>
	public static class PartyVitalsSerializer
	{
		/// <summary>Bit in the entry's flag byte: <see cref="PartyMemberVitalsEntry.BuffsChanged"/>.</summary>
		private const byte BuffsChangedBit = 1 << 0;
		/// <summary>Bit in the entry's flag byte: the buff array is non-null and non-empty.</summary>
		private const byte HasBuffsBit = 1 << 1;

		/// <summary>Upper bound accepted for a buff array length, against a corrupt or hostile stream.</summary>
		private const int MaxBuffsPerEntry = 256;
		/// <summary>Upper bound accepted for a member array length.</summary>
		private const int MaxMembers = 256;

		/// <summary>Writes one <see cref="PartyMemberVitalsEntry"/>.</summary>
		public static void WritePartyMemberVitalsEntry(this Writer writer, PartyMemberVitalsEntry value)
		{
			writer.WriteInt64(value.CharacterID);
			writer.WriteUInt8Unpacked(value.HealthPCT);
			writer.WriteUInt8Unpacked(value.ManaPCT);
			writer.WriteUInt8Unpacked(value.StaminaPCT);
			writer.WriteUInt16(value.DamagePerSecond);
			writer.WriteUInt16(value.HealPerSecond);

			bool hasBuffs = value.BuffsChanged && value.Buffs != null && value.Buffs.Length > 0;
			byte flags = 0;
			if (value.BuffsChanged) flags |= BuffsChangedBit;
			if (hasBuffs) flags |= HasBuffsBit;
			writer.WriteUInt8Unpacked(flags);

			if (!hasBuffs)
			{
				return;
			}

			writer.WriteInt32(value.Buffs.Length);
			for (int i = 0; i < value.Buffs.Length; ++i)
			{
				ObservedBuffEntry buff = value.Buffs[i];
				writer.WriteInt32(buff.TemplateID);
				writer.WriteInt32(buff.Stacks);
				writer.WriteSingle(buff.RemainingSeconds);
				writer.WriteSingle(buff.TotalSeconds);
			}
		}

		/// <summary>Reads one <see cref="PartyMemberVitalsEntry"/>.</summary>
		public static PartyMemberVitalsEntry ReadPartyMemberVitalsEntry(this Reader reader)
		{
			PartyMemberVitalsEntry value = new PartyMemberVitalsEntry()
			{
				CharacterID = reader.ReadInt64(),
				HealthPCT = reader.ReadUInt8Unpacked(),
				ManaPCT = reader.ReadUInt8Unpacked(),
				StaminaPCT = reader.ReadUInt8Unpacked(),
				DamagePerSecond = reader.ReadUInt16(),
				HealPerSecond = reader.ReadUInt16(),
			};

			byte flags = reader.ReadUInt8Unpacked();
			value.BuffsChanged = (flags & BuffsChangedBit) != 0;
			if ((flags & HasBuffsBit) == 0)
			{
				value.Buffs = null;
				return value;
			}

			int count = reader.ReadInt32();
			if (count < 0 || count > MaxBuffsPerEntry)
			{
				/* Cannot resynchronise past a count that is not trusted; the broadcast reader
				 * framing above this will discard the packet. Return what was read so far. */
				value.Buffs = null;
				return value;
			}

			ObservedBuffEntry[] buffs = new ObservedBuffEntry[count];
			for (int i = 0; i < count; ++i)
			{
				buffs[i] = new ObservedBuffEntry()
				{
					TemplateID = reader.ReadInt32(),
					Stacks = reader.ReadInt32(),
					RemainingSeconds = reader.ReadSingle(),
					TotalSeconds = reader.ReadSingle(),
				};
			}
			value.Buffs = buffs;
			return value;
		}

		/// <summary>Writes a <see cref="PartyMemberVitalsUpdateBroadcast"/>.</summary>
		public static void WritePartyMemberVitalsUpdateBroadcast(this Writer writer, PartyMemberVitalsUpdateBroadcast value)
		{
			int count = value.Members != null ? value.Members.Length : 0;
			writer.WriteInt32(count);
			for (int i = 0; i < count; ++i)
			{
				writer.WritePartyMemberVitalsEntry(value.Members[i]);
			}
		}

		/// <summary>Reads a <see cref="PartyMemberVitalsUpdateBroadcast"/>.</summary>
		public static PartyMemberVitalsUpdateBroadcast ReadPartyMemberVitalsUpdateBroadcast(this Reader reader)
		{
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxMembers)
			{
				return new PartyMemberVitalsUpdateBroadcast() { Members = System.Array.Empty<PartyMemberVitalsEntry>() };
			}

			PartyMemberVitalsEntry[] members = new PartyMemberVitalsEntry[count];
			for (int i = 0; i < count; ++i)
			{
				members[i] = reader.ReadPartyMemberVitalsEntry();
			}
			return new PartyMemberVitalsUpdateBroadcast() { Members = members };
		}
	}
}
