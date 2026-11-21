using System;
using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared.Core;

[assembly: FishMMO.Shared.HarmRelation(typeof(FishMMO.Shared.CompanionHarmRelation))]

namespace FishMMO.Shared
{
	/// <summary>What one relation says about a hit: nothing (ask the next), yes, no, or yes only because the attacker chose to.</summary>
	public enum HarmJudgement : byte
	{
		/// <summary>This relation has no say; the next one decides.</summary>
		Undecided = 0,
		/// <summary>Allowed.</summary>
		Allow = 1,
		/// <summary>Refused.</summary>
		Refuse = 2,
		/// <summary>Allowed only because the attacker's side chose to fight (an aggression toggle): the attacker is the aggressor.</summary>
		AllowAsAggressor = 3,
	}

	/// <summary>
	/// One feature's say in who may harm whom: the arena's teams, wildlife, companions (party, guild, one's own pet),
	/// faction standing. <see cref="HarmRule"/> asks the relations in <see cref="Order"/>; the first that decides wins.
	/// </summary>
	/// <remarks>
	/// A feature declares its relation with <c>[assembly: HarmRelation(typeof(MyRelation))]</c>, so removing the feature
	/// (a module's folder) removes its say and nothing else: with no relation deciding, a hit is allowed.
	/// Relations are asked with each side's principal (<see cref="HarmRule.Principal"/>: a pet answers as its owner).
	/// </remarks>
	public interface IHarmRelation
	{
		/// <summary>Lower is asked first. The built-ins: arena 100, wildlife 200, companions 300, factions 400.</summary>
		int Order { get; }

		/// <summary>This relation's say about <paramref name="attacker"/> harming <paramref name="defender"/> (two different sides).</summary>
		HarmJudgement Judge(ICharacter attacker, ICharacter defender);

		/// <summary>A hit between two different sides was allowed and is about to land (each relation hears it, whoever decided).</summary>
		void OnAllowed(ICharacter attacker, ICharacter defender);

		/// <summary>
		/// A hit was refused at the character an attack cast actually struck (never area splash): <paramref name="refusedBy"/>
		/// is the relation that refused it. Each relation hears it.
		/// </summary>
		void OnRefusedAttack(ICharacter attacker, ICharacter defender, long castKey, IHarmRelation refusedBy);

		/// <summary>
		/// True when this relation's refusal is lifted by the attacking side turning its aggression toggle on (the factions'
		/// "not an enemy" refusal). False for refusals no toggle changes: an arena's teams, a pack, companions.
		/// </summary>
		bool AggressionLiftsRefusal => false;
	}

	/// <summary>A relation that also says who are companions: never harmed, never drawn hostile, whatever anyone's toggle says.</summary>
	public interface ICompanionRelation
	{
		/// <summary>Whether two principals are on one side.</summary>
		bool AreCompanions(ICharacter a, ICharacter b);
	}

	/// <summary>Declares a feature's <see cref="IHarmRelation"/> (it needs a public parameterless constructor).</summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class HarmRelationAttribute : Attribute
	{
		public Type RelationType { get; }

		public HarmRelationAttribute(Type relationType)
		{
			RelationType = relationType;
		}
	}

	/// <summary>
	/// Who may harm whom: the one rule every hostile effect answers to, so an ability needs no authored condition to leave
	/// friends alone. Asked by damage (<see cref="CharacterDamageController.Damage"/>) and, through
	/// <see cref="CharacterEvade"/>, by debuffs, knockback, interrupts, taunts, buff-stripping dispels and resource drains.
	/// </summary>
	/// <remarks>
	/// <para>The rule itself is Core; what decides it is supplied by features as <see cref="IHarmRelation"/>s, asked in order
	/// until one decides. Shipped: the arena's match sides (only an opponent, whatever anyone's toggle says), wildlife
	/// (outside every other relation), companions (party, guild, one's own pet: never), and faction standing with the
	/// aggression toggles. With no relation deciding, or none installed, a hit is allowed: combat works without any of
	/// them.</para>
	/// <para>No attacker (the world: a fall, lava), or the character harming itself, is never restricted. A pet answers as its
	/// owner on either side (<see cref="Principal"/>).</para>
	/// </remarks>
	public static class HarmRule
	{
		private static List<IHarmRelation> relations;
		private static readonly object gate = new object();

		/// <summary>The installed relations, in the order they are asked.</summary>
		public static IReadOnlyList<IHarmRelation> Relations => Installed();

		/// <summary>
		/// Whether <paramref name="attacker"/> may harm <paramref name="defender"/>. <paramref name="aggressor"/> is the
		/// character (a pet's owner, for a pet) whose own choice alone allowed it (an aggression toggle), else null.
		/// </summary>
		public static bool MayHarm(ICharacter attacker, ICharacter defender, out ICharacter aggressor)
		{
			return Judge(attacker, defender, out aggressor, out _);
		}

