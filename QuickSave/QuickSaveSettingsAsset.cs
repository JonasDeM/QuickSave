// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Debug = UnityEngine.Debug;

[assembly:InternalsVisibleTo("io.jonasdem.quicksave.editor")]
[assembly:InternalsVisibleTo("io.jonasdem.quicksave.tests")]

namespace QuickSave
{
    public class QuickSaveSettingsAsset : ScriptableObject
    {
        private const string ResourceFolder = "Assets/QuickSave/Resources";
        public const string AssetFolder = ResourceFolder + "/QuickSave";
        private const string RelativeFilePathNoExt = "QuickSave/QuickSaveSettings";
        private const string RelativeFilePath = RelativeFilePathNoExt + ".asset";
        private const string AssetPath = ResourceFolder + "/" + RelativeFilePath;
        private const string NotFoundMessage = "QuickSaveSettings.asset was not found, attach the PersistencyAuthoring script to a gameobject & press the 'create settings file' button.";
        
        [SerializeField]
        private List<QuickSaveSettingsTypeInfo> _allQuickSaveTypeInfos = new List<QuickSaveSettingsTypeInfo>();
        internal List<QuickSaveSettingsTypeInfo> AllQuickSaveTypeInfos => _allQuickSaveTypeInfos;
        
        [SerializeField]
        public QuickSaveArchetypeCollection QuickSaveArchetypeCollection = null;
        
        [SerializeField]
        internal bool ForceUseNonGroupedJobsInBuild = false;
        [SerializeField]
        internal bool ForceUseGroupedJobsInEditor = false;
        [SerializeField]
        internal bool VerboseBakingLog = false;
        
        [Serializable]
        internal struct QuickSaveSettingsTypeInfo
        {
            // If any of these values change on the type definition itself the validity check will pick that up & the user needs to force update
            // example: user makes his previous IComponentData into an IBufferElementData
            // example: user renames his type or changes the namespace
            // example: something in unity changes that makes the stable type hash different than before
            public string FullTypeName;
            public ulong StableTypeHash;
            public bool IsBuffer;
            public int MaxElements;

