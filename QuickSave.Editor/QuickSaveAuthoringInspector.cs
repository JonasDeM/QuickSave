// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuickSave.Editor
{
    [CustomEditor(typeof(QuickSaveAuthoring)), CanEditMultipleObjects]
    public class QuickSaveAuthoringInspector : UnityEditor.Editor
    {
        List<string> _cachedFullTypeNames;
        private QuickSaveSettingsAsset _quickSaveSettingsAsset;
        private List<QuickSaveArchetypeCollection.QuickSaveArchetypeDefinition> _allPresets;
        private static readonly string[] _mixedPresetDropDownList = new string[] { "--Mixed Presets--" };

        private void OnEnable()
        {
            _quickSaveSettingsAsset = QuickSaveSettingsAsset.Get();
            if (_quickSaveSettingsAsset)
            {
                _cachedFullTypeNames = _quickSaveSettingsAsset.AllQuickSaveTypeInfos.Select(info => info.FullTypeName).ToList();
                _cachedFullTypeNames.Insert(0, "");

                if (_quickSaveSettingsAsset.QuickSaveArchetypeCollection)
                {
                    _allPresets = new List<QuickSaveArchetypeCollection.QuickSaveArchetypeDefinition>(_quickSaveSettingsAsset.QuickSaveArchetypeCollection.Definitions);
                    _allPresets.Insert(0, new QuickSaveArchetypeCollection.QuickSaveArchetypeDefinition() {Name = ""});
                    _allPresets.Add(new QuickSaveArchetypeCollection.QuickSaveArchetypeDefinition() { Name = "Create New Preset"});
                }
            }
        }

        public override void OnInspectorGUI()
        {
            // Handle missing settings asset
            if (_quickSaveSettingsAsset == null)
            {
                if (GUILayout.Button("Create QuickSaveSettings", GUILayout.Height(50)))
                {
                    QuickSaveSettingsAsset.CreateInEditor();
                    _quickSaveSettingsAsset = QuickSaveSettingsAsset.Get();
                    _quickSaveSettingsAsset.QuickSaveArchetypeCollection = QuickSaveArchetypeCollection.CreateInEditor();
                    EditorUtility.SetDirty(_quickSaveSettingsAsset);
                    AssetDatabase.SaveAssets();
                    OnEnable();
                }
                return;
            }
            
            // Handle missing preset asset
            if (_quickSaveSettingsAsset.QuickSaveArchetypeCollection == null)
            {
                if (GUILayout.Button("Create QuickSaveArchetypeCollection", GUILayout.Height(50)))
                {
                    _quickSaveSettingsAsset.QuickSaveArchetypeCollection = QuickSaveArchetypeCollection.CreateInEditor();
                    EditorUtility.SetDirty(_quickSaveSettingsAsset);
                    AssetDatabase.SaveAssets();
                    OnEnable();
                }
                return;
            }
            
            // Grab the info from the first selected target
            var singleTarget = ((QuickSaveAuthoring) target);
            List<string> singleTargetTypeList = singleTarget.FullTypeNamesToPersist;
            if (singleTarget.QuickSaveArchetypeName != "")
            {
                var preset = _allPresets.Find(p => p.Name == singleTarget.QuickSaveArchetypeName);
                singleTargetTypeList = preset != null ? preset.FullTypeNames : new List<string>();
            }

            // Gather info on all targets as a whole
            bool samePresets = true;
            bool allNoPreset = true;
            bool sameHashes = true;
            foreach (Object o in targets)
            {
                var persistenceAuthoring = (QuickSaveAuthoring) o;
                List<string> typeList = persistenceAuthoring.FullTypeNamesToPersist;
                if (persistenceAuthoring.QuickSaveArchetypeName != "")
                {
                    var preset = _allPresets.Find(p => p.Name == persistenceAuthoring.QuickSaveArchetypeName);
                    typeList = preset != null ? preset.FullTypeNames : new List<string>();
                }
                
                if (singleTarget.QuickSaveArchetypeName != persistenceAuthoring.QuickSaveArchetypeName)
                    samePresets = false;

                if (persistenceAuthoring.QuickSaveArchetypeName != "")
                    allNoPreset = false;
                
                if (singleTargetTypeList.Count != typeList.Count || singleTargetTypeList.Except(typeList).Any())
                    sameHashes = false;
            }

            // Select a Preset
            if (samePresets)
            {
                int selectedIndex = _allPresets.FindIndex(p => p.Name == singleTarget.QuickSaveArchetypeName);
                
                GUI.enabled = !(allNoPreset && singleTargetTypeList.Count > 0);
                string noPresetText = GUI.enabled ? "No Preset" : "Custom Combination";
                int newSelectedIndex = EditorGUILayout.Popup($"Preset", selectedIndex,
                    _allPresets.Select(p => string.IsNullOrEmpty(p.Name) ? noPresetText : p.Name).ToArray());
                GUI.enabled = true;
                
                if (newSelectedIndex != selectedIndex)
                {
                    if (newSelectedIndex == _allPresets.Count - 1)
                    {
                        Selection.activeObject = _quickSaveSettingsAsset.QuickSaveArchetypeCollection;
                        return;
                    }
                    foreach (var o in targets)
                    {
                        var persistenceAuthoring = (QuickSaveAuthoring) o;
                        persistenceAuthoring.QuickSaveArchetypeName = _allPresets[newSelectedIndex].Name;
                        persistenceAuthoring.FullTypeNamesToPersist.Clear();
                        EditorUtility.SetDirty(persistenceAuthoring);
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                EditorGUILayout.Popup("Preset", 0, _mixedPresetDropDownList);
                GUI.enabled = true;
            }

            GUILayout.Space(8);
            EditorGUILayout.LabelField(allNoPreset ? "Type Combination (No Preset)" : "Type Combination");

            // Select the Types
            if (sameHashes)
            {
                GUI.enabled = allNoPreset;

                for (int i = 0; i < singleTargetTypeList.Count; i++)
                {
                    int selectedIndex = _cachedFullTypeNames.FindIndex(s => s == singleTargetTypeList[i]);

                    EditorGUILayout.BeginHorizontal();
                    int newSelectedIndex = EditorGUILayout.Popup($"Type {i}", selectedIndex,
                        _cachedFullTypeNames.Select(s => string.IsNullOrEmpty(s) ? "None" : _quickSaveSettingsAsset.GetPrettyNameInEditor(s)).ToArray());

                    if (GUI.enabled && GUILayout.Button("-", GUILayout.Width(18), GUILayout.Height(18)))
                    {
                        foreach (var o in targets)
                        {
                            var persistenceAuthoring = (QuickSaveAuthoring) o;
                            persistenceAuthoring.FullTypeNamesToPersist.RemoveAt(i);
                            EditorUtility.SetDirty(persistenceAuthoring);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    if (newSelectedIndex != selectedIndex)
                    {
                        foreach (var o in targets)
                        {
                            var persistenceAuthoring = (QuickSaveAuthoring) o;
                            persistenceAuthoring.FullTypeNamesToPersist[i] = _cachedFullTypeNames[newSelectedIndex];
                            persistenceAuthoring.FullTypeNamesToPersist = persistenceAuthoring.FullTypeNamesToPersist.Distinct().ToList();
                            persistenceAuthoring.FullTypeNamesToPersist.Sort();
                            EditorUtility.SetDirty(persistenceAuthoring);
                        }
                    }
                }

                if (singleTargetTypeList.Count == 0 && !allNoPreset)
                {
                    GUI.enabled = false;
                    EditorGUILayout.TextField("--No Types in Preset--");
                    GUI.enabled = true;
                }
                
                GUI.enabled = true;
            }
            else
            {
                GUI.enabled = false;
                EditorGUILayout.TextField("--Mixed Type Combinations--");
                GUI.enabled = true;
            }

            if (allNoPreset)
            {
                GUI.enabled = !singleTarget.FullTypeNamesToPersist.Contains("");
                string buttonText = GUI.enabled ? "Add Type" : "Add Type (You still have a \"None\" Value)";
                if (sameHashes && GUILayout.Button(buttonText))
                {
                    foreach (var o in targets)
                    {
                        var persistenceAuthoring = (QuickSaveAuthoring) o;
                        persistenceAuthoring.FullTypeNamesToPersist.Add("");
                        EditorUtility.SetDirty(persistenceAuthoring);
                    }
                }
                GUI.enabled = true;
            }
            
            GUILayout.Space(8);
            if (GUILayout.Button("Reset"))
            {
                foreach (var o in targets)
                {
                    var persistenceAuthoring = (QuickSaveAuthoring) o;
                    persistenceAuthoring.QuickSaveArchetypeName = "";
                    persistenceAuthoring.FullTypeNamesToPersist.Clear();
                    EditorUtility.SetDirty(persistenceAuthoring);
                }
            }

            if (allNoPreset && singleTargetTypeList.Count == 0)
                EditorGUILayout.HelpBox("Select a Preset or make a custom Type Combination with 'Add'", MessageType.Info);
        }
    }
}