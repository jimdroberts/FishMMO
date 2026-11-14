using System;
using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Exact delta encoding for floats: each changed component travels as the difference between the two values' IEEE-754
	/// bit patterns, zigzag varint packed. The reader adds it back to the baseline's bits, so the decoded value is the
	/// written one BIT FOR BIT.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why not FishNet's vector deltas.</b> They round each difference to 1 mm (a quaternion's to 1e-4) and do not send
	/// a smaller one at all, so a delta reconcile restored the owner up to a couple of millimetres from the server's state.
	/// The motor then replayed from a state the server never had, and any threshold a millimetre can cross (the 5 mm
	/// ground probe on unstable ground, a climb's arrival and top checks) could decide differently on the replay than on
	/// the server, which shows as a correction.
	/// </para>
	/// <para>
	/// <b>Cost.</b> A difference of N units in the last place packs to about log128(2N) bytes: a few metres from the
	/// baseline at a few hundred metres out is 3 bytes an axis, about one more than the rounded form. A component that
	/// crosses zero costs up to 5 bytes for that one write, because sign and magnitude are separate in the bit pattern.
	/// Unchanged components cost nothing beyond a 3- or 4-bit mask.
	/// </para>
	/// </remarks>
	public static class LosslessFloatDelta
	{
		/// <summary>The bit-pattern difference from <paramref name="previous"/> to <paramref name="next"/>; 0 when bitwise equal.</summary>
		public static int Difference(float previous, float next)
		{
			return unchecked(BitConverter.SingleToInt32Bits(next) - BitConverter.SingleToInt32Bits(previous));
		}

		/// <summary><paramref name="previous"/> moved by a bit-pattern difference; the exact inverse of <see cref="Difference"/>.</summary>
		public static float Apply(float previous, int difference)
		{
			return BitConverter.Int32BitsToSingle(unchecked(BitConverter.SingleToInt32Bits(previous) + difference));
		}

		/// <summary>
		/// Writes the changed components of <paramref name="next"/> against <paramref name="previous"/>: a mask byte, then
		/// each changed component's difference. Writes nothing and returns false when nothing changed, unless
		/// <paramref name="always"/>, which writes an empty mask.
		/// </summary>
		public static bool WriteVector3(Writer writer, Vector3 previous, Vector3 next, bool always)
		{
			int x = Difference(previous.x, next.x);
			int y = Difference(previous.y, next.y);
			int z = Difference(previous.z, next.z);
			byte mask = (byte)((x != 0 ? 1 : 0) | (y != 0 ? 2 : 0) | (z != 0 ? 4 : 0));
			if (mask == 0 && !always)
			{
				return false;
			}
			writer.WriteUInt8Unpacked(mask);
			if (x != 0) writer.WriteSignedPackedWhole(x);
			if (y != 0) writer.WriteSignedPackedWhole(y);
			if (z != 0) writer.WriteSignedPackedWhole(z);
			return true;
		}

		/// <summary>Reads what <see cref="WriteVector3(Writer, Vector3, Vector3, bool)"/> wrote.</summary>
		public static Vector3 ReadVector3(Reader reader, Vector3 previous)
		{
			byte mask = reader.ReadUInt8Unpacked();
			Vector3 result = previous;
			if ((mask & 1) != 0) result.x = Apply(previous.x, (int)reader.ReadSignedPackedWhole());
			if ((mask & 2) != 0) result.y = Apply(previous.y, (int)reader.ReadSignedPackedWhole());
			if ((mask & 4) != 0) result.z = Apply(previous.z, (int)reader.ReadSignedPackedWhole());
			return result;
		}

		/// <summary>The quaternion form of <see cref="WriteVector3(Writer, Vector3, Vector3, bool)"/>: four components.</summary>
		public static bool WriteQuaternion(Writer writer, Quaternion previous, Quaternion next, bool always)
		{
			int x = Difference(previous.x, next.x);
			int y = Difference(previous.y, next.y);
			int z = Difference(previous.z, next.z);
			int w = Difference(previous.w, next.w);
			byte mask = (byte)((x != 0 ? 1 : 0) | (y != 0 ? 2 : 0) | (z != 0 ? 4 : 0) | (w != 0 ? 8 : 0));
			if (mask == 0 && !always)
			{
				return false;
			}
			writer.WriteUInt8Unpacked(mask);
			if (x != 0) writer.WriteSignedPackedWhole(x);
			if (y != 0) writer.WriteSignedPackedWhole(y);
			if (z != 0) writer.WriteSignedPackedWhole(z);
			if (w != 0) writer.WriteSignedPackedWhole(w);
			return true;
		}

		/// <summary>Reads what <see cref="WriteQuaternion(Writer, Quaternion, Quaternion, bool)"/> wrote.</summary>
		public static Quaternion ReadQuaternion(Reader reader, Quaternion previous)
		{
			byte mask = reader.ReadUInt8Unpacked();
			Quaternion result = previous;
			if ((mask & 1) != 0) result.x = Apply(previous.x, (int)reader.ReadSignedPackedWhole());
			if ((mask & 2) != 0) result.y = Apply(previous.y, (int)reader.ReadSignedPackedWhole());
			if ((mask & 4) != 0) result.z = Apply(previous.z, (int)reader.ReadSignedPackedWhole());
			if ((mask & 8) != 0) result.w = Apply(previous.w, (int)reader.ReadSignedPackedWhole());
			return result;
		}

		/// <summary>A quaternion written whole and exact: four floats.</summary>
		public static void WriteQuaternion(Writer writer, Quaternion value)
		{
			writer.WriteSingle(value.x);
			writer.WriteSingle(value.y);
			writer.WriteSingle(value.z);
			writer.WriteSingle(value.w);
		}

		/// <summary>Reads what <see cref="WriteQuaternion(Writer, Quaternion)"/> wrote.</summary>
		public static Quaternion ReadQuaternion(Reader reader)
		{
			return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		}
	}
}
