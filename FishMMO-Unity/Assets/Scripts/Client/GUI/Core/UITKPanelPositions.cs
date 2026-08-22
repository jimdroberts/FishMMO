using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;

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
	/// Coordinates are <b>panel points</b>, not screen pixels. PanelSettings is authored
	/// ScaleWithScreenSize against a 1200x800 reference matching on width, so a stored X means the
	/// same thing on any monitor; the visible height in points changes with aspect ratio, which is
	/// why <see cref="UITKControl"/> re-clamps a restored position rather than trusting it.
	/// </para>
	/// </remarks>
	public static class UITKPanelPositions
	{
		/// <summary>Prefix for the per-panel configuration keys.</summary>
		private const string KeyPrefix = "UI.Panel.";

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
		/// How long to wait after the last change before rewriting the configuration file.
		/// </summary>
		/// <remarks>
		/// <see cref="Configuration.Save"/> serialises and rewrites the whole file. Dragging four
		/// panels into place is four rewrites without this, and the player arranging their UI is
		/// exactly the moment they are dropping panels in quick succession.
		/// </remarks>
		private const float SaveDebounceSeconds = 1.0f;

		/// <summary>True while a write is owed to disk.</summary>
		private static bool savePending;

		/// <summary>Unscaled time at which the owed write is due.</summary>
		private static float saveDeadline;

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

				if (Configuration.GlobalSettings != null)
				{
					Configuration.GlobalSettings.Set(SnapGridKey, clamped);
					RequestSave();
				}
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
			if (string.IsNullOrEmpty(panelName) || Configuration.GlobalSettings == null)
			{
				return;
			}

			Configuration.GlobalSettings.Set(KeyPrefix + panelName + ".X", position.x);
			Configuration.GlobalSettings.Set(KeyPrefix + panelName + ".Y", position.y);
			RequestSave();
		}

		/// <summary>
		/// Forgets a panel's stored position, returning it to wherever its stylesheet puts it.
		/// </summary>
		/// <param name="panelName">The panel's GameObject name.</param>
		public static void Clear(string panelName)
		{
			if (string.IsNullOrEmpty(panelName) || Configuration.GlobalSettings == null)
			{
				return;
			}

			bool removedX = Configuration.GlobalSettings.Remove(KeyPrefix + panelName + ".X");
			bool removedY = Configuration.GlobalSettings.Remove(KeyPrefix + panelName + ".Y");

			if (removedX || removedY)
			{
				RequestSave();
			}
		}

		/// <summary>
		/// Marks the configuration as owing a write, to be flushed once the player settles.
		/// </summary>
		private static void RequestSave()
		{
			savePending = true;
			saveDeadline = Time.unscaledTime + SaveDebounceSeconds;
		}

		/// <summary>
		/// Flushes an owed write once its quiet period has elapsed.
		/// </summary>
		/// <remarks>
		/// Driven from <see cref="UITKControl"/>'s per-frame hook, which every panel already runs.
		/// The early-out is a single bool read, so the forty-odd panels that share it cost
		/// nothing between drags.
		/// </remarks>
		public static void Pump()
		{
			if (!savePending || Time.unscaledTime < saveDeadline)
			{
				return;
			}

			Flush();
		}

		/// <summary>
		/// Writes the configuration to disk immediately, if anything is owed.
		/// </summary>
		public static void Flush()
		{
			if (!savePending)
			{
				return;
			}
			savePending = false;

			if (Configuration.GlobalSettings == null)
			{
				return;
			}

			/* Not in the editor. Constants.GetWorkingDirectory resolves to the repository root
			 * there rather than to an install directory, and every other settings write in the
			 * client is guarded the same way — see Client.OnDestroy and UITKOptions.FlushConfiguration.
			 * The in-memory Set above still applies, so positions survive hide/show and scene
			 * changes while playing in the editor; only the cross-session part is skipped. */
#if !UNITY_EDITOR && !UNITY_WEBGL
			try
			{
				Configuration.GlobalSettings.Save();
			}
			catch (System.Exception ex)
			{
				Log.Warning("UITKPanelPositions", $"Saving panel positions failed: {ex.Message}");
			}
#endif
		}
	}
}
