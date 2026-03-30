using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for ability events. Extends <see cref="Trigger"/> to provide ECA-driven conditions and actions.
	/// Ability requirements (resources, faction, archetype, attributes) are defined as ECA conditions on the inherited
	/// <see cref="Trigger.Conditions"/> list or on the parent <see cref="BaseAbilityTemplate.ActivationConditions"/> list.
	/// </summary>
	public abstract class AbilityEvent : Trigger, ITooltip
	{
		/// <summary>
		/// The icon representing the ability event (set in the inspector).
		/// </summary>
		[SerializeField]
		private Sprite icon;

		/// <summary>
		/// Optional target selector for this event. When assigned, overrides the default target
		/// (collision target or self) and uses <see cref="TargetSelector.SelectTargets"/> to
		/// resolve one or more targets before executing this event's conditions and actions.
		/// <para>
		/// For self-target abilities (self-buffs, self-heals), assign a <see cref="SelfTargetSelector"/>.
		/// For area effects (PBAoE, AoE), assign an <see cref="AreaTargetSelector"/>.
		/// When <c>null</c>, the event uses the default target from the collision or self-target path.
		/// </para>
		/// </summary>
		[Tooltip("Optional target selector. Overrides the default collision/self target. Use SelfTargetSelector for self-buffs, AreaTargetSelector for AoE, etc. When empty, defaults to the collision target or caster.")]
		public TargetSelector TargetSelectorOverride;

		/// <summary>
		/// Time required to activate the event (in seconds). Aggregated into the runtime ability.
		/// </summary>
		public float ActivationTime;

		/// <summary>
		/// Lifetime of the event effect (in seconds). Aggregated into the runtime ability.
		/// </summary>
		public float LifeTime;

		/// <summary>
		/// Speed of the event effect (units per second). Aggregated into the runtime ability.
		/// </summary>
		public float Speed;

		/// <summary>
		/// Cooldown time for the event (in seconds). Aggregated into the runtime ability.
		/// </summary>
		public float Cooldown;

		/// <summary>
		/// Crafting price of the event (in-game currency cost to add this event during ability crafting).
		/// </summary>
		public int Price;

		/// <summary>
		/// The name of the event (from the ScriptableObject name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon representing the event (property accessor).
		/// </summary>
		public Sprite Icon { get { return this.icon; } }

		/// <summary>
		/// Returns the tooltip string for the ability event.
		/// </summary>
		/// <returns>The tooltip string for the ability event.</returns>
		public string Tooltip()
		{
			using (var builder = new TooltipBuilder())
			{
				BuildTooltip(builder);
				return builder.Build();
			}
		}

		/// <summary>
		/// Populates the tooltip builder with this ability event's tooltip lines.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public virtual void BuildTooltip(TooltipBuilder builder)
		{
			builder.AddLine("Ability Event: " + Name, 0, TooltipColors.Title);
		}
	}
}