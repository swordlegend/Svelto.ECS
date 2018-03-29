﻿using System;
using System.Collections.Generic;
using DBC;
using Svelto.DataStructures;
using Svelto.ECS.Internal;

#if ENGINE_PROFILER_ENABLED && UNITY_EDITOR
using Svelto.ECS.Profiler;
#endif

namespace Svelto.ECS
{
    public partial class EnginesRoot : IDisposable
    {
        public void Dispose()
        {
            foreach (var entity in _entityViewsDB)
                if (entity.Value.isQueryiableEntityView)
                    foreach (var entityView in entity.Value)
                        RemoveEntityViewFromEngines(_entityViewEngines, entityView as EntityView, entity.Key);
        }

        ///--------------------------------------------

        public IEntityFactory GenerateEntityFactory()
        {
            return new GenericEntityFactory(new DataStructures.WeakReference<EnginesRoot>(this));
        }

        public IEntityFunctions GenerateEntityFunctions()
        {
            return new GenericEntityFunctions(new DataStructures.WeakReference<EnginesRoot>(this));
        }

        ///--------------------------------------------

        void BuildEntity<T>(int entityID, object[] implementors = null) where T : IEntityDescriptor, new()
        {
            BuildEntityInGroup<T>
                (entityID, ExclusiveGroups.StandardEntity, implementors);
        }

        void BuildEntity(int entityID, EntityDescriptorInfo entityDescriptor, object[] implementors)
        {
            BuildEntityInGroup
                (entityID, ExclusiveGroups.StandardEntity, entityDescriptor, implementors);
        }

        /// <summary>
        /// Build the entity using the entityID, inside the group with Id groupID, using the
        /// implementors (if necessary). The entityViews generated will be stored to be
        /// added later in the engines. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entityID"></param>
        /// <param name="groupID"></param>
        /// <param name="implementors"></param>
        void BuildEntityInGroup<T>(int entityID, int groupID, object[] implementors = null)
            where T : IEntityDescriptor, new()
        {
            EntityFactory.BuildGroupedEntityViews(entityID, groupID,
                                                  _groupedEntityViewsToAdd.current,
                                                  EntityDescriptorTemplate<T>.Default,
                                                  implementors);
        }

        void BuildEntityInGroup(int      entityID, int groupID, EntityDescriptorInfo entityDescriptor,
                                object[] implementors = null)
        {
            EntityFactory.BuildGroupedEntityViews(entityID, groupID,
                                                  _groupedEntityViewsToAdd.current,
                                                  entityDescriptor, implementors);
        }

        ///--------------------------------------------

        void Preallocate<T>(int groupID, int size) where T : IEntityDescriptor, new()
        {
            var entityViewsToBuild = ((EntityDescriptorInfo) EntityDescriptorTemplate<T>.Default).entityViewsToBuild;
            var count              = entityViewsToBuild.Length;

            for (var index = 0; index < count; index++)
            {
                var entityViewBuilder = entityViewsToBuild[index];
                var entityViewType    = entityViewBuilder.GetEntityViewType();

                //reserve space for the global pool
                ITypeSafeList dbList;
                if (_entityViewsDB.TryGetValue(entityViewType, out dbList) == false)
                    _entityViewsDB[entityViewType] = entityViewBuilder.Preallocate(ref dbList, size);
                else
                    dbList.AddCapacity(size);

                //reserve space for the single group
                Dictionary<Type, ITypeSafeList> @group;
                if (_groupEntityViewsDB.TryGetValue(groupID, out group) == false)
                    group = _groupEntityViewsDB[groupID] = new Dictionary<Type, ITypeSafeList>();
                
                if (group.TryGetValue(entityViewType, out dbList) == false)
                    group[entityViewType] = entityViewBuilder.Preallocate(ref dbList, size);
                else
                    dbList.AddCapacity(size);
                
                if (_groupedEntityViewsToAdd.current.TryGetValue(groupID, out group) == false)
                    group = _groupEntityViewsDB[groupID] = new Dictionary<Type, ITypeSafeList>();
                
                //reserve space to the temporary buffer
                if (group.TryGetValue(entityViewType, out dbList) == false)
                    group[entityViewType] = entityViewBuilder.Preallocate(ref dbList, size);
                else
                    dbList.AddCapacity(size);
            }
        }

        void RemoveEntity(ref EntityInfoView entityInfoView)
        {
            InternalRemoveFromGroupAndDBAndEngines(entityInfoView.entityViews, entityInfoView.ID,
                                                   entityInfoView.groupID);
        }

        void RemoveEntity(int entityID, int groupID)
        {
            var entityInfoView = _DB.QueryEntityViewInGroup<EntityInfoView>(entityID, groupID);

            RemoveEntity(ref entityInfoView);
        }

        void RemoveGroupAndEntitiesFromDB(int groupID)
        {
            foreach (var group in _groupEntityViewsDB[groupID])
            {
                var entityViewType = group.Key;

                int count;
                var entities = group.Value.ToArrayFast(out count);

                for (var i = 0; i < count; i++)
                {
                    var entityID = entities[i].ID;

                    InternalRemoveEntityViewFromDBAndEngines(entityViewType, entityID, groupID);
                }
            }

            _groupEntityViewsDB.Remove(groupID);
        }

