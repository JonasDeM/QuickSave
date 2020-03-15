// Author: Jonas De Maeseneer

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace DotsPersistency
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public class EndFramePersistentDataSystem : PersistencyJobSystem
    {
        public PersistentDataStorage PersistentDataStorage { get; private set; }
        private EntityQuery _unloadStreamRequests;

        protected override void OnCreate()
        {
            InitializeReadOnly(RuntimePersistableTypesInfo.Load());
            PersistentDataStorage = World.GetOrCreateSystem<BeginFramePersistentDataSystem>().PersistentDataStorage;

            _unloadStreamRequests = GetEntityQuery(ComponentType.Exclude<RequestPersistentSceneLoaded>()
                , ComponentType.ReadOnly<RequestSceneLoaded>(), ComponentType.ReadOnly<SceneSectionData>());
            RequireForUpdate(_unloadStreamRequests);
        }
    
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            JobHandle allPersistJobs = inputDependencies;

            var sceneSectionsToUnload = _unloadStreamRequests.ToComponentDataArray<SceneSectionData>(Allocator.Temp);
            foreach (var sceneSectionData in sceneSectionsToUnload)
            {
                SceneSection sceneSectionToPersist = new SceneSection()
                {
                    Section = sceneSectionData.SubSectionIndex,
                    SceneGUID = sceneSectionData.SceneGUID
                };
                
                JobHandle.CombineDependencies(
                    allPersistJobs,
                    ScheduleCopyToPersistentDataContainer(inputDependencies, sceneSectionToPersist, PersistentDataStorage.GetExistingContainer(sceneSectionToPersist)));
            }
            sceneSectionsToUnload.Dispose();
            
            // this will trigger the actual unload
            EntityManager.RemoveComponent<RequestSceneLoaded>(_unloadStreamRequests);

            return allPersistJobs;
        }
    }
}