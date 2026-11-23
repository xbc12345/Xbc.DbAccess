using System.Diagnostics;

namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// DbAccess ActivitySource，发布 SQL 执行 span（<see cref="ActivityName"/>，ActivityKind.Client），承载指标无法承载的 SQL 文本（慢 SQL 定位）。
    /// <para>tag 遵循 OTel 数据库语义约定：db.system（提供程序名）、db.operation（操作名）、db.statement（SQL 文本）。
    /// SQL 文本只进 span 不进指标（高基数限制），且不含参数值（框架全参数化执行）。</para>
    /// <para>未接入 ActivityListener 时 <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> 返回 null，调用方判空，零开销。</para>
    /// </summary>
    public static class DbAccessActivitySource
    {
        /// <summary>
        /// ActivitySource 名称，与 Meter 名称一致。
        /// </summary>
        public const string Name = "Xbc.DbAccess";

        /// <summary>
        /// SQL 执行 span 名称。
        /// </summary>
        public const string ActivityName = "Xbc.DbAccess.Execute";

        private static readonly ActivitySource activitySource = new ActivitySource(Name);

        /// <summary>
        /// 开始一次 SQL 执行 span（ActivityKind.Client）。
        /// </summary>
        /// <param name="provider">提供程序名称，如 SqlClient、MySqlClient。</param>
        /// <param name="operation">操作名称，如 ExecuteNonQuery。</param>
        /// <param name="sql">SQL 命令文本（参数化占位符形式，不含参数值）。</param>
        /// <returns>已开始的 Activity；未接入 listener 时返回 null。</returns>
        internal static Activity StartExecution(string provider, string operation, string sql)
        {
            var activity = activitySource.StartActivity(ActivityName, ActivityKind.Client);
            if (activity != null)
            {
                activity.SetTag("db.system", provider);
                activity.SetTag("db.operation", operation);
                activity.SetTag("db.statement", sql);
            }

            return activity;
        }

        /// <summary>
        /// 在失败的 SQL 执行 span 上记录 OTel 约定的 exception 事件（type / message / stacktrace，堆栈为空时省略）与 error.type tag。
        /// <para>三 TFM 的 ActivityEvent 均提供 (name, timestamp, ActivityTagsCollection) 构造，统一走该形式。</para>
        /// </summary>
        /// <param name="activity">SQL 执行 span。</param>
        /// <param name="exception">执行抛出的异常。</param>
        internal static void RecordException(Activity activity, Exception exception)
        {
            var exceptionType = exception.GetType().FullName;// OTel 语义约定：exception.type 为异常类型的完整名称（fully qualified name）
            var tags = new ActivityTagsCollection
            {
                { "exception.type", exceptionType },
                { "exception.message", exception.Message }
            };

            if (!string.IsNullOrEmpty(exception.StackTrace))
                tags.Add("exception.stacktrace", exception.StackTrace);

            activity.AddEvent(new ActivityEvent("exception", tags: tags));
            activity.AddTag("error.type", exceptionType);
        }
    }
}
