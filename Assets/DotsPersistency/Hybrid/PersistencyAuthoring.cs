// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace DotsPersistency.Hybrid
{
    [RequiresEntityConversion]
    public class PersistencyAuthoring : MonoBehaviour
    {
        public List<ulong> TypesToPersistHashes= new List<ulong>();

        public PersistedTypes GetPersistedTypes()
        {
            return new PersistedTypes()
            {
                ComponentDataTypeHashList = GetComponentDataTypesToPersistHashes(),
                BufferElementTypeHashList = GetBufferDataTypesToPersistHashes()
            };
        }
        
        private FixedList128<ulong> GetComponentDataTypesToPersistHashes()
        {
            var retVal = new FixedList128<ulong>();
            Debug.Assert(TypesToPersistHashes.Count <= retVal.Capacity, $"more than {retVal.Capacity} persisted ComponentData types is not supported");
            foreach (var hash in TypesToPersistHashes)
            {
                if (hash != 0 && TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(hash)).Category == TypeManager.TypeCategory.ComponentData)
                {
                    retVal.Add(hash); 
                }
            }
            return retVal;
        }
        
        private FixedList64<ulong> GetBufferDataTypesToPersistHashes()
        {
            var retVal = new FixedList64<ulong>();
            Debug.Assert(TypesToPersistHashes.Count <= retVal.Capacity, $"more than {retVal.Capacity} persisted BufferData types is not supported");
            foreach (var hash in TypesToPersistHashes)
            {
                if (hash != 0 && TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(hash)).Category == TypeManager.TypeCategory.BufferData) 
                {
                    retVal.Add(hash);
                }
            }
            return retVal;
        }
        
        public int CalculateArrayIndex()
        {
            Transform rootParent = transform;
            while (rootParent.parent)
            {
                rootParent = rootParent.parent;
            }
            int arrayIndex = 0;
            
            foreach (GameObject rootGameObject in gameObject.scene.GetRootGameObjects())
            {
                if (rootGameObject.transform == rootParent)
                    break;
                arrayIndex += rootGameObject.GetComponentsInChildren<PersistencyAuthoring>().Count(comp => TypesToPersistHashes.SequenceEqual(comp.TypesToPersistHashes));
            }
            foreach (PersistencyAuthoring child in rootParent.GetComponentsInChildren<PersistencyAuthoring>())
            {
                if (child == this)
                    break;

                if (TypesToPersistHashes.SequenceEqual(child.TypesToPersistHashes))
                {
                    arrayIndex += 1;
                }
            }
            return arrayIndex;
        }

    }
}
