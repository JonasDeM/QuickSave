using System.Collections.Generic;
using Unity.Entities;
using Unity.Jobs;

namespace DotsPersistency
{
    public abstract class PersistencyJobSystem : JobComponentSystem
    {
        private Dictionary<ComponentType, EntityQuery> _queryCache = new Dictionary<ComponentType, EntityQuery>(32);
        
        protected void InitializeReadOnly(RuntimePersistableTypesInfo typesInfo)
        {
            foreach (ulong typeHash in typesInfo.StableTypeHashes)
            {
                CacheQuery(ComponentType.ReadOnly(TypeManager.GetTypeIndexFromStableTypeHash(typeHash)));
            }
        }
        
        protected void InitializeReadWrite(RuntimePersistableTypesInfo typesInfo)
        {
            _queryCache.Add(ComponentType.ReadOnly<PersistenceState>(), CreatePersistenceEntityQuery());

            foreach (ulong typeHash in typesInfo.StableTypeHashes)
            {
                CacheQuery(ComponentType.FromTypeIndex(TypeManager.GetTypeIndexFromStableTypeHash(typeHash)));
            }
        }

        private void CacheQuery(ComponentType type)
        {
            _queryCache.Add(type, CreatePersistenceEntityQuery(type));
        }

        private EntityQuery GetCachedQuery(ComponentType persistedType)
        {
            return _queryCache[persistedType];
        }
        
        private EntityQuery CreatePersistenceEntityQuery(ComponentType persistedType)
        {
            EntityQueryDesc queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PersistenceArchetype>(),
                    ComponentType.ReadOnly<PersistenceState>(),
                    ComponentType.ReadOnly<SceneSection>(),
                    persistedType
                },
                Options = EntityQueryOptions.IncludeDisabled
            };

