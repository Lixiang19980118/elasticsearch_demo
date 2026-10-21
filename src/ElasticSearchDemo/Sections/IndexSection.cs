using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;

namespace ElasticSearchDemo.Sections;

/// <summary>
/// 2. 索引管理：创建（含 mappings / settings / 自定义分词器）、查看、判断存在、更新设置、刷新、统计、删除。
/// 对应参考文档里的「索引操作 / 创建索引 / 修改索引」。
/// </summary>
public static class IndexSection
{
    /// <summary>本分节演示用的索引名。</summary>
    public const string BooksIndex = "books";

    public static async Task RunAsync(ElasticsearchClient client)
    {
        Demo.Section("索引管理（创建 / 查看 / 设置 / 统计 / 删除）");

        // ---------- 1) 删除可能残留的同名索引，保证可重复运行 ----------
        Demo.Step($"如果 {BooksIndex} 已存在先删除（DELETE /{BooksIndex}）");
        var existsBefore = await client.Indices.ExistsAsync(BooksIndex);
        if (existsBefore.Exists)
        {
            await client.Indices.DeleteAsync(BooksIndex);
            Demo.Ok($"已删除旧的 {BooksIndex}");
        }
        else
        {
            Demo.Ok($"{BooksIndex} 不存在，无需删除");
        }

        // ---------- 2) 创建索引：settings + analysis + mappings ----------
        Demo.Step($"创建索引 {BooksIndex}（PUT /{BooksIndex}，带 settings + mappings + 自定义分词器）");
        Demo.Info("对应旧教程里的 PUT /lib 加 mappings；8.x 客户端用 Indices.CreateAsync 的 Descriptor。");

        var create = await client.Indices.CreateAsync<Book>(BooksIndex, c => c
            .Settings(s => s
                .NumberOfShards(1)          // demo 用单分片，方便观察分片行为
                .NumberOfReplicas(0)        // 单节点集群，副本给 0 才能是 Green
                .RefreshInterval("1s")      // 默认 1s 自动刷新；下面还会手动 refresh
                .Analysis(a => a
                    // 自定义分词器：standard 切词 + 小写 + 去音标，方便英文全文检索
                    .Analyzers(an => an
                        .Custom("book_analyzer", ca => ca
                            .Tokenizer("standard")
                            .Filter("lowercase", "asciifolding"))
                        // 中文分词：standard 把汉字切成单字，加上 cjk_bigram 后相邻两字组成词
                        // 生产环境更推荐装 analysis-ik 插件（词典分词，效果更好）
                        .Custom("chinese_analyzer", ca => ca
                            .Tokenizer("standard")
                            .Filter("cjk_width", "lowercase", "cjk_bigram")))
                    // 给 keyword 字段用的 normalizer：只做小写，用于精确匹配时忽略大小写
                    .Normalizers(n => n
                        .Lowercase("lowercase_normalizer"))))
            .Mappings(m => m
                .Properties(p => p
                    .Keyword(b => b.Id)
                    .Text(b => b.Title, t => t
                        .Analyzer("book_analyzer")
                        // 同时建一个 keyword 子字段，既全文检索也能精确聚合/排序
                        .Fields(f => f.Keyword("keyword")))
                    .Keyword(b => b.Author, k => k.Normalizer("lowercase_normalizer"))
                    .Text(b => b.Summary, t => t.Analyzer("book_analyzer"))
                    .Keyword(b => b.Category)
                    .Keyword(b => b.Tags)
                    .DoubleNumber(b => b.Price)
                    .IntegerNumber(b => b.Stock)
                    .Date(b => b.PublishDate)
                    .Boolean(b => b.InStock))));

        Demo.Check(create, $"创建索引 {BooksIndex}");
        Demo.Info($"acknowledged = {create.Acknowledged}，shards_acknowledged = {create.ShardsAcknowledged}");

        // ---------- 3) 查看映射：GET /books/_mapping ----------
        Demo.Step($"查看映射（GET /{BooksIndex}/_mapping）");
        // 泛型的 GetMappingAsync<T>() 同样不接受索引名参数，索引写在 Descriptor 里。
        var mapping = await client.Indices.GetMappingAsync<Book>(m => m.Indices(BooksIndex));
        Demo.Check(mapping, "获取映射");
        var properties = mapping.Mappings[BooksIndex].Mappings.Properties;
        Demo.Info($"映射里一共有 {properties.Count()} 个字段：");
        foreach (var (name, prop) in properties)
        {
            // 字典的 key 是 PropertyName 类型，取 .Name 才是字段名字符串
            Demo.Info($"  - {name.Name,-13} {prop.GetType().Name.Replace("Property", "").ToLowerInvariant()}");
        }

        // 顺便打印一份原始 JSON，方便和 Kibana 里看到的对照
        Demo.Json("映射原文", Demo.RawBody(mapping));

        // ---------- 4) 查看索引设置：GET /books/_settings ----------
        Demo.Step($"查看索引设置（GET /{BooksIndex}，client.Indices.GetAsync）");
        var indexInfo = await client.Indices.GetAsync(BooksIndex);
        Demo.Check(indexInfo, "获取索引信息");
        if (indexInfo.Indices.TryGetValue(BooksIndex, out var state))
        {
            Demo.Info($"分片数       : {state.Settings?.NumberOfShards}");
            Demo.Info($"副本数       : {state.Settings?.NumberOfReplicas}");
            Demo.Info($"刷新间隔     : {state.Settings?.RefreshInterval}");
            Demo.Info($"索引 UUID    : {state.Settings?.Uuid}");
        }

        Demo.Info("注意：客户端把 settings 反序列化成了强类型对象，像 analysis 这种嵌套结构");
        Demo.Info("      不一定能完整映射（这里 Analysis 就是 null）。要看原文就直接读响应体：");
        Demo.Json("settings 原文", Demo.RawBody(indexInfo));

        // ---------- 5) 判断索引是否存在 ----------
        Demo.Step("判断索引是否存在（HEAD /books）");
        var exists = await client.Indices.ExistsAsync(BooksIndex);
        Demo.Info($"{BooksIndex} 存在吗？ {exists.Exists}  ← 8.x 里 HEAD 请求成功即 200，客户端把它映射成 Exists 属性");

        var existsFake = await client.Indices.ExistsAsync("not_exist_index");
        Demo.Info($"not_exist_index 存在吗？ {existsFake.Exists}  ← 返回 404，但这是「正常结果」，IsValidResponse 依然为 true");

        // ---------- 6) 修改索引设置：PUT /books/_settings ----------
        Demo.Step($"修改索引设置（PUT /{BooksIndex}/_settings，把 refresh_interval 改成 5s）");
        var putSettings = await client.Indices.PutSettingsAsync(BooksIndex, s => s
            .Settings(si => si.RefreshInterval("5s")));
        Demo.Check(putSettings, "修改索引设置");

        // ---------- 7) 关闭 / 打开索引（可选演示） ----------
        Demo.Step($"关闭再打开索引（POST /{BooksIndex}/_close → /_open）");
        var close = await client.Indices.CloseAsync(BooksIndex);
        Demo.Check(close, "关闭索引");
        Demo.Info("索引关闭后不能读写，但元数据（映射、设置）保留，可用于改分片数等操作。");

        var open = await client.Indices.OpenAsync(BooksIndex);
        Demo.Check(open, "打开索引");

        // ---------- 8) 刷新：让刚写入的数据立即可被搜索到 ----------
        Demo.Step($"刷新索引（POST /{BooksIndex}/_refresh）");
        Demo.Info("ES 是近实时的：默认写入后要等 1 秒才会出现在搜索结果里。");
        Demo.Info("调用 _refresh 可以强制立刻刷新，demo 里为了结果确定都会刷新一下。");
        var refresh = await client.Indices.RefreshAsync(BooksIndex);
        Demo.Check(refresh, "刷新索引");

        // ---------- 9) 索引统计：GET /books/_stats ----------
        Demo.Step($"索引统计（GET /{BooksIndex}/_stats，client.Indices.StatsAsync）");
        Metrics statsMetrics = "docs,store";
        var stats = await client.Indices.StatsAsync(BooksIndex, statsMetrics);
        Demo.Check(stats, "获取索引统计");

        if (stats.Indices?.TryGetValue(BooksIndex, out var indexStats) == true)
        {
            Demo.Info($"文档数     : {indexStats.Total?.Docs?.Count}");
            Demo.Info($"已删除文档 : {indexStats.Total?.Docs?.Deleted}");
            Demo.Info($"占用空间   : {indexStats.Total?.Store?.SizeInBytes} 字节");
        }
        else
        {
            Demo.Info("索引当前还没有文档（下面的文档分节会写入）。");
        }
    }
}
