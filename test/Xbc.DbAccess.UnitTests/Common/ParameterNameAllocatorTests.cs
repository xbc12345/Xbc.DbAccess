using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using Xbc.DbAccess.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// ParameterNameAllocator 的单元测试：空注册集合与无名参数的跳过分支、参数前缀剥除登记、
    /// Reserve 的空名忽略与防撞登记、Allocate 的唯一名分配。
    /// </summary>
    public class ParameterNameAllocatorTests
    {
        #region 测试替身

        private sealed class StubParameter : DbParameter
        {
            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; }
            public override bool IsNullable { get; set; }
            public override string ParameterName { get; set; }
            public override int Size { get; set; }
            public override string SourceColumn { get; set; }
            public override bool SourceColumnNullMapping { get; set; }
            public override object Value { get; set; }
            public override void ResetDbType()
            {
            }
        }

        #endregion

        [Fact]
        public void Constructor_NullReservedParameters_NoNamesReserved()
        {
            // reservedParameters 为 null：等价于无初始占用，直接返回
            var allocator = new ParameterNameAllocator((IEnumerable<DbParameter>)null, "@");

            Assert.Equal("Age", allocator.Allocate("Age"));
        }

        [Fact]
        public void Constructor_NullReservedNames_NoNamesReserved()
        {
            var allocator = new ParameterNameAllocator((IEnumerable<string>)null);

            Assert.Equal("Age", allocator.Allocate("Age"));
        }

        [Fact]
        public void Constructor_EmptyReservedCollection_NoNamesReserved()
        {
            var allocator = new ParameterNameAllocator(Array.Empty<DbParameter>(), "@");

            Assert.Equal("Age", allocator.Allocate("Age"));
        }

        [Fact]
        public void Constructor_ParameterWithNullName_Skipped()
        {
            // 无名参数（ParameterName 为 null/空白）跳过登记，不影响后续分配
            var parameters = new DbParameter[]
            {
                new StubParameter { ParameterName = null },
                new StubParameter { ParameterName = "" }
            };

            var allocator = new ParameterNameAllocator(parameters, "@");

            Assert.Equal("Age", allocator.Allocate("Age"));
        }

        [Fact]
        public void Constructor_ReservedParameters_StripsPrefixAndPreventsCollision()
        {
            var parameters = new DbParameter[]
            {
                new StubParameter { ParameterName = "@Age", Value = 20 },// 带前缀：剥前缀后按裸名登记
                new StubParameter { ParameterName = "Price", Value = 1 } // 用户直传裸名：原样登记
            };

            var allocator = new ParameterNameAllocator(parameters, "@");

            // 与已登记裸名撞名时自动让位
            Assert.Equal("Age_2", allocator.Allocate("Age"));
            Assert.Equal("Price_2", allocator.Allocate("Price"));
            Assert.Equal("Name", allocator.Allocate("Name"));
        }

        [Fact]
        public void Reserve_NullOrWhitespace_Ignored()
        {
            var allocator = new ParameterNameAllocator();

            allocator.Reserve(null);
            allocator.Reserve("   ");

            // 空名不登记：空基名清理为 "p_" 后仍可直接分配
            Assert.Equal("p_", allocator.Allocate(""));
        }

        [Fact]
        public void Reserve_ValidName_PreventsCollision()
        {
            var allocator = new ParameterNameAllocator();

            allocator.Reserve("Age");

            Assert.Equal("Age_2", allocator.Allocate("Age"));
            Assert.Equal("Age_3", allocator.Allocate("Age"));// 连续分配继续让位
        }

        [Fact]
        public void Allocate_BaseNameSanitized_AndUnique()
        {
            var allocator = new ParameterNameAllocator();

            // 基名统一经 CreateSafeName 清理（幂等）：调用方无需预先清理；空格转义为 Unicode 码点 u0020
            Assert.Equal("Myu0020Col", allocator.Allocate("My Col"));
            Assert.Equal("Myu0020Col_2", allocator.Allocate("My Col"));
        }

        [Fact]
        public void Constructor_CombinedNameConflictingWithColumn_AllowsColumnPriority()
        {
            // 组合名（Age_0）与真实列名冲突时让位，避免参数错位
            var parameters = new DbParameter[] { new StubParameter { ParameterName = "@Age_0" } };
            var allocator = new ParameterNameAllocator(parameters, "@");

            Assert.Equal("Age", allocator.Allocate("Age"));
            Assert.Equal("Age_0_2", allocator.Allocate("Age_0"));
        }
    }
}
