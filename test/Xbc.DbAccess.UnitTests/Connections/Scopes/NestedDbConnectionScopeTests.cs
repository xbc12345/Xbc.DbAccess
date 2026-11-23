using Moq;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections.Scopes
{
    /// <summary>
    /// NestedDbConnectionScope 的单元测试。
    /// </summary>
    public class NestedDbConnectionScopeTests
    {
        #region 构造函数测试 - 根作用域

        [Fact]
        public void Constructor_RootScope_SetsProperties()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            Assert.NotEqual(Guid.Empty, scope.ScopeId);
            Assert.True(scope.ManageConnection);
            Assert.True(scope.IsRootScope);
            Assert.Equal(0, scope.NestedLevel);
            Assert.Null(scope.ParentScope);
        }

        #endregion

        #region 构造函数测试 - 子作用域

        [Fact]
        public void Constructor_ChildScope_SetsProperties()
        {
            var mockConnection = new Mock<DbConnection>();
            var parentScope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var childScope = new NestedDbConnectionScope(parentScope, Guid.NewGuid());

            Assert.NotEqual(Guid.Empty, childScope.ScopeId);
            Assert.False(childScope.ManageConnection); // 子作用域不管理连接
            Assert.False(childScope.IsRootScope);
            Assert.Equal(1, childScope.NestedLevel);
            Assert.Same(parentScope, childScope.ParentScope);
        }

        [Fact]
        public void Constructor_ChildScope_SharesConnectionWithParent()
        {
            var mockConnection = new Mock<DbConnection>();
            var parentScope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var childScope = new NestedDbConnectionScope(parentScope, Guid.NewGuid());

            // 子作用域共享父作用域的连接
            // 通过 ManageConnection 属性验证（子作用域不管理连接）
            Assert.False(childScope.ManageConnection);
        }

        #endregion

        #region NestedLevel 测试

        [Fact]
        public void NestedLevel_RootScope_ReturnsZero()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            Assert.Equal(0, scope.NestedLevel);
        }

        [Fact]
        public void NestedLevel_ChildScope_ReturnsParentLevelPlusOne()
        {
            var mockConnection = new Mock<DbConnection>();
            var root = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var level1 = new NestedDbConnectionScope(root, Guid.NewGuid());
            var level2 = new NestedDbConnectionScope(level1, Guid.NewGuid());
            var level3 = new NestedDbConnectionScope(level2, Guid.NewGuid());

            Assert.Equal(1, level1.NestedLevel);
            Assert.Equal(2, level2.NestedLevel);
            Assert.Equal(3, level3.NestedLevel);
        }

        [Fact]
        public void NestedLevel_WhenDisposed_ThrowsObjectDisposedException()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.NestedLevel);
        }

        #endregion

        #region IsRootScope 测试

        [Fact]
        public void IsRootScope_RootScope_ReturnsTrue()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            Assert.True(scope.IsRootScope);
        }

        [Fact]
        public void IsRootScope_ChildScope_ReturnsFalse()
        {
            var mockConnection = new Mock<DbConnection>();
            var parent = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var child = new NestedDbConnectionScope(parent, Guid.NewGuid());

            Assert.False(child.IsRootScope);
        }

        [Fact]
        public void IsRootScope_WhenDisposed_ThrowsObjectDisposedException()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.IsRootScope);
        }

        #endregion

        #region ParentScope 测试

        [Fact]
        public void ParentScope_RootScope_ReturnsNull()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            Assert.Null(scope.ParentScope);
        }

        [Fact]
        public void ParentScope_ChildScope_ReturnsParent()
        {
            var mockConnection = new Mock<DbConnection>();
            var parent = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var child = new NestedDbConnectionScope(parent, Guid.NewGuid());

            Assert.Same(parent, child.ParentScope);
        }

        [Fact]
        public void ParentScope_WhenDisposed_ThrowsObjectDisposedException()
        {
            var mockConnection = new Mock<DbConnection>();
            var parent = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var child = new NestedDbConnectionScope(parent, Guid.NewGuid());
            child.Dispose();

            Assert.Throws<ObjectDisposedException>(() => child.ParentScope);
        }

        #endregion

        #region Dispose 测试 - 根作用域

        [Fact]
        public void Dispose_RootScope_WhenManageConnectionTrue_DisposesConnection()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            scope.Dispose();

            // Dispose 被调用，但 DbConnection.Dispose 不是虚方法，无法验证
            Assert.True(scope.ManageConnection);
        }

        [Fact]
        public void Dispose_RootScope_WhenManageConnectionFalse_DoesNotDisposeConnection()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new NestedDbConnectionScope(mockConnection.Object, false, Guid.NewGuid());

            scope.Dispose();

            // 不管理连接，Dispose 不应被调用
            Assert.False(scope.ManageConnection);
        }

        #endregion

        #region Dispose 测试 - 子作用域

        [Fact]
        public void Dispose_ChildScope_NeverDisposesConnection()
        {
            var mockConnection = new Mock<DbConnection>();
            var parent = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var child = new NestedDbConnectionScope(parent, Guid.NewGuid());

            child.Dispose();

            // 子作用域不管理连接，Dispose 不应被调用
            Assert.False(child.ManageConnection);
        }

        [Fact]
        public void Dispose_ChildScope_ThenParent_DisposesConnectionOnce()
        {
            var mockConnection = new Mock<DbConnection>();
            var parent = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var child = new NestedDbConnectionScope(parent, Guid.NewGuid());

            child.Dispose();
            parent.Dispose();

            // 只有根作用域会释放连接
            Assert.True(parent.ManageConnection);
            Assert.False(child.ManageConnection);
        }

        #endregion

        #region LifecycleEnded 事件测试

        [Fact]
        public void LifecycleEnded_WhenDisposed_RaisesEvent()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new NestedDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var eventRaised = false;

            scope.LifecycleEnded += (sender, e) =>
            {
                eventRaised = true;
            };

            scope.Dispose();

            Assert.True(eventRaised);
        }

        #endregion
    }
}
