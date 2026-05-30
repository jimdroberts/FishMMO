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
		/// Called on each tick while the buff is active. No-op by default.
		/// Override in derived classes for periodic effects (HoT, DoT, etc.).
		/// </summary>
		/// <param name="buff">The buff instance.</param>
		/// <param name="target">The character affected.</param>
		public virtual void OnTick(Buff buff, ICharacter target) { }
	}
}