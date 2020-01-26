// Author: Jonas De Maeseneer

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace DotsPersistency.Hybrid
{
    [ConverterVersion("Jonas", 9)]
    [UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
    public class PersistencyConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring)
                =>
            {
                Entity e = GetPrimaryEntity(persistencyAuthoring);

                FixedList64<ulong> list = new FixedList64<ulong>
                {
                    TypeManager.GetTypeInfo(ComponentType.ReadWrite<Translation>().TypeIndex).StableTypeHash
                };
                
                var componentsToPersist = new PersistentComponents() { TypeHashList = list };
                DstEntityManager.AddSharedComponentData(e, componentsToPersist);
                
                DstEntityManager.AddComponentData(e, new PersistenceState()
                {
                    ArrayIndex = persistencyAuthoring.ArrayIndex
                });
            });
        }
    }
}
