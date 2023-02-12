// Author: Jonas De Maeseneer

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using Debug = UnityEngine.Debug;

namespace QuickSave
{
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    [UpdateBefore(typeof(QuickSaveBeginFrameSystem))]
    public partial struct QuickSaveSceneSystem : ISystem
    {
        private NativeHashMap<SceneSection, QuickSaveSceneInfoRef> _tempNewSceneSections;
        private SystemHandle _executingSystemHandle;
        private EntityQuery _quickSaveSceneInfoQuery;
        
        private EntityQuery _sectionsToCleanup;
        
        private ComponentLookup<QuickSaveSceneSection> _quickSaveSceneSectionLookup;
        private ComponentLookup<AutoApplyOnLoad> _autoApplyOnLoadLookup;
        private ComponentLookup<QuickSaveDataContainer> _containerLookup;
        private BufferLookup<QuickSaveDataContainer.Data> _dataLookup;
        private BufferLookup<QuickSaveArchetypeDataLayout> _dataLayoutLookup;
        private BufferLookup<DataTransferRequest> _dataTransferRequestLookup;

        public void OnCreate(ref SystemState state)
        {
            QuickSaveSettings.Initialize();
            
            _tempNewSceneSections = new NativeHashMap<SceneSection, QuickSaveSceneInfoRef>(16, Allocator.Persistent);
            _executingSystemHandle = state.World.GetOrCreateSystem<QuickSaveBeginFrameSystem>();
            
            _quickSaveSceneInfoQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<QuickSaveSceneInfoRef>().Build(state.EntityManager);
            state.RequireForUpdate(_quickSaveSceneInfoQuery);
            
            _sectionsToCleanup = new EntityQueryBuilder(Allocator.Temp)
                .WithNone<SceneSectionData>()
                .WithAll<QuickSaveSceneSection>().Build(state.EntityManager);
            
            _quickSaveSceneSectionLookup = state.GetComponentLookup<QuickSaveSceneSection>(true);
            _autoApplyOnLoadLookup = state.GetComponentLookup<AutoApplyOnLoad>(true);
            _containerLookup = state.GetComponentLookup<QuickSaveDataContainer>(true);
            _dataLookup = state.GetBufferLookup<QuickSaveDataContainer.Data>();
            _dataLayoutLookup = state.GetBufferLookup<QuickSaveArchetypeDataLayout>();
            _dataTransferRequestLookup = state.GetBufferLookup<DataTransferRequest>();
            
            // Create some archetypes at the start to avoid the cost at runtime
            CacheArchetypes(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob, PlaybackPolicy.SinglePlayback);
            
            // Cleanup any initial containers that belonged to scene section entities that were destroyed
            // This systems doesn't always run, so the cleanup can be delayed, but that's fine, it will run before new initial containers are created.
            new CleanupDestroyedSceneSections{ Ecb = ecb }.Run(_sectionsToCleanup);
            
            // QuickSaveSceneInfoRef that are not disabled notify us of a newly loaded sceneSection
            _tempNewSceneSections.Clear();
            new ProcessQuickSaveSceneInfo()
            {
                MapToFill = _tempNewSceneSections,
                Ecb = ecb
            }.Run(_quickSaveSceneInfoQuery);

            _quickSaveSceneSectionLookup.Update(ref state);
            _autoApplyOnLoadLookup.Update(ref state);
            _containerLookup.Update(ref state);
            _dataLookup.Update(ref state);
            _dataLayoutLookup.Update(ref state);
            _dataTransferRequestLookup.Update(ref state);
            
            new ProcessNewSceneSections()
            {
                InfoMap = _tempNewSceneSections,
                Ecb = ecb,
                ExecutingRequestsSystem = _executingSystemHandle,
                QuickSaveSceneSectionLookup = _quickSaveSceneSectionLookup,
                AutoApplyOnLoadLookup = _autoApplyOnLoadLookup,
                ContainerLookup = _containerLookup,
                DataLookup = _dataLookup,
                DataLayoutLookup = _dataLayoutLookup,
                DataTransferRequestLookup = _dataTransferRequestLookup
            }.Run();
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
        
        public void OnDestroy(ref SystemState state)
        {
            _tempNewSceneSections.Dispose();
            QuickSaveSettings.CleanUp();
        }

        [BurstCompile]
        private partial struct CleanupDestroyedSceneSections : IJobEntity
        {
            public EntityCommandBuffer Ecb;
            
            public void Execute(Entity entity, in QuickSaveSceneSection cleanupComp)
            {
                if (cleanupComp.InitialStateContainerEntity.Index > 0)
                    Ecb.DestroyEntity(cleanupComp.InitialStateContainerEntity);
                
                Ecb.RemoveComponent<QuickSaveSceneSection>(entity);
            }
        }

        [BurstCompile]
        private partial struct ProcessQuickSaveSceneInfo : IJobEntity
        {
            [WriteOnly]
            public NativeHashMap<SceneSection, QuickSaveSceneInfoRef> MapToFill;
            public EntityCommandBuffer Ecb;
            
            public void Execute(Entity entity, in QuickSaveSceneInfoRef sceneSectionInfo)
            {
                var sceneSection = new SceneSection()
                {
                    SceneGUID = sceneSectionInfo.InfoRef.Value.SceneGUID,
                    Section = 0
                };
                MapToFill.Add(sceneSection, sceneSectionInfo);
                Ecb.AddComponent<Disabled>(entity);
            }
        }

        [BurstCompile]
        private partial struct ProcessNewSceneSections : IJobEntity
        {
            [ReadOnly]
            public NativeHashMap<SceneSection, QuickSaveSceneInfoRef> InfoMap;
            public EntityCommandBuffer Ecb;
            
            public SystemHandle ExecutingRequestsSystem;

            [ReadOnly]
            public ComponentLookup<QuickSaveSceneSection> QuickSaveSceneSectionLookup;
            [ReadOnly]
            public ComponentLookup<AutoApplyOnLoad> AutoApplyOnLoadLookup;
            [ReadOnly]
            public ComponentLookup<QuickSaveDataContainer> ContainerLookup;
            public BufferLookup<QuickSaveDataContainer.Data> DataLookup;
            public BufferLookup<QuickSaveArchetypeDataLayout> DataLayoutLookup;
            public BufferLookup<DataTransferRequest> DataTransferRequestLookup;

            public void Execute(Entity sceneSectionEntity, in SceneSectionData sceneSectionData)
            {
                var sceneSection = new SceneSection {SceneGUID = sceneSectionData.SceneGUID, Section = sceneSectionData.SubSectionIndex};
                if (!InfoMap.TryGetValue(sceneSection, out var info)) // Todo this filtering could be done in the query?
                    return;

                Hash128 containerIdentifier = sceneSection.SceneGUID;
                ref QuickSaveSceneInfo sceneInfo = ref info.InfoRef.Value;

                Entity initialContainerEntity;
                QuickSaveDataContainer initialContainer;
                DynamicBuffer<QuickSaveDataContainer.Data> initialContainerData;
                DynamicBuffer<QuickSaveArchetypeDataLayout> initialDataLayouts;
                if (QuickSaveSceneSectionLookup.TryGetComponent(sceneSectionEntity, out var quickSaveSceneSection))
                {
                    initialContainerEntity = quickSaveSceneSection.InitialStateContainerEntity;
                    initialContainer = ContainerLookup[initialContainerEntity];
                    initialContainerData = DataLookup[initialContainerEntity];
                    initialDataLayouts = DataLayoutLookup[initialContainerEntity];
                }
                else
                {
                    var initialRequest = new DataTransferRequest()
                    {
                        ExecutingSystem = ExecutingRequestsSystem,
                        RequestType = DataTransferRequest.Type.FromEntitiesToDataContainer
                    };
                    initialContainerEntity = QuickSaveAPI.CreateInitialSceneContainer(Ecb, containerIdentifier, ref sceneInfo, initialRequest, out initialContainer, out initialContainerData, out initialDataLayouts);

                    Ecb.AddComponent(sceneSectionEntity, new QuickSaveSceneSection {InitialStateContainerEntity = initialContainerEntity});
                }

                // AUTO APPLY
                // **********
                
                if (AutoApplyOnLoadLookup.TryGetComponent(sceneSectionEntity, out AutoApplyOnLoad autoApply))
                {
                    Entity containerToAutoApplyEntity = autoApply.ContainerEntityToApply;
                    if (containerToAutoApplyEntity != Entity.Null)
                    {
                        if (!DataTransferRequestLookup.HasBuffer(containerToAutoApplyEntity))
                        {
                            // TODO Add functionality that upgrades every container that needs upgrading, not only the auto apply one (then this code gets replaced by this)
                            // TODO then also update the comments on RequestDeserializeIntoInvalidContainer
                            var containerToApply = ContainerLookup[containerToAutoApplyEntity];
                            var dataToApply = DataLookup[containerToAutoApplyEntity];
                            var layoutsToApply = DataLayoutLookup[containerToAutoApplyEntity];
                            
                            if (ValidateAndUpgradeContainer(ref containerToApply, ref dataToApply, ref layoutsToApply, in initialContainer, in initialContainerData, in initialDataLayouts))
                            {
                                containerToApply.InitialContainer = initialContainerEntity;
                                containerToApply.ValidData = true;
                                
                                // Needs to be done via ECB because there's the possibility that initialContainerEntity is an unrealized entity (created in future ecb playback)
                                Ecb.SetComponent(containerToAutoApplyEntity, containerToApply);

                                var requests = Ecb.AddBuffer<DataTransferRequest>(containerToAutoApplyEntity);
                                requests.Add(new DataTransferRequest()
                                {
                                    ExecutingSystem = ExecutingRequestsSystem,
                                    RequestType = DataTransferRequest.Type.FromDataContainerToEntities
                                });
                            }
                            else
                            {
                                Debug.LogWarning("Validation failed on AutoApplyOnLoad container.");
                                containerToApply.ValidData = false;
                                Ecb.SetComponent(containerToAutoApplyEntity, containerToApply);
                            }
                        }
                        else
                        {
                            DataTransferRequestLookup[containerToAutoApplyEntity].Add(new DataTransferRequest()
                            {
                                ExecutingSystem = ExecutingRequestsSystem,
                                RequestType = DataTransferRequest.Type.FromDataContainerToEntities
                            });
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Encountered an AutoApplyOnLoad component that has Entity.Null as its container!");
                    }
                }
            }
        }
        
        private static bool ValidateAndUpgradeContainer(ref QuickSaveDataContainer containerToUpgrade, ref DynamicBuffer<QuickSaveDataContainer.Data> dataToUpgrade, ref DynamicBuffer<QuickSaveArchetypeDataLayout> dataLayoutsToUpgrade,
            in QuickSaveDataContainer initialContainer, in DynamicBuffer<QuickSaveDataContainer.Data> initialData, in DynamicBuffer<QuickSaveArchetypeDataLayout> initialLayouts)
        {
            if (containerToUpgrade.DataLayoutHash != initialContainer.DataLayoutHash)
            {
                Debug.LogError("ValidateAndUpgradeContainer: Container had a different data layout than its initial container!");
                return false;
            }

            if (containerToUpgrade.EntityCapacity != initialContainer.EntityCapacity)
            {
                Debug.LogError("ValidateAndUpgradeContainer: Container had a different amount of entities than its initial container!");
                return false;
            }
                
            if (dataToUpgrade.Length != initialData.Length)
            {
                Debug.LogError("ValidateAndUpgradeContainer: Container had a different amount of bytes than its initial container!");
                return false;
            }

            dataLayoutsToUpgrade.CopyFrom(initialLayouts);
            return true;
        }
        
        private static void CacheArchetypes(ref SystemState state)
        {
            state.EntityManager.CreateArchetype(QuickSaveAPI.InitializedContainerArchetype);
            state.EntityManager.CreateArchetype(QuickSaveAPI.InitializedSerializableContainerArchetype);
            state.EntityManager.CreateArchetype(QuickSaveAPI.UninitializedContainerArchetype);
        }
    }
}