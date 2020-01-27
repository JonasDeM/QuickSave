// Author: Jonas De Maeseneer

using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace DotsPersistency.Hybrid
{
    [ExecuteAlways]
    public class PersistencyAuthoring : MonoBehaviour
    {
        public int ArrayIndex;
        
        public List<ulong> TypesToPersistHashes;

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
        
#if UNITY_EDITOR
        public void Update()
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
                arrayIndex += rootGameObject.GetComponentsInChildren<PersistencyAuthoring>().Length;
            }
            foreach (PersistencyAuthoring child in rootParent.GetComponentsInChildren<PersistencyAuthoring>())
            {
                if (child == this)
                    break;
                arrayIndex += 1;
            }

            if (ArrayIndex != arrayIndex)
            {
                EditorUtility.SetDirty(this);
                ArrayIndex = arrayIndex;
            }
        }
#endif

    }
}
