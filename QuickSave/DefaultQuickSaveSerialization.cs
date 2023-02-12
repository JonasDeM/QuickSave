// Author: Jonas De Maeseneer

using System;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace QuickSave
{
    // Add or Enable this on a QuickSaveContainer to request the data to be written to disk
    public struct RequestSerialization : IComponentData, IEnableableComponent
    {
        public FixedString32Bytes FolderName;
        public FixedString32Bytes FileNamePostfix;
    }
    
    // Add or Enable this on a QuickSaveContainer to request the data to be read from disk
    public struct RequestDeserialization : IComponentData, IEnableableComponent
    {
        public FixedString32Bytes FolderName;
        public FixedString32Bytes FileNamePostfix;
        
        // Any combination of flags is valid
        // But there are 3 common use-cases if you want the data applied asap to a loaded scene
        // common use case 1: No guarantees on whether the scene is loaded or not => Set All Flags
        // common use case 2: The scene is guaranteed to be loaded => Set 'InstantApplyRequest' Flag
        // common use case 3: The scene is guaranteed to NOT be loaded & will NOT be loaded during deserialization => Set 'AddAutoApplyOnLoadToScene' & 'RequestSceneLoaded' Flags
        public ActionFlags PostCompleteActions;
        public Entity PostCompleteActionSceneEntity; // Only required when 'RequestSceneLoaded' or 'AddAutoApplyToScene' is set.
        
        [Flags]
        public enum ActionFlags : byte
        {
            None,
            
            // Will ensure the scene has a RequestSceneLoaded component
            RequestSceneLoaded = 1 << 0,
            
            // Will ensure the scene has an AutoApplyOnLoad component with this deserialized container as the ContainerEntityToApply
            AddAutoApplyOnLoadToScene = 1 << 1,
            
            // Will add a request to the container to apply the data to the entities asap regardless of the scene being loaded or not
            // but only when the container already has the DataTransferRequest buffer (non-validated containers don't have it until they're validated)
            InstantApplyRequest = 1 << 2, 
            
            All = RequestSceneLoaded | AddAutoApplyOnLoadToScene | InstantApplyRequest
        }
    }
    
    [BurstCompile]
    [UpdateAfter(typeof(QuickSaveEndFrameSystem))]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct DefaultQuickSaveSerializationSystem : ISystem
    {
        private ComponentTypeHandle<RequestSerialization> _serializationTypeHandle;
        private ComponentTypeHandle<RequestDeserialization> _deserializationTypeHandle;
        
        private ComponentTypeHandle<QuickSaveDataContainer> _containerTypeHandle;
        private BufferTypeHandle<QuickSaveDataContainer.Data> _dataTypeHandle;
        private EntityTypeHandle _entityTypeHandle;
        
        private EntityQuery _serializeQuery;
        private EntityQuery _deserializeQuery;
        private EntityQuery _noValidationDeserializeQuery;
        private EntityQuery _anyRequest;

        private NativeList<int> _incompleteDeserialization;
        private ComponentLookup<QuickSaveDataContainer> _containerLookup;
        private BufferLookup<QuickSaveDataContainer.Data> _containerDataLookup;
        private ComponentLookup<SceneSectionData> _sceneSectionLookup;
        private BufferLookup<DataTransferRequest> _dataTransferRequestLookup;
        private SystemHandle _quickSaveBeginFrameSystem;
        
        private UnsafeList<CachedRequestContainer> _cachedRequestContainers;
        private JobHandle _fileIOJobHandle;
        
        public void OnCreate(ref SystemState state)
        {
            _serializationTypeHandle = state.GetComponentTypeHandle<RequestSerialization>();
            _deserializationTypeHandle = state.GetComponentTypeHandle<RequestDeserialization>();
            _containerTypeHandle = state.GetComponentTypeHandle<QuickSaveDataContainer>(true);
            _dataTypeHandle = state.GetBufferTypeHandle<QuickSaveDataContainer.Data>(true);
            _entityTypeHandle = state.GetEntityTypeHandle();
            
            _serializeQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RequestSerialization, QuickSaveDataContainer, QuickSaveDataContainer.Data, QuickSaveArchetypeDataLayout>()
                .Build(ref state);
            
            _deserializeQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RequestDeserialization, QuickSaveDataContainer, QuickSaveDataContainer.Data, QuickSaveArchetypeDataLayout>()
                .Build(ref state);

            _anyRequest = new EntityQueryBuilder(Allocator.Temp)
                .WithAny<RequestSerialization, RequestDeserialization>()
                .WithAll<QuickSaveDataContainer, QuickSaveDataContainer.Data, QuickSaveArchetypeDataLayout>()
                .Build(ref state);
            
            state.RequireForUpdate(_anyRequest);

            _containerLookup = state.GetComponentLookup<QuickSaveDataContainer>();
            _containerDataLookup = state.GetBufferLookup<QuickSaveDataContainer.Data>();
            _sceneSectionLookup = state.GetComponentLookup<SceneSectionData>(true);
            _dataTransferRequestLookup = state.GetBufferLookup<DataTransferRequest>(true);
            _quickSaveBeginFrameSystem = state.World.GetOrCreateSystem<QuickSaveBeginFrameSystem>();
            
            // Force the AddReaderWriter to run in OnCreate
            state.GetComponentTypeHandle<BeginInitializationEntityCommandBufferSystem.Singleton>(); 
            
            _incompleteDeserialization = new NativeList<int>(4, Allocator.Persistent);
            _cachedRequestContainers = new UnsafeList<CachedRequestContainer>(4, Allocator.Persistent);
            AddCachedRequestContainer(out _, out _, out _, out _);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _incompleteDeserialization.Dispose();
            foreach (var cachedRequestContainer in _cachedRequestContainers)
            {
                cachedRequestContainer.Dispose();
            }
            _cachedRequestContainers.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Complete deserialize requests from previous frame(s)
            for (int i = _incompleteDeserialization.Length - 1; i >= 0; i--)
            {
                var containerIndex = _incompleteDeserialization[i];
                CachedRequestContainer requestContainer = _cachedRequestContainers[containerIndex];
                if (!requestContainer.AccessJobHandle.IsCompleted) 
                    continue;
                
                requestContainer.AccessJobHandle.Complete();
                
                // Copy the Deserialized data to the actual container
                _containerLookup.Update(ref state);
                _containerDataLookup.Update(ref state);
                _sceneSectionLookup.Update(ref state);
                _dataTransferRequestLookup.Update(ref state);
                var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
                state.Dependency = new CompleteDeserialization()
                {
                    RequestsToComplete = requestContainer.DeserializeRequests.AsArray(),
                    CombinedDeserializedData = requestContainer.CombinedDataToDeserialize.AsArray(),
                    ContainerLookup = _containerLookup,
                    DataLookup = _containerDataLookup,
                    PostCompleteEcb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged),
                    PostCompleteQuickSaveApplySystem = _quickSaveBeginFrameSystem,
                    SceneSectionLookup = _sceneSectionLookup,
                    DataTransferRequestLookup = _dataTransferRequestLookup,
                }.Schedule(state.Dependency);

                SetJobHandleForCachedContainer(containerIndex, state.Dependency, true);
                _incompleteDeserialization.RemoveAtSwapBack(i);
            }
            
            // Check new requests from this frame
            if (_anyRequest.IsEmpty)
                return;
            bool anySerializeRequests = !_serializeQuery.IsEmpty;
            bool anyDeserializeRequests = !_deserializeQuery.IsEmpty;
            
            int indexOfCached = GetOrCreateRequestContainers(out var serializeRequests, out var combinedDataToSerialize, out var deserializeRequests, out var combinedDataToDeserialize);
            
            // Gathering new Requests
            _serializationTypeHandle.Update(ref state);
            _deserializationTypeHandle.Update(ref state);
            _containerTypeHandle.Update(ref state);
            _dataTypeHandle.Update(ref state);
            _entityTypeHandle.Update(ref state);
            state.Dependency = new ConsumeRequests()
            {
                RequestSerializationTypeHandle = _serializationTypeHandle,
                RequestDeserializationTypeHandle = _deserializationTypeHandle,
                
                ContainerTypeHandle = _containerTypeHandle,
                DataTypeHandle = _dataTypeHandle,
                EntityTypeHandle = _entityTypeHandle,
                
                InternalSerializeRequests = serializeRequests,
                CombinedDataToSerialize = combinedDataToSerialize,
                InternalDeserializeRequests = deserializeRequests
            }.Schedule(_anyRequest, state.Dependency);
            
            // File IO Jobs
            _fileIOJobHandle = JobHandle.CombineDependencies(_fileIOJobHandle, state.Dependency);
            if (anySerializeRequests)
            {
                _fileIOJobHandle = new FileIOSerializeJob()
                {
                    InternalSerializeRequests = serializeRequests.AsDeferredJobArray(),
                    CombinedContainerData = combinedDataToSerialize.AsDeferredJobArray()
                }.Schedule(_fileIOJobHandle);
            }
            if (anyDeserializeRequests)
            {
                _fileIOJobHandle = new FileIODeserializeJob()
                {
                    InternalDeserializeRequests = deserializeRequests.AsDeferredJobArray(),
                    CombinedContainerData = combinedDataToDeserialize
                }.Schedule(_fileIOJobHandle);
                
                // In one of the next updates we still need to copy the deserialized data to the container
                _incompleteDeserialization.Add(indexOfCached);
            }

            SetJobHandleForCachedContainer(indexOfCached, _fileIOJobHandle, !anyDeserializeRequests);
        }
        
        private int AddCachedRequestContainer(out NativeList<InternalSerializeRequest> serializeRequests, out NativeList<QuickSaveDataContainer.Data> combinedDataToSerialize,
            out NativeList<InternalDeserializeRequest> deserializeRequests, out NativeList<QuickSaveDataContainer.Data> combinedDataToDeserialize)
        {
            var newEntry = new CachedRequestContainer()
            {
                AccessJobHandle = default,
                CanGetRecycledAfterJob = true,
                SerializeRequests = new NativeList<InternalSerializeRequest>(8, Allocator.Persistent),
                CombinedDataToSerialize = new NativeList<QuickSaveDataContainer.Data>(1024 * 1024, Allocator.Persistent),
                DeserializeRequests = new NativeList<InternalDeserializeRequest>(8, Allocator.Persistent),
                CombinedDataToDeserialize = new NativeList<QuickSaveDataContainer.Data>(1024 * 1024, Allocator.Persistent)
            };
            _cachedRequestContainers.Add(newEntry);
            serializeRequests = newEntry.SerializeRequests;
            combinedDataToSerialize = newEntry.CombinedDataToSerialize;
            deserializeRequests = newEntry.DeserializeRequests;
            combinedDataToDeserialize = newEntry.CombinedDataToDeserialize;
            return _cachedRequestContainers.Length - 1;
        }

        private int GetOrCreateRequestContainers(out NativeList<InternalSerializeRequest> serializeRequests, out NativeList<QuickSaveDataContainer.Data> combinedDataToSerialize,
            out NativeList<InternalDeserializeRequest> deserializeRequests, out NativeList<QuickSaveDataContainer.Data> combinedDataToDeserialize)
        {
            for (var i = 0; i < _cachedRequestContainers.Length; i++)
            {
                var cachedRequest = _cachedRequestContainers[i];
                if (!cachedRequest.CanGetRecycledAfterJob || !cachedRequest.AccessJobHandle.IsCompleted) 
                    continue;
                
                // Found an Available Container
                cachedRequest.AccessJobHandle.Complete();
                
                serializeRequests = cachedRequest.SerializeRequests;
                serializeRequests.Clear();
                combinedDataToSerialize = cachedRequest.CombinedDataToSerialize;
                combinedDataToSerialize.Clear();
                
                deserializeRequests = cachedRequest.DeserializeRequests;
                deserializeRequests.Clear();
                combinedDataToDeserialize = cachedRequest.CombinedDataToDeserialize;
                combinedDataToDeserialize.Clear();
                return i;
            }

            // No Available Container Found
            return AddCachedRequestContainer(out serializeRequests, out combinedDataToSerialize, out deserializeRequests, out combinedDataToDeserialize);
        }

        private void SetJobHandleForCachedContainer(int index, JobHandle jobHandle, bool canGetRecycledAfterJob)
        {
            var temp = _cachedRequestContainers[index];
            temp.AccessJobHandle = jobHandle;
            temp.CanGetRecycledAfterJob = canGetRecycledAfterJob;
            _cachedRequestContainers[index] = temp;
        }

        private struct CachedRequestContainer : IDisposable
        {
            public JobHandle AccessJobHandle;
            public bool CanGetRecycledAfterJob;
            public NativeList<InternalSerializeRequest> SerializeRequests;
            public NativeList<QuickSaveDataContainer.Data> CombinedDataToSerialize;
            public NativeList<InternalDeserializeRequest> DeserializeRequests;
            public NativeList<QuickSaveDataContainer.Data> CombinedDataToDeserialize;

            public void Dispose()
            {
                SerializeRequests.Dispose();
                CombinedDataToSerialize.Dispose();
                DeserializeRequests.Dispose();
                CombinedDataToDeserialize.Dispose();
            }
        }
        
        private struct InternalSerializeRequest
        {
            public RequestSerialization RequestInfo;
            public QuickSaveDataContainer Container;
            public int DataLength;
        }
        
        private struct InternalDeserializeRequest
        {
            public RequestDeserialization RequestInfo;
            public QuickSaveDataContainer Container;
            public bool Validate;
            public int DataLength;
            public Entity ContainerEntity;
        }

        private struct FileIOSerializeJob : IJob
        {
            [ReadOnly]
            public NativeArray<InternalSerializeRequest> InternalSerializeRequests;
            [ReadOnly]
            public NativeArray<QuickSaveDataContainer.Data> CombinedContainerData;
            
            public void Execute()
            {
                int dataOffset = 0;
                for (int i = 0; i < InternalSerializeRequests.Length; i++)
                {
                    var request = InternalSerializeRequests[i];
                    
                    string path = QuickSaveAPI.CalculatePathString(new StringBuilder(128), request.RequestInfo.FolderName.ToString(), request.Container.GUID, request.RequestInfo.FileNamePostfix.ToString());
                    string folderPath = Path.GetDirectoryName(path);
                    if (!Directory.Exists(folderPath))
                        Directory.CreateDirectory(folderPath);
                
                    using (var fileStream = File.OpenWrite(path))
                    {
                        using (BinaryWriter writer = new BinaryWriter(fileStream))
                        {
                            // 4 bytes | frame identifier (frame when data was copied from entities to container)
                            writer.Write(request.Container.FrameIdentifier);
                            // 8 bytes | checksum of container data layout
                            writer.Write(request.Container.DataLayoutHash);
                            // 4 bytes | amount of entities
                            writer.Write(request.Container.EntityCapacity);
                            // 4 bytes | amount of bytes raw data (=N)
                            writer.Write(request.DataLength);
                            // N bytes | raw data
                            writer.Write(CombinedContainerData.GetSubArray(dataOffset, request.DataLength).Reinterpret<byte>().AsReadOnlySpan());
                        }
                    }

                    dataOffset += request.DataLength;
                }
            }
        }

        private struct FileIODeserializeJob : IJob
        {
            public NativeArray<InternalDeserializeRequest> InternalDeserializeRequests;
            public NativeList<QuickSaveDataContainer.Data> CombinedContainerData;
            
            public void Execute()
            {
                for (int i = 0; i < InternalDeserializeRequests.Length; i++)
                { 
                    var request = InternalDeserializeRequests[i];
                    var container = request.Container;
                    string path = QuickSaveAPI.CalculatePathString(new StringBuilder(128), request.RequestInfo.FolderName.ToString(), container.GUID, request.RequestInfo.FileNamePostfix.ToString());
                
                    if (!File.Exists(path))
                    {
                        Debug.LogError("DefaultQuickSaveSerialization: File not found!");
                        return;
                    }
            
                    // Read From Disk
                    using (var fileStream = File.OpenRead(path))
                    {
                        using (BinaryReader reader = new BinaryReader(fileStream))
                        {
                            int frameIdentifier = reader.ReadInt32();
                            ulong dataLayoutHashInFile = reader.ReadUInt64();
                            int amountEntitiesInFile = reader.ReadInt32();
                            int amountBytesOfRawData = reader.ReadInt32();

                            if (request.Validate)
                            {
                                if (container.DataLayoutHash != dataLayoutHashInFile)
                                {
                                    Debug.LogError("DefaultQuickSaveSerialization: Data in file had a different layout than the container!");
                                    return;
                                }

                                if (container.EntityCapacity != amountEntitiesInFile)
                                {
                                    Debug.LogError("DefaultQuickSaveSerialization: Data in file had a different amount of entities than the container!");
                                    return;
                                }

                                if (request.DataLength != amountBytesOfRawData)
                                {
                                    Debug.LogError("DefaultQuickSaveSerialization: Data in file had a different amount of bytes than the container!");
                                    return;
                                }
                            }
                            
                            container.ValidData = request.Validate;
                            container.FrameIdentifier = frameIdentifier;
                            container.DataLayoutHash = dataLayoutHashInFile;
                            container.EntityCapacity = amountEntitiesInFile;
                            request.DataLength = amountBytesOfRawData;

                            request.Container = container;
                            InternalDeserializeRequests[i] = request;

                            int dataOffset = CombinedContainerData.Length;
                            CombinedContainerData.ResizeUninitialized(dataOffset + amountBytesOfRawData);
                            reader.Read(CombinedContainerData.AsArray().GetSubArray(dataOffset, amountBytesOfRawData).Reinterpret<byte>().AsSpan());
                        }
                    }
                }
            }
        }
        
        [BurstCompile]
        private struct CompleteDeserialization : IJob
        {
            [ReadOnly]
            public NativeArray<InternalDeserializeRequest> RequestsToComplete;
            [ReadOnly]
            public NativeArray<QuickSaveDataContainer.Data> CombinedDeserializedData;

            public ComponentLookup<QuickSaveDataContainer> ContainerLookup;
            public BufferLookup<QuickSaveDataContainer.Data> DataLookup;

            public EntityCommandBuffer PostCompleteEcb;
            public SystemHandle PostCompleteQuickSaveApplySystem;
            [ReadOnly]
            public ComponentLookup<SceneSectionData> SceneSectionLookup;
            [ReadOnly]
            public BufferLookup<DataTransferRequest> DataTransferRequestLookup;
            
            public void Execute()
            {
                // Copy the deserialized data to the containers
                int offset = 0;
                for (int i = 0; i < RequestsToComplete.Length; i++)
                {
                    InternalDeserializeRequest request = RequestsToComplete[i];
                    if (ContainerLookup.HasComponent(request.ContainerEntity) && DataLookup.TryGetBuffer(request.ContainerEntity, out var destinationBuffer))
                    {
                        ContainerLookup[request.ContainerEntity] = request.Container; // Set Container Info
                        destinationBuffer.CopyFrom(CombinedDeserializedData.GetSubArray(offset, request.DataLength)); // Set Data
                    }
                    offset += request.DataLength;
                }
                
                // PostCompleteActions
                for (int i = 0; i < RequestsToComplete.Length; i++)
                {
                    InternalDeserializeRequest request = RequestsToComplete[i];
                    RequestDeserialization.ActionFlags actionFlags = request.RequestInfo.PostCompleteActions;
                    Entity sceneEntity = request.RequestInfo.PostCompleteActionSceneEntity;

                    if (sceneEntity != Entity.Null && SceneSectionLookup.HasComponent(sceneEntity))
                    {
                        if ((actionFlags & RequestDeserialization.ActionFlags.RequestSceneLoaded) != 0)
                        {
                            PostCompleteEcb.AddComponent<RequestSceneLoaded>(sceneEntity);
                        }
                        if ((actionFlags & RequestDeserialization.ActionFlags.AddAutoApplyOnLoadToScene) != 0)
                        {
                            PostCompleteEcb.AddComponent(sceneEntity, new AutoApplyOnLoad()
                            {
                                ContainerEntityToApply = request.ContainerEntity
                            });
                        }
                    }
                    
                    if ((actionFlags & RequestDeserialization.ActionFlags.InstantApplyRequest) != 0
                        && DataTransferRequestLookup.HasBuffer(request.ContainerEntity))
                    {
                        PostCompleteEcb.AppendToBuffer(request.ContainerEntity, new DataTransferRequest
                        {
                            RequestType = DataTransferRequest.Type.FromDataContainerToEntities,
                            ExecutingSystem = PostCompleteQuickSaveApplySystem
                        });
                    }
                }
            }
        }

        [BurstCompile]
        private struct ConsumeRequests : IJobChunk
        {
            public ComponentTypeHandle<RequestSerialization> RequestSerializationTypeHandle;
            public ComponentTypeHandle<RequestDeserialization> RequestDeserializationTypeHandle;
            
            [ReadOnly]
            public ComponentTypeHandle<QuickSaveDataContainer> ContainerTypeHandle;
            [ReadOnly]
            public BufferTypeHandle<QuickSaveDataContainer.Data> DataTypeHandle;
            [ReadOnly]
            public EntityTypeHandle EntityTypeHandle;
            
            [WriteOnly]
            public NativeList<InternalSerializeRequest> InternalSerializeRequests;
            [WriteOnly]
            public NativeList<QuickSaveDataContainer.Data> CombinedDataToSerialize;
            
            [WriteOnly]
            public NativeList<InternalDeserializeRequest> InternalDeserializeRequests;
            
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var containerArray = chunk.GetNativeArray(ref ContainerTypeHandle);
                var dataBuffers = chunk.GetBufferAccessor(ref DataTypeHandle);
                
                // Consume Serialize Requests
                if (chunk.Has(ref RequestSerializationTypeHandle))
                {
                    var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                    var requests = chunk.GetNativeArray(ref RequestSerializationTypeHandle);
                    var enabledMask = chunk.GetEnabledMask(ref RequestSerializationTypeHandle);
                    
                    while (enumerator.NextEntityIndex(out int nextIndex))
                    {
                        enabledMask[nextIndex] = false;

                        var container = containerArray[nextIndex];
                        if (!container.ValidData)
                        {
                            Debug.LogWarning("SerializationRequest on a container with .ValidData set to false. (Removing Request)");
                            continue;
                        }

                        var dataBuffer = dataBuffers[nextIndex];
                        InternalSerializeRequests.Add(new InternalSerializeRequest()
                        {
                            RequestInfo = requests[nextIndex],
                            Container = container,
                            DataLength = dataBuffer.Length
                        });
                
                        CombinedDataToSerialize.AddRange(dataBuffer.AsNativeArray());
                    }
                }
                
                // Consume Deserialize Requests
                if (chunk.Has(ref RequestDeserializationTypeHandle))
                {
                    var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                    var requests = chunk.GetNativeArray(ref RequestDeserializationTypeHandle);
                    var enabledMask = chunk.GetEnabledMask(ref RequestDeserializationTypeHandle);
                    var entities = chunk.GetNativeArray(EntityTypeHandle);
                    
                    while (enumerator.NextEntityIndex(out int nextIndex))
                    {
                        enabledMask[nextIndex] = false;
                        
                        InternalDeserializeRequests.Add(new InternalDeserializeRequest()
                        {
                            RequestInfo = requests[nextIndex],
                            Container = containerArray[nextIndex],
                            Validate = chunk.Has<DataTransferRequest>(),
                            DataLength = dataBuffers[nextIndex].Length,
                            ContainerEntity = entities[nextIndex]
                        });
                    }
                }
            }
        }
    }
}

