using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Every synthesised clip, made once on first request and kept for the life of the process.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Made off the main thread.</b> A recipe is pure arithmetic (<see cref="SoundSynth"/>), so it runs on the
	/// thread pool and only the clip is made back on the main thread, when <see cref="Get"/> is next asked. Until
	/// then <see cref="Get"/> returns null and the caller stays silent for those few frames: a rain loop that starts
	/// a quarter of a second late is better than a frame that hitches to make it. A recipe must not call Unity —
	/// on a worker that throws, and the sound would never be heard.
	/// </para>
	/// <para>
	/// <b>WebGL has no threads:</b> there the recipe runs where it is asked for.
	/// </para>
	/// </remarks>
	public static class SoundLibrary
	{
		private sealed class Entry
		{
			public AudioClip Clip;
			public Task<float[]> Job;
			public int Rate;
			public bool Failed;
		}

		private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

		/// <summary>
		/// The clip made by <paramref name="recipe"/> under <paramref name="key"/>, or null while it is being made.
		/// The key names the sound and must change when the recipe does.
		/// </summary>
		public static AudioClip Get(string key, Func<float[]> recipe, int rate = SoundSynth.Rate)
		{
			if (entries.TryGetValue(key, out Entry entry))
			{
				if (entry.Clip != null)
				{
					return entry.Clip;
				}
				if (entry.Failed)
				{
					return null;
				}
				if (entry.Job != null && entry.Job.IsCompleted)
				{
					Finish(key, entry);
				}
				return entry.Clip;
			}

			entry = new Entry { Rate = rate };
			entries[key] = entry;
#if UNITY_WEBGL && !UNITY_EDITOR
			entry.Job = Task.FromResult(Run(key, recipe));
#else
			entry.Job = Task.Run(() => Run(key, recipe));
#endif
			if (entry.Job.IsCompleted)
			{
				Finish(key, entry);
			}
			return entry.Clip;
		}

		/// <summary>Makes the clip now, on this thread, if it is not made yet: for a test or a scene's first frame.</summary>
		public static AudioClip GetNow(string key, Func<float[]> recipe, int rate = SoundSynth.Rate)
		{
			AudioClip clip = Get(key, recipe, rate);
			if (clip != null || !entries.TryGetValue(key, out Entry entry) || entry.Failed)
			{
				return clip;
			}
			entry.Job.Wait();
			Finish(key, entry);
			return entry.Clip;
		}

		private static float[] Run(string key, Func<float[]> recipe)
		{
			try
			{
				return recipe();
			}
			catch (Exception e)
			{
				// Logged from the main thread in Finish; a worker's log line is not always kept.
				throw new InvalidOperationException("Sound '" + key + "' could not be made: " + e.Message, e);
			}
		}

		private static void Finish(string key, Entry entry)
		{
			if (entry.Job.IsFaulted || entry.Job.IsCanceled)
			{
				entry.Failed = true;
				Log.Warning("SoundLibrary", entry.Job.Exception != null ? entry.Job.Exception.GetBaseException().Message : "Sound '" + key + "' was cancelled.");
				entry.Job = null;
				return;
			}
			float[] data = entry.Job.Result;
			entry.Job = null;
			if (data == null || data.Length == 0)
			{
				entry.Failed = true;
				return;
			}
			entry.Clip = SoundSynth.ToClip(key, data, entry.Rate);
		}

		/// <summary>Forgets every clip (a domain reload in the editor leaves them orphaned otherwise).</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnPlay()
		{
			// The clips are kept across scenes (DontSave), so the editor would keep the last play's too.
			foreach (Entry entry in entries.Values)
			{
				if (entry.Clip != null)
				{
					UnityEngine.Object.DestroyImmediate(entry.Clip);
				}
			}
			entries.Clear();
		}
	}
}
