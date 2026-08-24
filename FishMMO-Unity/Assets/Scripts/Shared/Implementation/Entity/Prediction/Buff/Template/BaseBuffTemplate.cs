using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for all buff templates, defining shared properties, tooltip logic, and effect hooks.
	/// Stack and tick hooks are virtual with sensible defaults: stacking delegates to apply/remove,
	/// and ticking is a no-op. Derived classes only override what they need (OCP).
	/// </summary>
	public abstract class BaseBuffTemplate : CachedScriptableObject<BaseBuffTemplate>, ICachedObject, ITooltip
	{
		/// <summary>
		/// Addressable reference to the visual effect prefab to instantiate when the buff is applied.
		/// </summary>
		public AssetReferenceGameObject FXPrefabReference;

		/// <summary>
		/// The loaded FX prefab. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private GameObject loadedFXPrefab;

		/// <summary>
		/// The description of the buff, shown in tooltips.
		/// </summary>
		public string Description;

		/// <summary>
		/// Addressable reference to the icon sprite for this buff.
		/// </summary>
		[SerializeField]
		private AssetReferenceSprite icon;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The duration of the buff in seconds. If 0, the buff may be permanent or event-driven.
		/// </summary>
		public float Duration;

		/// <summary>
		/// The interval in seconds between OnTick calls while the buff is active.
		/// </summary>
		public float TickRate;

		[Header("Event-Condition-Action (ECA) Triggers")]
		/// <summary>
		/// Triggers fired on every tick of this buff.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is how periodic effects are authored. A damage-over-time buff is one of these
		/// carrying an <see cref="ApplyDamageAction"/>; a heal-over-time is the same with an
		/// <see cref="ApplyHealAction"/>. The event is fired with the buff's carrier as the target
		/// and the character who applied it as the initiator, so those actions behave identically
		/// to the same actions on an ability — resistance applies, threat is generated against the
		/// caster, and the target can die of it.
		/// </para>
		/// <para>
		/// Preferred over a bespoke template subclass. Every periodic effect worth having is a
		/// composition of actions that already exist, and expressing it here means conditions,
		/// target selectors and the two condition branches all work on a buff exactly as they do
		/// on an ability, with no C# to write and nothing new for the AI to learn to read.
		/// </para>
		/// </remarks>
		[Tooltip("Triggers fired each tick. A DoT is an ApplyDamageAction here; a HoT is an ApplyHealAction.")]
		public List<BuffTickEvent> OnTickEvents = new List<BuffTickEvent>();

		/// <summary>
		/// The maximum number of stacks this buff can have.
		/// </summary>
		public uint MaxStacks;

		/// <summary>
		/// True if the buff is permanent and does not expire.
		/// </summary>
		public bool IsPermanent;

		/// <summary>
		/// True if this buff is a debuff (negative effect).
		/// </summary>
		public bool IsDebuff;

		/// <summary>
		/// True if this buff must never be shown to anyone but the character carrying it.
		/// </summary>
		/// <remarks>
		/// The target frame shows other players' and NPCs' buffs, and the list it shows is built
		/// on the SERVER — the client is never sent, and therefore cannot reveal, anything marked
		/// here. Internal bookkeeping buffs (combat-logout markers, scripted boss-phase state,
		/// quest gates, GM effects) exist to drive logic, not to be read off an enemy's nameplate,
		/// and several of them would tell an observer something the game deliberately does not:
		/// which phase a boss is in, or whether a player is flagged for something.
		/// <para>
		/// Defaults to false so existing content is unchanged. Set it on the template.
		/// </para>
		/// </remarks>
		[Tooltip("Hide this buff from other players' target frames. Use for internal/bookkeeping buffs.")]
		public bool HiddenFromOthers;

		/// <summary>
		/// The name of this buff template (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon for this buff template (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// Returns the tooltip string for this buff, including name, description, and secondary details.
		/// </summary>
		public virtual string Tooltip()
		{
			using (var builder = new TooltipBuilder())
			{
				BuildTooltip(builder);
				return builder.Build();
			}
		}

		/// <summary>
		/// Populates the tooltip builder with this buff's tooltip lines.
		/// Override in derived classes to add additional lines.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public virtual void BuildTooltip(TooltipBuilder builder)
		{
			builder.AddLine(Name, 0, TooltipColors.Title, false, "140%");
			if (!string.IsNullOrWhiteSpace(Description))
			{
				builder.AddLine(Description, 10, TooltipColors.Label);
			}
			SecondaryTooltip(builder);
		}

		/// <summary>
		/// Appends additional information to the tooltip (e.g., secondary effects). Override in derived classes.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public virtual void SecondaryTooltip(TooltipBuilder builder) { }

		/// <summary>
		/// Called when the buff template is loaded into cache. Loads the icon and FX prefab on the client.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(BaseBuffTemplate))
				return;

#if !UNITY_SERVER
			if (icon != null && icon.RuntimeKeyIsValid())
			{
				icon.LoadAssetAsync<Sprite>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedIcon = handle.Result;
				};
			}

			if (FXPrefabReference != null && FXPrefabReference.RuntimeKeyIsValid())
			{
				AddressableLoadProcessor.LoadPrefabAsync(FXPrefabReference, (go) => loadedFXPrefab = go);
			}
