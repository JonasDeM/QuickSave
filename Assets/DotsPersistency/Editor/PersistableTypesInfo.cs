using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DotsPersistency.Editor
{
    public class PersistableTypesInfo : ScriptableObject
    {
        private const string FOLDER = "Assets/Cache/DotsPersistency";
        private const string FILENAME = "UserDefinedPersistableTypes.asset";
        private static PersistableTypesInfo _instance;
        public List<string> FullTypeNames = new List<string>()
        {
            "Unity.Transforms.Translation",
            "Unity.Transforms.Rotation"
        };
        
        public List<AssemblyDefinitionAsset> Assemblies = new List<AssemblyDefinitionAsset>() { };
        
        
        public static PersistableTypesInfo GetInstance()
        {
            if (_instance == null)
            {            
                _instance = AssetDatabase.LoadAssetAtPath<PersistableTypesInfo>(FOLDER + "/" + FILENAME);
                if (_instance == null)
                {
                    Directory.CreateDirectory(FOLDER);
                    _instance = CreateInstance<PersistableTypesInfo>();
                    AssetDatabase.CreateAsset(_instance, FOLDER + "/" + FILENAME);
                }
            }
            return _instance;
        }
        
    }
}
