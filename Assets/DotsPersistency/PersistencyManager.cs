// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace DotsPersistency
{
    public class PersistencyManager : IDisposable
    {
        List<PersistentDataStorage> GetDataForSceneSection(SceneSection sceneSection)
        {
            // load from somewhere
            return new List<PersistentDataStorage>();
        }
        void SetDataForSceneSection(SceneSection sceneSection, List<PersistentDataStorage> data)
        {
            // store somewhere
        }

        //public JobHandle ScheduleFromPersistentDataJobs(SceneSection sceneSection, PersistencyJobSystem system, EntityCommandBufferSystem ecbSystem, JobHandle inputDeps)
        //{
        //    JobHandle mainJobHandle = inputDeps;
        //    JobHandle ecbJobHandle = inputDeps;
//
        //    foreach (var storage in GetDataForSceneSection(sceneSection))
        //    {
        //        PersistenceArchetype persistenceArchetype = storage.GetPersistedTypes();
        //        
        //        for (int i = 0; i < persistenceArchetype.ComponentDataTypeHashList.Length; i++)
        //        {
        //            ulong stableTypeHash = persistenceArchetype.ComponentDataTypeHashList[i];
        //            var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHash);
        //            var runtimeType = ComponentType.FromTypeIndex(typeIndex);
        //            var typeInfo = TypeManager.GetTypeInfo(typeIndex);
        //            
        //            var query = system.GetCachedQuery(runtimeType);
        //            query.SetSharedComponentFilter(persistenceArchetype, sceneSection);
        //            
        //            var dataAndFound = storage.GetDataAndFoundArrays(stableTypeHash);
        //            var elementSize = typeInfo.ElementSize;
//
        //            if (elementSize != 0)
        //            {
        //                mainJobHandle = JobHandle.CombineDependencies(mainJobHandle, new CopyByteArrayToComponentData()
        //                {
        //                    ChunkComponentType = system.GetArchetypeChunkComponentTypeDynamic(runtimeType),
        //                    TypeSize = elementSize,
        //                    PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(),
        //                    Input = dataAndFound.Data
        //                }.Schedule(query, inputDeps));
        //            }
        //            
        //            ecbJobHandle = JobHandle.CombineDependencies(ecbJobHandle, new RemoveComponent()
        //            {
        //                ComponentType = runtimeType,
        //                Ecb = ecbSystem.CreateCommandBuffer().ToConcurrent(),
        //                InputFound = dataAndFound.Found
        //            }.Schedule(query, inputDeps));
//
        //            runtimeType.AccessModeType = ComponentType.AccessMode.Exclude;
        //            var excludeQuery = system.GetCachedQuery(runtimeType);
        //            excludeQuery.SetSharedComponentFilter(persistenceArchetype, sceneSection);
        //            
        //            ecbJobHandle = JobHandle.CombineDependencies(ecbJobHandle, new AddMissingComponent()
        //            {
        //                EntityType = system.GetArchetypeChunkEntityType(),
        //                PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(true),
        //                Ecb = ecbSystem.CreateCommandBuffer().ToConcurrent(),
        //                ComponentType = runtimeType,
        //                InputFound = dataAndFound.Found,
        //                InputData = dataAndFound.Data
        //            }.Schedule(excludeQuery, inputDeps));
        //        }
        //        
        //        for (int i = 0; i < persistenceArchetype.BufferElementTypeHashList.Length; i++)
        //        {
        //            ulong stableTypeHash = persistenceArchetype.BufferElementTypeHashList[i];
        //            var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHash);
        //            var runtimeType = ComponentType.FromTypeIndex(typeIndex); // read/write
        //            var typeInfo = TypeManager.GetTypeInfo(typeIndex);
        //            
        //            var query = system.GetCachedQuery(runtimeType);
        //            query.SetSharedComponentFilter(persistenceArchetype, sceneSection);
        //            
        //            var dataAndFound = storage.GetDataAndFoundArrays(stableTypeHash);
        //            var elementSize = typeInfo.ElementSize;
//
        //            mainJobHandle = JobHandle.CombineDependencies(mainJobHandle, new CopyByteArrayToBufferElements()
        //            {
        //                ChunkBufferType = system.GetArchetypeChunkBufferTypeDynamic(runtimeType),
        //                ElementSize = elementSize,
        //                MaxElements = typeInfo.BufferCapacity, 
        //                PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(true),
        //                InputData = dataAndFound.Data,
        //                AmountPersisted = dataAndFound.GetFoundAmountForBuffers()
        //            }.Schedule(query, inputDeps));
        //        }
        //    }
        //    
        //    ecbSystem.AddJobHandleForProducer(ecbJobHandle);
        //    return JobHandle.CombineDependencies(mainJobHandle, ecbJobHandle);
        //}
//
        //public JobHandle ScheduleToPersistentDataJobs(SceneSection sceneSection, PersistencyJobSystem system, JobHandle inputDeps)
        //{
        //    // todo create the storage with a swap chain buffer
        //    
        //    // todo GC Allocs
        //    List<PersistenceArchetype> persistentComponentsList = new List<PersistenceArchetype>();
        //    system.EntityManager.GetAllUniqueSharedComponentData(persistentComponentsList);
        //    List<PersistentDataStorage> storageList = new List<PersistentDataStorage>(persistentComponentsList.Count);
        //    
        //    var jobHandle = inputDeps;
        //    var generalQuery = system.GetCachedGeneralQuery();
//
        //    foreach (var persistentComponents in persistentComponentsList)
        //    {           
        //        if (persistentComponents.ComponentDataTypeHashList.Length < 1 && persistentComponents.BufferElementTypeHashList.Length < 1)
        //            continue;
//
        //        generalQuery.SetSharedComponentFilter(persistentComponents, sceneSection);
        //        var storage = new PersistentDataStorage(generalQuery.CalculateEntityCount(), persistentComponents);
        //        storageList.Add(storage);
//
        //        for (int i = 0; i < persistentComponents.ComponentDataTypeHashList.Length; i++)
        //        {
        //            ulong stableTypeHash = persistentComponents.ComponentDataTypeHashList[i];
        //            var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHash);
        //            var runtimeType = ComponentType.ReadOnly(typeIndex);
        //            var typeInfo = TypeManager.GetTypeInfo(typeIndex);
        //            Debug.Assert(typeInfo.Category != TypeManager.TypeCategory.BufferData, $"{runtimeType} in wrong list!"); 
        //            Debug.Assert(!TypeManager.HasEntityReferences(typeIndex), $"Persisting components with Entity References is not supported. Type: {runtimeType}"); 
        //            Debug.Assert(typeInfo.BlobAssetRefOffsetCount == 0, $"Persisting components with BlobAssetReferences is not supported. Type: {runtimeType}"); 
//
        //            
        //            var query = system.GetCachedQuery(runtimeType);
        //            query.SetSharedComponentFilter(persistentComponents, sceneSection);
        //            
        //            var dataAndFound = storage.GetDataAndFoundArrays(stableTypeHash);
        //            var elementSize = TypeManager.GetTypeInfo(typeIndex).ElementSize;
        //            if (elementSize != 0)
        //            {
        //                jobHandle = JobHandle.CombineDependencies(jobHandle, new CopyComponentDataToByteArray()
        //                {
        //                    ChunkComponentType = system.GetArchetypeChunkComponentTypeDynamic(runtimeType),
        //                    TypeSize = TypeManager.GetTypeInfo(typeIndex).ElementSize,
        //                    PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(),
        //                    OutputData = dataAndFound.Data,
        //                    OutputFound = dataAndFound.Found
        //                }.Schedule(query, inputDeps));
        //            }
        //            else
        //            {
        //                jobHandle = JobHandle.CombineDependencies(jobHandle, new FindPersistentEntities()
        //                {
        //                    OutputFound = dataAndFound.Found
        //                }.Schedule(query, inputDeps));
        //            }
        //            
        //            //#if UNITY_EDITOR
        //            //var entityCount = query.CalculateEntityCount();
        //            //var totalTracking = storage.GetDataAndFoundArrays(stableTypeHash).Found.Length;
        //            //Debug.Log($"Persisted {runtimeType}\nTotal Entities: {totalTracking} (Regardless whether they still exist)" +
        //            //          $"\nEntities with: {entityCount} | without: {totalTracking - entityCount}\nTotal: {query.CalculateChunkCount()} Chunks" +
        //            //          $"\nScene: \"{AssetDatabase.GUIDToAssetPath(sceneSection.SceneGUID.ToString())}\", Section: {sceneSection.Section}");
        //            //#endif
        //        }
        //        
        //        for (int i = 0; i < persistentComponents.BufferElementTypeHashList.Length; i++)
        //        {
        //            ulong stableTypeHash = persistentComponents.BufferElementTypeHashList[i];
        //            var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHash);
        //            var runtimeType = ComponentType.ReadOnly(typeIndex);
        //            var typeInfo = TypeManager.GetTypeInfo(typeIndex);
        //            Debug.Assert(typeInfo.Category == TypeManager.TypeCategory.BufferData, $"{runtimeType} in wrong list!"); 
        //            Debug.Assert(!TypeManager.HasEntityReferences(typeIndex), $"Persisting components with Entity References is not supported. Type: {runtimeType}");
        //            Debug.Assert(typeInfo.BlobAssetRefOffsetCount == 0, $"Persisting components with BlobAssetReferences is not supported. Type: {runtimeType}");
//
        //            var query = system.GetCachedQuery(runtimeType);
        //            query.SetSharedComponentFilter(persistentComponents, sceneSection);
        //            
        //            var dataAndFound = storage.GetDataAndFoundArrays(stableTypeHash);
        //            var elementSize = TypeManager.GetTypeInfo(typeIndex).ElementSize;
        //            Debug.Assert(elementSize > 0, $"Persisting Empty IBufferElementData is not supported. Type: {runtimeType}");
//
        //            jobHandle = JobHandle.CombineDependencies(jobHandle, new CopyBufferElementsToByteArray()
        //            {
        //                ChunkBufferType = system.GetArchetypeChunkBufferTypeDynamic(runtimeType),
        //                ElementSize = elementSize,
        //                MaxElements = typeInfo.BufferCapacity,
        //                PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(true),
        //                OutputData = dataAndFound.Data,
        //                AmountPersisted = dataAndFound.GetFoundAmountForBuffers()
        //            }.Schedule(query, inputDeps));
        //        }
        //    }
//
        //    SetDataForSceneSection(sceneSection, storageList);
//
        //    return jobHandle;
        //}

        public void Dispose()
        {
            // dispose containers
        }
        
        public struct PersistentDataStorage : IDisposable
        {
            
            public struct DataAndFound : IDisposable
            {
                // note that you can't use the same index, Found can be indexed normally, but for Data you need to multiply by TypeSize
                public NativeArray<bool> Found;
                public NativeArray<byte> Data;

                public NativeArray<int> GetFoundAmountForBuffers()
                {
                    return Found.Reinterpret<int>(1);
                }
                
                public void Dispose()
                {
                    Data.Dispose();
                    Found.Dispose();
                }
            }
            
            
            // Todo optimization make this 1 big array to index in
            private Dictionary<ulong, DataAndFound> _typeToData;

            public PersistenceArchetype PersistenceArchetype => _persistenceArchetype;
            private PersistenceArchetype _persistenceArchetype;

            public PersistentDataStorage(int count, PersistenceArchetype persistenceArchetype)
            {
                _persistenceArchetype = persistenceArchetype;
                _typeToData = new Dictionary<ulong, DataAndFound>(persistenceArchetype.ComponentDataTypeHashList.Length + persistenceArchetype.BufferElementTypeHashList.Length);
                foreach (var hash in persistenceArchetype.ComponentDataTypeHashList)
                {
                    var typeInfo = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(hash));
                    var typeSize = typeInfo.ElementSize;
                    _typeToData[hash] = new DataAndFound
                    {
                        Data = new NativeArray<byte>(typeSize * count, Allocator.Persistent),
                        Found = new NativeArray<bool>(count, Allocator.Persistent)
                    };
                }
                foreach (var hash in persistenceArchetype.BufferElementTypeHashList)
                {
                    var typeInfo = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(hash));
                    var typeSize = typeInfo.ElementSize;
                    int amountElements = typeInfo.BufferCapacity;
                    _typeToData[hash] = new DataAndFound
                    {
                        Data = new NativeArray<byte>(typeSize * amountElements * count, Allocator.Persistent),
                        Found = new NativeArray<bool>( count * sizeof(int) / sizeof(bool), Allocator.Persistent)
                    };
                }
            }

            public DataAndFound GetDataAndFoundArrays(ulong typeHash)
            {
                return _typeToData[typeHash];
            }

            public void Dispose()
            {
                foreach (var value in _typeToData.Values)
                {
                    value.Dispose();
                }
            }

            public PersistenceArchetype GetPersistedTypes()
            {
                throw new NotImplementedException();
            }
        }

    }
}