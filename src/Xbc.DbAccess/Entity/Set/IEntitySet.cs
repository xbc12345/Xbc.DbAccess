using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体集合接口，提供实体 CRUD、主键查找与查询链入口（<see cref="Where"/> / <see cref="AsQuery"/>）。
    /// <para>写操作只处理主实体行（导航属性仅做外键内存同步）；删除不级联，被引用记录交由数据库外键约束处理；
    /// 导航属性读取经 <see cref="Where"/> 组合 Include/ThenInclude；标量聚合与投影经查询链提供（<c>Where(...)</c> / <see cref="AsQuery"/>）。</para>
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    public interface IEntitySet<T> where T : class
    {
        /// <summary>
        /// 异步根据主键查找实体（不加载导航属性）。
        /// <para>需要加载导航属性时使用 <see cref="Where"/> 组合：
        /// <c>set.Where(u => u.Id == key).Include(u => u.Orders).SingleOrDefaultAsync()</c>，
        /// 能力与 FindAsync 一致且支持多级导航（ThenInclude）与更多取数形态。</para>
        /// </summary>
        /// <param name="key">主键值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>找到的实体；不存在时返回 null。</returns>
        Task<T> FindAsync(object key, CancellationToken cancellationToken = default);

        /// <summary>
        /// 按谓词构建实体查询，返回可继续组合的查询对象（<see cref="IEntityQuery{T}.Include"/> 加载导航属性、
        /// OrderBy/Skip/Take 组合、First/SingleOrDefault/ToList 取数）。
        /// <para>谓词表达式的能力与条件 UPDATE/DELETE 一致：比较运算、逻辑运算（&amp;&amp;/||/!）、布尔列直接作谓词、
        /// 与 null 的等于比较（IS [NOT] NULL）、可空列 HasValue、集合 Contains（IN）、字符串 Contains/StartsWith/EndsWith
        /// （LIKE，自动转义通配符）、string.IsNullOrEmpty/IsNullOrWhiteSpace、字符串变换（ToLower/ToUpper/Trim 族）
        /// 与日期部分（Year/Month/Day/Date/Hour/Minute/Second）比较；闭包变量一律参数化、永不内联进 SQL；
        /// 投影、Join、子查询、算术运算、导航属性等谓词内嵌套形态不受支持（抛出异常）；分组查询不是谓词内形态，而是独立链点（Where 返回的 <see cref="IEntityQuery{T}"/> 提供 GroupBy）。</para>
        /// <para>按主键查找等价于 <c>Where(u => u.Id == key).SingleOrDefaultAsync()</c>（<see cref="FindAsync"/> 为其主键专用便捷形态）。</para>
        /// </summary>
        /// <param name="predicate">过滤条件表达式，如 <c>u => u.Age &gt; 18</c>。</param>
        /// <returns>带 WHERE 条件的实体查询对象。</returns>
        IEntityQuery<T> Where(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 将实体集合投影为无条件的全表查询链（视角转换，不产生查询）：返回可继续组合的查询对象
        /// （Include/OrderBy/Skip/Take/keyset 分页与各终结方法）。
        /// <para>带过滤条件的场景请使用 <see cref="Where"/>；本方法与 Where 同为查询链的起点，是 keyset 分页
        /// （<see cref="IExecutableEntityQuery{T}.KeysetAfterAsync"/>）等无条件链式操作的入口。</para>
        /// </summary>
        /// <returns>全表实体查询对象。</returns>
        IEntityQuery<T> AsQuery();

        /// <summary>
        /// 异步插入实体，只插入主表行。
        /// <para>1:1 引用已赋值且本方外键列为默认值时，将对方键值（OtherKey 列）回填到本方外键列；外键列已有值时以外键为准。</para>
        /// <para>1:N 集合导航已赋值时，插入后同步对方实体外键列：默认值回填本方主键值（ThisKey 列），已有值以对方外键为准（对方实体入库由调用方负责）。</para>
        /// </summary>
        /// <param name="entity">要插入的实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>当前实体受影响的行数。</returns>
        Task<int> InsertAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步批量插入实体，只插入主表行。
        /// <para>1:1 引用已赋值且本方外键列为默认值时，将对方键值（OtherKey 列）回填到本方外键列；外键列已有值时以外键为准。</para>
        /// <para>1:N 集合导航已赋值时，插入后同步对方实体外键列：默认值回填本方主键值（ThisKey 列），已有值以对方外键为准（对方实体入库由调用方负责）。</para>
        /// </summary>
        /// <param name="entities">实体集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        Task<int> InsertRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步更新实体（按主键匹配），只更新主实体行（含 1:1 外键列）。
        /// <para><b>注意：</b>更新不做变更追踪，除主键与自增列外的所有列都会进入 SET 子句，实体须为完整查询
        /// （如 <see cref="FindAsync(object, CancellationToken)"/>）加载的完整实体，部分投影或手工构造的实体会以属性默认值覆盖数据库原值。</para>
        /// <para>1:N 集合导航已赋值时，更新后同步对方实体外键列：默认值回填本方主键值（ThisKey 列），已有值以对方外键为准。</para>
        /// <para>配置并发令牌列（<c>[Column(IsConcurrencyToken = true)]</c>，byte[]）时：更新成功后自动换发并回填新令牌；
        /// 影响行数为 0（目标行不存在或令牌已被并发修改）抛 <see cref="DbConcurrencyException"/>；未配置时静默返回 0。</para>
        /// </summary>
        /// <param name="entity">要更新的实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>当前实体受影响的行数。</returns>
        Task<int> UpdateAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步条件更新：将满足条件的所有行的指定列更新为赋值表达式的值，返回受影响的行数。
        /// <para>赋值表达式支持对象初始化器（<c>x => new User { Age = 18 }</c>）与匿名类型（<c>x => new { Age = 18 }</c>，不支持元组与带参构造形态——两者无成员名承载「更新哪一列」的寻址语义），
        /// 赋值到主键列、标识列或导航属性、重复赋值同一列时抛出异常。</para>
        /// <para>条件表达式支持比较/逻辑运算、布尔列作谓词、与 null 比较（IS [NOT] NULL）、集合 Contains（IN）、
        /// 字符串 Contains/StartsWith/EndsWith（LIKE，自动转义通配符）；闭包变量一律参数化；导航属性、嵌套成员路径与算术运算抛出异常。</para>
        /// </summary>
        /// <typeparam name="TValues">赋值载体类型（匿名类型或实体类型）。</typeparam>
        /// <param name="assignment">赋值表达式，如 <c>x => new { Age = 18 }</c>。</param>
        /// <param name="predicate">条件表达式，如 <c>x => x.Name == "Alice"</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        Task<int> UpdateAsync<TValues>(Expression<Func<T, TValues>> assignment, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步批量更新实体（按主键匹配），只更新主实体行。
        /// <para><b>注意：</b>与 <see cref="UpdateAsync(T, CancellationToken)"/> 一致，不做变更追踪、全列 SET，实体须为完整查询加载的完整实体。</para>
        /// <para>1:N 集合导航已赋值时，更新后同步对方实体外键列：默认值回填本方主键值（ThisKey 列），已有值以对方外键为准。</para>
        /// <para>配置并发令牌列时逐条做乐观并发校验，任一冲突抛 <see cref="DbConcurrencyException"/> 并由外层事务整体回滚。</para>
        /// </summary>
        /// <param name="entities">实体集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步删除实体（按主键匹配），只删除主实体行，不级联删除对方实体（被外键引用时删除失败）。
        /// <para>配置并发令牌列时：影响行数为 0（目标行不存在或令牌已被并发修改）抛 <see cref="DbConcurrencyException"/>；未配置时静默返回 0。</para>
        /// </summary>
        /// <param name="entity">要删除的实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>当前实体受影响的行数。</returns>
        Task<int> DeleteAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步条件删除：删除满足条件的所有行，返回受影响的行数。不级联删除对方实体（被外键引用时抛数据库原生外键约束异常）。
        /// <para>条件表达式支持比较/逻辑运算、布尔列作谓词、与 null 比较（IS [NOT] NULL）、集合 Contains（IN）、
        /// 字符串 Contains/StartsWith/EndsWith（LIKE，自动转义通配符）；闭包变量一律参数化；导航属性、嵌套成员路径与算术运算抛出异常。</para>
        /// </summary>
        /// <param name="predicate">条件表达式，如 <c>x => x.Age &gt; 18</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        Task<int> DeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步批量删除实体（按主键匹配），只删除主实体行，不级联删除对方实体（被外键引用时删除失败）。
        /// <para>配置并发令牌列时退化为事务内逐条删除（IN 子句无法逐行校验令牌）：任一冲突抛 <see cref="DbConcurrencyException"/> 并整体回滚。</para>
        /// </summary>
        /// <param name="entities">实体集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        Task<int> DeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步 Upsert 实体（存在则更新，不存在则插入），只处理主表行。冲突键为实体主键，实体没有主键时抛出异常。
        /// <para>更新列覆盖除主键、自增列外的全部列（不做变更追踪），调用方需自行避免覆盖并发修改。</para>
        /// <para>自增主键为默认值时走插入路径并回填生成的 ID（个别方言的 upsert 机制不支持回填，如 Oracle）；带显式值时按该值参与冲突判定（无需回填）。</para>
        /// <para>方言差异：个别方言（如 MySQL）的冲突判定覆盖所有唯一键（不只主键），且更新路径的受影响行数返回 2；其余方言为严格主键冲突。</para>
        /// </summary>
        /// <param name="entity">要插入或更新的实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数（MySQL 更新路径返回 2，其余方言为 1）。</returns>
        Task<int> UpsertAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步 bulk 插入实体，只插入主表行，面向大批量数据的高性能写入。
        /// <para>有原生批量写入 API 的方言走原生路径，无原生 API 的方言回退多值 INSERT（分批语义与 <see cref="InsertRangeAsync(IEnumerable{T}, CancellationToken)"/> 一致）。</para>
        /// <para>自增列被忽略且不回填生成的 ID，需回填 ID 请改用
        /// <see cref="InsertRangeAsync(IEnumerable{T}, CancellationToken)"/>；全部列为自增列的退化场景抛出异常。</para>
        /// <para>导航属性同步与 <see cref="InsertRangeAsync(IEnumerable{T}, CancellationToken)"/> 一致；配置并发令牌列时为每个实体生成初始令牌。</para>
        /// <para>整批在事务中执行（已有活动事务时自动嵌套为保存点子事务）；<paramref name="options"/> 仅原生路径生效，回退路径静默忽略。</para>
        /// <para>部分方言的原生路径默认不校验 CHECK/外键约束、不触发触发器；NULL 值按字面写入。</para>
        /// </summary>
        /// <param name="entities">实体集合。</param>
        /// <param name="options">bulk 插入选项，null 时使用方言默认值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已插入的行数。</returns>
        /// <exception cref="ArgumentException"><paramref name="entities"/> 为 null、空集合或包含 null 元素，或选项值非法。</exception>
        Task<long> BulkInsertAsync(IEnumerable<T> entities, BulkInsertOptions options = null, CancellationToken cancellationToken = default);
    }
}
