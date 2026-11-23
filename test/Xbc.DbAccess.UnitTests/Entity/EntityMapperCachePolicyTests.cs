using System;
using System.Data.Common;
using System.Threading;
using Moq;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>映射缓存治理串行集合：策略与缓存均为全局静态，测试须独占运行（不与其他测试类并行）。</summary>
    [CollectionDefinition("EntityMapperCachePolicySerial", DisableParallelization = true)]
    public class EntityMapperCachePolicySerialCollection
    {
    }

    /// <summary>
    /// EntityMapperCachePolicy 治理语义测试：上限淘汰（LRU）、空闲过期、命中续期、默认不治理、淘汰后重建。
    /// 时间类断言留足裕量（毫秒级裕量远大于秒表分辨率），淘汰先后依赖 Thread.Sleep 保证最后访问时间可分序。
    /// </summary>
    [Collection("EntityMapperCachePolicySerial")]
    public class EntityMapperCachePolicyTests
    {
        /// <summary>缓存治理测试实体载体（独立于其他测试的实体，避免缓存键互相干扰）。</summary>
        public class CacheProbeEntity
        {
            public int Id { get; set; }

            public int Age { get; set; }
        }

        private readonly int? _originalMax = XbcDbEntity.EntityMapperCachePolicy.MaxEntries;
        private readonly TimeSpan? _originalIdle = XbcDbEntity.EntityMapperCachePolicy.IdleExpiration;

        private static readonly XbcDbEntity.ProjectionPlan AgeScalarPlan
            = XbcDbEntity.ProjectionPlan.Scalar(new[] { XbcDbEntity.EntityMetadataCache<CacheProbeEntity>.GetColumn(nameof(CacheProbeEntity.Age)) });

        private static Mock<DbDataReader> CreateIntAgeReader(int value)
        {
            var reader = new Mock<DbDataReader>();
            reader.Setup(r => r.FieldCount).Returns(1);
            reader.Setup(r => r.GetName(0)).Returns("Age");
            reader.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
            reader.Setup(r => r.IsDBNull(0)).Returns(false);
            reader.Setup(r => r.GetInt32(0)).Returns(value);
            return reader;
        }

        private static Mock<DbDataReader> CreateLongAgeReader(long value)
        {
            var reader = new Mock<DbDataReader>();
            reader.Setup(r => r.FieldCount).Returns(1);
            reader.Setup(r => r.GetName(0)).Returns("Age");
            reader.Setup(r => r.GetFieldType(0)).Returns(typeof(long));
            reader.Setup(r => r.IsDBNull(0)).Returns(false);
            reader.Setup(r => r.GetInt64(0)).Returns(value);
            return reader;
        }

        private static void SetPolicy(int? max, TimeSpan? idle)
        {
            XbcDbEntity.EntityMapperCachePolicy.MaxEntries = max;
            XbcDbEntity.EntityMapperCachePolicy.IdleExpiration = idle;
        }

        private void RestorePolicy()
        {
            XbcDbEntity.EntityMapperCachePolicy.MaxEntries = _originalMax;
            XbcDbEntity.EntityMapperCachePolicy.IdleExpiration = _originalIdle;
            XbcDbEntity.EntityMapperCore.ClearAllCache();
        }

        [Fact]
        public void CachePolicy_MaxEntries_EvictsOldest_EntryRebuilds()
        {
            try
            {
                XbcDbEntity.EntityMapperCore.ClearAllCache();
                SetPolicy(1, null);

                var toInt = XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<CacheProbeEntity, int>(CreateIntAgeReader(42).Object, AgeScalarPlan);
                Assert.Equal(1, XbcDbEntity.EntityMapperCore.ProjectionMapperCacheEntryCount);

                Thread.Sleep(50);// 保证两形态最后访问时间可分先后（淘汰取最旧）
                var toLong = XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<CacheProbeEntity, long>(CreateLongAgeReader(42L).Object, AgeScalarPlan);

                // 第二次插入触发清扫：最后访问更旧的 int 形态被移出，缓存维持在上限 1 条
                Assert.Equal(1, XbcDbEntity.EntityMapperCore.ProjectionMapperCacheEntryCount);
                Assert.Equal(42L, toLong(CreateLongAgeReader(42L).Object));

                // 重建语义：被淘汰的 int 形态再次访问走 miss→编译，委托可用且取值正确；被淘汰前持有的原委托引用不受影响
                var toIntAgain = XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<CacheProbeEntity, int>(CreateIntAgeReader(42).Object, AgeScalarPlan);
                Assert.Equal(1, XbcDbEntity.EntityMapperCore.ProjectionMapperCacheEntryCount);
                Assert.Equal(42, toIntAgain(CreateIntAgeReader(42).Object));
                Assert.Equal(42, toInt(CreateIntAgeReader(42).Object));
            }
            finally
            {
                RestorePolicy();
            }
        }

        [Fact]
        public void CachePolicy_IdleExpiration_HitRefreshes_IdleEvicted()
        {
            try
            {
                XbcDbEntity.EntityMapperCore.ClearAllCache();
                SetPolicy(null, TimeSpan.FromMilliseconds(150));

                var intReader = CreateIntAgeReader(7);
                var toInt = XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<CacheProbeEntity, int>(intReader.Object, AgeScalarPlan);
                Assert.Equal(1, XbcDbEntity.EntityMapperCore.ProjectionMapperCacheEntryCount);

                Thread.Sleep(100);
                // 命中续期走 GetOrAdd 入口（与真实查询执行同路径；直接调用委托不经过缓存，无续期语义）
                toInt = XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<CacheProbeEntity, int>(intReader.Object, AgeScalarPlan);

                // 距最后命中约 100ms < 150ms：显式清扫不动热条目
                Thread.Sleep(100);
                XbcDbEntity.EntityMapperCachePolicy.Sweep();
                Assert.Equal(1, XbcDbEntity.EntityMapperCore.ProjectionMapperCacheEntryCount);

                // 距最后命中约 350ms ≥ 150ms：显式清扫回收空闲条目
                Thread.Sleep(250);
                XbcDbEntity.EntityMapperCachePolicy.Sweep();
                Assert.Equal(0, XbcDbEntity.EntityMapperCore.ProjectionMapperCacheEntryCount);
            }
            finally
            {
                RestorePolicy();
            }
        }

        [Fact]
        public void CachePolicy_Unset_DefaultKeepsUnboundedBehavior()
        {
            try
            {
                XbcDbEntity.EntityMapperCore.ClearAllCache();
                SetPolicy(null, null);

                XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<CacheProbeEntity, int>(CreateIntAgeReader(1).Object, AgeScalarPlan);
                XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<CacheProbeEntity, long>(CreateLongAgeReader(1L).Object, AgeScalarPlan);

                // 默认（未设置策略）：两形态共存无淘汰，显式清扫为无操作——与治理前的原行为一致
                Assert.Equal(2, XbcDbEntity.EntityMapperCore.ProjectionMapperCacheEntryCount);
                XbcDbEntity.EntityMapperCachePolicy.Sweep();
                Assert.Equal(2, XbcDbEntity.EntityMapperCore.ProjectionMapperCacheEntryCount);
            }
            finally
            {
                RestorePolicy();
            }
        }
    }
}
