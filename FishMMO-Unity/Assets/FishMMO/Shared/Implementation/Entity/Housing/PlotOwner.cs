using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Who owns a plot: a character, a guild, or nobody.
	/// </summary>
	/// <remarks>
	/// A plot row stores ownership as two columns, an owning character and an owning guild, and
	/// exactly one of them may be set. Nothing in the schema enforces that — the codebase has no
	/// check constraints, and neither column can carry a foreign key because both use zero to mean
	/// "none" — so the invariant is held here instead, in a type that cannot represent a plot owned
	/// by a character <em>and</em> a guild at once.
	///
	/// <para>Rows are read back through <see cref="TryFromColumns"/> rather than a constructor, so
	/// a row that does somehow hold both is refused at the boundary instead of being resolved by
	/// whichever column the reader happened to check first. Two readers guessing differently about
	/// the same plot is how one owner ends up able to build on it and another ends up able to sell
	/// it.</para>
	/// </remarks>
	public readonly struct PlotOwner : IEquatable<PlotOwner>
	{
		/// <summary>
		/// An unclaimed plot.
		/// </summary>
		public static readonly PlotOwner None = default;

		/// <summary>
		/// What kind of holder owns the plot.
		/// </summary>
		public PlotOwnerType Type { get; }

		/// <summary>
		/// The owner's identifier, interpreted according to <see cref="Type"/>. Zero when unowned.
		/// </summary>
		public long ID { get; }

		private PlotOwner(PlotOwnerType type, long id)
		{
			Type = type;
			ID = id;
		}

		/// <summary>
		/// True when somebody owns the plot.
		/// </summary>
		public bool IsOwned => Type != PlotOwnerType.Unowned;

		/// <summary>
		/// The owning character, or zero when the plot is unowned or guild-owned.
		/// </summary>
		/// <remarks>
		/// Written straight into the plot row's owner column, which is why it reports zero rather
		/// than the guild's identifier for a guild-owned plot.
		/// </remarks>
		public long CharacterID => Type == PlotOwnerType.Character ? ID : 0;

		/// <summary>
		/// The owning guild, or zero when the plot is unowned or character-owned.
		/// </summary>
		public long GuildID => Type == PlotOwnerType.Guild ? ID : 0;

		/// <summary>
		/// Ownership by an individual character.
		/// </summary>
		/// <param name="characterID">The owning character. Must be greater than zero.</param>
		/// <exception cref="ArgumentOutOfRangeException">
		/// Thrown when <paramref name="characterID"/> is not greater than zero.
		/// </exception>
		/// <remarks>
		/// Throws rather than returning <see cref="None"/>, because an identifier of zero here is a
		/// caller that has lost track of who it is acting for. Quietly folding that into "unowned"
		/// would answer a claim by releasing the plot to everyone.
		/// </remarks>
		public static PlotOwner ForCharacter(long characterID)
		{
			if (characterID <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(characterID), characterID, "A character owner must have an identifier greater than zero.");
			}
			return new PlotOwner(PlotOwnerType.Character, characterID);
		}

		/// <summary>
		/// Ownership by a guild.
		/// </summary>
		/// <param name="guildID">The owning guild. Must be greater than zero.</param>
		/// <exception cref="ArgumentOutOfRangeException">
		/// Thrown when <paramref name="guildID"/> is not greater than zero.
		/// </exception>
		public static PlotOwner ForGuild(long guildID)
		{
			if (guildID <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(guildID), guildID, "A guild owner must have an identifier greater than zero.");
			}
			return new PlotOwner(PlotOwnerType.Guild, guildID);
		}

		/// <summary>
		/// Reads ownership back from a plot row's two owner columns.
		/// </summary>
		/// <param name="ownerCharacterID">The row's owning character, zero for none.</param>
		/// <param name="ownerGuildID">The row's owning guild, zero for none.</param>
		/// <param name="owner">The ownership the columns describe, or <see cref="None"/> on failure.</param>
		/// <returns>False when the columns contradict each other or hold a negative identifier.</returns>
		/// <remarks>
		/// Both columns set is not a state any writer here produces, so encountering it means the
		/// row was written by something else or corrupted. Refusing it keeps that damage contained
		/// to the one plot rather than letting each reader invent its own answer.
		/// </remarks>
		public static bool TryFromColumns(long ownerCharacterID, long ownerGuildID, out PlotOwner owner)
		{
			owner = None;

			if (ownerCharacterID < 0 || ownerGuildID < 0)
			{
				return false;
			}
			if (ownerCharacterID > 0 && ownerGuildID > 0)
			{
				return false;
			}

			if (ownerCharacterID > 0)
			{
				owner = new PlotOwner(PlotOwnerType.Character, ownerCharacterID);
			}
			else if (ownerGuildID > 0)
			{
				owner = new PlotOwner(PlotOwnerType.Guild, ownerGuildID);
			}

			return true;
		}

		/// <summary>
		/// True when this kind of ownership is permitted by the server's housing configuration.
		/// </summary>
		/// <remarks>
		/// The gate that <see cref="HousingOwnershipMode"/> exists to provide, asked in the one
		/// place that knows what kind of owner is being proposed. <see cref="None"/> is permitted
		/// under every mode including <see cref="HousingOwnershipMode.Neither"/>: releasing a plot
		/// has to keep working on a server that has since turned housing off, or plots claimed
		/// while it was on could never be given back.
		/// </remarks>
		public bool IsAllowedBy(HousingOwnershipMode mode)
		{
			switch (Type)
			{
				case PlotOwnerType.Character:
					return mode.AllowsPlayerOwnership();
				case PlotOwnerType.Guild:
					return mode.AllowsGuildOwnership();
				default:
					return true;
			}
		}

		/// <inheritdoc />
		public bool Equals(PlotOwner other)
		{
			return Type == other.Type && ID == other.ID;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return obj is PlotOwner other && Equals(other);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return ((int)Type * 397) ^ ID.GetHashCode();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return IsOwned ? $"{Type}:{ID}" : "Unowned";
		}

		public static bool operator ==(PlotOwner left, PlotOwner right) => left.Equals(right);

		public static bool operator !=(PlotOwner left, PlotOwner right) => !left.Equals(right);
	}
}