#endif
		}

		/// <summary>
		/// Called when the buff template is unloaded from cache. Releases the icon and FX prefab on the client.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(BaseBuffTemplate))
			{
#if !UNITY_SERVER
				if (icon != null && icon.IsValid())
				{
					icon.ReleaseAsset();
				}
				loadedIcon = null;

				if (FXPrefabReference != null && FXPrefabReference.IsValid())
				{
					AddressableLoadProcessor.UnloadPrefab(FXPrefabReference);
				}
				loadedFXPrefab = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}

		/// <summary>
		/// Instantiates the FXPrefab on the target when the buff is applied (client-side only).
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public virtual void OnApplyFX(Buff buff, ICharacter target)
		{
			if (buff == null || target == null)
			{
				return;
			}
#if !UNITY_SERVER
			if (loadedFXPrefab != null)
			{
				Instantiate(loadedFXPrefab, target.MeshRoot != null ? target.MeshRoot : target.Transform);
			}
#endif
		}

		/// <summary>
		/// Called when the buff is applied to a character. Must be implemented by derived classes.
		/// When reached through <see cref="OnApplyStack"/>, <see cref="Buff.Stacks"/>
		/// still contains the pre-increment stack count; stack-aware templates should account
		/// for that ordering or override <see cref="OnApplyStack"/> directly.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public abstract void OnApply(Buff buff, ICharacter target);

		/// <summary>
		/// Called when the buff is removed from a character. Must be implemented by derived classes.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public abstract void OnRemove(Buff buff, ICharacter target);

		/// <summary>
		/// Called when a stack of the buff is applied. Defaults to delegating to OnApply.
		/// <see cref="Buff.Stacks"/> is incremented after this hook returns, so this hook
		/// observes the previous stack count. Override in derived classes for custom stacking behavior.
		/// </summary>
		/// <param name="buff">The buff instance being stacked.</param>
		/// <param name="target">The character receiving the stack.</param>
		public virtual void OnApplyStack(Buff buff, ICharacter target)
		{
			OnApply(buff, target);
		}

		/// <summary>
		/// Called when a stack of the buff is removed. Defaults to delegating to OnRemove.
		/// Override in derived classes for custom unstacking behavior.
		/// </summary>
		/// <param name="buff">The buff instance being unstacked.</param>
		/// <param name="target">The character losing the stack.</param>
		public virtual void OnRemoveStack(Buff buff, ICharacter target)
		{
			OnRemove(buff, target);
		}

		/// <summary>
		/// Called on each tick while the buff is active. Fires <see cref="OnTickEvents"/>.
		/// </summary>
		/// <remarks>
		/// Derived classes that add their own periodic behaviour must call
		/// <c>base.OnTick(buff, target)</c>, or the buff's authored tick events are silently
		/// dropped for that template type.
		/// </remarks>
		/// <param name="buff">The buff instance.</param>
		/// <param name="target">The character affected.</param>
		public virtual void OnTick(Buff buff, ICharacter target)
		{
			InvokeTickEvents(buff, target);
		}

		/// <summary>
		/// Fires this buff's ECA tick triggers against the character carrying it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Initiator is the caster, target is the carrier.</b> That split is what makes an
		/// <see cref="ApplyDamageAction"/> on a tick event behave like a hit rather than like
		/// self-harm: the damage lands on the carrier and is credited to whoever applied the buff,
		/// which is what generates threat and grants the kill.
		/// </para>
		/// <para>
		/// When the caster is gone — disconnected, despawned — the carrier stands in as initiator
		/// so the trigger still has a character to resolve values and conditions against. The
		/// effect continues to land, because a lingering poison is part of the simulation whether
		/// or not whoever cast it is still in the scene; it simply credits nobody, and
		/// <see cref="ApplyDamageAction"/> is content with that.
		/// </para>
		/// </remarks>
		/// <param name="buff">The buff instance.</param>
		/// <param name="target">The character carrying the buff.</param>
		protected void InvokeTickEvents(Buff buff, ICharacter target)
		{
			if (buff == null || target == null || OnTickEvents == null || OnTickEvents.Count < 1)
			{
				return;
			}

			ICharacter caster = buff.Caster;

			BuffEventData eventData = new BuffEventData(caster ?? target, buff)
			{
				Target = target.GameObject,
				TargetCharacter = target,
			};

			for (int i = 0; i < OnTickEvents.Count; ++i)
			{
				BuffTickEvent tickEvent = OnTickEvents[i];
				if (tickEvent != null)
				{
					tickEvent.Execute(eventData);
				}
			}
		}

		/// <summary>
		/// Applies one round of periodic resource changes, routing health through the damage
		/// pipeline so a tick behaves like any other hit.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why not write the resource directly.</b> Both tick templates used to call
		/// <c>CharacterResourceAttribute.AddToCurrentValue</c>, which clamps the value and raises
		/// an attribute-changed notification and nothing else. That skipped every consequence of
		/// being hurt: resistances were not applied, <c>Immortal</c> was ignored, neither party
		/// entered combat, no <c>OnDamaged</c> event was raised — so a damage-over-time effect
		/// generated no threat at all — and, worst of all, nothing could die of it.
		/// <see cref="ICharacterDamageController.Kill"/> is only ever reached from inside
		/// <c>Damage</c>, so a DoT drained its victim to zero health and stopped there; the
		/// early-out at the top of <c>Damage</c> then rejected every subsequent hit as
		/// "already dead", leaving a character permanently alive at nothing.
		/// </para>
		/// <para>
		/// <b>Health only.</b> <c>Damage</c> and <c>Heal</c> operate on the health resource. A tick
		/// against mana, stamina or any other resource has no damage semantics to borrow and keeps
		/// writing the resource directly.
		/// </para>
		/// <para>
		/// <b>Both sides, once each.</b> This runs on the client as well as the server, which is
		/// what keeps predicted health in step with the server's; the authoritative consequences
		/// are already gated inside the damage controller, whose <c>Kill</c> returns immediately
		/// off the server. ECA trigger dispatch is suppressed while replaying, because reconcile
		/// re-runs every tick since the last authoritative state and a single tick of poison must
		/// not count a dozen times toward an achievement.
		/// </para>
		/// </remarks>
		/// <param name="buff">The buff instance, supplying stacks, attribution and replay state.</param>
		/// <param name="target">The character the tick lands on.</param>
		/// <param name="tickAttributes">Per-tick resource modifiers. Positive heals, negative damages.</param>
		/// <param name="damageAttribute">
		/// Damage type used to resolve resistance when a tick reduces health. Null means the tick
		/// bypasses resistance entirely.
		/// </param>
		protected static void ApplyResourceTick(
			Buff buff,
			ICharacter target,
			System.Collections.Generic.List<BuffAttributeTemplate> tickAttributes,
			DamageAttributeTemplate damageAttribute)
		{
			if (buff == null || target == null || tickAttributes == null || tickAttributes.Count < 1)
			{
				return;
			}

			if (!target.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			// A character with no damage controller still takes the direct-write path below, so a
			// resource tick on something that cannot fight is not silently dropped.
			target.TryGet(out ICharacterDamageController damageController);
			CharacterResourceAttribute health = damageController?.ResourceInstance;

			int multiplier = 1 + buff.Stacks;

			/* Null once the caster is gone — disconnected, despawned, destroyed. The tick still
			 * lands: a lingering poison is part of the simulation whether or not whoever applied
			 * it is still around. It simply credits nobody. */
			ICharacter caster = buff.Caster;
			bool suppressTriggers = buff.IsReplaying;

			for (int i = 0; i < tickAttributes.Count; ++i)
			{
				BuffAttributeTemplate tickAttribute = tickAttributes[i];
				if (tickAttribute?.Template == null)
				{
					continue;
				}

				int amount = tickAttribute.Value * multiplier;
				if (amount == 0)
				{
					continue;
				}

				if (!attributeController.TryGetResourceAttribute(tickAttribute.Template.ID, out CharacterResourceAttribute resourceAttribute))
				{
					continue;
				}

				if (damageController != null && health != null && ReferenceEquals(resourceAttribute, health))
				{
					if (amount < 0)
					{
						damageController.Damage(caster, -amount, damageAttribute, suppressTriggers);
					}
					else
					{
						damageController.Heal(caster, amount, suppressTriggers);
					}
					continue;
				}

				// Non-health resource: no damage semantics apply.
				resourceAttribute.AddToCurrentValue(amount);
			}
		}
	}
}