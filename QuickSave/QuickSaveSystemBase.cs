// Author: Jonas De Maeseneer

using System.Diagnostics;
using QuickSave.Containers;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Hash128 = Unity.Entities.Hash128;

namespace QuickSave
{
    // Inheriting from QuickSaveSystemBase is the easiest way to create a new QuickSaveSystem.
    // The second way is to create your own ISystem & use QuickSaveSystemState directly.
    // Most of QuickSaveSystemBase its Update is BurstCompiled so the benefit of creating a custom ISystem is quite low.
    [BurstCompile]
    public abstract unsafe partial class QuickSaveSystemBase : SystemBase
    {
        public abstract bool HandlesPersistRequests { get; } // Return true if you will request FromEntitiesToDataContainer data requests for this system
        public abstract bool HandlesApplyRequests { get; }   // Return true if you will request FromDataContainerToEntities data requests for this system
        public abstract EntityCommandBufferSystem EcbSystem { get; }
        protected EntityCommandBufferSystem CachedEcbSystem;

        private QuickSaveSystemState _quickSaveSystemState;
        
        protected override void OnCreate()
        {
            CachedEcbSystem = EcbSystem;
            _quickSaveSystemState.OnCreate(ref CheckedStateRef, HandlesPersistRequests, HandlesApplyRequests);
        }

        protected override void OnUpdate()
        {
            JobHandle jobHandleForProducer = default;
            BurstUpdate((QuickSaveSystemState*)UnsafeUtility.AddressOf(ref _quickSaveSystemState), ref CheckedStateRef, ref CachedEcbSystem.GetPendingBuffersRef(), ref jobHandleForProducer);
            CachedEcbSystem.AddJobHandleForProducer(jobHandleForProducer);
        }

        protected override void OnDestroy()
        {
            _quickSaveSystemState.OnDestroy(ref CheckedStateRef);
        }

        [BurstCompile]
        private static void BurstUpdate(QuickSaveSystemState* quickSaveSystemState, ref SystemState systemState, ref UnsafeList<EntityCommandBuffer> ecbs, ref JobHandle jobHandleForProducer)
        {
            quickSaveSystemState->OnUpdate(ref systemState, ref ecbs, out jobHandleForProducer);
        }
    }
    
    // QuickSaveSystemState can be reused inside multiple systems (both SystemBase & ISystem)
    // OnUpdate is BurstCompatible, so be sure to BurstCompile it!
    public struct QuickSaveSystemState
    {
        public bool HandlesPersistRequests => _handlesPersistRequests;
        private bool _handlesPersistRequests; // Set this to true if you will request FromEntitiesToDataContainer data requests for this system
        public bool HandlesApplyRequests => _handlesApplyRequests; 
        private bool _handlesApplyRequests; // Set this to true if you will request FromDataContainerToEntities data requests for this system
        
        // User Entities
        public EntityQuery PersistableEntitiesQuery;
        public ComponentTypeHandle<LocalIndexInContainer> LocalIndexTypeHandle;
        public SharedComponentTypeHandle<QuickSaveArchetypeIndexInContainer> QuickSaveArchetypeIndexInContainerHandle;
        public EntityTypeHandle EntityTypeHandle;
        
        // Container Lookups
        public BufferLookup<QuickSaveArchetypeDataLayout> DataLayoutLookup;
        public BufferLookup<QuickSaveDataContainer.Data> DataReadLookup;
        public BufferLookup<QuickSaveDataContainer.Data> DataReadWriteLookup;

        // Request Gathering
        public EntityQuery DataTransferRequestsQuery;
        public ComponentTypeHandle<QuickSaveDataContainer> ContainerTypeHandle;
        public BufferTypeHandle<QuickSaveArchetypeDataLayout> LayoutTypeHandle;
        public BufferTypeHandle<DataTransferRequest> DataTransferRequestTypeHandle;
        // containers that could be temp, but I prefer Persistent+Clean() since they keep their capacity
        private NativeList<DataTransferRequestInternal> _persistRequests;
        private NativeList<DataTransferRequestInternal> _applyRequests;
        private NativeList<QuickSaveTypeHandle> _persistableTypeHandles;
        private NativeHashSet<Hash128> _allApplyRequestGUIDs;
        private NativeHashSet<QuickSaveTypeHandle> _currentRequestUniqueTypeHandles;
        
