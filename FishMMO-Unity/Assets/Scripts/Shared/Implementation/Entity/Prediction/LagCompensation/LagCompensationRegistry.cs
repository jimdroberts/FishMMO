using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server-side registry of <see cref="CharacterPositionHistory"/>, and the scoped rewind that
	/// resolves a hit against where characters were rather than where they are.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>How to use it.</b> Wrap a spatial query in a <see langword="using"/> block. The scope
	/// displaces every eligible character in the scene, keeps them displaced for the body, and puts
	/// them back on dispose — including when the body throws, which is the whole reason it is a
	/// scope rather than a pair of calls.
	/// </para>
	/// <code>
	/// using (LagCompensationRegistry.Rewind(scene, target, caster))
	/// {
	///     hitCount = physicsScene.OverlapSphere(center, radius, hits, mask, QueryTriggerInteraction.UseGlobal);
	/// }
	/// </code>
	/// <para>
	/// <b>Scene-scoped on purpose.</b> Characters are indexed by scene handle because each scene runs
	/// its own <see cref="PhysicsScene"/>. Rewinding every character on the server for a query that
	/// can only hit one scene would be both wasteful and wrong.
	/// </para>
	/// <para>
	/// <b>The caster is never rewound.</b> A shooter aims from where it is now, not where it was, so
	/// displacing it would move the origin of its own query.
	/// </para>
	/// </remarks>
	public static class LagCompensationRegistry
	{
		private static readonly Dictionary<int, List<CharacterPositionHistory>> byScene
			= new Dictionary<int, List<CharacterPositionHistory>>();

		/// <summary>Reused across rewinds so a hit resolution does not allocate.</summary>
		private static readonly List<CharacterPositionHistory> rewound
			= new List<CharacterPositionHistory>(64);

		/// <summary>True while a rewind scope is open. Nested rewinds are refused, not stacked.</summary>
		private static bool scopeOpen;

		/// <summary>Adds a character's history to the registry for its current scene.</summary>
		internal static void Register(CharacterPositionHistory history)
		{
			if (history == null)
			{
				return;
			}

			int handle = history.gameObject.scene.handle;
			if (!byScene.TryGetValue(handle, out List<CharacterPositionHistory> list))
			{
				list = new List<CharacterPositionHistory>();
				byScene[handle] = list;
			}
			if (!list.Contains(history))
			{
				list.Add(history);
			}
		}

		/// <summary>Removes a character's history from every scene bucket it may be in.</summary>
		/// <remarks>
		/// Scans all buckets rather than the character's current scene: a character that moved
		/// between scenes would otherwise be removed from the wrong bucket and leak a destroyed
		/// reference into rewinds forever.
		/// </remarks>
		internal static void Unregister(CharacterPositionHistory history)
		{
			if (history == null)
			{
				return;
			}
			foreach (KeyValuePair<int, List<CharacterPositionHistory>> pair in byScene)
			{
				pair.Value.Remove(history);
			}
		}

		/// <summary>Number of characters registered in a scene. For diagnostics and tests.</summary>
		public static int RegisteredIn(Scene scene)
			=> byScene.TryGetValue(scene.handle, out List<CharacterPositionHistory> list) ? list.Count : 0;

		/// <summary>Clears the registry. For test teardown.</summary>
		internal static void Clear()
		{
			byScene.Clear();
			rewound.Clear();
			scopeOpen = false;
		}

		/// <summary>
		/// Displaces every character in <paramref name="scene"/> to where it was at
		/// <paramref name="tick"/>, until the returned scope is disposed.
		/// </summary>
		/// <param name="scene">Scene whose characters should be rewound.</param>
		/// <param name="tick">Server tick to resolve against.</param>
		/// <param name="exclude">Character to leave in place — normally the caster.</param>
		public static RewindScope Rewind(Scene scene, RewindTarget target, ICharacter exclude = null)
		{
			if (scopeOpen)
			{
				/* A nested rewind would capture already-displaced positions as the restore target
				 * and strand every character in the past when the inner scope closed. Returning an
				 * inactive scope means the inner query runs against the outer rewind, which is the
				 * only sane interpretation. */
				return default;
			}

			if (!byScene.TryGetValue(scene.handle, out List<CharacterPositionHistory> list) || list.Count == 0)
			{
				return default;
			}

			Transform excludeTransform = exclude?.Transform;
			rewound.Clear();

			/* A throw part-way through leaves everything displaced SO FAR stranded in the past with no
			 * scope to restore it — the characters simply stay where they were half a second ago, for
			 * good. Putting them back before the exception escapes is the only correct handling; the
			 * caller still sees the throw. */
			try
			{
				for (int i = 0; i < list.Count; i++)
				{
					CharacterPositionHistory history = list[i];
					if (history == null || history.transform == excludeTransform)
					{
						continue;
					}
					if (history.Rewind(target))
					{
						rewound.Add(history);
					}
				}
			}
			catch
			{
				RestoreAll();
				throw;
			}

			if (rewound.Count == 0)
			{
				return default;
			}

			// Colliders follow transforms only at a sync point. Without this the query runs against
			// the pre-rewind collider positions and the whole scope silently does nothing.
			Physics.SyncTransforms();
			scopeOpen = true;
			return new RewindScope(true);
		}

		/// <summary>Returns every displaced character to its live position.</summary>
		/// <remarks>
		/// <para>
		/// <b>Every character is restored even if one of them cannot be.</b> A single throw part-way
		/// through this loop used to abandon the rest of the list, so the characters after the failure
		/// stayed displaced permanently. Each restore is therefore isolated: one bad entry costs that
		/// one character's position, not everybody's.
		/// </para>
		/// <para>
		/// <b>And the scope is closed whatever happens.</b> Without the <c>finally</c>,
		/// <see cref="scopeOpen"/> stayed true after a throw, every subsequent
		/// <see cref="Rewind(Scene, RewindTarget, ICharacter)"/> took the nested-scope branch and
		/// returned an inactive scope, and from that moment on every hit in the process resolved
		/// against live positions — silently, with no log and no way to recover short of a restart.
		/// A mechanism whose failure mode is "quietly stop working forever" has to be the one that
		/// cannot fail.
		/// </para>
		/// </remarks>
		private static void RestoreAll()
		{
			try
			{
				for (int i = 0; i < rewound.Count; i++)
				{
					try
					{
						rewound[i]?.Restore();
					}
					catch (Exception ex)
					{
						Log.Error("LagCompensationRegistry",
							$"Failed to restore a rewound character; it may be left displaced. {ex}");
					}
				}
			}
			finally
			{
				rewound.Clear();
				Physics.SyncTransforms();
				scopeOpen = false;
			}
		}

		/// <summary>
		/// Holds characters displaced for the duration of a <see langword="using"/> block.
		/// </summary>
		public readonly struct RewindScope : IDisposable
		{
			private readonly bool active;

			internal RewindScope(bool active) => this.active = active;

			/// <summary>True when characters were actually displaced.</summary>
			public bool Active => active;

			/// <summary>Restores every displaced character.</summary>
			public void Dispose()
			{
				if (active)
				{
					RestoreAll();
				}
			}
		}
	}
}
