using System.Data.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体映射器（泛型版本），提供 DbDataReader 到实体的高性能映射功能。
    /// <para>通过 <see cref="EntityMapperCore"/> 构建表达式树并缓存映射委托，泛型版本以强类型映射委托直接调用，与非泛型版本复用同一编译产物。</para>
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    internal static class EntityMapper<T> where T : class
    {
        /// <summary>
        /// 清理当前实体类型的所有缓存。
        /// </summary>
        public static void ClearCache()
        {
            EntityMapperCore.ClearCache(typeof(T));
        }

        #region 实体映射
        /// <summary>
        /// 使用 DbDataReader 异步读取单条数据，并映射为类型 <typeparamref name="T"/> 的实例。若未读取到数据，则返回 null。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static async Task<T> MapAsync(DbDataReader reader, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(reader, nameof(reader));

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var mapper = (Func<DbDataReader, T>)EntityMapperCore.GetOrAddMapper(typeof(T), reader);
                return mapper(reader);
            }

            return null;
        }

        /// <summary>
        /// 使用 DbDataReader 异步读取多条数据，并映射为类型 <typeparamref name="T"/> 的实例列表。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static async Task<List<T>> MapListAsync(DbDataReader reader, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(reader, nameof(reader));

            var result = new List<T>();
            var mapper = (Func<DbDataReader, T>)EntityMapperCore.GetOrAddMapper(typeof(T), reader);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(mapper(reader));
            }

            return result;
        }
        #endregion

        #region 投影映射
        /// <summary>
        /// 使用 DbDataReader 异步读取单行数据，并映射为投影实例（单列标量值、匿名类型、元组或命名类型实例）。
        /// <para>以标志位而非返回值表达行存在性：空结果集与「首行列值为 NULL」的映射结果均为 default(TResult)，二者须区分。</para>
        /// </summary>
        /// <typeparam name="TResult">投影结果类型。</typeparam>
        /// <param name="plan">投影映射计划（结果形态 + 投影列组 + 成员绑定）。</param>
        /// <param name="reader">数据读取器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否读取到数据行及映射结果。</returns>
        public static async Task<(bool HasRow, TResult Value)> MapProjectionAsync<TResult>(
            ProjectionPlan plan,
            DbDataReader reader,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(plan, nameof(plan));
            Guard.ThrowIfNull(reader, nameof(reader));

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var mapper = EntityMapperCore.GetOrAddProjectionMapper<T, TResult>(reader, plan);
                return (true, mapper(reader));
            }

            return (false, default);
        }

        /// <summary>
        /// 使用 DbDataReader 异步读取多列数据，映射为投影实例列表（单列为标量值，多列为匿名类型/元组实例或命名类型实例——无参构造 + 初始化器；投影映射计划由调用方在构建期解析选择器得到）。
        /// <para>逐列的值读取与 NULL 处理与实体赋值一致。</para>
        /// </summary>
        /// <typeparam name="TResult">投影结果类型：单列为属性类型，多列为匿名类型、元组（ValueTuple）或命名类型（无参构造 + 对象初始化器）。</typeparam>
        /// <param name="plan">投影映射计划（结果形态 + 投影列组 + 成员绑定）。</param>
        /// <param name="reader">数据读取器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>映射后的投影实例列表。</returns>
        public static async Task<List<TResult>> MapProjectionListAsync<TResult>(
            ProjectionPlan plan,
            DbDataReader reader,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(plan, nameof(plan));
            Guard.ThrowIfNull(reader, nameof(reader));

            var result = new List<TResult>();
            var mapper = EntityMapperCore.GetOrAddProjectionMapper<T, TResult>(reader, plan);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(mapper(reader));
            }

            return result;
        }
        #endregion
    }
}
