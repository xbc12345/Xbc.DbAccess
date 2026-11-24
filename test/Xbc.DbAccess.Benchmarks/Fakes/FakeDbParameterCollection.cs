using System.Collections;
using System.Data.Common;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟参数集合，内部以数组列表承载 FakeDbParameter。
    /// <para>基类使用全限定名，避免与 Xbc.DbAccess 同名的 DbParameterCollection 冲突。</para>
    /// </summary>
    public sealed class FakeDbParameterCollection : System.Data.Common.DbParameterCollection
    {
        private readonly System.Collections.Generic.List<DbParameter> items
            = new System.Collections.Generic.List<DbParameter>();

        /// <inheritdoc />
        public override int Count => items.Count;

        /// <inheritdoc />
        public override object SyncRoot => ((ICollection)items).SyncRoot;

        /// <inheritdoc />
        public override int Add(object value)
        {
            items.Add((DbParameter)value);
            return items.Count - 1;
        }

        /// <inheritdoc />
        public override void Clear() => items.Clear();

        /// <inheritdoc />
        public override bool Contains(object value) => items.Contains((DbParameter)value);

        /// <inheritdoc />
        public override bool Contains(string value)
        {
            foreach (var item in items)
            {
                if (string.Equals(item.ParameterName, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void CopyTo(Array array, int index) => ((ICollection)items).CopyTo(array, index);

        /// <inheritdoc />
        public override System.Collections.IEnumerator GetEnumerator() => items.GetEnumerator();

        /// <inheritdoc />
        public override int IndexOf(object value) => items.IndexOf((DbParameter)value);

        /// <inheritdoc />
        public override int IndexOf(string parameterName)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].ParameterName, parameterName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        /// <inheritdoc />
        public override void Insert(int index, object value) => items.Insert(index, (DbParameter)value);

        /// <inheritdoc />
        public override void Remove(object value) => items.Remove((DbParameter)value);

        /// <inheritdoc />
        public override void RemoveAt(int index) => items.RemoveAt(index);

        /// <inheritdoc />
        public override void RemoveAt(string parameterName) => items.RemoveAt(IndexOf(parameterName));

        /// <inheritdoc />
        protected override DbParameter GetParameter(int index) => items[index];

        /// <inheritdoc />
        protected override DbParameter GetParameter(string parameterName) => items[IndexOf(parameterName)];

        /// <inheritdoc />
        protected override void SetParameter(int index, DbParameter value) => items[index] = value;

        /// <inheritdoc />
        protected override void SetParameter(string parameterName, DbParameter value)
            => items[IndexOf(parameterName)] = value;

        /// <inheritdoc />
        public override void AddRange(Array values)
        {
            foreach (var value in values)
                Add(value);
        }
    }
}
