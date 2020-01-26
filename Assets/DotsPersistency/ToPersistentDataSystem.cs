// Author: Jonas De Maeseneer

using Unity.Entities;
using Unity.Jobs;

namespace DotsPersistency
{
    public class ToPersistentDataSystem : JobComponentSystem
    {
        private FromPersistentDataSystem _fromSystem;

        protected override void OnCreate()
        { 
            _fromSystem = World.GetOrCreateSystem<FromPersistentDataSystem>();
        }
    
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            return _fromSystem.PersistencyManager.ScheduleToPersistentDataJobs(this, inputDependencies);
        }
    }
}