// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace QuickSave.Editor
{
    [CustomEditor(typeof(QuickSaveArchetypeCollection))]
    public class QuickSaveArchetypeCollectionInspector : UnityEditor.Editor
    {
        private ReorderableList _reorderableList;
        private QuickSaveArchetypeCollection _archetypeCollection;
        private QuickSaveSettingsAsset _quickSaveSettingsAsset;
        List<string> _cachedFullTypeNames;
        [SerializeField]
        private List<bool> _foldouts = new List<bool>();

        public static readonly string[] InvalidNames = new string[3] {"None", "No Preset", "Custom Combination"};
        private string _newEntryName = "";
        
        private void OnEnable()
        {
            _archetypeCollection = (QuickSaveArchetypeCollection) target;
            _quickSaveSettingsAsset = QuickSaveSettingsAsset.Get();
            _cachedFullTypeNames = _quickSaveSettingsAsset.AllQuickSaveTypeInfos.Select(info => info.FullTypeName).ToList();
            _cachedFullTypeNames.Add("");

            ResizeFoldoutList();

            _reorderableList = new ReorderableList(_archetypeCollection.Definitions, typeof(QuickSaveArchetypeCollection.QuickSaveArchetypeDefinition));
            _reorderableList.drawElementCallback = DrawElementCallback;
            _reorderableList.drawHeaderCallback = DrawHeaderCallback;
            _reorderableList.onAddCallback = OnAddCallback;
            _reorderableList.onRemoveCallback = OnRemoveCallback;
            _reorderableList.elementHeightCallback = ElementHeightCallback;
            _reorderableList.drawNoneElementCallback = DrawNoneCallback;
        }

        private float ElementHeightCallback(int index)
        {
            float height = EditorGUIUtility.singleLineHeight + 2;
            if (_foldouts[index])
                height *= _archetypeCollection.Definitions[index].FullTypeNames.Count + 2;
            return height + 4;
        }

        public override void OnInspectorGUI()
        {
            GUI.enabled = !EditorApplication.isPlayingOrWillChangePlaymode;
            
            _reorderableList.DoLayoutList();
            
            if (EditorUtility.IsDirty(_archetypeCollection) && _reorderableList.list.Count > 0)
            {
                EditorGUILayout.HelpBox("Save for the changes to go into effect! (Ctrl+S)", MessageType.Info);
            }
        }

        private void DrawElementCallback(Rect rect, int index, bool isactive, bool isfocused)
        {
            var entry = _archetypeCollection.Definitions[index];
            rect.min += new Vector2(16, 1);
            rect.max -= new Vector2(16, 3);
            rect.height = EditorGUIUtility.singleLineHeight;
            // Name
            _foldouts[index] = EditorGUI.Foldout(rect, _foldouts[index], entry.Name);
            if (!_foldouts[index])
                return;
            
            // Type List
            for (int i = 0; i < entry.FullTypeNames.Count; i++)
            {
                rect.y += EditorGUIUtility.singleLineHeight + 2;
                int selectedIndex = _cachedFullTypeNames.FindIndex(s => s == entry.FullTypeNames[i]);
                
                var popupRect = new Rect(rect);
                popupRect.xMax -= EditorGUIUtility.singleLineHeight + 2;
                int newSelectedIndex = EditorGUI.Popup(popupRect, selectedIndex,
                    _cachedFullTypeNames.Select((s =>
                        string.IsNullOrEmpty(s) ? "None" : _quickSaveSettingsAsset.GetPrettyNameInEditor(s))).ToArray());

                if (GUI.Button(new Rect(popupRect.xMax + 2, popupRect.y, EditorGUIUtility.singleLineHeight, EditorGUIUtility.singleLineHeight), "-"))
                {
                    entry.FullTypeNames.RemoveAt(i);
                    EditorUtility.SetDirty(_archetypeCollection);
                }

                if (newSelectedIndex != selectedIndex)
                {
                    entry.FullTypeNames[i] = _cachedFullTypeNames[newSelectedIndex];
                    entry.FullTypeNames = entry.FullTypeNames.ToList();
                    entry.FullTypeNames.Sort();
                    EditorUtility.SetDirty(_archetypeCollection);
                }
            }
            
            // Add
            rect.y += EditorGUIUtility.singleLineHeight + 2;
            rect.xMin = rect.center.x;
            bool enabledBefore = GUI.enabled;
            GUI.enabled = !entry.FullTypeNames.Contains("");
            string buttonText = GUI.enabled ? "Add Type" : "Add Type (\"None\" Value exists)";
            if (GUI.Button(rect, buttonText))
            {
                entry.FullTypeNames.Add("");
                EditorUtility.SetDirty(_archetypeCollection);
            }
            GUI.enabled = enabledBefore;
        }
        
        private void DrawHeaderCallback(Rect rect)
        {
            if (_reorderableList.list.Count > 0)
                EditorGUI.LabelField(rect, "Presets");
            else
                EditorGUI.LabelField(rect, "Presets (Adding)");
        }
        
        private void OnRemoveCallback(ReorderableList list)
        {
            list.list.RemoveAt(list.index);
            ResizeFoldoutList();
            EditorUtility.SetDirty(_archetypeCollection);
        }

        private void OnAddCallback(ReorderableList list)
        {
            list.list = new List<QuickSaveArchetypeCollection.QuickSaveArchetypeDefinition>();
        }
        
        private void DrawNoneCallback(Rect rect)
        {
            bool enabledBefore = GUI.enabled;

            var drawRect = new Rect(rect);
            drawRect.xMin = rect.xMin;
            drawRect.xMax = rect.center.x;

            _newEntryName = EditorGUI.TextField(drawRect, _newEntryName);
            
            if (!IsNameValid(_newEntryName, out string invalidReason, out bool showInfoBox))
            {
                if (showInfoBox)
                    EditorGUILayout.HelpBox(invalidReason, MessageType.Warning);
                GUI.enabled = false;
            }
            
            drawRect.xMin = rect.center.x + 2;
            drawRect.xMax = rect.center.x + Mathf.Abs(rect.width / 4f);
            if (GUI.Button(drawRect, "Add"))
            {
                _archetypeCollection.Definitions.Add(new QuickSaveArchetypeCollection.QuickSaveArchetypeDefinition() { Name = _newEntryName});
                _reorderableList.list = _archetypeCollection.Definitions;
                ResizeFoldoutList();
                _foldouts[_reorderableList.count - 1] = true;
                EditorUtility.SetDirty(_archetypeCollection);
            }
            
            drawRect.xMin = rect.center.x + Mathf.Abs(rect.width / 4f) + 2;
            drawRect.xMax = rect.xMax;
            GUI.enabled = true;
            if (_archetypeCollection.Definitions.Count > 0 && GUI.Button(drawRect, "Cancel"))
            {
                _reorderableList.list = _archetypeCollection.Definitions;
            }
            
            GUI.enabled = enabledBefore;
        }

        private void ResizeFoldoutList()
        {
            if (_foldouts == null)
                _foldouts = new List<bool>(Enumerable.Repeat(false, _archetypeCollection.Definitions.Count));
            if(_foldouts.Count > _archetypeCollection.Definitions.Count)
                _foldouts.RemoveRange(_archetypeCollection.Definitions.Count, _foldouts.Count - _archetypeCollection.Definitions.Count);
            if (_foldouts.Count < _archetypeCollection.Definitions.Count)
                _foldouts.AddRange(Enumerable.Repeat(false, _archetypeCollection.Definitions.Count - _foldouts.Count));
        }
        
        private bool IsNameValid(string nameToValidate, out string invalidReason, out bool showInfoBox)
        {
            if (string.IsNullOrEmpty(nameToValidate))
            {
                invalidReason = "The Name is invalid because an empty Name is not allowed.";
                showInfoBox = false;
                return false;
            }
            
            foreach (var invalidName in InvalidNames)
            {
                if (nameToValidate.Equals(invalidName))
                {
                    invalidReason = $"The Name can't be '{invalidName}'.";
                    showInfoBox = true;
                    return false;
                }
            }
            
            if (_archetypeCollection.Definitions.Any(d => d.Name == nameToValidate))
            {
                invalidReason = $"An entry already exists with the Name '{nameToValidate}'.";
                showInfoBox = true;
                return false;
            }

            invalidReason = "";
            showInfoBox = false;
            return true;
        }
    }
}