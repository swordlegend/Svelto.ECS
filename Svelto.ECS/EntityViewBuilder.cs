﻿using System;
using Svelto.ECS.Internal;

namespace Svelto.ECS
{
    public interface IEntityViewBuilder
    {
        void BuildEntityViewAndAddToList(ref ITypeSafeList list, int entityID, out IEntityView entityView);
        ITypeSafeList Preallocate(ref ITypeSafeList list, int size);

        Type GetEntityViewType();
        void MoveEntityView(int entityID, ITypeSafeList fromSafeList, ITypeSafeList toSafeList);
        bool mustBeFilled { get; }
    }

    public struct EntityViewBuilder<EntityViewType> : IEntityViewBuilder where EntityViewType : EntityView, new()
    {
        public void BuildEntityViewAndAddToList(ref ITypeSafeList list, int entityID, out IEntityView entityView)
        {
            if (list == null)
                list = new TypeSafeFasterListForECSForClasses<EntityViewType>();

            var castedList = list as TypeSafeFasterListForECSForClasses<EntityViewType>;

            var lentityView = EntityView<EntityViewType>.BuildEntityView(entityID);

            castedList.Add(lentityView);

            entityView = lentityView;
        }

        public ITypeSafeList Preallocate(ref ITypeSafeList list, int size)
        {
            if (list == null)
                list = new TypeSafeFasterListForECSForClasses<EntityViewType>(size);
            else
                list.AddCapacity(size);

            return list;
        }

        public Type GetEntityViewType()
        {
            return ENTITY_VIEW_TYPE;
        }

        public void MoveEntityView(int entityID, ITypeSafeList fromSafeList, ITypeSafeList toSafeList)
        {
            var fromCastedList = fromSafeList as TypeSafeFasterListForECSForClasses<EntityViewType>;
            var toCastedList = toSafeList as TypeSafeFasterListForECSForClasses<EntityViewType>;

            toCastedList.Add(fromCastedList[fromCastedList.GetIndexFromID(entityID)]);
        }

        public bool mustBeFilled
        {
            get { return true; }
        }

        internal static readonly Type ENTITY_VIEW_TYPE = typeof(EntityViewType);
    }

    public struct EntityViewStructBuilder<EntityViewType> : IEntityViewBuilder where EntityViewType : struct, IEntityStruct
    {
        public void BuildEntityViewAndAddToList(ref ITypeSafeList list, int entityID, out IEntityView entityView)
        {
            var structEntityView = default(EntityViewType);
            structEntityView.ID = entityID;
            
            if (list == null)
                list = new TypeSafeFasterListForECSForStructs<EntityViewType>();

            var castedList = list as TypeSafeFasterListForECSForStructs<EntityViewType>;

            castedList.Add(structEntityView);

            entityView = null;
        }

        public ITypeSafeList Preallocate(ref ITypeSafeList list, int size)
        {
            if (list == null)
                list = new TypeSafeFasterListForECSForStructs<EntityViewType>(size);
            else
                list.AddCapacity(size);

            return list;
        }

        public Type GetEntityViewType()
        {
            return ENTITY_VIEW_TYPE;
        }

        public void MoveEntityView(int entityID, ITypeSafeList fromSafeList, ITypeSafeList toSafeList)
        {
            var fromCastedList = fromSafeList as TypeSafeFasterListForECSForStructs<EntityViewType>;
            var toCastedList = toSafeList as TypeSafeFasterListForECSForStructs<EntityViewType>;

            toCastedList.Add(fromCastedList[fromCastedList.GetIndexFromID(entityID)]);
        }

        public bool mustBeFilled
        {
            get { return false; }
        }

        internal static readonly Type ENTITY_VIEW_TYPE = typeof(EntityViewType);
    }    
}