        // profiling
        ProfilerMarker _gatherPfm;
        ProfilerMarker _persistPfm;
        ProfilerMarker _applyPfm;

        public void OnCreate(ref SystemState state, bool handlesPersistRequests, bool handlesApplyRequests)
        {
            QuickSaveSettings.Initialize();
            if (QuickSaveSettings.NothingToSave)
            {
                state.Enabled = false;
            }
            
            if (!handlesPersistRequests && !handlesApplyRequests)
            {
                Debug.LogWarning($"Both handlesPersistRequests & handlesApplyRequests were false! This system will never do anything then!");
                state.Enabled = false;
                return;
            }
            _handlesPersistRequests = handlesPersistRequests;
            _handlesApplyRequests = handlesApplyRequests;

            PersistableEntitiesQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<QuickSaveArchetypeIndexInContainer, LocalIndexInContainer, SceneSection>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
            LocalIndexTypeHandle  = state.GetComponentTypeHandle<LocalIndexInContainer>(true);
            QuickSaveArchetypeIndexInContainerHandle = state.GetSharedComponentTypeHandle<QuickSaveArchetypeIndexInContainer>();
            EntityTypeHandle = state.GetEntityTypeHandle();
            
            DataTransferRequestsQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<QuickSaveArchetypeDataLayout, QuickSaveDataContainer>()
                .WithAllRW<DataTransferRequest>()
                .Build(ref state);
            ContainerTypeHandle = state.GetComponentTypeHandle<QuickSaveDataContainer>();
            LayoutTypeHandle = state.GetBufferTypeHandle<QuickSaveArchetypeDataLayout>(true);
            DataTransferRequestTypeHandle = state.GetBufferTypeHandle<DataTransferRequest>();
            _persistRequests = new NativeList<DataTransferRequestInternal>(32, Allocator.Persistent);
            _applyRequests = new NativeList<DataTransferRequestInternal>(32, Allocator.Persistent);
            _persistableTypeHandles = new NativeList<QuickSaveTypeHandle>(32, Allocator.Persistent);
            _allApplyRequestGUIDs = new NativeHashSet<Hash128>(32, Allocator.Persistent);
            _currentRequestUniqueTypeHandles = new NativeHashSet<QuickSaveTypeHandle>(32, Allocator.Persistent);

            DataLayoutLookup = state.GetBufferLookup<QuickSaveArchetypeDataLayout>(true);
            if (HandlesPersistRequests)
                DataReadWriteLookup = state.GetBufferLookup<QuickSaveDataContainer.Data>();
            if (HandlesApplyRequests)
                DataReadLookup = state.GetBufferLookup<QuickSaveDataContainer.Data>(true);
            
            state.RequireForUpdate(DataTransferRequestsQuery);

            AddAllReaderWriters(ref state, readOnly: !_handlesApplyRequests);
            
            // profiling
            _gatherPfm = new ProfilerMarker(nameof(GatherDataTransferRequests));
            _persistPfm = new ProfilerMarker($"Schedule {nameof(DataTransferRequest.Type.FromEntitiesToDataContainer)}");
            _applyPfm = new ProfilerMarker($"Schedule {nameof(DataTransferRequest.Type.FromDataContainerToEntities)}");
        }
        
