// Author: Jonas De Maeseneer

using Unity.Entities;
using Unity.Jobs;
using Unity.Scenes;

namespace DotsPersistency
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    public class FromPersistentDataSystem : JobComponentSystem
    {
        public PersistencyManager PersistencyManager;

        protected override void OnCreate()
        {
            PersistencyManager = new PersistencyManager();
        }

        protected override void OnDestroy()
        {
            PersistencyManager.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            return PersistencyManager.ScheduleFromPersistentDataJobs(this, inputDependencies);
        }
    }
}