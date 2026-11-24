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
		private static readonly Dictionary<SceneHandle, List<CharacterPositionHistory>> byScene
			= new Dictionary<SceneHandle, List<CharacterPositionHistory>>();

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

			SceneHandle handle = history.gameObject.scene.handle;
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
			foreach (KeyValuePair<SceneHandle, List<CharacterPositionHistory>> pair in byScene)
			{
				pair.Value.Remove(history);
			}
		}

		/// <summary>Number of characters registered in a scene. For diagnostics and tests.</summary>
		public static int RegisteredIn(Scene scene)
			=> byScene.TryGetValue(scene.handle, out List<CharacterPositionHistory> list) ? list.Count : 0;

		#region Recorded grid

		/// <summary>
		/// How far a recorded character may have moved since the last recording pass, metres: the grid is one tick old when a
		/// ship, a region or a bounded rewind reads it, and this covers a tick's travel at any speed a character runs, rides or
		/// flies with room to spare.
		/// </summary>
		public const float RecordedSlack = 4f;

		/// <summary>Each scene's recorded characters, as of the last recording pass.</summary>
		private static readonly Dictionary<SceneHandle, RecordedGrid> gridByScene = new Dictionary<SceneHandle, RecordedGrid>();

		/// <summary>
		/// Where the characters of one scene stood at the last recording pass, bucketed across the ground: a ship asking whether
		/// anyone is near reads a few cells instead of every character's transform (it read thousands, per ship, per tick).
		/// </summary>
		internal sealed class RecordedGrid
		{
			/// <summary>Cell edge, metres: a ship's reach (~100 m) spans a handful, a region or a rewind one to four.</summary>
			public const float CellSize = 32f;

			private readonly Dictionary<long, int> heads = new Dictionary<long, int>();
			private Vector3[] positions = new Vector3[64];
			private CharacterPositionHistory[] histories = new CharacterPositionHistory[64];
			private bool[] players = new bool[64];
			private int[] next = new int[64];

			/// <summary>Characters in the grid.</summary>
			public int Count { get; private set; }

			/// <summary>The recorded character at <paramref name="index"/>.</summary>
			public CharacterPositionHistory HistoryAt(int index) => histories[index];

			/// <summary>Where the character at <paramref name="index"/> was recorded.</summary>
			public Vector3 PositionAt(int index) => positions[index];

			public void Clear()
			{
				if (Count == 0)
				{
					return;
				}
				heads.Clear();
				System.Array.Clear(histories, 0, Count);
				Count = 0;
			}

			public void Add(CharacterPositionHistory history, Vector3 position, bool player)
			{
				if (Count == positions.Length)
				{
					int size = Count * 2;
					System.Array.Resize(ref positions, size);
					System.Array.Resize(ref histories, size);
					System.Array.Resize(ref players, size);
					System.Array.Resize(ref next, size);
				}
				int index = Count++;
				positions[index] = position;
				histories[index] = history;
				players[index] = player;
				long key = Key(Cell(position.x), Cell(position.z));
				next[index] = heads.TryGetValue(key, out int head) ? head : -1;
				heads[key] = index;
			}

			private static int Cell(float value) => Mathf.FloorToInt(value / CellSize);

			private static long Key(int x, int z) => ((long)x << 32) | (uint)z;

			/// <summary>
			/// True when a recorded character (a player, when <paramref name="playersOnly"/>) lies inside the box
			/// [<paramref name="min"/>, <paramref name="max"/>]; with <paramref name="radius"/> above zero, only one within that
			/// distance of <paramref name="center"/> across the ground counts as well.
			/// </summary>
			public bool Any(Vector3 min, Vector3 max, bool playersOnly, Vector3 center, float radius)
			{
				if (Count == 0)
				{
					return false;
				}
				float radiusSquared = radius * radius;
				int x0 = Cell(min.x), x1 = Cell(max.x), z0 = Cell(min.z), z1 = Cell(max.z);
				long cells = (long)(x1 - x0 + 1) * (z1 - z0 + 1);
				if (cells > Count)
				{
					// A query wider than the crowd: the packed list is cheaper than its cells.
					for (int i = 0; i < Count; i++)
					{
						if (Matches(i, min, max, playersOnly, center, radiusSquared))
						{
							return true;
						}
					}
					return false;
				}
				for (int x = x0; x <= x1; x++)
				{
					for (int z = z0; z <= z1; z++)
					{
						if (!heads.TryGetValue(Key(x, z), out int i))
						{
							continue;
						}
						for (; i >= 0; i = next[i])
						{
							if (Matches(i, min, max, playersOnly, center, radiusSquared))
							{
								return true;
							}
						}
					}
				}
				return false;
			}

			private bool Matches(int i, Vector3 min, Vector3 max, bool playersOnly, Vector3 center, float radiusSquared)
			{
				if (playersOnly && !players[i])
				{
					return false;
				}
				Vector3 p = positions[i];
				if (p.x < min.x || p.x > max.x || p.y < min.y || p.y > max.y || p.z < min.z || p.z > max.z)
				{
					return false;
				}
				if (radiusSquared > 0f)
				{
					float dx = p.x - center.x, dz = p.z - center.z;
					return dx * dx + dz * dz <= radiusSquared;
				}
				return true;
			}
		}

		/// <summary>Empties every scene's grid: the start of a recording pass.</summary>
		internal static void BeginRecordingPass()
		{
			foreach (KeyValuePair<SceneHandle, RecordedGrid> pair in gridByScene)
			{
				pair.Value.Clear();
			}
		}

		/// <summary>Puts a character just recorded into its scene's grid.</summary>
		internal static void NoteRecorded(CharacterPositionHistory history, SceneHandle scene, Vector3 position)
		{
			if (!gridByScene.TryGetValue(scene, out RecordedGrid grid))
			{
				grid = new RecordedGrid();
				gridByScene[scene] = grid;
			}
			grid.Add(history, position, history.IsPlayer);
		}

		/// <summary>The scene's grid, or null when nothing has been recorded there.</summary>
		internal static RecordedGrid GridOf(Scene scene)
			=> gridByScene.TryGetValue(scene.handle, out RecordedGrid grid) && grid.Count > 0 ? grid : null;

		/// <summary>
		/// The shared recording pass of one TimeManager: every character a client can aim at (observed, and every player) has its
		/// tick recorded and its place noted in its scene's grid, in one walk on <c>OnPostTick</c>.
		/// </summary>
		internal sealed class Recorder
		{
			private static readonly Dictionary<FishNet.Managing.Timing.TimeManager, Recorder> byTimeManager
				= new Dictionary<FishNet.Managing.Timing.TimeManager, Recorder>();

			/// <summary>Every registered history: walked instead of <see cref="recorded"/> while a synthetic claim aims at anything.</summary>
			private readonly List<CharacterPositionHistory> all = new List<CharacterPositionHistory>();

			/// <summary>The histories a client can aim at: observed characters, and every player.</summary>
			private readonly List<CharacterPositionHistory> recorded = new List<CharacterPositionHistory>();

			public static Recorder For(FishNet.Managing.Timing.TimeManager timeManager)
			{
				if (!byTimeManager.TryGetValue(timeManager, out Recorder recorder))
				{
					// A destroyed TimeManager (a finished play-mode run) leaves its entry behind; drop those first.
					List<FishNet.Managing.Timing.TimeManager> dead = null;
					foreach (FishNet.Managing.Timing.TimeManager key in byTimeManager.Keys)
					{
						if (key == null)
						{
							(dead ??= new List<FishNet.Managing.Timing.TimeManager>()).Add(key);
						}
					}
					if (dead != null)
					{
						foreach (FishNet.Managing.Timing.TimeManager key in dead)
						{
							byTimeManager.Remove(key);
						}
					}
					recorder = new Recorder();
					byTimeManager[timeManager] = recorder;
					timeManager.OnPostTick += recorder.PostTick;
				}
				return recorder;
			}

			public void Add(CharacterPositionHistory history)
			{
				if (history.RecorderAllIndex >= 0)
				{
					return;
				}
				history.RecorderAllIndex = all.Count;
				all.Add(history);
			}

			public void Remove(CharacterPositionHistory history)
			{
				SetRecorded(history, false);
				int index = history.RecorderAllIndex;
				if (index < 0)
				{
					return;
				}
				int last = all.Count - 1;
				CharacterPositionHistory moved = all[last];
				all[index] = moved;
				moved.RecorderAllIndex = index;
				all.RemoveAt(last);
				history.RecorderAllIndex = -1;
			}

			public void SetRecorded(CharacterPositionHistory history, bool recordedNow)
			{
				int index = history.RecorderRecordedIndex;
				if (recordedNow)
				{
					if (index < 0)
					{
						history.RecorderRecordedIndex = recorded.Count;
						recorded.Add(history);
					}
					return;
				}
				if (index < 0)
				{
					return;
				}
				int last = recorded.Count - 1;
				CharacterPositionHistory moved = recorded[last];
				recorded[index] = moved;
				moved.RecorderRecordedIndex = index;
				recorded.RemoveAt(last);
				history.RecorderRecordedIndex = -1;
			}

			private void PostTick()
			{
				BeginRecordingPass();
				List<CharacterPositionHistory> list = LagCompensationTick.ClaimOverride != null ? all : recorded;
				for (int i = 0; i < list.Count; i++)
				{
					CharacterPositionHistory history = list[i];
					if (history == null)
					{
						continue;
					}
					try
					{
						history.RecordTick();
					}
					catch (Exception ex)
					{
						Log.Error("LagCompensationRegistry", $"Recording a character's tick failed. {ex}");
					}
				}
			}
		}

		#endregion

		/// <summary>
		/// True when any recorded character in <paramref name="scene"/> stood within <paramref name="reach"/> metres of
		/// <paramref name="point"/> across the ground (x and z) at the last recording pass: what lets something that only
		/// matters to a character near it (a ship's hull collider, FloatingBoats) stand down where none is. Recorded means a
		/// character a client can see (observed, or a player): one nobody observes moves for nobody, and is met again, as
		/// before, the tick a player comes near enough to see it. The grid is a tick old, which the caller's own reach covers.
		/// </summary>
		public static bool AnyWithin(Scene scene, Vector3 point, float reach)
		{
			RecordedGrid grid = GridOf(scene);
			if (grid == null || !(reach >= 0f))
			{
				return false;
			}
			Vector3 extent = new Vector3(reach, float.PositiveInfinity, reach);
			return grid.Any(point - extent, point + extent, false, point, Mathf.Max(reach, 1e-3f));
		}

		/// <summary>
		/// True when a player stood within <paramref name="bounds"/> grown by <paramref name="margin"/> at the last recording
		/// pass: what lets a region's trigger skip its physics overlap while no player can be inside it.
		/// </summary>
		public static bool AnyPlayerWithin(Scene scene, Bounds bounds, float margin)
		{
			RecordedGrid grid = GridOf(scene);
			if (grid == null)
			{
				return false;
			}
			Vector3 grow = new Vector3(margin, margin, margin);
			return grid.Any(bounds.min - grow, bounds.max + grow, true, default, 0f);
		}

		/// <summary>Clears the registry. For test teardown.</summary>
		internal static void Clear()
		{
			byScene.Clear();
			gridByScene.Clear();
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
			return Rewind(scene, target, exclude, default, float.PositiveInfinity);
		}

		/// <summary>
		/// How far a character can have been from where it stands now at any moment a rewind may reach back to, metres:
		/// the margin a bounded rewind adds round its query so nobody who could have been in it is left out.
		/// </summary>
		public const float RewindReach = 20f;

		/// <summary>
		/// <see cref="Rewind(Scene, RewindTarget, ICharacter)"/>, for a query that only looks within
		/// <paramref name="radius"/> of <paramref name="center"/>: only the characters standing within that radius plus
		/// <see cref="RewindReach"/> are moved.
		/// </summary>
		/// <remarks>
		/// Every step of every player-cast object rewound EVERY character in the scene and synced physics twice, to test a
		/// segment a few decimetres long. A character farther away than it could have travelled in the rewind window
		/// cannot be in the query at either position, so it is left where it stands; and when nobody is near, nothing is
		/// moved and nothing is synced at all.
		/// </remarks>
		public static RewindScope Rewind(Scene scene, RewindTarget target, ICharacter exclude, Vector3 center, float radius)
		{
			if (scopeOpen)
			{
				/* A nested rewind would capture already-displaced positions as the restore target
				 * and strand every character in the past when the inner scope closed. Returning an
				 * inactive scope means the inner query runs against the outer rewind, which is the
				 * only sane interpretation. */
				return default;
			}

			/* Only the characters recorded at the last pass: they are the ones with a live ring (an unobserved NPC's ring is
			 * stale, and rewinding it would stand it where it was when last seen). The grid holds them packed with the place
			 * they were recorded, so a bounded rewind discards the far ones without reading a transform. */
			RecordedGrid grid = GridOf(scene);
			if (grid == null)
			{
				return default;
			}

			Transform excludeTransform = exclude?.Transform;
			rewound.Clear();
			bool bounded = !float.IsInfinity(radius) && !float.IsNaN(radius);
			float reach = bounded ? radius + RewindReach : 0f;
			float reachSqr = reach * reach;
			float recordedReach = reach + RecordedSlack;
			float recordedReachSqr = recordedReach * recordedReach;

			/* A throw part-way through leaves everything displaced SO FAR stranded in the past with no
			 * scope to restore it — the characters simply stay where they were half a second ago, for
			 * good. Putting them back before the exception escapes is the only correct handling; the
			 * caller still sees the throw. */
			try
			{
				for (int i = 0; i < grid.Count; i++)
				{
					if (bounded && (grid.PositionAt(i) - center).sqrMagnitude > recordedReachSqr)
					{
						continue;
					}
					CharacterPositionHistory history = grid.HistoryAt(i);
					if (history == null || history.transform == excludeTransform)
					{
						continue;
					}
					if (bounded && (history.transform.position - center).sqrMagnitude > reachSqr)
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