        public void OnUpdate(ref SystemState state, ref UnsafeList<EntityCommandBuffer> ecbs, out JobHandle jobHandleForProducer)
        {
            jobHandleForProducer = default;
            
            // This polls every frame to check if the user requested an action.
            _gatherPfm.Begin();
            EntityTypeHandle.Update(ref state);
            ContainerTypeHandle.Update(ref state);
            LayoutTypeHandle.Update(ref state);
            DataTransferRequestTypeHandle.Update(ref state);
            DataTransferRequestsQuery.GetDependency().Complete();
            new GatherDataTransferRequests
            {
                EntityTypeHandle = EntityTypeHandle,
                ContainerTypeHandle = ContainerTypeHandle,
                LayoutTypeHandle = LayoutTypeHandle,
                RequestTypeHandle = DataTransferRequestTypeHandle,
                SystemHandle = state.SystemHandle,
                CurrentFrameIdentifier = Time.frameCount,
                OutPersistRequests = _persistRequests,
                OutApplyRequests = _applyRequests,
                PersistableTypeHandles = _persistableTypeHandles,
                AllApplyRequestGUIDs = _allApplyRequestGUIDs,
                CurrentRequestUniqueTypeHandles = _currentRequestUniqueTypeHandles
            }.Run(DataTransferRequestsQuery);
            _allApplyRequestGUIDs.Clear();
            _currentRequestUniqueTypeHandles.Clear();
            _gatherPfm.End();

            if (_persistRequests.Length + _applyRequests.Length == 0)
                return;
            
            // Act on FromEntitiesToDataContainer Requests
            if (HandlesPersistRequests && !_persistRequests.IsEmpty)
            {
                DataReadWriteLookup.Update(ref state);
                DataLayoutLookup.Update(ref state);
                LocalIndexTypeHandle.Update(ref state);
                QuickSaveArchetypeIndexInContainerHandle.Update(ref state);
                
                NativeArray<JobHandle> jobHandles = new NativeArray<JobHandle>(_persistRequests.Length, Allocator.Temp);
                JobHandle inputDependencies = state.Dependency;

                for (var i = 0; i < _persistRequests.Length; i++)
                {
                    _persistPfm.Begin();
                    var request = _persistRequests[i];
                    var uniqueTypeHandles = _persistableTypeHandles.AsArray().GetSubArray(request.UniqueTypeHandlesFirstIndex, request.UniqueTypeHandlesAmount);
                    var persistDep = SchedulePersist(ref state, inputDependencies, request, uniqueTypeHandles);
                    jobHandles[i] = persistDep;
                    _persistPfm.End();
                }
                state.Dependency = JobHandle.CombineDependencies(jobHandles);
                _persistRequests.Clear();
            }
            else
            {
                SafetyChecks.AssertPersistRequestsEmpty(_persistRequests, ref state);
            }

            // Act on FromDataContainerToEntities Requests
            if (HandlesApplyRequests && !_applyRequests.IsEmpty)
            {
                DataReadLookup.Update(ref state);
                DataLayoutLookup.Update(ref state);
                LocalIndexTypeHandle.Update(ref state);
                QuickSaveArchetypeIndexInContainerHandle.Update(ref state);
                
                NativeArray<JobHandle> jobHandles = new NativeArray<JobHandle>(_applyRequests.Length, Allocator.Temp);
                JobHandle inputDependencies = state.Dependency;

                for (var i = 0; i < _applyRequests.Length; i++)
                {
                    _applyPfm.Begin();
                    var request = _applyRequests[i];
                    var uniqueTypeHandles = _persistableTypeHandles.AsArray().GetSubArray(request.UniqueTypeHandlesFirstIndex, request.UniqueTypeHandlesAmount);
                    var applyDep = ScheduleApply(ref state, inputDependencies, request, uniqueTypeHandles, ref ecbs);
                    jobHandles[i] = applyDep;
                    _applyPfm.End();
                }
                state.Dependency = JobHandle.CombineDependencies(jobHandles);
                jobHandleForProducer = state.Dependency;
                _applyRequests.Clear();
            }
            else
            {
                SafetyChecks.AssertApplyRequestsEmpty(_applyRequests, ref state);
            }
            
            _persistableTypeHandles.Clear();
        }
        
        public void OnDestroy(ref SystemState state)
        {
            QuickSaveSettings.CleanUp();
            
            _persistRequests.Dispose();
            _applyRequests.Dispose();
            _persistableTypeHandles.Dispose();
            _allApplyRequestGUIDs.Dispose();
            _currentRequestUniqueTypeHandles.Dispose();
        }
        
        private JobHandle SchedulePersist(ref SystemState state, JobHandle inputDeps, DataTransferRequestInternal request, NativeArray<QuickSaveTypeHandle> uniqueTypeHandles)
        {
            if (QuickSaveSettings.UseGroupedJobs())
                return SchedulePersistGroupedJob(ref state, inputDeps, request, uniqueTypeHandles);
            else
                return SchedulePersistJobs(ref state, inputDeps, request);
        }