		/// <summary>
		/// True when <paramref name="attacker"/> may not harm <paramref name="defender"/> now, and the only thing in the way is
		/// the attacking side's aggression toggle being off (see <see cref="IHarmRelation.AggressionLiftsRefusal"/>). An order
		/// that names such a target (a pet sent at a neutral) can turn the toggle on and go ahead; companions, an arena's teams
		/// and a pack are never this.
		/// </summary>
		public static bool RefusedForWantOfAggression(ICharacter attacker, ICharacter defender)
		{
			return !Judge(attacker, defender, out _, out IHarmRelation refusedBy) &&
				refusedBy != null && refusedBy.AggressionLiftsRefusal;
		}

		/// <summary>
		/// <see cref="MayHarm"/>, with its consequences told to every relation: the call every hostile route makes once it is
		/// about to apply the effect. Allowed between two sides: <see cref="IHarmRelation.OnAllowed"/> (the aggression lock
		/// restarts). Refused at the character an attack cast struck: <see cref="IHarmRelation.OnRefusedAttack"/> (refused
		/// casts count toward turning aggression on).
		/// </summary>
		public static bool Allows(ICharacter attacker, ICharacter defender)
		{
			if (!Judge(attacker, defender, out _, out IHarmRelation refusedBy))
			{
				NoteRefusal(attacker, defender, refusedBy);
				return false;
			}
			if (IsUnrestricted(attacker, defender))
			{
				return true;
			}
			ICharacter attackerSide = Principal(attacker);
			ICharacter defenderSide = Principal(defender);
			if (ReferenceEquals(attackerSide, defenderSide))
			{
				return true;
			}
			foreach (IHarmRelation relation in Installed())
			{
				relation.OnAllowed(attackerSide, defenderSide);
			}
			return true;
		}

		private static bool IsUnrestricted(ICharacter attacker, ICharacter defender)
		{
			return attacker == null || defender == null || ReferenceEquals(attacker, defender) || attacker.ID == defender.ID;
		}

		private static bool Judge(ICharacter attacker, ICharacter defender, out ICharacter aggressor, out IHarmRelation refusedBy)
		{
			aggressor = null;
			refusedBy = null;
			if (IsUnrestricted(attacker, defender))
			{
				return true;
			}
			// A pet is its owner's hand: its relations, its toggle, its arena team.
			ICharacter attackerSide = Principal(attacker);
			ICharacter defenderSide = Principal(defender);
			foreach (IHarmRelation relation in Installed())
			{
				switch (relation.Judge(attackerSide, defenderSide))
				{
					case HarmJudgement.Allow:
						return true;
					case HarmJudgement.AllowAsAggressor:
						aggressor = attackerSide;
						return true;
					case HarmJudgement.Refuse:
						refusedBy = relation;
						return false;
				}
			}
			return true;
		}

		/// <summary>The attack cast whose hit is being applied, if any: see <see cref="BeginAttack"/>.</summary>
		public struct AttackScope
		{
			internal bool Active;
			internal long CastKey;
			internal ICharacter Target;
			internal bool Splash;
		}

		private static AttackScope currentAttack;

		/// <summary>
		/// Marks the effects applied until <see cref="EndAttack"/> as one hit of the cast <paramref name="castKey"/> on
		/// <paramref name="target"/>, so a refusal among them can be counted per cast. Returns the scope it replaced, for
		/// <see cref="EndAttack"/> (hits can nest).
		/// </summary>
		/// <param name="castKey">Identifies the cast (its projectiles and pulses share it).</param>
		/// <param name="target">The character the cast struck.</param>
		/// <param name="splash">True for an area blast's victims, which are never counted: a neutral beside the enemy is not a target.</param>
		public static AttackScope BeginAttack(long castKey, ICharacter target, bool splash)
		{
			AttackScope previous = currentAttack;
			currentAttack = new AttackScope { Active = true, CastKey = castKey, Target = target, Splash = splash };
			return previous;
		}

		/// <summary>Ends the scope <see cref="BeginAttack"/> opened, restoring the one it returned.</summary>
		public static void EndAttack(AttackScope previous)
		{
			currentAttack = previous;
		}

		private static void NoteRefusal(ICharacter attacker, ICharacter defender, IHarmRelation refusedBy)
		{
			AttackScope attack = currentAttack;
			if (refusedBy == null || !attack.Active || attack.Splash || attacker == null || defender == null || !ReferenceEquals(defender, attack.Target))
			{
				return;
			}
			ICharacter attackerSide = Principal(attacker);
			ICharacter defenderSide = Principal(defender);
			if (ReferenceEquals(attackerSide, defenderSide))
			{
				return;
			}
			foreach (IHarmRelation relation in Installed())
			{
				relation.OnRefusedAttack(attackerSide, defenderSide, attack.CastKey, refusedBy);
			}
		}

