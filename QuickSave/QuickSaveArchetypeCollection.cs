// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuickSave
{
    public class QuickSaveArchetypeCollection : ScriptableObject
    {
        private const string AssetPath = QuickSaveSettingsAsset.AssetFolder + "/QuickSaveArchetypeCollection.asset";
        
        [Serializable]
        public class QuickSaveArchetypeDefinition
        {
            public string Name = "New_QuickSaveArchetype_RenameMe";
            public List<string> FullTypeNames = new List<string>();
        }

        public List<QuickSaveArchetypeDefinition> Definitions = new List<QuickSaveArchetypeDefinition>();

#if UNITY_EDITOR
        public static QuickSaveArchetypeCollection CreateInEditor()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<QuickSaveArchetypeCollection>(AssetPath);
            if (asset != null)
                return asset;
            
            asset = CreateInstance<QuickSaveArchetypeCollection>();
            UnityEditor.AssetDatabase.CreateAsset(asset, AssetPath);
            UnityEditor.AssetDatabase.Refresh();
            return asset;
        }
#endif
    }
}