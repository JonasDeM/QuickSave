// Author: Jonas De Maeseneer

using Unity.Entities;

namespace QuickSave
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class QuickSaveEndFrameSystem  : QuickSaveSystemBase
    {
        public override bool HandlesPersistRequests => true;
        public override bool HandlesApplyRequests => false;
        public override EntityCommandBufferSystem EcbSystem => World.GetOrCreateSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }
}