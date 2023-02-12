// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace QuickSave
{
    public static class QuickSaveSettings
    {
        private static bool _initialized;
        
        // Settings
        private static bool _verboseBakingLog = false;
        public static bool VerboseBakingLog => _verboseBakingLog;
        
        // Type Info (Accessible in burst via SharedStatic)
        private static NativeArray<TypeIndex> _typeIndexArray;
        private static NativeArray<UnManagedTypeInfo> _unManagedTypeInfos;
        // Type Info (Not accessible in burst)
        private static NativeHashMap<TypeIndex, QuickSaveTypeHandle> _typeIndexToHandle;
        public static ulong StableHashOfAllQuickSaveTypes;
        private static readonly ulong HashOfZero = TypeHash.FNV1A64(0);
        private static ManagedTypeInfo[] _managedTypeInfos;
        
        public static bool NothingToSave => StableHashOfAllQuickSaveTypes == HashOfZero;
        
        internal struct ManagedTypeInfo
        {
            public string FullTypeName;
        }
        internal struct UnManagedTypeInfo
        {
            public int MaxElements;
        }
        
        public static void Initialize()
        {
            if (_initialized)
                return;
            
            var asset = QuickSaveSettingsAsset.Get();
            if (asset != null)
                Initialize(asset);
            else
                InitializeNoAsset();
        }
        
        internal static unsafe void Initialize(QuickSaveSettingsAsset asset)
        {
            if (_initialized)
                return;

            if (asset.AllQuickSaveTypeInfos.Count > QuickSaveTypeHandle.MaxTypes)
            {
                throw new ArgumentException($"The maximum number of QuickSave Types is {QuickSaveTypeHandle.MaxTypes} & this project has {asset.AllQuickSaveTypeInfos.Count}!");
            }
            
            _verboseBakingLog = asset.VerboseBakingLog;
            
            _typeIndexArray = new NativeArray<TypeIndex>(asset.AllQuickSaveTypeInfos.Count, Allocator.Persistent);
            _typeIndexToHandle = new NativeHashMap<TypeIndex, QuickSaveTypeHandle>(asset.AllQuickSaveTypeInfos.Count, Allocator.Persistent);
            StableHashOfAllQuickSaveTypes = HashOfZero;
            
            for (int i = 0; i < asset.AllQuickSaveTypeInfos.Count; i++)
            {
                var typeInfo = asset.AllQuickSaveTypeInfos[i];
                typeInfo.ValidityCheck();

                StableHashOfAllQuickSaveTypes = TypeHash.CombineFNV1A64(StableHashOfAllQuickSaveTypes, typeInfo.StableTypeHash);
                StableHashOfAllQuickSaveTypes = TypeHash.CombineFNV1A64(StableHashOfAllQuickSaveTypes, typeInfo.IsBuffer ? 17UL : 31UL);
                StableHashOfAllQuickSaveTypes = TypeHash.CombineFNV1A64(StableHashOfAllQuickSaveTypes, (ulong)typeInfo.MaxElements);
                
                int typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(typeInfo.StableTypeHash);
                _typeIndexArray[i] = typeIndex;
                _typeIndexToHandle[typeIndex] = new QuickSaveTypeHandle(i);
            }

            _unManagedTypeInfos = new NativeArray<UnManagedTypeInfo>(asset.AllQuickSaveTypeInfos.Count, Allocator.Persistent);
            _managedTypeInfos = new ManagedTypeInfo[asset.AllQuickSaveTypeInfos.Count];
            for (int i = 0; i < asset.AllQuickSaveTypeInfos.Count; i++)
            {
                _unManagedTypeInfos[i] = new UnManagedTypeInfo {MaxElements = asset.AllQuickSaveTypeInfos[i].MaxElements};
                _managedTypeInfos[i] = new ManagedTypeInfo {FullTypeName = asset.AllQuickSaveTypeInfos[i].FullTypeName};
            }
            
            TypeIndexArray.Ref.Data = new IntPtr(_typeIndexArray.GetUnsafePtr());
            UnManagedTypeInfoArray.Ref.Data = new IntPtr(_unManagedTypeInfos.GetUnsafePtr());
            TypeArraySize.Ref.Data = asset.AllQuickSaveTypeInfos.Count;
            ForceUseNonGroupedJobsInBuild.Ref.Data = asset.ForceUseNonGroupedJobsInBuild;
            ForceUseGroupedJobsInEditor.Ref.Data = asset.ForceUseGroupedJobsInEditor;
            QuickSaveContainerName.Ref.Data = new FixedString64Bytes("QuickSaveContainer");

            _initialized = true;
        }

        private static unsafe void InitializeNoAsset()
        {
            _typeIndexArray = new NativeArray<TypeIndex>(1, Allocator.Persistent);
            _unManagedTypeInfos = new NativeArray<UnManagedTypeInfo>(1, Allocator.Persistent);
            _typeIndexToHandle = new NativeHashMap<TypeIndex, QuickSaveTypeHandle>(0, Allocator.Persistent);
            _managedTypeInfos = new ManagedTypeInfo[0];
            
            TypeIndexArray.Ref.Data = new IntPtr(_typeIndexArray.GetUnsafePtr());
            UnManagedTypeInfoArray.Ref.Data = new IntPtr(_unManagedTypeInfos.GetUnsafePtr());
            TypeArraySize.Ref.Data = 0;
            ForceUseNonGroupedJobsInBuild.Ref.Data = false;
            ForceUseGroupedJobsInEditor.Ref.Data = false;
            QuickSaveContainerName.Ref.Data = new FixedString64Bytes("QuickSaveContainer");
            
            _initialized = true;
        }
        
        public static void CleanUp()
        {
            if (!_initialized)
                return;
            
            _typeIndexArray.Dispose();
            TypeIndexArray.Ref.Data = default;

            _unManagedTypeInfos.Dispose();
            UnManagedTypeInfoArray.Ref.Data = default;

            TypeArraySize.Ref.Data = 0;
            ForceUseNonGroupedJobsInBuild.Ref.Data = false;
            ForceUseGroupedJobsInEditor.Ref.Data = false;
            QuickSaveContainerName.Ref.Data = default;

            _typeIndexToHandle.Dispose();
            _managedTypeInfos = default;
            
            _initialized = false;
        }
        
        public static ref FixedString64Bytes GetQuickSaveContainerName()
        {
            return ref QuickSaveContainerName.Ref.Data;
        }

        public static QuickSaveTypeHandle GetTypeHandleFromTypeIndex(int typeIndex)
        {
            return _typeIndexToHandle[typeIndex];
        }

        // fastest way to get the type index
        public static unsafe TypeIndex GetTypeIndex(QuickSaveTypeHandle typeHandle)
        {
            CheckValidTypeHandle(typeHandle);
            return GetTypeIndexPointer()[typeHandle.IndexForQuickSaveSettings];
        }
        
        public static NativeArray<TypeIndex>.ReadOnly GetAllTypeIndices()
        {
            return _typeIndexArray.AsReadOnly();
        }

        public static unsafe int GetMaxElements(QuickSaveTypeHandle typeHandle)
        {
            CheckValidTypeHandle(typeHandle);
            return GetUnManagedTypeInfoPointer()[typeHandle.IndexForQuickSaveSettings].MaxElements;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckValidTypeHandle(QuickSaveTypeHandle typeHandle)
        {
            if (!typeHandle.IsValid && typeHandle.IndexForQuickSaveSettings < TypeArraySize.Ref.Data)
            {
                throw new ArgumentException("Expected a valid type handle");
            }
        }
        
        public static QuickSaveTypeHandle GetQuickSaveTypeHandleFromFullTypeName(string fullTypeName)
        {
            for (int i = 0; i < _managedTypeInfos.Length; i++)
            {
                if (_managedTypeInfos[i].FullTypeName == fullTypeName)
                {
                    return new QuickSaveTypeHandle(i);
                }
            }
            
            throw new ArgumentException($"{fullTypeName} was not registered as a QuickSave type.");
        }

        internal static NativeArray<QuickSaveTypeHandle> GetTypeHandles(List<string> fullTypeNames, Allocator allocator)
        {
            var typeHandleList = new NativeList<QuickSaveTypeHandle>(fullTypeNames.Count, allocator);
            
            foreach (var fullTypeName in fullTypeNames)
            {
                if (ContainsType(fullTypeName))
                {
                    typeHandleList.Add(GetQuickSaveTypeHandleFromFullTypeName(fullTypeName));
                }
            }
            
            return typeHandleList.AsArray();
        }

        public static bool ContainsType(string fullTypeName)
        {
            for (int i = 0; i < _managedTypeInfos.Length; i++)
            {
                if (_managedTypeInfos[i].FullTypeName == fullTypeName)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool UseGroupedJobs()
        {
#if UNITY_EDITOR
            return ForceUseGroupedJobsInEditor.Ref.Data;
#elif ENABLE_UNITY_COLLECTIONS_CHECKS
            return false;
#else
            return !ForceUseNonGroupedJobsInBuild.Ref.Data;
#endif
        }
        
        // This method can be used without the QuickSaveSettings being initialized
        public static bool IsSupported(TypeManager.TypeInfo info, out string notSupportedReason)
        {
            if (info.Category != TypeManager.TypeCategory.BufferData && info.Category != TypeManager.TypeCategory.ComponentData)
            {
                notSupportedReason = $"Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()} is not supported." +
                                     $" Reason: Needs to be {nameof(IComponentData)} or {nameof(IBufferElementData)}. (But it was {info.Category}).";
                return false;
            }
            
            if (info.EntityOffsetCount > 0)
            {
                notSupportedReason = $"Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()} is not supported." +
                                     $" Reason: Persisting components with Entity References is not supported.";
                return false;
            }

            if (info.HasBlobAssetRefs)
            {
                notSupportedReason = $"Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()} is not supported." +
                                     $" Reason: Persisting components with BlobAssetReferences is not supported.";
                return false;
            }
            
            if (info.HasWeakAssetRefs)
            {
                notSupportedReason = $"Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()} is not supported." +
                                     $" Reason: Persisting components with WeakAssetReferences is not supported.";
                return false;
            }

            if (info.TypeIndex.IsManagedComponent)
            {
                notSupportedReason = $"Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()} is not supported." +
                                     $" Reason: Persisting managed components is not supported.";
                return false;
            }
            
            if (info.BakingOnlyType || info.TemporaryBakingType)
            {
                notSupportedReason = $"Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()} is not supported. " +
                                     $"Reason: Persisting baking-only components is not supported. " +
                                     $"(Components with {nameof(BakingTypeAttribute)} attribute or {nameof(TemporaryBakingTypeAttribute)} attribute)";
                return false;
            }

            notSupportedReason = "";
            return true;
        }

        private struct QuickSaveSettingsKeyContext { }
        private struct TypeIndexArray
        {
            public static readonly SharedStatic<IntPtr> Ref = SharedStatic<IntPtr>.GetOrCreate<QuickSaveSettingsKeyContext, TypeIndexArray>();
        }
        private struct UnManagedTypeInfoArray
        {
            public static readonly SharedStatic<IntPtr> Ref = SharedStatic<IntPtr>.GetOrCreate<QuickSaveSettingsKeyContext, UnManagedTypeInfoArray>();
        }
        private struct TypeArraySize
        {
            public static readonly SharedStatic<int> Ref = SharedStatic<int>.GetOrCreate<QuickSaveSettingsKeyContext, TypeArraySize>();
        }
        private struct ForceUseNonGroupedJobsInBuild
        {
            public static readonly SharedStatic<bool> Ref = SharedStatic<bool>.GetOrCreate<QuickSaveSettingsKeyContext, ForceUseNonGroupedJobsInBuild>();
        }
        private struct ForceUseGroupedJobsInEditor
        {
            public static readonly SharedStatic<bool> Ref = SharedStatic<bool>.GetOrCreate<QuickSaveSettingsKeyContext, ForceUseGroupedJobsInEditor>();
        }
        private struct QuickSaveContainerName
        {
            public static readonly SharedStatic<FixedString64Bytes> Ref = SharedStatic<FixedString64Bytes>.GetOrCreate<QuickSaveSettingsKeyContext, QuickSaveContainerName>();
        }
        
        private static unsafe TypeIndex* GetTypeIndexPointer()
        {
            return (TypeIndex*)TypeIndexArray.Ref.Data;
        }
        
        private static unsafe UnManagedTypeInfo* GetUnManagedTypeInfoPointer()
        {
            return (UnManagedTypeInfo*)UnManagedTypeInfoArray.Ref.Data;
        }
    }
}
