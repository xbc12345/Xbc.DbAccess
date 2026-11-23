using System.Data.Common;

namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 参数名分配器：清理基名后分配唯一参数名，重名时自动追加 _2、_3 数字后缀。
    /// <para>基名统一经 <see cref="ParameterNameSanitizer.CreateSafeName"/> 清理（幂等），调用方无需预先清理。
    /// 同一条 SQL 的全部参数应共享同一分配器实例，组合名（如 <c>Age_0</c>）与真实列名冲突时自动让位，避免参数错位。</para>
    /// </summary>
    internal sealed class ParameterNameAllocator
    {
        private readonly HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 初始化分配器，可选地登记一批已占用参数名，使后续 <see cref="Allocate"/> 分配的名称与其防撞。
        /// </summary>
        /// <param name="reservedNames">已占用的参数名集合（原始名称即可），null 或空集合时等价于无初始占用。</param>
        public ParameterNameAllocator(IEnumerable<string> reservedNames = null)
        {
            if (reservedNames == null)
                return;

            foreach (var name in reservedNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                usedNames.Add(ParameterNameSanitizer.CreateSafeName(name));
            }
        }

        /// <summary>
        /// 初始化分配器，登记一批已有数据库参数占用的名称空间，使后续 <see cref="Allocate"/> 分配的名称与其防撞。
        /// <para>参数名剥去方言参数前缀后按裸名登记（<see cref="Allocate"/> 的分配名同样清洗后比较，
        /// SQL 撞名条件为两者相等）——若直接以全名登记（<c>@Age</c>），分配域中的裸名 <c>Age</c> 与其永不相等，防撞会静默失效。</para>
        /// </summary>
        /// <param name="reservedParameters">已有参数集合（原始参数即可），null 时等价于无初始占用，无名参数跳过。</param>
        /// <param name="parameterPrefix">方言参数前缀（IDbParameterBuilder.ParameterPrefix）。</param>
        public ParameterNameAllocator(IEnumerable<DbParameter> reservedParameters, string parameterPrefix)
        {
            Guard.ThrowIfNullOrWhiteSpace(parameterPrefix, nameof(parameterPrefix));

            if (reservedParameters == null)
                return;

            foreach (var parameter in reservedParameters)
            {
                var name = parameter?.ParameterName;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // 精确剥一次前缀（StartsWith 而非 TrimStart）：参数名可能不带前缀（用户直传裸名），重复前缀字符（如 @@X）不误剥。
                // 裸名按原样登记，不再经 CreateSafeName 清理：Allocate 的比较域是清洗后的分配名，
                // SQL 撞名条件即「分配名 == 裸名原样」；裸名再清洗会在链参数名本身非法（如 1Price → p_1Price）时引入多余的让位
                var bareName = name.StartsWith(parameterPrefix, StringComparison.Ordinal)
                    ? name.Substring(parameterPrefix.Length)
                    : name;

                usedNames.Add(bareName);
            }
        }

        /// <summary>
        /// 分配唯一参数名。
        /// </summary>
        /// <param name="baseName">基名（原始列名或组合名），内部经 <see cref="ParameterNameSanitizer.CreateSafeName"/> 清理。</param>
        /// <returns>基名未被占用时返回清理后的基名，否则返回追加了数字后缀的名称。</returns>
        public string Allocate(string baseName)
        {
            // 清理收口在分配器内：调用方无法遗漏，未来新增调用点不依赖约定保证安全
            baseName = ParameterNameSanitizer.CreateSafeName(baseName);

            if (usedNames.Add(baseName))
                return baseName;

            for (int suffix = 2; ; suffix++)
            {
                var candidate = $"{baseName}_{suffix}";
                if (usedNames.Add(candidate))
                    return candidate;
            }
        }

        /// <summary>
        /// 登记一个占用名称空间的参数名，使之后 <see cref="Allocate"/> 分配的名称与其防撞。
        /// </summary>
        /// <param name="name">已占用的参数名（原始名称即可），内部同样经 <see cref="ParameterNameSanitizer.CreateSafeName"/> 清理。</param>
        public void Reserve(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            usedNames.Add(ParameterNameSanitizer.CreateSafeName(name));
        }
    }
}