        private JobHandle ScheduleApply(ref SystemState state, JobHandle inputDeps, DataTransferRequestInternal request,
            NativeArray<QuickSaveTypeHandle> uniqueTypeHandles, ref UnsafeList<EntityCommandBuffer> ecbs)
        {
            if (QuickSaveSettings.UseGroupedJobs())
                return ScheduleApplyGroupedJob(ref state, inputDeps, request, uniqueTypeHandles, ref ecbs);
            else
                return ScheduleApplyJobs(ref state, inputDeps, request, ref ecbs);
        }
        
        private JobHandle SchedulePersistJobs(ref SystemState state, JobHandle inputDeps, DataTransferRequestInternal request)
        {
            var returnJobHandle = inputDeps;
            
            DynamicBuffer<QuickSaveArchetypeDataLayout> dataLayouts = state.EntityManager.GetBuffer<QuickSaveArchetypeDataLayout>(request.ContainerEntity);

            for (int archetypeIndex = 0; archetypeIndex < dataLayouts.Length; archetypeIndex++)
            {
                QuickSaveArchetypeDataLayout dataLayout = dataLayouts[archetypeIndex];
                ref BlobArray<QuickSaveArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.TypeInfoArrayRef.Value;
                
                SceneSection sceneSection = new SceneSection {SceneGUID = request.SceneGUID, Section = 0};
                QuickSaveArchetypeIndexInContainer quickSaveArchetypeIndexInContainer = new QuickSaveArchetypeIndexInContainer
                { 
                    IndexInContainer = (ushort)archetypeIndex
                };
                
                for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
                {
                    // type info
                    QuickSaveArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                    ComponentType runtimeType = ComponentType.ReadOnly(QuickSaveSettings.GetTypeIndex(typeInfo.QuickSaveTypeHandle));
                    int stride = typeInfo.ElementSize * typeInfo.MaxElements + QuickSaveMetaData.SizeOfStruct;
                    int byteSize = dataLayout.Amount * stride;
                    
                    // query
                    var query = PersistableEntitiesQuery;
                    query.SetSharedComponentFilter(sceneSection, quickSaveArchetypeIndexInContainer);

                    JobHandle jobHandle;
                    if (typeInfo.IsBuffer)
                    {
                        jobHandle = new CopyBufferElementsToByteArray
                        {
                            BufferTypeHandle = state.GetDynamicComponentTypeHandle(runtimeType),
                            MaxElements = typeInfo.MaxElements,
                            LocalIndexInContainerType = LocalIndexTypeHandle,
                            ContainerEntity = request.ContainerEntity,
                            ByteArrayLookup = DataReadWriteLookup,
                            SubArrayOffset = dataLayout.Offset + typeInfo.Offset,
                            SubArrayByteSize = byteSize
                        }.Schedule(query, inputDeps);
                    }
                    else
                    {
                        jobHandle = new CopyComponentDataToByteArray()
                        {
                            ComponentTypeHandle = state.GetDynamicComponentTypeHandle(runtimeType),
                            TypeSize = typeInfo.ElementSize,
                            LocalIndexInContainerType = LocalIndexTypeHandle,
                            ContainerEntity = request.ContainerEntity,
                            ByteArrayLookup = DataReadWriteLookup,
                            SubArrayOffset = dataLayout.Offset + typeInfo.Offset,
                            SubArrayByteSize = byteSize
                        }.Schedule(query, inputDeps);
                    }
                    
                    query.ResetFilter();
                    returnJobHandle = JobHandle.CombineDependencies(returnJobHandle, jobHandle);
                }
            }

            return returnJobHandle;
        }
        
