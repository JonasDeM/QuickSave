// Author: Jonas De Maeseneer

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Scenes;
using UnityEngine;

namespace QuickSave
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    public class QuickSaveBeginFrameSystem : QuickSaveSystemBase
    {
        public override bool HandlesPersistRequests => true;
        public override bool HandlesApplyRequests => true;
        public override EntityCommandBufferSystem EcbSystem => World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
    }
}