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
		/// Instantiates the FXPrefab on the target when the buff becomes visible on it (client-side only).
		/// </summary>
		/// <remarks>
		/// <para>
		/// Returns the instance so the caller can own its lifetime. <see cref="BuffController"/>
		/// tracks one instance per template per character and hands it back to
		/// <see cref="OnRemoveFX"/> when the buff leaves; before that the instance was fire-and-forget
		/// and outlived the buff it was meant to show — on the owner and on every observer.
		/// </para>
		/// <para>
		/// <paramref name="buff"/> is null when the FX is driven by the observer-facing list rather
		/// than a simulated buff: observers no longer hold <see cref="Buff"/> instances for other
		/// characters, only <see cref="ObservedBuffEntry"/>. Overrides must tolerate that.
		/// </para>
		/// <para>
		/// Parented under <see cref="ICharacter.MeshRoot"/> so it follows the model. That root is
		/// cleared when the race model (re)loads, which destroys the instance; the controller
		/// re-creates it from <see cref="IModelReadyHandler.OnModelReady"/>.
		/// </para>
		/// </remarks>
		/// <param name="buff">The simulated buff instance, or null for an observed buff.</param>
		/// <param name="target">The character receiving the buff.</param>
		/// <returns>The instantiated FX, or null when there is nothing to show.</returns>
		public virtual GameObject OnApplyFX(Buff buff, ICharacter target)
		{
			if (target == null)
			{
				return null;
			}
#if !UNITY_SERVER
			if (loadedFXPrefab != null)
			{
				Transform parent = target.MeshRoot != null ? target.MeshRoot : target.Transform;
				return Instantiate(loadedFXPrefab, parent);
			}
#endif
			return null;
		}

		/// <summary>
		/// Tears down an FX instance previously returned by <see cref="OnApplyFX"/> (client-side only).
		/// </summary>
		/// <param name="fxInstance">The instance to remove. May already be destroyed.</param>
		/// <param name="target">The character the buff left. May be null during teardown.</param>
		public virtual void OnRemoveFX(GameObject fxInstance, ICharacter target)
		{
			if (fxInstance != null)
			{
				Destroy(fxInstance);
			}
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
		/// <para>
		/// <b>Server only.</b> With state forwarding off, the owner still ticks its own buffs for
		/// prediction, but an ECA trigger is a side effect — damage credit, threat, chained
		/// effects, achievements — and must run exactly once. The owner's health is corrected by
		/// the attribute reconcile; nothing here needs to run on a client.
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
			if (ShouldSuppressTickSideEffects(buff))
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
		/// <b>Both sides, side effects on one.</b> The resource mutation runs on the owning client
		/// as well as the server, which is what keeps predicted health in step with the server's.
		/// The triggers hanging off <c>Damage</c>/<c>Heal</c> are passed as suppressed on every
		/// client — replayed or not — and only ever fire on the server. Before this, the flag
		/// tracked replay alone, so the owner's first pass over a tick fired achievements and
		/// combat triggers a second time alongside the server. See
		/// <see cref="ShouldSuppressTickSideEffects"/>.
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
			bool suppressTriggers = ShouldSuppressTickSideEffects(buff);

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

		/// <summary>
		/// True when a tick's non-idempotent side effects (ECA triggers, achievement and combat
		/// hooks) must not run for <paramref name="buff"/>'s current tick.
		/// </summary>
		/// <remarks>
		/// Suppressed on every client, and on the server during a replay. The only pass that is
		/// allowed to have consequences is the server's first execution of a tick.
		/// </remarks>
		/// <param name="buff">The buff being ticked.</param>
		internal static bool ShouldSuppressTickSideEffects(Buff buff)
		{
			return buff == null || buff.IsReplaying || !buff.IsAuthoritative;
		}
	}
}