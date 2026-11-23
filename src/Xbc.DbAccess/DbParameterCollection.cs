using System.Collections;
using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 参数集合，用于构建和管理 <see cref="DbParameter"/>。
    /// </summary>
    public class DbParameterCollection : ICollection<DbParameter>
    {
        // 使用不区分大小写的字典存储参数，方便按名称访问
        private readonly Dictionary<string, DbParameter> parameters
            = new Dictionary<string, DbParameter>(StringComparer.OrdinalIgnoreCase);
        private readonly IDbParameterBuilder parameterBuilder;

        /// <summary>
        /// 参数构建器。
        /// </summary>
        public IDbParameterBuilder ParameterBuilder => parameterBuilder;

        /// <summary>
        /// 集合中参数的数量。
        /// </summary>
        public int Count => parameters.Count;

        /// <summary>
        /// 集合是否为只读。
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// 初始化 <see cref="DbParameterCollection"/> 类的新实例。
        /// </summary>
        /// <param name="parameterBuilder">参数构建器。</param>
        public DbParameterCollection(IDbParameterBuilder parameterBuilder)
        {
            this.parameterBuilder = parameterBuilder ?? throw new ArgumentNullException(nameof(parameterBuilder));
        }

        #region 添加参数
        /// <summary>
        /// 获取参数的存储键：名称为空时保持原样，否则归一化为带前缀参数名，
        /// 保证字符串添加与直接添加 <see cref="DbParameter"/> 两种路径的键一致。
        /// </summary>
        /// <param name="parameterName">参数名。</param>
        /// <returns></returns>
        private string GetParameterKey(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
                return parameterName;

            return parameterBuilder.GetParameterName(parameterName);
        }

        /// <summary>
        /// 添加参数，重名时抛出包含参数名的异常。
        /// </summary>
        /// <param name="parameter">数据库参数。</param>
        /// <param name="key">归一化后的存储键。</param>
        private void AddParameter(DbParameter parameter, string key)
        {
            if (parameters.ContainsKey(key))
                throw new ArgumentException($"已存在同名的参数: {key}。");

            parameters.Add(key, parameter);
        }

        /// <summary>
        /// 添加参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <returns></returns>
        public DbParameterCollection Add(string name, object value, DbType? dbType = null)
        {
            var parameter = parameterBuilder.CreateParameter(name, value, dbType);
            AddParameter(parameter, parameter.ParameterName);
            return this;
        }

        /// <summary>
        /// 添加输出参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <param name="size">参数长度（字符/字节数），null 时对字符串/二进制类型使用默认值 4000。</param>
        /// <returns></returns>
        public DbParameterCollection AddOutput(string name, DbType dbType, int? size = null)
        {
            var parameter = parameterBuilder.CreateParameter(name, DBNull.Value, dbType, ParameterDirection.Output, size);
            AddParameter(parameter, parameter.ParameterName);
            return this;
        }

        /// <summary>
        /// 添加返回值参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <returns></returns>
        public DbParameterCollection AddReturnValue(string name, DbType dbType)
        {
            var parameter = parameterBuilder.CreateParameter(name, DBNull.Value, dbType, ParameterDirection.ReturnValue);
            AddParameter(parameter, parameter.ParameterName);
            return this;
        }

        /// <summary>
        /// 添加参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <param name="direction">参数方向。</param>
        /// <param name="size">参数长度（字符/字节数），null 时对输出类方向的字符串/二进制类型使用默认值 4000。</param>
        /// <returns></returns>
        public DbParameterCollection Add(string name, object value, DbType? dbType, ParameterDirection direction, int? size = null)
        {
            var parameter = parameterBuilder.CreateParameter(name, value, dbType, direction, size);
            AddParameter(parameter, parameter.ParameterName);
            return this;
        }
        #endregion

        #region 获取参数值
        /// <summary>
        /// 获取参数值。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <returns></returns>
        public object GetValue(string name)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));

            var parameterName = parameterBuilder.GetParameterName(name);
            if (!parameters.TryGetValue(parameterName, out var param))
                throw new ArgumentException($"参数 '{parameterName}' 不存在。", nameof(name));

            return param.Value;
        }

        /// <summary>
        /// 获取参数值并转换为指定类型。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="name">参数名。</param>
        /// <returns></returns>
        public T GetValue<T>(string name)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));

            var parameterName = parameterBuilder.GetParameterName(name);
            if (!parameters.TryGetValue(parameterName, out var param))
                throw new ArgumentException($"参数 '{parameterName}' 不存在。", nameof(name));

            return DbValueConvert.ChangeType<T>(param.Value);
        }

        /// <summary>
        /// 尝试获取参数值。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <returns></returns>
        public bool TryGetValue(string name, out object value)
        {
            value = DBNull.Value;

            if (string.IsNullOrEmpty(name))
                return false;

            var parameterName = parameterBuilder.GetParameterName(name);
            if (!parameters.TryGetValue(parameterName, out var param))
                return false;

            value = param.Value;
            return true;
        }

        /// <summary>
        /// 尝试获取参数值并转换为指定类型。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="name">参数名。</param>
        /// <param name="value">转换后的值。</param>
        /// <returns></returns>
        public bool TryGetValue<T>(string name, out T value)
        {
            value = default;

            if (string.IsNullOrEmpty(name))
                return false;

            var parameterName = parameterBuilder.GetParameterName(name);
            if (!parameters.TryGetValue(parameterName, out var param))
                return false;

            try
            {
                value = DbValueConvert.ChangeType<T>(param.Value);
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region ICollection<DbParameter> 实现
        /// <summary>
        /// 将参数添加到集合中。
        /// </summary>
        /// <param name="parameter">数据库参数，必须具有非空名称。</param>
        public void Add(DbParameter parameter)
        {
            Guard.ThrowIfNull(parameter, nameof(parameter));

            // 参数必须具有非空名称：空名称无法归一化存储键，也无法按名访问
            if (parameter.ParameterName == null)
                throw new ArgumentNullException(nameof(parameter), "参数必须具有非空名称。");

            if (string.IsNullOrWhiteSpace(parameter.ParameterName))
                throw new ArgumentException("参数必须具有非空名称。", nameof(parameter));

            // 归一化存储键，与字符串添加路径保持一致（手工构造的参数名可能不带前缀）
            AddParameter(parameter, GetParameterKey(parameter.ParameterName));
        }

        /// <summary>
        /// 移除所有参数。
        /// </summary>
        public void Clear()
        {
            parameters.Clear();
        }

        /// <summary>
        /// 确定集合是否包含指定参数。
        /// </summary>
        /// <param name="parameter">数据库参数。</param>
        /// <returns></returns>
        public bool Contains(DbParameter parameter)
        {
            Guard.ThrowIfNull(parameter, nameof(parameter));
            return parameters.ContainsKey(GetParameterKey(parameter.ParameterName));
        }

        /// <summary>
        /// 确定集合是否包含指定名称的参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <returns></returns>
        public bool Contains(string name)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));

            var parameterName = parameterBuilder.GetParameterName(name);
            return parameters.ContainsKey(parameterName);
        }

        /// <summary>
        /// 将集合中的参数复制到兼容的一维数组中。
        /// </summary>
        /// <param name="array">目标数组。</param>
        /// <param name="arrayIndex">目标数组的起始索引。</param>
        public void CopyTo(DbParameter[] array, int arrayIndex)
        {
            parameters.Values.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// 从集合中移除指定参数。
        /// </summary>
        /// <param name="parameter">数据库参数。</param>
        /// <returns></returns>
        public bool Remove(DbParameter parameter)
        {
            Guard.ThrowIfNull(parameter, nameof(parameter));
            return parameters.Remove(GetParameterKey(parameter.ParameterName));
        }

        /// <summary>
        /// 从集合中移除指定名称的参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <returns></returns>
        public bool Remove(string name)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));

            var parameterName = parameterBuilder.GetParameterName(name);
            return parameters.Remove(parameterName);
        }

        /// <summary>
        /// 返回循环访问集合的枚举器。
        /// </summary>
        /// <returns></returns>
        public IEnumerator<DbParameter> GetEnumerator()
        {
            return parameters.Values.GetEnumerator();
        }

        /// <summary>
        /// 返回循环访问集合的枚举器。
        /// </summary>
        /// <returns></returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        #endregion
    }
}
