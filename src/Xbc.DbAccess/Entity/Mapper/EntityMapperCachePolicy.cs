namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体与投影映射委托缓存的全局治理策略（建议启动期设置一次；属性写入无原子性保证，运行期热改仅作启发式生效）。
    /// <para>两个映射缓存（实体映射、投影映射）各自独立按本策略治理：<see cref="IdleExpiration"/> 回收冷条目内存、
    /// <see cref="MaxEntries"/> 封顶条目数（防动态查询形状持续变化导致的内存无界增长）；
    /// 两项均为 null（默认）时保持无界缓存的原行为，命中路径仅多一次时间戳写。</para>
    /// <para>触发时机：<b>插入期惰性触发</b>（miss = 编译新委托的低频时刻）+ 显式 <see cref="Sweep"/>，命中路径永不触发——
    /// 命中即续期（刷新最后访问时间），清理永远不动热条目；被淘汰条目再次访问时走正常 miss→编译路径，
    /// 成本与首次一致且单键单次编译语义不变。上限为软上限：扫描与插入间无原子性，并发插入可使瞬时条目数略超上限。</para>
    /// </summary>
    public static class EntityMapperCachePolicy
    {
        /// <summary>
        /// 缓存最大条目数（两个映射缓存各自独立计算）；null 表示无上限（默认，保持现状）。
        /// 超出时在插入期按最后访问时间移除最旧条目（LRU 语义：刚命中的条目时间戳最新，天然最不容易被淘汰）。
        /// 推荐值：常规应用 1000；动态拼接裸 SQL 且结果形状不可控的场景应显式设置。
        /// </summary>
        public static int? MaxEntries { get; set; }

        /// <summary>
        /// 条目空闲过期时长（自最后访问起算）；null 表示不过期（默认，保持现状）。
        /// 过期条目在插入期扫描或显式 <see cref="Sweep"/> 时移除（惰性清理：无查询流量时冷条目滞留属预期，内存占用以历史峰值为界）。
        /// 推荐值：常规应用 2 小时；配合宿主后台定时 <see cref="Sweep"/> 覆盖「无新查询也能回收」的诉求。
        /// </summary>
        public static TimeSpan? IdleExpiration { get; set; }

        /// <summary>
        /// 按当前策略立即清扫两个映射缓存：先移除空闲超期条目，再按最后访问时间移除最旧条目直至 <see cref="MaxEntries"/> 上限内。
        /// <para>供宿主（如 ASP.NET 的 HostedService）在长驻且无新查询形状产生的场景下定期调用；
        /// 有查询流量时插入期已自动触发清扫，无需手动调用。</para>
        /// </summary>
        public static void Sweep()
        {
            EntityMapperCore.SweepCaches();
        }
    }
}
