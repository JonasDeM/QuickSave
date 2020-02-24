// Author: Jonas De Maeseneer

using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace DotsPersistency.Hybrid
{
    [ConverterVersion("Jonas", 9)]
    [UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
    public class PersistencyConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            int amount = 0;
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring)
                =>
            {
                Entity e = GetPrimaryEntity(persistencyAuthoring);
                
                DstEntityManager.AddSharedComponentData(e, persistencyAuthoring.GetPersistedTypes());
                
                DstEntityManager.AddComponentData(e, new PersistenceState()
                {
                    ArrayIndex = persistencyAuthoring.CalculateArrayIndex()
                });

                amount++;
            });
            
            Debug.Log("Amount persisted entities: " + amount + "\nLast converted: " + DateTime.Now);
        }
    }
}
