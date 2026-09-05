namespace FishMMO.Shared
{
	/// <summary>
	/// Who may do what on a plot, resolved from its state, its owner, and the grants on it.
	/// </summary>
	/// <remarks>
	/// One place, and a pure function, because this answer is needed in several that must agree.
	/// The server decides whether a placement is written and whether a body is pushed back out of a
	/// doorway; the client greys out the buttons it knows will be refused. If those disagreed the
	/// player would be shown a door they cannot walk through, which reads as the game being broken
	/// rather than as the house being locked.
	///
	/// <para>Locked by default is the rule this encodes. A plot admits its owner, and admits
	/// everybody the owner has explicitly named; it admits nobody else, and neither an unclaimed
	/// grant row nor an unrecognised permission bit can widen that.</para>
	/// </remarks>
	public static class PlotAccess
	{
		/// <summary>
		/// Cleans a stored or received permission mask down to bits that mean something.
		/// </summary>
		/// <remarks>
		/// Applied to everything crossing a boundary — a row read back, a request from a client —
		/// so an unknown bit is dropped at the edge rather than stored and later reinterpreted. A
		/// permission retired in a later version then stops meaning anything, instead of quietly
		/// becoming whichever permission is given its bit next.
		/// </remarks>
		public static PlotPermission Sanitize(int mask)
		{
			return (PlotPermission)mask & PlotPermission.All;
		}

		/// <inheritdoc cref="Sanitize(int)"/>
		public static PlotPermission Sanitize(PlotPermission permissions)
		{
			return permissions & PlotPermission.All;
		}

		/// <summary>
		/// True when a character is the plot's owner, personally or through their guild.
		/// </summary>
		/// <param name="owner">Who holds the plot.</param>
		/// <param name="characterID">The character asking.</param>
		/// <param name="characterGuildID">Their guild, or zero when they are in none.</param>
		/// <remarks>
		/// Guild membership is passed in rather than looked up, so this stays a function of its
		/// arguments and can be reasoned about — and tested — without a guild system to hand. A
		/// character with no guild can never match a guild-owned plot, which is why the zero case is
		/// rejected explicitly rather than left to compare equal to an unset owner column.
		/// </remarks>
		public static bool IsOwner(PlotOwner owner, long characterID, long characterGuildID)
		{
			switch (owner.Type)
			{
				case PlotOwnerType.Character:
					return characterID > 0 && owner.ID == characterID;
				case PlotOwnerType.Guild:
					return characterGuildID > 0 && owner.ID == characterGuildID;
				default:
					return false;
			}
		}

		/// <summary>
		/// Everything a character may do on a plot right now.
		/// </summary>
		/// <param name="state">The plot's lifecycle state.</param>
		/// <param name="owner">Who holds it.</param>
		/// <param name="characterID">The character asking.</param>
		/// <param name="characterGuildID">Their guild, or zero.</param>
		/// <param name="granted">What the plot's access list gives them, if anything.</param>
		/// <remarks>
		/// State is consulted before ownership, because two of the four states answer the same way
		/// for everybody. An abandoned plot admits nobody at all — there is no owner left to admit
		/// them and nothing standing on it to enter — and an empty lot is a piece of the public
		/// world that happens to be for sale, so people may walk across it and nothing more.
		///
		/// <para>A grant is only honoured on an <see cref="PlotState.Occupied"/> plot. Rows outlive
		/// the ownership that created them: a plot reclaimed for unpaid tax and bought by somebody
		/// else must not come with the previous owner's friends still holding keys, and clearing
		/// those rows is a write that can fail or be interrupted. Ignoring them outside the one
		/// state where they apply makes that cleanup a tidiness matter rather than a security
		/// one.</para>
		/// </remarks>
		public static PlotPermission Resolve(
			PlotState state,
			PlotOwner owner,
			long characterID,
			long characterGuildID,
			PlotPermission granted)
		{
			if (state == PlotState.Abandoned)
			{
				return PlotPermission.None;
			}

			if (state == PlotState.Empty)
			{
				/* Nobody owns a bare lot, so nobody may build on one — claiming is how that starts,
				 * and it is not a permission. Walking over it is fine; it is a field. */
				return PlotPermission.Enter;
			}

			if (IsOwner(owner, characterID, characterGuildID))
			{
				return PlotPermission.All;
			}

			/* Building is closed to everyone but the owner, grant or no grant. The ground is moving
			 * — structures appear and vanish as they are placed — and a visitor standing in one is a
			 * visitor inside a wall that was not there a moment ago. */
			if (state == PlotState.Building)
			{
				return PlotPermission.None;
			}

			return Sanitize(granted);
		}

		/// <summary>
		/// True when a character may cross into the plot.
		/// </summary>
		public static bool AllowsEntry(
			PlotState state,
			PlotOwner owner,
			long characterID,
			long characterGuildID,
			PlotPermission granted)
		{
			return Resolve(state, owner, characterID, characterGuildID, granted).HasFlag(PlotPermission.Enter);
		}

		/// <summary>
		/// What a character with <see cref="PlotPermission.InviteFriends"/> is actually able to hand out.
		/// </summary>
		/// <param name="granterHolds">What the character doing the granting holds themselves.</param>
		/// <param name="requested">What they are trying to give.</param>
		/// <remarks>
		/// Nobody may grant a permission they do not hold. Without this the whole model collapses to
		/// its weakest link: a friend given only <see cref="PlotPermission.Enter"/> and
		/// <see cref="PlotPermission.InviteFriends"/> could grant themselves — or a stranger — the
		/// right to strip the house, and the owner's careful choice of what to trust them with would
		/// have meant nothing.
		///
		/// <para>An owner passes <see cref="PlotPermission.All"/> and is therefore unclamped, which
		/// is the intended asymmetry: the owner is the source of the authority, not a holder of
		/// it.</para>
		/// </remarks>
		public static PlotPermission ClampGrant(PlotPermission granterHolds, PlotPermission requested)
		{
			if (!granterHolds.HasFlag(PlotPermission.InviteFriends))
			{
				return PlotPermission.None;
			}

			return Sanitize(requested) & Sanitize(granterHolds);
		}
	}
}
