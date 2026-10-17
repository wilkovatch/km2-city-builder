using System.Collections.Generic;

namespace History {
    //Stable ids for the city elements. Undo/redo records refer to elements by these ids instead of node references,
    //since the nodes are freed when an element is deleted (and recreated with a new node by undo).
    public static class ElementRegistry {
        public const int CityPropertiesId = 0;
        static int nextId = 1;
        static Dictionary<int, object> byId = new Dictionary<int, object>();
        static Dictionary<object, int> byElement = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        //registers the element (with the given id, when recreating it) and returns its id
        public static int Register(object element, int id = -1) {
            if (element == null) return -1;
            if (byElement.TryGetValue(element, out var existing)) return existing;
            if (id < 0) {
                id = nextId++;
            } else {
                if (byId.TryGetValue(id, out var old) && old != null) byElement.Remove(old);
                if (id >= nextId) nextId = id + 1;
            }
            byId[id] = element;
            byElement[element] = id;
            UndoManager.NotifyCreated(element, id);
            return id;
        }

        public static void Unregister(object element) {
            if (element == null) return;
            if (byElement.TryGetValue(element, out var id)) {
                byElement.Remove(element);
                byId.Remove(id);
            }
        }

        //the new element takes the place of the old one (e.g. when a mesh instance is replaced with a different mesh)
        public static void Transfer(object oldElement, object newElement) {
            if (oldElement == null || newElement == null) return;
            if (!byElement.TryGetValue(oldElement, out var id)) return;
            byElement.Remove(oldElement);
            byId[id] = newElement;
            byElement[newElement] = id;
        }

        public static int GetId(object element) {
            if (element == null) return -1;
            return byElement.TryGetValue(element, out var id) ? id : -1;
        }

        public static bool IsRegistered(object element) {
            return element != null && byElement.ContainsKey(element);
        }

        public static object Get(int id) {
            return byId.TryGetValue(id, out var element) ? element : null;
        }

        public static T Get<T>(int id) where T : class {
            return Get(id) as T;
        }

        public static void Reset() {
            byId.Clear();
            byElement.Clear();
            nextId = 1;
        }
    }
}
