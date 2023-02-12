// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace QuickSave.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(QuickSaveSubScene))]
    public class QuickSaveSubSceneInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            List<Hash128> subSceneGUIDS = targets
                .Select(o => ((QuickSaveSubScene)o).GetComponent<SubScene>())
                .Where(comp => comp != null)
                .Select(comp => comp.SceneGUID)
                .ToList();
            if (subSceneGUIDS.Count == 0) 
                return;

            string postFix = subSceneGUIDS.Count > 1 ? $" ({subSceneGUIDS.Count})" : "";
            GUI.enabled = EditorApplication.isPlaying;
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load" + postFix))
            {
                LoadSections(subSceneGUIDS);
            }
            if (GUILayout.Button("Reset To Initial State" + postFix))
            {
                ResetToInitialState(subSceneGUIDS);
            }
            if (GUILayout.Button("Unload" + postFix))
            {
                UnloadSections(subSceneGUIDS);
            }
            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;
        }
        
        private static void LoadSections(List<Hash128> guids)
        {
            foreach (var world in World.All)
            {
                EntityManager entityManager = world.EntityManager;
                var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SceneSectionData>(), ComponentType.Exclude<RequestSceneLoaded>());
                var entities = query.ToEntityArray(Allocator.Temp);
                foreach (Entity entity in entities)
                {
                    Hash128 guid = entityManager.GetComponentData<SceneSectionData>(entity).SceneGUID;
                    if (guids.Contains(guid))
                    {
                        entityManager.AddComponent<RequestSceneLoaded>(entity);
                    }
                }
                entities.Dispose();
            }
        }
        
        private static void ResetToInitialState(List<Hash128> guids)
        {
            foreach (var world in World.All)
            {
                EntityManager entityManager = world.EntityManager;
                var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<QuickSaveDataContainer>(), ComponentType.ReadWrite<DataTransferRequest>());
                var entities = query.ToEntityArray(Allocator.Temp);
                foreach (Entity entity in entities)
                {
                    var container = entityManager.GetComponentData<QuickSaveDataContainer>(entity);
                    if (guids.Contains(container.GUID) && QuickSaveAPI.IsInitialContainer(entity, container))
                    {
                        entityManager.GetBuffer<DataTransferRequest>(entity).Add(new DataTransferRequest()
                        {
                            ExecutingSystem = world.GetOrCreateSystem<QuickSaveBeginFrameSystem>(),
                            RequestType = DataTransferRequest.Type.FromDataContainerToEntities
                        });
                    }
                }
                entities.Dispose();
            }
        }

        private static void UnloadSections(List<Hash128> guids)
        {
            foreach (var world in World.All)
            {
                EntityManager entityManager = world.EntityManager;
                var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SceneSectionData>(), ComponentType.ReadOnly<RequestSceneLoaded>());
                var entities = query.ToEntityArray(Allocator.Temp);
                foreach (Entity entity in entities)
                {
                    Hash128 guid = entityManager.GetComponentData<SceneSectionData>(entity).SceneGUID;
                    if (guids.Contains(guid))
                    {
                        entityManager.AddComponent<RequestSceneUnloaded>(entity);
                    }
                }

                entities.Dispose();
            }
        }
    }
}
