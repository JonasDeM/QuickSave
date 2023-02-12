// Author: Jonas De Maeseneer

using System;
using UnityEngine;

namespace QuickSave
{
    [Serializable]
    public struct QuickSaveTypeHandle : IEquatable<QuickSaveTypeHandle>
    {
        [SerializeField]
        private ushort _handle;

        public bool IsValid => _handle > 0;
        internal int IndexForQuickSaveSettings => _handle - 1;

        public static QuickSaveTypeHandle Invalid => default;
        public static int MaxTypes => ushort.MaxValue - 1;

        public QuickSaveTypeHandle(int indexInQuickSaveSettings)
        {
            _handle = (ushort) (indexInQuickSaveSettings + 1);
        }

        public bool Equals(QuickSaveTypeHandle other)
        {
            return _handle == other._handle;
        }
        
        public int CompareTo(QuickSaveTypeHandle other)
        {
            return _handle.CompareTo(other._handle);
        }

        public override bool Equals(object obj)
        {
            return obj is QuickSaveTypeHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _handle.GetHashCode();
        }
    }
}