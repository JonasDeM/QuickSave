// Author: Jonas De Maeseneer

using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;

namespace DotsPersistency
{
    [StructLayout(LayoutKind.Explicit)]
    public struct PersistentComponents : ISharedComponentData
    {
        // Todo optimization during runtime we should have mapped these to indices
        [FieldOffset(0)]
        public FixedList64<ulong> TypeHashList;
    }
    
    public struct PersistenceState : IComponentData
    {
        public int ArrayIndex;
    }
    
    public struct PersistenceSystemState : ISystemStateComponentData
    {
    }
}