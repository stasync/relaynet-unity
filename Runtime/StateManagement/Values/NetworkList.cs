using Core.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LROTech.Network.StateManagement.Values;

namespace LROTech.Network.StateManagement
{
    public class SerializableList<TValue> : SerializableValueBase<List<TValue>, IReadOnlyList<TValue>>, IList<TValue>, ICollectionWithCount
    {
        public SerializableList(Func<NetReader, bool, TValue> valueReader, Action<NetWriter, TValue> valueWriter) :
            this(new List<TValue>(), valueReader, valueWriter)
        {
        }

        public SerializableList(List<TValue> defaultValue, Func<NetReader, bool, TValue> valueReader, Action<NetWriter, TValue> valueWriter) :
            base(defaultValue,
                (r, f) => r.ReadList(cValueReader => valueReader(cValueReader, f)),
                (w, v) => w.WriteCollection(v, (cValue, cValueWriter) => valueWriter(cValueWriter, cValue)))
        {
        }

        public void SetAt(int index, TValue value)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            if (index >= Count)
                InternalValue.AddRange(Enumerable.Repeat<TValue>(default, index + 1 - Count));

            SetAtUnsafe(index, value);
        }

        void SetAtUnsafe(int index, TValue value)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            var curretnValue = InternalValue[index];
            if (curretnValue.Equals(value))
                return;

            InternalValue[index] = value;
        }

        #region IList

        public TValue this[int index]
        {
            get => Value[index];
            set => SetAtUnsafe(index, value);
        }

        public int Count => Value.Count;

        public bool IsReadOnly => false;

        public void Add(TValue item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue.Add(item);
        }

        public void Clear()
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            if (InternalValue.Count == 0)
                return;

            InternalValue.Clear();
        }

        public bool Contains(TValue item) => Value.Contains(item);

        public void CopyTo(TValue[] array, int arrayIndex) => InternalValue.CopyTo(array, arrayIndex);

        public int IndexOf(TValue item) => InternalValue.IndexOf(item);

        public void Insert(int index, TValue item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue.Insert(index, item);
        }

        public bool Remove(TValue item)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            return InternalValue.Remove(item);
        }

        public void RemoveAt(int index)
        {
            if (!HasPermission)
                throw new InvalidOperationException($"No authority to change list of type {typeof(TValue).Name}");

            InternalValue.RemoveAt(index);
        }

        public IEnumerator<TValue> GetEnumerator() => Value.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Value.GetEnumerator();

        #endregion

        public override string ToString() =>
            $"[{string.Join("; ", InternalValue)}]";
    }

    public class SerializableFloatList : SerializableList<float>
    {
        public SerializableFloatList() : this(new List<float>())
        {
        }

        public SerializableFloatList(List<float> defaultValue) : base(defaultValue, (r, f) => r.ReadSingle(), (w, v) => w.WriteSingle(v))
        {
        }
    }

    public class SerializableIntList : SerializableList<int>
    {
        public SerializableIntList() : this(new List<int>())
        {
        }

        public SerializableIntList(List<int> defaultValue) : base(defaultValue, (r, f) => r.ReadInt32(), (w, v) => w.WriteInt32(v))
        {
        }
    }

    public class SerializableBoolList : SerializableList<bool>
    {
        public SerializableBoolList() : this(new List<bool>())
        {
        }

        public SerializableBoolList(List<bool> defaultValue) : base(defaultValue, (r, f) => r.ReadBoolean(), (w, v) => w.WriteBool(v))
        {
        }
    }

    public class SerializableStringList : SerializableList<string>
    {
        public SerializableStringList() : this(new List<string>())
        {
        }

        public SerializableStringList(List<string> defaultValue) : base(defaultValue, (r, f) => r.ReadString(), (w, v) => w.WriteString(v))
        {
        }
    }
}