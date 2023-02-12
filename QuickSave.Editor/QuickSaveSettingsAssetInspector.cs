// Author: Jonas De Maeseneer

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace QuickSave.Editor
{
    [CustomEditor(typeof(QuickSaveSettingsAsset))]
    public class QuickSaveSettingsAssetInspector : UnityEditor.Editor
    {
        private List<string> _allAvailableTypes;
        private ReorderableList _reorderableList;
        private QuickSaveSettingsAsset _quickSaveSettingsAsset;

        private bool _foldoutSettings = false;
        
        private void OnEnable()
        {
            _quickSaveSettingsAsset = (QuickSaveSettingsAsset) target;
            _reorderableList = new ReorderableList(_quickSaveSettingsAsset.AllQuickSaveTypeInfos, typeof(QuickSaveSettingsAsset.QuickSaveSettingsTypeInfo));
            _reorderableList.drawHeaderCallback = DrawHeaderCallback;
            _reorderableList.draggable = false;
            _reorderableList.drawElementCallback = DrawElementCallback;
            _reorderableList.elementHeight = EditorGUIUtility.singleLineHeight + 2;
            _reorderableList.onAddCallback = OnAddCallback;
            _reorderableList.onRemoveCallback = OnRemoveCallback;
        }

        private void OnRemoveCallback(ReorderableList list)
        {
            list.list.RemoveAt(list.index);
            EditorUtility.SetDirty(_quickSaveSettingsAsset);
        }

        private void OnAddCallback(ReorderableList list)
        {
            FindTypeWindow window = EditorWindow.GetWindow<FindTypeWindow>();
            window.OnTypeChosen = fullTypeName =>
            {
                _quickSaveSettingsAsset.AddQuickSaveTypeInEditor(fullTypeName);
                // trigger the list to update
                _reorderableList.list = _quickSaveSettingsAsset.AllQuickSaveTypeInfos;
                Repaint();
            };
        }

        public override void OnInspectorGUI()
        {
            GUI.enabled = !EditorApplication.isPlayingOrWillChangePlaymode;
            
            _reorderableList.DoLayoutList();
            if (GUILayout.Button("Force Update Type Data"))
            {
                var oldInfo = new List<QuickSaveSettingsAsset.QuickSaveSettingsTypeInfo>(_quickSaveSettingsAsset.AllQuickSaveTypeInfos);
                _quickSaveSettingsAsset.ClearPersistableTypesInEditor();
                for (int i = oldInfo.Count - 1; i >= 0; i--)
                {
                    var infoToRetain = oldInfo[i];
                    _quickSaveSettingsAsset.AddQuickSaveTypeInEditor(infoToRetain.FullTypeName, infoToRetain.MaxElements);
                }
            }

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            _quickSaveSettingsAsset.QuickSaveArchetypeCollection = EditorGUILayout.ObjectField("Preset Collection", _quickSaveSettingsAsset.QuickSaveArchetypeCollection, typeof(QuickSaveArchetypeCollection), false) as QuickSaveArchetypeCollection;
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_quickSaveSettingsAsset);
            }
            _foldoutSettings = EditorGUILayout.Foldout(_foldoutSettings, "Advanced Options", true);
            EditorGUI.BeginChangeCheck();
            if (_foldoutSettings)
            {
                _quickSaveSettingsAsset.VerboseBakingLog = EditorGUILayout.ToggleLeft("Verbose Baking Log", _quickSaveSettingsAsset.VerboseBakingLog);
                _quickSaveSettingsAsset.ForceUseGroupedJobsInEditor = EditorGUILayout.ToggleLeft("Force Grouped Jobs In Editor (Only Works With JobsDebugger Disabled)", _quickSaveSettingsAsset.ForceUseGroupedJobsInEditor);
                _quickSaveSettingsAsset.ForceUseNonGroupedJobsInBuild = EditorGUILayout.ToggleLeft("Force Non-Grouped Jobs In Build (Usually Slower)", _quickSaveSettingsAsset.ForceUseNonGroupedJobsInBuild);
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_quickSaveSettingsAsset);
            }
            
            if (EditorUtility.IsDirty(_quickSaveSettingsAsset))
            {
                EditorGUILayout.HelpBox("Save for the changes to go into effect! (Ctrl+S)", MessageType.Info);
            }

            GUI.enabled = true;
        }

        private void DrawElementCallback(Rect rect, int index, bool isactive, bool isfocused)
        {
            var info = _quickSaveSettingsAsset.AllQuickSaveTypeInfos[index];
            int maxElementBoxWidth = math.max(50, 10 * info.MaxElements.ToString().Length);
            string elementName = info.FullTypeName + (info.IsBuffer ? " [B]" : "");
            rect.xMax -= maxElementBoxWidth + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.LabelField(rect, elementName);

            if (info.IsBuffer)
            {
                rect.xMax += maxElementBoxWidth + EditorGUIUtility.standardVerticalSpacing;
                rect.xMin = rect.xMax - (maxElementBoxWidth + EditorGUIUtility.standardVerticalSpacing);
                rect.height -= 1;
                
                EditorGUI.BeginChangeCheck();
                info.MaxElements = math.clamp(EditorGUI.IntField(rect, info.MaxElements), 1, QuickSaveMetaData.MaxValueForAmount);
                if (EditorGUI.EndChangeCheck())
                {
                    _quickSaveSettingsAsset.AllQuickSaveTypeInfos[index] = info;
                    EditorUtility.SetDirty(_quickSaveSettingsAsset);
                }
            }
        }

        private void DrawHeaderCallback(Rect rect)
        {
            EditorGUI.LabelField(rect, "Types");
        }
    }
}
