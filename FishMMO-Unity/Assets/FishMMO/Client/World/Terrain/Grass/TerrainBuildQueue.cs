using System;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Client
{
	/// <summary>
	/// A terrain take-on builder's arithmetic off the main thread, one piece after another in the order it was queued
	/// (<see cref="GrassTerrain.Builder"/>, <see cref="DetailScatterTerrain.Builder"/>): the main thread keeps the
	/// TerrainData reads, a strip at a time, and hands each strip here. Plain managed code on a worker thread runs the very
	/// same float operations in the very same order as it did on the main thread, so the maps come out bit for bit as they
	/// did; only where the time is spent moved. Free of Unity, so the tests run it.
	/// </summary>
	/// <remarks>
	/// The work must touch nothing of Unity's but the thread-safe (<c>Mathf</c>'s managed helpers and its thread-safe
	/// FloatToHalf): arrays, the builder's own fields. A piece that throws stops the queue: the rest is skipped and
	/// <see cref="Fault"/> holds the exception, for the builder to give up and start again. Where there are no threads
	/// (a WebGL player) every piece runs at once, on the caller's thread, exactly as before.
	/// </remarks>
	public sealed class TerrainBuildQueue
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		/// <summary>No worker threads in this player: queued work runs inline.</summary>
		public static readonly bool Inline = true;
#else
		/// <summary>Tests and diagnostics: true runs queued work inline, on the caller's thread.</summary>
		public static bool Inline;
#endif

		private Task tail = Task.CompletedTask;
		private int pending;
		private volatile bool cancelled;
		private Exception fault;

		/// <summary>Pieces queued and not finished.</summary>
		public int Pending => Volatile.Read(ref pending);

		/// <summary>True once every piece queued so far has run (or been skipped).</summary>
		public bool Idle => Pending == 0;

		/// <summary>The first exception a piece threw (the rest of the queue is then skipped), or null.</summary>
		public Exception Fault => Volatile.Read(ref fault);

		/// <summary>Queues <paramref name="work"/> after everything queued before it.</summary>
		public void Enqueue(Action work)
		{
			Interlocked.Increment(ref pending);
			if (Inline)
			{
				Run(work);
				return;
			}
			tail = tail.ContinueWith(_ => Run(work), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
		}

		/// <summary>Skips everything still queued (an abandoned build): nothing more is written into its arrays.</summary>
		public void Cancel()
		{
			cancelled = true;
		}

		private void Run(Action work)
		{
			try
			{
				if (!cancelled && Volatile.Read(ref fault) == null)
				{
					work();
				}
			}
			catch (Exception e)
			{
				Interlocked.CompareExchange(ref fault, e, null);
			}
			finally
			{
				Interlocked.Decrement(ref pending);
			}
		}

		/// <summary>Blocks until every piece queued so far has run (tests; the builders poll <see cref="Idle"/>).</summary>
		public void Drain()
		{
			tail.Wait();
		}
	}
}