        private JobHandle ScheduleApplyJobs(ref SystemState state, JobHandle inputDeps, DataTransferRequestInternal request,
            ref UnsafeList<EntityCommandBuffer> ecbs)
        {
            var returnJobHandle = inputDeps;
            
            DynamicBuffer<QuickSaveArchetypeDataLayout> dataLayouts = state.EntityManager.GetBuffer<QuickSaveArchetypeDataLayout>(request.ContainerEntity);

            for (int archetypeIndex = 0; archetypeIndex < dataLayouts.Length; archetypeIndex++)
            {
                QuickSaveArchetypeDataLayout dataLayout = dataLayouts[archetypeIndex];
                ref BlobArray<QuickSaveArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.TypeInfoArrayRef.Value;
                
                SceneSection sceneSection = new SceneSection {SceneGUID = request.SceneGUID, Section = 0};
                QuickSaveArchetypeIndexInContainer quickSaveArchetypeIndexInContainer = new QuickSaveArchetypeIndexInContainer
                {
                    IndexInContainer = (ushort)archetypeIndex
                };
                
                for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
                {
                    // type info
                    QuickSaveArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                    ComponentType runtimeType = ComponentType.ReadWrite(QuickSaveSettings.GetTypeIndex(typeInfo.QuickSaveTypeHandle));
                    int stride = typeInfo.ElementSize * typeInfo.MaxElements + QuickSaveMetaData.SizeOfStruct;
                    int byteSize = dataLayout.Amount * stride;

                    // queries
                    var query = PersistableEntitiesQuery;
                    query.SetSharedComponentFilter(sceneSection, quickSaveArchetypeIndexInContainer);
                    
                    JobHandle jobHandle;
                    if (typeInfo.IsBuffer)
                    {
                        jobHandle = new CopyByteArrayToBufferElements()
                        {
                            BufferTypeHandle = state.GetDynamicComponentTypeHandle(runtimeType),
                            MaxElements = typeInfo.MaxElements,
                            LocalIndexInContainerType = LocalIndexTypeHandle,
                            ContainerEntity = request.ContainerEntity,
                            ByteArrayLookup = DataReadLookup,
                            SubArrayOffset = dataLayout.Offset + typeInfo.Offset,
                            SubArrayByteSize = byteSize
                        }.Schedule(query, inputDeps);
                    }
                    else
                    {
                        jobHandle = new CopyByteArrayToComponentData()
                        {
                            ComponentTypeHandle = state.GetDynamicComponentTypeHandle(runtimeType),
                            ComponentType = runtimeType,
                            TypeSize = typeInfo.ElementSize,
                            LocalIndexInContainerType = LocalIndexTypeHandle,
                            EntityType = EntityTypeHandle,
                            ContainerEntity = request.ContainerEntity,
                            ByteArrayLookup = DataReadLookup,
                            SubArrayOffset = dataLayout.Offset + typeInfo.Offset,
                            SubArrayByteSize = byteSize,
                            Ecb = EntityCommandBufferSystem.CreateCommandBuffer(ref ecbs, state.WorldUnmanaged).AsParallelWriter()
                        }.Schedule(query, inputDeps);
                    }
                    
                    query.ResetFilter();
                    returnJobHandle = JobHandle.CombineDependencies(returnJobHandle, jobHandle);
                }
            }

            return returnJobHandle;
        }
        
        private JobHandle SchedulePersistGroupedJob(ref SystemState state, JobHandle inputDeps, DataTransferRequestInternal request, NativeArray<QuickSaveTypeHandle> uniqueTypeHandles)
        {
            SceneSection sceneSection = new SceneSection {SceneGUID = request.SceneGUID, Section = 0};
            PersistableEntitiesQuery.SetSharedComponentFilter(sceneSection);

            // always temp job allocations
            ComponentTypeHandleArray componentTypeHandles = CreateComponentTypeHandleArray(ref state, uniqueTypeHandles, true);

            inputDeps = new GroupedPersistJob()
            {
                ContainerEntity = request.ContainerEntity,
                ByteArrayLookup = DataReadWriteLookup,
                DataLayoutLookup = DataLayoutLookup,
                LocalIndexInContainerTypeHandle = LocalIndexTypeHandle,
                QuickSaveArchetypeIndexInContainerHandle = QuickSaveArchetypeIndexInContainerHandle,
                DynamicComponentTypeHandles = componentTypeHandles,
            }.ScheduleParallel(PersistableEntitiesQuery, inputDeps);

            PersistableEntitiesQuery.ResetFilter();
            return inputDeps;
        }

