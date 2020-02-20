// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

namespace DotsPersistency
{
    public class PersistencyManager : IDisposable
    {
        [Serializable]
        public struct PersistentDataStorageKey : IComponentData
        {
            public PersistentComponents PersistentComponents;
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
            var persistentComponentsList = PersistentData.Keys.Select(key => key.PersistentComponents).Distinct();
            List<SceneSection> sceneSectionList = new List<SceneSection>();
            // only read from persistent data for scene sections currently loaded (bug what if there is an empty scene section? I will need to get all loaded scenesections in another manner)
            system.EntityManager.GetAllUniqueSharedComponentData(sceneSectionList);
            JobHandle mainJobHandle = inputDeps;
            JobHandle ecbJobHandle = inputDeps;

            foreach (var persistentComponents in persistentComponentsList)
            {
                if (persistentComponents.TypeHashList.Length < 1)
                    continue;

                foreach (var sceneSection in sceneSectionList)
                {
                    var key = new PersistentDataStorageKey
                    {
                        PersistentComponents = persistentComponents,
                        SceneSection = sceneSection
                    };

                    if (PersistentData.TryGetValue(key, out var persistentDataStorage))
                    {
                        for (int i = 0; i < persistentComponents.TypeHashList.Length; i++)
                        {
                            ulong stableTypeHash = persistentComponents.TypeHashList[i];
                            var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(persistentComponents.TypeHashList[i]);
                            var runtimeType = ComponentType.FromTypeIndex(typeIndex);
                            
                            var query = CreatePersistenceEntityQuery(system, runtimeType);
                            query.SetSharedComponentFilter(persistentComponents, sceneSection);
                            
                            var dataAndFound = persistentDataStorage.GetDataAndFoundArrays(stableTypeHash);
                            var elementSize = TypeManager.GetTypeInfo(typeIndex).ElementSize;

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
                                PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(),
                                Ecb = ecbSystem.CreateCommandBuffer().ToConcurrent(),
                                ComponentType = runtimeType,
                                InputFound = dataAndFound.Found,
                                InputData = dataAndFound.Data
                            }.Schedule(query, inputDeps));
                        }
                        
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

                        Debug.Log($"{sceneSection.SceneGUID} - {persistentComponents.TypeHashList.Length}: " + persistentDataStorage.GetAmount());
                        ecbJobHandle = JobHandle.CombineDependencies(ecbJobHandle, createEntitiesJob.Schedule(persistentDataStorage.GetAmount(), 8, getCompData));
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
            
            List<PersistentComponents> persistentComponentsList = new List<PersistentComponents>();
            system.EntityManager.GetAllUniqueSharedComponentData(persistentComponentsList);
            List<SceneSection> sceneSectionList = new List<SceneSection>();
            system.EntityManager.GetAllUniqueSharedComponentData(sceneSectionList);
            var jobHandle = inputDeps;
            
            foreach (var persistentComponents in persistentComponentsList)
            {           
                if (persistentComponents.TypeHashList.Length < 1)
                    continue;

                foreach (var sceneSection in sceneSectionList)
                {
                    var key = new PersistentDataStorageKey
                    {
                        PersistentComponents = persistentComponents,
                        SceneSection = sceneSection
                    };

                    if (!PersistentData.TryGetValue(key, out PersistentDataStorage persistentDataStorage))
                    {
                        var query = CreatePersistenceEntityQuery(system);
                        query.SetSharedComponentFilter(persistentComponents, sceneSection);
                        var typeHashList = persistentComponents.TypeHashList;
                        typeHashList.Add(PersistenceStateStableTypeHash);
                        var storage = new PersistentDataStorage(query.CalculateEntityCount(), typeHashList);
                        PersistentData[key] = storage;
                        persistentDataStorage = storage;
                    }

                    for (int i = 0; i < persistentComponents.TypeHashList.Length; i++)
                    {
                        ulong stableTypeHash = persistentComponents.TypeHashList[i];
                        var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(persistentComponents.TypeHashList[i]);
                        var runtimeType = ComponentType.ReadOnly(typeIndex);
                        
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
                            jobHandle = JobHandle.CombineDependencies(jobHandle, new FindComponentsOnPersistentEntities()
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
                    
                    var allPersistedEntitiesQuery = CreatePersistenceEntityQuery(system);
                    allPersistedEntitiesQuery.SetSharedComponentFilter(persistentComponents, sceneSection);
                    jobHandle = JobHandle.CombineDependencies(jobHandle, new FindComponentsOnPersistentEntities()
                    {
                        OutputFound = persistentDataStorage.GetFoundArray(PersistenceStateStableTypeHash)
                    }.Schedule(allPersistedEntitiesQuery, inputDeps));
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
                    ComponentType.ReadOnly<PersistentComponents>(),
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
                    ComponentType.ReadOnly<PersistentComponents>(),
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
                public NativeArray<byte> Data;
                public NativeArray<bool> Found;

                public void Dispose()
                {
                    Data.Dispose();
                    Found.Dispose();
                }
            }
            
            // Todo optimization make this an array to index in
            private Dictionary<ulong, DataAndFound> _typeToData;
            
            public bool CurrentlyLoaded;

            public PersistentDataStorage(int count, FixedList64<ulong> types)
            {
                _typeToData = new Dictionary<ulong, DataAndFound>(types.Length);
                for (int i = 0; i < types.Length; i++)
                {
                    var typeSize = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(types[i])).ElementSize;
                    _typeToData[types[i]] = new DataAndFound
                    {
                        Data = new NativeArray<byte>(typeSize * count, Allocator.Persistent),
                        Found = new NativeArray<bool>(count, Allocator.Persistent)
                    };
                }
                CurrentlyLoaded = true;
            }
        
            public DataAndFound GetDataAndFoundArrays(ulong typeHash)
            {
                return _typeToData[typeHash];
            }
            public NativeArray<byte> GetDataArray(ulong typeHash)
            {
                return _typeToData[typeHash].Data;
            }
            public NativeArray<bool> GetFoundArray(ulong typeHash)
            {
                return _typeToData[typeHash].Found;
            }
            public int GetAmount()
            {
                return _typeToData.First().Value.Found.Length;
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