// Author: Jonas De Maeseneer

using System;
using System.Diagnostics;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

using System.Runtime.CompilerServices;
[assembly:InternalsVisibleTo("com.studioaurelius.quicksave.baking")]

namespace QuickSave
{
    public static class QuickSaveAPI
    {
        public static readonly ComponentType[] InitializedContainerArchetype =
        {
            typeof(QuickSaveDataContainer), typeof(QuickSaveDataContainer.Data), typeof(QuickSaveArchetypeDataLayout),
            typeof(DataTransferRequest)
        };
        public static readonly ComponentType[] InitializedSerializableContainerArchetype =
        {
            typeof(QuickSaveDataContainer), typeof(QuickSaveDataContainer.Data), typeof(QuickSaveArchetypeDataLayout), 
            typeof(DataTransferRequest), typeof(RequestSerialization), typeof(RequestDeserialization)
        };
        public static readonly ComponentType[] UninitializedContainerArchetype =
        {
            typeof(QuickSaveDataContainer), typeof(QuickSaveDataContainer.Data), typeof(QuickSaveArchetypeDataLayout), 
            typeof(RequestDeserialization)
        };
        
        // The reason for not just storing a bool on the container is that this way you can duplicate/instantiate the initial container
        // and the new container won't be the initial one!
        public static bool IsInitialContainer(Entity containerEntity, QuickSaveDataContainer container)
        {
            return containerEntity == container.InitialContainer;
        }
        
        // This creates a duplicate of an existing container & its data
        // This is the main way for users to create containers
        // QuickSaveSceneSystem makes a valid container for each subscene that loads for the very first time,
        // these containers can be safely duplicated after QuickSaveBeginFrameSystem has initialized them with valid 'initial scene state'
        public static Entity InstantiateContainer(EntityManager entityManager, Entity validContainerEntity)
        {
            CheckContainerInitialized(entityManager, validContainerEntity);
            return entityManager.Instantiate(validContainerEntity);
        }
        
        // Ecb variant of InstantiateContainer (doesn't have any checks)
        public static Entity InstantiateContainer(EntityCommandBuffer ecb, Entity validContainerEntity)
        {
            return ecb.Instantiate(validContainerEntity);
        }

        // This is the second way to create a container, but should only be used in cases when no container exists yet for the subscene.
        // Returns an uninitialized container that will only become valid once these 2 things happened in order:
        // 1. Deserialization puts valid data into it.
        // 2. The Scene is loaded and you had this container as the AutoApplyOnLoad.
        public static Entity RequestDeserializeIntoNewUninitializedContainer(EntityManager entityManager, Hash128 sceneGUID, RequestDeserialization request)
        {
            Entity containerEntity = entityManager.CreateEntity(UninitializedContainerArchetype);
            entityManager.SetComponentData(containerEntity, new QuickSaveDataContainer
            {
                GUID = sceneGUID,
            });
            entityManager.SetComponentData(containerEntity, request);
            entityManager.SetName(containerEntity, QuickSaveSettings.GetQuickSaveContainerName());
            return containerEntity;
        }
        
        // Ecb variant of RequestDeserializeIntoNewUninitializedContainer
        public static Entity RequestDeserializeIntoNewUninitializedContainer(EntityCommandBuffer ecb, Hash128 sceneGUID, RequestDeserialization request)
        {
            Entity containerEntity = ecb.CreateEntity();
            ecb.AddComponent(containerEntity, new QuickSaveDataContainer
            {
                GUID = sceneGUID,
            });
            ecb.AddBuffer<QuickSaveDataContainer.Data>(containerEntity);
            ecb.AddBuffer<QuickSaveArchetypeDataLayout>(containerEntity);
            ecb.AddComponent(containerEntity, request);
            ecb.SetName(containerEntity, QuickSaveSettings.GetQuickSaveContainerName());
            return containerEntity;
        }
        
