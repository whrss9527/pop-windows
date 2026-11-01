using System.Net;
using System.Net.Http;
using System.Text;
using Pop.Core;

namespace Pop;

/// 在真正的结果卡片上控制异步完成顺序；既检查取消传递，也检查不服从取消的旧服务。
internal static class DeferredCardChecks
{
    public static async Task VerifyAsync(App app, int x, int y, Action<string, ResultCard> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("异步结果检查仅用于演示回归");
        var card = new ResultCard();
        var closed = false;
        var opened = false; var stateChanges = 0;
        using var presenter = new DeferredCard(card, value => { opened = value; stateChanges++; });
        try
        {
            var old = Start("旧翻译");
            presenter.Show(x, y, CardContent.Text("当前结果", "保留当前结果，旧翻译不会覆盖这里。"));
            if (!old.Token.IsCancellationRequested) throw new InvalidOperationException("新结果未取消旧翻译");
            old.Reply.SetResult(CardContent.Text("过期结果", "这段文字不应出现"));
            await old.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertPrimary("保留当前结果，旧翻译不会覆盖这里。");
            await Task.Delay(200); shot("deferred-new-result", card);

            var first = Start("第一次翻译");
            var second = Start("第二次翻译");
            second.Reply.SetResult(CardContent.Text("最新翻译", "只显示最近一次请求的结果。"));
            await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            first.Reply.SetResult(CardContent.Text("第一次的迟到翻译", "过期内容"));
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!first.Token.IsCancellationRequested) throw new InvalidOperationException("新请求未取消前一次请求");
            AssertPrimary("只显示最近一次请求的结果。");
            await Task.Delay(200); shot("deferred-latest-result", card);

            var closing = Start("关闭前的翻译");
            card.Dismiss(); // 用户在卡片上关闭也必须失效，不只覆盖协调器的关闭入口
            closing.Reply.SetException(new InvalidOperationException("迟到的服务错误"));
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(180);
            if (!closing.Token.IsCancellationRequested || opened || card.IsOpen)
                throw new InvalidOperationException("关闭后旧任务重新打开结果卡片");

            var copy = Start("复制失败前的翻译");
            presenter.CopyFailure(x, y, "保留复制结果", "未复制的完整内容", null);
            copy.Reply.SetResult(CardContent.Text("迟到翻译", "过期内容"));
            await copy.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!card.CopyFailed) throw new InvalidOperationException("旧任务清除了复制重试状态");
            AssertPrimary("未复制的完整内容");

            var toast = Start("提示前的翻译");
            presenter.Toast(x, y, "已完成当前操作");
            toast.Reply.SetResult(CardContent.Text("迟到翻译", "过期内容"));
            await toast.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (opened || card.PrimaryText is not null) throw new InvalidOperationException("旧任务把轻提示改回了可复制结果");

            // 用真实 Translator 和 HttpClient 验证取消到达传输层，不访问外网或真实密钥。
            using var handler = new WaitingHandler();
            using var http = new HttpClient(handler);
            var translator = new Translator(http);
            var translating = presenter.RunAsync(x, y, TranslationCards.Loading("source", "zh-Hans"), async token =>
            {
                var translated = await translator.TranslateAsync("source", "zh-Hans", TranslationEngine.Azure, "ci-placeholder", ct: token);
                return TranslationCards.Result("source", translated);
            });
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            presenter.Show(x, y, CardContent.Text("新的操作", "取消等待后仍保留新结果"));
            await translating.WaitAsync(TimeSpan.FromSeconds(5));
            if (!handler.Cancelled) throw new InvalidOperationException("翻译取消没有传递到 HTTP 请求");
            AssertPrimary("取消等待后仍保留新结果");

            // OCR 等不能中断的服务也要丢弃迟到结果；开始时可以没有加载卡片。
            var recognizing = Start("文字识别", loading: false);
            presenter.Dismiss();
            presenter.Show(x, y, CardContent.Text("之后的操作", "识别结果不能覆盖后续操作"));
            recognizing.Reply.SetResult(CardContent.Text("识别文字", "迟到的识别内容"));
            await recognizing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertPrimary("识别结果不能覆盖后续操作");

            var currentError = false;
            try { await presenter.RunAsync(x, y, null, _ => Task.FromException<CardContent>(new InvalidOperationException("当前服务错误"))); }
            catch (InvalidOperationException e) when (e.Message == "当前服务错误") { currentError = true; }
            if (!currentError) throw new InvalidOperationException("当前请求的错误被当成过期任务吞掉");

            var exiting = Start("退出前的翻译");
            presenter.Dispose(); card.Close(); closed = true; var changesAtClose = stateChanges;
            exiting.Reply.SetResult(CardContent.Text("退出后的旧结果", "不可回写"));
            await exiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!exiting.Token.IsCancellationRequested || opened || stateChanges != changesAtClose)
                throw new InvalidOperationException("退出后仍提交异步结果");

            (Task Task, TaskCompletionSource<CardContent> Reply, CancellationToken Token) Start(string title, bool loading = true)
            {
                var reply = new TaskCompletionSource<CardContent>(TaskCreationOptions.RunContinuationsAsynchronously);
                var token = CancellationToken.None;
                var task = presenter.RunAsync(x, y, loading ? new CardContent(title, [], Body: "正在等待…", Loading: true) : null,
                    cancellation => { token = cancellation; return reply.Task; });
                if (!token.CanBeCanceled) throw new InvalidOperationException("服务没有收到取消令牌");
                return (task, reply, token);
            }
            void AssertPrimary(string expected)
            {
                if (!opened || card.PrimaryText != expected) throw new InvalidOperationException("当前卡片被过期结果覆盖");
            }
        }
        finally { if (!closed) card.Close(); }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Cancelled = true; throw; }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        }
    }
}
