using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// Names the elements behind UI Toolkit's "Layout update failed to stabilize" error, which a web player reports without them.
	/// </summary>
	/// <remarks>
	/// Costs nothing until that error is logged. It then listens to every element's geometry for a few frames, and an element that
	/// changes many times in one frame is one the layout keeps returning to.
	/// </remarks>
	internal sealed class UILayoutLoopProbe : MonoBehaviour
	{
		private const string ErrorText = "failed to stabilize";
		private const int SampleFrames = 20;
		private const int ReportedElements = 12;
		private const int MinimumChangesPerFrame = 3;
		private const float SecondsBetweenReports = 30f;

		private sealed class Entry
		{
			public int Frame = -1;
			public int ThisFrame;
			public int MostInAFrame;
			public Rect OldRect;
			public Rect NewRect;
			public string Document;
		}

		private readonly Dictionary<VisualElement, Entry> entries = new Dictionary<VisualElement, Entry>();
		private EventCallback<GeometryChangedEvent> onGeometryChanged;
		private bool errorSeen;
		private int framesLeft;
		private float nextReportTime;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Create()
		{
			GameObject host = new GameObject(nameof(UILayoutLoopProbe)) { hideFlags = HideFlags.HideAndDontSave };
			DontDestroyOnLoad(host);
			host.AddComponent<UILayoutLoopProbe>();
		}

		private void OnEnable()
		{
			onGeometryChanged = OnGeometryChanged;
			Application.logMessageReceived += OnLogMessage;
		}

		private void OnDisable()
		{
			Application.logMessageReceived -= OnLogMessage;
			StopListening();
		}

		private void OnLogMessage(string condition, string stackTrace, LogType type)
		{
			if ((type == LogType.Error || type == LogType.Warning) && condition != null && condition.Contains(ErrorText))
			{
				errorSeen = true;
			}
		}

		private void Update()
		{
			if (framesLeft > 0)
			{
				if (--framesLeft == 0)
				{
					Report();
					StopListening();
				}
				return;
			}

			if (errorSeen && Time.unscaledTime >= nextReportTime)
			{
				errorSeen = false;
				nextReportTime = Time.unscaledTime + SecondsBetweenReports;
				StartListening();
				framesLeft = SampleFrames;
			}
		}

		private void StartListening()
		{
			foreach (UIDocument document in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
			{
				VisualElement root = document.rootVisualElement;
				if (root == null)
				{
					continue;
				}
				foreach (VisualElement element in root.Query<VisualElement>().ToList())
				{
					if (!entries.ContainsKey(element))
					{
						entries[element] = new Entry { Document = document.gameObject.name };
						element.RegisterCallback(onGeometryChanged);
					}
				}
			}
		}

		private void StopListening()
		{
			foreach (VisualElement element in entries.Keys)
			{
				element.UnregisterCallback(onGeometryChanged);
			}
			entries.Clear();
		}

		private void OnGeometryChanged(GeometryChangedEvent evt)
		{
			if (!(evt.currentTarget is VisualElement element) || !entries.TryGetValue(element, out Entry entry))
			{
				return;
			}

			if (entry.Frame != Time.frameCount)
			{
				entry.Frame = Time.frameCount;
				entry.ThisFrame = 0;
			}
			entry.ThisFrame++;
			if (entry.ThisFrame > entry.MostInAFrame)
			{
				entry.MostInAFrame = entry.ThisFrame;
			}
			entry.OldRect = evt.oldRect;
			entry.NewRect = evt.newRect;
		}

		private void Report()
		{
			var found = new List<KeyValuePair<VisualElement, Entry>>();
			foreach (KeyValuePair<VisualElement, Entry> pair in entries)
			{
				if (pair.Value.MostInAFrame >= MinimumChangesPerFrame)
				{
					found.Add(pair);
				}
			}
			found.Sort((a, b) => b.Value.MostInAFrame.CompareTo(a.Value.MostInAFrame));

			var report = new StringBuilder();
			report.Append("[UILayoutLoop] ").Append(found.Count).Append(" of ").Append(entries.Count)
				.Append(" elements changed geometry repeatedly within a frame.");
			for (int i = 0; i < found.Count && i < ReportedElements; i++)
			{
				Entry entry = found[i].Value;
				report.Append('\n').Append(entry.MostInAFrame).Append("x ").Append(entry.Document).Append(": ").Append(Describe(found[i].Key))
					.Append(' ').Append(entry.OldRect).Append(" -> ").Append(entry.NewRect);
			}
			Debug.LogWarning(report.ToString());
		}

		private static string Describe(VisualElement element)
		{
			var path = new List<string>();
			for (VisualElement at = element; at != null && path.Count < 6; at = at.parent)
			{
				string label = at.name;
				if (string.IsNullOrEmpty(label))
				{
					foreach (string className in at.GetClasses())
					{
						label = "." + className;
						break;
					}
				}
				path.Insert(0, string.IsNullOrEmpty(label) ? at.GetType().Name : label);
			}
			return string.Join("/", path);
		}
	}
}
