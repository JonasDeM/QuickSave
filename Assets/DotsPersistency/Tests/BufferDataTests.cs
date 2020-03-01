// Author: Jonas De Maeseneer

using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

// ReSharper disable AccessToDisposedClosure

namespace DotsPersistency.Tests
{
    [TestFixture]
    class BufferDataTests : EcsTestsFixture
    {
        [Test]
        public void TestReadBufferData([Values(0, 1, 2, 3, 60, 400)] int total)
        {
            CreateEntities(total);

            int maxAmount = 4;
            
            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            
            NativeArray<PersistentDynamicBufferData1> array1Data = new NativeArray<PersistentDynamicBufferData1>(entityAmount1 * maxAmount, Allocator.TempJob);
            NativeArray<PersistentDynamicBufferData2> array2Data = new NativeArray<PersistentDynamicBufferData2>(entityAmount2 * maxAmount, Allocator.TempJob);
            NativeArray<PersistentDynamicBufferData3> array3Data = new NativeArray<PersistentDynamicBufferData3>(entityAmount3 * maxAmount, Allocator.TempJob);
            NativeArray<int> array1Amount = new NativeArray<int>(entityAmount1, Allocator.TempJob);
            NativeArray<int> array2Amount = new NativeArray<int>(entityAmount2, Allocator.TempJob);
            NativeArray<int> array3Amount = new NativeArray<int>(entityAmount3, Allocator.TempJob);
            
            var query1 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData1), typeof(PersistenceState));
            var query2 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData2), typeof(PersistenceState));
            var query3 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData3), typeof(PersistenceState));
            
            // Action
            var job1 = new CopyBufferElementsToByteArray()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array1Data.Reinterpret<byte>(UnsafeUtility.SizeOf<PersistentDynamicBufferData1>()),
                AmountPersisted = array1Amount,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData1)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData1>().ElementSize,
                MaxElements = maxAmount
            }.Schedule(query1);
            
            var job2 = new CopyBufferElementsToByteArray()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array2Data.Reinterpret<byte>(UnsafeUtility.SizeOf<PersistentDynamicBufferData2>()),
                AmountPersisted = array2Amount,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData2)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData2>().ElementSize,
                MaxElements = maxAmount
            }.Schedule(query2);
            
            var job3 = new CopyBufferElementsToByteArray()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array3Data.Reinterpret<byte>(UnsafeUtility.SizeOf<PersistentDynamicBufferData3>()),
                AmountPersisted = array3Amount,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData3)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData3>().ElementSize,
                MaxElements = maxAmount
            }.Schedule(query3);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            // Check Results
            Entities.With(query1).ForEach(entity =>
            {
                var originalData = m_Manager.GetBuffer<PersistentDynamicBufferData1>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                
                Assert.AreEqual(originalData.Length, array1Amount[persistenceState.ArrayIndex], "CopyBufferElementsToByteArray returned the wrong amount persisted.");

                for (var i = 0; i < originalData.Length; i++)
                {
                    if (i < maxAmount)
                    {
                        var originalElement = originalData[i];
                        Assert.AreEqual(originalElement, array1Data[persistenceState.ArrayIndex * maxAmount + i], "Data output by CopyBufferElementsToByteArray does not match data on entity.");
                    }
                }
            });
            
            Entities.With(query2).ForEach(entity =>
            {
                var originalData = m_Manager.GetBuffer<PersistentDynamicBufferData2>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                
                Assert.AreEqual(originalData.Length, array2Amount[persistenceState.ArrayIndex], "CopyBufferElementsToByteArray returned the wrong amount persisted.");

                for (var i = 0; i < originalData.Length; i++)
                {
                    if (i < maxAmount)
                    {
                        var originalElement = originalData[i];
                        Assert.AreEqual(originalElement, array2Data[persistenceState.ArrayIndex * maxAmount + i], "Data output by CopyBufferElementsToByteArray does not match data on entity.");
                    }
                }
            });
            
            Entities.With(query3).ForEach(entity =>
            {
                var originalData = m_Manager.GetBuffer<PersistentDynamicBufferData3>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                
                Assert.AreEqual(Mathf.Clamp(originalData.Length, 0, maxAmount), array3Amount[persistenceState.ArrayIndex], "CopyBufferElementsToByteArray returned the wrong amount persisted.");
                
                for (var i = 0; i < originalData.Length; i++)
                {
                    if (i < maxAmount)
                    {
                        var originalElement = originalData[i];
                        Assert.AreEqual(originalElement, array3Data[persistenceState.ArrayIndex * maxAmount + i], "Data output by CopyBufferElementsToByteArray does not match data on entity.");
                    }
                }
            });
            
            Assert.True((array3Amount.Distinct().Count() == 1 && array3Amount.Distinct().Contains(maxAmount)) || array3Amount.Length == 0, "CopyBufferElementsToByteArray returned the wrong amount persisted.");

            // Cleanup
            array1Data.Dispose();
            array2Data.Dispose();
            array3Data.Dispose();
            array1Amount.Dispose();
            array2Amount.Dispose();
            array3Amount.Dispose();
            m_Manager.DestroyEntity(m_Manager.CreateEntityQuery(typeof(PersistenceState)));
        }
        
        [Test]
        public void TestApplyStoredBufferData([Values(0, 1, 2, 3, 60, 400)] int total)
        {
            CreateEntities(total);

            int maxElements = total + 2;
            int persistedElementAmount = total;
            
            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            
            NativeArray<PersistentDynamicBufferData1> array1Data = new NativeArray<PersistentDynamicBufferData1>(entityAmount1 * maxElements, Allocator.TempJob);
            NativeArray<PersistentDynamicBufferData2> array2Data = new NativeArray<PersistentDynamicBufferData2>(entityAmount2 * maxElements, Allocator.TempJob);
            NativeArray<PersistentDynamicBufferData3> array3Data = new NativeArray<PersistentDynamicBufferData3>(entityAmount3 * maxElements, Allocator.TempJob);
            NativeArray<int> array1Amount = new NativeArray<int>(Enumerable.Repeat(persistedElementAmount, entityAmount1).ToArray(), Allocator.TempJob);
            NativeArray<int> array2Amount = new NativeArray<int>(Enumerable.Repeat(persistedElementAmount, entityAmount2).ToArray(), Allocator.TempJob);
            NativeArray<int> array3Amount = new NativeArray<int>(Enumerable.Repeat(persistedElementAmount, entityAmount3).ToArray(), Allocator.TempJob);

            for (int i = 0; i < array1Data.Length; i++)
            {
                array1Data[i] = new PersistentDynamicBufferData1() {Value = i};
            }
            for (int i = 0; i < array2Data.Length; i++)
            {
                array2Data[i] = new PersistentDynamicBufferData2() {Value = i};
            }
            for (int i = 0; i < array3Data.Length; i++)
            {
                array3Data[i] = new PersistentDynamicBufferData3() {Value = (byte)i};
            }
            
            var query1 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData1), typeof(PersistenceState));
            var query2 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData2), typeof(PersistenceState));
            var query3 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData3), typeof(PersistenceState));
            
            // Action
            var job1 = new CopyByteArrayToBufferElements()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                InputData = array1Data.Reinterpret<byte>(UnsafeUtility.SizeOf<PersistentDynamicBufferData1>()),
                AmountPersisted = array1Amount,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData1)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData1>().ElementSize,
                MaxElements = maxElements
            }.Schedule(query1);
            
            var job2 = new CopyByteArrayToBufferElements()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                InputData = array2Data.Reinterpret<byte>(UnsafeUtility.SizeOf<PersistentDynamicBufferData2>()),
                AmountPersisted = array2Amount,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData2)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData2>().ElementSize,
                MaxElements = maxElements
            }.Schedule(query2);
            
            var job3 = new CopyByteArrayToBufferElements()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                InputData = array3Data.Reinterpret<byte>(UnsafeUtility.SizeOf<PersistentDynamicBufferData3>()),
                AmountPersisted = array3Amount,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData3)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData3>().ElementSize,
                MaxElements = maxElements
            }.Schedule(query3);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            // Check Results
            Entities.With(query1).ForEach(entity =>
            {
                var newData = m_Manager.GetBuffer<PersistentDynamicBufferData1>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                
                Assert.AreEqual(newData.Length, array1Amount[persistenceState.ArrayIndex], "CopyByteArrayToBufferElements made a buffer that was the wrong size.");

                for (var i = 0; i < newData.Length; i++)
                {
                    Assert.AreEqual(array1Data[persistenceState.ArrayIndex * maxElements + i], newData[i], "Data on entity set by CopyBufferElementsToByteArray does not match data in InputArray.");
                }
            });
            
            Entities.With(query2).ForEach(entity =>
            {
                var newData = m_Manager.GetBuffer<PersistentDynamicBufferData2>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                
                Assert.AreEqual(newData.Length, array2Amount[persistenceState.ArrayIndex], "CopyByteArrayToBufferElements made a buffer that was the wrong size.");

                for (var i = 0; i < newData.Length; i++)
                {
                    Assert.AreEqual(array2Data[persistenceState.ArrayIndex * maxElements + i], newData[i], "Data on entity set by CopyBufferElementsToByteArray does not match data in InputArray.");
                }
            });
            
            Entities.With(query3).ForEach(entity =>
            {
                var newData = m_Manager.GetBuffer<PersistentDynamicBufferData3>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                
                Assert.AreEqual(newData.Length, array3Amount[persistenceState.ArrayIndex], "CopyByteArrayToBufferElements made a buffer that was the wrong size.");
                
                for (var i = 0; i < newData.Length; i++)
                {
                    Assert.AreEqual(array3Data[persistenceState.ArrayIndex * maxElements + i], newData[i], "Data on entity set by CopyBufferElementsToByteArray does not match data in InputArray.");
                }
            });

            // Cleanup
            array1Data.Dispose();
            array2Data.Dispose();
            array3Data.Dispose();
            array1Amount.Dispose();
            array2Amount.Dispose();
            array3Amount.Dispose();
            m_Manager.DestroyEntity(m_Manager.CreateEntityQuery(typeof(PersistenceState)));
        }
        
        public Entity CreateEntity<T, U>(int index) 
            where T : struct, IBufferElementData
            where U : struct, IBufferElementData
        {
            var entity = m_Manager.CreateEntity(typeof(T), typeof(U), typeof(PersistenceState));
            m_Manager.SetComponentData(entity, new PersistenceState() {ArrayIndex = index});
            return entity;
        }

        void CreateEntities(int count)
        {
            for (int i = 0; i != count; i++)
            {
                int zeroOneTwo = i % 3;
                int peristenceIndex = i / 3;
                switch (zeroOneTwo)
                {
                    case 0:
                        var e1 = CreateEntity<DynamicBufferData1, PersistentDynamicBufferData1>(peristenceIndex);
                        var buffer1 = m_Manager.GetBuffer<DynamicBufferData1>(e1);
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 1});
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 2});
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 3});
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 4});
                        var buffer1P = m_Manager.GetBuffer<PersistentDynamicBufferData1>(e1);
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 1});
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 2});
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 3});
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 4});
                        break;
                    case 1: 
                        CreateEntity<DynamicBufferData2, PersistentDynamicBufferData2>(peristenceIndex);
                        break;
                    case 2: 
                        var e3 = CreateEntity<DynamicBufferData3, PersistentDynamicBufferData3>(peristenceIndex);
                        var buffer3 = m_Manager.GetBuffer<DynamicBufferData3>(e3);
                        buffer3.Add(new DynamicBufferData3() {Value = 1});
                        buffer3.Add(new DynamicBufferData3() {Value = 2});
                        buffer3.Add(new DynamicBufferData3() {Value = 3});
                        buffer3.Add(new DynamicBufferData3() {Value = 4});
                        buffer3.Add(new DynamicBufferData3() {Value = 5});
                        var buffer3P = m_Manager.GetBuffer<PersistentDynamicBufferData3>(e3);
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 1});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 2});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 3});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 4});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 5});
                        break;
                    default: throw new Exception("zeroOneTwo was not 0, 1 or 2");
                }
            }
        }
        
        [InternalBufferCapacity(2)]
        struct DynamicBufferData1 : IBufferElementData
        {
            public int Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        struct DynamicBufferData2 : IBufferElementData
        {
#pragma warning disable 649
            public float Value;
#pragma warning restore 649
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        struct DynamicBufferData3 : IBufferElementData
        {
            public byte Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        
        [InternalBufferCapacity(2)]
        struct PersistentDynamicBufferData1 : IBufferElementData
        {
            public int Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        struct PersistentDynamicBufferData2 : IBufferElementData
        {
            public float Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        struct PersistentDynamicBufferData3 : IBufferElementData
        {
            public byte Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
    }
}
