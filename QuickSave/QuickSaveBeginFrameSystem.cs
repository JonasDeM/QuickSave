// Author: Jonas De Maeseneer

using Unity.Entities;
using Unity.Scenes;

namespace QuickSave
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    public partial class QuickSaveBeginFrameSystem : QuickSaveSystemBase
    {
        public override bool HandlesPersistRequests => true;
        public override bool HandlesApplyRequests => true;
        public override EntityCommandBufferSystem EcbSystem => World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>();
    }
}