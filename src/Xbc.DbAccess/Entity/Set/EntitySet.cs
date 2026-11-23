using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体集合实现类，提供基于主键的实体 CRUD 操作，自动生成 INSERT/UPDATE/DELETE SQL 语句。
    /// <para>按职责拆分为多个 partial 文件，本文件承载构造、通用辅助与导航外键同步。</para>
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    internal sealed partial class EntitySet<T> : IEntitySet<T> where T : class
    {
        private readonly IDbAccess dbAccess;
        private readonly IDbParameterBuilder parameterBuilder;
        private readonly ISqlBuilder sqlBuilder;
        private readonly ISqlDialect sqlDialect;
        private readonly EntityMetadata entityMetadata;

        // 方言转义后的表名与主键列名，构造时一次计算供各 partial 共享
        private readonly string quotedTableName;
        private readonly string quotedPrimaryKeyColumnName;// 无主键实体为 null

        // 多值插入每批最大行数与 IN 子句批量删除每批最大键数，构造时一次计算
        private readonly int maxRowsPerInsertBatch;
        private readonly int maxKeysPerInClauseBatch;

        /// <summary>
        /// 初始化实体集合。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        public EntitySet(IDbAccess dbAccess)
        {
            this.dbAccess = dbAccess ?? throw new ArgumentNullException(nameof(dbAccess));

            // 构造期即拦截非框架实现，避免推迟到首次写入才失败
            if (dbAccess is not DbAccess)
                throw new NotSupportedException($"EntitySet 仅支持框架的 DbAccess 实现，实际类型为 '{dbAccess.GetType()}'。");

            parameterBuilder = dbAccess.ProviderFactory.ParameterBuilder;
            sqlBuilder = dbAccess.ProviderFactory.SqlBuilder;
            sqlDialect = dbAccess.ProviderFactory.SqlDialect;
            entityMetadata = EntityMetadataCache<T>.Metadata;
            quotedTableName = sqlDialect.QuoteTableName(entityMetadata.Schema, entityMetadata.TableName);
            quotedPrimaryKeyColumnName = entityMetadata.PrimaryKey != null
                ? sqlDialect.QuoteIdentifier(entityMetadata.PrimaryKey.ColumnName)
                : null;
            maxRowsPerInsertBatch = sqlDialect.GetMaxRowsPerInsertBatch(entityMetadata.Columns.Count(c => !c.IsIdentity));
            maxKeysPerInClauseBatch = sqlDialect.GetMaxKeysPerInClauseBatch();
        }

        #region 通用方法
        /// <summary>
        /// 检查实体是否有主键列，若没有则抛出 <see cref="NotSupportedException"/> 异常。
        /// </summary>
        private void ThrowIfNoPrimaryKey()
        {
            if (entityMetadata.PrimaryKey == null)
                throw new NotSupportedException($"实体类型 '{typeof(T)}' 未定义主键列，无法执行操作。");
        }

        /// <summary>
        /// 构建主键等于指定值的条件子句（如 WHERE Pk = @Pk），参数名经 <paramref name="allocator"/> 分配，与同语句其他参数互不冲突。
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <param name="allocator">参数名分配器（与其他参数源共享同一实例）。</param>
        /// <param name="parametersList">参数列表，构建的参数追加到此列表。</param>
        /// <returns>WHERE 条件项，如 <c>[Id] = @Id</c>。</returns>
        private string BuildPrimaryKeyEqualsClause(object entity, ParameterNameAllocator allocator, List<DbParameter> parametersList)
        {
            var pk = entityMetadata.PrimaryKey;
            var pkValue = pk.GetValueForDb(entity);
            var parameter = parameterBuilder.CreateParameter(allocator.Allocate(pk.ColumnName), pkValue, pk.DbType);
            parametersList.Add(parameter);
            return $"{quotedPrimaryKeyColumnName} = {parameter.ParameterName}";
        }

        /// <summary>
        /// 翻译谓词表达式为 WHERE 条件片段与参数清单（查询、聚合与条件写路径的共享入口）。
        /// </summary>
        /// <param name="predicate">谓词表达式。</param>
        /// <param name="parameterNameAllocator">参数名分配器（与同语句其他参数共享时须传入同一实例；独立语句传新实例）。</param>
        /// <returns>翻译结果。</returns>
        private WhereClauseTranslation TranslatePredicate(Expression<Func<T, bool>> predicate, ParameterNameAllocator parameterNameAllocator)
        {
            var translator = new WhereClauseTranslator(entityMetadata, sqlDialect, parameterBuilder, parameterNameAllocator);
            return translator.Translate(predicate);
        }
        #endregion

        #region 导航外键同步
        /// <summary>
        /// 将 1:1 引用的对方键值（OtherKey 列）同步到本方实体的外键列（ThisKey 列），纯内存同步，不查库、不递归。
        /// <para>外键列已有有效值时以外键为准（键值不一致时输出诊断警告）；为默认值时由引用回填。</para>
        /// </summary>
        /// <param name="entity">本方实体。</param>
        private void SyncOneToOneForeignKeys(object entity)
        {
            if (entityMetadata.Navigations == null || entityMetadata.Navigations.Count == 0)
                return;

            foreach (var nav in entityMetadata.Navigations)
            {
                if (nav.NavigationType != NavigationType.OneToOne)
                    continue;

                var navValue = nav.GetValue(entity);
                if (navValue == null)
                    continue;

                // 对方键值取 OtherKey 列（与读路径 Include 装配的键匹配一致），且取 .NET 原始值：
                // 经 GetValueForDb 会把 null 转为 DBNull 导致"已有有效值"误判，转换器场景下还会回填数据库值导致类型错误
                var targetKey = nav.OtherKeyColumn;
                var targetKeyValue = targetKey.GetValue(navValue);
                if (targetKey.IsDefaultValue(navValue))
                    continue;

                // 外键列已有有效值时以外键为准（引用可能过期，覆盖会静默回滚用户修改），键值不一致时输出诊断警告
                var foreignKey = nav.ThisKeyColumn;
                var foreignKeyValue = foreignKey.GetValue(entity);
                if (!foreignKey.IsDefaultValue(entity))
                {
                    if (!Equals(foreignKeyValue, targetKeyValue))
                    {
                        DiagnosticLog.Warn(nameof(EntitySet<T>),
                            "1:1 引用导航属性 '{NavigationProperty}' 的对方键值 '{TargetKeyValue}' 与实体 '{EntityType}' 的外键列 '{ForeignKeyColumn}' 值 '{ForeignKeyValue}' 不一致，以外键列为准（引用可能已过期）。",
                            nav.PropertyName, targetKeyValue, entity.GetType().ToString(), foreignKey.ColumnName, foreignKeyValue);
                    }

                    continue;
                }

                // 外键列为默认值（未设置），由引用回填
                foreignKey.SetValue(entity, targetKeyValue);
            }
        }

        /// <summary>
        /// 将本方主键值（ThisKey 列）同步到已赋值的 1:N 集合导航中每个对方实体的外键列（OtherKey 列），纯内存同步，不查库、不递归、不自动插入对方实体。
        /// <para>对方外键列为默认值时由本方主键回填；已有值时以对方外键为准（键值不一致时输出诊断警告）。</para>
        /// <para>对方实体的入库由调用方负责。须在本方实体插入完成后调用（自增主键插入后才回填有效值）。</para>
        /// </summary>
        /// <param name="entity">本方实体。</param>
        private void SyncOneToManyForeignKeys(object entity)
        {
            if (entityMetadata.Navigations == null || entityMetadata.Navigations.Count == 0)
                return;

            foreach (var nav in entityMetadata.Navigations)
            {
                if (nav.NavigationType != NavigationType.OneToMany)
                    continue;

                // 本方主键值无效（null 或默认值，如自增列尚未回填）时跳过；同样取 .NET 原始值
                var parentKeyValue = nav.ThisKeyColumn.GetValue(entity);
                if (nav.ThisKeyColumn.IsDefaultValue(entity))
                    continue;

                var collectionValue = nav.GetValue(entity);
                if (collectionValue == null)
                    continue;

                // 对方外键为默认值时由本方主键回填；已有值时以对方外键为准（可能已被挪出本集合），键值不一致时输出诊断警告
                var childForeignKey = nav.OtherKeyColumn;
                foreach (var child in (IEnumerable)collectionValue)
                {
                    if (child == null)
                        continue;

                    var childForeignKeyValue = childForeignKey.GetValue(child);
                    if (childForeignKey.IsDefaultValue(child))
                    {
                        childForeignKey.SetValue(child, parentKeyValue);
                        continue;
                    }

                    if (!Equals(childForeignKeyValue, parentKeyValue))
                        DiagnosticLog.Warn(nameof(EntitySet<T>),
                            "1:N 集合导航 '{NavigationProperty}' 中对方实体 '{ChildEntityType}' 的外键列 '{ForeignKeyColumn}' 值 '{ForeignKeyValue}' 与本方实体 '{ParentEntityType}' 的主键值 '{ParentKeyValue}' 不一致，以对方实体外键列为准（该实体可能已挪出本集合）。",
                            nav.PropertyName, child.GetType().ToString(), childForeignKey.ColumnName, childForeignKeyValue, entity.GetType().ToString(), parentKeyValue);
                }
            }
        }
        #endregion
    }
}
