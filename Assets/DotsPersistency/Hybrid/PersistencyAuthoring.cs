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

        public FixedList64<ulong> GetFixedTypesToPersistHashes()
        {
            Debug.Assert(TypesToPersistHashes.Count <= 64, "more than 64 persisted types is not supported");
            var retVal = new FixedList64<ulong>();
            foreach (var hash in TypesToPersistHashes)
            {
                if (hash != 0)
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
