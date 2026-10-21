using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;

namespace ElasticSearchDemo.Sections;

/// <summary>
/// 4. 批量操作（Bulk API）。
/// 对应参考文档里的「批量操作 / bulk」。
/// </summary>
public static class BulkSection
{
    private const string Index = IndexSection.BooksIndex;

    public static async Task RunAsync(ElasticsearchClient client)
    {
        Demo.Section("批量操作（Bulk）");

        // ---------- 1) 一次请求写入多条文档 ----------
        Demo.Step($"Bulk 批量写入 8 本书（POST /_bulk）");
        Demo.Info("Bulk 把多个 index/update/delete 操作打包成一次 HTTP 请求，比循环单条写入快得多。");
        Demo.Info("客户端用 IndexMany 简化：每个文档生成一行 action + 一行 source。");

        var books = SeedBooks();
        var bulk = await client.BulkAsync(b => b
            .Index(Index)
            .IndexMany(books, (op, book) => op.Id(book.Id))
            .Refresh(Refresh.WaitFor));    // 写完立刻刷新，后面的检索分节才能马上查到

        Demo.Check(bulk, "批量写入");
        Demo.Info($"took = {bulk.Took} ms，errors = {bulk.Errors}，共 {bulk.Items.Count} 个操作结果");

        Demo.Step("逐个操作的结果（ResponseItem）");
        foreach (var item in bulk.Items.Take(4))
        {
            Demo.Info($"  {item.Operation,-6} _id={item.Id,-4} status={item.Status} result={item.Result}");
        }
        Demo.Info($"  …（其余 {Math.Max(0, bulk.Items.Count - 4)} 条同理）");
        Demo.Info("注意 status 是 HTTP 状态码：201=创建成功，200=更新成功，404=文档不存在。");
        Demo.Info("即使个别操作失败，整个 _bulk 请求也返回 200 —— 所以必须逐个检查 Items。");

        if (bulk.Errors)
        {
            Demo.Warn("存在失败的子操作：");
            foreach (ResponseItem item in bulk.ItemsWithErrors)
            {
                Demo.Warn($"  _id={item.Id} {item.Error?.Type}: {item.Error?.Reason}");
            }
        }

        // ---------- 2) 批量中混合多种操作 ----------
        Demo.Step("混合操作：一条请求里同时 index / update / delete");
        var mixed = await client.BulkAsync(b => b
            .Index(Index)
            .Index(new Book
            {
                Id = "900",
                Title = "Bulk Inserted Book",
                Author = "demo",
                Category = "demo",
                Price = 9.9,
                Stock = 1,
                PublishDate = DateTimeOffset.UtcNow,
                InStock = true,
            }, op => op.Id("900"))
            .Update<Book, BookPartial>(u => u
                .Id("900")
                .Doc(new BookPartial { Stock = 42 })
                .DocAsUpsert(true))
            .Delete("999", op => op.Index(Index))
            .Refresh(Refresh.WaitFor));

        Demo.Check(mixed, "混合批量操作");
        foreach (var item in mixed.Items)
        {
            Demo.Info($"  {item.Operation,-6} _id={item.Id,-4} status={item.Status} result={item.Result}");
        }

        // ---------- 3) 统计一下现在索引里有多少文档 ----------
        Demo.Step("确认写入结果（count + 索引统计）");
        // 注意：泛型的 CountAsync<T>() 不接受索引名参数，索引要么写在 Descriptor 里，
        // 要么给该类型配默认索引。这里用 Descriptor 写法。
        var count = await client.CountAsync<Book>(c => c.Indices(Index));
        Demo.Info($"当前 {Index} 索引里共有 {count.Count} 个文档");

        var stats = await client.Indices.StatsAsync(Index, (Metrics)"docs");
        if (stats.Indices?.TryGetValue(Index, out var indexStats) == true)
        {
            Demo.Info($"_stats 里的文档数 = {indexStats.Total?.Docs?.Count}");
        }

        // ---------- 4) BulkAll：大数据量时的分页批量导入 ----------
        Demo.Step("BulkAll：把 IEnumerable 按批自动写入（适合几万条以上的导入）");
        Demo.Info("BulkAll 内部会把数据切成 Size 大小的批，逐批发 _bulk，还带重试和退避。");
        Demo.Info("客户端侧用 Wait(...) 阻塞等待完成，并通过回调拿到每批结果。");

        var many = SeedMoreBooks(30);
        var pages = 0;
        var totalItems = 0L;

        try
        {
            client.BulkAll(many, b => b
                    .Index(Index)
                    .Size(10)              // 每批 10 条
                    .RefreshOnCompleted()  // 全部完成后刷新一次
                    .MaxDegreeOfParallelism(2)
                    .BackOffRetries(3)
                    .BackOffTime(TimeSpan.FromSeconds(1)))
                .Wait(TimeSpan.FromMinutes(2), response =>
                {
                    pages++;
                    totalItems += response.Items.Count;
                    Demo.Info($"  第 {response.Page + 1} 批：写入 {response.Items.Count} 条，重试 {response.Retries} 次");
                });

            Demo.Ok($"BulkAll 完成：共 {pages} 批、{totalItems} 条");
        }
        catch (Exception ex)
        {
            Demo.Warn($"BulkAll 失败：{ex.Message}");
        }

        var countAfter = await client.CountAsync<Book>(c => c.Indices(Index));
        Demo.Info($"BulkAll 之后 {Index} 索引里共有 {countAfter.Count} 个文档");
    }

