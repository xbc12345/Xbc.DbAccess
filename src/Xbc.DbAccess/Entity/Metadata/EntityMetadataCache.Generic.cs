namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体元数据缓存（泛型版本），通过泛型静态字段提供类型安全的实体元数据访问。
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    internal static class EntityMetadataCache<T> where T : class
    {
        private static readonly Lazy<EntityMetadata> metadataLazy
            = new Lazy<EntityMetadata>(() => EntityMetadataCache.GetMetadata(typeof(T)));

        /// <summary>
        /// 获取实体元数据。
        /// </summary>
        public static EntityMetadata Metadata => metadataLazy.Value;

        /// <summary>
        /// 获取表名。
        /// </summary>
        public static string TableName => Metadata.TableName;

        /// <summary>
        /// 获取架构名。
        /// </summary>
        public static string Schema => Metadata.Schema;

        /// <summary>
        /// 获取所有列信息。
        /// </summary>
        public static IReadOnlyList<ColumnMetadata> Columns => Metadata.Columns;

        /// <summary>
        /// 获取主键列。
        /// </summary>
        public static ColumnMetadata PrimaryKey => Metadata.PrimaryKey;

        /// <summary>
        /// 获取标识列。
        /// </summary>
        public static ColumnMetadata IdentityColumn => Metadata.IdentityColumn;

        /// <summary>
        /// 获取导航属性集合。
        /// </summary>
        public static IReadOnlyList<NavigationMetadata> Navigations => Metadata.Navigations;

        /// <summary>
        /// 获取列元数据。
        /// </summary>
        /// <param name="propertyName">属性名。</param>
        /// <returns>列元数据，如果未找到则返回 null。</returns>
        public static ColumnMetadata GetColumn(string propertyName)
        {
            Guard.ThrowIfNullOrWhiteSpace(propertyName, nameof(propertyName));

            // 经元数据实例的属性名字典查找（构建时一次性建立），替代逐列线性扫描
            return Metadata.GetColumn(propertyName);
        }
    }
}
