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
        [Serializable]
        public struct PersistentDataStorageKey : IComponentData
        {
            public PersistedTypes PersistedTypes;
            public SceneSection SceneSection;
        }
    
        // Todo optimization make this an array to index in
        private Dictionary<PersistentDataStorageKey, PersistentDataStorage> PersistentData = new Dictionary<PersistentDataStorageKey, PersistentDataStorage>(10);
        
        internal ulong PersistenceStateStableTypeHash;
        
        public PersistencyManager()
        {
            PersistenceStateStableTypeHash = TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(typeof(PersistenceState))).StableTypeHash;
        }
        
        public JobHandle ScheduleFromPersistentDataJobs(ComponentSystemBase system, EntityCommandBufferSystem ecbSystem, JobHandle inputDeps)
        {            
            // get all the type combinations that were once persisted
            var persistentComponentsList = PersistentData.Keys.Select(key => key.PersistedTypes).Distinct();
            List<SceneSection> sceneSectionList = new List<SceneSection>();
            // only read from persistent data for scene sections currently loaded
            // (bug what if there is an empty scene section? I will need to get all loaded scenesections in another manner)
            system.EntityManager.GetAllUniqueSharedComponentData(sceneSectionList);
            JobHandle mainJobHandle = inputDeps;
            JobHandle ecbJobHandle = inputDeps;
            
            foreach (var persistentComponents in persistentComponentsList)
            {
                if (persistentComponents.ComponentDataTypeHashList.Length < 1)
                    continue;

                foreach (var sceneSection in sceneSectionList)
                {
                    var key = new PersistentDataStorageKey
                    {
                        PersistedTypes = persistentComponents,
                        SceneSection = sceneSection
                    };

                    if (PersistentData.TryGetValue(key, out var persistentDataStorage))
                    {
                        for (int i = 0; i < persistentComponents.ComponentDataTypeHashList.Length; i++)
                        {
                            ulong stableTypeHash = persistentComponents.ComponentDataTypeHashList[i];
                            var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHash);
                            var runtimeType = ComponentType.FromTypeIndex(typeIndex);
                            var typeInfo = TypeManager.GetTypeInfo(typeIndex);
                            
                            var query = CreatePersistenceEntityQuery(system, runtimeType);
                            query.SetSharedComponentFilter(persistentComponents, sceneSection);
                            
                            var dataAndFound = persistentDataStorage.GetDataAndFoundArrays(stableTypeHash);
                            var elementSize = typeInfo.ElementSize;

                            if (elementSize != 0)
                            {
                                mainJobHandle = JobHandle.CombineDependencies(mainJobHandle, new CopyByteArrayToComponentData()
                                {
                                    ChunkComponentType = system.GetArchetypeChunkComponentTypeDynamic(runtimeType),
                                    TypeSize = elementSize,
                                    PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(),
                                    Input = dataAndFound.Data
                                }.Schedule(query, inputDeps));
                            }
                            
                            // todo exclude entities without that component
                            ecbJobHandle = JobHandle.CombineDependencies(ecbJobHandle, new RemoveComponents()
                            {
                                ComponentType = runtimeType,
                                Ecb = ecbSystem.CreateCommandBuffer().ToConcurrent(),
                                InputFound = dataAndFound.Found
                            }.Schedule(query, inputDeps));
                            
                            // todo exclude entities with that component
                            ecbJobHandle = JobHandle.CombineDependencies(ecbJobHandle, new AddMissingComponents()
                            {
                                EntityType = system.GetArchetypeChunkEntityType(),
                                PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(true),
                                Ecb = ecbSystem.CreateCommandBuffer().ToConcurrent(),
                                ComponentType = runtimeType,
                                InputFound = dataAndFound.Found,
                                InputData = dataAndFound.Data
                            }.Schedule(query, inputDeps));
                        }
                        
                        for (int i = 0; i < persistentComponents.BufferElementTypeHashList.Length; i++)
                        {
                            ulong stableTypeHash = persistentComponents.BufferElementTypeHashList[i];
                            var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHash);
                            var runtimeType = ComponentType.FromTypeIndex(typeIndex); // read/write
                            var typeInfo = TypeManager.GetTypeInfo(typeIndex);
                            
                            var query = CreatePersistenceEntityQuery(system, runtimeType);
                            query.SetSharedComponentFilter(persistentComponents, sceneSection);
                            
                            var dataAndFound = persistentDataStorage.GetDataAndFoundArrays(stableTypeHash);
                            var elementSize = typeInfo.ElementSize;

                            mainJobHandle = JobHandle.CombineDependencies(mainJobHandle, new CopyByteArrayToBufferElements()
                            {
                                ChunkBufferType = system.GetArchetypeChunkBufferTypeDynamic(runtimeType),
                                ElementSize = elementSize,
                                MaxElements = typeInfo.BufferCapacity, 
                                PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(true),
                                InputData = dataAndFound.Data,
                                AmountPersisted = dataAndFound.GetFoundAmountForBuffers()
                            }.Schedule(query, inputDeps));
                        }
                        
#if DOTS_PERSISTENCY_ENABLE_DESTROY_AND_CREATE
                        var allPersistedEntitiesQuery = CreatePersistenceEntityQuery(system);
                        allPersistedEntitiesQuery.SetSharedComponentFilter(persistentComponents, sceneSection);
                        
                        // Destroy Entities that were 'Not Found' in the current PersistentData
                        ecbJobHandle = JobHandle.CombineDependencies(ecbJobHandle, new DestroyEntities()
                        {
                            Ecb = ecbSystem.CreateCommandBuffer().ToConcurrent(),
                            InputFound = persistentDataStorage.GetFoundArray(PersistenceStateStableTypeHash)
                        }.Schedule(allPersistedEntitiesQuery, inputDeps));
                        
                        // Create Entities that were 'Found' in the current PersistentData, but don't exist anymore
                        var amountHashes = persistentComponents.TypeHashList.Length;
                        
                        var createEntitiesJob = new CreateEntities()
                        {
                            Ecb = ecbSystem.CreateCommandBuffer().ToConcurrent(),
                            SceneSection = sceneSection,
                            PersistentComponents = persistentComponents,
                            EntitiesFound = persistentDataStorage.GetFoundArray(PersistenceStateStableTypeHash),
                            ArrayOfInputFoundArrays = new NativeArray<IntPtr>(amountHashes, Allocator.TempJob),
                            ArrayOfInputDataArrays = new NativeArray<IntPtr>(amountHashes, Allocator.TempJob),
                            ComponentTypesToAdd = new NativeArray<ComponentType>(amountHashes, Allocator.TempJob),
                            ComponentTypesSizes = new NativeArray<int>(amountHashes, Allocator.TempJob),
                            ExistingEntities = allPersistedEntitiesQuery.ToComponentDataArray<PersistenceState>(Allocator.TempJob, out JobHandle getCompData)
                        };
                        
                        for (var index = 0; index < amountHashes; index++)
                        {
                            var hash = persistentComponents.TypeHashList[index];
                            unsafe
                            {
                                createEntitiesJob.ArrayOfInputFoundArrays[index] = new IntPtr(persistentDataStorage.GetFoundArray(hash).GetUnsafeReadOnlyPtr());
                                createEntitiesJob.ArrayOfInputDataArrays[index] = new IntPtr(persistentDataStorage.GetDataArray(hash).GetUnsafeReadOnlyPtr());
                            }
                            var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(hash);
                            createEntitiesJob.ComponentTypesToAdd[index] = ComponentType.FromTypeIndex(typeIndex);
                            createEntitiesJob.ComponentTypesSizes[index] = TypeManager.GetTypeInfo(typeIndex).ElementSize;
                        }

                        ecbJobHandle = JobHandle.CombineDependencies(ecbJobHandle, createEntitiesJob.Schedule(persistentDataStorage.GetAmount(), 8, getCompData));
#endif
                    }
                }
            }
            
            ecbSystem.AddJobHandleForProducer(ecbJobHandle);
            return JobHandle.CombineDependencies(mainJobHandle, ecbJobHandle);
        }

        public JobHandle ScheduleToPersistentDataJobs(ComponentSystemBase system, JobHandle inputDeps)
        {
            // clear previous storage
            foreach (var value in PersistentData.Values)
            {
                value.Dispose();
            }
            PersistentData.Clear();
            
            List<PersistedTypes> persistentComponentsList = new List<PersistedTypes>();
            system.EntityManager.GetAllUniqueSharedComponentData(persistentComponentsList);
            List<SceneSection> sceneSectionList = new List<SceneSection>();
            system.EntityManager.GetAllUniqueSharedComponentData(sceneSectionList);
            var jobHandle = inputDeps;
            
            foreach (var persistentComponents in persistentComponentsList)
            {           
                if (persistentComponents.ComponentDataTypeHashList.Length < 1)
                    continue;

                foreach (var sceneSection in sceneSectionList)
                {
                    var key = new PersistentDataStorageKey
                    {
                        PersistedTypes = persistentComponents,
                        SceneSection = sceneSection
                    };

                    if (!PersistentData.TryGetValue(key, out PersistentDataStorage persistentDataStorage))
                    {
                        var query = CreatePersistenceEntityQuery(system);
                        query.SetSharedComponentFilter(persistentComponents, sceneSection);
                        var compDataTypeHashList = persistentComponents.ComponentDataTypeHashList.ToArray();
                        var bufferDataTypeHashList = persistentComponents.BufferElementTypeHashList.ToArray();
#if DOTS_PERSISTENCY_ENABLE_DESTROY_AND_CREATE
                        // todo if creation & destruction is supported then the highest PersistenceState arrayindex needs to be passed in as size
                        throw new NotImplementedException("The define DOTS_PERSISTENCY_ENABLE_DESTROY_AND_CREATE is not supported yet.");
                        compDataTypeHashList.Add(PersistenceStateStableTypeHash);
#endif
                        var storage = new PersistentDataStorage(query.CalculateEntityCount(), compDataTypeHashList, bufferDataTypeHashList);

                        PersistentData[key] = storage;
                        persistentDataStorage = storage;
                    }

                    for (int i = 0; i < persistentComponents.ComponentDataTypeHashList.Length; i++)
                    {
                        ulong stableTypeHash = persistentComponents.ComponentDataTypeHashList[i];
                        var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHash);
                        var runtimeType = ComponentType.ReadOnly(typeIndex);
                        var typeInfo = TypeManager.GetTypeInfo(typeIndex);
                        Debug.Assert(typeInfo.Category != TypeManager.TypeCategory.BufferData, $"{runtimeType} in wrong list!"); 
                        Debug.Assert(!TypeManager.HasEntityReferences(typeIndex), $"Persisting components with Entity References is not supported. Type: {runtimeType}"); 
                        Debug.Assert(typeInfo.BlobAssetRefOffsetCount == 0, $"Persisting components with BlobAssetReferences is not supported. Type: {runtimeType}"); 

                        
                        var query = CreatePersistenceEntityQuery(system, runtimeType);
                        query.SetSharedComponentFilter(persistentComponents, sceneSection);
                        
                        var dataAndFound = persistentDataStorage.GetDataAndFoundArrays(stableTypeHash);
                        var elementSize = TypeManager.GetTypeInfo(typeIndex).ElementSize;
                        if (elementSize != 0)
                        {
                            jobHandle = JobHandle.CombineDependencies(jobHandle, new CopyComponentDataToByteArray()
                            {
                                ChunkComponentType = system.GetArchetypeChunkComponentTypeDynamic(runtimeType),
                                TypeSize = TypeManager.GetTypeInfo(typeIndex).ElementSize,
                                PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(),
                                OutputData = dataAndFound.Data,
                                OutputFound = dataAndFound.Found
                            }.Schedule(query, inputDeps));
                        }
                        else
                        {
                            jobHandle = JobHandle.CombineDependencies(jobHandle, new FindPersistentEntities()
                            {
                                OutputFound = dataAndFound.Found
                            }.Schedule(query, inputDeps));
                        }
                        
                        #if UNITY_EDITOR
                        var entityCount = query.CalculateEntityCount();
                        var totalTracking = persistentDataStorage.GetDataAndFoundArrays(stableTypeHash).Found.Length;
                        Debug.Log($"Persisted {runtimeType}\nTotal Entities: {totalTracking} (Regardless whether they still exist)" +
                                  $"\nEntities with: {entityCount} | without: {totalTracking - entityCount}\nTotal: {query.CalculateChunkCount()} Chunks" +
                                  $"\nScene: \"{AssetDatabase.GUIDToAssetPath(sceneSection.SceneGUID.ToString())}\", Section: {sceneSection.Section}");
                        #endif
                    }
                    
                    for (int i = 0; i < persistentComponents.BufferElementTypeHashList.Length; i++)
                    {
                        ulong stableTypeHash = persistentComponents.BufferElementTypeHashList[i];
                        var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHash);
                        var runtimeType = ComponentType.ReadOnly(typeIndex);
                        var typeInfo = TypeManager.GetTypeInfo(typeIndex);
                        Debug.Assert(typeInfo.Category == TypeManager.TypeCategory.BufferData, $"{runtimeType} in wrong list!"); 
                        //Debug.Assert(!TypeManager.HasEntityReferences(typeIndex), $"Persisting components with Entity References is not supported. Type: {runtimeType}");
                        Debug.Assert(typeInfo.BlobAssetRefOffsetCount == 0, $"Persisting components with BlobAssetReferences is not supported. Type: {runtimeType}");

                        var query = CreatePersistenceEntityQuery(system, runtimeType);
                        query.SetSharedComponentFilter(persistentComponents, sceneSection);
                        
                        var dataAndFound = persistentDataStorage.GetDataAndFoundArrays(stableTypeHash);
                        var elementSize = TypeManager.GetTypeInfo(typeIndex).ElementSize;
                        Debug.Assert(elementSize > 0, $"Persisting Empty IBufferElementData is not supported. Type: {runtimeType}");

                        jobHandle = JobHandle.CombineDependencies(jobHandle, new CopyBufferElementsToByteArray()
                        {
                            ChunkBufferType = system.GetArchetypeChunkBufferTypeDynamic(runtimeType),
                            ElementSize = elementSize,
                            MaxElements = typeInfo.BufferCapacity, 
                            PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(true),
                            OutputData = dataAndFound.Data,
                            AmountPersisted = dataAndFound.GetFoundAmountForBuffers()
                        }.Schedule(query, inputDeps));
                    }
                    
#if DOTS_PERSISTENCY_ENABLE_DESTROY_AND_CREATE
                    var allPersistedEntitiesQuery = CreatePersistenceEntityQuery(system);
                    allPersistedEntitiesQuery.SetSharedComponentFilter(persistentComponents, sceneSection);
                    jobHandle = JobHandle.CombineDependencies(jobHandle, new FindComponentsOnPersistentEntities()
                    {
                        OutputFound = persistentDataStorage.GetFoundArray(PersistenceStateStableTypeHash)
                    }.Schedule(allPersistedEntitiesQuery, inputDeps));
#endif
                }
            }

            return jobHandle;
        }
        
        private static EntityQuery CreatePersistenceEntityQuery(ComponentSystemBase system, ComponentType persistedType)
        {
            EntityQueryDesc queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PersistedTypes>(),
                    ComponentType.ReadOnly<PersistenceState>(),
                    ComponentType.ReadOnly<SceneSection>(),
                    persistedType
                },
                Options = EntityQueryOptions.IncludeDisabled
            };

            var query = system.GetEntityQuery(queryDesc);
            return query;
        }
        
        private static EntityQuery CreatePersistenceEntityQuery(ComponentSystemBase system)
        {
            EntityQueryDesc queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PersistedTypes>(),
                    ComponentType.ReadOnly<PersistenceState>(),
                    ComponentType.ReadOnly<SceneSection>()
                },
                Options = EntityQueryOptions.IncludeDisabled
            };

            var query = system.GetEntityQuery(queryDesc);
            return query;
        }

        public void Dispose()
        {
            foreach (var value in PersistentData.Values)
            {
                value.Dispose();
            }
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

            public PersistentDataStorage(int count, ulong[] compDataTypeHashes, ulong[] bufferDataTypeHashes)
            {
                _typeToData = new Dictionary<ulong, DataAndFound>(compDataTypeHashes.Length + bufferDataTypeHashes.Length);
                foreach (var hash in compDataTypeHashes)
                {
                    var typeInfo = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(hash));
                    var typeSize = typeInfo.ElementSize;
                    _typeToData[hash] = new DataAndFound
                    {
                        Data = new NativeArray<byte>(typeSize * count, Allocator.Persistent),
                        Found = new NativeArray<bool>(count, Allocator.Persistent)
                    };
                }
                foreach (var hash in bufferDataTypeHashes)
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
        }

    }
}