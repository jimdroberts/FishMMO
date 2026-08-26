using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Stores where the player has dragged each panel, and the grid those drags snap to.
	/// </summary>
	/// <remarks>
	/// Panel positions have to survive two very different lifetimes, and the shorter one is the
	/// reason this exists at all. Hiding a panel disables its <see cref="UnityEngine.UIElements.UIDocument"/>,
	/// and re-enabling it clones the UXML afresh — so the inline <c>left</c>/<c>top</c> a drag
	/// wrote are discarded the first time the panel is closed. Without somewhere outside the
	/// visual tree to keep them, "move the inventory window" only lasted until the player pressed
	/// the key that closed it.
	/// <para>
	/// The longer lifetime is the session. Values live in <see cref="Configuration.GlobalSettings"/>
	/// under <c>UI.Panel.&lt;name&gt;.X</c> / <c>.Y</c>, beside every other client setting, so a
	/// player's arrangement is still there next launch.
	/// </para>
	/// <para>
	/// Reads happen here; writes go through <see cref="ClientSettings"/>. This class used to own a
	/// debounce timer of its own, which meant two independent timers rewriting the same whole file
	/// on their own schedules — see <see cref="ClientSettings"/> for what that cost. There is one
	/// pending write in the client and one place that flushes it.
	/// </para>
	/// <para>
	/// Coordinates are <b>panel points</b>, not screen pixels. PanelSettings is authored
	/// ScaleWithScreenSize against a 1200x800 reference matching on width, so a stored X means the
	/// same thing on any monitor; the visible height in points changes with aspect ratio, which is
	/// why <see cref="UITKControl"/> re-clamps a restored position rather than trusting it.
	/// </para>
	/// </remarks>
	public static class UITKPanelPositions
	{
		/// <summary>Prefix for the per-panel configuration keys.</summary>
		/// <remarks>
		/// Public because <see cref="UIProfile"/> copies these keys wholesale into and out of a
		/// shareable profile and has to agree with this class about which keys those are. A second
		/// copy of the literal is one rename away from a profile that saves nothing and a load that
		/// wipes every window position.
		/// </remarks>
		public const string KeyPrefix = "UI.Panel.";

		/// <summary>Configuration key holding the drag snap grid, in panel points.</summary>
		public const string SnapGridKey = "UI.SnapGridSize";

		/// <summary>
		/// Grid used when the player has never chosen one.
		/// </summary>
		/// <remarks>
		/// Small enough to feel like free movement and large enough that two panels dragged
		/// roughly level actually end up level. Zero disables snapping entirely.
		/// </remarks>
		public const float DefaultSnapGridSize = 8.0f;

		/// <summary>Largest grid the options slider offers, in panel points.</summary>
		public const float MaxSnapGridSize = 32.0f;

		/// <summary>
		/// Cached snap grid, so a drag does not parse a configuration string per pointer move.
		/// </summary>
		/// <remarks>NaN means "not read yet"; <see cref="InvalidateSnapGrid"/> puts it back.</remarks>
		private static float snapGridSize = float.NaN;

		/// <summary>
		/// The drag snap grid in panel points. Zero disables snapping.
		/// </summary>
		public static float SnapGridSize
		{
			get
			{
				if (float.IsNaN(snapGridSize))
				{
					float value = DefaultSnapGridSize;
					if (Configuration.GlobalSettings != null)
					{
						Configuration.GlobalSettings.TryGetFloat(SnapGridKey, out value, DefaultSnapGridSize);
					}
					/* Clamped on read, not only on write. This is a text file a player can edit and
					 * a crash can truncate; a negative grid makes Snap divide into the wrong
					 * quadrant and a huge one makes every panel unreachable in the corner. */
					snapGridSize = Mathf.Clamp(value, 0.0f, MaxSnapGridSize);
				}
				return snapGridSize;
			}
			set
			{
				float clamped = Mathf.Clamp(value, 0.0f, MaxSnapGridSize);
				snapGridSize = clamped;

				ClientSettings.Set(SnapGridKey, clamped);
			}
		}

		/// <summary>
		/// Drops the cached snap grid so the next read comes from the configuration again.
		/// </summary>
		/// <remarks>
		/// Needed because the configuration can be replaced wholesale after this has been read —
		/// <c>UITKOptions.EnsureConfigurationLoaded</c> creates and loads it lazily, and a panel
		/// dragged before the options screen was ever opened would otherwise keep snapping to the
		/// compiled-in default for the rest of the session.
		/// </remarks>
		public static void InvalidateSnapGrid()
		{
			snapGridSize = float.NaN;
		}

		/// <summary>
		/// Rounds a position onto the snap grid.
		/// </summary>
		/// <param name="position">Desired top-left, in panel points.</param>
		/// <returns>The position rounded to the nearest grid intersection, or unchanged when
		/// snapping is off.</returns>
		public static Vector2 Snap(Vector2 position)
		{
			float grid = SnapGridSize;
			if (grid <= 0.0f)
			{
				return position;
			}

			return new Vector2(
				Mathf.Round(position.x / grid) * grid,
				Mathf.Round(position.y / grid) * grid);
		}

		/// <summary>
		/// Reads the stored position for a panel.
		/// </summary>
		/// <param name="panelName">The panel's GameObject name, as registered with <see cref="UIManager"/>.</param>
		/// <param name="position">The stored top-left, in panel points.</param>
		/// <returns>True when the player has moved this panel before.</returns>
		public static bool TryLoad(string panelName, out Vector2 position)
		{
			position = Vector2.zero;

			if (string.IsNullOrEmpty(panelName) || Configuration.GlobalSettings == null)
			{
				return false;
			}

			/* Both halves or neither. A file holding only X — a truncated write, or a hand edit —
			 * would otherwise restore the panel to the top of the screen, which for a panel the
			 * player had moved to the bottom reads as the position having been forgotten wrongly
			 * rather than not at all. */
			if (!Configuration.GlobalSettings.TryGetFloat(KeyPrefix + panelName + ".X", out float x) ||
				!Configuration.GlobalSettings.TryGetFloat(KeyPrefix + panelName + ".Y", out float y))
			{
				return false;
			}

			if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
			{
				return false;
			}

			position = new Vector2(x, y);
			return true;
		}

		/// <summary>
		/// Records where the player left a panel.
		/// </summary>
		/// <param name="panelName">The panel's GameObject name.</param>
		/// <param name="position">Top-left, in panel points.</param>
		public static void Store(string panelName, Vector2 position)
		{
			if (string.IsNullOrEmpty(panelName))
			{
				return;
			}

			/* Through ClientSettings rather than into the store directly, so a dragged panel and a
			 * changed setting share one pending write instead of racing two of them over the same
			 * file. It is also what applies the invariant float formatting: a position written with
			 * the machine's own locale came back multiplied by a hundred on any comma-decimal
			 * system, which put every window in the bottom-right corner. */
			ClientSettings.Set(KeyPrefix + panelName + ".X", position.x);
			ClientSettings.Set(KeyPrefix + panelName + ".Y", position.y);
		}

		/// <summary>
		/// Forgets a panel's stored position, returning it to wherever its stylesheet puts it.
		/// </summary>
		/// <param name="panelName">The panel's GameObject name.</param>
		public static void Clear(string panelName)
		{
			if (string.IsNullOrEmpty(panelName))
			{
				return;
			}

			/* ClientSettings.Remove schedules a write only when a key actually went away. That
			 * matters here: "reset every window" walks all forty-odd panels, and most of them have
			 * nothing stored — scheduling a write for each would push the debounce deadline out by
			 * its full interval forty times over, delaying the write the reset actually owes. */
			ClientSettings.Remove(KeyPrefix + panelName + ".X");
			ClientSettings.Remove(KeyPrefix + panelName + ".Y");
		}
	}
}
