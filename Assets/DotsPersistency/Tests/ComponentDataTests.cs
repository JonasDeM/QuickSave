using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Tests;
using UnityEngine;
// ReSharper disable AccessToDisposedClosure

namespace DotsPersistency.Tests
{
    [TestFixture]
    class ComponentDataTests : ECSTestsFixture
    {
        struct EcsPersistingTestData : IComponentData, IEquatable<EcsTestData>, IEquatable<EcsPersistingTestData>
        {
            public EcsTestData data;
            public bool Equals(EcsTestData other)
            {
                return data.Equals(other);
            }

            public bool Equals(EcsPersistingTestData other)
            {
                return data.Equals(other.data);
            }
        }
        
        struct EcsPersistingFloatTestData2 : IComponentData, IEquatable<EcsTestFloatData2>, IEquatable<EcsPersistingFloatTestData2>
        {
            public EcsTestFloatData2 data;

            public bool Equals(EcsTestFloatData2 other)
            {
                return data.Equals(other);
            }

            public bool Equals(EcsPersistingFloatTestData2 other)
            {
                return data.Equals(other.data);
            }
        }
        
        struct EcsPersistingTestData5 : IComponentData, IEquatable<EcsTestData5>, IEquatable<EcsPersistingTestData5>
        {
            public EcsTestData5 data;

            public bool Equals(EcsTestData5 other)
            {
                return data.Equals(other);
            }

            public bool Equals(EcsPersistingTestData5 other)
            {
                return data.Equals(other.data);
            }
        }
        
        public Entity CreateEntity<T, U>(int index, T value, U value2) 
            where T : struct, IComponentData
            where U : struct, IComponentData
        {
            var entity = m_Manager.CreateEntity(typeof(T), typeof(U), typeof(PersistenceState));
            m_Manager.SetComponentData<T>(entity, value);
            m_Manager.SetComponentData<U>(entity, value2);
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
                        var ecsData = new EcsTestData(i);
                        CreateEntity(peristenceIndex, ecsData, new EcsPersistingTestData() { data = ecsData});
                        break;
                    case 1: 
                        var ecsData2 = new EcsTestFloatData2() {Value0 = i * 1.1111f, Value1 = i * 7.7777f};
                        CreateEntity(peristenceIndex, ecsData2, new EcsPersistingFloatTestData2() { data = ecsData2});
                        break;
                    case 2: 
                        var ecsData5 = new EcsTestData5(i * i);
                        CreateEntity(peristenceIndex, ecsData5, new EcsPersistingTestData5() { data = ecsData5});
                        break;
                    default: throw new Exception("zeroOneTwo was not 0, 1 or 2");
                }
            }
        }

        [Test]
        public void TestReadComponentData()
        {
            // Preparation
            int total = 60;
            CreateEntities(total);
            
            NativeArray<EcsPersistingTestData> array1IntData = new NativeArray<EcsPersistingTestData>(total / 3, Allocator.TempJob);
            NativeArray<bool> array1IntFound = new NativeArray<bool>( total / 3, Allocator.TempJob);
            NativeArray<EcsPersistingFloatTestData2> array2FloatData = new NativeArray<EcsPersistingFloatTestData2>(total / 3, Allocator.TempJob);
            NativeArray<bool> array2FloatFound = new NativeArray<bool>(total / 3, Allocator.TempJob);
            NativeArray<EcsPersistingTestData5> array5IntData = new NativeArray<EcsPersistingTestData5>(total / 3, Allocator.TempJob);
            NativeArray<bool> array5IntFound = new NativeArray<bool>(total / 3, Allocator.TempJob);

            var query1 = m_Manager.CreateEntityQuery(typeof(EcsPersistingTestData), typeof(PersistenceState));
            var query2 = m_Manager.CreateEntityQuery(typeof(EcsPersistingFloatTestData2), typeof(PersistenceState));
            var query3 = m_Manager.CreateEntityQuery(typeof(EcsPersistingTestData5), typeof(PersistenceState));

            // Action
            new CopyComponentDataToByteArray()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingTestData)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData>(),
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array1IntData.Reinterpret<byte>(UnsafeUtility.SizeOf<EcsPersistingTestData>()),
                OutputFound = array1IntFound
            }.Run(query1);
            
            new CopyComponentDataToByteArray()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingFloatTestData2)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() ,
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array2FloatData.Reinterpret<byte>(UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>()),
                OutputFound = array2FloatFound
            }.Run(query2);
            
            new CopyComponentDataToByteArray()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingTestData5)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData5>() ,
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array5IntData.Reinterpret<byte>(UnsafeUtility.SizeOf<EcsPersistingTestData5>()),
                OutputFound = array5IntFound
            }.Run(query3);
            
            // Check Results
            Entities.With(query1).ForEach(entity =>
            {
                var originalData = m_Manager.GetComponentData<EcsPersistingTestData>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                Assert.True(originalData.Equals(array1IntData[persistenceState.ArrayIndex]), "Data output by CopyComponentDataToByteArray does not match data on entity.");
            });
            
            Entities.With(query2).ForEach(entity =>
            {
                var originalData = m_Manager.GetComponentData<EcsPersistingFloatTestData2>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                Assert.True(originalData.Equals(array2FloatData[persistenceState.ArrayIndex]), "Data output by CopyComponentDataToByteArray does not match data on entity.");
            });
            
            Entities.With(query3).ForEach(entity =>
            {
                var originalData = m_Manager.GetComponentData<EcsPersistingTestData5>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                Assert.True(originalData.Equals(array5IntData[persistenceState.ArrayIndex]), "Data output by CopyComponentDataToByteArray does not match data on entity.");
            });

            // Cleanup
            array1IntData.Dispose();
            array1IntFound.Dispose();
            array2FloatData.Dispose();
            array2FloatFound.Dispose();
            array5IntData.Dispose();
            array5IntFound.Dispose();
        }
        
        [Test]
        public void TestApplyStoredData()
        {
            
        }
    }
}