    /// <summary>准备批量写入的示例数据。</summary>
    private static List<Book> SeedBooks() =>
    [
        new Book
        {
            Id = "2", Title = "Lucene in Action", Author = "Michael McCandless",
            Summary = "The authoritative guide to Apache Lucene, the search library behind Elasticsearch.",
            Category = "search-engine", Tags = ["lucene", "java", "search"],
            Price = 44.0, Stock = 60, PublishDate = new DateTimeOffset(2010, 7, 1, 0, 0, 0, TimeSpan.Zero), InStock = true,
        },
        new Book
        {
            Id = "3", Title = "Elasticsearch 权威指南", Author = "曾勇",
            Summary = "中文 Elasticsearch 入门与实战，覆盖安装、检索、聚合与集群运维。",
            Category = "search-engine", Tags = ["elasticsearch", "chinese", "guide"],
            Price = 79.0, Stock = 25, PublishDate = new DateTimeOffset(2017, 6, 1, 0, 0, 0, TimeSpan.Zero), InStock = true,
        },
        new Book
        {
            Id = "4", Title = "Designing Data-Intensive Applications", Author = "Martin Kleppmann",
            Summary = "A deep dive into the principles behind reliable, scalable and maintainable data systems.",
            Category = "architecture", Tags = ["distributed", "data", "architecture"],
            Price = 59.9, Stock = 200, PublishDate = new DateTimeOffset(2017, 3, 16, 0, 0, 0, TimeSpan.Zero), InStock = true,
        },
        new Book
        {
            Id = "5", Title = "Kafka: The Definitive Guide", Author = "Neha Narkhede",
            Summary = "Real-time data and stream processing at scale with Apache Kafka.",
            Category = "streaming", Tags = ["kafka", "streaming", "distributed"],
            Price = 42.0, Stock = 0, PublishDate = new DateTimeOffset(2017, 9, 1, 0, 0, 0, TimeSpan.Zero), InStock = false,
        },
        new Book
        {
            Id = "6", Title = "Clean Code", Author = "Robert C. Martin",
            Summary = "A handbook of agile software craftsmanship, focused on writing readable code.",
            Category = "engineering", Tags = ["craftsmanship", "java", "best-practice"],
            Price = 35.5, Stock = 300, PublishDate = new DateTimeOffset(2008, 8, 1, 0, 0, 0, TimeSpan.Zero), InStock = true,
        },
        new Book
        {
            Id = "7", Title = "The Pragmatic Programmer", Author = "Andrew Hunt",
            Summary = "Your journey to mastery, from journeyman to master in software development.",
            Category = "engineering", Tags = ["craftsmanship", "career"],
            Price = 38.0, Stock = 150, PublishDate = new DateTimeOffset(1999, 10, 20, 0, 0, 0, TimeSpan.Zero), InStock = true,
        },
        new Book
        {
            Id = "8", Title = "Site Reliability Engineering", Author = "Betsy Beyer",
            Summary = "How Google runs production systems, covering SLOs, monitoring and incident response.",
            Category = "operations", Tags = ["sre", "operations", "google"],
            Price = 55.0, Stock = 40, PublishDate = new DateTimeOffset(2016, 4, 8, 0, 0, 0, TimeSpan.Zero), InStock = true,
        },
        new Book
        {
            Id = "9", Title = "Database Internals", Author = "Alex Petrov",
            Summary = "A deep dive into how distributed data systems work, from storage engines to consensus.",
            Category = "architecture", Tags = ["database", "distributed", "storage"],
            Price = 51.0, Stock = 70, PublishDate = new DateTimeOffset(2019, 10, 1, 0, 0, 0, TimeSpan.Zero), InStock = true,
        },
    ];

    /// <summary>给 BulkAll 演示准备一批数据。</summary>
    private static List<Book> SeedMoreBooks(int count)
    {
        var categories = new[] { "search-engine", "architecture", "engineering", "operations", "streaming" };
        var list = new List<Book>(count);

        for (var i = 0; i < count; i++)
        {
            var n = 100 + i;
            list.Add(new Book
            {
                Id = n.ToString(),
                Title = $"BulkAll Sample Book #{n}",
                Author = $"author-{i % 5}",
                Summary = "Generated by the BulkAll demo to show batched indexing.",
                Category = categories[i % categories.Length],
                Tags = ["bulkall", "generated"],
                Price = 10 + i,
                Stock = i * 3,
                PublishDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i),
                InStock = i % 3 != 0,
            });
        }

        return list;
    }

    /// <summary>批量更新/upsert 用的局部文档。</summary>
    private sealed class BookPartial
    {
        [System.Text.Json.Serialization.JsonPropertyName("stock")]
        public int? Stock { get; set; }
    }
}
