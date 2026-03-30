#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Visual node-based editor for AIBehaviorTree assets.
	/// Provides a canvas for creating, connecting, and editing behavior tree nodes.
	/// </summary>
	public class BehaviorTreeEditorWindow : EditorWindow
	{
		private AIBehaviorTree tree;
		private Vector2 canvasOffset;
		private Vector2 dragStartPos;
		private bool isDraggingCanvas;
		private int draggingNodeIndex = -1;
		private Vector2 nodeDragOffset;
		private int selectedNodeIndex = -1;

		private bool isConnecting;
		private int connectFromIndex = -1;
		private int connectFromSlot = -1;

		private List<AIBehaviorNode> allNodes = new List<AIBehaviorNode>();
		private Dictionary<int, float> nodeHeightCache = new Dictionary<int, float>();

		private const float NODE_WIDTH = 260f;
		private const float NODE_MIN_HEIGHT = 60f;
		private const float NODE_HEADER_HEIGHT = 24f;
		private const float SLOT_HEIGHT = 20f;
		private const float GRID_SIZE = 20f;
		private const float DOT_SIZE = 12f;

		private static readonly Color NODE_COLOR = new Color(0.22f, 0.22f, 0.22f, 1f);
		private static readonly Color NODE_SELECTED_COLOR = new Color(0.15f, 0.35f, 0.55f, 1f);
		private static readonly Color ROOT_HEADER_COLOR = new Color(0.2f, 0.5f, 0.2f, 1f);
		private static readonly Color COMPOSITE_HEADER_COLOR = new Color(0.4f, 0.3f, 0.15f, 1f);
		private static readonly Color DECORATOR_HEADER_COLOR = new Color(0.3f, 0.2f, 0.4f, 1f);
		private static readonly Color LEAF_HEADER_COLOR = new Color(0.35f, 0.35f, 0.35f, 1f);
		private static readonly Color CONNECTION_COLOR = new Color(0.8f, 0.8f, 0.2f, 0.8f);
		private static readonly Color GRID_COLOR = new Color(0.2f, 0.2f, 0.2f, 0.3f);
		private static readonly Color GRID_MAJOR_COLOR = new Color(0.2f, 0.2f, 0.2f, 0.5f);

		private GUIStyle headerStyle;
		private GUIStyle labelStyle;

		[MenuItem("FishMMO/Behavior Tree Editor")]
		public static void ShowWindow()
		{
			var window = GetWindow<BehaviorTreeEditorWindow>("Behavior Tree Editor");
			window.minSize = new Vector2(800, 500);
		}

		/// <summary>
		/// Opens the editor window for a specific AIBehaviorTree asset.
		/// </summary>
		public static void Open(AIBehaviorTree behaviorTree)
		{
			var window = GetWindow<BehaviorTreeEditorWindow>("Behavior Tree Editor");
			window.minSize = new Vector2(800, 500);
			window.SetTree(behaviorTree);
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
			if (tree != null)
			{
				RebuildNodeList();
			}
			Repaint();
		}

		private void SetTree(AIBehaviorTree behaviorTree)
		{
			tree = behaviorTree;
			selectedNodeIndex = -1;
			isConnecting = false;
			RebuildNodeList();
			Repaint();
		}

		private void RebuildNodeList()
		{
			allNodes.Clear();
			if (tree == null) return;

			CollectNodes(tree.Root);
			AutoLayoutIfNeeded();
		}

		private void CollectNodes(AIBehaviorNode node)
		{
			if (node == null || allNodes.Contains(node)) return;

			allNodes.Add(node);

			if (node is AICompositeNode composite && composite.Children != null)
			{
				for (int i = 0; i < composite.Children.Length; i++)
				{
					CollectNodes(composite.Children[i]);
				}
			}
			else if (node is AIInverter inverter)
			{
				CollectNodes(inverter.Child);
			}
			else if (node is AIRepeater repeater)
			{
				CollectNodes(repeater.Child);
			}
		}

		private void AutoLayoutIfNeeded()
		{
			bool needsLayout = false;
			for (int i = 0; i < allNodes.Count; i++)
			{
				if (allNodes[i].EditorPosition == Vector2.zero && i > 0)
				{
					needsLayout = true;
					break;
				}
			}

			if (needsLayout || (allNodes.Count > 0 && allNodes[0].EditorPosition == Vector2.zero))
			{
				AutoLayout();
			}
		}

		private void AutoLayout()
		{
			if (tree == null || tree.Root == null) return;

			Dictionary<AIBehaviorNode, Vector2> positions = new Dictionary<AIBehaviorNode, Vector2>();
			float[] colX = { 0 };
			LayoutNode(tree.Root, 0, positions, colX);

			foreach (var kvp in positions)
			{
				Undo.RecordObject(kvp.Key, "Auto Layout");
				kvp.Key.EditorPosition = kvp.Value;
				EditorUtility.SetDirty(kvp.Key);
			}
		}

		private float LayoutNode(AIBehaviorNode node, int depth, Dictionary<AIBehaviorNode, Vector2> positions, float[] nextY)
		{
			if (node == null || positions.ContainsKey(node)) return nextY[0];

			float x = depth * (NODE_WIDTH + 80);
			float y = nextY[0];

			List<AIBehaviorNode> children = GetChildren(node);

			if (children.Count > 0)
			{
				float startY = nextY[0];
				for (int i = 0; i < children.Count; i++)
				{
					LayoutNode(children[i], depth + 1, positions, nextY);
					if (i < children.Count - 1)
					{
						nextY[0] += 20;
					}
				}
				float endY = nextY[0];
				y = (startY + endY) * 0.5f;
			}
			else
			{
				nextY[0] += 100;
			}

			positions[node] = new Vector2(x, y);
			return nextY[0];
		}

		private List<AIBehaviorNode> GetChildren(AIBehaviorNode node)
		{
			List<AIBehaviorNode> children = new List<AIBehaviorNode>();

			if (node is AICompositeNode composite && composite.Children != null)
			{
				for (int i = 0; i < composite.Children.Length; i++)
				{
					if (composite.Children[i] != null)
					{
						children.Add(composite.Children[i]);
					}
				}
			}
			else if (node is AIInverter inverter && inverter.Child != null)
			{
				children.Add(inverter.Child);
			}
			else if (node is AIRepeater repeater && repeater.Child != null)
			{
				children.Add(repeater.Child);
			}

			return children;
		}

		private int GetMaxChildSlots(AIBehaviorNode node)
		{
			if (node is AICompositeNode composite)
			{
				return composite.Children != null ? composite.Children.Length + 1 : 1;
			}
			if (node is AIInverter || node is AIRepeater)
			{
				return 1;
			}
			return 0;
		}

		private bool CanHaveChildren(AIBehaviorNode node)
		{
			return node is AICompositeNode || node is AIInverter || node is AIRepeater;
		}

		private void OnGUI()
		{
			DrawToolbar();

			if (tree == null)
			{
				DrawNoTreeMessage();
				return;
			}

			nodeHeightCache.Clear();

			DrawGrid();
			DrawNodes();
			DrawConnections();
			DrawConnectionPreview();
			ProcessEvents(Event.current);

			if (GUI.changed)
			{
				Repaint();
			}
		}

		private void DrawToolbar()
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

			EditorGUI.BeginChangeCheck();
			var newTree = (AIBehaviorTree)EditorGUILayout.ObjectField(
				tree, typeof(AIBehaviorTree), false, GUILayout.Width(250));
			if (EditorGUI.EndChangeCheck() && newTree != tree)
			{
				SetTree(newTree);
			}

			GUILayout.FlexibleSpace();

			if (tree != null)
			{
				if (GUILayout.Button("Auto Layout", EditorStyles.toolbarButton))
				{
					AutoLayout();
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

		private void DrawNoTreeMessage()
		{
			var rect = new Rect(0, 20, position.width, position.height - 20);
			GUI.Box(rect, GUIContent.none);

			GUILayout.BeginArea(rect);
			GUILayout.FlexibleSpace();
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();

			GUILayout.BeginVertical();
			GUILayout.Label("No Behavior Tree selected.", EditorStyles.centeredGreyMiniLabel);
			GUILayout.Space(8);
			GUILayout.Label("Drag an AIBehaviorTree asset here or use the field above.", EditorStyles.centeredGreyMiniLabel);
			GUILayout.EndVertical();

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.FlexibleSpace();
			GUILayout.EndArea();

			HandleDragAndDrop(rect);
		}

		private void HandleDragAndDrop(Rect dropArea)
		{
			Event evt = Event.current;
			if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
			{
				if (!dropArea.Contains(evt.mousePosition)) return;

				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

				if (evt.type == EventType.DragPerform)
				{
					DragAndDrop.AcceptDrag();
					foreach (var obj in DragAndDrop.objectReferences)
					{
						if (obj is AIBehaviorTree bt)
						{
							SetTree(bt);
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
			DrawGridLines(canvasRect, GRID_SIZE, GRID_COLOR);
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
			for (int i = 0; i < allNodes.Count; i++)
			{
				DrawNode(i);
			}
		}

		private void DrawNode(int index)
		{
			var node = allNodes[index];
			if (node == null) return;

			Rect nodeRect = GetNodeRect(index);
			bool isSelected = index == selectedNodeIndex;
			bool isRoot = tree.Root == node;

			// Background
			EditorGUI.DrawRect(nodeRect, isSelected ? NODE_SELECTED_COLOR : NODE_COLOR);

			// Header
			Rect headerRect = new Rect(nodeRect.x, nodeRect.y, nodeRect.width, NODE_HEADER_HEIGHT);
			Color headerColor = GetHeaderColor(node, isRoot);
			EditorGUI.DrawRect(headerRect, headerColor);

			// Title
			string title = GetNodeTitle(node, isRoot);
			GUI.Label(headerRect, title, GetHeaderStyle());

			// Set as root button
			if (!isRoot)
			{
				Rect rootBtn = new Rect(nodeRect.xMax - 22, nodeRect.y + 3, 18, 18);
				if (GUI.Button(rootBtn, "R", EditorStyles.miniButton))
				{
					Undo.RecordObject(tree, "Set Root Node");
					tree.Root = node;
					EditorUtility.SetDirty(tree);
					RebuildNodeList();
				}
			}

			// Inline fields
			float contentX = nodeRect.x + 6;
			float contentW = nodeRect.width - 12;
			float y = nodeRect.y + NODE_HEADER_HEIGHT + 4;

			// Type-specific inline content
			var so = new SerializedObject(node);
			so.Update();

			if (node is AIConditionNode)
			{
				var prop = so.FindProperty("Condition");
				if (prop != null)
				{
					float h = EditorGUI.GetPropertyHeight(prop, true);
					EditorGUI.PropertyField(new Rect(contentX, y, contentW, h), prop, new GUIContent("Condition"), true);
					y += h + 2;
				}
			}
			else if (node is AIStateTransitionNode)
			{
				var prop = so.FindProperty("TargetState");
				if (prop != null)
				{
					float h = EditorGUI.GetPropertyHeight(prop, true);
					EditorGUI.PropertyField(new Rect(contentX, y, contentW, h), prop, new GUIContent("Target State"), true);
					y += h + 2;
				}
			}
			else if (node is AIRepeater)
			{
				var prop = so.FindProperty("RepeatCount");
				if (prop != null)
				{
					float h = EditorGUI.GetPropertyHeight(prop);
					EditorGUI.PropertyField(new Rect(contentX, y, contentW, h), prop, new GUIContent("Repeat Count"));
					y += h + 2;
				}
			}

			// EditorComment
			var commentProp = so.FindProperty("EditorComment");
			if (commentProp != null)
			{
				float h = EditorGUI.GetPropertyHeight(commentProp, true);
				EditorGUI.PropertyField(new Rect(contentX, y, contentW, h), commentProp, new GUIContent("Note"), true);
				y += h + 2;
			}

			so.ApplyModifiedProperties();

			// Child connection output dots
			if (CanHaveChildren(node))
			{
				List<AIBehaviorNode> children = GetChildren(node);

				if (node is AICompositeNode)
				{
					int slotCount = children.Count + 1; // extra slot for adding
					for (int c = 0; c < slotCount; c++)
					{
						bool hasChild = c < children.Count;
						Color dotColor = hasChild ? CONNECTION_COLOR : Color.gray;

						Rect dotRect = new Rect(nodeRect.xMax - 16, y + 2, DOT_SIZE, DOT_SIZE);
						EditorGUI.DrawRect(dotRect, dotColor);

						string slotLabel = hasChild ? children[c].name : "+ child";
						GUI.Label(new Rect(contentX, y + 1, contentW - 22, SLOT_HEIGHT),
							slotLabel, GetLabelStyle());

						HandleDotInteraction(dotRect, index, c, hasChild, children);

						y += SLOT_HEIGHT + 2;
					}
				}
				else
				{
					// Decorator (single child)
					bool hasChild = children.Count > 0;
					Color dotColor = hasChild ? CONNECTION_COLOR : Color.gray;

					Rect dotRect = new Rect(nodeRect.xMax - 16, y + 2, DOT_SIZE, DOT_SIZE);
					EditorGUI.DrawRect(dotRect, dotColor);

					string slotLabel = hasChild ? children[0].name : "+ child";
					GUI.Label(new Rect(contentX, y + 1, contentW - 22, SLOT_HEIGHT),
						slotLabel, GetLabelStyle());

					HandleDotInteraction(dotRect, index, 0, hasChild, children);

					y += SLOT_HEIGHT + 2;
				}
			}

			// Border
			Handles.BeginGUI();
			Handles.DrawSolidRectangleWithOutline(nodeRect, Color.clear,
				isSelected ? new Color(0.4f, 0.7f, 1f, 1f) : new Color(0.4f, 0.4f, 0.4f, 0.5f));
			Handles.EndGUI();
		}

		private void HandleDotInteraction(Rect dotRect, int nodeIndex, int slotIndex, bool hasChild, List<AIBehaviorNode> children)
		{
			if (Event.current.type == EventType.MouseDown && dotRect.Contains(Event.current.mousePosition))
			{
				if (Event.current.button == 0)
				{
					isConnecting = true;
					connectFromIndex = nodeIndex;
					connectFromSlot = slotIndex;
					Event.current.Use();
				}
				else if (Event.current.button == 1 && hasChild)
				{
					// Right-click to disconnect
					DisconnectChild(nodeIndex, slotIndex);
					Event.current.Use();
				}
			}
		}

		private void DrawConnections()
		{
			Handles.BeginGUI();

			for (int i = 0; i < allNodes.Count; i++)
			{
				var node = allNodes[i];
				if (node == null) continue;

				List<AIBehaviorNode> children = GetChildren(node);
				Rect sourceRect = GetNodeRect(i);
				float slotsStartY = GetChildSlotsStartY(i);

				for (int c = 0; c < children.Count; c++)
				{
					int childIndex = allNodes.IndexOf(children[c]);
					if (childIndex < 0) continue;

					Rect targetRect = GetNodeRect(childIndex);

					float sourceY = slotsStartY + c * (SLOT_HEIGHT + 2) + DOT_SIZE * 0.5f + 2;
					Vector2 startPos = new Vector2(sourceRect.xMax - 10, sourceY);
					Vector2 endPos = new Vector2(targetRect.x, targetRect.y + NODE_HEADER_HEIGHT * 0.5f);

					float tangent = Mathf.Max(50, Mathf.Abs(endPos.x - startPos.x) * 0.5f);
					Handles.DrawBezier(startPos, endPos,
						startPos + Vector2.right * tangent, endPos + Vector2.left * tangent,
						CONNECTION_COLOR, null, 2.5f);

					// Arrow
					Vector2 dir = (endPos - (endPos + Vector2.left * tangent)).normalized;
					Handles.color = CONNECTION_COLOR;
					Handles.DrawAAConvexPolygon(
						endPos,
						endPos - dir * 8 + new Vector2(-dir.y, dir.x) * 4,
						endPos - dir * 8 - new Vector2(-dir.y, dir.x) * 4);
				}
			}

			Handles.EndGUI();
		}

		private void DrawConnectionPreview()
		{
			if (!isConnecting) return;

			Rect sourceRect = GetNodeRect(connectFromIndex);
			float slotsStartY = GetChildSlotsStartY(connectFromIndex);
			float sourceY = slotsStartY + connectFromSlot * (SLOT_HEIGHT + 2) + DOT_SIZE * 0.5f + 2;
			Vector2 startPos = new Vector2(sourceRect.xMax - 10, sourceY);
			Vector2 mousePos = Event.current.mousePosition;

			Handles.BeginGUI();
			float tangent = Mathf.Max(50, Mathf.Abs(mousePos.x - startPos.x) * 0.5f);
			Handles.DrawBezier(startPos, mousePos,
				startPos + Vector2.right * tangent, mousePos + Vector2.left * tangent,
				Color.white, null, 2f);
			Handles.EndGUI();

			Repaint();
		}

		private float GetChildSlotsStartY(int nodeIndex)
		{
			var node = allNodes[nodeIndex];
			Rect nodeRect = GetNodeRect(nodeIndex);
			float y = nodeRect.y + NODE_HEADER_HEIGHT + 4;

			// Skip past inline fields to find where slots begin
			if (node is AIConditionNode)
			{
				var so = new SerializedObject(node);
				var prop = so.FindProperty("Condition");
				if (prop != null)
				{
					y += EditorGUI.GetPropertyHeight(prop, true) + 2;
				}
			}
			else if (node is AIStateTransitionNode)
			{
				var so = new SerializedObject(node);
				var prop = so.FindProperty("TargetState");
				if (prop != null)
				{
					y += EditorGUI.GetPropertyHeight(prop, true) + 2;
				}
			}
			else if (node is AIRepeater)
			{
				var so = new SerializedObject(node);
				var prop = so.FindProperty("RepeatCount");
				if (prop != null)
				{
					y += EditorGUI.GetPropertyHeight(prop) + 2;
				}
			}

			// EditorComment
			{
				var so = new SerializedObject(node);
				var prop = so.FindProperty("EditorComment");
				if (prop != null)
				{
					y += EditorGUI.GetPropertyHeight(prop, true) + 2;
				}
			}

			return y;
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
				case EventType.ContextClick:
					ProcessContextMenu(evt);
					break;
			}
		}

		private void ProcessMouseDown(Event evt)
		{
			if (evt.button == 0)
			{
				int clickedNode = GetNodeAtPosition(evt.mousePosition);
				if (clickedNode >= 0)
				{
					selectedNodeIndex = clickedNode;
					Rect headerRect = GetNodeHeaderRect(clickedNode);

					if (headerRect.Contains(evt.mousePosition))
					{
						draggingNodeIndex = clickedNode;
						nodeDragOffset = evt.mousePosition - new Vector2(
							allNodes[clickedNode].EditorPosition.x + canvasOffset.x,
							allNodes[clickedNode].EditorPosition.y + canvasOffset.y + 20);
					}
					evt.Use();
				}
				else
				{
					selectedNodeIndex = -1;
					isDraggingCanvas = true;
					dragStartPos = evt.mousePosition;
					evt.Use();
				}
			}
			else if (evt.button == 2)
			{
				isDraggingCanvas = true;
				dragStartPos = evt.mousePosition;
				evt.Use();
			}
		}

		private void ProcessMouseDrag(Event evt)
		{
			if (draggingNodeIndex >= 0)
			{
				Undo.RecordObject(allNodes[draggingNodeIndex], "Move BT Node");
				allNodes[draggingNodeIndex].EditorPosition = new Vector2(
					evt.mousePosition.x - nodeDragOffset.x - canvasOffset.x,
					evt.mousePosition.y - nodeDragOffset.y - canvasOffset.y - 20);
				EditorUtility.SetDirty(allNodes[draggingNodeIndex]);
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
			if (isConnecting && evt.button == 0)
			{
				int targetNode = GetNodeAtPosition(evt.mousePosition);
				if (targetNode >= 0 && targetNode != connectFromIndex)
				{
					ConnectChild(connectFromIndex, connectFromSlot, targetNode);
				}
				isConnecting = false;
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

				menu.AddItem(new GUIContent("Set as Root"), false, () =>
				{
					Undo.RecordObject(tree, "Set Root Node");
					tree.Root = allNodes[capturedIndex];
					EditorUtility.SetDirty(tree);
					RebuildNodeList();
				});

				menu.AddItem(new GUIContent("Focus in Project"), false, () =>
				{
					Selection.activeObject = allNodes[capturedIndex];
					EditorGUIUtility.PingObject(allNodes[capturedIndex]);
				});

				menu.AddSeparator("");

				menu.AddItem(new GUIContent("Remove from Tree"), false, () =>
				{
					DeleteNode(capturedIndex);
				});
			}
			else
			{
				Vector2 pos = evt.mousePosition - canvasOffset - new Vector2(0, 20);

				menu.AddItem(new GUIContent("Add/Selector"), false, () => CreateAndAddNode<AISelector>(pos));
				menu.AddItem(new GUIContent("Add/Sequence"), false, () => CreateAndAddNode<AISequence>(pos));
				menu.AddSeparator("Add/");
				menu.AddItem(new GUIContent("Add/Inverter"), false, () => CreateAndAddNode<AIInverter>(pos));
				menu.AddItem(new GUIContent("Add/Repeater"), false, () => CreateAndAddNode<AIRepeater>(pos));
				menu.AddSeparator("Add/");
				menu.AddItem(new GUIContent("Add/Condition"), false, () => CreateAndAddNode<AIConditionNode>(pos));
				menu.AddItem(new GUIContent("Add/State Transition"), false, () => CreateAndAddNode<AIStateTransitionNode>(pos));
				menu.AddSeparator("Add/");
				menu.AddItem(new GUIContent("Add/Has Target"), false, () => CreateAndAddNode<AIHasTargetNode>(pos));
				menu.AddItem(new GUIContent("Add/Is Dead"), false, () => CreateAndAddNode<AIIsDeadNode>(pos));
				menu.AddItem(new GUIContent("Add/Group In Combat"), false, () => CreateAndAddNode<AIGroupInCombatNode>(pos));
				menu.AddItem(new GUIContent("Add/Adopt Group Target"), false, () => CreateAndAddNode<AIAdoptGroupTargetNode>(pos));

				menu.AddSeparator("");

				menu.AddItem(new GUIContent("Add Existing Node..."), false, () =>
				{
					ShowAddExistingNodePicker(pos);
				});
			}

			menu.ShowAsContext();
			evt.Use();
		}

		private void CreateAndAddNode<T>(Vector2 position) where T : AIBehaviorNode
		{
			// Create the asset in the same folder as the tree
			string treePath = AssetDatabase.GetAssetPath(tree);
			string dir = System.IO.Path.GetDirectoryName(treePath);

			T node = CreateInstance<T>();
			node.name = $"New {typeof(T).Name}";
			node.EditorPosition = position;

			string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{node.name}.asset");
			AssetDatabase.CreateAsset(node, assetPath);
			AssetDatabase.SaveAssets();

			// If tree has no root, set this as root
			if (tree.Root == null)
			{
				Undo.RecordObject(tree, "Set Root Node");
				tree.Root = node;
				EditorUtility.SetDirty(tree);
			}

			allNodes.Add(node);
			Repaint();
		}

		private void ShowAddExistingNodePicker(Vector2 position)
		{
			int controlID = GUIUtility.GetControlID(FocusType.Passive);
			EditorGUIUtility.ShowObjectPicker<AIBehaviorNode>(null, false, "", controlID);

			// Store position for when picker completes
			EditorApplication.update += PickerUpdate;

			void PickerUpdate()
			{
				if (Event.current != null && Event.current.commandName == "ObjectSelectorClosed")
				{
					EditorApplication.update -= PickerUpdate;
					var picked = EditorGUIUtility.GetObjectPickerObject() as AIBehaviorNode;
					if (picked != null && !allNodes.Contains(picked))
					{
						picked.EditorPosition = position;
						EditorUtility.SetDirty(picked);
						allNodes.Add(picked);
						Repaint();
					}
				}
				// Also check for updates to repaint
				if (EditorGUIUtility.GetObjectPickerObject() != null)
				{
					// Picker still open
				}
			}
		}

		private void ConnectChild(int parentIndex, int slotIndex, int childIndex)
		{
			var parent = allNodes[parentIndex];
			var child = allNodes[childIndex];

			if (parent is AICompositeNode composite)
			{
				Undo.RecordObject(parent, "Connect BT Node");

				AIBehaviorNode[] oldChildren = composite.Children ?? new AIBehaviorNode[0];

				if (slotIndex < oldChildren.Length)
				{
					// Replace existing slot
					oldChildren[slotIndex] = child;
					composite.Children = oldChildren;
				}
				else
				{
					// Add new child
					var newChildren = new AIBehaviorNode[oldChildren.Length + 1];
					System.Array.Copy(oldChildren, newChildren, oldChildren.Length);
					newChildren[newChildren.Length - 1] = child;
					composite.Children = newChildren;
				}

				EditorUtility.SetDirty(parent);
			}
			else if (parent is AIInverter inverter)
			{
				Undo.RecordObject(parent, "Connect BT Node");
				inverter.Child = child;
				EditorUtility.SetDirty(parent);
			}
			else if (parent is AIRepeater repeater)
			{
				Undo.RecordObject(parent, "Connect BT Node");
				repeater.Child = child;
				EditorUtility.SetDirty(parent);
			}

			if (!allNodes.Contains(child))
			{
				allNodes.Add(child);
			}

			RebuildNodeList();
		}

		private void DisconnectChild(int parentIndex, int slotIndex)
		{
			var parent = allNodes[parentIndex];

			if (parent is AICompositeNode composite && composite.Children != null)
			{
				if (slotIndex < composite.Children.Length)
				{
					Undo.RecordObject(parent, "Disconnect BT Node");
					var list = new List<AIBehaviorNode>(composite.Children);
					list.RemoveAt(slotIndex);
					composite.Children = list.ToArray();
					EditorUtility.SetDirty(parent);
				}
			}
			else if (parent is AIInverter inverter)
			{
				Undo.RecordObject(parent, "Disconnect BT Node");
				inverter.Child = null;
				EditorUtility.SetDirty(parent);
			}
			else if (parent is AIRepeater repeater)
			{
				Undo.RecordObject(parent, "Disconnect BT Node");
				repeater.Child = null;
				EditorUtility.SetDirty(parent);
			}

			RebuildNodeList();
		}

		private void DeleteNode(int index)
		{
			if (index < 0 || index >= allNodes.Count) return;

			var node = allNodes[index];

			// Remove as child from any parent
			for (int i = 0; i < allNodes.Count; i++)
			{
				var parent = allNodes[i];
				if (parent == node) continue;

				if (parent is AICompositeNode composite && composite.Children != null)
				{
					bool changed = false;
					var list = new List<AIBehaviorNode>(composite.Children);
					for (int c = list.Count - 1; c >= 0; c--)
					{
						if (list[c] == node)
						{
							list.RemoveAt(c);
							changed = true;
						}
					}
					if (changed)
					{
						Undo.RecordObject(parent, "Remove BT Node Reference");
						composite.Children = list.ToArray();
						EditorUtility.SetDirty(parent);
					}
				}
				else if (parent is AIInverter inverter && inverter.Child == node)
				{
					Undo.RecordObject(parent, "Remove BT Node Reference");
					inverter.Child = null;
					EditorUtility.SetDirty(parent);
				}
				else if (parent is AIRepeater repeater && repeater.Child == node)
				{
					Undo.RecordObject(parent, "Remove BT Node Reference");
					repeater.Child = null;
					EditorUtility.SetDirty(parent);
				}
			}

			// Clear root if this was root
			if (tree.Root == node)
			{
				Undo.RecordObject(tree, "Clear Root");
				tree.Root = null;
				EditorUtility.SetDirty(tree);
			}

			if (selectedNodeIndex == index)
			{
				selectedNodeIndex = -1;
			}

			RebuildNodeList();
		}

		private void CenterView()
		{
			if (allNodes.Count == 0)
			{
				canvasOffset = Vector2.zero;
				return;
			}

			Vector2 center = Vector2.zero;
			for (int i = 0; i < allNodes.Count; i++)
			{
				if (allNodes[i] != null)
				{
					center += allNodes[i].EditorPosition;
				}
			}
			center /= allNodes.Count;

			canvasOffset = new Vector2(position.width * 0.5f, position.height * 0.5f) - center - new Vector2(NODE_WIDTH * 0.5f, 50);
		}

		private Rect GetNodeRect(int index)
		{
			var node = allNodes[index];
			float height = CalculateNodeHeight(index);
			return new Rect(
				node.EditorPosition.x + canvasOffset.x,
				node.EditorPosition.y + canvasOffset.y + 20,
				NODE_WIDTH,
				height);
		}

		private Rect GetNodeHeaderRect(int index)
		{
			var node = allNodes[index];
			return new Rect(
				node.EditorPosition.x + canvasOffset.x,
				node.EditorPosition.y + canvasOffset.y + 20,
				NODE_WIDTH,
				NODE_HEADER_HEIGHT);
		}

		private float CalculateNodeHeight(int index)
		{
			if (nodeHeightCache.TryGetValue(index, out float cached))
			{
				return cached;
			}

			var node = allNodes[index];
			float height = NODE_HEADER_HEIGHT + 4;

			var so = new SerializedObject(node);

			// Type-specific fields
			if (node is AIConditionNode)
			{
				var prop = so.FindProperty("Condition");
				if (prop != null) height += EditorGUI.GetPropertyHeight(prop, true) + 2;
			}
			else if (node is AIStateTransitionNode)
			{
				var prop = so.FindProperty("TargetState");
				if (prop != null) height += EditorGUI.GetPropertyHeight(prop, true) + 2;
			}
			else if (node is AIRepeater)
			{
				var prop = so.FindProperty("RepeatCount");
				if (prop != null) height += EditorGUI.GetPropertyHeight(prop) + 2;
			}

			// EditorComment
			var commentProp = so.FindProperty("EditorComment");
			if (commentProp != null) height += EditorGUI.GetPropertyHeight(commentProp, true) + 2;

			// Child slots
			if (CanHaveChildren(node))
			{
				List<AIBehaviorNode> children = GetChildren(node);

				if (node is AICompositeNode)
				{
					int slotCount = children.Count + 1;
					height += slotCount * (SLOT_HEIGHT + 2);
				}
				else
				{
					height += SLOT_HEIGHT + 2;
				}
			}

			height += 4; // bottom padding

			float result = Mathf.Max(NODE_MIN_HEIGHT, height);
			nodeHeightCache[index] = result;
			return result;
		}

		private int GetNodeAtPosition(Vector2 pos)
		{
			for (int i = allNodes.Count - 1; i >= 0; i--)
			{
				if (allNodes[i] != null && GetNodeRect(i).Contains(pos))
				{
					return i;
				}
			}
			return -1;
		}

		private Color GetHeaderColor(AIBehaviorNode node, bool isRoot)
		{
			if (isRoot) return ROOT_HEADER_COLOR;
			if (node is AICompositeNode) return COMPOSITE_HEADER_COLOR;
			if (node is AIInverter || node is AIRepeater) return DECORATOR_HEADER_COLOR;
			return LEAF_HEADER_COLOR;
		}

		private string GetNodeTitle(AIBehaviorNode node, bool isRoot)
		{
			string typeName = node.GetType().Name;

			// Clean up type names for display
			if (typeName.StartsWith("AI")) typeName = typeName.Substring(2);

			string prefix = isRoot ? "\u25B6 " : "";
			return $"{prefix}{typeName}: {node.name}";
		}

		// ───────── Styles ─────────

		private GUIStyle GetHeaderStyle()
		{
			if (headerStyle == null)
			{
				headerStyle = new GUIStyle(EditorStyles.boldLabel)
				{
					alignment = TextAnchor.MiddleLeft,
					fontSize = 11,
					padding = new RectOffset(6, 24, 0, 0),
					normal = { textColor = Color.white }
				};
			}
			return headerStyle;
		}

		private GUIStyle GetLabelStyle()
		{
			if (labelStyle == null)
			{
				labelStyle = new GUIStyle(EditorStyles.miniLabel)
				{
					normal = { textColor = new Color(0.8f, 0.8f, 0.8f, 1f) },
					fontSize = 10
				};
			}
			return labelStyle;
		}
	}

	/// <summary>
	/// Custom inspector for AIBehaviorTree that adds a button to open the visual editor.
	/// </summary>
	[CustomEditor(typeof(AIBehaviorTree))]
	public class AIBehaviorTreeEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			if (GUILayout.Button("Open Behavior Tree Editor", GUILayout.Height(30)))
			{
				BehaviorTreeEditorWindow.Open((AIBehaviorTree)target);
			}

			EditorGUILayout.Space();
			DrawDefaultInspector();
		}
	}
}
#endif