        private JobHandle ScheduleApplyGroupedJob(ref SystemState state, JobHandle inputDeps, DataTransferRequestInternal request,
            NativeArray<QuickSaveTypeHandle> uniqueTypeHandles, ref UnsafeList<EntityCommandBuffer> ecbs)
        {
            SceneSection sceneSection = new SceneSection {SceneGUID = request.SceneGUID, Section = 0};
            PersistableEntitiesQuery.SetSharedComponentFilter(sceneSection);

            // always temp job allocations
            ComponentTypeHandleArray componentTypeHandles = CreateComponentTypeHandleArray(ref state, uniqueTypeHandles);

            inputDeps = new GroupedApplyJob()
            {
                ContainerEntity = request.ContainerEntity,
                ByteArrayLookup = DataReadLookup,
                DataLayoutLookup = DataLayoutLookup,
                EntityTypeHandle = EntityTypeHandle,
                LocalIndexInContainerTypeHandle = LocalIndexTypeHandle,
                QuickSaveArchetypeIndexInContainerHandle = QuickSaveArchetypeIndexInContainerHandle,
                DynamicComponentTypeHandles = componentTypeHandles,
                Ecb = EntityCommandBufferSystem.CreateCommandBuffer(ref ecbs, state.WorldUnmanaged).AsParallelWriter()
            }.ScheduleParallel(PersistableEntitiesQuery, inputDeps);

            PersistableEntitiesQuery.ResetFilter();
            return inputDeps;
        }

        private ComponentTypeHandleArray CreateComponentTypeHandleArray(ref SystemState state, NativeArray<QuickSaveTypeHandle> uniqueTypeHandles, bool readOnly = false)
        {
            ComponentTypeHandleArray array = new ComponentTypeHandleArray(uniqueTypeHandles.Length, Allocator.TempJob);
            for (int i = 0; i < uniqueTypeHandles.Length; i++)
            {
                TypeIndex typeIndex = QuickSaveSettings.GetTypeIndex(uniqueTypeHandles[i]);
                ComponentType componentType = readOnly ? ComponentType.ReadOnly(typeIndex) : ComponentType.ReadWrite(typeIndex);
                
                var typeHandle = state.GetDynamicComponentTypeHandle(componentType);
                array[i] = typeHandle;
            }

            return array;
        }

        private void AddAllReaderWriters(ref SystemState state, bool readOnly)
        {
            // This forces the initial sync point (caused by new AddReaderWriter) to happen in OnCreate
            foreach (TypeIndex typeIndex in QuickSaveSettings.GetAllTypeIndices())
            {
                ComponentType componentType = readOnly ? ComponentType.ReadOnly(typeIndex) : ComponentType.ReadWrite(typeIndex);
                state.GetDynamicComponentTypeHandle(componentType);
            }
        }
        
        private struct DataTransferRequestInternal
        {
            public Entity ContainerEntity;
            public Hash128 SceneGUID;
            public int UniqueTypeHandlesFirstIndex;
            public int UniqueTypeHandlesAmount;
        }
        
        [BurstCompile] // TODO: Since this runs on the main thread, this is a good optimization target
        private struct GatherDataTransferRequests : IJobChunk
        {
            // IJobChunk Implementation
            [ReadOnly]
            public EntityTypeHandle EntityTypeHandle;
            public BufferTypeHandle<DataTransferRequest> RequestTypeHandle;
            [ReadOnly]
            public BufferTypeHandle<QuickSaveArchetypeDataLayout> LayoutTypeHandle;
            public ComponentTypeHandle<QuickSaveDataContainer> ContainerTypeHandle;
            
            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityTypeHandle);
                var requestBuffers = chunk.GetBufferAccessor(ref RequestTypeHandle);
                var layoutBuffers = chunk.GetBufferAccessor(ref LayoutTypeHandle);
                var containers = chunk.GetNativeArray(ref ContainerTypeHandle);
                
                for (int i = 0; i < entities.Length; i++)
                {
                    ref var containerRef = ref UnsafeUtility.ArrayElementAsRef<QuickSaveDataContainer>(containers.GetUnsafePtr(), i);
                    Execute(entities[i], requestBuffers[i], layoutBuffers[i], ref containerRef);
                }
            }
            
            // IJobEntity Implementation (This struct used to be an IJobEntity, but methods that schedule IJobEntity are not reusable)
            public SystemHandle SystemHandle;
            public int CurrentFrameIdentifier;
            
            public NativeList<DataTransferRequestInternal> OutPersistRequests;
            public NativeList<DataTransferRequestInternal> OutApplyRequests;
            public NativeList<QuickSaveTypeHandle> PersistableTypeHandles;
            
            public NativeHashSet<Hash128> AllApplyRequestGUIDs;
            public NativeHashSet<QuickSaveTypeHandle> CurrentRequestUniqueTypeHandles;

