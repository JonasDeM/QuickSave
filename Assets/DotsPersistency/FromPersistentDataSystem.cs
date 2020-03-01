// Author: Jonas De Maeseneer

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Scenes;
using UnityEngine;

namespace DotsPersistency
{
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    public class FromPersistentDataSystem : PersistencyJobSystem
    {
        public PersistencyManager PersistencyManager;
        private EntityCommandBufferSystem _ecbSystem;

        protected override void OnCreate()
        {
            PersistencyManager = new PersistencyManager();
            _ecbSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();
        }

        protected override void OnDestroy()
        {
            PersistencyManager.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                return PersistencyManager.ScheduleFromPersistentDataJobs(this, _ecbSystem, inputDependencies);
            }
            return inputDependencies;
        }
    }
}