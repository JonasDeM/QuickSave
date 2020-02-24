using System;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Internal.Commands;
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

            Entities.With(query3).ForEach(entity =>
            {
                if (m_Manager.GetComponentData<PersistenceState>(entity).ArrayIndex < 3)
                {
                    m_Manager.DestroyEntity(entity);
                }
            });

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
            
            Assert.False(array1IntFound.Contains(false), "1 or more entities were not found for persisting");
            Assert.False(array2FloatFound.Contains(false), "1 or more entities were not found for persisting");
            Assert.True(array5IntFound.Contains(false), "All values in the found array were true but some entities did not exist.");

            // Cleanup
            array1IntData.Dispose();
            array1IntFound.Dispose();
            array2FloatData.Dispose();
            array2FloatFound.Dispose();
            array5IntData.Dispose();
            array5IntFound.Dispose();
            m_Manager.DestroyEntity(m_Manager.CreateEntityQuery(typeof(PersistenceState)));
        }
        
        [Test]
        public void TestApplyStoredData()
        {
            // Preparation
            int total = 60;
            CreateEntities(total);
            
            NativeArray<EcsPersistingTestData> array1IntData = new NativeArray<EcsPersistingTestData>(total / 3, Allocator.TempJob);
            NativeArray<bool> array1IntFound = new NativeArray<bool>(Enumerable.Repeat(true, array1IntData.Length).ToArray(), Allocator.TempJob);
            NativeArray<EcsPersistingFloatTestData2> array2FloatData = new NativeArray<EcsPersistingFloatTestData2>(total / 3, Allocator.TempJob);
            NativeArray<bool> array2FloatFound = new NativeArray<bool>(Enumerable.Repeat(true, array2FloatData.Length).ToArray(), Allocator.TempJob);
            NativeArray<EcsPersistingTestData5> array5IntData = new NativeArray<EcsPersistingTestData5>(total / 3, Allocator.TempJob);
            NativeArray<bool> array5IntFound = new NativeArray<bool>(Enumerable.Repeat(true, array5IntData.Length).ToArray(), Allocator.TempJob);

            var query1 = m_Manager.CreateEntityQuery(typeof(EcsPersistingTestData), typeof(PersistenceState));
            var query2 = m_Manager.CreateEntityQuery(typeof(EcsPersistingFloatTestData2), typeof(PersistenceState));
            var query3 = m_Manager.CreateEntityQuery(typeof(EcsPersistingTestData5), typeof(PersistenceState));

            Entities.WithAll<PersistenceState>().ForEach(entity =>
            {
                int index = m_Manager.GetComponentData<PersistenceState>(entity).ArrayIndex;
                if (index < 3)
                {
                    m_Manager.DestroyEntity(entity);
                }
                else if (m_Manager.HasComponent<EcsPersistingTestData>(entity))
                {
                    array1IntData[index] = m_Manager.GetComponentData<EcsPersistingTestData>(entity).Modified();
                }
                else if (m_Manager.HasComponent<EcsPersistingFloatTestData2>(entity))
                {
                    array2FloatData[index] = m_Manager.GetComponentData<EcsPersistingFloatTestData2>(entity).Modified();
                }
                else if (m_Manager.HasComponent<EcsPersistingTestData5>(entity))
                {
                    array5IntData[index] = m_Manager.GetComponentData<EcsPersistingTestData5>(entity).Modified();
                }
            });
            int amount1 = query1.CalculateEntityCount();
            int amount2 = query2.CalculateEntityCount();
            int amount3 = query3.CalculateEntityCount();

            // Action
            new CopyByteArrayToComponentData()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingTestData)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData>(),
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                Input = array1IntData.Reinterpret<byte>(UnsafeUtility.SizeOf<EcsPersistingTestData>())
            }.Run(query1);
            
            new CopyByteArrayToComponentData()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingFloatTestData2)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>(),
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                Input = array2FloatData.Reinterpret<byte>(UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>())
            }.Run(query2);

            new CopyByteArrayToComponentData()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingTestData5)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData5>(),
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                Input = array5IntData.Reinterpret<byte>(UnsafeUtility.SizeOf<EcsPersistingTestData5>())
            }.Run(query3);

            // Check Results
            Entities.With(query1).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingTestData>(entity);
                var originalData = new EcsPersistingTestData() { data = m_Manager.GetComponentData<EcsTestData>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "CopyByteArrayToComponentData:: Restored data on entity is incorrect.");
            });
            
            Entities.With(query2).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingFloatTestData2>(entity);
                var originalData = new EcsPersistingFloatTestData2() { data = m_Manager.GetComponentData<EcsTestFloatData2>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "CopyByteArrayToComponentData:: Restored data on entity is incorrect.");
            });
            
            Entities.With(query3).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingTestData5>(entity);
                var originalData = new EcsPersistingTestData5() { data = m_Manager.GetComponentData<EcsTestData5>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "CopyByteArrayToComponentData:: Restored data on entity is incorrect.");
            });
            
            // Preparation
            Entities.WithAll<PersistenceState>().ForEach(entity =>
            {
                int index = m_Manager.GetComponentData<PersistenceState>(entity).ArrayIndex;
                if (index % 2 == 1)
                {
                    if (m_Manager.HasComponent<EcsPersistingTestData>(entity))
                    {
                        m_Manager.RemoveComponent<EcsPersistingTestData>(entity);
                    }
                    else if (m_Manager.HasComponent<EcsPersistingFloatTestData2>(entity))
                    {
                        m_Manager.RemoveComponent<EcsPersistingFloatTestData2>(entity);
                    }
                    else if (m_Manager.HasComponent<EcsPersistingTestData5>(entity))
                    {
                        m_Manager.RemoveComponent<EcsPersistingTestData5>(entity);
                    }
                }
            });

            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                new AddMissingComponents()
                {
                    ComponentType = typeof(EcsPersistingTestData),
                    TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData>(),
                    EntityType = m_Manager.GetArchetypeChunkEntityType(),
                    PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                    InputData = array1IntData.Reinterpret<byte>(UnsafeUtility.SizeOf<EcsPersistingTestData>()),
                    InputFound = array1IntFound,
                    Ecb = cmds.ToConcurrent()
                }.Run(m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(PersistenceState)));
                cmds.Playback(m_Manager);
            }

            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                new AddMissingComponents()
                {
                    ComponentType = typeof(EcsPersistingFloatTestData2),
                    TypeSize = UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>(),
                    EntityType = m_Manager.GetArchetypeChunkEntityType(),
                    PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                    InputData = array2FloatData.Reinterpret<byte>(UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>()),
                    InputFound = array2FloatFound,
                    Ecb = cmds.ToConcurrent()
                }.Run(m_Manager.CreateEntityQuery(typeof(EcsTestFloatData2), typeof(PersistenceState)));
                cmds.Playback(m_Manager);
            }

            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                new AddMissingComponents()
                {
                    ComponentType = typeof(EcsPersistingTestData5),
                    TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData5>(),
                    EntityType = m_Manager.GetArchetypeChunkEntityType(),
                    PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                    InputData = array5IntData.Reinterpret<byte>(UnsafeUtility.SizeOf<EcsPersistingTestData5>()),
                    InputFound = array5IntFound,
                    Ecb = cmds.ToConcurrent()
                }.Run(m_Manager.CreateEntityQuery(typeof(EcsTestData5), typeof(PersistenceState)));
                cmds.Playback(m_Manager);
            }

            // Check Results
            Assert.AreEqual(amount1, query1.CalculateEntityCount(), "AddMissingComponents:: Not all missing components have not been restored");
            Entities.With(query1).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingTestData>(entity);
                var originalData = new EcsPersistingTestData() { data = m_Manager.GetComponentData<EcsTestData>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "AddMissingComponents:: Restored data on entity is incorrect.");
            });
            
            Assert.AreEqual(amount2, query2.CalculateEntityCount(), "AddMissingComponents:: Not all missing components have not been restored");
            Entities.With(query2).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingFloatTestData2>(entity);
                var originalData = new EcsPersistingFloatTestData2() { data = m_Manager.GetComponentData<EcsTestFloatData2>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "AddMissingComponents:: Restored data on entity is incorrect.");
            });
            
            Assert.AreEqual(amount3, query3.CalculateEntityCount(), "AddMissingComponents:: Not all missing components have not been restored");
            Entities.With(query3).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingTestData5>(entity);
                var originalData = new EcsPersistingTestData5() { data = m_Manager.GetComponentData<EcsTestData5>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "AddMissingComponents:: Restored data on entity is incorrect.");
            });

            // Cleanup
            array1IntData.Dispose();
            array1IntFound.Dispose();
            array2FloatData.Dispose();
            array2FloatFound.Dispose();
            array5IntData.Dispose();
            array5IntFound.Dispose();
            m_Manager.DestroyEntity(m_Manager.CreateEntityQuery(typeof(PersistenceState)));
        }
        
        
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
            
            public EcsPersistingTestData Modified()
            {
                return new EcsPersistingTestData()
                {
                    data = new EcsTestData(data.value * data.value * data.value)
                };
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
            
            public EcsPersistingFloatTestData2 Modified()
            {
                return new EcsPersistingFloatTestData2()
                {
                    data = new EcsTestFloatData2() { Value0 = data.Value0 + 1.0f, Value1 = data.Value1 + 1.0f}
                };
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

            public EcsPersistingTestData5 Modified()
            {
                return new EcsPersistingTestData5()
                {
                    data = new EcsTestData5(data.value0 * data.value0)
                };
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
    }
}