        // CalculatePathString creates a managed string that can be used to find the container its serialized counterpart on disk.
        // It creates the folders if they do not exist yet.
        // It uses a cached StringBuilder for improved performance.
        public static string CalculatePathString(StringBuilder sb, string folderName, Hash128 guid, string postFix = "")
        {
            sb.Clear();
            sb.Append(Application.streamingAssetsPath);
            sb.Append("/QuickSave/");
            sb.Append(string.IsNullOrEmpty(folderName) ? "DefaultSceneStatesFolder" : folderName);
            sb.Append("/");
            sb.Append(guid.ToString());
            if (!string.IsNullOrEmpty(postFix))
            {
                sb.Append("_");
                sb.Append(postFix);
            }
            sb.Append(".qs"); // qs stands for QuickSave :)
            var path = sb.ToString();
            sb.Clear();

            return path;
        }
        
        // Use this to assert an entity is an initialized container
        [Conditional("DEBUG")]
        public static void CheckContainerInitialized(EntityManager entityManager, Entity containerEntity)
        {
            foreach (var componentType in InitializedContainerArchetype)
            {
                CheckEntityHasComponent(entityManager, containerEntity, componentType);
            }
        }
        
        
        // Internal 
        // ********
        
        internal static Entity CreateInitialSceneContainer(EntityManager entityManager, Hash128 guid, ref QuickSaveSceneInfo sceneInfo, out DynamicBuffer<QuickSaveDataContainer.Data> outBuffer)
        {
            var archetype = entityManager.CreateArchetype(InitializedContainerArchetype);
            Entity containerEntity = entityManager.CreateEntity(archetype);

            var layouts = entityManager.GetBuffer<QuickSaveArchetypeDataLayout>(containerEntity);
            DataLayoutHelpers.BuildDataLayouts(ref sceneInfo, layouts);
            
            int amountEntities = 0;
            int containerSize = 0;
            foreach (var layout in layouts)
            {
                amountEntities += layout.Amount;
                containerSize += layout.Amount * layout.SizePerEntity;
            }
            
            entityManager.SetComponentData(containerEntity, new QuickSaveDataContainer()
            {
                GUID = guid,
                DataLayoutHash = sceneInfo.DataLayoutHash,
                EntityCapacity = amountEntities,
                FrameIdentifier = -1,
                InitialContainer = containerEntity
            });
            
            outBuffer = entityManager.GetBuffer<QuickSaveDataContainer.Data>(containerEntity);
            outBuffer.Resize(containerSize, NativeArrayOptions.ClearMemory);
            
            return containerEntity;
        }
        
        internal static Entity CreateInitialSceneContainer(EntityCommandBuffer ecb, Hash128 guid, ref QuickSaveSceneInfo sceneInfo, DataTransferRequest request,
            out QuickSaveDataContainer container, out DynamicBuffer<QuickSaveDataContainer.Data> initialContainerData, out DynamicBuffer<QuickSaveArchetypeDataLayout> layouts)
        {
            Entity containerEntity = ecb.CreateEntity();
            layouts = ecb.AddBuffer<QuickSaveArchetypeDataLayout>(containerEntity);
            DataLayoutHelpers.BuildDataLayouts(ref sceneInfo, layouts);
            int amountEntities = 0;
            int containerSize = 0;
            foreach (var layout in layouts)
            {
                amountEntities += layout.Amount;
                containerSize += layout.Amount * layout.SizePerEntity;
            }

            container = new QuickSaveDataContainer()
            {
                GUID = guid,
                DataLayoutHash = sceneInfo.DataLayoutHash,
                EntityCapacity = amountEntities,
                FrameIdentifier = -1,
                InitialContainer = containerEntity
            };
            ecb.AddComponent(containerEntity, container);
            
            var requestBuffer = ecb.AddBuffer<DataTransferRequest>(containerEntity);
            requestBuffer.Add(request);
            
            initialContainerData = ecb.AddBuffer<QuickSaveDataContainer.Data>(containerEntity);
            initialContainerData.Resize(containerSize, NativeArrayOptions.ClearMemory);

            ecb.SetName(containerEntity, QuickSaveSettings.GetQuickSaveContainerName());

            return containerEntity;
        }
        
        [Conditional("DEBUG")]
        private static void CheckEntityHasComponent(EntityManager entityManager, Entity containerEntity, ComponentType componentType)
        {
            if (!entityManager.HasComponent(containerEntity, componentType))
            {
                throw new ArgumentException($"Expected a valid container as argument, but the entity missed the {componentType.ToString()} component.");
            }
        }
    }
}