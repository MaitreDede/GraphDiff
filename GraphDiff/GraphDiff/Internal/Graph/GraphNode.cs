using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RefactorThis.GraphDiff.Internal.Graph
{
    internal class GraphNode
    {       
        protected readonly PropertyInfo Accessor;

        protected string IncludeString
        {
            get
            {
                var ownIncludeString = Accessor != null ? Accessor.Name : null;
                return Parent != null && Parent.IncludeString != null
                        ? Parent.IncludeString + "." + ownIncludeString
                        : ownIncludeString;
            }
        }

        public GraphNode Parent { get; set; }
        public Stack<GraphNode> Members { get; private set; }

        public GraphNode()
        {
            Members = new Stack<GraphNode>();
        }

        protected GraphNode(GraphNode parent, PropertyInfo accessor)
        {
            Accessor = accessor;
            Members = new Stack<GraphNode>();
            Parent = parent;
        }

        // overridden by different implementations
        public virtual void Update<T>(IChangeTracker changeTracker, IEntityManager entityManager, T persisted, T updating) where T : class
        {
            // Foreach branch perform recursive update
            foreach (var member in Members)
            {
                member.Update(changeTracker, entityManager, persisted, updating);
            }

            // KW 04/28/16
            // Originally this code was calling changeTracker.UpdateItem() before recursing to child members.
            // This caused a bug where if you change just an FK ID on the entity, but not the associated nav 
            // property, the update is not saved.

            // The reason is because when UpdateItem() is called and the updated FK ID value is copied over 
            // to the persisted entity, it nulls out the associated nav property (Probably because the FK ID 
            // doesn't match the ID on the nav property.)  So then when the code runs to update child members, 
            // it sees that the child nav property is null on persisted, and is defined on updating, so it registers 
            // that as a change as well.  So there are two changes, the real change (Updated FK ID), and the nav 
            // property change (NULL back to original value).  The nav property change was the one that was winning,
            // resulting in the real intended change not being saved to the database.
            //
            // Updating the child members first, and then updating the entities own properties after seems to
            // fix this problem because the FK ID is changed last.
            changeTracker.UpdateItem(updating, persisted, true);
        }

        public List<string> GetIncludeStrings(IEntityManager entityManager)
        {
            var includeStrings = new List<string>();
            var ownIncludeString = IncludeString;
            if (!string.IsNullOrEmpty(ownIncludeString))
            {
                includeStrings.Add(ownIncludeString);
            }

            includeStrings.AddRange(GetRequiredNavigationPropertyIncludes(entityManager));

            foreach (var member in Members)
            {
                includeStrings.AddRange(member.GetIncludeStrings(entityManager));
            }

            return includeStrings;
        }

        public string GetUniqueKey()
        {
            string key = "";
            if (Parent != null && Parent.Accessor != null)
            {
                key += Parent.Accessor.DeclaringType.FullName + "_" + Parent.Accessor.Name;
            }
            else
            {
                key += "NoParent";
            }
            return key + "_" + Accessor.DeclaringType.FullName + "_" + Accessor.Name;
        }

        protected T GetValue<T>(object instance)
        {
            return (T)Accessor.GetValue(instance, null);
        }

        protected void SetValue(object instance, object value)
        {
            Accessor.SetValue(instance, value, null);
        }

        protected virtual IEnumerable<string> GetRequiredNavigationPropertyIncludes(IEntityManager entityManager)
        {
            return new string[0];
        }

        protected List<string> GetMappedNaviationProperties()
        {
            return Members.Select(m => m.Accessor.Name).ToList();
        }

        protected static IEnumerable<string> GetRequiredNavigationPropertyIncludes(IEntityManager entityManager, Type entityType, string ownIncludeString)
        {
            return entityManager
                .GetRequiredNavigationPropertiesForType(entityType)
                .Select(navigationProperty => ownIncludeString + "." + navigationProperty.Name);
        }

        protected static void ThrowIfCollectionType(PropertyInfo accessor, string mappingType)
        {
            if (IsCollectionType(accessor.PropertyType))
                throw new ArgumentException(string.Format("Collection '{0}' can not be mapped as {1} entity. Please map it as {1} collection.", accessor.Name, mappingType));
        }

        private static bool IsCollectionType(Type propertyType)
        {
            return propertyType.IsArray || propertyType.GetInterface(typeof (IEnumerable<>).FullName) != null;
        }
    }
}
