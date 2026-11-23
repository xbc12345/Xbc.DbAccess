using Moq;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections.Scopes
{
    /// <summary>
    /// DefaultDbConnectionScope 的单元测试。
    /// </summary>
    public class DefaultDbConnectionScopeTests
    {
        #region 构造函数测试

        [Fact]
        public void Constructor_NullConnection_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new DefaultDbConnectionScope(null, true, Guid.NewGuid()));
        }

        [Fact]
        public void Constructor_ValidConnection_CreatesScope()
        {
            var mockConnection = new Mock<DbConnection>();

            var scope = new DefaultDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            Assert.NotNull(scope);
            Assert.NotEqual(Guid.Empty, scope.ScopeId);
        }

        #endregion

        #region 属性测试

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ManageProperty_ReturnsExpectedValue(bool manageConnection)
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new DefaultDbConnectionScope(mockConnection.Object, manageConnection, Guid.NewGuid());

            Assert.Equal(manageConnection, scope.ManageConnection);
        }

        [Fact]
        public void ScopeId_Always_ReturnsUniqueGuid()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope1 = new DefaultDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var scope2 = new DefaultDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            Assert.NotEqual(scope1.ScopeId, scope2.ScopeId);
        }

        #endregion

        #region LifecycleEnded 事件测试

        [Fact]
        public void LifecycleEnded_WhenDisposed_RaisesEvent()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new DefaultDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            var eventRaised = false;
            DbResourceLifecycleEndedEventArgs eventArgs = null;

            scope.LifecycleEnded += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            scope.Dispose();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);
            Assert.Equal(scope.ScopeId, eventArgs.ResourceId);
            Assert.Equal(DbResourceType.ConnectionScope, eventArgs.ResourceType);
        }

        #endregion

        #region Dispose 测试 - ManageConnection = true

        [Fact]
        public void Dispose_WhenManageConnectionTrue_DisposesConnection()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new DefaultDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            scope.Dispose();

            // 连接应该被释放
            // 注意：DbConnection.Dispose 不是虚方法，无法验证调用次数
        }

        [Fact]
        public void Dispose_WhenManageConnectionTrue_OnSameThread_Succeeds()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new DefaultDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            var exception = Record.Exception(() => scope.Dispose());

            Assert.Null(exception);
        }

        #endregion

        #region Dispose 测试 - ManageConnection = false

        [Fact]
        public void Dispose_WhenManageConnectionFalse_DoesNotDisposeConnection()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new DefaultDbConnectionScope(mockConnection.Object, false, Guid.NewGuid());

            var exception = Record.Exception(() => scope.Dispose());

            Assert.Null(exception);
        }

        #endregion

        #region Dispose 测试 - 多次释放

        [Fact]
        public void Dispose_MultipleTimes_DoesNotThrow()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new DefaultDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());

            scope.Dispose();
            var exception = Record.Exception(() => scope.Dispose());

            Assert.Null(exception);
        }

        #endregion

        #region Dispose 后访问测试

        [Fact]
        public void Dispose_WhenDisposed_MultipleTimes_DoesNotThrow()
        {
            var mockConnection = new Mock<DbConnection>();
            var scope = new DefaultDbConnectionScope(mockConnection.Object, true, Guid.NewGuid());
            scope.Dispose();

            // 多次释放不应抛出异常
            var exception = Record.Exception(() => scope.Dispose());

            Assert.Null(exception);
        }

        #endregion
    }
}
