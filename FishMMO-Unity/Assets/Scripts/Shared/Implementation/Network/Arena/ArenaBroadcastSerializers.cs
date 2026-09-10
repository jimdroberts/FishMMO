using FishNet.Serializing;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Hand written wire format for <see cref="ArenaMatchStateBroadcast"/> and the entries it
	/// carries.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The match state goes to every occupant of an arena instance once a second for the clock,
	/// and again on every phase change and every score change — so it is the one arena message
	/// whose size is worth shaping. Two things the generated serializer had to send
	/// unconditionally are shaped away here:
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// <see cref="ArenaObjectiveEntry.Position"/> is only meaningful for a flag lying on the
	/// ground. A flag at home has no marker to place and a carried flag hangs off the carrier's
	/// transform, so <c>ArenaFlagVisuals</c> reads the field on the dropped branch alone; a
	/// control point never has a position at all, and the producer leaves it at zero. Twelve
	/// bytes per objective per second, on every control-point map, for a field nothing read.
	/// </item>
	/// <item>
	/// A member's three flags — present, ready, reconnecting — share one byte instead of
	/// spending one each.
	/// </item>
	/// </list>
	/// <para>
	/// <see cref="ArenaMatchStateBroadcast.ArenaTemplateID"/> is written UNPACKED. Template ids
	/// are <c>(typeName + assetName).GetDeterministicHashCode()</c> values that occupy the whole
	/// 32-bit range, so FishNet's signed-packed form — a seven-bit-per-byte varint — spends five
	/// bytes where unpacked spends four. The same trade
	/// <see cref="ObservedBuffEntry.WriteTo"/> makes for a buff template id. Everything else here
	/// stays packed, because scores, team indices, seconds and object ids really are small.
	/// </para>
	/// <para>
	/// Discovered by FishNet's codegen through the <c>Write*</c>/<c>Read*</c> naming convention,
	/// and applied in the Client and Server assemblies because each struct carries
	/// <c>[UseGlobalCustomSerializer]</c> — the same arrangement
	/// <c>AbilityObserverBroadcastSerializers</c> uses. <see cref="ArenaMemberEntry"/> is marked
	/// too, so the one wire form serves both the state broadcast and
	/// <see cref="ArenaResultsBroadcast.Placements"/> rather than two shapes drifting apart.
	/// </para>
	/// </remarks>
	public static class ArenaBroadcastSerializers
	{
		/// <summary>Low bits of an objective's header byte holding its <see cref="ArenaObjectiveKind"/>.</summary>
		private const byte OBJECTIVE_KIND_MASK = 0x0F;

		/// <summary>Set when a dropped flag's world position follows the objective's fields.</summary>
		private const byte OBJECTIVE_FLAG_HAS_POSITION = 0x10;

		/// <summary>Bit in a member's flag byte: <see cref="ArenaMemberEntry.Present"/>.</summary>
		private const byte MEMBER_PRESENT_BIT = 1 << 0;

		/// <summary>Bit in a member's flag byte: <see cref="ArenaMemberEntry.Ready"/>.</summary>
		private const byte MEMBER_READY_BIT = 1 << 1;

		/// <summary>Bit in a member's flag byte: <see cref="ArenaMemberEntry.Reconnecting"/>.</summary>
		private const byte MEMBER_RECONNECTING_BIT = 1 << 2;

		/// <summary>Upper bound accepted for a seat count, against a corrupt or hostile stream.</summary>
		/// <remarks>
		/// An arena format's seats are a handful per team; the bound exists so a malformed message
		/// cannot make the reader allocate an arbitrarily large array before the stream runs out,
		/// not because 256 seats is expected.
		/// </remarks>
		public const int MaxMembers = 256;

		/// <summary>Upper bound accepted for an objective count.</summary>
		public const int MaxObjectives = 256;

		/// <summary>Upper bound accepted for a team count.</summary>
		public const int MaxTeamScores = 64;

		// ──────────────────────────────────────────────────────────────────
		//  Objective entry
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Writes one <see cref="ArenaObjectiveEntry"/>, sending its position only for a dropped
		/// flag.
		/// </summary>
		public static void WriteArenaObjectiveEntry(this Writer writer, ArenaObjectiveEntry value)
		{
			/* A position is only sent for the one state that has one to send: a flag stand whose
			 * flag is lying on the ground. Progress carries the ArenaFlagState for a flag stand and
			 * capture progress for a control point, so the kind must be tested first — a control
			 * point two interactions into a capture is not a dropped flag. */
			bool hasPosition = value.Kind == ArenaObjectiveKind.FlagStand &&
							   value.Progress == (int)ArenaFlagState.Dropped;

			byte header = (byte)((byte)value.Kind & OBJECTIVE_KIND_MASK);
			if (hasPosition)
			{
				header |= OBJECTIVE_FLAG_HAS_POSITION;
			}

			writer.WriteUInt8Unpacked(header);
			writer.WriteInt64(value.ObjectiveID);
			writer.WriteInt32(value.Team);
			writer.WriteInt32(value.Progress);
			writer.WriteInt64(value.Holder);

			if (hasPosition)
			{
				writer.WriteVector3(value.Position);
			}
		}

		/// <summary>Reads one <see cref="ArenaObjectiveEntry"/> written by the method above.</summary>
		/// <remarks>
		/// An objective that carried no position comes back at <c>Vector3.zero</c>, which is exactly
		/// what the producer set for it and what every consumer already ignores: the flag visuals
		/// only read the field on their dropped branch, and the HUD's objective lines never read it
		/// at all.
		/// </remarks>
		public static ArenaObjectiveEntry ReadArenaObjectiveEntry(this Reader reader)
		{
			byte header = reader.ReadUInt8Unpacked();

			ArenaObjectiveEntry value = new ArenaObjectiveEntry()
			{
				Kind = (ArenaObjectiveKind)(header & OBJECTIVE_KIND_MASK),
				ObjectiveID = reader.ReadInt64(),
				Team = reader.ReadInt32(),
				Progress = reader.ReadInt32(),
				Holder = reader.ReadInt64(),
				Position = Vector3.zero,
			};

			if ((header & OBJECTIVE_FLAG_HAS_POSITION) != 0)
			{
				value.Position = reader.ReadVector3();
			}
			return value;
		}

		// ──────────────────────────────────────────────────────────────────
		//  Member entry
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Writes one <see cref="ArenaMemberEntry"/>, its three flags in one byte.</summary>
		public static void WriteArenaMemberEntry(this Writer writer, ArenaMemberEntry value)
		{
			writer.WriteInt64(value.CharacterID);
			writer.WriteInt32(value.Team);
			writer.WriteInt32(value.Kills);
			writer.WriteInt32(value.Deaths);
			writer.WriteInt32(value.Score);

			byte flags = 0;
			if (value.Present) flags |= MEMBER_PRESENT_BIT;
			if (value.Ready) flags |= MEMBER_READY_BIT;
			if (value.Reconnecting) flags |= MEMBER_RECONNECTING_BIT;
			writer.WriteUInt8Unpacked(flags);
		}

		/// <summary>Reads one <see cref="ArenaMemberEntry"/> written by the method above.</summary>
		public static ArenaMemberEntry ReadArenaMemberEntry(this Reader reader)
		{
			ArenaMemberEntry value = new ArenaMemberEntry()
			{
				CharacterID = reader.ReadInt64(),
				Team = reader.ReadInt32(),
				Kills = reader.ReadInt32(),
				Deaths = reader.ReadInt32(),
				Score = reader.ReadInt32(),
			};

			byte flags = reader.ReadUInt8Unpacked();
			value.Present = (flags & MEMBER_PRESENT_BIT) != 0;
			value.Ready = (flags & MEMBER_READY_BIT) != 0;
			value.Reconnecting = (flags & MEMBER_RECONNECTING_BIT) != 0;
			return value;
		}

		// ──────────────────────────────────────────────────────────────────
		//  Match state
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Writes an <see cref="ArenaMatchStateBroadcast"/>.</summary>
		public static void WriteArenaMatchStateBroadcast(this Writer writer, ArenaMatchStateBroadcast value)
		{
			// Unpacked: a full-range template hash costs five bytes packed and four unpacked.
			writer.WriteInt32Unpacked(value.ArenaTemplateID);
			writer.WriteInt32(value.Format);
			writer.WriteUInt8Unpacked((byte)value.Phase);
			writer.WriteInt32(value.SecondsRemaining);

			int scoreCount = value.TeamScores != null ? value.TeamScores.Length : 0;
			writer.WriteInt32(scoreCount);
			for (int i = 0; i < scoreCount; ++i)
			{
				writer.WriteInt32(value.TeamScores[i]);
			}

			int memberCount = value.Members != null ? value.Members.Length : 0;
			writer.WriteInt32(memberCount);
			for (int i = 0; i < memberCount; ++i)
			{
				writer.WriteArenaMemberEntry(value.Members[i]);
			}

			int objectiveCount = value.Objectives != null ? value.Objectives.Length : 0;
			writer.WriteInt32(objectiveCount);
			for (int i = 0; i < objectiveCount; ++i)
			{
				writer.WriteArenaObjectiveEntry(value.Objectives[i]);
			}
		}

		/// <summary>Reads an <see cref="ArenaMatchStateBroadcast"/> written by the method above.</summary>
		/// <remarks>
		/// A count outside its bound cannot be resynchronised past — the entries behind it are of
		/// unknown length — so the message is abandoned at that point and returned with empty
		/// arrays, which the HUD treats as a match with nothing in it rather than indexing into a
		/// partially read one.
		/// </remarks>
		public static ArenaMatchStateBroadcast ReadArenaMatchStateBroadcast(this Reader reader)
		{
			ArenaMatchStateBroadcast value = new ArenaMatchStateBroadcast()
			{
				ArenaTemplateID = reader.ReadInt32Unpacked(),
				Format = reader.ReadInt32(),
				Phase = (ArenaMatchPhase)reader.ReadUInt8Unpacked(),
				SecondsRemaining = reader.ReadInt32(),
				TeamScores = System.Array.Empty<int>(),
				Members = System.Array.Empty<ArenaMemberEntry>(),
				Objectives = System.Array.Empty<ArenaObjectiveEntry>(),
			};

			int scoreCount = reader.ReadInt32();
			if (scoreCount < 0 || scoreCount > MaxTeamScores)
			{
				return value;
			}
			if (scoreCount > 0)
			{
				int[] scores = new int[scoreCount];
				for (int i = 0; i < scoreCount; ++i)
				{
					scores[i] = reader.ReadInt32();
				}
				value.TeamScores = scores;
			}

			int memberCount = reader.ReadInt32();
			if (memberCount < 0 || memberCount > MaxMembers)
			{
				return value;
			}
			if (memberCount > 0)
			{
				ArenaMemberEntry[] members = new ArenaMemberEntry[memberCount];
				for (int i = 0; i < memberCount; ++i)
				{
					members[i] = reader.ReadArenaMemberEntry();
				}
				value.Members = members;
			}

			int objectiveCount = reader.ReadInt32();
			if (objectiveCount < 0 || objectiveCount > MaxObjectives)
			{
				return value;
			}
			if (objectiveCount > 0)
			{
				ArenaObjectiveEntry[] objectives = new ArenaObjectiveEntry[objectiveCount];
				for (int i = 0; i < objectiveCount; ++i)
				{
					objectives[i] = reader.ReadArenaObjectiveEntry();
				}
				value.Objectives = objectives;
			}

			return value;
		}
	}
}