		/// <summary>
		/// Whether <paramref name="a"/> and <paramref name="b"/> are on one side outside an arena: any
		/// <see cref="ICompanionRelation"/> says so (the same party, the same guild, a pet and its owner, two pets of one
		/// owner). Companions are never harmed, and never drawn hostile, whatever their aggression toggles say.
		/// </summary>
		public static bool AreCompanions(ICharacter a, ICharacter b)
		{
			if (a == null || b == null)
			{
				return false;
			}
			ICharacter sideA = Principal(a);
			ICharacter sideB = Principal(b);
			if (ReferenceEquals(sideA, sideB))
			{
				return true;
			}
			foreach (IHarmRelation relation in Installed())
			{
				if (relation is ICompanionRelation companions && companions.AreCompanions(sideA, sideB))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The character whose relations <paramref name="character"/> acts under: a companion's (a pet's) owner, else itself.</summary>
		public static ICharacter Principal(ICharacter character)
		{
			return character is NPC npc && npc.CompanionOwner != null ? npc.CompanionOwner : character;
		}

		/// <summary>Adds a relation by hand (tests); a type already installed is replaced.</summary>
		public static void Install(IHarmRelation relation)
		{
			if (relation == null)
			{
				return;
			}
			lock (gate)
			{
				var next = new List<IHarmRelation>(Installed());
				next.RemoveAll(r => r.GetType() == relation.GetType());
				next.Add(relation);
				Sort(next);
				relations = next;
			}
		}

		/// <summary>Removes the relation of a type; true when there was one.</summary>
		public static bool Uninstall(Type relationType)
		{
			lock (gate)
			{
				var next = new List<IHarmRelation>(Installed());
				bool removed = next.RemoveAll(r => r.GetType() == relationType) > 0;
				relations = next;
				return removed;
			}
		}

		/// <summary>Forgets the installed relations; the next question discovers them again from the loaded assemblies.</summary>
		public static void Rediscover()
		{
			lock (gate)
			{
				relations = null;
			}
		}

		private static List<IHarmRelation> Installed()
		{
			List<IHarmRelation> current = relations;
			if (current != null)
			{
				return current;
			}
			lock (gate)
			{
				relations ??= Discover();
				return relations;
			}
		}

		/// <summary>Every <see cref="HarmRelationAttribute"/> in the loaded assemblies, instantiated and ordered.</summary>
		private static List<IHarmRelation> Discover()
		{
			var found = new List<IHarmRelation>();
			var types = new HashSet<Type>();
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				object[] attributes;
				try
				{
					attributes = assembly.GetCustomAttributes(typeof(HarmRelationAttribute), false);
				}
				catch
				{
					continue;
				}
				foreach (HarmRelationAttribute attribute in attributes)
				{
					Type type = attribute.RelationType;
					if (type == null || !typeof(IHarmRelation).IsAssignableFrom(type) || !types.Add(type))
					{
						continue;
					}
					try
					{
						found.Add((IHarmRelation)Activator.CreateInstance(type));
					}
					catch (Exception ex)
					{
						FishMMO.Logging.Log.Error("HarmRule", $"The harm relation {type.FullName} could not be created; hits are judged without it.", ex);
					}
				}
			}
			Sort(found);
			return found;
		}

		private static void Sort(List<IHarmRelation> list)
		{
			list.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.GetType().FullName, b.GetType().FullName));
		}
	}

	/// <summary>
	/// Companions: the same party, the same guild, or an owner and its own pet (the principals are then the same). Never
	/// harmed, whatever either toggle says: aggression never turns into friendly fire. Asked after the arena (where the
	/// match's teams decide alone) and wildlife (which has no party or guild).
	/// </summary>
	public sealed class CompanionHarmRelation : IHarmRelation, ICompanionRelation
	{
		public const int RelationOrder = 300;

		public int Order => RelationOrder;

		public HarmJudgement Judge(ICharacter attacker, ICharacter defender)
		{
			// Party, guild, and an owner with its own pet: never, toggle or not.
			return AreCompanions(attacker, defender) ? HarmJudgement.Refuse : HarmJudgement.Undecided;
		}

		public bool AreCompanions(ICharacter a, ICharacter b)
		{
			if (ReferenceEquals(a, b))
			{
				return true;
			}
			if (a.TryGet(out IPartyController party) && b.TryGet(out IPartyController otherParty) &&
				party.ID != 0 && party.ID == otherParty.ID)
			{
				return true;
			}
			return a.TryGet(out IGuildController guild) && b.TryGet(out IGuildController otherGuild) &&
				guild.ID != 0 && guild.ID == otherGuild.ID;
		}

		public void OnAllowed(ICharacter attacker, ICharacter defender) { }

		public void OnRefusedAttack(ICharacter attacker, ICharacter defender, long castKey, IHarmRelation refusedBy) { }
	}
}
