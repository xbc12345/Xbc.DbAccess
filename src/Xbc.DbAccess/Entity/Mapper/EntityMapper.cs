using System.Data.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体映射器（非泛型版本），提供 DbDataReader 到实体的映射功能。
    /// <para>通过 <see cref="EntityMapperCore"/> 构建表达式树并缓存映射委托，实现高性能的对象映射。</para>
    /// </summary>
    internal static class EntityMapper
    {
        /// <summary>
        /// 清理指定实体类型的所有缓存。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        public static void ClearCache(Type entityType)
        {
            Guard.ThrowIfNull(entityType, nameof(entityType));
            EntityMapperCore.ClearCache(entityType);
        }

        /// <summary>
        /// 清理所有缓存。
        /// </summary>
        public static void ClearAllCache()
        {
            EntityMapperCore.ClearAllCache();
        }

        #region 实体映射
        /// <summary>
        /// 使用 DbDataReader 异步读取单条数据，并映射为指定类型的实例。若未读取到数据，则返回 null。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="reader">数据读取器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static async Task<object> MapAsync(Type entityType, DbDataReader reader, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(entityType, nameof(entityType));
            Guard.ThrowIfNull(reader, nameof(reader));

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var mapper = (Func<DbDataReader, object>)EntityMapperCore.GetOrAddMapper(entityType, reader);
                return mapper(reader);
            }

            return null;
        }

        /// <summary>
        /// 使用 DbDataReader 异步读取多条数据，并映射为指定类型的实例列表。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="reader">数据读取器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static async Task<List<object>> MapListAsync(Type entityType, DbDataReader reader, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(entityType, nameof(entityType));
            Guard.ThrowIfNull(reader, nameof(reader));

            var result = new List<object>();
            var mapper = (Func<DbDataReader, object>)EntityMapperCore.GetOrAddMapper(entityType, reader);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(mapper(reader));
            }

            return result;
        }
        #endregion
    }
}