            var query = GetEntityQuery(queryDesc);
            return query;
        }
        
        private EntityQuery CreatePersistenceEntityQuery()
        {
            EntityQueryDesc queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PersistenceArchetype>(),
                    ComponentType.ReadOnly<PersistenceState>(),
                    ComponentType.ReadOnly<SceneSection>()
                },
                Options = EntityQueryOptions.IncludeDisabled
            };

            var query = GetEntityQuery(queryDesc);
            return query;
        }

        protected JobHandle ScheduleCopyToPersistentDataContainer(JobHandle inputDeps, SceneSection sceneSection, PersistentDataContainer dataContainer)
        {
            var returnJobHandle = inputDeps;

            for (int persistenceArchetypeIndex = 0; persistenceArchetypeIndex < dataContainer.Count; persistenceArchetypeIndex++)
            {
                PersistenceArchetype persistenceArchetype = dataContainer.GetPersistenceArchetypeAtIndex(persistenceArchetypeIndex);
                ref BlobArray<PersistedTypeInfo> typeInfoArray = ref persistenceArchetype.PersistedTypeInfoArrayRef.Value;
                var dataForArchetype = dataContainer.GetSubArrayAtIndex(persistenceArchetypeIndex);
                
                for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
                {
                    // type info
                    PersistedTypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                    ComponentType runtimeType = ComponentType.ReadOnly(TypeManager.GetTypeIndexFromStableTypeHash(typeInfo.StableHash));
                    int stride = typeInfo.ElementSize * typeInfo.MaxElements + PersistenceMetaData.SizeOfStruct;
                    int byteSize = persistenceArchetype.Amount * stride;
                    
                    // query
                    var query = GetCachedQuery(runtimeType);
                    query.SetSharedComponentFilter(sceneSection, persistenceArchetype);
                    
                    // Grab containers
                    var persistenceStateChunkType = GetArchetypeChunkComponentType<PersistenceState>(true);
                    var outputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                    
                    JobHandle jobHandle;
                    if (typeInfo.IsBuffer)
                    {
                        jobHandle = new CopyBufferElementsToByteArray
                        {
                            ChunkBufferType = this.GetArchetypeChunkBufferTypeDynamic(runtimeType),
                            ElementSize = typeInfo.ElementSize,
                            MaxElements = typeInfo.MaxElements,
                            PersistenceStateType = persistenceStateChunkType,
                            OutputData = outputData
                        }.Schedule(query, inputDeps);
                    }
                    else
                    {
                        jobHandle = new CopyComponentDataToByteArray()
                        {
                            ChunkComponentType = GetArchetypeChunkComponentTypeDynamic(runtimeType),
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = persistenceStateChunkType,
                            OutputData = outputData
                        }.Schedule(query, inputDeps);
                    }

                    returnJobHandle = JobHandle.CombineDependencies(returnJobHandle, jobHandle);
                }
            }

            return returnJobHandle;
        }
        
        protected JobHandle ScheduleApplyToSceneSection(JobHandle inputDeps, SceneSection sceneSection, PersistentDataContainer dataContainer, EntityCommandBufferSystem ecbSystem)
        {
            var returnJobHandle = inputDeps;

            for (int persistenceArchetypeIndex = 0; persistenceArchetypeIndex < dataContainer.Count; persistenceArchetypeIndex++)
            {
                PersistenceArchetype persistenceArchetype = dataContainer.GetPersistenceArchetypeAtIndex(persistenceArchetypeIndex);
                ref BlobArray<PersistedTypeInfo> typeInfoArray = ref persistenceArchetype.PersistedTypeInfoArrayRef.Value;
                var dataForArchetype = dataContainer.GetSubArrayAtIndex(persistenceArchetypeIndex);
                
                for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
                {
                    // type info
                    PersistedTypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                    ComponentType runtimeType = ComponentType.FromTypeIndex(TypeManager.GetTypeIndexFromStableTypeHash(typeInfo.StableHash));
                    int stride = typeInfo.ElementSize * typeInfo.MaxElements + PersistenceMetaData.SizeOfStruct;
                    int byteSize = persistenceArchetype.Amount * stride;
                    
                    // query
                    var query = GetCachedQuery(runtimeType);
                    query.SetSharedComponentFilter(sceneSection, persistenceArchetype);
                    
                    // Grab read-only containers
                    var persistenceStateChunkType = GetArchetypeChunkComponentType<PersistenceState>(true);
                    var inputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                    
                    JobHandle jobHandle;
                    if (typeInfo.IsBuffer)
                    {
                        jobHandle = new CopyByteArrayToBufferElements()
                        {
                            ChunkBufferType = this.GetArchetypeChunkBufferTypeDynamic(runtimeType),
                            ElementSize = typeInfo.ElementSize,
                            MaxElements = typeInfo.MaxElements,
                            PersistenceStateType = persistenceStateChunkType,
                            InputData = inputData
                        }.Schedule(query, inputDeps);
                    }
                    else
                    {
                        JobHandle compDataJobHandle1 = new CopyByteArrayToComponentData()
                        {
                            ChunkComponentType = GetArchetypeChunkComponentTypeDynamic(runtimeType),
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = persistenceStateChunkType,
                            InputData = inputData
                        }.Schedule(query, inputDeps);
                        
                        JobHandle compDataJobHandle2 = new RemoveComponent()
                        {
                            ComponentType = runtimeType,
                            TypeSize = typeInfo.ElementSize,
                            InputData = inputData,
                            Ecb = ecbSystem.CreateCommandBuffer().ToConcurrent()
                        }.Schedule(query, inputDeps);
                        
                        JobHandle compDataJobHandle3 = new AddMissingComponent()
                        {
                            ComponentType = runtimeType,
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = persistenceStateChunkType,
                            EntityType = GetArchetypeChunkEntityType(),
                            InputData = inputData,
                            Ecb = ecbSystem.CreateCommandBuffer().ToConcurrent()
                        }.Schedule(query, inputDeps);
                        
                        jobHandle = JobHandle.CombineDependencies(compDataJobHandle1, compDataJobHandle2, compDataJobHandle3);
                    }
                    returnJobHandle = JobHandle.CombineDependencies(returnJobHandle, jobHandle);
                }
            }

            return returnJobHandle;
        }
    }
}
