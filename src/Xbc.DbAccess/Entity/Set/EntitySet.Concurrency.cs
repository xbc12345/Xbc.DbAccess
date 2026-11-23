using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <content>
    /// 乐观并发令牌的生成与校验辅助（令牌列经 <c>[Column(IsConcurrencyToken = true)]</c> 声明，见 <see cref="EntityMetadata.ConcurrencyTokenColumn"/>）。
    /// <para>契约：插入仅生成初始令牌；实体版更新/删除做令牌校验，0 行命中抛 <see cref="DbConcurrencyException"/>（更新成功后换发并回填，删除无换发）；条件更新/删除与 Upsert 不做令牌校验。</para>
    /// </content>
    internal sealed partial class EntitySet<T>
    {
        #region 并发令牌辅助
        /// <summary>
        /// 确保新行实体持有初始并发令牌：未配置令牌列时直接返回；值为 null 或空数组时生成初始 Guid 字节并写入实体。
        /// <para>供纯插入路径在构建 SQL 前调用。幂等：已有值时不动（调用方显式设置的令牌被尊重）。</para>
        /// <para>更新类路径改写行内容，令牌作为版本号须前进，直接生成新值（<see cref="GenerateConcurrencyToken"/>），不经本方法。</para>
        /// </summary>
        /// <param name="entity">实体。</param>
        private void EnsureConcurrencyToken(object entity)
        {
            var tokenColumn = entityMetadata.ConcurrencyTokenColumn;
            if (tokenColumn == null)
                return;

            var token = tokenColumn.GetValue(entity) as byte[];
            if (token == null || token.Length == 0)
                tokenColumn.SetValue(entity, GenerateConcurrencyToken());
        }

        /// <summary>
        /// 生成新的并发令牌（Guid 字节，应用侧生成，跨数据库兼容）。
        /// </summary>
        /// <returns>16 字节令牌。</returns>
        private static byte[] GenerateConcurrencyToken()
        {
            // 不依赖数据库专属特性（如 SQL Server rowversion），跨数据库行为一致
            return Guid.NewGuid().ToByteArray();
        }

        /// <summary>
        /// 读取实体的当前并发令牌用于 WHERE 等值条件。调用方须已确认实体配置了令牌列。
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <returns>当前令牌字节。</returns>
        /// <exception cref="InvalidOperationException">令牌值为 null/空数组。默认值令牌无法构建等值条件
        /// （与 NULL 的等值比较恒不匹配，会被误报为并发冲突），显式失败优于静默误判。</exception>
        private byte[] GetRequiredConcurrencyToken(object entity)
        {
            var tokenColumn = entityMetadata.ConcurrencyTokenColumn;
            var token = tokenColumn.GetValue(entity) as byte[];

            if (token == null || token.Length == 0)
                throw new InvalidOperationException($"实体表 '{entityMetadata.TableName}' 的并发令牌列 '{tokenColumn.ColumnName}' 值为空，无法执行乐观并发校验；请通过完整查询加载实体或先设置令牌值。");

            return token;
        }

        /// <summary>
        /// 影响行数为 0 时的并发冲突判定：配置了令牌列的实体版更新/删除未命中（目标行不存在或令牌已被并发修改）时抛出。
        /// </summary>
        /// <param name="affectedRows">受影响的行数。</param>
        private void ThrowIfConcurrencyConflict(int affectedRows)
        {
            if (affectedRows != 0 || entityMetadata.ConcurrencyTokenColumn == null)
                return;

            throw new DbConcurrencyException($"表 '{entityMetadata.TableName}' 的乐观并发校验失败：目标行不存在或并发令牌已被其他操作修改（影响行数为 0，两者不可区分），请重新查询实体后重试。");
        }

        /// <summary>
        /// 构建并发令牌列的 SET 子句项（UPDATE 路径）：令牌列换发新令牌。
        /// </summary>
        /// <param name="tokenColumn">并发令牌列元数据。</param>
        /// <param name="newToken">新令牌字节。</param>
        /// <param name="allocator">参数名分配器（与同一语句的其他参数共享）。</param>
        /// <param name="parametersList">参数列表，构建的参数追加到此列表。</param>
        /// <returns>SET 子句项，如 <c>[RowVersion] = @RowVersion</c>。</returns>
        private string BuildConcurrencyTokenSetClause(ColumnMetadata tokenColumn, byte[] newToken, ParameterNameAllocator allocator, List<DbParameter> parametersList)
        {
            var parameter = parameterBuilder.CreateParameter(allocator.Allocate(tokenColumn.ColumnName), newToken, tokenColumn.DbType);
            parametersList.Add(parameter);
            return $"{sqlDialect.QuoteIdentifier(tokenColumn.ColumnName)} = {parameter.ParameterName}";
        }

        /// <summary>
        /// 构建并发令牌列的 WHERE 等值条件（更新/删除路径）：与旧令牌等值做乐观并发校验。
        /// <para>参数名以 <c>_Old</c> 后缀与 SET 侧新值参数区分；与其他参数名冲突时由分配器自动让位。</para>
        /// </summary>
        /// <param name="tokenColumn">并发令牌列元数据。</param>
        /// <param name="oldToken">旧令牌字节。</param>
        /// <param name="allocator">参数名分配器（与同一语句的其他参数共享）。</param>
        /// <param name="parametersList">参数列表，构建的参数追加到此列表。</param>
        /// <returns>WHERE 条件项，如 <c>[RowVersion] = @RowVersion_Old</c>。</returns>
        private string BuildConcurrencyTokenCondition(ColumnMetadata tokenColumn, byte[] oldToken, ParameterNameAllocator allocator, List<DbParameter> parametersList)
        {
            var parameter = parameterBuilder.CreateParameter(allocator.Allocate(tokenColumn.ColumnName + "_Old"), oldToken, tokenColumn.DbType);
            parametersList.Add(parameter);
            return $"{sqlDialect.QuoteIdentifier(tokenColumn.ColumnName)} = {parameter.ParameterName}";
        }
        #endregion
    }
}
