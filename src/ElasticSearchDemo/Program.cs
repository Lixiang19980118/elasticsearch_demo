using Elastic.Clients.Elasticsearch;
using ElasticSearchDemo.Sections;

namespace ElasticSearchDemo;

/// <summary>
/// Elasticsearch 8.x 学习 Demo 的入口。
///
/// 运行前先启动 Elasticsearch（仓库根目录的 docker-compose.yml）：
///     docker compose up -d
/// 然后：
///     dotnet run --project src/ElasticSearchDemo
///
/// 想看具体某个分节，可以传参数，比如：
///     dotnet run --project src/ElasticSearchDemo -- index documents search
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var endpoint = Environment.GetEnvironmentVariable("ES_URL") ?? "http://localhost:9200";
        var runAll = args.Length == 0;

        PrintBanner(endpoint, runAll, args);

        // 连接设置。生产环境请用 https + 证书指纹 + 认证，见 README。
        var settings = new ElasticsearchClientSettings(new Uri(endpoint))
            // 打开调试模式：可以拿到每次请求/响应的原始内容（Demo.RawBody 就靠它）
            .EnableDebugMode(details =>
            {
                // 只打印出错的请求，正常的请求太吵了
                if (!details.HasSuccessfulStatusCode)
                {
                    Console.WriteLine($"  [调试] {details.HttpMethod} {details.Uri} -> {(int?)details.HttpStatusCode}");
                    if (details.OriginalException is { } ex)
                    {
                        Console.WriteLine($"  [调试] 异常: {ex.Message}");
                    }
                }
            })
            .RequestTimeout(TimeSpan.FromSeconds(30));

        var client = new ElasticsearchClient(settings);

        // 等 ES 起来
        if (!await Demo.WaitForClusterAsync(client, TimeSpan.FromSeconds(60)))
        {
            return 1;
        }

        // 按顺序执行各分节
        var sections = new (string Key, string Title, Func<Task> Run)[]
        {
            ("cluster", "集群信息与健康状态", () => ClusterSection.RunAsync(client)),
            ("index", "索引管理", () => IndexSection.RunAsync(client)),
            ("documents", "文档 CRUD", () => DocumentSection.RunAsync(client)),
            ("bulk", "批量操作", () => BulkSection.RunAsync(client)),
            ("search", "检索（Query DSL）", () => SearchSection.RunAsync(client)),
            ("aggregations", "聚合分析", () => AggregationSection.RunAsync(client)),
            ("advanced", "进阶：分词 / 按查询删改 / 别名 / ES|QL", () => AdvancedSection.RunAsync(client)),
            ("bank", "完整示例：bank 账户数据", () => BankSection.RunAsync(client)),
        };

        var toRun = runAll
            ? sections
            : sections.Where(s => args.Contains(s.Key, StringComparer.OrdinalIgnoreCase)).ToArray();

        if (toRun.Length == 0)
        {
            Console.WriteLine($"没有匹配的分节。可用的 key：{string.Join(", ", sections.Select(s => s.Key))}");
            return 1;
        }

        var failed = 0;
        foreach (var (key, title, run) in toRun)
        {
            try
            {
                await run();
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine();
                Demo.Warn($"分节「{title}」({key}) 执行失败：{ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException is { } inner)
                {
                    Demo.Warn($"  内部异常：{inner.Message}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "全部完成。想深入某个主题，可以直接改对应的 Sections/*.cs 反复跑。"
            : $"执行结束，有 {failed} 个分节出错（见上面的 ✘）。");

        return failed == 0 ? 0 : 1;
    }

    private static void PrintBanner(string endpoint, bool runAll, string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        Elasticsearch 8.x + .NET 官方客户端 学习 Demo             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"  客户端包 : Elastic.Clients.Elasticsearch 8.19.27（8.x 系列，替代老的 NEST）");
        Console.WriteLine($"  连接地址 : {endpoint}");
        Console.WriteLine($"  运行模式 : {(runAll ? "全部分节" : string.Join(", ", args))}");
    }
}