            [Conditional("DEBUG")]
            public void ValidityCheck()
            {
                if (MaxElements < 1 || string.IsNullOrEmpty(FullTypeName))
                {
                    throw new DataException("Invalid PersistableTypeInfo in the RuntimePersistableTypesInfo asset! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
                }

                TypeIndex typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(StableTypeHash);
                
                if (typeIndex == -1 || typeIndex == TypeIndex.Null)
                {
                    throw new DataException($"{FullTypeName} has an invalid StableTypeHash in PersistableTypeInfo in the RuntimePersistableTypesInfo asset! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
                }
                
                if (TypeManager.IsBuffer(typeIndex) != IsBuffer)
                {
                    throw new DataException($"{FullTypeName} is set as a buffer in the RuntimePersistableTypesInfo asset, but is not actually a buffer type! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
                }

                if (!IsBuffer && MaxElements > 1)
                {
                    throw new DataException($"{FullTypeName} has {MaxElements.ToString()} as MaxElements the RuntimePersistableTypesInfo asset, but it is not a buffer type! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
                }
            }
        }

        public static QuickSaveSettingsAsset Get()
        {
#if UNITY_EDITOR
            QuickSaveSettingsAsset settingsAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<QuickSaveSettingsAsset>(AssetPath);
            if (Application.isPlaying && UnityEditor.EditorUtility.IsDirty(settingsAsset))
            {
                Debug.LogError("The PersistencySettings ScriptableObject was modified but not yet saved! This will result in the persistency logic completely breaking. (Exit PlayMode & Press Ctrl+S to fix)");
                Debug.Break();
            }
#else
            // This only actually loads it the first time, so multiple calls are totally fine
            QuickSaveSettingsAsset settingsAsset = Resources.Load<QuickSaveSettingsAsset>(RelativeFilePathNoExt);
#endif
            if (settingsAsset == null)
            {
                Debug.LogWarning(NotFoundMessage);
            }
            return settingsAsset;
        }

        public static bool IsSupported(TypeManager.TypeInfo info, out string notSupportedReason)
        {
            // This method can be used without the QuickSaveSettings being initialized
            return QuickSaveSettings.IsSupported(info, out notSupportedReason);
        }
        
#if UNITY_EDITOR
        public static void CreateInEditor()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<QuickSaveSettingsAsset>(AssetPath);
            if (settings == null)
            {
                System.IO.Directory.CreateDirectory(AssetFolder);

                settings = CreateInstance<QuickSaveSettingsAsset>();
                settings.AddQuickSaveTypeInEditor(typeof(LocalTransform).FullName);
                settings.AddQuickSaveTypeInEditor(typeof(Disabled).FullName);
                UnityEditor.AssetDatabase.CreateAsset(settings, AssetPath);
            }
        }

        public bool AddQuickSaveTypeInEditor(string fullTypeName, int maxElements = -1)
        {
            if (_allQuickSaveTypeInfos.Any(info => info.FullTypeName == fullTypeName))
            {
                Debug.Log($"Failed to add {fullTypeName} is already in the list!");
                return false;
            }

            bool creationSuccess = CreatePersistableTypeInfoFromFullTypeName(fullTypeName, out QuickSaveSettingsTypeInfo persistableTypeInfo);
            if (!creationSuccess)
            {
                Debug.Log($"Removed {fullTypeName}, type doesn't exist in the ECS TypeManager");
                return false;
            }
            
            if (_allQuickSaveTypeInfos.Count + 1 > QuickSaveTypeHandle.MaxTypes)
            {
                Debug.Log($"Failed to add {fullTypeName}, reached the maximum number of types in the list! {_allQuickSaveTypeInfos.Count}/{QuickSaveTypeHandle.MaxTypes}");
                return false;
            }

            if (maxElements > 0 && persistableTypeInfo.IsBuffer)
            {
                persistableTypeInfo.MaxElements = maxElements;
            }

            _allQuickSaveTypeInfos.Add(persistableTypeInfo);
            _allQuickSaveTypeInfos.Sort((info1, info2) => string.CompareOrdinal(info1.FullTypeName, info2.FullTypeName));
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }

        private static bool CreatePersistableTypeInfoFromFullTypeName(string fullTypeName, out QuickSaveSettingsTypeInfo persistableTypeInfo)
        {
            foreach (var typeInfoEntry in TypeManager.AllTypes)
            {
                if (typeInfoEntry.Type != null && typeInfoEntry.Type.FullName == fullTypeName && QuickSaveSettings.IsSupported(typeInfoEntry, out _))
                {
                    bool isBuffer = typeInfoEntry.Category == TypeManager.TypeCategory.BufferData;
                    persistableTypeInfo = new QuickSaveSettingsTypeInfo()
                    {
                        FullTypeName = fullTypeName,
                        StableTypeHash = typeInfoEntry.StableTypeHash,
                        IsBuffer = isBuffer,
                        MaxElements = math.max(1, isBuffer ? typeInfoEntry.BufferCapacity : 1) // Initial BufferCapacity seems like a decent default max
                    };
                    return true;
                }
            }

            persistableTypeInfo = default;
            return false;
        }

        public void ClearPersistableTypesInEditor()
        {
            _allQuickSaveTypeInfos.Clear();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        internal string GetPrettyNameInEditor(string fullTypeName)
        {
            for (int i = 0; i < _allQuickSaveTypeInfos.Count; i++)
            {
                var typeInfo = _allQuickSaveTypeInfos[i];
                if (_allQuickSaveTypeInfos[i].FullTypeName == fullTypeName)
                {
                    string prettyName = fullTypeName.Substring(math.clamp(fullTypeName.LastIndexOf('.') + 1, 0, fullTypeName.Length))
                                        + (typeInfo.IsBuffer ? " [B]" : "");
                    return prettyName;
                }
            }

            return "Invalid";
        }
#endif // UNITY_EDITOR
    }
}
