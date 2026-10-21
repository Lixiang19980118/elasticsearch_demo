using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;

namespace ElasticSearchDemo;

/// <summary>
/// demo 里反复用到的小工具：控制台分节标题、等待 ES 就绪、彩色打印。
/// </summary>
public static class Demo
{
    private const string Reset = "\u001b[0m";
    private const string Cyan = "\u001b[36m";
    private const string Yellow = "\u001b[33m";
    private const string Green = "\u001b[32m";
    private const string Red = "\u001b[31m";
    private const string Gray = "\u001b[90m";

    private static int _section;

    /// <summary>打印一个分节标题。</summary>
    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"{Cyan}══════════════════════════════════════════════════════════════════{Reset}");
        Console.WriteLine($"{Cyan}  {++_section}. {title}{Reset}");
        Console.WriteLine($"{Cyan}══════════════════════════════════════════════════════════════════{Reset}");
    }

    /// <summary>打印一个小标题（分节内的步骤）。</summary>
    public static void Step(string text) => Console.WriteLine($"\n{Yellow}▶ {text}{Reset}");

    /// <summary>打印一行普通说明。</summary>
    public static void Info(string text) => Console.WriteLine($"  {text}");

    /// <summary>打印一行成功信息。</summary>
    public static void Ok(string text) => Console.WriteLine($"  {Green}✔ {text}{Reset}");

    /// <summary>打印一行警告。</summary>
    public static void Warn(string text) => Console.WriteLine($"  {Red}✘ {text}{Reset}");

    /// <summary>打印请求/响应的原始 JSON（缩进后）。</summary>
    public static void Json(string label, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Console.WriteLine($"  {Gray}{label}: (空){Reset}");
            return;
        }

        var pretty = json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            // 不是 JSON（比如 ES|QL 的 csv/tsv 输出）就原样打印
        }

        Console.WriteLine($"  {Gray}{label}:{Reset}");
        foreach (var line in pretty.Split('\n'))
        {
            Console.WriteLine($"    {Gray}{line.TrimEnd()}{Reset}");
        }
    }

    /// <summary>
    /// 客户端返回的响应体原文。用来说明「8.x 客户端默认不抛异常，要看 IsValidResponse」。
    /// </summary>
    public static string? RawBody(ElasticsearchResponse response) =>
        response.ApiCallDetails?.ResponseBodyInBytes is { Length: > 0 } bytes
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : null;

    /// <summary>检查响应是否成功，失败时打印服务端返回的错误信息。</summary>
    public static bool Check(ElasticsearchResponse response, string what)
    {
        if (response.IsValidResponse)
        {
            Ok($"{what} 成功");
            return true;
        }

        Warn($"{what} 失败：HTTP {(int?)response.ApiCallDetails?.HttpStatusCode} {response.ElasticsearchServerError?.Error?.Reason}");
        return false;
    }

    /// <summary>
    /// 检查「流式」响应（ES|QL 这类直接把结果吐成 Stream 的接口）。
    /// 它继承自 StreamResponseBase，没有 IsValidResponse，所以只能看 HTTP 状态码。
    /// </summary>
    public static bool Check(ElasticsearchStreamResponse response, string what)
    {
        if (response.ApiCallDetails?.HasSuccessfulStatusCode == true)
        {
            Ok($"{what} 成功");
            return true;
        }

        Warn($"{what} 失败：HTTP {(int?)response.ApiCallDetails?.HttpStatusCode}");
        return false;
    }

    /// <summary>
    /// 轮询等待 Elasticsearch 就绪（容器刚起来时可能还在初始化）。
    /// </summary>
    public static async Task<bool> WaitForClusterAsync(ElasticsearchClient client, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var attempt = 0;

        while (DateTime.UtcNow < deadline)
        {
            attempt++;
            try
            {
                var info = await client.InfoAsync();
                if (info.IsValidResponse)
                {
                    Ok($"已连接 {info.Name}，Elasticsearch {info.Version?.Number}（第 {attempt} 次尝试）");
                    return true;
                }
            }
            catch (TransportException ex)
            {
                Console.Write($"\r  等待 Elasticsearch 启动… 第 {attempt} 次（{ex.Message.Split('\n')[0]}）   ");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Console.WriteLine();
        Warn($"等待超时（{timeout.TotalSeconds:0} 秒）。请先执行：docker compose up -d");
        return false;
    }
}
