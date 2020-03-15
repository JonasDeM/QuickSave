using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Scenes;
using Debug = UnityEngine.Debug;

namespace DotsPersistency
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    [UpdateBefore(typeof(BeginFramePersistentDataSystem))]
    public class PersistenceInitializationSystem : ComponentSystem
    {
        private EntityQuery _query;
        private EntityQuery _sceneLoadedCheckQuery;
        public PersistentDataStorage PersistentDataStorage { get; private set; }

        private EntityCommandBufferSystem _ecbSystem;
        private BeginFramePersistentDataSystem _beginFrameSystem;

        protected override void OnCreate()
        {
            _beginFrameSystem = World.GetOrCreateSystem<BeginFramePersistentDataSystem>();
            PersistentDataStorage = _beginFrameSystem.PersistentDataStorage;
            _sceneLoadedCheckQuery = GetEntityQuery(ComponentType.ReadOnly<SceneSection>());
            
            _query = GetEntityQuery(new EntityQueryDesc(){ 
                All = new [] {ComponentType.ReadOnly<TypeHashesToPersist>(), ComponentType.ReadOnly<SceneSection>()},
                Options = EntityQueryOptions.IncludeDisabled
            });
            _ecbSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            EntityCommandBuffer ecb = _ecbSystem.CreateCommandBuffer();
            NativeList<SceneSection> sceneSectionsToInit = new NativeList<SceneSection>(2, Allocator.Temp);
            
            Entities.ForEach((Entity entity, ref RequestPersistentSceneLoaded requestInfo, ref SceneSectionData sceneSectionData) =>
            {
                SceneSection sceneSection = new SceneSection
                {
                    Section = sceneSectionData.SubSectionIndex,
                    SceneGUID = sceneSectionData.SceneGUID
                };

                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneLoaded.Stage.Complete)
                {
                    return;
                }
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneLoaded.Stage.WaitingForSceneLoad)
                {
                    _sceneLoadedCheckQuery.SetSharedComponentFilter(sceneSection);
                    if (_sceneLoadedCheckQuery.CalculateChunkCount() > 0)
                    {
                        sceneSectionsToInit.Add(sceneSection);
                        requestInfo.CurrentLoadingStage = RequestPersistentSceneLoaded.Stage.Complete;
                    }
                }
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneLoaded.Stage.InitialStage)
                {
                    requestInfo.CurrentLoadingStage = RequestPersistentSceneLoaded.Stage.WaitingForContainer;
                }
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneLoaded.Stage.WaitingForContainer && !PersistentDataStorage.IsWaitingForContainer())
                {
                    // After the container is available actually start loading the scene
                    ecb.AddComponent(entity, new RequestSceneLoaded()
                    {
                        LoadFlags = requestInfo.LoadFlags
                    });
                    requestInfo.CurrentLoadingStage = RequestPersistentSceneLoaded.Stage.WaitingForSceneLoad;
                }
            });
            
            var uniqueSharedCompData = new List<TypeHashesToPersist>();
            EntityManager.GetAllUniqueSharedComponentData(uniqueSharedCompData);
            uniqueSharedCompData.Remove(default);

            foreach (var sceneSection in sceneSectionsToInit)
            {
                InitSceneSection(sceneSection, uniqueSharedCompData, ecb);
            }
        }

        private void InitSceneSection(SceneSection sceneSection, List<TypeHashesToPersist> typeHashes, EntityCommandBuffer ecb)
        {
            int offset = 0;
            var archetypes = new NativeArray<PersistenceArchetype>(typeHashes.Count, Allocator.Temp);
            for (var i = 0; i < typeHashes.Count; i++)
            {
                var typeHashesToPersist = typeHashes[i];
                _query.SetSharedComponentFilter(typeHashesToPersist, sceneSection);
                int amount = _query.CalculateEntityCount();
                if (amount <= 0) 
                    continue;
                
                var persistenceArchetype = new PersistenceArchetype()
                {
                    Amount = _query.CalculateEntityCount(),
                    ArchetypeIndex = i,
                    PersistedTypeInfoArrayRef = BuildTypeInfoBlobAsset(typeHashesToPersist.TypeHashList, amount, out int sizePerEntity),
                    SizePerEntity = sizePerEntity,
                    Offset = offset
                };
                offset += amount * sizePerEntity;
                
                ecb.AddSharedComponent(_query, persistenceArchetype);
                ecb.RemoveComponent<TypeHashesToPersist>(_query);
            }

            if (PersistentDataStorage.HasContainer(sceneSection))
            {
                _beginFrameSystem.RequestApply(sceneSection);
            } // could do elsif PersistentDataStorage.HasDelta(sceneSection) => request persist, delta apply & apply
            else
            {
                PersistentDataStorage.CreateContainer(sceneSection, archetypes);
                _beginFrameSystem.RequestPersist(sceneSection);
            }

            archetypes.Dispose();
        }

        internal static BlobAssetReference<BlobArray<PersistedTypeInfo>> BuildTypeInfoBlobAsset(FixedList128<ulong> stableTypeHashes, int amountEntities, out int sizePerEntity)
        {
            BlobAssetReference<BlobArray<PersistedTypeInfo>> blobAssetReference;
            int currentOffset = 0;
            sizePerEntity = 0;
            
            using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref BlobArray<PersistedTypeInfo> blobArray = ref blobBuilder.ConstructRoot<BlobArray<PersistedTypeInfo>>();

                var blobBuilderArray = blobBuilder.Allocate(ref blobArray, stableTypeHashes.Length);

                for (int i = 0; i < blobBuilderArray.Length; i++)
                {
                    var typeInfo = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHashes[i]));

                    int maxElements = typeInfo.Category == TypeManager.TypeCategory.BufferData ? typeInfo.BufferCapacity : 1;

                    ValidateType(typeInfo);
                    
                    blobBuilderArray[i] = new PersistedTypeInfo()
                    {
                        StableHash = stableTypeHashes[i],
                        ElementSize = typeInfo.ElementSize,
                        IsBuffer = typeInfo.Category == TypeManager.TypeCategory.BufferData,
                        MaxElements = maxElements,
                        Offset = currentOffset
                    };
                    sizePerEntity += typeInfo.ElementSize * maxElements + sizeof(ushort); // PersistenceMetaData is one ushort
                    currentOffset += sizePerEntity * amountEntities;
                }

                blobAssetReference = blobBuilder.CreateBlobAssetReference<BlobArray<PersistedTypeInfo>>(Allocator.Persistent);
            }
            
            return blobAssetReference;
        }

        [Conditional("DEBUG")]
        private static void ValidateType(TypeManager.TypeInfo typeInfo)
        {
            if (TypeManager.HasEntityReferences(typeInfo.TypeIndex))
            {
                Debug.LogWarning($"Persisting components with Entity References is not supported. Type: {ComponentType.FromTypeIndex(typeInfo.TypeIndex)}");
            }

            if (typeInfo.BlobAssetRefOffsetCount > 0)
            {
                Debug.LogWarning($"Persisting components with BlobAssetReferences is not supported. Type: {ComponentType.FromTypeIndex(typeInfo.TypeIndex)}");
            }
        }
    }
}