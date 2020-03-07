// Author: Jonas De Maeseneer

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

namespace DotsPersistency
{
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public class ToPersistentDataSystem : PersistencyJobSystem
    {
        private FromPersistentDataSystem _fromSystem;

        protected override void OnCreate()
        {
            InitializeReadOnly(RuntimePersistableTypesInfo.Load());
            
            _fromSystem = World.GetOrCreateSystem<FromPersistentDataSystem>();
        }
    
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            if (Input.GetKeyDown(KeyCode.W))
            {
                //return _fromSystem.PersistencyManager.ScheduleToPersistentDataJobs(this, inputDependencies);
            }

            if (Input.GetKeyDown(KeyCode.D))
            {
                var ecbSystem = World.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();
                EntityCommandBuffer.Concurrent ecb = ecbSystem.CreateCommandBuffer().ToConcurrent();
                var jobhandle =  Entities.WithAll<PersistenceState>().ForEach((Entity entity, int entityInQueryIndex) =>
                {
                    ecb.AddComponent<Disabled>(entityInQueryIndex, entity);
                    ecb.DestroyEntity(entityInQueryIndex, entity);
                }).Schedule(inputDependencies);
                ecbSystem.AddJobHandleForProducer(jobhandle);
                return jobhandle;
            }
            
            return inputDependencies;
        }
    }
}