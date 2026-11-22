namespace FishMMO.Shared.Core
{
	/// <summary>
	/// A brain that can be charmed: turned, for a while, to fight for another character (the Mesmerize ability).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Server-side only, like <see cref="INPCBrain"/>, which the NPCs module's brain implements alongside this; content
	/// reaches it with <c>character.TryGet(out INPCBrain brain) &amp;&amp; brain is INPCCharm charm</c>. A client has no
	/// brain, so a charm there is only the buff's effect.
	/// </para>
	/// <para>
	/// While charmed the NPC answers to its charmer as a pet does: it follows, it fights what attacks the charmer and what
	/// the charmer attacks, and the harm rule, kill credit and threat judge it as the charmer's (<c>NPC.CharmedBy</c>).
	/// When the charm ends it forgets the fight and goes back to being itself.
	/// </para>
	/// </remarks>
	public interface INPCCharm
	{
		/// <summary>The character the NPC fights for now, or null when it is not charmed.</summary>
		ICharacter CharmMaster { get; }

		/// <summary>Turns the NPC to <paramref name="master"/>. False when it cannot be charmed now.</summary>
		bool BeginCharm(ICharacter master);

		/// <summary>Ends the charm <paramref name="master"/> holds. A charm held by someone else is left alone.</summary>
		void EndCharm(ICharacter master);
	}
}
