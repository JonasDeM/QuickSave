// Author: Jonas De Maeseneer

using System.Collections.Generic;
using UnityEngine;

namespace QuickSave
{
    [DisallowMultipleComponent]
    public class QuickSaveAuthoring : MonoBehaviour
    {
        public string QuickSaveArchetypeName = "";
        public List<string> FullTypeNamesToPersist = new List<string>();
    }
}
