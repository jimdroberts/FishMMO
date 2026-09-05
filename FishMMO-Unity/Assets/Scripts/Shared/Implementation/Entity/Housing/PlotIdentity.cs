using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// What identifies a plot of land: the scene it belongs to, and a key authored on it.
	/// </summary>
	/// <remarks>
	/// A plot is a foundation area placed in a scene by a designer, so its geometry is part of the
	/// scene asset and never needs to be stored or synchronised. Only ownership is persisted, which
	/// leaves the question of what a stored row points at.
	///
	/// <para>It cannot be a scene object identifier. Those are handed out fresh on every scene load
	/// and are explicitly never persisted, so a row keyed by one would attach to a different
	/// foundation after a restart. It cannot be a scene <em>instance</em> either: channels are
	/// several live copies of the same scene, and a plot is meant to look the same in all of them.
	/// What is left is the scene's name plus a key the designer writes on the foundation, which is
	/// stable across reloads, identical on every scene server in the cluster, and readable in a log
	/// line.</para>
	///
	/// <para>Keys are canonicalised on the way in rather than stored as typed. Guild names take the
	/// opposite approach — stored as entered, with a lowercase computed column beside them — because
	/// a guild name is shown to players and has to survive round-tripping intact. A plot key is a
	/// developer-facing identifier that is never displayed, so there is no reason to keep two forms
	/// of it, and one canonical form is what stops <c>Riverside_01</c> and <c>riverside_01</c> from
	/// becoming two plots that look like one.</para>
	/// </remarks>
	public readonly struct PlotIdentity : IEquatable<PlotIdentity>
	{
		/// <summary>
		/// Longest accepted plot key. Matches the column width.
		/// </summary>
		public const int MaxPlotKeyLength = 64;

		/// <summary>
		/// Longest accepted scene name. Matches the width every other scene-name column uses.
		/// </summary>
		public const int MaxSceneNameLength = 100;

		/// <summary>
		/// The Unity scene the plot's foundation is authored in.
		/// </summary>
		public string SceneName { get; }

		/// <summary>
		/// The designer-authored key, canonicalised. Unique within <see cref="SceneName"/>.
		/// </summary>
		public string PlotKey { get; }

		private PlotIdentity(string sceneName, string plotKey)
		{
			SceneName = sceneName;
			PlotKey = plotKey;
		}

		/// <summary>
		/// True when this identity names a plot rather than being an empty default.
		/// </summary>
		public bool IsValid => !string.IsNullOrEmpty(SceneName) && !string.IsNullOrEmpty(PlotKey);

		/// <summary>
		/// Builds an identity from an authored scene name and plot key.
		/// </summary>
		/// <param name="sceneName">The scene the foundation lives in.</param>
		/// <param name="plotKey">The key authored on the foundation.</param>
		/// <param name="identity">The canonicalised identity, or the default on failure.</param>
		/// <returns>False when either part is missing, blank, or too long for its column.</returns>
		/// <remarks>
		/// Length is checked here, against the same limits the columns use, so an over-long key is
		/// caught while a designer still has the scene open rather than as a truncation or an insert
		/// failure much later.
		/// </remarks>
		public static bool TryCreate(string sceneName, string plotKey, out PlotIdentity identity)
		{
			identity = default;

			if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(plotKey))
			{
				return false;
			}

			string scene = sceneName.Trim();
			string key = Normalize(plotKey);

			if (scene.Length > MaxSceneNameLength || key.Length > MaxPlotKeyLength)
			{
				return false;
			}

			identity = new PlotIdentity(scene, key);
			return true;
		}

		/// <summary>
		/// Reduces an authored plot key to its canonical form.
		/// </summary>
		/// <remarks>
		/// Lower-cased with the invariant culture rather than the current one. A server running
		/// under a Turkish locale would otherwise fold a dotted capital I to a dotless lowercase
		/// one, and produce a different key for the same foundation than the rest of the cluster.
		/// </remarks>
		public static string Normalize(string plotKey)
		{
			return plotKey == null ? string.Empty : plotKey.Trim().ToLowerInvariant();
		}

		/// <inheritdoc />
		public bool Equals(PlotIdentity other)
		{
			return string.Equals(SceneName, other.SceneName, StringComparison.Ordinal) &&
				   string.Equals(PlotKey, other.PlotKey, StringComparison.Ordinal);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return obj is PlotIdentity other && Equals(other);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			int sceneHash = SceneName == null ? 0 : SceneName.GetHashCode();
			int keyHash = PlotKey == null ? 0 : PlotKey.GetHashCode();
			return (sceneHash * 397) ^ keyHash;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return IsValid ? $"{SceneName}/{PlotKey}" : "<invalid plot>";
		}

		public static bool operator ==(PlotIdentity left, PlotIdentity right) => left.Equals(right);

		public static bool operator !=(PlotIdentity left, PlotIdentity right) => !left.Equals(right);
	}
}
