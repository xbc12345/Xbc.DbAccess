using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体元数据，包含实体类型对应的表名、列信息、主键、标识列和导航属性等映射信息。
    /// <para>通过 <see cref="Builder"/> 构建实例，构建完成后不可修改。</para>
    /// </summary>
    internal sealed class EntityMetadata
    {
        // 属性名到列元数据的查找字典，SetColumns 时一次性建立
        private Dictionary<string, ColumnMetadata> columnMap;

        /// <summary>
        /// 实体类型。
        /// </summary>
        public Type EntityType { get; private set; }

        /// <summary>
        /// 表名。
        /// </summary>
        public string TableName { get; private set; }

        /// <summary>
        /// 架构名。
        /// </summary>
        public string Schema { get; private set; }

        /// <summary>
        /// 列集合。
        /// </summary>
        public IReadOnlyList<ColumnMetadata> Columns { get; private set; }

        /// <summary>
        /// 主键列。
        /// </summary>
        public ColumnMetadata PrimaryKey { get; private set; }

        /// <summary>
        /// 标识列。
        /// </summary>
        public ColumnMetadata IdentityColumn { get; private set; }

        /// <summary>
        /// 乐观并发令牌列；未配置时为 null（每实体至多一列，由元数据构建校验保证）。
        /// </summary>
        public ColumnMetadata ConcurrencyTokenColumn { get; private set; }

        /// <summary>
        /// 导航属性集合。
        /// </summary>
        public IReadOnlyList<NavigationMetadata> Navigations { get; private set; }

        private EntityMetadata() { }

        /// <summary>
        /// 设置列集合。仅供 <see cref="EntityMetadataCache"/> 在构建列后调用。
        /// </summary>
        internal void SetColumns(List<ColumnMetadata> columns)
        {
            Columns = columns.AsReadOnly();

            // 属性名查找字典：序数比较（区分大小写）；属性名重复时先注册者胜出
            columnMap = new Dictionary<string, ColumnMetadata>(columns.Count, StringComparer.Ordinal);
            foreach (var column in columns)
            {
                if (!columnMap.ContainsKey(column.PropertyName))
                    columnMap[column.PropertyName] = column;
            }
        }

        /// <summary>
        /// 设置导航属性集合。仅供 <see cref="EntityMetadataCache"/> 在构建导航属性后调用。
        /// </summary>
        internal void SetNavigations(List<NavigationMetadata> navigations)
        {
            Navigations = navigations.AsReadOnly();
        }

        /// <summary>
        /// 获取指定属性名的列元数据。
        /// </summary>
        /// <param name="propertyName">属性名。</param>
        /// <returns>列元数据；如果未找到则返回 null。</returns>
        public ColumnMetadata GetColumn(string propertyName)
        {
            // 属性名为 null 时无匹配（不抛异常）；列尚未设置时（构建中）同样返回 null
            if (string.IsNullOrEmpty(propertyName))
                return null;

            return columnMap?.TryGetValue(propertyName, out var column) == true ? column : null;
        }

        /// <summary>
        /// 解析列选择器表达式为单级映射列：常量、计算表达式、多级路径与闭包链直接抛出异常。
        /// <para>与查找语义的 <see cref="GetColumn(string)"/> 不同，本方法在解析失败或属性不是映射列时直接抛出异常，供排序键、聚合列与投影列解析共用。</para>
        /// </summary>
        /// <param name="selector">列选择器表达式（如 <c>x =&gt; x.Name</c>）。</param>
        /// <returns>列元数据。</returns>
        /// <exception cref="ArgumentException">表达式无效、不是根实体的单级成员访问（含多级路径与闭包链）。</exception>
        /// <exception cref="NotSupportedException">属性不是实体的映射列。</exception>
        internal ColumnMetadata ResolveColumn(LambdaExpression selector)
        {
            Guard.ThrowIfNull(selector, nameof(selector));

            var accessor = MemberAccessor.Create(selector);
            if (accessor.IsDeepPath)
                throw new ArgumentException($"列选择器不支持多级访问路径 '{accessor.MemberPath}'，请使用单级属性访问。", nameof(selector));

            var column = GetColumn(accessor.MemberName);
            if (column == null)
                throw new NotSupportedException($"'{accessor.ParameterType}.{accessor.MemberName}' 不是实体 '{EntityType}' 的映射列。");

            return column;
        }

        /// <summary>
        /// 获取指定属性名的导航属性元数据。
        /// </summary>
        /// <param name="propertyName">属性名。</param>
        /// <returns>导航属性元数据；如果未找到则返回 null。</returns>
        public NavigationMetadata GetNavigation(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return null;

            return Navigations?.FirstOrDefault(n => n.PropertyName == propertyName);
        }

        /// <summary>
        /// 解析导航属性元数据，验证表达式为单级访问并在当前实体元数据中查找导航属性。
        /// <para>与查找语义的 <see cref="GetNavigation(string)"/> 不同，本方法在解析失败或属性不是导航属性时直接抛出异常。</para>
        /// </summary>
        /// <param name="selector">导航属性选择器表达式。</param>
        /// <returns>导航属性元数据。</returns>
        /// <exception cref="ArgumentException">表达式为多级访问路径。</exception>
        /// <exception cref="NotSupportedException">属性不是导航属性。</exception>
        public NavigationMetadata ResolveNavigation(LambdaExpression selector)
        {
            Guard.ThrowIfNull(selector, nameof(selector));

            var accessor = MemberAccessor.Create(selector);
            if (accessor.IsDeepPath)
                throw new ArgumentException($"导航属性选择器不支持多级访问路径 '{accessor.MemberPath}'，请使用单级导航属性访问。", nameof(selector));

            var navigation = GetNavigation(accessor.MemberName);
            if (navigation == null)
                throw new NotSupportedException($"'{accessor.ParameterType}.{accessor.MemberName}' 不是导航属性，请使用 [Navigation] 特性标记。");

            return navigation;
        }

        /// <summary>
        /// 实体元数据构建器，用于在 <see cref="EntityMetadataCache"/> 中构建不可变的元数据实例。
        /// </summary>
        internal sealed class Builder
        {
            public Type EntityType { get; set; }
            public string TableName { get; set; }
            public string Schema { get; set; }
            public ColumnMetadata PrimaryKey { get; set; }
            public ColumnMetadata IdentityColumn { get; set; }
            public ColumnMetadata ConcurrencyTokenColumn { get; set; }

            public EntityMetadata Build()
            {
                return new EntityMetadata
                {
                    EntityType = EntityType,
                    TableName = TableName,
                    Schema = Schema,
                    PrimaryKey = PrimaryKey,
                    IdentityColumn = IdentityColumn,
                    ConcurrencyTokenColumn = ConcurrencyTokenColumn,
                };
            }
        }
    }
}
