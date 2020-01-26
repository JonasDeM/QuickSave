// Author: Jonas De Maeseneer

using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace DotsPersistency.Hybrid
{
    [ExecuteAlways]
    public class PersistencyAuthoring : MonoBehaviour
    {
        public FixedList64<int> TypesToPersistHashes;
        public int ArrayIndex;

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
