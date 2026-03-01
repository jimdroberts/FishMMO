using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Visual node-based editor for DialogueTemplate assets.
	/// Provides a canvas for creating, connecting, and editing dialogue nodes with full ECA support.
	/// </summary>
	public class DialogueTreeEditorWindow : EditorWindow
	{
		private DialogueTemplate template;
		private SerializedObject serializedTemplate;
		private Vector2 scrollPosition;
		private Vector2 canvasOffset;
		private Vector2 dragStartPos;
		private bool isDraggingCanvas;
		private int draggingNodeIndex = -1;
		private Vector2 nodeDragOffset;
		private ConnectionState connectionState;
		private int selectedNodeIndex = -1;

		private const float NODE_WIDTH = 220f;
		private const float NODE_MIN_HEIGHT = 80f;
		private const float NODE_HEADER_HEIGHT = 24f;
		private const float CHOICE_HEIGHT = 20f;
		private const float GRID_SIZE = 20f;
		private const float CONNECTION_DOT_RADIUS = 6f;

		private static readonly Color NODE_COLOR = new Color(0.22f, 0.22f, 0.22f, 1f);
		private static readonly Color NODE_SELECTED_COLOR = new Color(0.15f, 0.35f, 0.55f, 1f);
		private static readonly Color NODE_HEADER_COLOR = new Color(0.35f, 0.35f, 0.35f, 1f);
		private static readonly Color START_NODE_HEADER_COLOR = new Color(0.2f, 0.5f, 0.2f, 1f);
		private static readonly Color CONNECTION_COLOR = new Color(0.8f, 0.8f, 0.2f, 0.8f);
		private static readonly Color GRID_COLOR = new Color(0.2f, 0.2f, 0.2f, 0.3f);
		private static readonly Color GRID_MAJOR_COLOR = new Color(0.2f, 0.2f, 0.2f, 0.5f);

		private struct ConnectionState
		{
			public bool IsConnecting;
			public int FromNodeIndex;
			public int FromChoiceIndex;
		}

		[MenuItem("FishMMO/Dialogue Tree Editor")]
		public static void ShowWindow()
		{
			var window = GetWindow<DialogueTreeEditorWindow>("Dialogue Tree Editor");
			window.minSize = new Vector2(800, 500);
		}

		/// <summary>
		/// Opens the editor window for a specific DialogueTemplate asset.
		/// </summary>
		public static void Open(DialogueTemplate dialogueTemplate)
		{
			var window = GetWindow<DialogueTreeEditorWindow>("Dialogue Tree Editor");
			window.minSize = new Vector2(800, 500);
			window.SetTemplate(dialogueTemplate);
		}

		private void OnEnable()
		{
			Undo.undoRedoPerformed += OnUndoRedo;
		}

		private void OnDisable()
		{
			Undo.undoRedoPerformed -= OnUndoRedo;
		}

		private void OnUndoRedo()
		{
			if (template != null)
			{
				serializedTemplate = new SerializedObject(template);
			}
			Repaint();
		}

		private void SetTemplate(DialogueTemplate dialogueTemplate)
		{
			template = dialogueTemplate;
			serializedTemplate = template != null ? new SerializedObject(template) : null;
			selectedNodeIndex = -1;
			connectionState = default;
			Repaint();
		}

		private void OnGUI()
		{
			DrawToolbar();

			if (template == null)
			{
				DrawNoTemplateMessage();
				return;
			}

			serializedTemplate.Update();

			ProcessEvents(Event.current);
			DrawGrid();
			DrawConnections();
			DrawNodes();
			DrawConnectionPreview();

			if (GUI.changed)
			{
				Repaint();
			}
		}

		private void DrawToolbar()
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

			// Template field
			EditorGUI.BeginChangeCheck();
			var newTemplate = (DialogueTemplate)EditorGUILayout.ObjectField(
				template, typeof(DialogueTemplate), false, GUILayout.Width(250));
			if (EditorGUI.EndChangeCheck() && newTemplate != template)
			{
				SetTemplate(newTemplate);
			}

			GUILayout.FlexibleSpace();

			if (template != null)
			{
				if (GUILayout.Button("Add Node", EditorStyles.toolbarButton))
				{
					AddNode();
				}

				if (GUILayout.Button("Center View", EditorStyles.toolbarButton))
				{
					CenterView();
				}

				if (selectedNodeIndex >= 0 && GUILayout.Button("Delete Selected", EditorStyles.toolbarButton))
				{
					DeleteNode(selectedNodeIndex);
				}
			}

			EditorGUILayout.EndHorizontal();
		}

		private void DrawNoTemplateMessage()
		{
			var rect = new Rect(0, 20, position.width, position.height - 20);
			GUI.Box(rect, GUIContent.none);

			GUILayout.BeginArea(rect);
			GUILayout.FlexibleSpace();
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();

			GUILayout.BeginVertical();
			GUILayout.Label("No Dialogue Template selected.", EditorStyles.centeredGreyMiniLabel);
			GUILayout.Space(8);
			GUILayout.Label("Drag a DialogueTemplate asset here or use the field above.", EditorStyles.centeredGreyMiniLabel);
			GUILayout.EndVertical();

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.FlexibleSpace();
			GUILayout.EndArea();

			// Handle drag-and-drop onto the window
			HandleDragAndDrop(rect);
		}

		private void HandleDragAndDrop(Rect dropArea)
		{
			Event evt = Event.current;
			if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
			{
				if (!dropArea.Contains(evt.mousePosition))
				{
					return;
				}

				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

				if (evt.type == EventType.DragPerform)
				{
					DragAndDrop.AcceptDrag();
					foreach (var obj in DragAndDrop.objectReferences)
					{
						if (obj is DialogueTemplate dt)
						{
							SetTemplate(dt);
							break;
						}
					}
				}
				evt.Use();
			}
		}

		private void DrawGrid()
		{
			Rect canvasRect = new Rect(0, 20, position.width, position.height - 20);

			// Minor grid
			DrawGridLines(canvasRect, GRID_SIZE, GRID_COLOR);
			// Major grid
			DrawGridLines(canvasRect, GRID_SIZE * 5, GRID_MAJOR_COLOR);
		}

		private void DrawGridLines(Rect rect, float spacing, Color color)
		{
			Handles.BeginGUI();
			Handles.color = color;

			float ox = canvasOffset.x % spacing;
			float oy = canvasOffset.y % spacing;

			for (float x = rect.x + ox; x < rect.x + rect.width; x += spacing)
			{
				Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.y + rect.height));
			}
			for (float y = rect.y + oy; y < rect.y + rect.height; y += spacing)
			{
				Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.x + rect.width, y));
			}

			Handles.EndGUI();
		}

		private void DrawNodes()
		{
			if (template.Nodes == null)
			{
				return;
			}

			for (int i = 0; i < template.Nodes.Count; i++)
			{
				DrawNode(i);
			}
		}

		private void DrawNode(int index)
		{
			var node = template.Nodes[index];
			if (node == null)
			{
				return;
			}

			Rect nodeRect = GetNodeRect(index);
			bool isSelected = index == selectedNodeIndex;
			bool isStartNode = node.NodeId == template.StartNodeId;

			// Node background
			EditorGUI.DrawRect(nodeRect, isSelected ? NODE_SELECTED_COLOR : NODE_COLOR);

			// Node header
			Rect headerRect = new Rect(nodeRect.x, nodeRect.y, nodeRect.width, NODE_HEADER_HEIGHT);
			EditorGUI.DrawRect(headerRect, isStartNode ? START_NODE_HEADER_COLOR : NODE_HEADER_COLOR);

			// Node title
			string title = isStartNode ? $"\u25B6 Node {node.NodeId}" : $"Node {node.NodeId}";
			GUI.Label(headerRect, title, GetNodeTitleStyle());

			// Start node button
			if (!isStartNode)
			{
				Rect startBtnRect = new Rect(nodeRect.xMax - 22, nodeRect.y + 3, 18, 18);
				if (GUI.Button(startBtnRect, "\u25B6", EditorStyles.miniButton))
				{
					Undo.RecordObject(template, "Set Start Node");
					template.StartNodeId = node.NodeId;
					EditorUtility.SetDirty(template);
				}
			}

			// Node text preview
			float yOffset = nodeRect.y + NODE_HEADER_HEIGHT + 4;
			Rect textRect = new Rect(nodeRect.x + 6, yOffset, nodeRect.width - 12, 36);
			string preview = string.IsNullOrEmpty(node.Text) ? "(empty)" : node.Text;
			if (preview.Length > 60)
			{
				preview = preview.Substring(0, 57) + "...";
			}
			GUI.Label(textRect, preview, GetNodeTextStyle());

			yOffset += 38;

			// ECA indicators
			Rect ecaRect = new Rect(nodeRect.x + 6, yOffset, nodeRect.width - 12, 16);
			int condCount = node.Conditions != null ? node.Conditions.Count : 0;
			int enterCount = node.OnEnterActions != null ? node.OnEnterActions.Count : 0;
			int exitCount = node.OnExitActions != null ? node.OnExitActions.Count : 0;
			if (condCount > 0 || enterCount > 0 || exitCount > 0)
			{
				string ecaInfo = "";
				if (condCount > 0) ecaInfo += $"C:{condCount} ";
				if (enterCount > 0) ecaInfo += $"A\u2193:{enterCount} ";
				if (exitCount > 0) ecaInfo += $"A\u2191:{exitCount}";
				GUI.Label(ecaRect, ecaInfo.Trim(), GetECAInfoStyle());
			}
			yOffset += 18;

			// Choices
			if (node.Choices != null)
			{
				for (int c = 0; c < node.Choices.Count; c++)
				{
					var choice = node.Choices[c];
					if (choice == null) continue;

					Rect choiceRect = new Rect(nodeRect.x + 6, yOffset, nodeRect.width - 32, CHOICE_HEIGHT);
					string choiceText = string.IsNullOrEmpty(choice.Text) ? "(no text)" : choice.Text;
					if (choiceText.Length > 25)
					{
						choiceText = choiceText.Substring(0, 22) + "...";
					}
					GUI.Label(choiceRect, $"  \u2192 {choiceText}", GetChoiceStyle());

					// Connection dot
					Rect dotRect = new Rect(nodeRect.xMax - 14, yOffset + 4, 12, 12);
					Color dotColor = choice.NextNodeId >= 0 ? CONNECTION_COLOR : Color.gray;
					EditorGUI.DrawRect(dotRect, dotColor);

					if (Event.current.type == EventType.MouseDown && dotRect.Contains(Event.current.mousePosition))
					{
						if (Event.current.button == 0)
						{
							connectionState = new ConnectionState
							{
								IsConnecting = true,
								FromNodeIndex = index,
								FromChoiceIndex = c
							};
							Event.current.Use();
						}
						else if (Event.current.button == 1)
						{
							// Right-click to disconnect
							Undo.RecordObject(template, "Disconnect Choice");
							choice.NextNodeId = -1;
							EditorUtility.SetDirty(template);
							Event.current.Use();
						}
					}

					yOffset += CHOICE_HEIGHT + 2;
				}
			}

			// Add choice button
			Rect addChoiceRect = new Rect(nodeRect.x + 6, yOffset, nodeRect.width - 12, 18);
			if (GUI.Button(addChoiceRect, "+ Add Choice", EditorStyles.miniButton))
			{
				Undo.RecordObject(template, "Add Dialogue Choice");
				if (node.Choices == null)
				{
					node.Choices = new List<DialogueChoice>();
				}
				node.Choices.Add(new DialogueChoice { Text = "New Choice", NextNodeId = -1 });
				EditorUtility.SetDirty(template);
			}

			// Node border
			Handles.BeginGUI();
			Handles.color = isSelected ? new Color(0.4f, 0.7f, 1f, 1f) : new Color(0.4f, 0.4f, 0.4f, 1f);
			Handles.DrawSolidRectangleWithOutline(nodeRect, Color.clear,
				isSelected ? new Color(0.4f, 0.7f, 1f, 1f) : new Color(0.4f, 0.4f, 0.4f, 0.5f));
			Handles.EndGUI();
		}

		private void DrawConnections()
		{
			if (template.Nodes == null)
			{
				return;
			}

			Handles.BeginGUI();

			for (int i = 0; i < template.Nodes.Count; i++)
			{
				var node = template.Nodes[i];
				if (node?.Choices == null) continue;

				for (int c = 0; c < node.Choices.Count; c++)
				{
					var choice = node.Choices[c];
					if (choice == null || choice.NextNodeId < 0) continue;

					int targetIndex = FindNodeIndex(choice.NextNodeId);
					if (targetIndex < 0) continue;

					Rect sourceRect = GetNodeRect(i);
					Rect targetRect = GetNodeRect(targetIndex);

					float sourceY = sourceRect.y + NODE_HEADER_HEIGHT + 60 + c * (CHOICE_HEIGHT + 2) + CHOICE_HEIGHT * 0.5f;
					Vector2 startPos = new Vector2(sourceRect.xMax - 8, sourceY);
					Vector2 endPos = new Vector2(targetRect.x, targetRect.y + NODE_HEADER_HEIGHT * 0.5f);

					float tangent = Mathf.Max(50, Mathf.Abs(endPos.x - startPos.x) * 0.5f);
					Vector2 startTangent = startPos + Vector2.right * tangent;
					Vector2 endTangent = endPos + Vector2.left * tangent;

					Handles.DrawBezier(startPos, endPos, startTangent, endTangent, CONNECTION_COLOR, null, 2.5f);

					// Arrow at the end
					Vector2 dir = (endPos - endTangent).normalized;
					Vector2 arrowLeft = endPos - dir * 8 + new Vector2(-dir.y, dir.x) * 4;
					Vector2 arrowRight = endPos - dir * 8 - new Vector2(-dir.y, dir.x) * 4;
					Handles.color = CONNECTION_COLOR;
					Handles.DrawAAConvexPolygon(endPos, arrowLeft, arrowRight);
				}
			}

			Handles.EndGUI();
		}

		private void DrawConnectionPreview()
		{
			if (!connectionState.IsConnecting)
			{
				return;
			}

			Rect sourceRect = GetNodeRect(connectionState.FromNodeIndex);
			float sourceY = sourceRect.y + NODE_HEADER_HEIGHT + 60 +
				connectionState.FromChoiceIndex * (CHOICE_HEIGHT + 2) + CHOICE_HEIGHT * 0.5f;
			Vector2 startPos = new Vector2(sourceRect.xMax - 8, sourceY);
			Vector2 mousePos = Event.current.mousePosition;

			Handles.BeginGUI();
			float tangent = Mathf.Max(50, Mathf.Abs(mousePos.x - startPos.x) * 0.5f);
			Handles.DrawBezier(startPos, mousePos,
				startPos + Vector2.right * tangent, mousePos + Vector2.left * tangent,
				Color.white, null, 2f);
			Handles.EndGUI();

			Repaint();
		}

		private void ProcessEvents(Event evt)
		{
			switch (evt.type)
			{
				case EventType.MouseDown:
					ProcessMouseDown(evt);
					break;
				case EventType.MouseDrag:
					ProcessMouseDrag(evt);
					break;
				case EventType.MouseUp:
					ProcessMouseUp(evt);
					break;
				case EventType.ScrollWheel:
					break;
				case EventType.ContextClick:
					ProcessContextMenu(evt);
					break;
			}
		}

		private void ProcessMouseDown(Event evt)
		{
			if (evt.button == 0)
			{
				// Check if clicking on a node
				int clickedNode = GetNodeAtPosition(evt.mousePosition);
				if (clickedNode >= 0)
				{
					selectedNodeIndex = clickedNode;
					Rect headerRect = GetNodeHeaderRect(clickedNode);

					if (headerRect.Contains(evt.mousePosition))
					{
						draggingNodeIndex = clickedNode;
						nodeDragOffset = evt.mousePosition - new Vector2(
							template.Nodes[clickedNode].EditorPosition.x + canvasOffset.x,
							template.Nodes[clickedNode].EditorPosition.y + canvasOffset.y + 20);
					}

					evt.Use();
				}
				else
				{
					// Start canvas drag
					selectedNodeIndex = -1;
					isDraggingCanvas = true;
					dragStartPos = evt.mousePosition;
					evt.Use();
				}
			}
			else if (evt.button == 2)
			{
				// Middle mouse canvas drag
				isDraggingCanvas = true;
				dragStartPos = evt.mousePosition;
				evt.Use();
			}
		}

		private void ProcessMouseDrag(Event evt)
		{
			if (draggingNodeIndex >= 0)
			{
				Undo.RecordObject(template, "Move Dialogue Node");
				template.Nodes[draggingNodeIndex].EditorPosition = new Vector2(
					evt.mousePosition.x - nodeDragOffset.x - canvasOffset.x,
					evt.mousePosition.y - nodeDragOffset.y - canvasOffset.y - 20);
				EditorUtility.SetDirty(template);
				evt.Use();
			}
			else if (isDraggingCanvas)
			{
				canvasOffset += evt.mousePosition - dragStartPos;
				dragStartPos = evt.mousePosition;
				evt.Use();
			}
		}

		private void ProcessMouseUp(Event evt)
		{
			if (connectionState.IsConnecting && evt.button == 0)
			{
				int targetNode = GetNodeAtPosition(evt.mousePosition);
				if (targetNode >= 0 && targetNode != connectionState.FromNodeIndex)
				{
					Undo.RecordObject(template, "Connect Dialogue Choice");
					var choice = template.Nodes[connectionState.FromNodeIndex].Choices[connectionState.FromChoiceIndex];
					choice.NextNodeId = template.Nodes[targetNode].NodeId;
					EditorUtility.SetDirty(template);
				}
				connectionState = default;
				evt.Use();
			}

			draggingNodeIndex = -1;
			isDraggingCanvas = false;
		}

		private void ProcessContextMenu(Event evt)
		{
			int clickedNode = GetNodeAtPosition(evt.mousePosition);

			GenericMenu menu = new GenericMenu();

			if (clickedNode >= 0)
			{
				int capturedIndex = clickedNode;
				menu.AddItem(new GUIContent("Set as Start Node"), false, () =>
				{
					Undo.RecordObject(template, "Set Start Node");
					template.StartNodeId = template.Nodes[capturedIndex].NodeId;
					EditorUtility.SetDirty(template);
				});

				menu.AddItem(new GUIContent("Inspect in Inspector"), false, () =>
				{
					selectedNodeIndex = capturedIndex;
					Selection.activeObject = template;
					EditorGUIUtility.PingObject(template);
				});

				menu.AddSeparator("");

				menu.AddItem(new GUIContent("Delete Node"), false, () => DeleteNode(capturedIndex));
			}
			else
			{
				Vector2 pos = evt.mousePosition - canvasOffset - new Vector2(0, 20);
				menu.AddItem(new GUIContent("Add Node Here"), false, () => AddNodeAtPosition(pos));
			}

			menu.ShowAsContext();
			evt.Use();
		}

		private void AddNode()
		{
			Undo.RecordObject(template, "Add Dialogue Node");

			if (template.Nodes == null)
			{
				template.Nodes = new List<DialogueNode>();
			}

			int newId = template.GenerateNodeId();
			var node = new DialogueNode
			{
				NodeId = newId,
				Text = "New dialogue node",
				EditorPosition = -canvasOffset + new Vector2(
					position.width * 0.5f - NODE_WIDTH * 0.5f,
					position.height * 0.5f - 50)
			};

			template.Nodes.Add(node);

			if (template.Nodes.Count == 1)
			{
				template.StartNodeId = newId;
			}

			EditorUtility.SetDirty(template);
			serializedTemplate.Update();
		}

		private void AddNodeAtPosition(Vector2 pos)
		{
			Undo.RecordObject(template, "Add Dialogue Node");

			if (template.Nodes == null)
			{
				template.Nodes = new List<DialogueNode>();
			}

			int newId = template.GenerateNodeId();
			var node = new DialogueNode
			{
				NodeId = newId,
				Text = "New dialogue node",
				EditorPosition = pos
			};

			template.Nodes.Add(node);

			if (template.Nodes.Count == 1)
			{
				template.StartNodeId = newId;
			}

			EditorUtility.SetDirty(template);
			serializedTemplate.Update();
		}

		private void DeleteNode(int index)
		{
			if (template.Nodes == null || index < 0 || index >= template.Nodes.Count)
			{
				return;
			}

			Undo.RecordObject(template, "Delete Dialogue Node");

			int deletedId = template.Nodes[index].NodeId;

			// Remove connections pointing to this node
			for (int i = 0; i < template.Nodes.Count; i++)
			{
				if (template.Nodes[i]?.Choices == null) continue;
				foreach (var choice in template.Nodes[i].Choices)
				{
					if (choice != null && choice.NextNodeId == deletedId)
					{
						choice.NextNodeId = -1;
					}
				}
			}

			template.Nodes.RemoveAt(index);

			// Update start node if needed
			if (template.StartNodeId == deletedId && template.Nodes.Count > 0)
			{
				template.StartNodeId = template.Nodes[0].NodeId;
			}

			if (selectedNodeIndex == index)
			{
				selectedNodeIndex = -1;
			}
			else if (selectedNodeIndex > index)
			{
				selectedNodeIndex--;
			}

			EditorUtility.SetDirty(template);
			serializedTemplate.Update();
		}

		private void CenterView()
		{
			if (template.Nodes == null || template.Nodes.Count == 0)
			{
				canvasOffset = Vector2.zero;
				return;
			}

			Vector2 center = Vector2.zero;
			for (int i = 0; i < template.Nodes.Count; i++)
			{
				if (template.Nodes[i] != null)
				{
					center += template.Nodes[i].EditorPosition;
				}
			}
			center /= template.Nodes.Count;

			canvasOffset = new Vector2(position.width * 0.5f, position.height * 0.5f) - center - new Vector2(NODE_WIDTH * 0.5f, 50);
		}

		private Rect GetNodeRect(int index)
		{
			var node = template.Nodes[index];
			float height = CalculateNodeHeight(node);
			return new Rect(
				node.EditorPosition.x + canvasOffset.x,
				node.EditorPosition.y + canvasOffset.y + 20,
				NODE_WIDTH,
				height);
		}

		private Rect GetNodeHeaderRect(int index)
		{
			var node = template.Nodes[index];
			return new Rect(
				node.EditorPosition.x + canvasOffset.x,
				node.EditorPosition.y + canvasOffset.y + 20,
				NODE_WIDTH,
				NODE_HEADER_HEIGHT);
		}

		private float CalculateNodeHeight(DialogueNode node)
		{
			float height = NODE_HEADER_HEIGHT + 4 + 38 + 18; // header + text preview + ECA info
			int choiceCount = node.Choices != null ? node.Choices.Count : 0;
			height += choiceCount * (CHOICE_HEIGHT + 2);
			height += 24; // add choice button
			return Mathf.Max(NODE_MIN_HEIGHT, height);
		}

		private int GetNodeAtPosition(Vector2 pos)
		{
			if (template.Nodes == null) return -1;

			// Iterate in reverse so topmost (last drawn) nodes are selected first
			for (int i = template.Nodes.Count - 1; i >= 0; i--)
			{
				if (GetNodeRect(i).Contains(pos))
				{
					return i;
				}
			}
			return -1;
		}

		private int FindNodeIndex(int nodeId)
		{
			if (template.Nodes == null) return -1;
			for (int i = 0; i < template.Nodes.Count; i++)
			{
				if (template.Nodes[i] != null && template.Nodes[i].NodeId == nodeId)
				{
					return i;
				}
			}
			return -1;
		}

		// ───────── Styles ─────────

		private GUIStyle nodeTitle;
		private GUIStyle nodeText;
		private GUIStyle choiceStyle;
		private GUIStyle ecaInfo;

		private GUIStyle GetNodeTitleStyle()
		{
			if (nodeTitle == null)
			{
				nodeTitle = new GUIStyle(EditorStyles.boldLabel)
				{
					alignment = TextAnchor.MiddleLeft,
					fontSize = 11,
					padding = new RectOffset(6, 6, 0, 0),
					normal = { textColor = Color.white }
				};
			}
			return nodeTitle;
		}

		private GUIStyle GetNodeTextStyle()
		{
			if (nodeText == null)
			{
				nodeText = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
				{
					normal = { textColor = new Color(0.8f, 0.8f, 0.8f, 1f) },
					fontSize = 10,
					wordWrap = true
				};
			}
			return nodeText;
		}

		private GUIStyle GetChoiceStyle()
		{
			if (choiceStyle == null)
			{
				choiceStyle = new GUIStyle(EditorStyles.miniLabel)
				{
					normal = { textColor = new Color(0.9f, 0.9f, 0.6f, 1f) },
					fontSize = 10
				};
			}
			return choiceStyle;
		}

		private GUIStyle GetECAInfoStyle()
		{
			if (ecaInfo == null)
			{
				ecaInfo = new GUIStyle(EditorStyles.miniLabel)
				{
					normal = { textColor = new Color(0.5f, 0.8f, 1f, 0.8f) },
					fontSize = 9,
					fontStyle = FontStyle.Italic
				};
			}
			return ecaInfo;
		}
	}

	/// <summary>
	/// Custom inspector for DialogueTemplate that adds a button to open the visual editor.
	/// </summary>
	[CustomEditor(typeof(DialogueTemplate))]
	public class DialogueTemplateEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			if (GUILayout.Button("Open Dialogue Tree Editor", GUILayout.Height(30)))
			{
				DialogueTreeEditorWindow.Open((DialogueTemplate)target);
			}

			EditorGUILayout.Space();
			DrawDefaultInspector();
		}
	}
}