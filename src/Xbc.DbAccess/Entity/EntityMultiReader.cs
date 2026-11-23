using System.Data.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 多结果集实体映射器，用于从包含多个结果集的 DbDataReader 中顺序读取并映射为不同类型的实体列表。
    /// <para>每次读取（<see cref="ReadAsync{T}"/> / <see cref="ReadFirstAsync{T}"/>）当前结果集后自动移动到下一个；
    /// 越过最后一个结果集后再读取将抛出异常，避免结果集数量与读取次数不匹配时静默返回空结果。</para>
    /// </summary>
    public class EntityMultiReader : DisposableBase
    {
        private readonly DbDataReader reader;
        private bool consumed;// 是否已越过最后一个结果集（NextResult 返回 false）

        /// <summary>
        /// 初始化多结果集实体映射器的新实例。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        public EntityMultiReader(DbDataReader reader)
        {
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        /// <summary>
        /// 异步读取当前结果集并映射为指定类型的实例列表，然后移动到下一个结果集。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public async Task<List<T>> ReadAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            ThrowIfDisposed();
            ThrowIfConsumed();

            var result = await EntityMapper<T>.MapListAsync(reader, cancellationToken).ConfigureAwait(false);
            await MoveNextResultAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// 异步读取当前结果集并映射为指定类型的实例列表，然后移动到下一个结果集。
        /// </summary>
        /// <param name="entityType"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List<object>> ReadAsync(Type entityType, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(entityType, nameof(entityType));

            ThrowIfDisposed();
            ThrowIfConsumed();

            var result = await EntityMapper.MapListAsync(entityType, reader, cancellationToken).ConfigureAwait(false);
            await MoveNextResultAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// 异步读取当前结果集的第一条数据并映射为指定类型的实例，然后移动到下一个结果集。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public async Task<T> ReadFirstAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            ThrowIfDisposed();
            ThrowIfConsumed();

            var result = await EntityMapper<T>.MapAsync(reader, cancellationToken).ConfigureAwait(false);
            await MoveNextResultAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// 异步读取当前结果集的第一条数据并映射为指定类型的实例，然后移动到下一个结果集。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public async Task<object> ReadFirstAsync(Type entityType, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(entityType, nameof(entityType));

            ThrowIfDisposed();
            ThrowIfConsumed();

            var result = await EntityMapper.MapAsync(entityType, reader, cancellationToken).ConfigureAwait(false);
            await MoveNextResultAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// 移动到下一个结果集；若当前已是最后一个结果集则标记为已耗尽。
        /// <para><see cref="DbDataReader.NextResultAsync(CancellationToken)"/> 抛异常时同样先标记已耗尽再重抛。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task MoveNextResultAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                    consumed = true;
            }
            catch
            {
                // 抛异常时同样置 consumed，后续读取经 ThrowIfConsumed 明确报错
                consumed = true;
                throw;
            }
        }

        /// <summary>
        /// 结果集已全部读取完毕时抛出异常。
        /// </summary>
        private void ThrowIfConsumed()
        {
            if (consumed)
                throw new InvalidOperationException("多结果集已全部读取完毕，没有更多的结果集可供读取，请检查 SQL 返回的结果集数量与读取次数是否匹配。");
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            reader?.Dispose();
        }
    }
}
