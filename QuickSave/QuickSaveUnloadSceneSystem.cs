// Author: Jonas De Maeseneer

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace QuickSave
{
    [BurstCompile]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial struct QuickSaveUnloadSceneSystem  : ISystem
    {
        private BufferLookup<DataTransferRequest> _requestLookup;
        
        private SystemHandle _quickSaveSystemHandle;
        private EntityQuery _autoPersistRequests;
        
        private ComponentTypeSet _typesToRemoveForUnload;
        private EntityQuery _unloadRequests;
        
        public void OnCreate(ref SystemState state)
        {
            _requestLookup = state.GetBufferLookup<DataTransferRequest>();
            
            _quickSaveSystemHandle = state.World.GetOrCreateSystem<QuickSaveEndFrameSystem>();
            _autoPersistRequests = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SceneSectionData, RequestSceneLoaded, QuickSaveSceneSection, RequestSceneUnloaded, AutoPersistOnUnload>()
                .Build(ref state);
            
            _typesToRemoveForUnload = new ComponentTypeSet(typeof(RequestSceneLoaded), typeof(RequestSceneUnloaded));
            _unloadRequests = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RequestSceneUnloaded>()
                .Build(ref state);
        }

        public void OnDestroy(ref SystemState state) { }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!_autoPersistRequests.IsEmpty)
            {
                _requestLookup.Update(ref state);
                new AutoPersistRequestJob
                {
                    DataTransferRequestLookup = _requestLookup,
                    ExecutingSystem = _quickSaveSystemHandle
                }.Schedule(_autoPersistRequests);
            }

            if (!_unloadRequests.IsEmpty)
            {
                // this will trigger the actual unload
                var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
                EntityCommandBuffer ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);
                ecb.RemoveComponent(_unloadRequests, _typesToRemoveForUnload, EntityQueryCaptureMode.AtPlayback);
            }
        }
        
        [BurstCompile]
        public partial struct AutoPersistRequestJob : IJobEntity
        {
            public BufferLookup<DataTransferRequest> DataTransferRequestLookup;
            public SystemHandle ExecutingSystem;
            
            public void Execute(in AutoPersistOnUnload autoPersistOnUnload)
            {
                if (DataTransferRequestLookup.HasBuffer(autoPersistOnUnload.ContainerEntityToPersist))
                {
                    var buffer = DataTransferRequestLookup[autoPersistOnUnload.ContainerEntityToPersist];
                    buffer.Add(new DataTransferRequest
                    {
                        ExecutingSystem = ExecutingSystem,
                        RequestType = DataTransferRequest.Type.FromEntitiesToDataContainer
                    });
                }
            }
        }
    }
}