// Author: Jonas De Maeseneer

using System;
using System.Diagnostics;
using Unity.Entities;

namespace QuickSave
{
    // Info for grabbing the correct sub-array & dataLayout for a chunk
    [Serializable]
    public struct QuickSaveArchetypeIndexInContainer : ISharedComponentData
    {
        public ushort IndexInContainer;
    }
    
    // This component holds the index into the sub array 
    public struct LocalIndexInContainer : IComponentData, IEquatable<LocalIndexInContainer>
    {
        public int LocalIndex;

        public bool Equals(LocalIndexInContainer other)
        {
            return LocalIndex == other.LocalIndex;
        }
    }

    // This struct sits in front of every data block in a persisted data array
    public readonly struct QuickSaveMetaData
    {
        private readonly ushort _data;
        public const int SizeOfStruct = sizeof(ushort);

        public QuickSaveMetaData(int diff, ushort amount, bool enabled)
        {
            CheckMaxAmount(amount);
            _data = amount;
            if (diff != 0)
            {
                _data |= ChangedFlag;
            }
            if (enabled)
            {
                _data |= EnabledFlag;
            }
        }
        
        public QuickSaveMetaData(int diff, ushort amount)
        {
            CheckMaxAmount(amount);
            _data = amount;
            if (diff != 0)
            {
                _data |= ChangedFlag;
            }
            _data |= EnabledFlag;
        }

        public int AmountFound => _data & MaxValueForAmount;
        public bool FoundOne => AmountFound != 0;
        
        public bool HasChanged => (_data & ChangedFlag) != 0;
        private const ushort ChangedFlag = 0b1000000000000000; // 1000 0000 0000 0000
        public bool Enabled => (_data & EnabledFlag) != 0;
        private const ushort EnabledFlag = 0b0100000000000000; // 0100 0000 0000 0000
        
        public const ushort MaxValueForAmount = 0b0011111111111111; // 0011 1111 1111 1111

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckMaxAmount(int amount)
        {
            if (amount > MaxValueForAmount)
            {
                throw new ArgumentException($"QuickSaveMetaData expects the 'amount' parameter to be smaller or equal to {MaxValueForAmount}.");
            }
        }
    }
}