        void InternalRemoveEntityViewFromDBAndEngines(Type  entityViewType,
                                                      int   entityID,
                                                      int   groupID)
        {
            var entityViews = _entityViewsDB[entityViewType];
            if (entityViews.MappedRemove(entityID) == false)
                _entityViewsDB.Remove(entityViewType);

            if (entityViews.isQueryiableEntityView)
            {
                var typeSafeDictionary = _groupedEntityViewsDBDic[groupID][entityViewType];
                var entityView         = typeSafeDictionary.GetIndexedEntityView(entityID);

                if (typeSafeDictionary.Remove(entityID) == false)
                    _groupedEntityViewsDBDic[groupID].Remove(entityViewType);

                for (var current = entityViewType; current != _entityViewType; current = current.BaseType)
                    RemoveEntityViewFromEngines(_entityViewEngines, entityView, current);
            }
        }

        void SwapEntityGroup(int entityID, int fromGroupID, int toGroupID)
        {
            Check.Require(fromGroupID != toGroupID,
                          "can't move an entity to the same group where it already belongs to");

            var entityViewBuilders      = _DB.QueryEntityView<EntityInfoView>(entityID).entityViews;
            var entityViewBuildersCount = entityViewBuilders.Length;

            var groupedEntities = _groupEntityViewsDB[fromGroupID];

            Dictionary<Type, ITypeSafeList> groupedEntityViewsTyped;
            if (_groupEntityViewsDB.TryGetValue(toGroupID, out groupedEntityViewsTyped) == false)
            {
                groupedEntityViewsTyped = new Dictionary<Type, ITypeSafeList>();

                _groupEntityViewsDB.Add(toGroupID, groupedEntityViewsTyped);
            }

            for (var i = 0; i < entityViewBuildersCount; i++)
            {
                var entityViewBuilder = entityViewBuilders[i];
                var entityViewType    = entityViewBuilder.GetEntityViewType();

                var           fromSafeList = groupedEntities[entityViewType];
                ITypeSafeList toSafeList;

                if (groupedEntityViewsTyped.TryGetValue(entityViewType, out toSafeList) == false)
                    groupedEntityViewsTyped[entityViewType] = toSafeList = fromSafeList.Create();

                entityViewBuilder.MoveEntityView(entityID, fromSafeList, toSafeList);

                fromSafeList.MappedRemove(entityID);
            }

            var entityInfoView = _DB.QueryEntityView<EntityInfoView>(entityID);
            entityInfoView.groupID = toGroupID;
        }

        void InternalRemoveFromGroupAndDBAndEngines(IEntityViewBuilder[]                  entityViewBuilders,
                                                    int                                   entityID, int groupID)
        {
            InternalRemoveFromGroupDB(entityViewBuilders, entityID, groupID);

            var entityViewBuildersCount = entityViewBuilders.Length;

            for (var i = 0; i < entityViewBuildersCount; i++)
            {
                var entityViewType = entityViewBuilders[i].GetEntityViewType();

                InternalRemoveEntityViewFromDBAndEngines(entityViewType, entityID, groupID);
            }
            
            InternalRemoveEntityViewFromDBAndEngines(typeof(EntityInfoView), entityID, groupID);
        }

        void InternalRemoveFromGroupDB(IEntityViewBuilder[] entityViewBuilders, int entityID, int groupID)
        {
            var entityViewBuildersCount = entityViewBuilders.Length;
            var group = _groupEntityViewsDB[groupID];

            for (var i = 0; i < entityViewBuildersCount; i++)
            {
                var entityViewType = entityViewBuilders[i].GetEntityViewType();
                var typeSafeList = group[entityViewType];
                typeSafeList.MappedRemove(entityID);
            }
        }

        static void RemoveEntityViewFromEngines(Dictionary<Type, FasterList<IHandleEntityViewEngine>> entityViewEngines,
                                                IEntityView                                           entityView,
                                                Type                                                  entityViewType)
        {
            FasterList<IHandleEntityViewEngine> enginesForEntityView;

            if (entityViewEngines.TryGetValue(entityViewType, out enginesForEntityView))
            {
                int count;
                var fastList = FasterList<IHandleEntityViewEngine>.NoVirt.ToArrayFast(enginesForEntityView, out count);

                for (var j = 0; j < count; j++)
                {
#if ENGINE_PROFILER_ENABLED && UNITY_EDITOR
                    EngineProfiler.MonitorRemoveDuration(fastList[j], entityView);
#else
                    fastList[j].Remove(entityView);
#endif
                }
            }
        }

        readonly EntityViewsDB _DB;
        
        //grouped set of entity views, this is the standard way to handle entity views
        readonly Dictionary<int, Dictionary<Type, ITypeSafeList>>         _groupEntityViewsDB;
        
        //indexable entity views when the entity ID is known. Usually useful to handle
        //event based logic.
        readonly Dictionary<int, Dictionary<Type, ITypeSafeDictionary>>   _groupedEntityViewsDBDic;

        //Global pool of entity views when engines want to manage entityViews regardless
        //the group
        readonly Dictionary<Type, ITypeSafeList>                          _entityViewsDB;
    }
}