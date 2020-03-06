using System.Collections.Generic;
using Unity.Entities;

namespace DotsPersistency
{
    public abstract class PersistencyJobSystem : JobComponentSystem
    {
        private Dictionary<ComponentType, EntityQuery> _queryCache = new Dictionary<ComponentType, EntityQuery>(16);
        
        protected void InitializeReadOnly(RuntimePersistableTypesInfo typesInfo)
        {
            _queryCache.Add(ComponentType.ReadOnly<PersistenceState>(), CreatePersistenceEntityQuery());

            foreach (ulong typeHash in typesInfo.StableTypeHashes)
            {
                CacheQuery(ComponentType.ReadOnly(TypeManager.GetTypeIndexFromStableTypeHash(typeHash)));
            }
        }
        
        protected void InitializeReadWrite(RuntimePersistableTypesInfo typesInfo)
        {
            _queryCache.Add(ComponentType.ReadOnly<PersistenceState>(), CreatePersistenceEntityQuery());

            foreach (ulong typeHash in typesInfo.StableTypeHashes)
            {
                CacheQuery(ComponentType.FromTypeIndex(TypeManager.GetTypeIndexFromStableTypeHash(typeHash)));
            }
        }

        internal void CacheQuery(ComponentType type)
        {
            // adding the same one twice errors, use TryCacheQuery if you want it to be silent about adding the same twice
            _queryCache.Add(type, CreatePersistenceEntityQuery(type));
        }

        internal EntityQuery GetCachedQuery(ComponentType persistedType)
        {
            return _queryCache[persistedType];
        }
        
        internal EntityQuery GetCachedGeneralQuery()
        {
            return _queryCache[ComponentType.ReadOnly<PersistenceState>()];
        }
        
        private EntityQuery CreatePersistenceEntityQuery(ComponentType persistedType)
        {
            EntityQueryDesc queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PersistedTypes>(),
                    ComponentType.ReadOnly<PersistenceState>(),
                    ComponentType.ReadOnly<SceneSection>(),
                    persistedType
                },
                Options = EntityQueryOptions.IncludeDisabled
            };

            var query = GetEntityQuery(queryDesc);
            return query;
        }
        
        private EntityQuery CreatePersistenceEntityQuery()
        {
            EntityQueryDesc queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PersistedTypes>(),
                    ComponentType.ReadOnly<PersistenceState>(),
                    ComponentType.ReadOnly<SceneSection>()
                },
                Options = EntityQueryOptions.IncludeDisabled
            };

            var query = GetEntityQuery(queryDesc);
            return query;
        }
    }
}
