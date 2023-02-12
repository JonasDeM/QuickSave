// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace QuickSave.Editor
{
    public class FindTypeWindow : EditorWindow
    {
        private List<Type> _allAvailableTypes;
        internal delegate void TypeSelectedDelegate(string fullTypeName);
        internal TypeSelectedDelegate OnTypeChosen = persistableTypeInfo => { };

        [SerializeField]
        private Vector2 _scrollPos;
        [SerializeField]
        private string _currentFilter = "";
        [SerializeField]
        private bool _showUnityTypes = false;
        [SerializeField]
        private bool _showTestTypes = false;
        [SerializeField]
        private bool _showNonSupportedTypes = false;
        
        private void OnEnable()
        {
            UpdateTypeList();
            titleContent = new GUIContent("Choose a type");
        }

        private void OnGUI()
        {
            _currentFilter = EditorGUILayout.TextField("Filter: ", _currentFilter);
            EditorGUI.BeginChangeCheck();
            _showUnityTypes = EditorGUILayout.Toggle("Show Unity Types", _showUnityTypes);
            _showTestTypes = EditorGUILayout.Toggle("Show Test Types", _showTestTypes);
            _showNonSupportedTypes = EditorGUILayout.Toggle("Show Unsupported Types (Adding will fail but give you the explanation why.)", _showNonSupportedTypes);
            if (EditorGUI.EndChangeCheck())
            {
                UpdateTypeList();
            }
            List<string> filterValues = _currentFilter.Split(' ').ToList();
            
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            bool allHidden = true;
            foreach (Type ecsType in _allAvailableTypes)
            {
                string fullTypeName = ecsType.FullName.Replace("+", ".");
                bool hide = filterValues.Any(filterValue => fullTypeName.IndexOf(filterValue, StringComparison.InvariantCultureIgnoreCase) == -1);
                
                var oldAlignment =  GUI.skin.button.alignment;
                GUI.skin.button.alignment = TextAnchor.MiddleLeft;
                if (!hide)
                {
                    if (GUILayout.Button(fullTypeName))
                    {
                        if (!QuickSaveSettingsAsset.IsSupported(TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(ecsType)), out string notSupportedReason))
                        {
                            EditorUtility.DisplayDialog(nameof(QuickSaveSettingsAsset), notSupportedReason, "Ok");
                        }
                        else
                        {
                            OnTypeChosen(fullTypeName);
                            Close();
                        }
                    }
                }
                GUI.skin.button.alignment = oldAlignment;
                allHidden &= hide;
            }
            if (allHidden)
            {
                EditorGUILayout.HelpBox("No types found. Try changing the options above.", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }

        private void UpdateTypeList()
        {
            _allAvailableTypes = TypeManager.GetAllTypes()
                .Where(info => info.Type != null && info.Type.FullName != null && info.Category != TypeManager.TypeCategory.UnityEngineObject)
                .Where(info => _showNonSupportedTypes || QuickSaveSettingsAsset.IsSupported(info, out _))
                .Select(info => info.Type)
                .Where(type => type.Namespace == null || type.Namespace != typeof(LocalIndexInContainer).Namespace) // Don't show QuickSave Types
                .Where(type => (_showUnityTypes || !IsUnityType(type)) && (_showTestTypes || !IsTestType(type)))
                .ToList();
            _allAvailableTypes.Sort((type, type1) => string.CompareOrdinal(type.FullName, type1.FullName));
        }

        private static bool IsUnityType(Type type)
        {
            return type.Namespace != null && type.Namespace.Contains("Unity");
        }
        
        private static bool IsTestType(Type type)
        {
            return type.Namespace != null && type.Namespace.Contains("Test");
        }
        
        // Some code to ensure the window is closed when scripts are reloaded
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded() 
        {
            if (HasOpenInstances<FindTypeWindow>())
            {
                GetWindow<FindTypeWindow>().Close();
            }
        }
    }
}