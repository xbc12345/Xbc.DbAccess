namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 分组键列：列元数据 + 复合键的成员名（单成员标量键为 null——g.Key 即键列本身，无成员可寻址）。
    /// </summary>
    internal sealed class GroupedKeyColumn
    {
        public GroupedKeyColumn(ColumnMetadata column, string memberName)
        {
            Column = column;
            MemberName = memberName;
        }

        /// <summary>键列元数据。</summary>
        public ColumnMetadata Column { get; }

        /// <summary>复合键（匿名类型/初始化器）的成员名；单成员标量键为 null。</summary>
        public string MemberName { get; }
    }

    /// <summary>
    /// 聚合 SQL 片段：已方言引用的聚合表达式 + 源列元数据（Count/LongCount 无源列为 null）。
    /// <para>片段文本同时作为别名分配与去重的键（同一聚合在 Select 与排序中重复出现时共用别名）。</para>
    /// </summary>
    internal sealed class AggregateFragment
    {
        public AggregateFragment(string sql, ColumnMetadata column)
        {
            Sql = sql;
            Column = column;
        }

        /// <summary>聚合表达式 SQL 文本（如 <c>SUM("Amount")</c>、<c>COUNT(*)</c>、方言 AVG 表达式）。</summary>
        public string Sql { get; }

        /// <summary>聚合源列元数据；Count/LongCount 无源列为 null（投影读取走 mapper 裸读取分支）。</summary>
        public ColumnMetadata Column { get; }
    }

    /// <summary>
    /// 分组投影输出列：键列引用或聚合片段（互斥）。Column 供投影 mapper 按位读取
    /// （键列为键列元数据、聚合为源列元数据、COUNT 为 null 走裸读取分支）。
    /// </summary>
    internal sealed class GroupedOutputColumn
    {
        public GroupedOutputColumn(ColumnMetadata column, AggregateFragment aggregate)
        {
            Column = column;
            Aggregate = aggregate;
        }

        /// <summary>键列或聚合源列元数据；COUNT 类聚合为 null。</summary>
        public ColumnMetadata Column { get; }

        /// <summary>聚合片段；键列项为 null。</summary>
        public AggregateFragment Aggregate { get; }
    }

    /// <summary>
    /// 分组排序目标：键列（外层自然名引用）或聚合片段（外层别名引用，SQL 构建期由分组层分配）。
    /// </summary>
    internal sealed class GroupedOrderingTarget
    {
        public GroupedOrderingTarget(ColumnMetadata keyColumn, AggregateFragment aggregate, bool descending)
        {
            KeyColumn = keyColumn;
            Aggregate = aggregate;
            Descending = descending;
        }

        /// <summary>键列排序目标；聚合排序为 null。</summary>
        public ColumnMetadata KeyColumn { get; }

        /// <summary>聚合排序目标；键列排序为 null。</summary>
        public AggregateFragment Aggregate { get; }

        /// <summary>是否降序。</summary>
        public bool Descending { get; }
    }
}
