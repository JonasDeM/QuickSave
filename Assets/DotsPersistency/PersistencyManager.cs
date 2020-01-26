using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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
        public Dictionary<PersistentDataStorageKey, PersistentDataStorage> PersistentData = new Dictionary<PersistentDataStorageKey, PersistentDataStorage>(10);

        public JobHandle ScheduleFromPersistentDataJobs(ComponentSystemBase system, JobHandle inputDeps)
        {            
            List<PersistentComponents> persistentComponentsList = new List<PersistentComponents>();
            system.EntityManager.GetAllUniqueSharedComponentData(persistentComponentsList);
            List<SceneSection> sceneSectionList = new List<SceneSection>();
            system.EntityManager.GetAllUniqueSharedComponentData(sceneSectionList);
            JobHandle jobHandle = inputDeps;

            foreach (var persistentComponents in persistentComponentsList)
            {
                if (persistentComponents.TypeHashList.Length < 1)
                    continue;
                var query = CreateEntityQuery(system, persistentComponents, false);

                foreach (var sceneSection in sceneSectionList)
                {
                    query.SetSharedComponentFilter(persistentComponents, sceneSection);
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
                            var runtimeType = ComponentType.FromTypeIndex(TypeManager.GetTypeIndexFromStableTypeHash(persistentComponents.TypeHashList[i]));
                            jobHandle = JobHandle.CombineDependencies(jobHandle, new CopyByteArrayToComponentData()
                            {
                                ChunkComponentType = system.GetArchetypeChunkComponentTypeDynamic(runtimeType),
                                PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(),
                                Input = persistentDataStorage.GetNativeArray(stableTypeHash)
                            }.Schedule(query, inputDeps));
                        }
                    }
                    else
                    {
                        PersistentData[key] = new PersistentDataStorage(query.CalculateEntityCount(), persistentComponents.TypeHashList);
                    }
                }
            }

            return jobHandle;
        }

        public JobHandle ScheduleToPersistentDataJobs(ComponentSystemBase system, JobHandle inputDeps)
        {
            List<PersistentComponents> persistentComponentsList = new List<PersistentComponents>();
            system.EntityManager.GetAllUniqueSharedComponentData(persistentComponentsList);
            List<SceneSection> sceneSectionList = new List<SceneSection>();
            system.EntityManager.GetAllUniqueSharedComponentData(sceneSectionList);
            var jobHandle = inputDeps;
            
            foreach (var persistentComponents in persistentComponentsList)
            {           
                if (persistentComponents.TypeHashList.Length < 1)
                    continue;
                var query = CreateEntityQuery(system, persistentComponents, true);

                foreach (var sceneSection in sceneSectionList)
                {
                    query.SetSharedComponentFilter(persistentComponents, sceneSection);
                    var key = new PersistentDataStorageKey
                    {
                        PersistentComponents = persistentComponents,
                        SceneSection = sceneSection
                    };

                    Debug.Assert(PersistentData.ContainsKey(key), "Unexpected error");
                    var persistentDataStorage = PersistentData[key];
                    for (int i = 0; i < persistentComponents.TypeHashList.Length; i++)
                    {
                        ulong stableTypeHash = persistentComponents.TypeHashList[i];
                        var runtimeType = ComponentType.ReadOnly(TypeManager.GetTypeIndexFromStableTypeHash(persistentComponents.TypeHashList[i]));
                        jobHandle = JobHandle.CombineDependencies(jobHandle, new CopyComponentDataToByteArray()
                        {
                            ChunkComponentType = system.GetArchetypeChunkComponentTypeDynamic(runtimeType),
                            PersistenceStateType = system.GetArchetypeChunkComponentType<PersistenceState>(),
                            Output = persistentDataStorage.GetNativeArray(stableTypeHash)
                        }.Schedule(query, inputDeps));
                    }
                }
            }

            return jobHandle;
        }
        
        private static EntityQuery CreateEntityQuery(ComponentSystemBase system, PersistentComponents persistentComponents, bool readOnly)
        {
            ComponentType[] typeArray = new ComponentType[persistentComponents.TypeHashList.Length + 3];
            typeArray[0] = ComponentType.ReadOnly<PersistentComponents>();
            typeArray[1] = ComponentType.ReadOnly<PersistenceState>();
            typeArray[2] = ComponentType.ReadOnly<SceneSection>();
            for (int i = 0; i < persistentComponents.TypeHashList.Length; i++)
            {
                // todo optimization Convert Hash to runtime index
                if (readOnly)
                {
                    typeArray[i + 3] = ComponentType.ReadOnly(TypeManager.GetTypeIndexFromStableTypeHash(persistentComponents.TypeHashList[i]));
                }
                else
                {
                    typeArray[i + 3] = ComponentType.FromTypeIndex(TypeManager.GetTypeIndexFromStableTypeHash(persistentComponents.TypeHashList[i]));
                }
            }

            var query = system.GetEntityQuery(typeArray);
            return query;
        }

        public void Dispose()
        {
            foreach (var value in PersistentData.Values)
            {
                value.Dispose();
            }
        }
        
        // todo one of these for each PersistenceArchetype+SubScene combination
        public struct PersistentDataStorage : IDisposable
        {
            // Todo optimization make this an array to index in
            private Dictionary<ulong, NativeArray<byte>> _typeToData;
            public bool CurrentlyLoaded;

            public PersistentDataStorage(int count, FixedList64<ulong> types)
            {
                _typeToData = new Dictionary<ulong, NativeArray<byte>>(types.Length);
                for (int i = 0; i < types.Length; i++)
                {
                    var typeSize = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(types[i])).ElementSize;
                    _typeToData[types[i]] = new NativeArray<byte>(typeSize * count, Allocator.Persistent);
                }
                CurrentlyLoaded = true;
            }
        
            public NativeArray<byte> GetNativeArray(ulong typeHash)
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