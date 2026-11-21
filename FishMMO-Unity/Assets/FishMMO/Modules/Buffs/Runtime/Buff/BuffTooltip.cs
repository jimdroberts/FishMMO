namespace FishMMO.Shared
{
	/// <summary>
	/// Adds the live state of one buff instance to a tooltip the template has already described.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="BaseBuffTemplate.BuildTooltip"/> can only state what every instance shares — the
	/// duration it was authored with, its tick cadence, its stack ceiling. The numbers a player
	/// actually wants while a buff is running are per-instance: how long this application has left,
	/// how many times it has stacked, how much of a spent pool remains. Only the peer holding the
	/// instance has them, and only the owner's own strip has an instance at all — the target and
	/// party frames hover somebody else's buff and call the template's tooltip alone.
	/// </para>
	/// <para>
	/// Kept out of <see cref="Buff"/> deliberately: that class is simulated state, replicated and
	/// reconciled on both peers, and nothing in it should know a tooltip exists.
	/// </para>
	/// </remarks>
	public static class BuffTooltip
	{
		/// <summary>
		/// Appends this instance's remaining time, stacks and charges, omitting whatever the buff
		/// does not have.
		/// </summary>
		/// <param name="content">The content to append to. A template's rows are already in it.</param>
		/// <param name="buff">The instance being hovered, or null for nothing to report.</param>
		/// <param name="currentTick">The tick to measure remaining time against.</param>
		public static void AppendLiveState(TooltipContent content, Buff buff, uint currentTick)
		{
			if (content == null || buff == null || buff.Template == null)
			{
				return;
			}

			BaseBuffTemplate template = buff.Template;

			/* A permanent buff has nothing to count down to, and RemainingSeconds answers with the
			 * authored duration for one, which would read as a timer that is not running. */
			if (!template.IsPermanent && template.Duration > 0.0f)
			{
				float remaining = buff.RemainingSeconds(currentTick);
				if (remaining > 0.0f)
				{
					content.AddStat("Time Left", $"{remaining:0.#}s", TooltipPriority.LiveState);
				}
			}

			/* Stacks counts applications ABOVE the first, the same convention the icon's own label
			 * uses — a single application is not annotated at all, so neither is it here. */
			if (buff.Stacks > 0)
			{
				content.AddStat("Stacks", (buff.Stacks + 1).ToString(), TooltipPriority.LiveState + 1);
			}

			/* Only for a buff that is spent rather than merely timed. The unit belongs to the
			 * template — damage points for an absorb shield, deflections for a guard — so the label
			 * stays generic and the number is the whole story. */
			if (template.InitialCharges > 0)
			{
				content.AddStat("Charges", buff.RemainingCharges.ToString(), TooltipPriority.LiveState + 2);
			}
		}
	}
}
