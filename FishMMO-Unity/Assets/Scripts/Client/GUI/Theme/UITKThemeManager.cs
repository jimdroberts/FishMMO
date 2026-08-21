using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Applies the player's colour overrides to every registered UI Toolkit panel.
	/// </summary>
	/// <remarks>
	/// The UI Toolkit replacement for <c>CanvasCrawler</c>, which walked a Canvas hierarchy and
	/// wrote colours onto Image, Text and Selectable components. The shape of the job is the same;
	/// what it walks is different. Panels are found by their theme USS class rather than by
	/// component type, which is both cheaper and more precise — <c>.fish-bar__fill--hp</c> names
	/// the health bar directly, where the crawler had to infer it from where an Image sat in the
	/// hierarchy.
	///
	/// <b>Why inline styles and not the tokens.</b> FishMMO-Theme.uss declares its palette as
	/// custom properties, which is the natural place for an override to land — but UI Toolkit
	/// exposes no runtime API for setting a custom property on an element, and a StyleSheet cannot
	/// be authored at runtime either. Custom properties are therefore an authoring convenience
	/// only. A live override has to be written as an inline style, which sits above USS in the
	/// cascade and is cleared with <see cref="StyleKeyword.Null"/> to hand control back.
	///
	/// <b>What that costs.</b> Inline styles have no pseudo-state, so an override applies to an
	/// element's resting appearance and its <c>:hover</c> / <c>:active</c> rules continue to come
	/// from the stylesheet. The Canvas theme crawler had the same limitation for the same reason
	/// — it wrote a Selectable's normal colour and left the ColorBlock transitions alone — so
	/// this is the behaviour being reproduced, not a shortfall against it.
	///
	/// Panels register on show rather than being swept globally, because each UIDocument owns a
	/// separate visual tree and there is no single root to walk from.
	/// </remarks>
	public static class UITKThemeManager
	{
		// ── Theme class names the overrides map onto ──────────────────────

		private const string CLASS_PANEL        = "fish-panel";
		private const string CLASS_PANEL_HEADER = "fish-panel__header";
		private const string CLASS_PANEL_FOOTER = "fish-panel__footer";
		private const string CLASS_PANEL_TITLE  = "fish-panel__title";
		private const string CLASS_PANEL_ICON   = "fish-panel__icon";
		private const string CLASS_SLOT         = "fish-slot";
		private const string CLASS_TAB_ACTIVE   = "fish-tab--active";
		private const string CLASS_BAR_HP       = "fish-bar__fill--hp";
		private const string CLASS_BAR_MP       = "fish-bar__fill--mp";
		private const string CLASS_BAR_STAM     = "fish-bar__fill--stam";
		private const string CLASS_CROSSHAIR    = "crosshair-icon";
		private const string CLASS_TOOLTIP      = "fish-tooltip";
		private const string CLASS_TT_TITLE     = "fish-tooltip__title";
		private const string CLASS_TT_BODY      = "fish-tooltip__body";
		private const string CLASS_TT_STAT      = "fish-tooltip__stat";
		private const string CLASS_ATTR_VALUE   = "fish-attr-row__value";
		private const string CLASS_LABEL        = "fish-label";
		private const string CLASS_BUTTON       = "fish-button";

		/// <summary>
		/// Roots currently under theme control, keyed to keep re-registration idempotent.
		/// </summary>
		private static readonly HashSet<VisualElement> roots = new HashSet<VisualElement>();

		/// <summary>
		/// The theme in force, or null when nothing is overridden.
		/// </summary>
		public static UITKTheme Current { get; private set; }

		/// <summary>
		/// Raised after the theme changes and every registered root has been repainted.
		/// </summary>
		public static event Action OnThemeChanged;

		/// <summary>
		/// Loads the theme from global configuration and applies it to everything registered.
		/// </summary>
		public static void Reload()
		{
			Current = new UITKTheme(Configuration.GlobalSettings);
			ApplyToAll();
			OnThemeChanged?.Invoke();
		}

		/// <summary>
		/// Puts a panel root under theme control and paints it immediately.
		/// </summary>
		/// <param name="root">The panel's root visual element.</param>
		public static void Register(VisualElement root)
		{
			if (root == null)
			{
				return;
			}

			/* Loaded on first registration rather than from a startup hook. Panels come up at
			 * different points across three scenes and there is no single moment that reliably
			 * precedes all of them, so the first panel to appear pulls the theme in. */
			if (Current == null)
			{
				Current = new UITKTheme(Configuration.GlobalSettings);
			}

			roots.Add(root);
			Apply(root);
		}

		/// <summary>
		/// Removes a panel root from theme control.
		/// </summary>
		/// <param name="root">The panel's root visual element.</param>
		public static void Unregister(VisualElement root)
		{
			if (root == null)
			{
				return;
			}
			roots.Remove(root);
		}

		/// <summary>
		/// Repaints every registered root.
		/// </summary>
		public static void ApplyToAll()
		{
			// Copied because a panel destroyed mid-walk would otherwise mutate the set.
			VisualElement[] snapshot = new VisualElement[roots.Count];
			roots.CopyTo(snapshot);
			for (int i = 0; i < snapshot.Length; ++i)
			{
				Apply(snapshot[i]);
			}
		}

		/// <summary>
		/// Applies the current theme to one root, or clears overrides when none is set.
		/// </summary>
		/// <param name="root">The panel's root visual element.</param>
		public static void Apply(VisualElement root)
		{
			if (root == null)
			{
				return;
			}

			UITKTheme theme = Current;
			if (theme == null || !theme.IsOverridden)
			{
				ClearOverrides(root);
				return;
			}

			try
			{
				// Surfaces
				SetBackground(root, CLASS_PANEL, theme, "Background");
				SetBackground(root, CLASS_PANEL_HEADER, theme, "Primary");
				SetBackground(root, CLASS_PANEL_FOOTER, theme, "Primary");
				SetBackground(root, CLASS_SLOT, theme, "Secondary");
				SetBackground(root, CLASS_PANEL_ICON, theme, "Highlight");
				SetBackground(root, CLASS_TAB_ACTIVE, theme, "Highlight");

				// Text
				SetColor(root, CLASS_PANEL_TITLE, theme, "Text");
				SetColor(root, CLASS_LABEL, theme, "Text");
				SetColor(root, CLASS_BUTTON, theme, "Text");
				SetColor(root, CLASS_ATTR_VALUE, theme, "Text");

				// Resource bars
				SetBackground(root, CLASS_BAR_HP, theme, "Health");
				SetBackground(root, CLASS_BAR_MP, theme, "Mana");
				SetBackground(root, CLASS_BAR_STAM, theme, "Stamina");

				// Crosshair tints its icon rather than filling a box.
				SetColor(root, CLASS_CROSSHAIR, theme, "Crosshair");
				SetTint(root, CLASS_CROSSHAIR, theme, "Crosshair");

				// Tooltip
				SetBackground(root, CLASS_TOOLTIP, theme, "Background");
				SetColor(root, CLASS_TT_TITLE, theme, "TooltipTitle");
				SetColor(root, CLASS_TT_BODY, theme, "TooltipLabel");
				SetColor(root, CLASS_TT_STAT, theme, "TooltipStat");
			}
			catch (Exception ex)
			{
				/* One malformed panel must not stop the rest of the UI from being themed, and a
				 * theme failure is never worth taking a panel down for. */
				Log.Error("UITKThemeManager", "Failed to apply theme to a panel root.", ex);
			}
		}

		/// <summary>
		/// Removes every inline override this class applies, returning the root to its stylesheet.
		/// </summary>
		/// <param name="root">The panel's root visual element.</param>
		private static void ClearOverrides(VisualElement root)
		{
			string[] backgroundClasses =
			{
				CLASS_PANEL, CLASS_PANEL_HEADER, CLASS_PANEL_FOOTER, CLASS_SLOT,
				CLASS_PANEL_ICON, CLASS_TAB_ACTIVE, CLASS_BAR_HP, CLASS_BAR_MP,
				CLASS_BAR_STAM, CLASS_TOOLTIP,
			};
			for (int i = 0; i < backgroundClasses.Length; ++i)
			{
				root.Query(className: backgroundClasses[i]).ForEach(e =>
					e.style.backgroundColor = StyleKeyword.Null);
			}

			string[] colorClasses =
			{
				CLASS_PANEL_TITLE, CLASS_LABEL, CLASS_BUTTON, CLASS_ATTR_VALUE,
				CLASS_CROSSHAIR, CLASS_TT_TITLE, CLASS_TT_BODY, CLASS_TT_STAT,
			};
			for (int i = 0; i < colorClasses.Length; ++i)
			{
				root.Query(className: colorClasses[i]).ForEach(e =>
					e.style.color = StyleKeyword.Null);
			}

			root.Query(className: CLASS_CROSSHAIR).ForEach(e =>
				e.style.unityBackgroundImageTintColor = StyleKeyword.Null);
		}

		/// <summary>
		/// Resolves the colour for a theme name.
		/// </summary>
		/// <param name="theme">The theme to read from.</param>
		/// <param name="name">One of <see cref="UITKTheme.ColorNames"/>.</param>
		/// <param name="color">The resolved colour.</param>
		/// <returns>True when the player has overridden this colour.</returns>
		private static bool TryResolve(UITKTheme theme, string name, out Color color)
		{
			color = Color.white;
			if (!theme.HasOverride(name))
			{
				return false;
			}

			switch (name)
			{
				case "Primary":      color = theme.Primary;      return true;
				case "Secondary":    color = theme.Secondary;    return true;
				case "Highlight":    color = theme.Highlight;    return true;
				case "Background":   color = theme.Background;   return true;
				case "Text":         color = theme.Text;         return true;
				case "Health":       color = theme.Health;       return true;
				case "Mana":         color = theme.Mana;         return true;
				case "Stamina":      color = theme.Stamina;      return true;
				case "Crosshair":    color = theme.Crosshair;    return true;
				case "TooltipTitle": color = theme.TooltipTitle; return true;
				case "TooltipLabel": color = theme.TooltipLabel; return true;
				case "TooltipValue": color = theme.TooltipValue; return true;
				case "TooltipStat":  color = theme.TooltipStat;  return true;
				default:                                          return false;
			}
		}

		/// <summary>
		/// Sets background-color on every element carrying a class, or clears it when unthemed.
		/// </summary>
		private static void SetBackground(VisualElement root, string className, UITKTheme theme, string name)
		{
			if (TryResolve(theme, name, out Color color))
			{
				root.Query(className: className).ForEach(e => e.style.backgroundColor = color);
			}
			else
			{
				root.Query(className: className).ForEach(e => e.style.backgroundColor = StyleKeyword.Null);
			}
		}

		/// <summary>
		/// Sets color on every element carrying a class, or clears it when unthemed.
		/// </summary>
		private static void SetColor(VisualElement root, string className, UITKTheme theme, string name)
		{
			if (TryResolve(theme, name, out Color color))
			{
				root.Query(className: className).ForEach(e => e.style.color = color);
			}
			else
			{
				root.Query(className: className).ForEach(e => e.style.color = StyleKeyword.Null);
			}
		}

		/// <summary>
		/// Sets the background-image tint on every element carrying a class.
		/// </summary>
		/// <remarks>
		/// Used for the crosshair, which is a sprite rather than text or a filled box, so
		/// <c>color</c> does nothing to it and the tint is what actually recolours the graphic.
		/// </remarks>
		private static void SetTint(VisualElement root, string className, UITKTheme theme, string name)
		{
			if (TryResolve(theme, name, out Color color))
			{
				root.Query(className: className).ForEach(e => e.style.unityBackgroundImageTintColor = color);
			}
			else
			{
				root.Query(className: className).ForEach(e => e.style.unityBackgroundImageTintColor = StyleKeyword.Null);
			}
		}
	}
}
