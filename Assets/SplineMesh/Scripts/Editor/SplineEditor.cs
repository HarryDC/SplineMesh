using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace SplineMesh {
    static class Extensions
    {
        public static int IndexOf<T>( this IReadOnlyList<T> self, T elementToFind )
        {
            int i = 0;
            foreach( T element in self )
            {
                if( Equals( element, elementToFind ) )
                    return i;
                i++;
            }
            return -1;
        }
    }
    
    [CustomEditor(typeof(Spline))]
    public class SplineEditor : Editor {

        private const int QUAD_SIZE = 12;
        private static Color CURVE_COLOR = new Color(0.8f, 0.8f, 0.8f);
        private static Color CURVE_BUTTON_COLOR = new Color(0.8f, 0.8f, 0.8f);
        private static Color DIRECTION_COLOR = Color.red;
        private static Color DIRECTION_BUTTON_COLOR = Color.red;
        private static Color UP_BUTTON_COLOR = Color.green;

        private static bool showUpVector = false;

        private enum SelectionType {
            Node,
            Direction,
            InverseDirection,
            Up
        }

        private SplineNode? selection;
        private int selectedIndex;
        private SelectionType selectionType;
        private bool mustCreateNewNode = false;

        private SerializedProperty nodesProp
        {
            get
            {
                SerializedProperty prop = serializedObject.GetIterator();
                while (prop.NextVisible(true))
                {
                    if (prop.propertyPath == "_nodes")
                    {
                        return prop;
                    }
                }
                return null;
            }
        }

        private Spline spline { get { return (Spline)serializedObject.targetObject; } }

        private GUIStyle nodeButtonStyle, directionButtonStyle, upButtonStyle;
        
        private static void ListProperties(SerializedObject serializedObject)
        {
            SerializedProperty prop = serializedObject.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                Debug.Log($"Name: {prop.name} Path:{prop.propertyPath} Type:{prop.propertyType}");
                enterChildren = false;
                EditorGUILayout.PropertyField(prop, true);
            }
        }

        private void OnEnable() {
            Texture2D t = new Texture2D(1, 1);
            t.SetPixel(0, 0, CURVE_BUTTON_COLOR);
            t.Apply();
            nodeButtonStyle = new GUIStyle();
            nodeButtonStyle.normal.background = t;
        
            t = new Texture2D(1, 1);
            t.SetPixel(0, 0, DIRECTION_BUTTON_COLOR);
            t.Apply();
            directionButtonStyle = new GUIStyle();
            directionButtonStyle.normal.background = t;
        
            t = new Texture2D(1, 1);
            t.SetPixel(0, 0, UP_BUTTON_COLOR);
            t.Apply();
            upButtonStyle = new GUIStyle();
            upButtonStyle.normal.background = t;
            selection = null;
			 
            // TODO Check undo/redo
            // Undo.undoRedoPerformed -= spline.RefreshCurves;
            // Undo.undoRedoPerformed += spline.RefreshCurves;
        }
        
        SplineNode AddClonedNode(SplineNode node, int index) {
            SplineNode res = new SplineNode(node.Position, node.Direction);
            if (index == spline.Nodes.Count - 1) {
                spline.AddNode(res);
            } else {
                spline.InsertNode(index + 1, res);
            }
            return res;
        }
        

        void OnSceneGUI() {
        // disable game object transform gyzmo
        // if the spline script is active
            if (Selection.activeGameObject == spline.gameObject) {
                if (!spline.enabled) {
                    Tools.current = Tool.Move;
                } else {
                    Tools.current = Tool.None;
                    if (selection == null && spline.Length > 0)
                    {
                        selection = spline.Nodes[0];
                        selectedIndex = 0;
                    }
                        
                }
            }
        
        // draw a bezier curve for each curve in the spline
        foreach (CubicBezierCurve curve in spline.Curves) {
            Handles.DrawBezier(spline.transform.TransformPoint(curve.Node1.Position),
                spline.transform.TransformPoint(curve.Node2.Position),
                spline.transform.TransformPoint(curve.Node1.Direction),
                spline.transform.TransformPoint(2 * curve.Node2.Position - curve.Node2.Direction),
                CURVE_COLOR,
                null,
                3);
        }
        
        if (!spline.enabled)
            return;

        if (selection == null) {
            return;
        }
        
        
        // draw the selection handles
        switch (selectionType) {
            case SelectionType.Node:
                // place a handle on the node and manage position change
                // TODO place the handle depending on user params (local or world)
                float3 newPosition = spline.transform.InverseTransformPoint
                    (Handles.PositionHandle(spline.transform.TransformPoint(selection.Value.Position), spline.transform.rotation));
                if (!newPosition.Equals(selection.Value.Position)) {
                    // position handle has been moved
                    if (mustCreateNewNode) {
                        mustCreateNewNode = false;
                        var node = AddClonedNode(selection.Value, selectedIndex);
                        node.Direction = newPosition - selection.Value.Position;
                        node.Position = newPosition;
                        selection = node;
                    } else {
                        var node = selection.Value;
                        node.Direction += newPosition - selection.Value.Position;
                        node.Position = newPosition;
                        selection = node;
                    }
                }
                break;
            case SelectionType.Direction:
            {
                var result = Handles.PositionHandle(spline.transform.TransformPoint(selection.Value.Direction),
                    Quaternion.identity);
                var node = selection.Value;
                node.Direction = spline.transform.InverseTransformPoint(result);
                selection = node;
                break;
            }
            case SelectionType.InverseDirection:
            {
                var result = Handles.PositionHandle(
                    2 * spline.transform.TransformPoint(selection.Value.Position) -
                    spline.transform.TransformPoint(selection.Value.Direction), Quaternion.identity);
                var node = selection.Value;
                node.Direction = 2 * selection.Value.Position - (float3)spline.transform.InverseTransformPoint(result);
                selection = node;
                break;
            }
            case SelectionType.Up:
            {
                var result = Handles.PositionHandle(
                    spline.transform.TransformPoint(selection.Value.Position + selection.Value.Up),
                    Quaternion.LookRotation(selection.Value.Direction - selection.Value.Position));
                var node = selection.Value;
                node.Up = math.normalize((float3)spline.transform.InverseTransformPoint(result) - selection.Value.Position);
                selection = node;
                break;
            }
        }
        
        
        // draw the handles of all nodes, and manage selection motion
        Handles.BeginGUI();
        var nodeIndex = 0;
        foreach (SplineNode n in spline.Nodes) {
            var dir = spline.transform.TransformPoint(n.Direction);
            var pos = spline.transform.TransformPoint(n.Position);
            var invDir = spline.transform.TransformPoint(2 * n.Position - n.Direction);
            var up = spline.transform.TransformPoint(n.Position + n.Up);
            // first we check if at least one thing is in the camera field of view
            if (!(CameraUtility.IsOnScreen(pos) ||
                CameraUtility.IsOnScreen(dir) ||
                CameraUtility.IsOnScreen(invDir) ||
                (showUpVector && CameraUtility.IsOnScreen(up)))) {
                continue;
            }
        
            Vector3 guiPos = HandleUtility.WorldToGUIPoint(pos);
            if (n.Equals(selection.Value)) {
                Vector3 guiDir = HandleUtility.WorldToGUIPoint(dir);
                Vector3 guiInvDir = HandleUtility.WorldToGUIPoint(invDir);
                Vector3 guiUp = HandleUtility.WorldToGUIPoint(up);
        
                // for the selected node, we also draw a line and place two buttons for directions
                Handles.color = DIRECTION_COLOR;
                Handles.DrawLine(guiDir, guiInvDir);
        
                // draw quads direction and inverse direction if they are not selected
                if (selectionType != SelectionType.Node) {
                    if (Button(guiPos, directionButtonStyle)) {
                        selectionType = SelectionType.Node;
                    }
                }
                if (selectionType != SelectionType.Direction) {
                    if (Button(guiDir, directionButtonStyle)) {
                        selectionType = SelectionType.Direction;
                    }
                }
                if (selectionType != SelectionType.InverseDirection) {
                    if (Button(guiInvDir, directionButtonStyle)) {
                        selectionType = SelectionType.InverseDirection;
                    }
                }
                if (showUpVector) {
                    Handles.color = Color.green;
                    Handles.DrawLine(guiPos, guiUp);
                    if (selectionType != SelectionType.Up) {
                        if (Button(guiUp, upButtonStyle)) {
                            selectionType = SelectionType.Up;
                        }
                    }
                }
            } else {
                if (Button(guiPos, nodeButtonStyle)) {
                    selection = n; 
                    selectedIndex = nodeIndex;
                    selectionType = SelectionType.Node;
                }
            }
            nodeIndex++;
        }
        Handles.EndGUI();
        
        if (GUI.changed)
            spline.UpdateNode(selectedIndex, selection.Value);
            EditorUtility.SetDirty(target);
        }

        bool Button(Vector2 position, GUIStyle style) {
            return GUI.Button(new Rect(position - new Vector2(QUAD_SIZE / 2, QUAD_SIZE / 2), new Vector2(QUAD_SIZE, QUAD_SIZE)), GUIContent.none, style);
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            if (selection == null)
                return;
        
            if(selectedIndex < 0) {
                selection = null;
            }
        
            // add button
            if (selection == null) {
                GUI.enabled = false;
            }
            
            if (GUILayout.Button("Add node after selected")) {
                Undo.RecordObject(spline, "add spline node");
                SplineNode newNode = new SplineNode(selection.Value.Direction, 
                    selection.Value.Direction + selection.Value.Direction - selection.Value.Position);
                if(selectedIndex == spline.Nodes.Count - 1) {
                    spline.AddNode(newNode);
                } else {
                    spline.InsertNode(selectedIndex + 1, newNode);
                }
                selection = newNode;
                serializedObject.Update();
            }
            GUI.enabled = true;
        
            // delete button
            if (selection == null || spline.Nodes.Count <= 2) {
                GUI.enabled = false;
            }
            if (GUILayout.Button("Delete selected node")) {
                Undo.RecordObject(spline, "delete spline node");
                spline.RemoveNode(selectedIndex);
                selection = null;
                selectedIndex = -1;
                serializedObject.Update();
                selectionType = SelectionType.Node;
            }
            GUI.enabled = true;
        
            showUpVector = GUILayout.Toggle(showUpVector, "Show up vector");
            spline.IsLoop = GUILayout.Toggle(spline.IsLoop, "Is loop");
        
            // nodes
            GUI.enabled = false;
            EditorGUILayout.PropertyField(nodesProp);
            GUI.enabled = true;
        
            if (selection != null) {
                SerializedProperty nodeProp = nodesProp.GetArrayElementAtIndex(selectedIndex);
        
                EditorGUILayout.LabelField("Selected node (node " + selectedIndex + ")");
        
                EditorGUI.indentLevel++;
                DrawNodeData(nodeProp, selectedIndex);
                EditorGUI.indentLevel--;
            } else {
                EditorGUILayout.LabelField("No selected node");
            }
            
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawNodeData(SerializedProperty nodeProperty, int selectedIndex) {
            var positionProp = nodeProperty.FindPropertyRelative("_position");
            var directionProp = nodeProperty.FindPropertyRelative("_direction");
            var upProp = nodeProperty.FindPropertyRelative("_up");
            var scaleProp = nodeProperty.FindPropertyRelative("_scale");
            var rollProp = nodeProperty.FindPropertyRelative("_roll");
        
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(positionProp, new GUIContent("Position"));
            EditorGUILayout.PropertyField(directionProp, new GUIContent("Direction"));
            EditorGUILayout.PropertyField(upProp, new GUIContent("Up"));
            EditorGUILayout.PropertyField(scaleProp, new GUIContent("Scale"));
            EditorGUILayout.PropertyField(rollProp, new GUIContent("Roll"));
            
            if (EditorGUI.EndChangeCheck()) {
                var node = new SplineNode
                {
                    Position = (float3)positionProp.boxedValue,
                    Direction = (float3)directionProp.boxedValue,
                    Up = (float3)upProp.boxedValue,
                    Scale = (float2)scaleProp.boxedValue,
                    Roll = rollProp.floatValue
                };
                nodeProperty.serializedObject.ApplyModifiedProperties();
                spline.UpdateNode(selectedIndex, node);
                selection = node;
                serializedObject.Update();
                EditorUtility.SetDirty(target);
            }
        }

        [MenuItem("GameObject/3D Object/Spline")]
        public static void CreateSpline() {
            new GameObject("Spline", typeof(Spline));
        }

        [DrawGizmo(GizmoType.InSelectionHierarchy)]
        static void DisplayUnselected(Spline spline, GizmoType gizmoType) {
            foreach (CubicBezierCurve curve in spline.Curves) {
                Handles.DrawBezier(spline.transform.TransformPoint(curve.Node1.Position),
                    spline.transform.TransformPoint(curve.Node2.Position),
                    spline.transform.TransformPoint(curve.Node1.Direction),
                    spline.transform.TransformPoint(2 * curve.Node2.Position - curve.Node2.Direction),
                    CURVE_COLOR,
                    null,
                    3);
            }
        }
    }
}
