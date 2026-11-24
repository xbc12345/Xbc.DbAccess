using Moq;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections.Scopes
{
    /// <summary>
    /// 连接作用域管理器补充覆盖测试：管理器释放期间作用域释放异常的吞并路径。
    /// </summary>
    public class DbConnectionScopeManagerCoverageTests
    {
        [Fact]
        public async Task DefaultManager_Dispose_WhenScopeDisposeThrows_SwallowsExceptionAndClearsScope()
        {
            var mockConnectionManager = new Mock<IDbConnectionManager>();
            var stubConnection = new StubDbConnection();
            _ = mockConnectionManager
                .Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(stubConnection);
            stubConnection.DisposeException = new InvalidOperationException("连接释放失败");

            var manager = new DefaultDbConnectionScopeManager(mockConnectionManager.Object);
            _ = await manager.CreateScopeAsync(Guid.NewGuid());

            // 管理器释放时作用域释放失败：吞并异常仅记录日志，作用域引用仍被清除
            Exception exception = Record.Exception(manager.Dispose);

            Assert.Null(exception);
            Assert.True(manager.IsDisposed);
            _ = Assert.Throws<ObjectDisposedException>(() => manager.CurrentScope);
        }

        [Fact]
        public async Task NestedManager_Dispose_WhenRootScopeDisposeThrows_SwallowsException()
        {
            var mockConnectionManager = new Mock<IDbConnectionManager>();
            var stubConnection = new StubDbConnection();
            _ = mockConnectionManager
                .Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(stubConnection);
            stubConnection.DisposeException = new InvalidOperationException("连接释放失败");

            var manager = new NestedDbConnectionScopeManager(mockConnectionManager.Object);
            _ = await manager.CreateScopeAsync(Guid.NewGuid());

            // 管理器释放时根作用域释放失败：吞并异常仅记录日志
            Exception exception = Record.Exception(manager.Dispose);

            Assert.Null(exception);
            Assert.True(manager.IsDisposed);
            _ = Assert.Throws<ObjectDisposedException>(() => manager.CurrentScope);
        }
    }
}
