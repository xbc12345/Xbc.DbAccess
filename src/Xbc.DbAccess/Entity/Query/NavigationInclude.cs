namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 导航属性包含项，记录一个导航属性及其在导航链中的父级信息。
    /// </summary>
    internal sealed class NavigationInclude
    {
        private string path;

        /// <summary>
        /// 本节点指向的导航属性元数据。
        /// </summary>
        public NavigationMetadata NavigationMetadata { get; }

        /// <summary>
        /// 父级导航属性包含项。根级导航属性为 null。
        /// </summary>
        public NavigationInclude Parent { get; }

        /// <summary>
        /// 初始化导航属性包含项。
        /// </summary>
        /// <param name="navigationMetadata">导航属性元数据。</param>
        /// <param name="parent">父级导航属性包含项，根级导航传 null。</param>
        public NavigationInclude(NavigationMetadata navigationMetadata, NavigationInclude parent)
        {
            NavigationMetadata = navigationMetadata ?? throw new ArgumentNullException(nameof(navigationMetadata));
            Parent = parent;
        }

        /// <summary>
        /// 获取从根到当前节点的完整导航路径字符串（如 "Orders.OrderDetails"），用于去重判断。
        /// </summary>
        public string Path
        {
            get
            {
                if (path == null)
                {
                    // Parent 为 null 时是根导航，路径即属性名；否则拼接父级路径
                    path = Parent != null
                        ? Parent.Path + "." + NavigationMetadata.PropertyName
                        : NavigationMetadata.PropertyName;
                }

                return path;
            }
        }
    }
}
