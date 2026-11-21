using System;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>Which of an ability's numbers a modifier scales.</summary>
	public enum AbilityScalingTarget : byte
	{
		ActivationTime = 0,
		Cooldown = 1,
		Speed = 2,
		LifeTime = 3,
		Power = 4,
	}

	/// <summary>
	/// A rule that scales one of an ability's numbers by something about where and when it is cast: the weather (the Climate
	/// and Weather module's <c>WeatherAbilityModifier</c>), and whatever other modules add.
	/// </summary>
	/// <remarks>
	/// Held on templates and events as <c>[SerializeReference]</c>, so the ability data names no module: a removed module's
	/// rules drop out and every number is unscaled (Phase 4 of the core and modules plan, 2026-11-17). A rule must be a pure
	/// function of the context, which both peers evaluate alike, because ability numbers are predicted.
	/// </remarks>
	public interface IAbilityModifier
	{
		/// <summary>Whether this rule scales <paramref name="target"/>.</summary>
		bool Affects(AbilityScalingTarget target);

		/// <summary>The multiplier for <paramref name="target"/> in this context; 1 changes nothing.</summary>
		float Multiplier(AbilityModifierContext context, AbilityScalingTarget target);
	}

	/// <summary>
	/// One evaluation of an ability's modifiers: the ability, its caster, and the input tick, plus a cache so rules of one
	/// kind can share what they look up (the weather rules take one sample between them).
	/// </summary>
	public sealed class AbilityModifierContext
	{
		/// <summary>The ability being cast.</summary>
		public Ability Ability { get; private set; }

		/// <summary>The behaviour running the replicate, for tick resolution.</summary>
		public NetworkBehaviour Behaviour { get; private set; }

		/// <summary>The caster.</summary>
		public ICharacter Character { get; private set; }

		/// <summary>The replicate's input tick.</summary>
		public uint InputTick { get; private set; }

		private readonly Dictionary<Type, object> cache = new Dictionary<Type, object>();

		internal void Begin(Ability ability, NetworkBehaviour behaviour, ICharacter character, uint inputTick)
		{
			Ability = ability;
			Behaviour = behaviour;
			Character = character;
			InputTick = inputTick;
			cache.Clear();
		}

		internal void End()
		{
			Ability = null;
			Behaviour = null;
			Character = null;
			cache.Clear();
		}

		/// <summary>A value a rule cached for this evaluation, keyed by its type.</summary>
		public bool TryGet<T>(out T value)
		{
			if (cache.TryGetValue(typeof(T), out object boxed) && boxed is T typed)
			{
				value = typed;
				return true;
			}
			value = default;
			return false;
		}

		/// <summary>Caches a value for the rest of this evaluation.</summary>
		public void Set<T>(T value) => cache[typeof(T)] = value;
	}

	/// <summary>Evaluates an ability's modifiers: its template's and every event's.</summary>
	public static class AbilityModifiers
	{
		/// <summary>Reused: evaluations run on the main thread, one at a time.</summary>
		private static readonly AbilityModifierContext context = new AbilityModifierContext();

		/// <summary>The product of every rule on <paramref name="ability"/> that scales <paramref name="target"/>; 1 when none does.</summary>
		public static float Multiplier(Ability ability, NetworkBehaviour behaviour, ICharacter character, uint inputTick, AbilityScalingTarget target)
		{
			if (ability == null || character?.GameObject == null || character.Transform == null)
			{
				return 1f;
			}

			// The cheap question first: is there any rule for this target anywhere on this ability?
			bool any = Any(ability.Template?.Modifiers, target);
			if (!any && ability.AbilityEvents != null)
			{
				foreach (KeyValuePair<int, AbilityEvent> entry in ability.AbilityEvents)
				{
					if (Any(entry.Value?.Modifiers, target))
					{
						any = true;
						break;
					}
				}
			}
			if (!any)
			{
				return 1f;
			}

			context.Begin(ability, behaviour, character, inputTick);
			try
			{
				float product = Product(ability.Template?.Modifiers, target);
				if (ability.AbilityEvents != null)
				{
					foreach (KeyValuePair<int, AbilityEvent> entry in ability.AbilityEvents)
					{
						product *= Product(entry.Value?.Modifiers, target);
					}
				}
				return Mathf.Max(0f, product);
			}
			finally
			{
				context.End();
			}
		}

		/// <summary>Whether any rule in <paramref name="rules"/> scales <paramref name="target"/>.</summary>
		public static bool Any(List<IAbilityModifier> rules, AbilityScalingTarget target)
		{
			if (rules == null)
			{
				return false;
			}
			for (int i = 0; i < rules.Count; i++)
			{
				if (rules[i] != null && rules[i].Affects(target))
				{
					return true;
				}
			}
			return false;
		}

		private static float Product(List<IAbilityModifier> rules, AbilityScalingTarget target)
		{
			if (rules == null || rules.Count == 0)
			{
				return 1f;
			}
			float product = 1f;
			for (int i = 0; i < rules.Count; i++)
			{
				IAbilityModifier rule = rules[i];
				if (rule != null && rule.Affects(target))
				{
					product *= rule.Multiplier(context, target);
				}
			}
			return product;
		}
	}
}
