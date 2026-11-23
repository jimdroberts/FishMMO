using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>What a <see cref="TravelTicket"/> grants.</summary>
	public enum TravelTicketKind : byte
	{
		/// <summary>
		/// A Gate traveller's way home: interacting with the lit <see cref="TravelTicket.StoneScene"/> stone takes the bound
		/// character back to the origin, until the gate's expiry.
		/// </summary>
		GateReturn = 1,

		/// <summary>A Call to Arms: the members of <see cref="TravelTicket.PartyID"/> may accept and go to the origin until expiry.</summary>
		CallToArms = 2,
	}

	/// <summary>
	/// A signed travel grant the scene servers hand each other through the client (issue #241): a Gate traveller's return ticket,
	/// or a Call to Arms summons.
	/// </summary>
	/// <remarks>
	/// <para><b>Why the client carries it.</b> Scene servers talk to each other only through the database, and the gate system
	/// has no table: a ticket rides the client from the server that issued it to the next one, which verifies it and keeps a
	/// copy while the character stays. A client that withholds it loses only its own way home.</para>
	/// <para><b>Why it is signed.</b> It names a place to be moved to. Unsigned, a modified client could write any origin and
	/// travel anywhere. HMAC-SHA256 (truncated to <see cref="MacLength"/> bytes) under a key every server derives from the
	/// deployment's signing-key KEK (<see cref="DeriveKey"/>); nothing new is stored. A ticket is bound to its character
	/// (return) or party (summons) and expires with its gate.</para>
	/// <para><b>Replay is harmless:</b> a replayed ticket only repeats the trip it already granted, to the same place, before
	/// the same expiry.</para>
	/// </remarks>
	public struct TravelTicket
	{
		/// <summary>Largest encoded ticket accepted, in bytes.</summary>
		public const int MaxBytes = 512;

		/// <summary>Bytes of the truncated MAC at the end of an encoded ticket.</summary>
		public const int MacLength = 16;

		/// <summary>Seconds a ticket is still honoured past its expiry, for clocks that disagree between machines.</summary>
		public const double ClockSkewSeconds = 5.0;

		private const byte Magic0 = (byte)'T', Magic1 = (byte)'K', Version = 1;
		private const int MaxNameBytes = 128;

		public TravelTicketKind Kind;

		/// <summary>The gate's (or summons') random id: travellers of one gate share it.</summary>
		public long ID;

		/// <summary>Return tickets: the character it belongs to. Summons: the caster.</summary>
		public long CharacterID;

		/// <summary>Summons: the party summoned. 0 on a return ticket.</summary>
		public long PartyID;

		/// <summary>When it stops being honoured, Unix milliseconds (UTC).</summary>
		public long ExpiresUnixMs;

		/// <summary>Where it takes the holder: the gate's casting spot (return) or the caster's side (summons).</summary>
		public string OriginScene;

		/// <summary>The origin's channel (scene row id); 0 for any channel of <see cref="OriginScene"/>.</summary>
		public long OriginSceneID;

		public Vector3 OriginPosition;

		/// <summary>Facing on arrival, degrees about up.</summary>
		public float OriginYaw;

		/// <summary>Return tickets: the Gate Stone's scene. Empty on a summons.</summary>
		public string StoneScene;

		/// <summary>Return tickets: the Gate Stone's waypoint index. -1 on a summons.</summary>
		public int StoneIndex;

		/// <summary>Summons: the caster's name, for the prompt. Empty on a return ticket.</summary>
		public string CasterName;

		/// <summary>Whether it is still honoured at <paramref name="nowUnixMs"/> (with <see cref="ClockSkewSeconds"/> of grace).</summary>
		public bool IsLive(long nowUnixMs)
		{
			return nowUnixMs <= ExpiresUnixMs + (long)(ClockSkewSeconds * 1000.0);
		}

		/// <summary>Seconds left at <paramref name="nowUnixMs"/>, never below zero.</summary>
		public float RemainingSeconds(long nowUnixMs)
		{
			return Mathf.Max(0.0f, (ExpiresUnixMs - nowUnixMs) / 1000.0f);
		}

		/// <summary>The ticket key: HMAC-SHA256 of a fixed label under the deployment's signing-key KEK. Null for no KEK.</summary>
		public static byte[] DeriveKey(byte[] kek)
		{
			if (kek == null || kek.Length == 0)
			{
				return null;
			}
			using (var hmac = new HMACSHA256(kek))
			{
				return hmac.ComputeHash(Encoding.ASCII.GetBytes("FishMMO travel ticket v1"));
			}
		}

		/// <summary>The signed bytes of <paramref name="ticket"/>, or null without a key.</summary>
		public static byte[] Encode(in TravelTicket ticket, byte[] key)
		{
			if (key == null || key.Length == 0)
			{
				return null;
			}
			using (var stream = new MemoryStream(128))
			using (var writer = new BinaryWriter(stream, Encoding.UTF8))
			{
				writer.Write(Magic0);
				writer.Write(Magic1);
				writer.Write(Version);
				writer.Write((byte)ticket.Kind);
				writer.Write(ticket.ID);
				writer.Write(ticket.CharacterID);
				writer.Write(ticket.PartyID);
				writer.Write(ticket.ExpiresUnixMs);
				WriteName(writer, ticket.OriginScene);
				writer.Write(ticket.OriginSceneID);
				writer.Write(ticket.OriginPosition.x);
				writer.Write(ticket.OriginPosition.y);
				writer.Write(ticket.OriginPosition.z);
				writer.Write(ticket.OriginYaw);
				WriteName(writer, ticket.StoneScene);
				writer.Write(ticket.StoneIndex);
				WriteName(writer, ticket.CasterName);
				writer.Flush();

				byte[] body = stream.ToArray();
				byte[] mac = Mac(key, body, body.Length);
				byte[] result = new byte[body.Length + MacLength];
				Buffer.BlockCopy(body, 0, result, 0, body.Length);
				Buffer.BlockCopy(mac, 0, result, body.Length, MacLength);
				return result.Length <= MaxBytes ? result : null;
			}
		}

		/// <summary>
		/// Reads and verifies <paramref name="data"/>: false for a wrong length, version or MAC, or a malformed body. Does not
		/// check expiry or who presents it; the caller does.
		/// </summary>
		public static bool TryDecode(byte[] data, byte[] key, out TravelTicket ticket)
		{
			ticket = default;
			if (data == null || key == null || key.Length == 0 || data.Length <= MacLength + 4 || data.Length > MaxBytes)
			{
				return false;
			}
			int bodyLength = data.Length - MacLength;
			byte[] expected = Mac(key, data, bodyLength);
			int difference = 0;
			for (int i = 0; i < MacLength; ++i)
			{
				difference |= expected[i] ^ data[bodyLength + i];
			}
			if (difference != 0)
			{
				return false;
			}

			try
			{
				using (var stream = new MemoryStream(data, 0, bodyLength, false))
				using (var reader = new BinaryReader(stream, Encoding.UTF8))
				{
					if (reader.ReadByte() != Magic0 || reader.ReadByte() != Magic1 || reader.ReadByte() != Version)
					{
						return false;
					}
					ticket.Kind = (TravelTicketKind)reader.ReadByte();
					ticket.ID = reader.ReadInt64();
					ticket.CharacterID = reader.ReadInt64();
					ticket.PartyID = reader.ReadInt64();
					ticket.ExpiresUnixMs = reader.ReadInt64();
					ticket.OriginScene = ReadName(reader);
					ticket.OriginSceneID = reader.ReadInt64();
					ticket.OriginPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					ticket.OriginYaw = reader.ReadSingle();
					ticket.StoneScene = ReadName(reader);
					ticket.StoneIndex = reader.ReadInt32();
					ticket.CasterName = ReadName(reader);
					if (stream.Position != bodyLength)
					{
						return false;
					}
				}
			}
			catch (EndOfStreamException)
			{
				return false;
			}
			catch (IOException)
			{
				return false;
			}
			if (ticket.Kind != TravelTicketKind.GateReturn && ticket.Kind != TravelTicketKind.CallToArms)
			{
				return false;
			}
			return !float.IsNaN(ticket.OriginPosition.x) && !float.IsNaN(ticket.OriginPosition.y) && !float.IsNaN(ticket.OriginPosition.z) &&
				!float.IsInfinity(ticket.OriginPosition.x) && !float.IsInfinity(ticket.OriginPosition.y) && !float.IsInfinity(ticket.OriginPosition.z);
		}

		private static byte[] Mac(byte[] key, byte[] data, int length)
		{
			using (var hmac = new HMACSHA256(key))
			{
				return hmac.ComputeHash(data, 0, length);
			}
		}

		private static void WriteName(BinaryWriter writer, string value)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
			int length = Math.Min(bytes.Length, MaxNameBytes);
			writer.Write((byte)length);
			writer.Write(bytes, 0, length);
		}

		private static string ReadName(BinaryReader reader)
		{
			int length = reader.ReadByte();
			if (length > MaxNameBytes)
			{
				throw new IOException("name too long");
			}
			byte[] bytes = reader.ReadBytes(length);
			if (bytes.Length != length)
			{
				throw new EndOfStreamException();
			}
			return Encoding.UTF8.GetString(bytes);
		}
	}
}