            public void Execute(Entity e, DynamicBuffer<DataTransferRequest> requests, DynamicBuffer<QuickSaveArchetypeDataLayout> dataLayouts, ref QuickSaveDataContainer container)
            {
                // Find amount valid requests
                int foundType = -1;
                
                for (int i = requests.Length - 1; i >= 0; i--)
                {
                    DataTransferRequest request = requests[i];
                    if (request.ExecutingSystem != SystemHandle)
                        continue;

                    if (foundType != -1)
                    {
                        Debug.LogWarning("Multiple DataTransferRequests on the same container for the same system are not allowed! (Removing request)");
                    }
                    else
                    {
                        if (request.RequestType == DataTransferRequest.Type.FromEntitiesToDataContainer)
                        {
                            foundType = (int)request.RequestType;
                        }
                        if (request.RequestType == DataTransferRequest.Type.FromDataContainerToEntities)
                        {
                            if (AllApplyRequestGUIDs.Contains(container.GUID))
                            {
                                Debug.LogWarning("Multiple DataTransferRequests (Type:FromDataContainerToEntities = Apply) on different containers, but same GUID, for the same system is not allowed! (Removing request)");
                            }
                            else if (!container.ValidData)
                            {
                                Debug.LogWarning("DataTransferRequests (Type:FromDataContainerToEntities = Apply) on a container that had .ValidData set to false. (Removing request)");
                            }
                            else
                            {
                                AllApplyRequestGUIDs.Add(container.GUID);
                                foundType = (int)request.RequestType;
                            }
                        }
                    }
                    requests.RemoveAtSwapBack(i);
                }

                if (foundType == -1)
                    return;
                
                // Create struct with all info necessary to schedule the jobs
                DataTransferRequestInternal dataTransferInfo = new DataTransferRequestInternal()
                {
                    ContainerEntity = e,
                    SceneGUID = container.GUID,
                    UniqueTypeHandlesFirstIndex = PersistableTypeHandles.Length,
                };
                
                CurrentRequestUniqueTypeHandles.Clear();
                for (int i = 0; i < dataLayouts.Length; i++)
                {
                    var dataLayout = dataLayouts[i];
                    ref var typeInfoArray = ref dataLayout.TypeInfoArrayRef.Value;
                    for (int j = 0; j < typeInfoArray.Length; j++)
                    {
                        var typeHandle = typeInfoArray[j].QuickSaveTypeHandle;
                        if (CurrentRequestUniqueTypeHandles.Contains(typeHandle))
                            continue;

                        CurrentRequestUniqueTypeHandles.Add(typeHandle);
                        PersistableTypeHandles.Add(typeHandle);
                    }
                }
                dataTransferInfo.UniqueTypeHandlesAmount = PersistableTypeHandles.Length - dataTransferInfo.UniqueTypeHandlesFirstIndex;
                
                // Add the internal requests to the list
                if (foundType == (int)DataTransferRequest.Type.FromEntitiesToDataContainer)
                {
                    container.ValidData = true;
                    container.FrameIdentifier = CurrentFrameIdentifier;
                    OutPersistRequests.Add(dataTransferInfo);
                }
                else if (foundType == (int)DataTransferRequest.Type.FromDataContainerToEntities)
                {
                    OutApplyRequests.Add(dataTransferInfo);
                }
            }
        }
        
        private static class SafetyChecks
        {
            [Conditional("DEBUG")]
            public static void AssertApplyRequestsEmpty(NativeList<DataTransferRequestInternal> applyRequests, ref SystemState systemState)
            {
                if (!applyRequests.IsEmpty)
                {
                    Debug.LogError($"Received ApplyRequest(s) but is set to not handle them! (Either change request type or {nameof(HandlesApplyRequests)} property on the system)");
                }
            }
            
            [Conditional("DEBUG")]
            public static void AssertPersistRequestsEmpty(NativeList<DataTransferRequestInternal> persistRequests, ref SystemState systemState)
            {
                if (!persistRequests.IsEmpty)
                {
                    Debug.LogError($"Received PersistRequest(s) but is set to not handle them! (Either change request type or {nameof(HandlesPersistRequests)} property on the system)");
                }
            }
        }
    }
}
